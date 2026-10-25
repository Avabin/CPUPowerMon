namespace CpuMon.Core;

/// <summary>
/// Stateful RAPL sampler. Thread-safe: the periodic timer and D-Bus
/// <c>Refresh</c> calls may sample concurrently.
/// </summary>
public sealed class PowerSampler
{
    private const double MicrojoulesPerJoule = 1_000_000.0;
    private const double MicrosecondsPerSecond = 1_000_000.0;

    private readonly object _sampleGate = new();
    private readonly object _stateGate = new();
    private readonly IRaplReader _reader;
    private readonly IMonotonicClock _clock;

    private bool _hasBaseline;
    private ulong _previousEnergyUj;
    private ulong _previousTimestampUs;
    private ulong _lastKnownMaxEnergyRangeUj;
    private string _lastKnownPackageName = string.Empty;
    private PowerSample _latest;

    public PowerSampler(IRaplReader reader, IMonotonicClock clock, int sampleIntervalMs)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        SampleIntervalMs = SampleInterval.ClampMilliseconds(sampleIntervalMs);
        _latest = PowerSample.Unavailable(0, "No sample yet.");
    }

    public int SampleIntervalMs { get; }

    public PowerSample Latest
    {
        get
        {
            lock (_stateGate)
            {
                return _latest;
            }
        }
    }

    public PowerSample Sample()
    {
        // Serialize reads and baseline updates, but keep Latest cheap: property
        // reads only take the short state lock and never wait for file I/O.
        lock (_sampleGate)
        {
            RaplReadResult readResult;
            try
            {
                readResult = _reader.Read();
            }
            catch (Exception ex)
            {
                readResult = RaplReadResult.Fail($"RAPL read failed: {ex.Message}");
            }

            ulong timestampUs = _clock.GetTimestampUs();

            if (!readResult.Success)
            {
                string error = string.IsNullOrWhiteSpace(readResult.Error)
                    ? "RAPL read failed."
                    : readResult.Error;
                PowerSample unavailable = PowerSample.Unavailable(
                    timestampUs,
                    error,
                    _lastKnownMaxEnergyRangeUj,
                    _lastKnownPackageName);

                lock (_stateGate)
                {
                    _latest = unavailable;
                }

                return unavailable;
            }

            RaplReading reading = readResult.Reading;
            string packageName = reading.PackageName ?? string.Empty;
            _lastKnownMaxEnergyRangeUj = reading.MaxEnergyRangeUj;
            _lastKnownPackageName = packageName;

            double watts = 0.0;
            if (_hasBaseline)
            {
                ulong deltaEnergyUj = ComputeDelta(
                    reading.EnergyUj,
                    _previousEnergyUj,
                    reading.MaxEnergyRangeUj);

                ulong deltaTimeUs = timestampUs >= _previousTimestampUs
                    ? timestampUs - _previousTimestampUs
                    : 0;

                if (deltaEnergyUj > 0 && deltaTimeUs > 0)
                {
                    double candidate =
                        (double)deltaEnergyUj / MicrojoulesPerJoule /
                        ((double)deltaTimeUs / MicrosecondsPerSecond);
                    watts = double.IsFinite(candidate) ? candidate : 0.0;
                }
            }

            _hasBaseline = true;
            _previousEnergyUj = reading.EnergyUj;
            _previousTimestampUs = timestampUs;

            PowerSample sample = new(
                reading.EnergyUj,
                watts,
                timestampUs,
                true,
                reading.MaxEnergyRangeUj,
                packageName,
                string.Empty);

            lock (_stateGate)
            {
                _latest = sample;
            }

            return sample;
        }
    }

    private static ulong ComputeDelta(ulong current, ulong previous, ulong maxEnergyRangeUj)
    {
        if (current >= previous)
        {
            return current - previous;
        }

        if (maxEnergyRangeUj >= previous)
        {
            UInt128 wrapped = (UInt128)(maxEnergyRangeUj - previous) + current;
            return wrapped > ulong.MaxValue ? ulong.MaxValue : (ulong)wrapped;
        }

        // The counter moved backwards but we do not know a valid wrap range.
        // Treat it as a non-advancing sample instead of inventing a huge delta.
        return 0;
    }
}
