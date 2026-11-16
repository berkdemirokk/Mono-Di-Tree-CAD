using TekstilCAD.Core.Theme;

namespace TekstilCAD.Tests;

public class UiPreferencesTests
{
    private static string TemporaryFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "tekstilcad-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    [Fact]
    public void DefaultsAreTheLightSimpleView()
    {
        var defaults = new UiPreferences();
        Assert.Equal("simple", defaults.View);
        Assert.Equal("Light", defaults.Theme);
        Assert.Equal(100, defaults.TextScale);
        Assert.Empty(defaults.Recent);
        Assert.Empty(defaults.FirstSteps);
        Assert.Same(ThemePalette.Light, defaults.Palette);
    }

    [Fact]
    public void AValidFileIsReadBack()
    {
        var original = new UiPreferences { View = "simple", Theme = "Light", TextScale = 130, Recent = ["C:\\a.tcad", "C:\\b.tcad"], FirstSteps = ["draw", "save"] };
        var read = UiPreferences.Parse(original.ToJson());
        Assert.Equal("simple", read.View);
        Assert.Equal("Light", read.Theme);
        Assert.Equal(130, read.TextScale);
        Assert.Equal(["C:\\a.tcad", "C:\\b.tcad"], read.Recent);
        Assert.Equal(["draw", "save"], read.FirstSteps);
        Assert.Same(ThemePalette.Light, read.Palette);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{ not json")]
    [InlineData("[1,2,3]")]
    [InlineData("\"Light\"")]
    [InlineData("{\"theme\":\"Neon\",\"view\":\"gigantic\",\"textScale\":999}")]
    [InlineData("{\"theme\":5,\"view\":[],\"textScale\":\"big\",\"recent\":{},\"firstSteps\":7}")]
    public void UnusableTextGivesTheDefaults(string? json)
    {
        var read = UiPreferences.Parse(json);
        Assert.Equal("Light", read.Theme);
        Assert.Equal("simple", read.View);
        Assert.Equal(100, read.TextScale);
        Assert.Empty(read.Recent);
    }

    [Fact]
    public void ValidPartsSurviveWrongNeighbours()
    {
        var read = UiPreferences.Parse("{\"theme\":\"Light\",\"textScale\":\"huge\",\"view\":\"nope\",\"recent\":[\"C:\\\\x.tcad\",5,null]}");
        Assert.Equal("Light", read.Theme);
        Assert.Equal(100, read.TextScale);
        Assert.Equal("simple", read.View);
        Assert.Equal(["C:\\x.tcad"], read.Recent);
    }

    [Fact]
    public void OversizedTextIsIgnored()
    {
        var padding = new string(' ', UiPreferences.MaximumBytes);
        Assert.Equal("Light", UiPreferences.Parse("{\"schema\":2,\"theme\":\"Dark\"}" + padding).Theme);
    }

    [Fact]
    public void SaveWritesAtomicallyAndLoadReadsItBack()
    {
        var folder = TemporaryFolder();
        try
        {
            var path = Path.Combine(folder, "nested", UiPreferences.FileName);
            Assert.Equal("Light", UiPreferences.Load(path).Theme);
            Assert.True(new UiPreferences { Theme = "Light" }.Save(path));
            Assert.Equal("Light", UiPreferences.Load(path).Theme);
            Assert.True(new UiPreferences { Theme = "Dark", TextScale = 115 }.Save(path));
            var read = UiPreferences.Load(path);
            Assert.Equal("Dark", read.Theme);
            Assert.Equal(115, read.TextScale);
            Assert.Equal([path], Directory.GetFiles(Path.GetDirectoryName(path)!));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void ACorruptFileGivesDefaultsAndCanBeReplaced()
    {
        var folder = TemporaryFolder();
        try
        {
            var path = Path.Combine(folder, UiPreferences.FileName);
            File.WriteAllText(path, "\0\0 broken");
            Assert.Equal("Light", UiPreferences.Load(path).Theme);
            Assert.True(new UiPreferences { Theme = "Dark" }.Save(path));
            Assert.Equal("Dark", UiPreferences.Load(path).Theme);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void AnEarlierBuildsDefaultPairIsReadAsNeverChosen()
    {
        var read = UiPreferences.Parse("{\"view\":\"detailed\",\"theme\":\"Dark\",\"textScale\":115,\"recent\":[\"C:\\\\a.tcad\"],\"firstSteps\":[\"draw\"]}");
        Assert.Equal("simple", read.View);
        Assert.Equal("Light", read.Theme);
        Assert.Equal(115, read.TextScale);
        Assert.Equal(["C:\\a.tcad"], read.Recent);
        Assert.Equal(["draw"], read.FirstSteps);
    }

    [Theory]
    [InlineData("{\"view\":\"detailed\",\"theme\":\"Light\"}", "detailed", "Light")]
    [InlineData("{\"view\":\"simple\",\"theme\":\"Dark\"}", "simple", "Dark")]
    [InlineData("{\"schema\":1,\"view\":\"simple\",\"theme\":\"Light\"}", "simple", "Light")]
    public void AnEarlierFileThatIsNotTheDefaultPairKeepsItsValues(string json, string view, string theme)
    {
        var read = UiPreferences.Parse(json);
        Assert.Equal(view, read.View);
        Assert.Equal(theme, read.Theme);
    }

    [Fact]
    public void ADetailedDarkChoiceMadeNowIsKept()
    {
        var json = new UiPreferences { View = "detailed", Theme = "Dark" }.ToJson();
        Assert.Contains("\"schema\": 2", json);
        var read = UiPreferences.Parse(json);
        Assert.Equal("detailed", read.View);
        Assert.Equal("Dark", read.Theme);
    }

    [Fact]
    public void AMigratedFileKeepsTheUsersLaterChoiceThroughSaveAndLoad()
    {
        var folder = TemporaryFolder();
        try
        {
            var path = Path.Combine(folder, UiPreferences.FileName);
            File.WriteAllText(path, "{\"view\":\"detailed\",\"theme\":\"Dark\"}");
            Assert.Equal("simple", UiPreferences.Load(path).View);
            Assert.True((UiPreferences.Load(path) with { View = "detailed", Theme = "Dark" }).Save(path));
            var read = UiPreferences.Load(path);
            Assert.Equal("detailed", read.View);
            Assert.Equal("Dark", read.Theme);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void ASaveThatCannotSucceedReturnsFalse()
    {
        var folder = TemporaryFolder();
        try
        {
            var blocker = Path.Combine(folder, "file");
            File.WriteAllText(blocker, "x");
            Assert.False(new UiPreferences().Save(Path.Combine(blocker, "inside", UiPreferences.FileName)));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void ARecentListIsCappedAndDeduplicatedWhenRead()
    {
        var many = string.Join(",", Enumerable.Range(0, 20).Select(i => "\"C:\\\\p" + (i % 12) + ".tcad\""));
        var read = UiPreferences.Parse("{\"recent\":[" + many + "]}");
        Assert.Equal(UiPreferences.MaximumRecent, read.Recent.Count);
        Assert.Equal(read.Recent.Count, read.Recent.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}
