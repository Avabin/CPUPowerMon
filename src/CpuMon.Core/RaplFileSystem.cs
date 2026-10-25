namespace CpuMon.Core;

/// <summary>
/// Reads the three files that make up a RAPL powercap device directory.
/// </summary>
public interface IRaplFileSystem
{
    string ReadAllText(string path);
}

public sealed class PhysicalRaplFileSystem : IRaplFileSystem
{
    public static PhysicalRaplFileSystem Instance { get; } = new();

    public string ReadAllText(string path) => File.ReadAllText(path);
}
