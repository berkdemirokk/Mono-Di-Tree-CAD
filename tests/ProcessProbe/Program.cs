using TekstilCAD.Core;
using TekstilCAD.FileFormats;

if (args.Length == 7 && args[0] == "plot-export-race")
{
    var version = ProjectStorage.CaptureVersion(args[2]);
    var coordinate = args[3] == "First" ? 10 : args[3] == "Second" ? 20 : 0;
    if (coordinate == 0 || args[1] is not ("svg" or "hpgl")) return 2;
    var project = new ProjectDocument { Pieces = [new PatternPiece { Name = args[3], Closed = false, Points = [new(0, 0), new(coordinate, 0)] }] };
    File.WriteAllText(args[4], version.Sha256 ?? "missing");
    var deadline = DateTime.UtcNow.AddSeconds(30);
    while (!File.Exists(args[5]))
    {
        if (DateTime.UtcNow >= deadline) return 5;
        Thread.Sleep(10);
    }
    try
    {
        if (args[1] == "svg") SvgFormat.Export(project, args[2], version);
        else HpglFormat.Export(project, args[2], 40, version);
        File.WriteAllText(args[6], "saved|" + args[3]);
    }
    catch (ProjectConflictException) { File.WriteAllText(args[6], "conflict|" + args[3]); }
    return 0;
}
if (args.Length == 6 && args[0] == "dxf-export-race")
{
    var version = ProjectStorage.CaptureVersion(args[1]);
    var coordinate = args[2] == "First" ? 10 : args[2] == "Second" ? 20 : 0;
    if (coordinate == 0) return 2;
    var project = new ProjectDocument { Pieces = [new PatternPiece { Name = args[2], Closed = false, Points = [new(0, 0), new(coordinate, 0)] }] };
    File.WriteAllText(args[3], version.Sha256 ?? "missing");
    var deadline = DateTime.UtcNow.AddSeconds(30);
    while (!File.Exists(args[4]))
    {
        if (DateTime.UtcNow >= deadline) return 5;
        Thread.Sleep(10);
    }
    try { DxfFormat.Export(project, args[1], version); File.WriteAllText(args[5], "saved|" + args[2]); }
    catch (ProjectConflictException) { File.WriteAllText(args[5], "conflict|" + args[2]); }
    return 0;
}
if (args.Length == 4 && args[0] == "dxf-hold-writer")
{
    using var writer = new FileStream(args[1], FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
    File.WriteAllText(args[2], "ready");
    var deadline = DateTime.UtcNow.AddSeconds(30);
    while (!File.Exists(args[3]))
    {
        if (DateTime.UtcNow >= deadline) return 5;
        Thread.Sleep(10);
    }
    return 0;
}
if (args.Length == 2 && args[0] == "dxf-import")
{
    try { var result = DxfFormat.Import(args[1]); Console.WriteLine("imported|" + result.Project.Pieces.Count); return 0; }
    catch (IOException) { Console.WriteLine("io-error"); return 3; }
}
if (args.Length == 2 && args[0] == "save-init")
{
    ProjectStorage.Save(new ProjectDocument { Name = "Initial" }, args[1]);
    return 0;
}
if (args.Length == 6 && args[0] == "save-race")
{
    var loaded = ProjectStorage.LoadWithVersion(args[1]);
    loaded.Project.Name = args[2];
    File.WriteAllText(args[3], loaded.Version.Sha256 ?? "missing");
    var deadline = DateTime.UtcNow.AddSeconds(30);
    while (!File.Exists(args[4]))
    {
        if (DateTime.UtcNow >= deadline) return 5;
        Thread.Sleep(10);
    }
    try
    {
        ProjectStorage.SaveIfUnchanged(loaded.Project, args[1], loaded.Version);
        File.WriteAllText(args[5], "saved|" + loaded.Project.Name);
    }
    catch (ProjectConflictException)
    {
        File.WriteAllText(args[5], "conflict|" + loaded.Project.Name);
    }
    return 0;
}
if (args.Length == 2 && args[0] == "scan")
{
    var scan = RecoveryStore.ScanDirectory(args[1]);
    if (scan.Errors.Count != 0) return 4;
    foreach (var candidate in scan.Candidates)
        Console.WriteLine($"{candidate.ProjectName}|{candidate.IsRecoverable}|{candidate.IsLegacy}");
    return 0;
}
if (args.Length != 3 || args[0] != "hold") return 2;
using var store = new RecoveryStore(args[1]);
store.Save(new ProjectDocument { Name = "Process crash recovery" });
File.WriteAllText(args[2], store.RecoveryPath);
Console.WriteLine("ready");
Console.Out.Flush();
Thread.Sleep(Timeout.Infinite);
return 0;
