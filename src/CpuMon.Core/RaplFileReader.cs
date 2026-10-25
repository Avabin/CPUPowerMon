using System.Globalization;

namespace CpuMon.Core;

/// <summary>
/// Reads <c>energy_uj</c>, <c>max_energy_range_uj</c> and <c>name</c> from a
/// powercap device directory. Only the directory comes from the operator
/// command line; no path is ever supplied over D-Bus.
/// </summary>
public sealed class RaplFileReader : IRaplReader
{
    public const string EnergyFileName = "energy_uj";
    public const string MaxEnergyRangeFileName = "max_energy_range_uj";
    public const string NameFileName = "name";

    private readonly string _devicePath;
    private readonly IRaplFileSystem _fileSystem;

    public RaplFileReader(string devicePath, IRaplFileSystem? fileSystem = null)
    {
        if (string.IsNullOrWhiteSpace(devicePath))
        {
            throw new ArgumentException("A RAPL device directory is required.", nameof(devicePath));
        }

        _devicePath = devicePath;
        _fileSystem = fileSystem ?? PhysicalRaplFileSystem.Instance;
    }

    public string DevicePath => _devicePath;

    public RaplReadResult Read()
    {
        string energyText;
        try
        {
            energyText = _fileSystem.ReadAllText(Path.Combine(_devicePath, EnergyFileName));
        }
        catch (Exception ex)
        {
            return RaplReadResult.Fail($"Could not read {EnergyFileName}: {ex.Message}");
        }

        if (!TryParseUnsigned(energyText, out ulong energyUj))
        {
            return RaplReadResult.Fail($"Invalid {EnergyFileName} contents.");
        }

        ulong maxEnergyRangeUj;
        try
        {
            string maxText = _fileSystem.ReadAllText(Path.Combine(_devicePath, MaxEnergyRangeFileName));
            if (!TryParseUnsigned(maxText, out maxEnergyRangeUj))
            {
                return RaplReadResult.Fail($"Invalid {MaxEnergyRangeFileName} contents.");
            }
        }
        catch (Exception ex) when (IsMissing(ex))
        {
            maxEnergyRangeUj = 0;
        }
        catch (Exception ex)
        {
            return RaplReadResult.Fail($"Could not read {MaxEnergyRangeFileName}: {ex.Message}");
        }

        string packageName;
        try
        {
            packageName = (_fileSystem.ReadAllText(Path.Combine(_devicePath, NameFileName)) ?? string.Empty).Trim();
        }
        catch (Exception)
        {
            packageName = string.Empty;
        }

        return RaplReadResult.Ok(new RaplReading(energyUj, maxEnergyRangeUj, packageName));
    }

    private static bool TryParseUnsigned(string? text, out ulong value)
    {
        value = 0;
        return text is not null &&
               ulong.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    private static bool IsMissing(Exception ex) =>
        ex is FileNotFoundException or DirectoryNotFoundException;
}
