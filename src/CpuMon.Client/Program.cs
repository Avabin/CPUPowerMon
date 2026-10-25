using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using CpuMon.Client.DBus;
using Tmds.DBus.Protocol;

namespace CpuMon.Client;

internal static class Program
{
    private const string Version = "0.1.0";
    private const string DefaultService = "org.cpupowermon.CpuMon";
    private const string ObjectPath = "/org/cpupowermon/CpuMon";
    private const int DefaultIntervalMs = 1000;
    private const int MinimumIntervalMs = 100;
    private const int MaximumIntervalMs = 60000;

    private static async Task<int> Main(string[] args)
    {
        if (!Options.TryParse(args, out Options options, out string? parseError))
        {
            Console.Error.WriteLine($"cpumonctl: {parseError}");
            Console.Error.WriteLine("Try 'cpumonctl --help' for usage.");
            return 2;
        }

        if (options.Help)
        {
            Console.Out.Write(Options.HelpText);
            return 0;
        }

        if (options.Version)
        {
            Console.Out.WriteLine($"cpumonctl {Version}");
            return 0;
        }

        string? address = options.Bus == "session" ? DBusAddress.Session : DBusAddress.System;
        if (string.IsNullOrEmpty(address))
        {
            Console.Error.WriteLine($"cpumonctl: no {options.Bus} bus address available");
            return 1;
        }

        try
        {
            return options.Watch
                ? await WatchAsync(address, options)
                : await QueryOnceAsync(address, options);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"cpumonctl: {Describe(ex)}");
            return 1;
        }
    }

    private static async Task<int> QueryOnceAsync(string address, Options options)
    {
        using var connection = new DBusConnection(address);
        await connection.ConnectAsync();
        CpuMon1 proxy = new DBusService(connection, options.Service).CreateCpuMon1(ObjectPath);

        var (energyUj, watts, timestampUs, sampleAvailable) = await proxy.GetSampleAsync();
        CpuMon1Properties props = await proxy.GetPropertiesAsync();

        bool available = sampleAvailable && props.Available;
        string error = ErrorText(props.LastError, available);

        if (options.Json)
        {
            Console.Out.WriteLine(Serialize(energyUj, watts, timestampUs, available, error, props.PackageName, props.SampleIntervalMs));
            Console.Out.Flush();
        }
        else if (available)
        {
            Console.Out.WriteLine(watts.ToString("F2", CultureInfo.InvariantCulture));
            Console.Out.Flush();
        }
        else
        {
            Console.Error.WriteLine($"cpumonctl: unavailable: {error}");
        }

        return available ? 0 : 1;
    }

    private static async Task<int> WatchAsync(string address, Options options)
    {
        using var cancellation = new CancellationTokenSource();
        using PosixSignalRegistration sigint = PosixSignalRegistration.Create(PosixSignal.SIGINT, context =>
        {
            context.Cancel = true;
            cancellation.Cancel();
        });
        using PosixSignalRegistration sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
        {
            context.Cancel = true;
            cancellation.Cancel();
        });

        CancellationToken token = cancellation.Token;
        DBusConnection? connection = null;
        CpuMon1? proxy = null;
        int failures = 0;
        string? lastUnavailable = null;
        long lastUnavailableLog = 0;

        try
        {
            while (!token.IsCancellationRequested)
            {
                long started = Stopwatch.GetTimestamp();
                try
                {
                    if (connection is null)
                    {
                        connection = new DBusConnection(address);
                        await connection.ConnectAsync();
                        proxy = new DBusService(connection, options.Service).CreateCpuMon1(ObjectPath);
                    }

                    var (energyUj, watts, timestampUs, sampleAvailable) = await proxy!.GetSampleAsync();
                    CpuMon1Properties props = await proxy.GetPropertiesAsync();

                    failures = 0;
                    bool available = sampleAvailable && props.Available;
                    string error = ErrorText(props.LastError, available);

                    if (available)
                    {
                        if (options.Json)
                        {
                            Console.Out.WriteLine(Serialize(energyUj, watts, timestampUs, true, error, props.PackageName, props.SampleIntervalMs));
                        }
                        else
                        {
                            Console.Out.WriteLine(watts.ToString("F2", CultureInfo.InvariantCulture));
                        }

                        Console.Out.Flush();
                        lastUnavailable = null;
                    }
                    else
                    {
                        if (options.Json)
                        {
                            Console.Out.WriteLine(Serialize(energyUj, watts, timestampUs, false, error, props.PackageName, props.SampleIntervalMs));
                            Console.Out.Flush();
                        }
                        else
                        {
                            long now = Stopwatch.GetTimestamp();
                            if (error != lastUnavailable || ElapsedSeconds(lastUnavailableLog, now) >= 30)
                            {
                                Console.Error.WriteLine($"cpumonctl: unavailable: {error}");
                                lastUnavailable = error;
                                lastUnavailableLog = now;
                            }
                        }
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    failures++;
                    int backoffMs = Math.Min(5000, 250 << Math.Min(failures - 1, 5));
                    Console.Error.WriteLine($"cpumonctl: {Describe(ex)}; retrying in {backoffMs} ms");
                    proxy = null;
                    connection?.Dispose();
                    connection = null;
                    await Task.Delay(backoffMs, token);
                    continue;
                }

                long elapsedMs = ElapsedMilliseconds(started);
                long remainingMs = options.IntervalMs - elapsedMs;
                if (remainingMs > 0)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(remainingMs), token);
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        finally
        {
            connection?.Dispose();
        }

        return 0;
    }

    private static string Serialize(
        ulong energyUj,
        double watts,
        ulong timestampUs,
        bool available,
        string error,
        string package,
        uint intervalMs)
        => JsonSerializer.Serialize(new
        {
            watts,
            energy_uj = energyUj,
            timestamp_us = timestampUs,
            available,
            error,
            package,
            interval_ms = intervalMs,
        });

    private static string ErrorText(string lastError, bool available)
    {
        if (!string.IsNullOrWhiteSpace(lastError))
        {
            return lastError;
        }

        return available ? string.Empty : "device unavailable";
    }

    private static string Describe(Exception ex)
        => ex switch
        {
            DBusErrorReplyException error => $"{error.ErrorName}: {error.ErrorMessage}",
            DBusConnectionClosedException => "D-Bus connection closed",
            _ => ex.Message,
        };

    private static long ElapsedMilliseconds(long started)
        => (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;

    private static double ElapsedSeconds(long started, long now)
        => Stopwatch.GetElapsedTime(started, now).TotalSeconds;

    private sealed class Options
    {
        public bool Help { get; private set; }
        public bool Version { get; private set; }
        public bool Json { get; private set; }
        public bool Watch { get; private set; }
        public string Bus { get; private set; } = "system";
        public string Service { get; private set; } = DefaultService;
        public int IntervalMs { get; private set; } = DefaultIntervalMs;

        public static readonly string HelpText =
            """
            Usage: cpumonctl [options]

            Read CPU package power from org.cpupowermon.CpuMon1 (system bus by
            default). Output is written to stdout; diagnostics go to stderr.

            Options:
              --json                Print one JSON object (or one per line with --watch)
              --watch               Keep polling and print one value per line
              --interval-ms N       Polling interval for --watch (default 1000)
              --bus system|session  Bus to use (default system)
              --service NAME        Well-known D-Bus name
              --version             Print version and exit
              -h, --help            Show this help

            Exit codes: 0 success, 1 D-Bus/query failure, 2 usage error.
            """;

        public static bool TryParse(string[] args, out Options options, out string? error)
        {
            options = new Options();
            error = null;

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                switch (arg)
                {
                    case "-h":
                    case "--help":
                        options.Help = true;
                        break;
                    case "--version":
                        options.Version = true;
                        break;
                    case "--json":
                        options.Json = true;
                        break;
                    case "--watch":
                        options.Watch = true;
                        break;
                    case "--bus":
                        if (!TryTakeValue(args, ref i, out string? bus, out error))
                        {
                            return false;
                        }

                        if (bus is not ("system" or "session"))
                        {
                            error = $"--bus must be 'system' or 'session', got '{bus}'";
                            return false;
                        }

                        options.Bus = bus;
                        break;
                    case "--service":
                        if (!TryTakeValue(args, ref i, out string? service, out error))
                        {
                            return false;
                        }

                        if (string.IsNullOrWhiteSpace(service))
                        {
                            error = "--service requires a non-empty name";
                            return false;
                        }

                        options.Service = service;
                        break;
                    case "--interval-ms":
                        if (!TryTakeValue(args, ref i, out string? intervalText, out error))
                        {
                            return false;
                        }

                        if (!int.TryParse(intervalText, NumberStyles.None, CultureInfo.InvariantCulture, out int interval)
                            || interval < MinimumIntervalMs
                            || interval > MaximumIntervalMs)
                        {
                            error = $"--interval-ms must be an integer between {MinimumIntervalMs} and {MaximumIntervalMs}";
                            return false;
                        }

                        options.IntervalMs = interval;
                        break;
                    default:
                        error = arg.StartsWith('-')
                            ? $"unknown option '{arg}'"
                            : $"unexpected argument '{arg}'";
                        return false;
                }
            }

            if (options.Help || options.Version)
            {
                return true;
            }

            return true;
        }

        private static bool TryTakeValue(string[] args, ref int index, out string? value, out string? error)
        {
            if (index + 1 >= args.Length)
            {
                value = null;
                error = $"option '{args[index]}' requires a value";
                return false;
            }

            value = args[++index];
            error = null;
            return true;
        }
    }
}
