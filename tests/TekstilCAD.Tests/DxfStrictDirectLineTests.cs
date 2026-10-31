using System.Text;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

/// <summary>End-to-end fixtures for model-space LINE beside real INSERT occurrences.</summary>
public sealed class DxfStrictDirectLineTests
{
    private static string Layer(string name, int flags = 0, int color = 7, int plot = 1) =>
        $"0\nLAYER\n2\n{name}\n70\n{flags}\n62\n{color}\n290\n{plot}\n6\nCONTINUOUS\n";

    private static string Line(string x1, string y1, string x2, string y2, string layer = "0", string extra = "") =>
        $"0\nLINE\n8\n{layer}\n10\n{x1}\n20\n{y1}\n11\n{x2}\n21\n{y2}\n{extra}";

    private static string Drawing(string direct, string extraLayer = "", string insert = "0\nINSERT\n2\nA\n8\n0\n10\n10\n20\n20\n") =>
        "0\nSECTION\n2\nTABLES\n0\nTABLE\n2\nLAYER\n" + Layer("0") + Layer("CUT") + extraLayer +
        "0\nENDTAB\n0\nENDSEC\n0\nSECTION\n2\nBLOCKS\n" +
        "0\nBLOCK\n2\nA\n10\n0\n20\n0\n30\n0\n" + Line("0", "0", "2", "0") +
        "0\nENDBLK\n0\nENDSEC\n0\nSECTION\n2\nENTITIES\n" + direct + insert +
        "0\nENDSEC\n0\nEOF\n";

    private static DxfImportResult Import(string drawing, string[]? approved = null, CancellationToken token = default)
    {
        var bytes = Encoding.UTF8.GetBytes(drawing);
        using var input = new MemoryStream(bytes, writable: false);
        try
        {
            return DxfFormat.ImportWithBlockPlacements(input, "model LINE", 1, DxfTextEncoding.Utf8,
                0, approved ?? ["0", "CUT"], token);
        }
        finally { Assert.Equal(bytes, input.ToArray()); }
    }

    [Fact]
    public void DirectLinesFormAClosedContourBesidePlacedBlock()
    {
        var direct = Line("0", "0", "2", "0", "CUT") + Line("0", "1", "2", "0", "CUT") + Line("0", "0", "0", "1", "CUT");
        var result = Import(Drawing(direct));
        Assert.Equal(2, result.Project.Pieces.Count);
        var triangle = Assert.Single(result.Project.Pieces, p => p.Closed);
        Assert.Equal(3, triangle.Points.Count);
        Assert.Contains(new Point2(0, 0), triangle.Points);
        Assert.Contains(new Point2(2, 0), triangle.Points);
        Assert.Contains(new Point2(0, 1), triangle.Points);
        var placed = Assert.Single(result.Project.Pieces, p => !p.Closed);
        Assert.Contains(new Point2(10, 20), placed.Points);
        Assert.Contains(new Point2(12, 20), placed.Points);
    }

    [Fact]
    public void DirectAndBlockLinesWithIdenticalEndpointsStaySeparate()
    {
        var result = Import(Drawing(Line("10", "20", "12", "20")));
        Assert.Equal(2, result.Project.Pieces.Count);
        Assert.All(result.Project.Pieces, p =>
        {
            Assert.False(p.Closed);
            Assert.Equal(2, p.Points.Count);
            Assert.Contains(new Point2(10, 20), p.Points);
            Assert.Contains(new Point2(12, 20), p.Points);
        });
        Assert.NotEqual(result.Project.Pieces[0].Id, result.Project.Pieces[1].Id);
    }

    [Fact]
    public void TouchingDirectLinesOnDifferentLayersDoNotJoin()
    {
        var direct = Line("0", "0", "1", "0", "0") + Line("1", "0", "2", "0", "CUT");
        var result = Import(Drawing(direct));
        Assert.Equal(3, result.Project.Pieces.Count);
        Assert.All(result.Project.Pieces, p => Assert.Equal(2, p.Points.Count));
        Assert.Single(result.Project.Pieces, p => p.Points.Contains(new Point2(0, 0)));
        Assert.Single(result.Project.Pieces, p => p.Points.Contains(new Point2(2, 0)));
    }

    [Fact]
    public void BranchingOrCrossingDirectLinesCannotProducePartialImport()
    {
        var branch = Line("0", "0", "2", "0") + Line("2", "0", "3", "0") + Line("2", "0", "2", "1");
        Assert.Throws<InvalidDataException>(() => Import(Drawing(branch)));
        var crossing = Line("0", "0", "2", "2") + Line("0", "2", "2", "0");
        Assert.Throws<InvalidDataException>(() => Import(Drawing(crossing)));
    }

    [Fact]
    public void UnapprovedUnknownAndHiddenModelLayersAreRejected()
    {
        Assert.Throws<InvalidDataException>(() => Import(Drawing(Line("0", "0", "1", "0", "CUT")), approved: ["0"]));
        Assert.Throws<InvalidDataException>(() => Import(Drawing(Line("0", "0", "1", "0", "UNKNOWN"))));
        Assert.Throws<InvalidDataException>(() => Import(Drawing(Line("0", "0", "1", "0", "HIDDEN"), Layer("HIDDEN", flags: 1)), approved: ["0", "HIDDEN"]));
    }

    [Theory]
    [InlineData("6\nBYBLOCK\n")]
    [InlineData("62\n1\n")]
    public void DirectLineWithUnresolvedStyleOrExplicitColorIsRejected(string extra)
    {
        Assert.Throws<InvalidDataException>(() => Import(Drawing(Line("0", "0", "1", "0", extra: extra))));
    }

    [Fact]
    public void CancelledImportDoesNotAlterSource()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => Import(Drawing(Line("0", "0", "1", "0")), token: cancellation.Token));
    }
}
