using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class IphoneALineFixtureTests
{
    [Fact]
    public void SyntheticIphoneElevenMeasurementsMatchDesktopReviewContract()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Apple", "project.yml"))) root = root.Parent;
        Assert.NotNull(root);
        var path = Path.Combine(root.FullName, "Apple", "Tests", "Fixtures", "ios-aline-11.tcad-request.json");
        var bytes = File.ReadAllText(path);
        var request = DesignRequestStorage.Deserialize(bytes);
        Assert.Equal("aline", request.ModelId);
        var review = ALineRequestMeasurements.Review(request.Prompt, request.Measurements);
        Assert.True(review.Complete, string.Join(" | ", review.Issues));
        Assert.Equal(new decimal?[] { 360, 360, 500, 500, 20, 20, 30, 30, 200, 600, 1800 }, review.Millimeters);
    }
}
