using IndustrialFieldDataAcquisitionTerminal;

static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
var folder = Path.Combine(Path.GetTempPath(), "device-catalog-" + Guid.NewGuid().ToString("N"));
try
{
    var path = Path.Combine(folder, "devices.json");
    var first = new DeviceCatalog(path);
    var second = new DeviceCatalog(path);
    var empty = first.Load();
    var stale = second.Load();
    var device = new DeviceProfile(Guid.NewGuid(), "车间采集板", DeviceCatalog.ProductModel, "192.168.1.10", 5000, 1, 250, "A 产线");
    var saved = first.Save(new[] { device }, empty.Revision);
    Check(saved.Devices.Count == 1 && first.Load().Devices.Single() == device, "round trip failed");
    var conflict = false;
    try { second.Save(Array.Empty<DeviceProfile>(), stale.Revision); } catch (IOException) { conflict = true; }
    Check(conflict && first.Load().Devices.Single() == device, "stale window overwrote newer data");
    var rejected = false;
    try { first.Save(new[] { device with { Port = 0 } }, saved.Revision); } catch (InvalidDataException) { rejected = true; }
    Check(rejected && first.Load().Devices.Single() == device, "invalid data modified the catalog");
    first.Save(new[] { device with { Port = 1502 } }, saved.Revision);
    Check(first.Load().Devices.Single().Port == 1502, "update failed");
    var legacy = device with { Type = "DAQ 采集板" };
    File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new[] { legacy }));
    var migrated = first.Load();
    Check(migrated.Devices.Single() == device && migrated.Revision != "missing", "legacy model migration must preserve identity and endpoint");
    File.WriteAllText(path, "{broken json");
    try { first.Load(); throw new Exception("corrupt file accepted"); } catch (System.Text.Json.JsonException) { }
    Check(File.ReadAllText(path) == "{broken json", "corrupt file must remain intact");
    Console.WriteLine("Device catalog persistence, validation and conflict tests passed.");
}
finally
{
    if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
}
