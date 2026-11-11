using BatteryWidget.Core.Devices;
using BatteryWidget.Core.Protocols;

namespace BatteryWidget.Core.Services;

/// <summary>汇总所有 provider 的结果，任何 provider 出错都不影响其它设备。</summary>
public sealed class DeviceService
{
    private readonly List<IDeviceProvider> _providers;

    public DeviceService()
    {
        _providers = new List<IDeviceProvider>
        {
            new MchoseProvider(),
            new LogitechProvider(),
            new StandardBatteryProvider()
        };
    }

    public IReadOnlyList<DeviceSnapshot> ReadAll(DeviceReadRequest request)
    {
        var results = new List<DeviceSnapshot>();
        foreach (var provider in _providers)
        {
            try
            {
                results.AddRange(provider.Read(request));
            }
            catch (Exception ex)
            {
                results.Add(new DeviceSnapshot(
                    "error-" + provider.Name, provider.Name, DeviceKind.Unknown, "", null, false,
                    Error: ex.Message));
            }
        }
        return results;
    }
}
