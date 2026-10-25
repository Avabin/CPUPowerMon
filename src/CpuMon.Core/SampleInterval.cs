namespace CpuMon.Core;

public static class SampleInterval
{
    public const int DefaultMilliseconds = 1000;
    public const int MinimumMilliseconds = 100;
    public const int MaximumMilliseconds = 60_000;

    public static int ClampMilliseconds(int milliseconds) =>
        Math.Clamp(milliseconds, MinimumMilliseconds, MaximumMilliseconds);
}
