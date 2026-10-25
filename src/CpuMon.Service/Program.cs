using System.Runtime.InteropServices;
using CpuMon.Core;

namespace CpuMon.Service;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (!CommandLineOptions.TryParse(args, out CommandLineOptions options, out string error))
        {
            Console.Error.WriteLine($"cpumond: {error}");
            Console.Error.WriteLine("Try 'cpumond --help'.");
            return 2;
        }

        if (options.ShowHelp)
        {
            PrintUsage(Console.Out);
            return 0;
        }

        if (options.ShowVersion)
        {
            Console.WriteLine(ServiceInfo.Version);
            return 0;
        }

        Log(string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "starting {0}: bus={1} name={2} device={3} interval={4}ms",
            ServiceInfo.Version,
            options.Bus.ToString().ToLowerInvariant(),
            options.BusName,
            options.DevicePath,
            options.IntervalMs));

        using var shutdown = new CancellationTokenSource();
        using var sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
        {
            context.Cancel = true;
            shutdown.Cancel();
        });
        using var sigint = PosixSignalRegistration.Create(PosixSignal.SIGINT, context =>
        {
            context.Cancel = true;
            shutdown.Cancel();
        });

        var sampler = new PowerSampler(
            new RaplFileReader(options.DevicePath),
            StopwatchMonotonicClock.Instance,
            options.IntervalMs);

        var runtime = new ServiceRuntime(sampler, options);

        try
        {
            Task samplerTask = runtime.RunSamplerAsync(shutdown.Token);
            Task busTask = runtime.RunBusLoopAsync(shutdown.Token);

            await Task.WhenAny(samplerTask, busTask).ConfigureAwait(false);

            shutdown.Cancel();
            await Task.WhenAll(samplerTask, busTask).ConfigureAwait(false);

            Log("stopped.");
            return 0;
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
            Log("stopped.");
            return 0;
        }
        catch (Exception ex)
        {
            Log($"fatal: {ex}");
            return 1;
        }
    }

    private static void PrintUsage(TextWriter writer)
    {
        writer.WriteLine("Usage: cpumond [options]");
        writer.WriteLine();
        writer.WriteLine("Reads Intel RAPL package energy and exports it on D-Bus.");
        writer.WriteLine();
        writer.WriteLine("Options:");
        writer.WriteLine("  --bus system|session   Bus to use (default: system).");
        writer.WriteLine("  --device PATH          RAPL package directory");
        writer.WriteLine("                         (default: /sys/class/powercap/intel-rapl:0).");
        writer.WriteLine("  --interval-ms N        Sample interval, clamped to 100..60000 ms");
        writer.WriteLine("                         (default: 1000).");
        writer.WriteLine("  --name NAME            Well-known bus name");
        writer.WriteLine("                         (default: org.cpupowermon.CpuMon).");
        writer.WriteLine("  --verbose              Log every sample to stderr.");
        writer.WriteLine("  --version              Print the service version and exit.");
        writer.WriteLine("  --help, -h             Show this help.");
    }

    private static void Log(string message)
    {
        Console.Error.WriteLine($"cpumond: {message}");
    }
}
