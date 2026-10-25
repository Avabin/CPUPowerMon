using CpuMon.Core;
using NSubstitute;
using Shouldly;

namespace CpuMon.Core.Tests;

[TestFixture]
public sealed class PowerSamplerTests
{
    [Test]
    public void Latest_BeforeAnySample_IsUnavailable()
    {
        // Arrange
        var reader = Substitute.For<IRaplReader>();
        var clock = Substitute.For<IMonotonicClock>();
        var sampler = new PowerSampler(reader, clock, 1000);

        // Act
        PowerSample latest = sampler.Latest;

        // Assert
        latest.Available.ShouldBeFalse();
        latest.EnergyUj.ShouldBe(0UL);
        latest.Watts.ShouldBe(0.0);
        latest.TimestampUs.ShouldBe(0UL);
        latest.Error.ShouldNotBeNullOrWhiteSpace();
    }

    [Test]
    public void Sample_FirstRead_IsBaselineOnly()
    {
        // Arrange
        var reader = Substitute.For<IRaplReader>();
        reader.Read().Returns(Ok(5_000_000, 100_000_000, "package-0"));
        var clock = Substitute.For<IMonotonicClock>();
        clock.GetTimestampUs().Returns(1_000_000UL);
        var sampler = new PowerSampler(reader, clock, 1000);

        // Act
        PowerSample sample = sampler.Sample();

        // Assert
        sample.Available.ShouldBeTrue();
        sample.Watts.ShouldBe(0.0);
        sample.EnergyUj.ShouldBe(5_000_000UL);
        sample.TimestampUs.ShouldBe(1_000_000UL);
        sample.MaxEnergyRangeUj.ShouldBe(100_000_000UL);
        sample.PackageName.ShouldBe("package-0");
        sample.Error.ShouldBeEmpty();
    }

    [Test]
    public void Sample_IncreasingCounter_ComputesWattsWithFractionalInterval()
    {
        // Arrange
        var reader = Substitute.For<IRaplReader>();
        reader.Read().Returns(
            Ok(1_000_000, 100_000_000, "package-0"),
            Ok(13_500_000, 100_000_000, "package-0"));
        var clock = Substitute.For<IMonotonicClock>();
        clock.GetTimestampUs().Returns(1_000_000UL, 3_500_000UL);
        var sampler = new PowerSampler(reader, clock, 1000);
        sampler.Sample();

        // Act
        PowerSample sample = sampler.Sample();

        // Assert
        sample.Available.ShouldBeTrue();
        sample.Watts.ShouldBe(5.0, 1e-9);
        sample.EnergyUj.ShouldBe(13_500_000UL);
        sample.TimestampUs.ShouldBe(3_500_000UL);
    }

    [Test]
    public void Sample_CounterWraps_UsesMaxEnergyRange()
    {
        // Arrange
        var reader = Substitute.For<IRaplReader>();
        reader.Read().Returns(
            Ok(9_000_000, 10_000_000, "package-0"),
            Ok(1_000_000, 10_000_000, "package-0"));
        var clock = Substitute.For<IMonotonicClock>();
        clock.GetTimestampUs().Returns(1_000_000UL, 2_000_000UL);
        var sampler = new PowerSampler(reader, clock, 1000);
        sampler.Sample();

        // Act
        PowerSample sample = sampler.Sample();

        // Assert
        sample.Available.ShouldBeTrue();
        sample.Watts.ShouldBe(2.0, 1e-9);
    }

    [Test]
    public void Sample_IdenticalCounter_ProducesZeroAndFiniteWatts()
    {
        // Arrange
        var reader = Substitute.For<IRaplReader>();
        reader.Read().Returns(
            Ok(5_000_000, 100_000_000, "package-0"),
            Ok(5_000_000, 100_000_000, "package-0"));
        var clock = Substitute.For<IMonotonicClock>();
        clock.GetTimestampUs().Returns(1_000_000UL, 2_000_000UL);
        var sampler = new PowerSampler(reader, clock, 1000);
        sampler.Sample();

        // Act
        PowerSample sample = sampler.Sample();

        // Assert
        sample.Available.ShouldBeTrue();
        sample.Watts.ShouldBe(0.0);
        double.IsFinite(sample.Watts).ShouldBeTrue();
    }

    [Test]
    public void Sample_NonAdvancingClock_ProducesZeroAndFiniteWatts()
    {
        // Arrange
        var reader = Substitute.For<IRaplReader>();
        reader.Read().Returns(
            Ok(1_000_000, 100_000_000, "package-0"),
            Ok(9_000_000, 100_000_000, "package-0"));
        var clock = Substitute.For<IMonotonicClock>();
        clock.GetTimestampUs().Returns(5_000_000UL, 5_000_000UL);
        var sampler = new PowerSampler(reader, clock, 1000);
        sampler.Sample();

        // Act
        PowerSample sample = sampler.Sample();

        // Assert
        sample.Watts.ShouldBe(0.0);
        double.IsFinite(sample.Watts).ShouldBeTrue();
    }

    [Test]
    public void Sample_ClockMovingBackwards_ProducesZeroAndFiniteWatts()
    {
        // Arrange
        var reader = Substitute.For<IRaplReader>();
        reader.Read().Returns(
            Ok(1_000_000, 100_000_000, "package-0"),
            Ok(9_000_000, 100_000_000, "package-0"));
        var clock = Substitute.For<IMonotonicClock>();
        clock.GetTimestampUs().Returns(5_000_000UL, 4_000_000UL);
        var sampler = new PowerSampler(reader, clock, 1000);
        sampler.Sample();

        // Act
        PowerSample sample = sampler.Sample();

        // Assert
        sample.Watts.ShouldBe(0.0);
        double.IsFinite(sample.Watts).ShouldBeTrue();
    }

    [Test]
    public void Sample_CounterMovesBackwardsWithoutWrapRange_ProducesZero()
    {
        // Arrange
        var reader = Substitute.For<IRaplReader>();
        reader.Read().Returns(
            Ok(9_000_000, 0, "package-0"),
            Ok(1_000_000, 0, "package-0"));
        var clock = Substitute.For<IMonotonicClock>();
        clock.GetTimestampUs().Returns(1_000_000UL, 2_000_000UL);
        var sampler = new PowerSampler(reader, clock, 1000);
        sampler.Sample();

        // Act
        PowerSample sample = sampler.Sample();

        // Assert
        sample.Available.ShouldBeTrue();
        sample.Watts.ShouldBe(0.0);
    }

    [Test]
    public void Sample_FirstReadFails_ReportsUnavailableWithoutBaseline()
    {
        // Arrange
        var reader = Substitute.For<IRaplReader>();
        reader.Read().Returns(Fail("energy_uj is missing."));
        var clock = Substitute.For<IMonotonicClock>();
        clock.GetTimestampUs().Returns(7_000_000UL);
        var sampler = new PowerSampler(reader, clock, 1000);

        // Act
        PowerSample sample = sampler.Sample();

        // Assert
        sample.Available.ShouldBeFalse();
        sample.EnergyUj.ShouldBe(0UL);
        sample.Watts.ShouldBe(0.0);
        sample.TimestampUs.ShouldBe(7_000_000UL);
        sample.MaxEnergyRangeUj.ShouldBe(0UL);
        sample.PackageName.ShouldBeEmpty();
        sample.Error.ShouldBe("energy_uj is missing.");
    }

    [Test]
    public void Sample_FailureAfterSuccess_ClearsValuesButRetainsMetadata()
    {
        // Arrange
        var reader = Substitute.For<IRaplReader>();
        reader.Read().Returns(
            Ok(5_000_000, 100_000_000, "package-1"),
            Fail("permission denied"));
        var clock = Substitute.For<IMonotonicClock>();
        clock.GetTimestampUs().Returns(1_000_000UL, 2_000_000UL);
        var sampler = new PowerSampler(reader, clock, 1000);
        sampler.Sample();

        // Act
        PowerSample sample = sampler.Sample();

        // Assert
        sample.Available.ShouldBeFalse();
        sample.EnergyUj.ShouldBe(0UL);
        sample.Watts.ShouldBe(0.0);
        sample.MaxEnergyRangeUj.ShouldBe(100_000_000UL);
        sample.PackageName.ShouldBe("package-1");
        sample.Error.ShouldBe("permission denied");
    }

    [Test]
    public void Sample_RecoveryAfterFailure_ComputesAcrossTheGap()
    {
        // Arrange
        var reader = Substitute.For<IRaplReader>();
        reader.Read().Returns(
            Ok(1_000_000, 100_000_000, "package-0"),
            Fail("gone"),
            Ok(3_000_000, 100_000_000, "package-0"));
        var clock = Substitute.For<IMonotonicClock>();
        clock.GetTimestampUs().Returns(1_000_000UL, 2_000_000UL, 3_000_000UL);
        var sampler = new PowerSampler(reader, clock, 1000);
        sampler.Sample();
        sampler.Sample();

        // Act
        PowerSample sample = sampler.Sample();

        // Assert
        sample.Available.ShouldBeTrue();
        sample.Watts.ShouldBe(1.0, 1e-9);
    }

    [Test]
    public void Sample_ReaderThrows_ReportsUnavailable()
    {
        // Arrange
        var reader = Substitute.For<IRaplReader>();
        reader.Read().Returns(_ => throw new InvalidOperationException("boom"));
        var clock = Substitute.For<IMonotonicClock>();
        clock.GetTimestampUs().Returns(1_000_000UL);
        var sampler = new PowerSampler(reader, clock, 1000);

        // Act
        PowerSample sample = sampler.Sample();

        // Assert
        sample.Available.ShouldBeFalse();
        sample.Error.ShouldContain("boom");
    }

    [Test]
    public void Sample_LargeCounterValuesRemainFinite()
    {
        // Arrange
        const ulong previous = ulong.MaxValue - 1_000_000;
        var reader = Substitute.For<IRaplReader>();
        reader.Read().Returns(
            Ok(previous, ulong.MaxValue, "package-0"),
            Ok(ulong.MaxValue, ulong.MaxValue, "package-0"));
        var clock = Substitute.For<IMonotonicClock>();
        clock.GetTimestampUs().Returns(1_000_000UL, 2_000_000UL);
        var sampler = new PowerSampler(reader, clock, 1000);
        sampler.Sample();

        // Act
        PowerSample sample = sampler.Sample();

        // Assert
        sample.Watts.ShouldBe(1.0, 1e-9);
        double.IsFinite(sample.Watts).ShouldBeTrue();
    }

    [Test]
    public void Latest_AfterSample_MatchesReturnedSample()
    {
        // Arrange
        var reader = Substitute.For<IRaplReader>();
        reader.Read().Returns(Ok(42, 1000, "package-0"));
        var clock = Substitute.For<IMonotonicClock>();
        clock.GetTimestampUs().Returns(99UL);
        var sampler = new PowerSampler(reader, clock, 1000);

        // Act
        PowerSample returned = sampler.Sample();

        // Assert
        sampler.Latest.ShouldBe(returned);
    }

    [TestCase(1, 100)]
    [TestCase(99, 100)]
    [TestCase(100, 100)]
    [TestCase(1_000, 1_000)]
    [TestCase(60_000, 60_000)]
    [TestCase(60_001, 60_000)]
    [TestCase(int.MaxValue, 60_000)]
    public void Constructor_ClampsSampleInterval(int requested, int expected)
    {
        // Arrange
        var reader = Substitute.For<IRaplReader>();
        var clock = Substitute.For<IMonotonicClock>();

        // Act
        var sampler = new PowerSampler(reader, clock, requested);

        // Assert
        sampler.SampleIntervalMs.ShouldBe(expected);
    }

    private static RaplReadResult Ok(ulong energyUj, ulong maxEnergyRangeUj, string packageName) =>
        RaplReadResult.Ok(new RaplReading(energyUj, maxEnergyRangeUj, packageName));

    private static RaplReadResult Fail(string error) => RaplReadResult.Fail(error);
}

public sealed class PowerSamplerNormalizationTests
{
    [Test]
    public void Sample_NullPackageName_IsNormalizedToEmpty()
    {
        // Arrange
        var reader = Substitute.For<IRaplReader>();
        reader.Read().Returns(RaplReadResult.Ok(new RaplReading(42, 1000, null!)));
        var clock = Substitute.For<IMonotonicClock>();
        clock.GetTimestampUs().Returns(1UL);
        var sampler = new PowerSampler(reader, clock, 1000);

        // Act
        PowerSample sample = sampler.Sample();

        // Assert
        sample.PackageName.ShouldBeEmpty();
        sampler.Latest.PackageName.ShouldBeEmpty();
    }

    [Test]
    public void Sample_FailureWithEmptyError_UsesFallbackError()
    {
        // Arrange
        var reader = Substitute.For<IRaplReader>();
        reader.Read().Returns(new RaplReadResult(false, default, string.Empty));
        var clock = Substitute.For<IMonotonicClock>();
        clock.GetTimestampUs().Returns(1UL);
        var sampler = new PowerSampler(reader, clock, 1000);

        // Act
        PowerSample sample = sampler.Sample();

        // Assert
        sample.Available.ShouldBeFalse();
        sample.Error.ShouldBe("RAPL read failed.");
    }
}
