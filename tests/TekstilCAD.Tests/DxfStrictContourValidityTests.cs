using System.Text;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

/// <summary>Closed DXF contours must be valid before strict placement import returns a project.</summary>
public sealed class DxfStrictContourValidityTests
{
    private const string Tables = "0\nSECTION\n2\nTABLES\n0\nTABLE\n2\nLAYER\n" +
        "0\nLAYER\n2\n0\n70\n0\n62\n7\n290\n1\n6\nCONTINUOUS\n0\nENDTAB\n0\nENDSEC\n";
    private const string Insert = "0\nINSERT\n2\nA\n10\n0\n20\n0\n";
    private static readonly (int X, int Y)[] Valid = [(0,0), (4,0), (4,4), (2,2), (0,4)];
    private static readonly (int X, int Y)[] Bowtie = [(0,0), (4,4), (0,4), (4,0)];
    private static readonly (int X, int Y)[] Collinear = [(0,0), (2,0), (4,0)];

    private static string Contour(bool classic, bool closed, (int X,int Y)[] points)
    {
        var result = new StringBuilder();
        if (classic)
        {
            result.Append("0\nPOLYLINE\n8\n0\n66\n1\n70\n").Append(closed ? 1 : 0).Append('\n');
            foreach (var (x,y) in points) result.Append($"0\nVERTEX\n8\n0\n10\n{x}\n20\n{y}\n");
            result.Append("0\nSEQEND\n8\n0\n");
        }
        else
        {
            result.Append("0\nLWPOLYLINE\n8\n0\n90\n").Append(points.Length).Append("\n70\n").Append(closed ? 1 : 0).Append('\n');
            foreach (var (x,y) in points) result.Append($"10\n{x}\n20\n{y}\n");
        }
        return result.ToString();
    }

    private static string Drawing(bool classic, bool model, bool closed, (int X,int Y)[] points)
    {
        string target = Contour(classic,closed,points);
        string content = model ? Contour(false,true,Valid) : target;
        string direct = model ? target : "";
        return Tables + "0\nSECTION\n2\nBLOCKS\n0\nBLOCK\n2\nA\n10\n0\n20\n0\n" + content +
            "0\nENDBLK\n0\nENDSEC\n0\nSECTION\n2\nENTITIES\n" + Insert + direct + "0\nENDSEC\n0\nEOF\n";
    }

    private static DxfImportResult Import(string text, CancellationToken token = default)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        using var stream = new MemoryStream(bytes, writable: false);
        try { return DxfFormat.ImportWithBlockPlacements(stream,"test",1,DxfTextEncoding.Utf8,0,["0"],token); }
        finally { Assert.Equal(bytes,stream.ToArray()); }
    }

    private static string Lines(params (string X, string Y)[] points)
    {
        var result = new StringBuilder();
        for (int i = 1; i < points.Length; i++)
            result.Append($"0\nLINE\n8\n0\n10\n{points[i-1].X}\n20\n{points[i-1].Y}\n11\n{points[i].X}\n21\n{points[i].Y}\n");
        return result.ToString();
    }

    private static string LineDrawing(bool model, string lines)
    {
        var block = model ? Contour(false,true,Valid) : lines;
        return Tables + "0\nSECTION\n2\nBLOCKS\n0\nBLOCK\n2\nA\n10\n0\n20\n0\n" + block +
            "0\nENDBLK\n0\nENDSEC\n0\nSECTION\n2\nENTITIES\n" + Insert + (model ? lines : "") + "0\nENDSEC\n0\nEOF\n";
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void SmallClosedLineTriangleBelowContourAreaLimitIsRejected(bool model)
    {
        var lines=Lines(("0","0"),("0.00001","0"),("0","0.00001"),("0","0"));
        Assert.Throws<InvalidDataException>(() => Import(LineDrawing(model,lines)));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void SelfCrossingClosedLinesAreRejected(bool model)
    {
        var lines=Lines(("0","0"),("4","4"),("0","4"),("4","0"),("0","0"));
        Assert.Throws<InvalidDataException>(() => Import(LineDrawing(model,lines)));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ValidClosedLineTriangleImports(bool model)
    {
        var lines=Lines(("0","0"),("4","0"),("0","4"),("0","0"));
        var result=Import(LineDrawing(model,lines));
        Assert.Equal(model ? 2 : 1,result.Project.Pieces.Count);
        Assert.All(result.Project.Pieces,p => Assert.True(p.Closed));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void OpenLineRemainsAnEditableOpenContour(bool model)
    {
        var result=Import(LineDrawing(model,Lines(("0","0"),("4","0"))));
        Assert.Single(result.Project.Pieces,p => !p.Closed);
    }

    [Theory]
    [InlineData(false,false)] [InlineData(false,true)] [InlineData(true,false)] [InlineData(true,true)]
    public void ClosedSelfCrossingContourFailsAtomically(bool classic, bool model)
    {
        Assert.Throws<InvalidDataException>(() => Import(Drawing(classic,model,true,Bowtie)));
    }

    [Theory]
    [InlineData(false,false)] [InlineData(false,true)] [InlineData(true,false)] [InlineData(true,true)]
    public void ClosedZeroAreaContourFailsAtomically(bool classic, bool model)
    {
        Assert.Throws<InvalidDataException>(() => Import(Drawing(classic,model,true,Collinear)));
    }

    [Theory]
    [InlineData(false,false)] [InlineData(false,true)] [InlineData(true,false)] [InlineData(true,true)]
    public void ValidConcaveContourStillImports(bool classic, bool model)
    {
        var result=Import(Drawing(classic,model,true,Valid));
        Assert.Equal(model ? 2 : 1,result.Project.Pieces.Count);
        Assert.All(result.Project.Pieces,p => Assert.True(p.Closed));
    }

    [Theory]
    [InlineData(false,false)] [InlineData(false,true)] [InlineData(true,false)] [InlineData(true,true)]
    public void OpenCrossingPolylineRemainsAnEditableOpenContour(bool classic, bool model)
    {
        var result=Import(Drawing(classic,model,false,Bowtie));
        Assert.Single(result.Project.Pieces,p => !p.Closed);
    }

    [Fact]
    public void CancelBeforeImportKeepsSourceBytesUnchanged()
    {
        using var cts=new CancellationTokenSource();
        cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => Import(Drawing(false,false,true,Valid),cts.Token));
    }
}
