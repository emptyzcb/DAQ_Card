using IndustrialFieldDataAcquisitionTerminal;

var root = Path.Combine(Path.GetTempPath(), "daq-project-catalog-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var registry = Path.Combine(root, "Projects", "project_locations.json");
    Directory.CreateDirectory(Path.GetDirectoryName(registry)!);
    var external = Path.Combine(root, "Example");
    Directory.CreateDirectory(external);
    File.WriteAllText(Path.Combine(external, "io_logic_user.c"), "void IO_Logic_Main(void) {}\n");
    File.WriteAllText(registry, System.Text.Json.JsonSerializer.Serialize(new[] { external }));

    var catalog = new ProjectCatalog(registry);
    var migrated = catalog.Load();
    Check(migrated.Count == 1 && migrated[0].Id != Guid.Empty, "Legacy registry migration failed.");
    Check(File.Exists(registry + ".bak"), "Legacy registry was not backed up before migration.");
    Check(catalog.Inspect(migrated[0]) == ProjectHealth.Ready, "Migrated project is not ready.");
    var id = migrated[0].Id;

    var renamed = Path.Combine(root, "Renamed");
    Directory.Move(external, renamed);
    var recovered = catalog.Load();
    Check(recovered.Count == 1 && recovered[0].Id == id && recovered[0].Path == renamed, "External rename was not recovered by project ID.");
    Check(catalog.Inspect(recovered[0]) == ProjectHealth.Ready, "Recovered project is not ready.");

    var source = Path.Combine(renamed, "io_logic_user.c");
    File.Move(source, Path.Combine(renamed, "renamed.c"));
    Check(catalog.Inspect(recovered[0]) == ProjectHealth.MissingSource, "Missing source was not detected.");
    Check(catalog.Load().Count == 1, "Missing-source project was removed from the list.");
    File.Move(Path.Combine(renamed, "renamed.c"), source);

    var relocated = Path.Combine(root, "Other", "Moved");
    Directory.CreateDirectory(Path.GetDirectoryName(relocated)!);
    Directory.Move(renamed, relocated);
    Check(catalog.Inspect(recovered[0]) == ProjectHealth.MissingDirectory, "Missing directory was not detected.");
    Check(catalog.TryRelocate(id, relocated, out var reason), $"Manual relocation failed: {reason}");
    Check(catalog.Load().Single().Path == relocated, "Relocated path was not persisted.");

    var other = Path.Combine(root, "Other", "Unrelated");
    Directory.CreateDirectory(other);
    File.WriteAllText(Path.Combine(other, "io_logic_user.c"), "other");
    Check(!catalog.TryRelocate(id, other, out _), "Different project identity was accepted.");
    catalog.Register(other);
    Check(!catalog.TryRelocate(id, other, out _, allowLegacyBinding: true), "Another identified project was rebound as the original.");

    var legacyOld = Path.Combine(root, "LegacyOld");
    File.WriteAllText(registry, System.Text.Json.JsonSerializer.Serialize(new[] { legacyOld }));
    var legacyReference = catalog.Load().Single();
    var legacyRenamed = Path.Combine(root, "LegacyRenamed");
    Directory.CreateDirectory(legacyRenamed);
    File.WriteAllText(Path.Combine(legacyRenamed, "io_logic_user.c"), "legacy");
    File.WriteAllText(Path.Combine(legacyRenamed, "project.json"), "{\"projectName\":\"LegacyOld\"}");
    Check(!catalog.TryRelocate(legacyReference.Id, legacyRenamed, out _), "Unidentified project was bound without confirmation.");
    Check(catalog.TryRelocate(legacyReference.Id, legacyRenamed, out reason, allowLegacyBinding: true), $"Confirmed legacy binding failed: {reason}");
    Check(catalog.Inspect(catalog.Load().Single()) == ProjectHealth.Ready, "Legacy project ID was not persisted.");

    catalog.Remove(legacyReference.Id);
    Check(catalog.Load().Count == 0, "Removed reference remained in the registry.");
    Check(File.Exists(Path.Combine(relocated, "io_logic_user.c")), "Removing reference deleted project files.");

    var local = Path.Combine(root, "Projects", "LocalProject");
    Directory.CreateDirectory(local);
    File.WriteAllText(Path.Combine(local, "io_logic_user.c"), "local");
    var discovered = catalog.Load().Single();
    catalog.Remove(discovered.Id);
    Check(catalog.Load().Count == 0, "A removed local project was rediscovered.");
    Check(File.Exists(Path.Combine(local, "io_logic_user.c")), "Removing a local reference deleted source files.");

    Console.WriteLine("Project catalog smoke passed.");
}
finally
{
    var target = Path.GetFullPath(root);
    var temp = Path.GetFullPath(Path.GetTempPath());
    if (!target.StartsWith(temp, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(target).StartsWith("daq-project-catalog-", StringComparison.Ordinal))
        throw new InvalidOperationException("Catalog probe cleanup escaped the temporary directory.");
    if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
}

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
