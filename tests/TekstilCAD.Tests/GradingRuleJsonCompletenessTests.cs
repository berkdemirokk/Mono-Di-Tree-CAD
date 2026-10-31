using TekstilCAD.Core;
using TekstilCAD.Grading;

namespace TekstilCAD.Tests;

public class GradingRuleJsonCompletenessTests
{
    [Theory]
    [InlineData("{\"Name\":\"R\",\"BaseSize\":\"M\",\"Increments\":[{\"X\":1},{\"X\":2,\"Y\":3}]}")]
    [InlineData("{\"Name\":\"R\",\"BaseSize\":\"M\",\"Increments\":[{\"Y\":1},{\"X\":2,\"Y\":3}]}")]
    [InlineData("{\"BaseSize\":\"M\",\"Increments\":[{\"X\":0,\"Y\":0},{\"X\":0,\"Y\":0}]}")]
    [InlineData("{\"Name\":\"R\",\"BaseSize\":\"M\",\"Increments\":[{\"X\":1,\"X\":2,\"Y\":0},{\"X\":0,\"Y\":0}]}")]
    public void IncompleteOrAmbiguousRulesAreRejected(string json)
        => Assert.Throws<InvalidDataException>(() => GradeRule.FromJson(json));

    [Theory]
    [InlineData("1e-10000")]
    [InlineData("-0.0001e-10000")]
    public void NonzeroCoordinateThatUnderflowsIsRejected(string value)
    {
        var json = "{\"Name\":\"R\",\"BaseSize\":\"M\",\"Increments\":[{\"X\":" + value + ",\"Y\":0},{\"X\":0,\"Y\":0}]}";
        Assert.Throws<InvalidDataException>(() => GradeRule.FromJson(json));
    }

    [Fact]
    public void ZeroWithLargeNegativeExponentIsAccepted()
    {
        var rule = GradeRule.FromJson("{\"Name\":\"R\",\"BaseSize\":\"M\",\"Increments\":[{\"X\":0e-10000,\"Y\":-0.0},{\"X\":0,\"Y\":0}]}");
        Assert.Equal(new Point2(0, 0), rule.Increments[0]);
    }

    [Fact]
    public void OversizedPointListIsRejectedBeforeIndividualEntriesAreRead()
    {
        var points = string.Join(',', Enumerable.Repeat("{}", Geometry.MaxValidatedContourVertices + 1));
        var json = "{\"Name\":\"R\",\"BaseSize\":\"M\",\"Increments\":[" + points + "]}";
        var error = Assert.Throws<InvalidDataException>(() => GradeRule.FromJson(json));
        Assert.Contains("nokta sayısı", error.Message);
    }

    [Fact]
    public void WrittenRuleStillRoundTrips()
    {
        var source = new GradeRule { Name = "Ön", BaseSize = "M", Increments = [new(0, 0), new(-1, 2)] };
        var result = GradeRule.FromJson(source.ToJson());
        Assert.Equal(source.Increments, result.Increments);
        Assert.Equal(source.BaseSize, result.BaseSize);
    }
}
