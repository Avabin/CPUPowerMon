using System.Diagnostics;

namespace CpuMon.Core;

/// <summary>
/// Monotonic timestamp source. Wall-clock changes must never influence power
/// calculations.
/// </summary>
public interface IMonotonicClock
{
    ulong GetTimestampUs();
}

public sealed class StopwatchMonotonicClock : IMonotonicClock
{
    public static StopwatchMonotonicClock Instance { get; } = new();

    public ulong GetTimestampUs()
    {
        long ticks = Stopwatch.GetTimestamp();
        long seconds = ticks / Stopwatch.Frequency;
        long remainder = ticks % Stopwatch.Frequency;

        // Split the conversion so the intermediate value cannot overflow on
        // hosts where Stopwatch.Frequency is 1 GHz and uptime is large.
        return (ulong)(seconds * 1_000_000L + remainder * 1_000_000L / Stopwatch.Frequency);
    }
}
