using CpuMon.Core;

namespace CpuMon.Service;

internal enum ServiceBus
{
    System,
    Session,
}

internal sealed class CommandLineOptions
{
    public ServiceBus Bus { get; private init; } = ServiceBus.System;

    public string DevicePath { get; private init; } = ServiceInfo.DefaultDevicePath;

    public int IntervalMs { get; private init; } = SampleInterval.DefaultMilliseconds;

    public string BusName { get; private init; } = ServiceInfo.DefaultBusName;

    public bool Verbose { get; private init; }

    public bool ShowVersion { get; private init; }

    public bool ShowHelp { get; private init; }

    public static bool TryParse(string[] args, out CommandLineOptions options, out string error)
    {
        var parsed = new MutableOptions();
        error = string.Empty;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            switch (arg)
            {
                case "--bus":
                    if (!TryTakeValue(args, ref i, arg, out string busValue, out error))
                    {
                        options = parsed.Build();
                        return false;
                    }

                    if (string.Equals(busValue, "system", StringComparison.OrdinalIgnoreCase))
                    {
                        parsed.Bus = ServiceBus.System;
                    }
                    else if (string.Equals(busValue, "session", StringComparison.OrdinalIgnoreCase))
                    {
                        parsed.Bus = ServiceBus.Session;
                    }
                    else
                    {
                        error = $"--bus must be 'system' or 'session', not '{busValue}'.";
                        options = parsed.Build();
                        return false;
                    }

                    break;

                case "--device":
                    if (!TryTakeValue(args, ref i, arg, out string deviceValue, out error))
                    {
                        options = parsed.Build();
                        return false;
                    }

                    if (string.IsNullOrWhiteSpace(deviceValue))
                    {
                        error = "--device requires a non-empty path.";
                        options = parsed.Build();
                        return false;
                    }

                    parsed.DevicePath = deviceValue;
                    break;

                case "--interval-ms":
                    if (!TryTakeValue(args, ref i, arg, out string intervalValue, out error))
                    {
                        options = parsed.Build();
                        return false;
                    }

                    if (!int.TryParse(intervalValue, out int intervalMs))
                    {
                        error = $"--interval-ms requires an integer, not '{intervalValue}'.";
                        options = parsed.Build();
                        return false;
                    }

                    parsed.IntervalMs = SampleInterval.ClampMilliseconds(intervalMs);
                    break;

                case "--name":
                    if (!TryTakeValue(args, ref i, arg, out string nameValue, out error))
                    {
                        options = parsed.Build();
                        return false;
                    }

                    if (string.IsNullOrWhiteSpace(nameValue))
                    {
                        error = "--name requires a non-empty D-Bus name.";
                        options = parsed.Build();
                        return false;
                    }

                    parsed.BusName = nameValue;
                    break;

                case "--verbose":
                    parsed.Verbose = true;
                    break;

                case "--version":
                    parsed.ShowVersion = true;
                    break;

                case "--help":
                case "-h":
                    parsed.ShowHelp = true;
                    break;

                default:
                    error = $"Unknown argument '{arg}'.";
                    options = parsed.Build();
                    return false;
            }
        }

        options = parsed.Build();
        return true;
    }

    private static bool TryTakeValue(
        string[] args,
        ref int index,
        string option,
        out string value,
        out string error)
    {
        if (index + 1 >= args.Length)
        {
            value = string.Empty;
            error = $"{option} requires a value.";
            return false;
        }

        index++;
        value = args[index];
        error = string.Empty;
        return true;
    }

    private sealed class MutableOptions
    {
        public ServiceBus Bus { get; set; } = ServiceBus.System;

        public string DevicePath { get; set; } = ServiceInfo.DefaultDevicePath;

        public int IntervalMs { get; set; } = SampleInterval.DefaultMilliseconds;

        public string BusName { get; set; } = ServiceInfo.DefaultBusName;

        public bool Verbose { get; set; }

        public bool ShowVersion { get; set; }

        public bool ShowHelp { get; set; }

        public CommandLineOptions Build() => new()
        {
            Bus = Bus,
            DevicePath = DevicePath,
            IntervalMs = IntervalMs,
            BusName = BusName,
            Verbose = Verbose,
            ShowVersion = ShowVersion,
            ShowHelp = ShowHelp,
        };
    }
}
