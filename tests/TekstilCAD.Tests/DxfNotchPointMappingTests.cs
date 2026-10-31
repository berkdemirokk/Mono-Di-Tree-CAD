using System.Text;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class DxfNotchPointMappingTests
{
    private static string Contour(string start="0",string target="10") => "0\nLWPOLYLINE\n8\nCUT\n90\n3\n70\n1\n10\n"+start+"\n20\n0\n10\n"+target+"\n20\n0\n10\n0\n20\n10\n";
    private static string Point(string x="10",string layer="MARK",string extra="") => "0\nPOINT\n8\n"+layer+"\n10\n"+x+"\n20\n0\n"+extra;
    private static DxfImportResult Read(string records,double scale=1)
    {
        using var input=new MemoryStream(Encoding.UTF8.GetBytes("0\nSECTION\n2\nENTITIES\n"+records+"0\nENDSEC\n0\nEOF\n"));
        return DxfFormat.Import(input,"test",scale);
    }
    private static DxfNotchPointSelection Select(DxfImportResult import) => new(
        import.SupplementalGeometry!.Records.First(r=>r.EntityType=="POINT").SourceOrdinal,
        import.Project.Pieces[0].Id,1);
    private static DxfNotchPointMappingResult Apply(DxfImportResult import,IReadOnlyCollection<DxfNotchPointSelection> selected,string layer="MARK") =>
        DxfNotchPointMapping.Apply(import,selected,layer,DxfTextileGrainlineMapping.CaptureFingerprint(import));

    [Fact]
    public void ExplicitPointAndVertexCreateDetachedVisualMark()
    {
        var import=Read(Contour()+Point());var before=import.Project.Clone();var selection=Select(import);
        var result=Apply(import,[selection]);var piece=Assert.Single(result.Project.Pieces);var mark=Assert.Single(piece.Notches!);
        Assert.Equal(1,mark.VertexIndex);Assert.Equal(piece.Points[1],mark.Position);
        Assert.Equal(selection.PointSourceOrdinal,Assert.Single(result.Applied).PointSourceOrdinal);
        Assert.Null(import.Project.Pieces[0].Notches);Assert.True(DocumentSession.ContentEquals(before,import.Project));
    }
    [Theory]
    [InlineData("0.1","0.1")]
    [InlineData("0.100","1e-1")]
    [InlineData("1e-1","0.1000")]
    public void EquivalentOriginalDecimalsMatchWithoutBinaryFormatting(string contourX,string pointX)
    {
        var import=Read(Contour(target:contourX)+Point(pointX));
        var result=Apply(import,[Select(import)]);
        var piece=Assert.Single(result.Project.Pieces);var mark=Assert.Single(piece.Notches!);
        Assert.Equal(piece.Points[1],mark.Position);
        Assert.Null(import.Project.Pieces[0].Notches);
    }
    [Fact]
    public void DistinctDecimalsRoundingToSameDoubleAreRejected()
    {
        var import=Read(Contour(target:"0.1")+Point("0.100000000000000001"));
        Assert.Throws<InvalidDataException>(()=>Apply(import,[Select(import)]));
    }
    [Fact]
    public void EqualNonzeroDecimalsThatUnderflowToZeroAreRejected()
    {
        var import=Read(Contour(start:"-10",target:"1e-400")+Point("1e-400"));
        Assert.Throws<InvalidDataException>(()=>Apply(import,[Select(import)]));
    }
    [Fact]
    public void LegacyLineUsesConservativeCanonicalDecimalFallback()
    {
        var import=Read("0\nLINE\n8\nCUT\n10\n0\n20\n0\n11\n10\n21\n0\n"+Point());
        var mapped=Apply(import,[Select(import)]);
        Assert.Equal(new Point2(10,0),Assert.Single(Assert.Single(mapped.Project.Pieces).Notches!).Position);
        var collision=Read("0\nLINE\n8\nCUT\n10\n0\n20\n0\n11\n10\n21\n0\n"+Point("10.000000000000000001"));
        Assert.Throws<InvalidDataException>(()=>Apply(collision,[Select(collision)]));
    }
    [Fact]
    public void LegacyPolylineUsesConservativeCanonicalDecimalFallback()
    {
        var poly="0\nPOLYLINE\n8\nCUT\n66\n1\n70\n0\n"+
            "0\nVERTEX\n8\nCUT\n10\n0\n20\n0\n0\nVERTEX\n8\nCUT\n10\n10\n20\n0\n0\nSEQEND\n";
        var import=Read(poly+Point());
        var mapped=Apply(import,[Select(import)]);
        Assert.Equal(new Point2(10,0),Assert.Single(Assert.Single(mapped.Project.Pieces).Notches!).Position);
    }
    [Theory]
    [InlineData("0.1","1e-1")]
    [InlineData("1e-1","0.1000")]
    public void LineSourceAcceptsExactEquivalentDecimals(string sourceX,string pointX)
    {
        var import=Read("0\nLINE\n8\nCUT\n10\n0\n20\n0\n11\n"+sourceX+"\n21\n0\n"+Point(pointX));
        var mapped=Apply(import,[Select(import)]);
        Assert.Equal(import.Project.Pieces[0].Points[1],Assert.Single(Assert.Single(mapped.Project.Pieces).Notches!).Position);
    }
    [Fact]
    public void ReversedLineChainUsesMatchingSourceEndpointAndRejectsDecimalCollision()
    {
        var lines="0\nLINE\n8\nCUT\n10\n0\n20\n0\n11\n0.1\n21\n0\n"+
            "0\nLINE\n8\nCUT\n10\n1\n20\n0\n11\n1e-1\n21\n0\n";
        var import=Read(lines+Point("0.1"));var piece=Assert.Single(import.Project.Pieces);
        int index=piece.Points.FindIndex(p=>p==new Point2(.1,0));
        var selection=Select(import) with { VertexIndex=index };
        var mapped=Apply(import,[selection]);Assert.Equal(index,Assert.Single(Assert.Single(mapped.Project.Pieces).Notches!).VertexIndex);
        var mismatch=Read(lines+Point("0.100000000000000001"));piece=Assert.Single(mismatch.Project.Pieces);
        index=piece.Points.FindIndex(p=>p==new Point2(.1,0));
        Assert.Throws<InvalidDataException>(()=>Apply(mismatch,[Select(mismatch) with { VertexIndex=index }]));
    }
    [Theory]
    [InlineData("0.1","1e-1")]
    [InlineData("1e-1","0.1000")]
    public void ClassicPolylineUsesOriginalVertexDecimals(string sourceX,string pointX)
    {
        var poly="0\nPOLYLINE\n8\nCUT\n66\n1\n70\n0\n"+
            "0\nVERTEX\n8\nCUT\n10\n0\n20\n0\n0\nVERTEX\n8\nCUT\n10\n"+sourceX+"\n20\n0\n0\nSEQEND\n";
        var import=Read(poly+Point(pointX));var mapped=Apply(import,[Select(import)]);
        Assert.Equal(import.Project.Pieces[0].Points[1],Assert.Single(Assert.Single(mapped.Project.Pieces).Notches!).Position);
        var mismatch=Read(poly+Point("0.100000000000000001"));
        Assert.Throws<InvalidDataException>(()=>Apply(mismatch,[Select(mismatch)]));
    }
    [Fact]
    public void MissingGeometryInventoryRetainsCanonicalFallback()
    {
        var import=Read("0\nLINE\n8\nCUT\n10\n0\n20\n0\n11\n10\n21\n0\n"+Point());
        var truncated=import with { SourceGeometry=new DxfSourceGeometryReport([],true) };
        var mapped=Apply(truncated,[Select(truncated)]);
        Assert.Equal(new Point2(10,0),Assert.Single(Assert.Single(mapped.Project.Pieces).Notches!).Position);
    }
    [Fact]
    public void ExplicitClassicClosureAliasMapsToFirstVertex()
    {
        var poly="0\nPOLYLINE\n8\nCUT\n66\n1\n70\n1\n"+
            "0\nVERTEX\n8\nCUT\n10\n0.1\n20\n0\n"+
            "0\nVERTEX\n8\nCUT\n10\n10\n20\n0\n"+
            "0\nVERTEX\n8\nCUT\n10\n10\n20\n10\n"+
            "0\nVERTEX\n8\nCUT\n10\n1e-1\n20\n0\n0\nSEQEND\n";
        var import=Read(poly+Point(".1"));var selection=Select(import) with { VertexIndex=3 };
        var mark=Assert.Single(Assert.Single(Apply(import,[selection]).Project.Pieces).Notches!);
        Assert.Equal(0,mark.VertexIndex);
        Assert.Equal(new Point2(.1,0),mark.Position);
    }
    [Fact]
    public void ReviewExportPointCanBeExplicitlyMappedBackToItsVertex()
    {
        var original=new ProjectDocument { Pieces=[new PatternPiece { Points=[new(0,0),new(10,0),new(0,10)],
            Notches=[new NotchMark { VertexIndex=1,Position=new(10,0) }] }] };
        var plan=DxfNotchReviewExport.Prepare(original);
        var directory=Path.Combine(Path.GetTempPath(),"notch-map-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            var path=Path.Combine(directory,"review.dxf");
            DxfNotchReviewExport.Write(plan,path,ProjectStorage.CaptureVersion(path));
            var import=DxfFormat.Import(path);var selected=Select(import);
            var result=Apply(import,[selected],plan.MarkerLayer);
            var piece=Assert.Single(result.Project.Pieces);var mark=Assert.Single(piece.Notches!);
            Assert.Equal(piece.Points[1],mark.Position);
            Assert.Null(import.Project.Pieces[0].Notches);
        }
        finally { Directory.Delete(directory,true); }
    }
    [Fact]
    public void DuplicatePointsWrongLayerAndDuplicateSelectionsFail()
    {
        var duplicate=Read(Contour()+Point()+Point());var selected=Select(duplicate);
        Assert.Throws<InvalidDataException>(()=>Apply(duplicate,[selected]));
        var import=Read(Contour()+Point());selected=Select(import);
        Assert.Throws<InvalidDataException>(()=>Apply(import,[selected],"OTHER"));
        Assert.Throws<InvalidDataException>(()=>Apply(import,[selected,selected]));
        Assert.Null(import.Project.Pieces[0].Notches);
    }
    [Fact]
    public void AmbiguousVertexAndDecimalRoundingCollisionAreRejected()
    {
        var ambiguous=Read(Contour()+Contour()+Point());
        Assert.Throws<InvalidDataException>(()=>Apply(ambiguous,[Select(ambiguous)]));
        var rounded=Read(Contour()+Point("10.000000000000000000001"));
        Assert.Throws<InvalidDataException>(()=>Apply(rounded,[Select(rounded)]));
        var scaled=Read(Contour()+Point(),2);
        Assert.Throws<InvalidDataException>(()=>Apply(scaled,[Select(scaled)]));
    }
    [Theory]
    [InlineData("30\n1\n")]
    [InlineData("60\n1\n")]
    [InlineData("410\nLayout1\n")]
    public void NonModelOrUnsupportedPointIsRejected(string extra)
    {
        var import=Read(Contour()+Point(extra:extra));
        Assert.Throws<InvalidDataException>(()=>Apply(import,[Select(import)]));
    }
    [Fact]
    public void ChangedSourceAndCancellationFailWithoutMutation()
    {
        var import=Read(Contour()+Point());var selection=Select(import);
        var fingerprint=DxfTextileGrainlineMapping.CaptureFingerprint(import);import.Project.Pieces[0].Name="changed";
        Assert.Throws<InvalidDataException>(()=>DxfNotchPointMapping.Apply(import,[selection],"MARK",fingerprint));
        using var cancel=new CancellationTokenSource();cancel.Cancel();
        Assert.Throws<OperationCanceledException>(()=>DxfNotchPointMapping.Apply(import,[selection],"MARK",fingerprint,cancel.Token));
        Assert.Null(import.Project.Pieces[0].Notches);
    }
}
