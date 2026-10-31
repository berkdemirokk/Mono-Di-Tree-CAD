using System.Globalization;
using System.Text;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class DxfAmbiguousLineJunctionTests
{
    private static DxfImportResult Import(params (string Layer, Point2 A, Point2 B)[] edges)
    {
        static string F(double value) => value.ToString("R", CultureInfo.InvariantCulture);
        var entities = new StringBuilder("0\nSECTION\n2\nENTITIES\n");
        foreach (var edge in edges)
            entities.Append($"0\nLINE\n8\n{edge.Layer}\n10\n{F(edge.A.X)}\n20\n{F(edge.A.Y)}\n11\n{F(edge.B.X)}\n21\n{F(edge.B.Y)}\n");
        entities.Append("0\nENDSEC\n0\nEOF\n");
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(entities.ToString()));
        return DxfFormat.Import(stream, "Junction");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void BranchRemainsSeparateRegardlessOfEntityOrder(int rotation)
    {
        (string Layer, Point2 A, Point2 B)[] original =
        [ ("A", new(-10, 0), new(0, 0)), ("A", new(0, 0), new(10, 0)), ("A", new(0, 0), new(0, 10)) ];
        var reordered = original.Skip(rotation).Concat(original.Take(rotation)).ToArray();
        var result = Import(reordered);
        Assert.Equal(3, result.Project.Pieces.Count);
        Assert.All(result.Project.Pieces, p => { Assert.False(p.Closed); Assert.Equal(2, p.Points.Count); });
        foreach (var edge in original)
            Assert.Contains(result.Project.Pieces, p => p.Points.SequenceEqual(new[] { edge.A, edge.B }));
        Assert.Contains(result.Warnings, w => w.StartsWith("Belirsiz LINE bağlantıları:", StringComparison.Ordinal));
    }

    [Fact]
    public void BranchInOneLayerDoesNotPreventUnambiguousChainInAnother()
    {
        var result = Import(("A", new(-10,0),new(0,0)),("A",new(0,0),new(10,0)),("A",new(0,0),new(0,10)),
            ("B",new(50,0),new(60,0)),("B",new(60,0),new(70,0)));
        Assert.Equal(4, result.Project.Pieces.Count);
        Assert.Equal(3, result.Project.Pieces.Count(p => p.Name == "A" && p.Points.Count == 2));
        var chain = Assert.Single(result.Project.Pieces, p => p.Name == "B");
        Assert.Equal(3, chain.Points.Count);
        Assert.False(chain.Closed);
    }

    [Theory]
    [InlineData(0.0000009, true)]
    [InlineData(0.0000011, false)]
    public void EndpointToleranceDeterminesWhetherConnectionIsAmbiguous(double displacement, bool ambiguous)
    {
        var result = Import(("A",new(-10,0),new(0,0)),("A",new(0,0),new(10,0)),
            ("A",new(0,displacement),new(0,10)));
        Assert.Equal(ambiguous, result.Warnings.Any(w => w.StartsWith("Belirsiz LINE bağlantıları:", StringComparison.Ordinal)));
        Assert.Equal(ambiguous ? 3 : 2, result.Project.Pieces.Count);
    }

    [Fact]
    public void NearbyButUnequalEndpointsAreNotSnappedOrDiscarded()
    {
        var result = Import(("A",new(0,0),new(10,0)),("A",new(10.0000005,0),new(20,0)));
        Assert.Equal(2,result.Project.Pieces.Count);
        Assert.Contains(result.Project.Pieces,p => p.Points.SequenceEqual(new[] { new Point2(10.0000005,0),new Point2(20,0) }));
        Assert.Contains(result.Warnings,w => w.StartsWith("Belirsiz LINE bağlantıları:",StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(5, 0, 5, 10)] // endpoint on another segment
    [InlineData(5, -5, 5, 5)] // proper crossing
    [InlineData(5, 0, 15, 0)] // overlap
    public void InteriorContactsAndCrossingsRemainSeparate(double x1,double y1,double x2,double y2)
    {
        var result=Import(("A",new(0,0),new(10,0)),("A",new(x1,y1),new(x2,y2)));
        Assert.Equal(2,result.Project.Pieces.Count);
        Assert.Contains(result.Project.Pieces,p=>p.Points.SequenceEqual(new[] {new Point2(0,0),new Point2(10,0)}));
        Assert.Contains(result.Project.Pieces,p=>p.Points.SequenceEqual(new[] {new Point2(x1,y1),new Point2(x2,y2)}));
        Assert.Contains(result.Warnings,w=>w.StartsWith("Belirsiz LINE bağlantıları:",StringComparison.Ordinal));
    }

    [Fact]
    public void SharedEndpointWithOverlappingEdgesRemainsSeparate()
    {
        var result=Import(("A",new(0,0),new(10,0)),("A",new(0,0),new(5,0)));
        Assert.Equal(2,result.Project.Pieces.Count);
        Assert.Contains(result.Warnings,w=>w.StartsWith("Belirsiz LINE bağlantıları:",StringComparison.Ordinal));
    }

    [Fact]
    public void BlockAndEntitiesWithSameDisplayNameDoNotJoin()
    {
        const string dxf = "0\nSECTION\n2\nBLOCKS\n0\nBLOCK\n2\na\n" +
            "0\nLINE\n8\nb\n10\n0\n20\n0\n11\n10\n21\n0\n0\nENDBLK\n0\nENDSEC\n" +
            "0\nSECTION\n2\nENTITIES\n0\nLINE\n8\na / b\n10\n10\n20\n0\n11\n20\n21\n0\n0\nENDSEC\n0\nEOF\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(dxf));
        var result=DxfFormat.Import(stream,"Source identity");
        Assert.Equal(2,result.Project.Pieces.Count);
        Assert.All(result.Project.Pieces,p=>Assert.Equal(2,p.Points.Count));
        Assert.Contains(result.Project.Pieces,p=>p.Points.SequenceEqual(new[] {new Point2(0,0),new Point2(10,0)}));
        Assert.Contains(result.Project.Pieces,p=>p.Points.SequenceEqual(new[] {new Point2(10,0),new Point2(20,0)}));
        Assert.Contains(result.Warnings,w=>w.Contains("BLOCK",StringComparison.Ordinal));
    }

    [Fact]
    public void RepeatedBlockNamesDoNotJoinAcrossDefinitions()
    {
        const string dxf="0\nSECTION\n2\nBLOCKS\n0\nBLOCK\n2\na\n" +
            "0\nLINE\n8\nb\n10\n0\n20\n0\n11\n10\n21\n0\n0\nENDBLK\n" +
            "0\nBLOCK\n2\na\n0\nLINE\n8\nb\n10\n10\n20\n0\n11\n20\n21\n0\n0\nENDBLK\n0\nENDSEC\n0\nEOF\n";
        using var stream=new MemoryStream(Encoding.UTF8.GetBytes(dxf));
        var result=DxfFormat.Import(stream,"Repeated blocks");
        Assert.Equal(2,result.Project.Pieces.Count);
        Assert.All(result.Project.Pieces,p=>Assert.Equal(2,p.Points.Count));
        Assert.Contains(result.Warnings,w=>w.Contains("BLOCK",StringComparison.Ordinal));
    }

    [Fact]
    public void ReversedDuplicateSegmentsRemainSeparate()
    {
        var result = Import(("A",new(0,0),new(10,0)),("A",new(10,0),new(0,0)));
        Assert.Equal(2, result.Project.Pieces.Count);
        Assert.All(result.Project.Pieces, p => { Assert.False(p.Closed); Assert.Equal(2,p.Points.Count); });
        Assert.Contains(result.Warnings, w => w.StartsWith("Belirsiz LINE bağlantıları:", StringComparison.Ordinal));
    }

    [Fact]
    public void SimpleClosedChainStillFormsOneClosedPiece()
    {
        var result = Import(("A",new(0,0),new(10,0)),("A",new(10,0),new(10,10)),
            ("A",new(10,10),new(0,10)),("A",new(0,10),new(0,0)));
        var piece = Assert.Single(result.Project.Pieces);
        Assert.True(piece.Closed);
        Assert.Equal(4, piece.Points.Count);
        Assert.DoesNotContain(result.Warnings, w => w.StartsWith("Belirsiz LINE bağlantıları:", StringComparison.Ordinal));
    }
}
