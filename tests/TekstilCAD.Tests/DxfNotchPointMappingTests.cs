using System.Text;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class DxfNotchPointMappingTests
{
    private static string Contour(string start="0") => "0\nLWPOLYLINE\n8\nCUT\n90\n3\n70\n1\n10\n"+start+"\n20\n0\n10\n10\n20\n0\n10\n0\n20\n10\n";
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
