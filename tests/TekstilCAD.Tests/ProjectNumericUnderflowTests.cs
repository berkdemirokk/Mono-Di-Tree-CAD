using System.Text;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class ProjectNumericUnderflowTests
{
    private const string Id = "39c83207-a423-4915-96d9-37c5e871bb5d";
    private static string Project(string pieceX = "0", string sourceX = "0", string deltaX = "0", string grainX = "0", string? nesting = null) =>
        "[4,{\"Name\":\"test\",\"Pieces\":[{\"Id\":\"" + Id + "\",\"Name\":\"p\",\"Closed\":true," +
        "\"Points\":[{\"X\":" + pieceX + ",\"Y\":0},{\"X\":10,\"Y\":0},{\"X\":0,\"Y\":10}]," +
        "\"Grainline\":{\"Start\":{\"X\":" + grainX + ",\"Y\":0},\"End\":{\"X\":1,\"Y\":1},\"NeedsReview\":false}}]," +
        "\"GradingRules\":[{\"SourcePieceId\":\"" + Id + "\",\"SourceClosed\":true," +
        "\"SourcePoints\":[{\"X\":" + sourceX + ",\"Y\":0},{\"X\":10,\"Y\":0},{\"X\":0,\"Y\":10}]," +
        "\"Deltas\":[{\"X\":" + deltaX + ",\"Y\":0},{\"X\":0,\"Y\":0},{\"X\":0,\"Y\":0}]}]" + nesting + "}]";

    private static void Read(string json) => ProjectStorage.DeserializeProject(Encoding.UTF8.GetBytes(json));

    [Theory]
    [InlineData("1e-400")]
    [InlineData("-1e-400")]
    public void RejectsNonzeroValuesThatWouldBecomeZero(string value)
    {
        Read(Project());
        foreach (var json in new[] { Project(pieceX: value), Project(sourceX: value), Project(deltaX: value), Project(grainX: value) })
        {
            var error = Assert.Throws<InvalidDataException>(() => Read(json));
            Assert.Contains("sıfıra yuvarlanacak", error.Message);
        }
        var nested = Assert.Throws<InvalidDataException>(() => Read(Project(nesting: $",\"NestingSnapshot\":{{\"Gap\":{value}}}")));
        Assert.Contains("sıfıra yuvarlanacak", nested.Message);
    }

    [Fact]
    public void AcceptsActualZeroAndRepresentableSubnormal()
    {
        Read(Project(pieceX: "0e-400", sourceX: "-0.0e+999", deltaX: "5e-324", grainX: "0"));
    }

    [Fact]
    public void RecoveryPendingCoordinateCannotSilentlyUnderflow()
    {
        var document = new ProjectDocument { Pieces = [new PatternPiece { Points = [new(0, 0), new(10, 0), new(0, 10)] }] };
        var pending = new RecoveryPendingDrawing("Çizgi", [new(123.456, 1)]);
        var original = RecoveryEnvelope.Serialize(document, [], pending);
        var corrupted = original.Replace("\"X\":123.456", "\"X\":1e-400", StringComparison.Ordinal);
        Assert.NotEqual(original, corrupted);
        var error = Assert.Throws<InvalidDataException>(() => RecoveryEnvelope.Deserialize(Encoding.UTF8.GetBytes(corrupted)));
        Assert.Contains("sıfıra yuvarlanacak", error.Message);
    }
}
