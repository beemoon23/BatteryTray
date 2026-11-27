namespace BatteryTray;

public sealed record DeviceBattery(string Id, string Name, int? Percent, string Source, string Status, DateTimeOffset UpdatedAt);
