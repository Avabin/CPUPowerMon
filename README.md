# CPUPowerMon

Unprivileged CPU package-power readings for Linux, from Intel RAPL.

`/sys/class/powercap/intel-rapl:0/energy_uj` is a root-only, cumulative
microjoule counter. CPUPowerMon turns its delta into watts and exposes it over
the system D-Bus.

- **`cpumond`** — root systemd service (`Type=dbus`) exposing
  `org.cpupowermon.CpuMon1` on the system bus.
- **`cpumonctl`** — unprivileged CLI: one-shot watts, JSON, or `--watch`.
- **Plasma applet** — pure QML widget with live watts and a 60-sample sparkline,
  fed by `cpumonctl --watch`.
- **Packaging** — install/service/policy scripts targeting `/usr/local` (Fedora
  Atomic safe).

## Quick look

```bash
cpumonctl                # 5.42
cpumonctl --json         # {"watts":5.42,...}
cpumonctl --watch        # one value per second
```

## Build

```bash
dotnet build CPUPowerMon.slnx
dotnet test CPUPowerMon.slnx
```

## Install

```bash
sudo ./packaging/install.sh
./packaging/install-plasmoid.sh   # per-user, no root
```

Then add **CPU Power Monitor** to a Plasma panel. Root installation and the
final system-bus verification are documented in the verification plan and are
performed by the user. Uninstall with `sudo ./packaging/uninstall.sh`.
