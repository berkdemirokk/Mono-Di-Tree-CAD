using System.IO;

namespace TekstilCAD.Desktop;

/// <summary>Runtime data is independent of the source/install directory.</summary>
internal static class AppPaths
{
    private static string? verificationDirectory;
#if TEKSTILCAD_ISOLATED_PREVIEW
    internal const bool IsIsolatedPreview = true;
    private const string DataFolderName = "Studio-Preview-v4";
#else
    internal const bool IsIsolatedPreview = false;
    private const string DataFolderName = "Studio";
#endif
    public static string DataDirectory => verificationDirectory ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TekstilCAD", DataFolderName);
    public static string RecoveryDirectory => Path.Combine(DataDirectory, "Recovery");
    public static string MachineSpoolDirectory => Path.Combine(DataDirectory, "MachineSpool");
    public static string ProjectsDirectory => Path.Combine(DataDirectory, "Projects");
    public static string LogsDirectory => Path.Combine(DataDirectory, "Logs");
    public static string PrepareProjectsDirectory()
    {
        TekstilCAD.FileFormats.ProjectStorage.EnsureWritableDestination(Path.Combine(ProjectsDirectory, "project.tcad"));
        Directory.CreateDirectory(ProjectsDirectory);
        return ProjectsDirectory;
    }
    internal static void UseVerificationDirectory(string path)
    {
        var full = Path.GetFullPath(path);
        var ancestor=new DirectoryInfo(full);
        while(ancestor is not null && !(File.Exists(Path.Combine(ancestor.FullName,"AGENTS.md")) && File.Exists(Path.Combine(ancestor.FullName,"TekstilCAD.slnx"))))ancestor=ancestor.Parent;
        if (ancestor is null || !full.StartsWith(Path.Combine(ancestor.FullName,"work")+Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Verification data must remain in the development workspace.");
        TekstilCAD.FileFormats.ProjectStorage.EnsureWritableDestination(Path.Combine(full,"verification.tcad"));
        verificationDirectory = full;
    }
}
