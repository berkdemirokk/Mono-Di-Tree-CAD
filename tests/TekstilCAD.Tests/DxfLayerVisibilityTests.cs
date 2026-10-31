using System.Text;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class DxfLayerVisibilityTests
{
    private static string Layer(string name,string fields)=>$"0\nLAYER\n2\n{name}\n{fields}";
    private static string Tables(string rows)=>$"0\nSECTION\n2\nTABLES\n0\nTABLE\n2\nLAYER\n{rows}0\nENDTAB\n0\nENDSEC\n";
    private static string Line(string layer,int a,int b)=>$"0\nLINE\n8\n{layer}\n10\n{a}\n20\n0\n11\n{b}\n21\n0\n";
    private static string Entities(string items)=>$"0\nSECTION\n2\nENTITIES\n{items}0\nENDSEC\n";
    private static DxfImportResult Import(string body)
    {
        using var stream=new MemoryStream(Encoding.UTF8.GetBytes(body+"0\nEOF\n"));
        return DxfFormat.Import(stream,"layers");
    }

    [Fact]
    public void OffAndFrozenLayersAreSkippedBeforeLineJoiningCaseInsensitively()
    {
        var tables=Tables(Layer("visible","70\n0\n62\n7\n")+Layer("off","70\n0\n62\n-7\n")+Layer("frozen","70\n1\n62\n7\n"));
        var result=Import(tables+Entities(Line("VISIBLE",0,10)+Line("OFF",10,20)+Line("FROZEN",20,30)+Line("visible",30,40)));
        Assert.Equal(2,result.Project.Pieces.Count);
        Assert.Contains(result.Warnings,w=>w.Contains("kapalı veya donmuş DXF katmanı"));
        Assert.Contains(result.Project.Pieces,p=>p.Points.SequenceEqual(new[]{new Point2(0,0),new Point2(10,0)}));
        Assert.Contains(result.Project.Pieces,p=>p.Points.SequenceEqual(new[]{new Point2(30,0),new Point2(40,0)}));
    }

    [Fact]
    public void LockedDefaultViewportFrozenAndNonPlotAreNotGloballyHidden()
    {
        var result=Import(Tables(Layer("locked","70\n4\n62\n7\n")+Layer("viewport","70\n2\n62\n7\n")+Layer("nonplot","70\n0\n62\n7\n290\n0\n"))+
            Entities(Line("locked",0,10)+Line("viewport",20,30)+Line("nonplot",40,50)));
        Assert.Equal(3,result.Project.Pieces.Count);
        Assert.Contains(result.Warnings,w=>w.Contains("plotlanmayan katman"));
        Assert.Contains(result.Warnings,w=>w.Contains("viewport'a özel"));
        Assert.DoesNotContain(result.Warnings,w=>w.Contains("kapalı veya donmuş"));
    }

    [Fact]
    public void TablesAfterEntitiesApplyAndUnknownLayersWarn()
    {
        var result=Import(Entities(Line("later",0,10)+Line("missing",20,30))+
            Tables(Layer("later","70\n0\n62\n-2\n")));
        Assert.Single(result.Project.Pieces);
        Assert.Contains(result.Warnings,w=>w.Contains("kapalı veya donmuş"));
        Assert.Contains(result.Warnings,w=>w.Contains("katman görünürlüğü bilinmiyor"));
        var noTable=Import(Entities(Line("0",0,10)));
        Assert.Single(noTable.Project.Pieces);
        Assert.Contains(noTable.Warnings,w=>w.Contains("katman görünürlüğü bilinmiyor"));
    }

    [Fact]
    public void HiddenPolylineIsSkippedWithoutLosingFollowingModelLine()
    {
        var poly="0\nPOLYLINE\n8\nhidden\n70\n0\n0\nVERTEX\n10\n0\n20\n0\n0\nVERTEX\n10\n10\n20\n0\n0\nSEQEND\n";
        var result=Import(Tables(Layer("hidden","70\n1\n62\n7\n")+Layer("visible","70\n0\n62\n7\n"))+Entities(poly+Line("visible",20,30)));
        Assert.Single(result.Project.Pieces);
        Assert.Contains(result.Warnings,w=>w.Contains("kapalı veya donmuş DXF katmanı"));
    }

    [Fact]
    public void BlockLayerZeroIsNotMistakenForResolvedInsertLayer()
    {
        var blocks="0\nSECTION\n2\nBLOCKS\n0\nBLOCK\n2\nKalıp\n"+Line("0",0,10)+"0\nENDBLK\n0\nENDSEC\n";
        var result=Import(Tables(Layer("0","70\n1\n62\n7\n"))+blocks+Entities(Line("0",20,30)));
        Assert.Single(result.Project.Pieces);
        Assert.Contains(result.Warnings,w=>w.Contains("katman görünürlüğü mirası uygulanmaz"));
        Assert.Contains(result.Warnings,w=>w.Contains("kapalı veya donmuş DXF katmanı"));
    }

    [Theory]
    [InlineData("70\nnot-number\n62\n7\n")]
    [InlineData("70\n0\n70\n1\n62\n7\n")]
    [InlineData("70\n-1\n62\n7\n")]
    [InlineData("70\n0\n62\n7\n290\n2\n")]
    public void MalformedLayerMetadataIsRejected(string fields)
        => Assert.Throws<InvalidDataException>(()=>Import(Tables(Layer("test",fields))+Entities(Line("test",0,10))));

    [Fact]
    public void DuplicateCaseInsensitiveNamesFailAndMissingFieldsWarn()
    {
        Assert.Throws<InvalidDataException>(()=>Import(Tables(Layer("model","70\n0\n62\n7\n")+Layer("MODEL","70\n1\n62\n7\n"))+Entities(Line("model",0,10))));
        var result=Import(Tables(Layer("model","70\n0\n"))+Entities(Line("model",0,10)));
        Assert.Single(result.Project.Pieces);Assert.Contains(result.Warnings,w=>w.Contains("katman görünürlüğü bilinmiyor"));
    }

    [Fact]
    public void OnlyHiddenGeometryExplainsFailureAndCancellationIsHonored()
    {
        var content=Tables(Layer("off","70\n0\n62\n-2\n"))+Entities(Line("off",0,10));
        var error=Assert.Throws<InvalidDataException>(()=>Import(content));
        Assert.Contains("kapalı veya donmuş DXF katmanı",error.Message);
        using var cancellation=new CancellationTokenSource();cancellation.Cancel();
        using var stream=new MemoryStream(Encoding.UTF8.GetBytes(content+"0\nEOF\n"));
        Assert.Throws<OperationCanceledException>(()=>DxfFormat.Import(stream,"cancel",1,cancellation.Token));
    }

    [Theory]
    [InlineData("70\n129\n62\n7\n")]
    [InlineData("70\n17\n62\n7\n")]
    [InlineData("70\n1\n")]
    [InlineData("62\n-7\n")]
    public void HiddenWithAmbiguousMetadataIsRetainedAndWarned(string fields)
    {
        var result=Import(Tables(Layer("ambiguous",fields))+Entities(Line("ambiguous",0,10)));
        Assert.Single(result.Project.Pieces);
        Assert.Contains(result.Warnings,w=>w.Contains("katman görünürlüğü bilinmiyor"));
        Assert.DoesNotContain(result.Warnings,w=>w.Contains("kapalı veya donmuş DXF katmanı"));
    }

    [Fact]
    public void LineGroupingUsesSameCaseInsensitiveLayerIdentity()
    {
        string Side(string layer,int x1,int y1,int x2,int y2)=>$"0\nLINE\n8\n{layer}\n10\n{x1}\n20\n{y1}\n11\n{x2}\n21\n{y2}\n";
        var entities=Side("KALIP",0,0,10,0)+Side("kalip",10,0,10,10)+Side("KALIP",10,10,0,10)+Side("kalip",0,10,0,0);
        var result=Import(Tables(Layer("KALIP","70\n0\n62\n7\n"))+Entities(entities));
        var piece=Assert.Single(result.Project.Pieces);
        Assert.True(piece.Closed);Assert.Equal(4,piece.Points.Count);
        Assert.DoesNotContain(result.Warnings,w=>w.Contains("Belirsiz LINE"));
    }

    [Fact]
    public void OwnExportProvidesExplicitVisibleLayersAndRoundTrips()
    {
        var root=new DirectoryInfo(AppContext.BaseDirectory);
        while(root is not null && !File.Exists(Path.Combine(root.FullName,"AGENTS.md")))root=root.Parent;
        if(root is null)throw new InvalidOperationException("Repository root missing.");
        var dir=Path.Combine(root.FullName,"work","dxf-visible-layer-tests",Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);var path=Path.Combine(dir,"export.dxf");
        var document=new ProjectDocument { Pieces=[new PatternPiece { Name="Parça",Points=[new(0,0),new(10,0),new(10,10)] },
            new PatternPiece { Name="Çizgi",Closed=false,Points=[new(20,0),new(30,0)] }] };
        DxfFormat.Export(document,path);
        var imported=DxfFormat.Import(path);
        Assert.Equal(2,imported.Project.Pieces.Count);
        Assert.All(document.Pieces,p=>Assert.Contains(imported.Project.Pieces,q=>q.Points.SequenceEqual(p.Points) && q.Closed==p.Closed));
        Assert.DoesNotContain(imported.Warnings,w=>w.Contains("katman görünürlüğü bilinmiyor",StringComparison.OrdinalIgnoreCase));
        Assert.Contains(imported.Warnings,w=>w.StartsWith("DXF renk bilgileri projede korunmaz",StringComparison.Ordinal));
        Assert.Contains("0\nTABLE\n2\nLAYER\n",File.ReadAllText(path));
    }
}
