using System.Security.Cryptography;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class Legacy03FixtureTests
{
    [Fact]
    public void Published03ProjectLoadsWithoutChangingItsBytes()
    {
        // Produced by the published 0.3 executable's hidden --verify run, not hand-authored.
        // Executable SHA256: 8EF2F959CEC50ED860A706995B5D71712382BC463E6740FEA553B78CAF7FFC10.
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        Assert.NotNull(root);
        var path = Path.Combine(root!.FullName, "tests", "TekstilCAD.Tests", "Fixtures", "studio-0.3-native.tcad");
        var before = File.ReadAllBytes(path);
        Assert.Equal("1F11E3C99FBE6222A2EB71353E62AA64FFF4B722DEDAB8C3957627E66EB7A5C3", Convert.ToHexString(SHA256.HashData(before)));
        var project = ProjectStorage.Load(path);
        Assert.Equal(2, project.Pieces.Count);
        Assert.True(project.Pieces[0].Closed);
        Assert.False(project.Pieces[1].Closed);
        Assert.Equal(0.123456789, project.Pieces[0].Points[0].X);
        Assert.Equal(0.987654321, project.Pieces[0].Points[0].Y);
        Assert.Empty(project.GradingRules);
        Assert.Equal(before, File.ReadAllBytes(path));
    }
}
