using System.Text;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

/// <summary>End-to-end DXF model-space INSERT expectations. These fixtures contain no vendor data.</summary>
public sealed class DxfInsertImportTests
{
    private static string Block(string name, string baseX, string baseY, string vertices) =>
        $"0\nBLOCK\n2\n{name}\n10\n{baseX}\n20\n{baseY}\n30\n0\n" +
        "0\nLWPOLYLINE\n8\n0\n90\n3\n70\n1\n" + vertices + "0\nENDBLK\n";

    private static string Drawing(string blocks, string entities) =>
        "0\nSECTION\n2\nTABLES\n0\nTABLE\n2\nLAYER\n" +
        "0\nLAYER\n2\n0\n70\n0\n62\n7\n290\n1\n6\nCONTINUOUS\n" +
        "0\nENDTAB\n0\nENDSEC\n" +
        "0\nSECTION\n2\nBLOCKS\n" + blocks + "0\nENDSEC\n" +
        "0\nSECTION\n2\nENTITIES\n" + entities + "0\nENDSEC\n0\nEOF\n";

    private static DxfImportResult Import(string text, double unitFactor = 1)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        using var input = new MemoryStream(bytes, writable: false);
        var result = DxfFormat.ImportWithBlockPlacements(input, "sentetik INSERT", unitFactor,
            DxfTextEncoding.Utf8, 0, ["0"]);
        Assert.Equal(bytes, input.ToArray());
        return result;
    }

    [Fact]
    public void StandardImportExplicitlyWarnsThatBlockPlacementWasNotApplied()
    {
        var triangle = Block("A", "1", "2", "10\n1\n20\n2\n10\n3\n20\n2\n10\n1\n20\n3\n");
        var unused = Block("B", "0", "0", "10\n100\n20\n100\n10\n101\n20\n100\n10\n100\n20\n101\n");
        var bytes = Encoding.UTF8.GetBytes(Drawing(triangle + unused, "0\nINSERT\n2\nA\n10\n4\n20\n5\n"));
        using var input = new MemoryStream(bytes, writable: false);
        var result = DxfFormat.Import(input, "standart");
        Assert.Equal(2, result.Project.Pieces.Count);
        Assert.True(result.SkippedInsertEntities);
        Assert.True(result.SkippedRelevantInsertEntities);
        Assert.True(result.ImportedBlockDefinitions);
        Assert.Contains(result.Warnings, w => w.StartsWith("BLOCK tanım geometrisi", StringComparison.Ordinal)
            && w.Contains("INSERT", StringComparison.Ordinal));
        Assert.Contains(result.Warnings, w => w.StartsWith("Atlanan desteklenmeyen DXF verileri:", StringComparison.Ordinal)
            && w.Contains("INSERT", StringComparison.Ordinal));
        Assert.Equal(bytes, input.ToArray());
    }

    [Theory]
    [InlineData("0\nINSERT\n2\nA\n10\n0\n20\n0\n60\n1\n")]
    [InlineData("0\nINSERT\n2\nA\n10\n0\n20\n0\n67\n1\n")]
    [InlineData("0\nINSERT\n2\nA\n10\n0\n20\n0\n410\nLayout1\n")]
    public void StandardImportReportsUnappliedInsertEvenWhenExcludedFromVisibleModelSpace(string insert)
    {
        var triangle = Block("A", "0", "0", "10\n0\n20\n0\n10\n2\n20\n0\n10\n0\n20\n2\n");
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(Drawing(triangle, insert)), writable: false);
        var result = DxfFormat.Import(input, "sentetik");
        Assert.True(result.SkippedInsertEntities);
        // Legacy import also turns block definitions into pieces. Even an
        // excluded INSERT must not make that unrelated geometry look safe.
        Assert.True(result.SkippedRelevantInsertEntities);
        Assert.True(result.ImportedBlockDefinitions);
    }

    [Fact]
    public void StandardImportReportsNestedInsertEvenWithoutModelSpaceInsert()
    {
        var triangle = Block("A", "0", "0", "10\n0\n20\n0\n10\n2\n20\n0\n10\n0\n20\n2\n");
        var nested = "0\nBLOCK\n2\nB\n10\n0\n20\n0\n30\n0\n" +
                     "0\nINSERT\n2\nA\n10\n5\n20\n5\n0\nENDBLK\n";
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(Drawing(triangle + nested, "")), writable: false);
        var result = DxfFormat.Import(input, "sentetik");
        Assert.True(result.SkippedInsertEntities);
        Assert.True(result.SkippedRelevantInsertEntities);
        Assert.True(result.ImportedBlockDefinitions);
    }

    [Fact]
    public void StandardImportWithoutBlocksOrInsertsReportsNeitherRisk()
    {
        var contour = "0\nLWPOLYLINE\n8\n0\n90\n3\n70\n1\n10\n0\n20\n0\n10\n2\n20\n0\n10\n0\n20\n2\n";
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(Drawing("", contour)), writable: false);
        var result = DxfFormat.Import(input, "sentetik");
        Assert.False(result.SkippedInsertEntities);
        Assert.False(result.SkippedRelevantInsertEntities);
        Assert.False(result.ImportedBlockDefinitions);
        Assert.Single(result.Project.Pieces);
    }

    [Fact]
    public void StandardImportDistinguishesBlockDefinitionsWithoutInsert()
    {
        var triangle = Block("A", "0", "0", "10\n0\n20\n0\n10\n2\n20\n0\n10\n0\n20\n2\n");
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(Drawing(triangle, "")), writable: false);
        var result = DxfFormat.Import(input, "sentetik");
        Assert.False(result.SkippedInsertEntities);
        Assert.False(result.SkippedRelevantInsertEntities);
        Assert.True(result.ImportedBlockDefinitions);
        Assert.Single(result.Project.Pieces);
    }

    [Fact]
    public void ManuallyConstructedImportResultDoesNotClaimRisksWereChecked()
    {
        var result = new DxfImportResult(new ProjectDocument(), []);
        Assert.Null(result.SkippedInsertEntities);
        Assert.Null(result.SkippedRelevantInsertEntities);
        Assert.Null(result.ImportedBlockDefinitions);
    }

    [Fact]
    public void StandardImportDoesNotHideVisibleInsertWhenAnotherIsExplicitlyExcluded()
    {
        var triangle = Block("A", "0", "0", "10\n0\n20\n0\n10\n2\n20\n0\n10\n0\n20\n2\n");
        var entities = "0\nINSERT\n2\nA\n60\n1\n" + "0\nINSERT\n2\nA\n10\n0\n20\n0\n";
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(Drawing(triangle, entities)), writable: false);
        var result = DxfFormat.Import(input, "sentetik");
        Assert.True(result.SkippedInsertEntities);
        Assert.True(result.SkippedRelevantInsertEntities);
    }

    [Fact]
    public void StandardImportDistinguishesInsertInsideExplicitPaperSpaceBlock()
    {
        var triangle = Block("A", "0", "0", "10\n0\n20\n0\n10\n2\n20\n0\n10\n0\n20\n2\n");
        var paper = "0\nBLOCK\n2\n*Paper_Space\n10\n0\n20\n0\n30\n0\n" +
                    "0\nINSERT\n2\nA\n10\n5\n20\n5\n0\nENDBLK\n";
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(Drawing(triangle + paper, "")), writable: false);
        var result = DxfFormat.Import(input, "sentetik");
        Assert.True(result.SkippedInsertEntities);
        Assert.True(result.SkippedRelevantInsertEntities);
    }

    [Theory]
    [InlineData("60\n1\n")]
    [InlineData("67\n1\n")]
    [InlineData("410\nLayout1\n")]
    public void ExcludedInsertWithOnlyModelSpaceGeometryIsNotReportedAsRelevant(string flags)
    {
        var contour = "0\nLWPOLYLINE\n8\n0\n90\n3\n70\n1\n10\n0\n20\n0\n10\n2\n20\n0\n10\n0\n20\n2\n";
        var bytes = Encoding.UTF8.GetBytes(Drawing("", contour + "0\nINSERT\n2\nA\n" + flags));
        using var input = new MemoryStream(bytes, writable: false);
        var result = DxfFormat.Import(input, "sentetik");
        Assert.True(result.SkippedInsertEntities);
        Assert.False(result.ImportedBlockDefinitions);
        Assert.False(result.SkippedRelevantInsertEntities);
        Assert.Single(result.Project.Pieces);
        Assert.Equal(bytes, input.ToArray());
    }

    [Fact]
    public void ExcludedInsertAndUnrelatedBlockDefinitionRemainRelevant()
    {
        var unrelated = Block("B", "0", "0", "10\n0\n20\n0\n10\n2\n20\n0\n10\n0\n20\n2\n");
        var bytes = Encoding.UTF8.GetBytes(Drawing(unrelated, "0\nINSERT\n2\nA\n60\n1\n"));
        using var input = new MemoryStream(bytes, writable: false);
        var result = DxfFormat.Import(input, "sentetik");
        Assert.True(result.SkippedInsertEntities);
        Assert.True(result.ImportedBlockDefinitions);
        Assert.True(result.SkippedRelevantInsertEntities);
        Assert.Equal(bytes, input.ToArray());
    }

    [Theory]
    [InlineData("60\n1\n60\n0\n")]
    [InlineData("60\nunknown\n")]
    [InlineData("67\n1\n67\n0\n")]
    [InlineData("67\n2\n")]
    [InlineData("410\nModel\n410\nLayout1\n")]
    [InlineData("410\n \n")]
    public void StandardImportRejectsAmbiguousInsertVisibilityOrSpace(string flags)
    {
        var triangle = Block("A", "0", "0", "10\n0\n20\n0\n10\n2\n20\n0\n10\n0\n20\n2\n");
        var bytes = Encoding.UTF8.GetBytes(Drawing(triangle, "0\nINSERT\n2\nA\n" + flags));
        using var input = new MemoryStream(bytes, writable: false);
        Assert.Throws<InvalidDataException>(() => DxfFormat.Import(input, "sentetik"));
        Assert.Equal(bytes, input.ToArray());
    }

    [Theory]
    [InlineData("67\n1\n410\nModel\n")]
    [InlineData("67\n0\n410\nLayout1\n")]
    [InlineData("60\n1\n62\n-7\n62\n7\n")]
    [InlineData("60\n1\n62\ninvalid\n")]
    public void StandardImportDoesNotTreatConflictingInsertMetadataAsSafelyExcluded(string flags)
    {
        var contour = "0\nLWPOLYLINE\n8\n0\n90\n3\n70\n1\n10\n0\n20\n0\n10\n2\n20\n0\n10\n0\n20\n2\n";
        var bytes = Encoding.UTF8.GetBytes(Drawing("", contour + "0\nINSERT\n2\nA\n" + flags));
        using var input = new MemoryStream(bytes, writable: false);
        try
        {
            var result = DxfFormat.Import(input, "sentetik");
            Assert.True(result.SkippedRelevantInsertEntities);
        }
        catch (InvalidDataException)
        {
            // Rejecting malformed or contradictory metadata is also safe.
        }
        Assert.Equal(bytes, input.ToArray());
    }

    private static void AssertContour(IReadOnlyList<Point2> actual, params Point2[] expected)
    {
        Assert.Equal(expected.Length, actual.Count);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i].X, actual[i].X, 8);
            Assert.Equal(expected[i].Y, actual[i].Y, 8);
        }
    }

    [Fact]
    public void TwoInsertOccurrencesExpandSeparatelyUsingBlockBaseAndTransformation()
    {
        // (x,y) => insertion + R90 * (2*(x-2), 3*(y-3)).
        var triangle = Block("KALIP", "2", "3", "10\n2\n20\n3\n10\n4\n20\n3\n10\n2\n20\n4\n");
        var unused = Block("KULLANILMAYAN", "0", "0", "10\n100\n20\n100\n10\n101\n20\n100\n10\n100\n20\n101\n");
        var entities = "0\nINSERT\n8\n0\n2\nKALIP\n10\n10\n20\n20\n41\n2\n42\n3\n50\n90\n" +
                       "0\nINSERT\n8\n0\n2\nKALIP\n10\n-10\n20\n5\n";
        var result = Import(Drawing(triangle + unused, entities));

        Assert.False(result.SkippedInsertEntities);
        Assert.False(result.SkippedRelevantInsertEntities);
        Assert.False(result.ImportedBlockDefinitions);
        Assert.Equal(2, result.Project.Pieces.Count);
        var first = Assert.Single(result.Project.Pieces, p => p.Points.Any(q => Math.Abs(q.X - 10) < 1e-8 && Math.Abs(q.Y - 24) < 1e-8));
        var second = Assert.Single(result.Project.Pieces, p => p.Points.Any(q => Math.Abs(q.X + 8) < 1e-8 && Math.Abs(q.Y - 5) < 1e-8));
        Assert.NotEqual(first.Id, second.Id);
        Assert.True(first.Closed);
        Assert.True(second.Closed);
        AssertContour(first.Points, new(10, 20), new(10, 24), new(7, 20));
        AssertContour(second.Points, new(-10, 5), new(-8, 5), new(-10, 6));
    }

    [Fact]
    public void FileUnitFactorAppliesAfterPlacementAndDoesNotImportUnusedDefinitions()
    {
        var triangle = Block("A", "1", "2", "10\n1\n20\n2\n10\n3\n20\n2\n10\n1\n20\n3\n");
        var unused = Block("B", "0", "0", "10\n100\n20\n100\n10\n101\n20\n100\n10\n100\n20\n101\n");
        var result = Import(Drawing(triangle + unused, "0\nINSERT\n2\nA\n10\n4\n20\n5\n"), 10);
        var piece = Assert.Single(result.Project.Pieces);
        Assert.True(piece.Closed);
        AssertContour(piece.Points, new(40, 50), new(60, 50), new(40, 60));
    }
}
