using TekstilCAD.Core;
using TekstilCAD.Grading;

namespace TekstilCAD.Tests;

public class GradingUnicodeIntegrityTests
{
    [Fact]
    public void UnpairedSurrogatesAreRejectedBeforeRuleSerialization()
    {
        var rule = new GradeRule { Name = "\uD800", Increments = [new(0, 0), new(1, 1)] };
        Assert.Throws<InvalidDataException>(() => rule.ToJson());
        rule.Name = "Kural";
        rule.BaseSize = "\uDC00";
        Assert.Throws<InvalidDataException>(() => rule.ToJson());
    }

    [Fact]
    public void GeneratedNamesRejectUnpairedSurrogatesAndAcceptValidPairs()
    {
        var source = new PatternPiece { Name = "Ön", Points = [new(0, 0), new(10, 0), new(0, 10)] };
        Point2[] increments = [new(0, 0), new(0, 0), new(0, 0)];
        Assert.Throws<ArgumentException>(() => GradingEngine.Generate(source, increments, 1, "\uD800"));
        source.Name = "\uDC00";
        Assert.Throws<ArgumentException>(() => GradingEngine.Generate(source, increments, 1, "L"));
        source.Name = "Ön";
        Assert.Equal("Ön · 😀", GradingEngine.Generate(source, increments, 1, "😀").Name);
    }
}
