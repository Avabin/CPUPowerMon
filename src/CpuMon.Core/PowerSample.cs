namespace CpuMon.Core;

/// <summary>
/// Latest sampler state plus the derived power. <see cref="EnergyUj"/> and
/// <see cref="Watts"/> are zero while unavailable, as required by the contract.
/// </summary>
public readonly record struct PowerSample(
    ulong EnergyUj,
    double Watts,
    ulong TimestampUs,
    bool Available,
    ulong MaxEnergyRangeUj,
    string PackageName,
    string Error)
{
    public static PowerSample Unavailable(
        ulong timestampUs,
        string error,
        ulong maxEnergyRangeUj = 0,
        string packageName = "") =>
        new(0, 0.0, timestampUs, false, maxEnergyRangeUj, packageName, error);
}
