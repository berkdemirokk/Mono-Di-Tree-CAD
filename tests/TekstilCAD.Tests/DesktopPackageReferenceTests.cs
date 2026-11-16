using Xunit;

namespace TekstilCAD.Tests;

public class DesktopPackageReferenceTests
{
    private static string Root()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        Assert.NotNull(root);
        return root!.FullName;
    }

    // The voice button only points to Windows voice typing (Win + H); no speech engine and no other package may come with it.
    [Fact]
    public void TheDesktopProjectReferencesNoNewPackage()
    {
        var project = File.ReadAllText(Path.Combine(Root(), "src", "TekstilCAD.Desktop", "TekstilCAD.Desktop.csproj"));
        Assert.DoesNotContain("PackageReference", project, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sherpa", project, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Vosk", project, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheCommandBoxSourcesStartNoSpeechRecognitionAndPressNoKeys()
    {
        var source = File.ReadAllText(Path.Combine(Root(), "src", "TekstilCAD.Desktop", "CommandBarView.cs"));
        Assert.DoesNotContain("SendInput", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SpeechRecognition", source, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Speech", source, StringComparison.Ordinal);
        Assert.DoesNotContain("keybd_event", source, StringComparison.Ordinal);
    }
}
