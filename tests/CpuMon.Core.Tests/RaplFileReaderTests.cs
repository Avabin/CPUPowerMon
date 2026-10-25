using CpuMon.Core;
using NSubstitute;
using Shouldly;

namespace CpuMon.Core.Tests;

[TestFixture]
public sealed class RaplFileReaderTests
{
    private string _devicePath = null!;

    [SetUp]
    public void SetUp()
    {
        _devicePath = Path.Combine(
            Path.GetTempPath(),
            "cpumon-rapl-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_devicePath);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_devicePath))
        {
            Directory.Delete(_devicePath, recursive: true);
        }
    }

    [Test]
    public void Read_ValidDevice_ReturnsEnergyRangeAndName()
    {
        // Arrange
        WriteFile("energy_uj", "123456\n");
        WriteFile("max_energy_range_uj", "10000000\n");
        WriteFile("name", "package-0\n");
        var reader = new RaplFileReader(_devicePath);

        // Act
        RaplReadResult result = reader.Read();

        // Assert
        result.Success.ShouldBeTrue();
        result.Error.ShouldBeEmpty();
        result.Reading.EnergyUj.ShouldBe(123456UL);
        result.Reading.MaxEnergyRangeUj.ShouldBe(10_000_000UL);
        result.Reading.PackageName.ShouldBe("package-0");
    }

    [Test]
    public void Read_MissingEnergyFile_ReturnsFailure()
    {
        // Arrange
        WriteFile("max_energy_range_uj", "1000\n");
        WriteFile("name", "package-0\n");
        var reader = new RaplFileReader(_devicePath);

        // Act
        RaplReadResult result = reader.Read();

        // Assert
        result.Success.ShouldBeFalse();
        result.Reading.EnergyUj.ShouldBe(0UL);
        result.Error.ShouldContain("energy_uj");
    }

    [Test]
    public void Read_MissingDeviceDirectory_ReturnsFailure()
    {
        // Arrange
        string missing = Path.Combine(_devicePath, "not-there");
        var reader = new RaplFileReader(missing);

        // Act
        RaplReadResult result = reader.Read();

        // Assert
        result.Success.ShouldBeFalse();
        result.Error.ShouldNotBeNullOrWhiteSpace();
    }

    [Test]
    public void Read_EmptyEnergyFile_ReturnsFailure()
    {
        // Arrange
        WriteFile("energy_uj", string.Empty);
        WriteFile("max_energy_range_uj", "1000\n");
        var reader = new RaplFileReader(_devicePath);

        // Act
        RaplReadResult result = reader.Read();

        // Assert
        result.Success.ShouldBeFalse();
        result.Error.ShouldContain("energy_uj");
    }

    [Test]
    public void Read_WhitespaceOnlyEnergyFile_ReturnsFailure()
    {
        // Arrange
        WriteFile("energy_uj", " \r\n\t");
        WriteFile("max_energy_range_uj", "1000\n");
        var reader = new RaplFileReader(_devicePath);

        // Act
        RaplReadResult result = reader.Read();

        // Assert
        result.Success.ShouldBeFalse();
    }

    [TestCase("not-a-number")]
    [TestCase("12.5")]
    [TestCase("-1")]
    [TestCase("+1")]
    [TestCase("999999999999999999999999999999999")]
    public void Read_InvalidEnergyContents_ReturnsFailure(string contents)
    {
        // Arrange
        WriteFile("energy_uj", contents);
        WriteFile("max_energy_range_uj", "1000\n");
        var reader = new RaplFileReader(_devicePath);

        // Act
        RaplReadResult result = reader.Read();

        // Assert
        result.Success.ShouldBeFalse();
        result.Error.ShouldContain("energy_uj");
    }

    [Test]
    public void Read_UnreadableEnergyFile_ReturnsFailure()
    {
        // Arrange
        var fileSystem = Substitute.For<IRaplFileSystem>();
        fileSystem
            .ReadAllText(Path.Combine("/fake-rapl", "energy_uj"))
            .Returns(_ => throw new UnauthorizedAccessException("Permission denied."));
        var reader = new RaplFileReader("/fake-rapl", fileSystem);

        // Act
        RaplReadResult result = reader.Read();

        // Assert
        result.Success.ShouldBeFalse();
        result.Error.ShouldContain("energy_uj");
        result.Error.ShouldContain("Permission denied");
    }

    [Test]
    public void Read_MissingMaxRangeFile_UsesZero()
    {
        // Arrange
        WriteFile("energy_uj", "42\n");
        WriteFile("name", "package-0\n");
        var reader = new RaplFileReader(_devicePath);

        // Act
        RaplReadResult result = reader.Read();

        // Assert
        result.Success.ShouldBeTrue();
        result.Reading.EnergyUj.ShouldBe(42UL);
        result.Reading.MaxEnergyRangeUj.ShouldBe(0UL);
        result.Reading.PackageName.ShouldBe("package-0");
    }

    [TestCase("garbage")]
    [TestCase("-5")]
    [TestCase("")]
    [TestCase("18446744073709551616")]
    public void Read_InvalidMaxRangeContents_ReturnsFailure(string contents)
    {
        // Arrange
        WriteFile("energy_uj", "42\n");
        WriteFile("max_energy_range_uj", contents);
        var reader = new RaplFileReader(_devicePath);

        // Act
        RaplReadResult result = reader.Read();

        // Assert
        result.Success.ShouldBeFalse();
        result.Error.ShouldContain("max_energy_range_uj");
    }

    [Test]
    public void Read_UnreadableMaxRangeFile_ReturnsFailure()
    {
        // Arrange
        var fileSystem = Substitute.For<IRaplFileSystem>();
        fileSystem
            .ReadAllText(Path.Combine("/fake-rapl", "energy_uj"))
            .Returns("42\n");
        fileSystem
            .ReadAllText(Path.Combine("/fake-rapl", "max_energy_range_uj"))
            .Returns(_ => throw new IOException("Input/output error."));
        var reader = new RaplFileReader("/fake-rapl", fileSystem);

        // Act
        RaplReadResult result = reader.Read();

        // Assert
        result.Success.ShouldBeFalse();
        result.Error.ShouldContain("max_energy_range_uj");
    }

    [Test]
    public void Read_MissingNameFile_UsesEmptyName()
    {
        // Arrange
        WriteFile("energy_uj", "42\n");
        WriteFile("max_energy_range_uj", "1000\n");
        var reader = new RaplFileReader(_devicePath);

        // Act
        RaplReadResult result = reader.Read();

        // Assert
        result.Success.ShouldBeTrue();
        result.Reading.PackageName.ShouldBeEmpty();
    }

    [Test]
    public void Read_NameIsTrimmed()
    {
        // Arrange
        WriteFile("energy_uj", "42\n");
        WriteFile("max_energy_range_uj", "1000\n");
        WriteFile("name", "  package-1 \r\n");
        var reader = new RaplFileReader(_devicePath);

        // Act
        RaplReadResult result = reader.Read();

        // Assert
        result.Success.ShouldBeTrue();
        result.Reading.PackageName.ShouldBe("package-1");
    }

    [Test]
    public void Read_UnreadableNameFile_IsTreatedAsUnknown()
    {
        // Arrange
        var fileSystem = Substitute.For<IRaplFileSystem>();
        fileSystem
            .ReadAllText(Path.Combine("/fake-rapl", "energy_uj"))
            .Returns("42\n");
        fileSystem
            .ReadAllText(Path.Combine("/fake-rapl", "max_energy_range_uj"))
            .Returns("1000\n");
        fileSystem
            .ReadAllText(Path.Combine("/fake-rapl", "name"))
            .Returns(_ => throw new UnauthorizedAccessException("Permission denied."));
        var reader = new RaplFileReader("/fake-rapl", fileSystem);

        // Act
        RaplReadResult result = reader.Read();

        // Assert
        result.Success.ShouldBeTrue();
        result.Reading.PackageName.ShouldBeEmpty();
    }

    [Test]
    public void Read_MissingMaxRangeThroughFileSystem_IsTreatedAsUnknown()
    {
        // Arrange
        var fileSystem = Substitute.For<IRaplFileSystem>();
        fileSystem
            .ReadAllText(Path.Combine("/fake-rapl", "energy_uj"))
            .Returns("42\n");
        fileSystem
            .ReadAllText(Path.Combine("/fake-rapl", "max_energy_range_uj"))
            .Returns(_ => throw new FileNotFoundException("Missing."));
        var reader = new RaplFileReader("/fake-rapl", fileSystem);

        // Act
        RaplReadResult result = reader.Read();

        // Assert
        result.Success.ShouldBeTrue();
        result.Reading.MaxEnergyRangeUj.ShouldBe(0UL);
    }

    private void WriteFile(string name, string contents)
    {
        File.WriteAllText(Path.Combine(_devicePath, name), contents);
    }
}
