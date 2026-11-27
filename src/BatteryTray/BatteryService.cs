using System.Diagnostics;
using HidSharp;
using Windows.Devices.Enumeration;
using Windows.Devices.Power;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace BatteryTray;

/// <summary>
/// Reads Windows and standard BLE batteries, and identifies supported vendor HID devices without
/// sending unverified vendor reports.
/// </summary>
public sealed class BatteryService
{
    private static readonly Guid BatteryServiceUuid = Guid.Parse("0000180F-0000-1000-8000-00805F9B34FB");
    private static readonly Guid BatteryLevelUuid = Guid.Parse("00002A19-0000-1000-8000-00805F9B34FB");

    public async Task<IReadOnlyList<DeviceBattery>> ScanAsync(CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, DeviceBattery>(StringComparer.OrdinalIgnoreCase);
        await ReadWindowsBatteryDevices(results, cancellationToken);
        await ReadBleBatteryDevices(results, cancellationToken);
        await ReadVendorHidDevices(results, cancellationToken);
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
                        var rawLevel = (int)reader.ReadByte();
                        if (rawLevel > 100) continue;
                        var id = "BLE:" + info.Id;
                        results[id] = new DeviceBattery(id, string.IsNullOrWhiteSpace(device.Name) ? info.Name : device.Name,
                            rawLevel, "Bluetooth LE Battery Service", "Conectado", DateTimeOffset.Now);
                    }
                }
                catch { }
                finally { device?.Dispose(); }
            }
        }
        catch { }
    }

    private static async Task ReadVendorHidDevices(Dictionary<string, DeviceBattery> results, CancellationToken ct)
    {
        // HID exchanges are synchronous in HidSharp. Keep them off the WinForms UI thread.
        var found = await Task.Run(() => ReadVendorHidDevicesSync(ct), ct);
        foreach (var item in found)
            results[item.Id] = item;
    }

    private static IReadOnlyList<DeviceBattery> ReadVendorHidDevicesSync(CancellationToken ct)
    {
        var results = new List<DeviceBattery>();
        var list = DeviceList.Local;

        // Ajazz/AULA-family 2.4 GHz receiver. This is a read-only battery request:
        // 20-byte output report 0x13, command 0x4A, additive checksum.
        foreach (var device in list.GetHidDevices(0x3554, 0xFA09))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (!HasAulaBatteryCollection(device))
                    continue;
                if (device.GetMaxOutputReportLength() < 20 || device.GetMaxInputReportLength() < 20)
                    continue;

                using var stream = device.Open();
                stream.ReadTimeout = 250;
                stream.WriteTimeout = 500;
                var request = BuildAulaBatteryRequest();
                stream.Write(request);

                var response = new byte[device.GetMaxInputReportLength()];
                var timer = Stopwatch.StartNew();
                while (timer.ElapsedMilliseconds < 1200)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        var count = stream.Read(response, 0, response.Length);
                        if (count < 20 || !IsAulaBatteryReply(response.AsSpan(0, count)))
                            continue;

                        var level = response[5];
                        var state = response[6];
                        if (level is 0 or > 100)
                            break; // Invalid/placeholder level: never publish it.

                        var id = $"HID:3554:FA09:{device.DevicePath}";
                        if (state == 0x10)
                        {
                            // On cable the receiver reports a fixed 100, even when the battery is not full.
                            results.Add(new DeviceBattery(id, "Ajazz F75", null,
                                "HID 2.4 GHz (3554:FA09)", "Conectado por cabo — nível não confiável", DateTimeOffset.Now));
                        }
                        else if (state == 0x01)
                        {
                            results.Add(new DeviceBattery(id, "Ajazz F75", level,
                                "HID 2.4 GHz (3554:FA09)", "Na bateria", DateTimeOffset.Now));
                        }
                        else
                        {
                            results.Add(new DeviceBattery(id, "Ajazz F75", level,
                                "HID 2.4 GHz (3554:FA09)", "Estado de energia não identificado", DateTimeOffset.Now));
                        }
                        break;
                    }
                    catch (TimeoutException) { /* Ignore unrelated/unsolicited reports and keep the bounded wait. */ }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch { /* Receiver absent, busy, unsupported or refusing access: keep the rest of the scan alive. */ }
        }

        // Onikuma B2 is identified, but no trustworthy public battery-query protocol is known yet.
        // Show its presence without inventing a percentage or sending undocumented HID commands.
        foreach (var device in list.GetHidDevices(0x3634, 0x4155))
        {
            ct.ThrowIfCancellationRequested();
            var id = $"HID:3634:4155:{device.DevicePath}";
            if (results.Any(x => x.Id.StartsWith("HID:3634:4155:", StringComparison.OrdinalIgnoreCase)))
                break;
            results.Add(new DeviceBattery(id, "Onikuma B2", null,
                "USB HID (3634:4155)", "Detectado — protocolo de bateria ainda não identificado", DateTimeOffset.Now));
        }

        return results;
    }

    private static bool HasAulaBatteryCollection(HidDevice device)
    {
        try
        {
            // HidSharp encodes a Usage as (usage page << 16) | usage.
            // Restrict writes to the documented vendor collection FF02:0002 and report 0x13.
            var descriptor = device.GetReportDescriptor();
            var hasUsage = descriptor.DeviceItems.Any(item =>
                item.Usages.GetAllValues().Any(value => value == 0xFF020002));
            var hasBatteryOutput = descriptor.OutputReports.Any(report =>
                report.ReportID == 0x13 && report.Length == 20);
            return hasUsage && hasBatteryOutput;
        }
        catch
        {
            // If the collection cannot be verified, do not send anything to the receiver.
            return false;
        }
    }

    private static byte[] BuildAulaBatteryRequest()
    {
        var frame = new byte[20];
        frame[0] = 0x13;
        frame[1] = 0x4A;
        frame[^1] = Checksum(frame);
        return frame;
    }

    private static bool IsAulaBatteryReply(ReadOnlySpan<byte> report)
    {
        if (report.Length < 20 || report[0] != 0x13 || (report[1] & 0x7F) != 0x4A)
            return false;
        return report[19] == Checksum(report[..20]);
    }

    private static byte Checksum(ReadOnlySpan<byte> frame)
    {
        byte sum = 0;
        foreach (var value in frame[..^1])
            sum = unchecked((byte)(sum + value));
        return sum;
    }
}
