using TekstilCAD.Grading;

namespace TekstilCAD.Tests;

public class GradingRuleJsonUnknownFieldsTests
{
    [Theory]
    [InlineData("\"FutureSetting\":1,", "")]
    [InlineData("", ",\"FutureAxis\":1")]
    [InlineData("", ",\"IsFinite\":false")]
    [InlineData("", ",\"IsFinite\":true,\"IsFinite\":true")]
    public void RejectsFieldsThatWouldOtherwiseBeDiscardedOrContradictCoordinates(string rootField, string pointField)
    {
        var json = "{" + rootField + "\"Name\":\"R\",\"BaseSize\":\"M\",\"Increments\":[{\"X\":0,\"Y\":0" + pointField + "},{\"X\":1,\"Y\":2}]}";
        Assert.Throws<InvalidDataException>(() => GradeRule.FromJson(json));
    }

    [Fact]
    public void AcceptsValidOptionalComputedField()
    {
        var rule = GradeRule.FromJson("{\"Name\":\"R\",\"BaseSize\":\"M\",\"Increments\":[{\"X\":0,\"Y\":0,\"IsFinite\":true},{\"X\":1,\"Y\":2}]}");
        Assert.Equal(2, rule.Increments.Count);
    }
}
