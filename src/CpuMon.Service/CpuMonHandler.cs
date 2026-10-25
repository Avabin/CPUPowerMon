using CpuMon.Core;
using CpuMon.DBus;
using Tmds.DBus.Protocol;

namespace CpuMon.Service;

internal sealed class CpuMonHandler : DBusHandler, ICpuMon1Handler, ICpuMon1Properties
{
    private readonly PowerSampler _sampler;
    private readonly Action _refresh;

    public CpuMonHandler(PowerSampler sampler, DBusConnection connection, Action refresh)
        : base(connection, ServiceInfo.ObjectPath, handlesChildPaths: false)
    {
        _sampler = sampler;
        _refresh = refresh;
    }

    public ValueTask HandleGetPropertyAsync(ICpuMon1Handler.GetPropertyContext context) =>
        context.Handle(this);

    public ValueTask HandleGetAllPropertiesAsync(ICpuMon1Handler.GetAllPropertiesContext context)
    {
        PowerSample sample = _sampler.Latest;
        return context.Handle(
            available: sample.Available,
            energyUj: sample.EnergyUj,
            lastError: sample.Error,
            maxEnergyRangeUj: sample.MaxEnergyRangeUj,
            packageName: sample.PackageName,
            powerWatts: sample.Watts,
            sampleIntervalMs: (uint)_sampler.SampleIntervalMs,
            version: ServiceInfo.Version);
    }

    public ValueTask<double> GetPowerWattsAsync() =>
        ValueTask.FromResult(_sampler.Latest.Watts);

    public ValueTask<(ulong, double, ulong, bool)> GetSampleAsync()
    {
        PowerSample sample = _sampler.Latest;
        return ValueTask.FromResult((
            sample.EnergyUj,
            sample.Watts,
            sample.TimestampUs,
            sample.Available));
    }

    public ValueTask RefreshAsync()
    {
        try
        {
            _refresh();
        }
        catch (Exception)
        {
            // Refresh is a best-effort immediate sample. Internal failures must
            // not turn into D-Bus errors; sampler state already records errors.
        }

        return default;
    }

    double ICpuMon1Properties.PowerWatts => _sampler.Latest.Watts;

    ulong ICpuMon1Properties.EnergyUj => _sampler.Latest.EnergyUj;

    ulong ICpuMon1Properties.MaxEnergyRangeUj => _sampler.Latest.MaxEnergyRangeUj;

    uint ICpuMon1Properties.SampleIntervalMs => (uint)_sampler.SampleIntervalMs;

    string ICpuMon1Properties.PackageName => _sampler.Latest.PackageName;

    bool ICpuMon1Properties.Available => _sampler.Latest.Available;

    string ICpuMon1Properties.LastError => _sampler.Latest.Error;

    string ICpuMon1Properties.Version => ServiceInfo.Version;
}
