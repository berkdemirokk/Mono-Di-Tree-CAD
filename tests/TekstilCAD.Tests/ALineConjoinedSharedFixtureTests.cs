using System.Text.Json;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class ALineConjoinedSharedFixtureTests
{
    private sealed record Example(string Text, bool Valid);
    [Fact]
    public void SharedConjoinedSourcesUseMatchingDecision()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Apple", "project.yml"))) root = root.Parent;
        Assert.NotNull(root);
        var path = Path.Combine(root.FullName, "Apple", "Tests", "Fixtures", "aline-conjoined-sources.json");
        var examples = JsonSerializer.Deserialize<Example[]>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        foreach (var example in examples)
        {
            var original = example.Text;
            var review = ALineRequestMeasurements.Review(original, "");
            Assert.True(example.Valid == !review.Issues.Any(), original + " | " + string.Join(" | ", review.Issues));
        }
    }
}
