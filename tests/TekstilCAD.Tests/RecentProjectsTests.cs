using TekstilCAD.Core.Theme;

namespace TekstilCAD.Tests;

public class RecentProjectsTests
{
    [Fact]
    public void AddPutsANewPathFirst()
    {
        Assert.Equal(["C:\\c.tcad", "C:\\a.tcad", "C:\\b.tcad"], RecentProjects.Add(["C:\\a.tcad", "C:\\b.tcad"], "C:\\c.tcad"));
    }

    [Fact]
    public void AddMovesAnExistingPathToTheFrontWithoutRegardToCase()
    {
        Assert.Equal(["C:\\B.TCAD", "C:\\a.tcad"], RecentProjects.Add(["C:\\a.tcad", "C:\\b.tcad"], "C:\\B.TCAD"));
    }

    [Fact]
    public void AddKeepsAtMostTheMaximum()
    {
        var current = Enumerable.Range(0, UiPreferences.MaximumRecent).Select(i => "C:\\p" + i + ".tcad").ToArray();
        var result = RecentProjects.Add(current, "C:\\new.tcad");
        Assert.Equal(UiPreferences.MaximumRecent, result.Count);
        Assert.Equal("C:\\new.tcad", result[0]);
        Assert.DoesNotContain("C:\\p" + (UiPreferences.MaximumRecent - 1) + ".tcad", result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AddIgnoresBlankPaths(string path)
    {
        Assert.Equal(["C:\\a.tcad"], RecentProjects.Add(["C:\\a.tcad"], path));
    }

    [Fact]
    public void RemoveDropsAMissingFile()
    {
        Assert.Equal(["C:\\b.tcad"], RecentProjects.Remove(["C:\\a.tcad", "C:\\b.tcad"], "C:\\A.tcad"));
    }

    [Fact]
    public void NormalizeDropsBlankDuplicateAndOverlongEntries()
    {
        var result = RecentProjects.Normalize(["", "C:\\a.tcad", "c:\\A.TCAD", " ", new string('x', 600), "C:\\b.tcad"]);
        Assert.Equal(["C:\\a.tcad", "C:\\b.tcad"], result);
    }
}
