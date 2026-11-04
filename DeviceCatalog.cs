using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace IndustrialFieldDataAcquisitionTerminal;

internal sealed record DeviceProfile(Guid Id, string Name, string Type, string Host, int Port, byte UnitId, int PollIntervalMs, string Notes);
internal sealed record DeviceCatalogSnapshot(IReadOnlyList<DeviceProfile> Devices, string Revision);

internal sealed class DeviceCatalog
{
    public const string ProductModel = "DAQ-800";
    public const string ProductName = "多源采集控制器";
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly string path;
    public DeviceCatalog(string path) => this.path = Path.GetFullPath(path);

    public DeviceCatalogSnapshot Load()
    {
        if (!File.Exists(path)) return new(Array.Empty<DeviceProfile>(), "missing");
        var bytes = File.ReadAllBytes(path);
        var items = JsonSerializer.Deserialize<List<DeviceProfile>>(bytes, Options)
            ?? throw new InvalidDataException("设备档案内容为空或格式无效。");
        Validate(items);
        // Read legacy profiles without changing their identity or connection settings.
        return new(items.Select(item => item with { Type = ProductModel }).ToArray(), Revision(bytes));
    }

    public DeviceCatalogSnapshot Save(IEnumerable<DeviceProfile> profiles, string expectedRevision)
    {
        var items = profiles.ToArray();
        Validate(items);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // An external lock protects the revision check and replacement across workbench instances.
        using var mutex = new Mutex(false, "Local\\DeviceWorkbench-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path.ToUpperInvariant()))));
        if (!mutex.WaitOne(TimeSpan.FromSeconds(3))) throw new IOException("设备档案正在被另一个窗口使用，请稍后重试。");
        try
        {
            if (Load().Revision != expectedRevision)
                throw new IOException("设备档案已被另一个窗口修改，请重新加载后再保存。");
            var bytes = JsonSerializer.SerializeToUtf8Bytes(items, Options);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temporary, bytes);
                File.Move(temporary, path, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return new(items, Revision(bytes));
        }
        finally { mutex.ReleaseMutex(); }
    }

    private static string Revision(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    public static void Validate(IReadOnlyCollection<DeviceProfile> items)
    {
        if (items.Count > 500) throw new InvalidDataException("设备档案最多支持 500 项。");
        var ids = new HashSet<Guid>();
        foreach (var item in items)
        {
            if (item.Id == Guid.Empty || !ids.Add(item.Id)) throw new InvalidDataException("设备档案身份无效或重复。");
            if (string.IsNullOrWhiteSpace(item.Name) || item.Name.Length > 80) throw new InvalidDataException("设备名称长度应为 1–80 个字符。");
            if (item.Type is not ("DAQ 采集板" or ProductModel)) throw new InvalidDataException("当前版本仅支持 DAQ-800 多源采集控制器。");
            if (string.IsNullOrWhiteSpace(item.Host) || item.Host.Length > 253 || item.Host.Any(char.IsWhiteSpace)) throw new InvalidDataException("请输入有效的 IP 地址或主机名。");
            if (item.Port is < 1 or > 65535 || item.UnitId is < 1 or > 247 || item.PollIntervalMs is < 100 or > 60000)
                throw new InvalidDataException("端口、Unit ID 或轮询间隔超出范围。");
            if (item.Notes is null || item.Notes.Length > 500) throw new InvalidDataException("备注不能超过 500 个字符。");
        }
    }
}
