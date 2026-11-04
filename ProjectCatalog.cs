using System.Text.Json;
using System.Text.Json.Nodes;

namespace IndustrialFieldDataAcquisitionTerminal;

internal enum ProjectHealth
{
    Ready,
    MissingDirectory,
    MissingSource,
    IdentityMismatch
}

internal sealed record ProjectReference(Guid Id, string Name, string Path, bool Removed = false);

internal sealed class ProjectCatalog
{
    private const string SourceFileName = "io_logic_user.c";
    private readonly string registryPath;
    private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public ProjectCatalog(string registryPath)
    {
        this.registryPath = Path.GetFullPath(registryPath);
    }

    public IReadOnlyList<ProjectReference> Load()
    {
        var entries = ReadRegistry(out var changed);
        var projectsRoot = Path.GetDirectoryName(registryPath)!;
        Directory.CreateDirectory(projectsRoot);
        foreach (var path in Directory.EnumerateDirectories(projectsRoot))
        {
            if (!File.Exists(Path.Combine(path, SourceFileName))) continue;
            var fullPath = Path.GetFullPath(path);
            if (entries.Any(entry => PathComparer.Equals(entry.Path, fullPath))) continue;
            var id = EnsureProjectId(fullPath);
            if (entries.Any(entry => entry.Id == id)) continue;
            entries.Add(new ProjectReference(id, Path.GetFileName(fullPath), fullPath));
            changed = true;
        }

        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            if (entry.Removed || Directory.Exists(entry.Path)) continue;
            var recoveredPath = FindMovedPath(entry, entries);
            if (recoveredPath is null) continue;
            entries[index] = entry with { Path = recoveredPath, Name = Path.GetFileName(recoveredPath) };
            changed = true;
        }

        if (changed)
        {
            if (File.Exists(registryPath) && !File.Exists(registryPath + ".bak"))
                File.Copy(registryPath, registryPath + ".bak");
            SaveRegistry(entries);
        }
        return entries.Where(entry => !entry.Removed).ToArray();
    }

    public IReadOnlyList<ProjectReference> LoadLocalOnly()
    {
        var projectsRoot = Path.GetDirectoryName(registryPath)!;
        if (!Directory.Exists(projectsRoot)) return Array.Empty<ProjectReference>();
        var local = new List<ProjectReference>();
        foreach (var path in Directory.EnumerateDirectories(projectsRoot))
        {
            if (!File.Exists(Path.Combine(path, SourceFileName))) continue;
            try { local.Add(new ProjectReference(EnsureProjectId(path), Path.GetFileName(path), Path.GetFullPath(path))); }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException) { }
        }
        return local;
    }

    public ProjectReference Register(string projectRoot)
    {
        var path = Path.GetFullPath(projectRoot);
        if (!File.Exists(Path.Combine(path, SourceFileName)))
            throw new InvalidOperationException($"工程根目录必须包含 {SourceFileName}。");

        var id = EnsureProjectId(path);
        var entries = ReadRegistry(out _);
        var existing = entries.FindIndex(entry => entry.Id == id || PathComparer.Equals(entry.Path, path));
        if (existing >= 0)
        {
            var previous = entries[existing];
            if (previous.Id != id || (!previous.Removed && Directory.Exists(previous.Path) && !PathComparer.Equals(previous.Path, path)))
                throw new InvalidOperationException("检测到重复或冲突的工程身份，请先处理原工程引用。");
            entries[existing] = new ProjectReference(id, Path.GetFileName(path), path);
        }
        else
        {
            entries.Add(new ProjectReference(id, Path.GetFileName(path), path));
        }

        SaveRegistry(entries);
        return entries[existing >= 0 ? existing : entries.Count - 1];
    }

    public ProjectHealth Inspect(ProjectReference entry)
    {
        if (!Directory.Exists(entry.Path)) return ProjectHealth.MissingDirectory;
        try
        {
            if (ReadProjectId(entry.Path) != entry.Id) return ProjectHealth.IdentityMismatch;
        }
        catch (IOException) { return ProjectHealth.IdentityMismatch; }
        catch (UnauthorizedAccessException) { return ProjectHealth.IdentityMismatch; }
        return File.Exists(Path.Combine(entry.Path, SourceFileName)) ? ProjectHealth.Ready : ProjectHealth.MissingSource;
    }

    public bool CanBindLegacyProject(string folder)
    {
        var path = Path.GetFullPath(folder);
        if (!File.Exists(Path.Combine(path, SourceFileName))) return false;
        var metadataPath = Path.Combine(path, "project.json");
        if (!File.Exists(metadataPath)) return true;
        try
        {
            return JsonNode.Parse(File.ReadAllText(metadataPath)) is JsonObject metadata
                && !metadata.ContainsKey("projectId");
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return false;
        }
    }

    public bool TryRelocate(Guid id, string folder, out string reason, bool allowLegacyBinding = false)
    {
        var path = Path.GetFullPath(folder);
        var entries = ReadRegistry(out _);
        var index = entries.FindIndex(entry => entry.Id == id && !entry.Removed);
        if (index < 0) { reason = "工程引用不存在。"; return false; }
        if (!Directory.Exists(path)) { reason = "所选目录不存在。"; return false; }
        if (!File.Exists(Path.Combine(path, SourceFileName))) { reason = $"所选目录缺少 {SourceFileName}。"; return false; }
        var candidateId = ReadProjectId(path);
        if (candidateId != id && (candidateId is not null || !allowLegacyBinding || !CanBindLegacyProject(path)))
        {
            reason = candidateId is null ? "所选旧工程没有工程 ID，需要确认后绑定。" : "所选目录的工程 ID 与原工程不一致。";
            return false;
        }
        if (entries.Any(entry => entry.Id != id && !entry.Removed && PathComparer.Equals(entry.Path, path)))
        {
            reason = "该目录已属于另一个工程。";
            return false;
        }

        if (candidateId is null) BindLegacyProjectId(path, id);
        entries[index] = entries[index] with { Path = path, Name = Path.GetFileName(path) };
        SaveRegistry(entries);
        reason = string.Empty;
        return true;
    }

    public void Remove(Guid id)
    {
        var entries = ReadRegistry(out _);
        var index = entries.FindIndex(entry => entry.Id == id && !entry.Removed);
        if (index < 0) return;
        entries[index] = entries[index] with { Removed = true };
        SaveRegistry(entries);
    }

    private List<ProjectReference> ReadRegistry(out bool changed)
    {
        changed = false;
        if (!File.Exists(registryPath)) return new List<ProjectReference>();
        using var document = JsonDocument.Parse(File.ReadAllText(registryPath));
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("工程列表格式无效。");

        var entries = new List<ProjectReference>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var legacyPath = Path.GetFullPath(item.GetString()!);
                var legacyId = Directory.Exists(legacyPath) ? EnsureProjectId(legacyPath) : Guid.NewGuid();
                entries.Add(new ProjectReference(legacyId, Path.GetFileName(legacyPath), legacyPath));
                changed = true;
                continue;
            }

            if (item.ValueKind != JsonValueKind.Object) continue;
            var pathText = item.GetProperty("path").GetString();
            if (string.IsNullOrWhiteSpace(pathText)) continue;
            var path = Path.GetFullPath(pathText);
            var id = item.TryGetProperty("id", out var idValue) && Guid.TryParse(idValue.GetString(), out var parsed)
                ? parsed : Guid.NewGuid();
            var name = item.TryGetProperty("name", out var nameValue) ? nameValue.GetString() : null;
            var removed = item.TryGetProperty("removed", out var removedValue) && removedValue.ValueKind == JsonValueKind.True;
            entries.Add(new ProjectReference(id, string.IsNullOrWhiteSpace(name) ? Path.GetFileName(path) : name, path, removed));
        }

        return entries;
    }

    private void SaveRegistry(List<ProjectReference> entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(registryPath)!);
        WriteAtomic(registryPath, JsonSerializer.Serialize(entries, JsonOptions));
    }

    private static string? FindMovedPath(ProjectReference entry, List<ProjectReference> entries)
    {
        var parent = Path.GetDirectoryName(entry.Path);
        if (parent is null || !Directory.Exists(parent)) return null;
        var matches = Directory.EnumerateDirectories(parent)
            .Where(path => ReadProjectId(path) == entry.Id && File.Exists(Path.Combine(path, SourceFileName)))
            .Where(path => !entries.Any(other => other.Id != entry.Id && !other.Removed && PathComparer.Equals(other.Path, path)))
            .Take(2)
            .ToArray();
        return matches.Length == 1 ? Path.GetFullPath(matches[0]) : null;
    }

    private static Guid EnsureProjectId(string projectRoot)
    {
        var metadataPath = Path.Combine(projectRoot, "project.json");
        var metadata = File.Exists(metadataPath)
            ? JsonNode.Parse(File.ReadAllText(metadataPath)) as JsonObject
            : new JsonObject();
        if (metadata is null) throw new InvalidDataException($"工程元数据格式无效：{metadataPath}");
        if (metadata.ContainsKey("projectId"))
        {
            if (Guid.TryParse(metadata["projectId"]?.ToString(), out var existing)) return existing;
            throw new InvalidDataException($"工程 ID 无效：{metadataPath}");
        }
        var id = Guid.NewGuid();
        metadata["projectId"] = id.ToString("D");
        metadata["projectName"] ??= Path.GetFileName(projectRoot);
        metadata["sourceFile"] ??= SourceFileName;
        WriteAtomic(metadataPath, metadata.ToJsonString(JsonOptions));
        return id;
    }

    private static void BindLegacyProjectId(string projectRoot, Guid id)
    {
        var metadataPath = Path.Combine(projectRoot, "project.json");
        var metadata = File.Exists(metadataPath)
            ? JsonNode.Parse(File.ReadAllText(metadataPath)) as JsonObject
            : new JsonObject();
        if (metadata is null || metadata.ContainsKey("projectId"))
            throw new InvalidDataException($"工程元数据身份冲突：{metadataPath}");
        metadata["projectId"] = id.ToString("D");
        metadata["projectName"] ??= Path.GetFileName(projectRoot);
        metadata["sourceFile"] ??= SourceFileName;
        WriteAtomic(metadataPath, metadata.ToJsonString(JsonOptions));
    }

    private static Guid? ReadProjectId(string projectRoot)
    {
        var metadataPath = Path.Combine(projectRoot, "project.json");
        if (!File.Exists(metadataPath)) return null;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(metadataPath));
            return document.RootElement.TryGetProperty("projectId", out var value)
                && value.ValueKind == JsonValueKind.String && Guid.TryParse(value.GetString(), out var id)
                ? id : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void WriteAtomic(string path, string contents)
    {
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, contents);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}
