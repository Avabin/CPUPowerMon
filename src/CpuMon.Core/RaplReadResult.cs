namespace CpuMon.Core;

public readonly record struct RaplReading(
    ulong EnergyUj,
    ulong MaxEnergyRangeUj,
    string PackageName);

public readonly record struct RaplReadResult(
    bool Success,
    RaplReading Reading,
    string Error)
{
    public static RaplReadResult Ok(RaplReading reading) =>
        new(true, reading, string.Empty);

    public static RaplReadResult Fail(string error) =>
        new(false, default, string.IsNullOrWhiteSpace(error) ? "RAPL read failed." : error);
}

/// <summary>
/// A single attempt to read a RAPL counter and its metadata.
/// </summary>
public interface IRaplReader
{
    RaplReadResult Read();
}
