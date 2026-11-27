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

    /// <summary>
    /// Passively records input reports from every HID interface exposed by the Onikuma B2 dongle.
    /// No Output or Feature reports are sent. All interfaces are observed concurrently so the
    /// requested duration is a single shared capture window.
    /// </summary>
    public Task<string> CaptureOnikumaReportsAsync(TimeSpan duration, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => CaptureOnikumaReportsSync(duration, cancellationToken), cancellationToken);
    }

    private static string CaptureOnikumaReportsSync(TimeSpan duration, CancellationToken ct)
    {
        var log = new System.Text.StringBuilder();
        var devices = DeviceList.Local.GetHidDevices(0x3634, 0x4155)
            .GroupBy(device => device.DevicePath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        log.AppendLine("BatteryTray — diagnóstico passivo Onikuma B2 (todas as interfaces HID)");
        log.AppendLine($"VID:PID 3634:4155 | Janela de captura: {duration.TotalSeconds:0} s por interface, em paralelo");
        log.AppendLine("Modo somente leitura: nenhum relatório Output/Feature foi enviado.");
        log.AppendLine($"Início: {DateTimeOffset.Now:O}");
        log.AppendLine($"Interfaces HID encontradas: {devices.Count}");
        log.AppendLine();

        if (devices.Count == 0)
        {
            log.AppendLine("Nenhuma interface HID com VID:PID 3634:4155 foi enumerada pelo Windows.");
            log.AppendLine($"Fim: {DateTimeOffset.Now:O}");
            return log.ToString();
        }

        // Read every HID collection at the same time. A silent interface must not prevent
        // another collection from being observed during the same 15-second window.
        var captures = devices.Select((device, index) =>
            Task.Run(() => CaptureOnikumaInterface(device, index + 1, duration, ct), ct)).ToArray();
        Task.WaitAll(captures, ct);

        foreach (var capture in captures)
        {
            log.Append(capture.Result);
            log.AppendLine();
        }

        log.AppendLine($"Fim: {DateTimeOffset.Now:O}");
        return log.ToString();
    }

    private static string CaptureOnikumaInterface(HidDevice device, int index, TimeSpan duration, CancellationToken ct)
    {
        var log = new System.Text.StringBuilder();
        log.AppendLine($"=== Interface {index} ===");
        log.AppendLine($"Caminho: {device.DevicePath}");

        try
        {
            var inputLength = device.GetMaxInputReportLength();
            log.AppendLine($"Relatório de entrada máximo: {inputLength} bytes");
            log.AppendLine($"Relatório de saída máximo: {device.GetMaxOutputReportLength()} bytes (não utilizado)");
            log.AppendLine($"Relatório Feature máximo: {device.GetMaxFeatureReportLength()} bytes (não utilizado)");

            try
            {
                var descriptor = device.GetReportDescriptor();
                var usageItems = descriptor.DeviceItems
                    .SelectMany(item => item.Usages.GetAllValues())
                    .Distinct()
                    .Select(value => $"{value >> 16:X4}:{value & 0xFFFF:X4}")
                    .ToArray();
                log.AppendLine($"Usages HID: {(usageItems.Length == 0 ? "não informados" : string.Join(", ", usageItems))}");
                log.AppendLine($"Report IDs de entrada: {string.Join(", ", descriptor.InputReports.Select(report => $"0x{report.ReportID:X2} ({report.Length} bytes)"))}");
            }
            catch (Exception ex)
            {
                log.AppendLine($"Descritor HID indisponível: {ex.GetType().Name}: {ex.Message}");
            }

            if (inputLength <= 0)
            {
                log.AppendLine("Resumo: interface sem relatório de entrada.");
                return log.ToString();
            }

            using var stream = device.Open();
            stream.ReadTimeout = 250;
            var buffer = new byte[inputLength];
            var timer = Stopwatch.StartNew();
            var reportCount = 0;
            var uniqueReports = new HashSet<string>(StringComparer.Ordinal);
            const int outputLimit = 300;

            while (timer.Elapsed < duration && reportCount < 2000)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var count = stream.Read(buffer, 0, buffer.Length);
                    if (count <= 0) continue;
                    reportCount++;
                    var hex = Convert.ToHexString(buffer.AsSpan(0, count));
                    uniqueReports.Add(hex);
                    if (reportCount <= outputLimit)
                        log.AppendLine($"{timer.Elapsed.TotalMilliseconds,8:0} ms | {count,2} bytes | {hex}");
                }
                catch (TimeoutException) { }
            }

            log.AppendLine($"Resumo: {reportCount} relatório(s), {uniqueReports.Count} padrão(ões) distinto(s).");
            if (reportCount > outputLimit)
                log.AppendLine($"Saída limitada aos primeiros {outputLimit} relatórios.");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            log.AppendLine($"Falha ao abrir/ler em modo passivo: {ex.GetType().Name}: {ex.Message}");
        }

        return log.ToString();
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
                "USB HID/JieLi (3634:4155)", "Dongle detectado — não há consulta de bateria documentada; nenhum comando foi enviado", DateTimeOffset.Now));
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
