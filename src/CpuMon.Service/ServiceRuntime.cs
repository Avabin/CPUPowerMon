using System.Globalization;
using CpuMon.Core;
using CpuMon.DBus;
using Tmds.DBus.Protocol;

namespace CpuMon.Service;

internal sealed class ServiceRuntime
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(1);
    private static readonly ObjectPath SignalPath = new(ServiceInfo.ObjectPath);
    private static readonly object LogGate = new();

    private readonly PowerSampler _sampler;
    private readonly CommandLineOptions _options;
    private readonly object _sampleEmitGate = new();

    private DBusConnection? _connection;

    public ServiceRuntime(PowerSampler sampler, CommandLineOptions options)
    {
        _sampler = sampler;
        _options = options;
    }

    public async Task RunSamplerAsync(CancellationToken cancellationToken)
    {
        SampleAndEmit();

        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_sampler.SampleIntervalMs));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                SampleAndEmit();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    public async Task RunBusLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            DBusConnection? connection = null;
            try
            {
                string? address = _options.Bus == ServiceBus.System
                    ? DBusAddress.System
                    : DBusAddress.Session;

                if (address is null)
                {
                    Log($"no {_options.Bus.ToString().ToLowerInvariant()} bus address is available; retrying.");
                    await DelayReconnectAsync(cancellationToken).ConfigureAwait(false);
                    continue;
                }

                connection = new DBusConnection(address);
                await connection.ConnectAsync().ConfigureAwait(false);

                var handler = new CpuMonHandler(_sampler, connection, SampleAndEmit);
                connection.AddMethodHandler(handler);

                await connection.RequestNameAsync(
                    _options.BusName,
                    RequestNameOptions.ReplaceExisting).ConfigureAwait(false);

                Volatile.Write(ref _connection, connection);
                Log($"connected to {_options.Bus.ToString().ToLowerInvariant()} bus; owning {_options.BusName} at {ServiceInfo.ObjectPath}.");

                using var shutdownRegistration = cancellationToken.Register(
                    static state => ((DBusConnection)state!).Dispose(),
                    connection);

                Exception? disconnectReason = await connection.DisconnectedAsync().ConfigureAwait(false);

                Volatile.Write(ref _connection, null);

                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                Log(disconnectReason is null
                    ? "D-Bus connection closed; reconnecting."
                    : $"D-Bus connection lost ({disconnectReason.Message}); reconnecting.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Volatile.Write(ref _connection, null);
                Log($"D-Bus connection failed ({ex.Message}); retrying.");
            }
            finally
            {
                connection?.Dispose();
            }

            await DelayReconnectAsync(cancellationToken).ConfigureAwait(false);
        }

        Volatile.Write(ref _connection, null);
    }

    private void SampleAndEmit()
    {
        // Keep signal order consistent with sample order when the timer and a
        // D-Bus Refresh race each other.
        lock (_sampleEmitGate)
        {
            PowerSample sample = _sampler.Sample();
            EmitSample(sample);
        }
    }

    private void EmitSample(PowerSample sample)
    {
        if (_options.Verbose)
        {
            string errorSuffix = string.IsNullOrEmpty(sample.Error)
                ? string.Empty
                : $" error=\"{sample.Error}\"";
            Log(string.Format(
                CultureInfo.InvariantCulture,
                "sample available={0} energy_uj={1} watts={2:F6} timestamp_us={3} max_uj={4} package=\"{5}\"{6}",
                sample.Available,
                sample.EnergyUj,
                sample.Watts,
                sample.TimestampUs,
                sample.MaxEnergyRangeUj,
                sample.PackageName,
                errorSuffix));
        }

        if (!sample.Available)
        {
            return;
        }

        DBusConnection? connection = Volatile.Read(ref _connection);
        if (connection is null)
        {
            return;
        }

        try
        {
            connection.EmitPowerChanged(
                SignalPath,
                sample.EnergyUj,
                sample.Watts,
                sample.TimestampUs);
        }
        catch (Exception ex)
        {
            Log($"failed to emit PowerChanged ({ex.Message}).");
        }
    }

    private static async Task DelayReconnectAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(ReconnectDelay, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }

    private static void Log(string message)
    {
        lock (LogGate)
        {
            Console.Error.WriteLine($"cpumond: {message}");
        }
    }
}
