using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("WorkbenchProjectSmoke")]

namespace IndustrialFieldDataAcquisitionTerminal;

internal sealed record WorkbenchDevice(DeviceProfile Profile, List<Rs485SensorConfig> Sensors, List<EngineeringChannel>? Engineering = null);
internal sealed record WorkbenchTrend(List<string> Channels, int WindowIndex, double? TimeWindowSeconds, string? ExpandedUnit);
internal sealed record WorkbenchCode(string Name, Dictionary<string, string> Files);
internal sealed record WorkbenchDocument(int Version, string Name, List<WorkbenchDevice> Devices,
    Guid? ActiveDeviceId, bool DeviceWorkspaceOpen, int PageIndex, WorkbenchTrend Trend,
    WorkbenchCode Code, bool LogVisible);
internal sealed record WorkbenchFile(WorkbenchDocument Document, string Revision);

// One bounded, portable document; no external paths or executable payloads are followed on load.
internal static class WorkbenchProject
{
    public const int Version = 1;
    public const int MaximumBytes = 16 * 1024 * 1024;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static WorkbenchFile Load(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaximumBytes) throw new InvalidDataException("工程文件不能超过 16 MB。");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var bytes = buffer.ToArray();
        var document = JsonSerializer.Deserialize<WorkbenchDocument>(bytes, Options)
            ?? throw new InvalidDataException("工程内容为空。");
        Validate(document);
        return new(document, Hash(bytes));
    }

    public static string Save(string path, WorkbenchDocument document, string? expectedRevision)
    {
        Validate(document);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document, Options);
        if (bytes.Length > MaximumBytes) throw new InvalidDataException("工程内容不能超过 16 MB。");
        path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var mutex = new Mutex(false, "Local\\WorkbenchProject-" + Hash(Encoding.UTF8.GetBytes(path.ToUpperInvariant())));
        if (!mutex.WaitOne(TimeSpan.FromSeconds(3))) throw new IOException("工程正在被另一个窗口保存。");
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            if (expectedRevision is not null && (!File.Exists(path) || Load(path).Revision != expectedRevision))
                throw new IOException("工程文件已在外部修改或删除，请重新打开，或选择“另存工程”。");
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(bytes); stream.Flush(flushToDisk: true); }
            if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
            else File.Move(temporary, path);
            return Hash(bytes);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            mutex.ReleaseMutex();
        }
    }

    public static void Validate(WorkbenchDocument document)
    {
        if (document.Version != Version) throw new InvalidDataException("不支持此工程版本，请使用对应版本的设备工作台。");
        if (string.IsNullOrWhiteSpace(document.Name) || document.Name.Length > 100) throw new InvalidDataException("工程名称无效。");
        if (document.Devices is null || document.Devices.Any(d => d is null || d.Profile is null || d.Sensors is null))
            throw new InvalidDataException("设备配置不完整。");
        DeviceCatalog.Validate(document.Devices.Select(d => d.Profile).ToArray());
        foreach (var device in document.Devices)
        {
            if (device.Sensors.Any(s => s is null || string.IsNullOrWhiteSpace(s.Name) || s.Name.Length > 100))
                throw new InvalidDataException("传感器名称无效。");
            Rs485ConfigurationProtocol.Validate(device.Sensors);
            EngineeringChannel.Validate(device.Engineering ?? new());
        }
        if (document.ActiveDeviceId is Guid id && !document.Devices.Any(d => d.Profile.Id == id))
            throw new InvalidDataException("当前设备不在工程中。");
        if (document.PageIndex is < 0 or > 9 || document.DeviceWorkspaceOpen && document.ActiveDeviceId is null ||
            document.PageIndex < 4 && (!document.DeviceWorkspaceOpen || document.ActiveDeviceId is null))
            throw new InvalidDataException("工程页面状态无效。");
        if (document.Trend is null || document.Trend.WindowIndex is < 0 or > 2 || document.Trend.Channels is null ||
            document.Trend.Channels.Count > 1024 || document.Trend.Channels.Any(k => string.IsNullOrWhiteSpace(k) || k.Length > 80) ||
            document.Trend.ExpandedUnit?.Length > 40 || document.Trend.TimeWindowSeconds is double seconds &&
            (!double.IsFinite(seconds) || seconds < 0.1 || seconds > new[] { 30, 120, 600 }[document.Trend.WindowIndex]))
            throw new InvalidDataException("趋势显示配置无效。");
        if (document.Code is null || string.IsNullOrWhiteSpace(document.Code.Name) || document.Code.Name.Length > 100 ||
            document.Code.Files is null || document.Code.Files.Count is < 1 or > 256 || !document.Code.Files.ContainsKey("io_logic_user.c"))
            throw new InvalidDataException("工程缺少联合控制器入口文件。");
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var (path, content) in document.Code.Files)
        {
            ValidateRelativePath(path);
            if (!paths.Add(path) || content is null || content.Length > 2 * 1024 * 1024)
                throw new InvalidDataException("工程文件重复、为空或过大。");
            total += Encoding.UTF8.GetByteCount(content);
        }
        if (total > MaximumBytes) throw new InvalidDataException("工程源码总大小超过限制。");
    }

    public static string MaterializeCode(WorkbenchCode code, string parent)
    {
        // Never use a project-supplied name as a filesystem path; every open gets its own copy.
        var name = new string(code.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray()).Trim(' ', '.');
        if (name.Length == 0 || System.Text.RegularExpressions.Regex.IsMatch(name, @"^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])($|\.)", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) name = "控制程序";
        var root = Path.Combine(Path.GetFullPath(parent), Guid.NewGuid().ToString("N"), name);
        Directory.CreateDirectory(root);
        foreach (var (relative, text) in code.Files)
        {
            ValidateRelativePath(relative);
            var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text, new UTF8Encoding(false));
        }
        return root;
    }

    private static void ValidateRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 180 || path.Contains('\\') || Path.IsPathRooted(path) ||
            path.Split('/').Any(part => part.Length == 0 || part is "." or ".." || part.EndsWith('.') || part.EndsWith(' ') ||
                part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                System.Text.RegularExpressions.Regex.IsMatch(part, @"^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])($|\.)", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) ||
            Path.GetExtension(path).ToLowerInvariant() is not (".c" or ".h" or ".txt"))
            throw new InvalidDataException("工程包含不允许的文件路径。");
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
