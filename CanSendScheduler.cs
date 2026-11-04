using System.Text.Json;

namespace IndustrialFieldDataAcquisitionTerminal;

internal sealed record CanSendTemplate(Guid Id, string Name, CanFrame Frame, int PeriodMs);
internal sealed class CanSendScheduler
{
    public const int MaximumTemplates = 64;
    public const int MinimumPeriodMs = 100;
    private readonly Dictionary<Guid, (CanSendTemplate Template, long Next)> running = new();
    public IReadOnlyCollection<Guid> RunningIds => running.Keys;
    public bool IsRunning(Guid id) => running.ContainsKey(id);
    public void Start(CanSendTemplate template, long now)
    {
        Validate(new[] { template });
        running[template.Id] = (template, checked(now + template.PeriodMs));
    }
    public void Stop(Guid id) => running.Remove(id);
    public void StopAll() => running.Clear();
    public IReadOnlyList<CanSendTemplate> Due(long now)
    {
        var due = new List<CanSendTemplate>();
        foreach (var id in running.Keys.ToArray())
        {
            var item = running[id];
            if (now < item.Next) continue;
            due.Add(item.Template);
            // Skip missed intervals after UI delays; never emit a burst to catch up.
            running[id] = (item.Template, checked(now + item.Template.PeriodMs));
        }
        return due;
    }
    public static void Validate(IReadOnlyCollection<CanSendTemplate> templates)
    {
        if (templates.Count > MaximumTemplates) throw new InvalidDataException("发送列表最多 64 条。");
        var ids = new HashSet<Guid>();
        foreach (var item in templates)
        {
            if (item is null || item.Id == Guid.Empty || !ids.Add(item.Id) || item.Name is null || item.Name.Length > 80 ||
                item.Frame is null || item.PeriodMs is < MinimumPeriodMs or > 3600000 ||
                item.Frame.DriverFlags != 0 || item.Frame.DeviceTimestampUs is not null || item.Frame.DriverStatus != 0)
                throw new InvalidDataException("发送列表包含无效项目；周期范围 100–3600000 ms。");
            item.Frame.ValidateForTransmit();
        }
    }
    public static void Save(string path, IReadOnlyCollection<CanSendTemplate> templates)
    {
        Validate(templates);
        var data = JsonSerializer.SerializeToUtf8Bytes(templates, new JsonSerializerOptions { WriteIndented = true });
        var full = Path.GetFullPath(path);
        var temp = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temp, data); File.Move(temp, full, overwrite: true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public static List<CanSendTemplate> Load(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > 1024 * 1024) throw new InvalidDataException("发送列表文件过大。");
        var items = JsonSerializer.Deserialize<List<CanSendTemplate>>(stream) ?? throw new InvalidDataException("发送列表为空。");
        Validate(items);
        return items;
    }
}
