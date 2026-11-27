using Windows.Devices.Enumeration;
using Windows.Devices.Power;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace BatteryTray;

/// <summary>Reads batteries exposed through Windows' battery interface and the standard BLE Battery Service.</summary>
public sealed class BatteryService
{
    private static readonly Guid BatteryServiceUuid = Guid.Parse("0000180F-0000-1000-8000-00805F9B34FB");
    private static readonly Guid BatteryLevelUuid = Guid.Parse("00002A19-0000-1000-8000-00805F9B34FB");

    public async Task<IReadOnlyList<DeviceBattery>> ScanAsync(CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, DeviceBattery>(StringComparer.OrdinalIgnoreCase);
        await ReadWindowsBatteryDevices(results, cancellationToken);
        await ReadBleBatteryDevices(results, cancellationToken);
        return results.Values.OrderBy(x => x.Name).ToList();
    }

    private static async Task ReadWindowsBatteryDevices(Dictionary<string, DeviceBattery> results, CancellationToken ct)
    {
        try
        {
            var devices = await DeviceInformation.FindAllAsync(Battery.GetDeviceSelector());
            foreach (var info in devices)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var battery = await Battery.FromIdAsync(info.Id);
                    if (battery is null) continue;
                    var report = battery.GetReport();
                    int? percent = null;
                    var full = report.FullChargeCapacityInMilliwattHours;
                    var remaining = report.RemainingCapacityInMilliwattHours;
                    if (full.HasValue && remaining.HasValue && full.Value > 0)
                        percent = Math.Clamp((int)Math.Round(100.0 * remaining.Value / full.Value), 0, 100);
                    results[info.Id] = new DeviceBattery(info.Id, string.IsNullOrWhiteSpace(info.Name) ? "Bateria detectada pelo Windows" : info.Name,
                        percent, "Windows Battery API", percent is null ? "Nível indisponível" : "Conectado", DateTimeOffset.Now);
                }
                catch { /* One inaccessible device must not stop the scan. */ }
            }
        }
        catch { }
    }

    private static async Task ReadBleBatteryDevices(Dictionary<string, DeviceBattery> results, CancellationToken ct)
    {
        try
        {
            var selector = BluetoothLEDevice.GetDeviceSelectorFromPairingState(true);
            var paired = await DeviceInformation.FindAllAsync(selector);
            foreach (var info in paired)
            {
                ct.ThrowIfCancellationRequested();
                BluetoothLEDevice? device = null;
                try
                {
                    device = await BluetoothLEDevice.FromIdAsync(info.Id);
                    if (device is null) continue;
                    var serviceResult = await device.GetGattServicesForUuidAsync(BatteryServiceUuid, BluetoothCacheMode.Uncached);
                    if (serviceResult.Status != GattCommunicationStatus.Success || serviceResult.Services.Count == 0) continue;
                    var service = serviceResult.Services[0];
                    using (service)
                    {
                        var charResult = await service.GetCharacteristicsForUuidAsync(BatteryLevelUuid, BluetoothCacheMode.Uncached);
                        if (charResult.Status != GattCommunicationStatus.Success || charResult.Characteristics.Count == 0) continue;
                        var characteristic = charResult.Characteristics[0];
                        var read = await characteristic.ReadValueAsync(BluetoothCacheMode.Uncached);
                        if (read.Status != GattCommunicationStatus.Success) continue;
                        using var reader = DataReader.FromBuffer(read.Value);
                        if (reader.UnconsumedBufferLength < 1) continue;
                        var level = Math.Clamp((int)reader.ReadByte(), 0, 100);
                        var id = "BLE:" + info.Id;
                        results[id] = new DeviceBattery(id, string.IsNullOrWhiteSpace(device.Name) ? info.Name : device.Name,
                            level, "Bluetooth LE Battery Service", "Conectado", DateTimeOffset.Now);
                    }
                }
                catch { }
                finally { device?.Dispose(); }
            }
        }
        catch { }
    }
}
