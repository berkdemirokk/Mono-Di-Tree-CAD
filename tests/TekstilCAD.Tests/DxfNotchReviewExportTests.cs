using System.Text;
using System.Globalization;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class DxfNotchReviewExportTests
{
    private static ProjectDocument Project()
    {
        var a=new PatternPiece { Name="Ön",Closed=true,Points=[new(0,0),new(10,0),new(10,20),new(0,0)],Metadata=new PieceMetadata { Size="M" } };
        a.Notches=[new NotchMark { VertexIndex=0,Position=a.Points[0] },new NotchMark { VertexIndex=2,Position=a.Points[2] }];
        var b=new PatternPiece { Name="Arka",Closed=false,Points=[new(10,20),new(30,40)] };
        b.Notches=[new NotchMark { VertexIndex=0,Position=b.Points[0] }];
        return new ProjectDocument { Pieces=[a,b] };
    }
    private static string PathForTest()=>Path.Combine(AppContext.BaseDirectory,$"notch-review-{Guid.NewGuid():N}.dxf");
    private static string Write(DxfNotchReviewPlan plan,string path)
    { var expected=ProjectStorage.CaptureVersion(path);DxfNotchReviewExport.Write(plan,path,expected);return File.ReadAllText(path,Encoding.UTF8); }
    [Theory]
    [InlineData(false,"20")]
    [InlineData(true,"-20")]
    public void PointMarkersAndContoursUseTheSameOrientation(bool flip,string expectedY)
    {
        var project=Project();var original=project.Clone();var plan=DxfNotchReviewExport.Prepare(project,flip);
        Assert.Equal(3,plan.Items.Length);Assert.Equal(2,plan.Items.Select(i=>i.MarkerLayer).Distinct().Count());
        Assert.Contains(plan.Omissions,o=>o.Kind==DxfExportOmissionKind.Size);Assert.True(plan.Matches(original));
        var path=PathForTest();try
        {
            var text=Write(plan,path);Assert.Equal(3,text.Split("\nPOINT\n").Length-1);
            var header=text[..text.IndexOf("0\nENDSEC\n",StringComparison.Ordinal)];
            Assert.Contains("9\n$PDMODE\n70\n34\n",header);
            Assert.Contains("9\n$PDSIZE\n40\n-2\n",header);
            Assert.Contains($"10\n10\n20\n{expectedY}\n",text);
            Assert.Contains($"100\nAcDbPoint\n10\n10\n20\n{expectedY}\n",text);
            Assert.Contains("90\n3\n70\n1\n",text);Assert.True(DocumentSession.ContentEquals(original,project));
        }
        finally { if(File.Exists(path))File.Delete(path); }
    }
    [Fact]
    public void NormalExportsStayBlockedEvenWhenCombinedReviewIsSupported()
    {
        var project=Project();var path=PathForTest();
        try
        {
            Assert.Throws<InvalidDataException>(()=>DxfFormat.Export(project,path));
            Assert.Throws<InvalidDataException>(()=>SvgFormat.Export(project,path));
            Assert.Throws<InvalidDataException>(()=>HpglFormat.Prepare(project));
            Assert.False(File.Exists(path));
            project.Pieces[0].Grainline=new Grainline { Start=new(0,0),End=new(0,5) };
            Assert.Single(DxfNotchReviewExport.Prepare(project,false).Grainlines);Assert.False(File.Exists(path));
            Assert.Throws<InvalidDataException>(()=>DxfFormat.Export(project,path));
            Assert.Throws<InvalidDataException>(()=>HpglFormat.Prepare(project));Assert.False(File.Exists(path));
        }
        finally { if(File.Exists(path))File.Delete(path); }
    }
    [Theory]
    [InlineData(false,"3","8")]
    [InlineData(true,"-3","-8")]
    public void CombinedReviewKeepsAllMarkersAndReportsGrainlineLoss(bool flip,string y1,string y2)
    {
        var project=Project();project.Pieces[0].Grainline=new Grainline { Start=new(2,3),End=new(4,8),NeedsReview=true };
        project.Pieces[1].Grainline=new Grainline { Start=new(12,21),End=new(15,24),NeedsReview=false };
        var before=project.Clone();var plan=DxfNotchReviewExport.Prepare(project,flip);var path=PathForTest();
        try
        {
            Assert.Equal(3,plan.Items.Length);Assert.Equal(2,plan.Grainlines.Length);
            Assert.True(plan.Grainlines[0].NeedsReview);Assert.False(plan.Grainlines[1].NeedsReview);
            Assert.Equal(4,plan.Grainlines.Select(g=>g.MarkerLayer).Concat(plan.Items.Select(i=>i.MarkerLayer).Distinct()).Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.Equal(2,plan.Omissions.Count(o=>o.Kind==DxfExportOmissionKind.GrainlineReviewState));
            Assert.DoesNotContain(plan.Omissions,o=>o.Kind==DxfExportOmissionKind.GrainlineUnsupported);
            var text=Write(plan,path);Assert.Equal(2,text.Split("\nLINE\n").Length-1);Assert.Equal(3,text.Split("\nPOINT\n").Length-1);
            Assert.Contains($"8\n{plan.Grainlines[0].MarkerLayer}\n100\nAcDbLine\n10\n2\n20\n{y1}\n30\n0\n11\n4\n21\n{y2}\n31\n0\n",text);
            Assert.True(DocumentSession.ContentEquals(before,project));Assert.True(plan.Matches(before));
            project.Pieces[0].Grainline!.NeedsReview=false;Assert.False(plan.Matches(project));
        }
        finally { if(File.Exists(path))File.Delete(path); }
    }
    [Fact]
    public void CombinedRecordsUseUniqueHandlesDeclaredLayersAndExactCoordinates()
    {
        var project=Project();
        project.Pieces[0].Grainline=new Grainline { Start=new(2,3),End=new(4,8),NeedsReview=true };
        project.Pieces[1].Grainline=new Grainline { Start=new(12,21),End=new(15,24) };
        var before=project.Clone();var plan=DxfNotchReviewExport.Prepare(project,true);var path=PathForTest();
        try
        {
            var lines=Write(plan,path).Split('\n');
            var records=new List<(string Type,List<(int Code,string Value)> Fields)>();
            for(int i=0;i+1<lines.Length;i+=2)
            {
                int code=int.Parse(lines[i],CultureInfo.InvariantCulture);var value=lines[i+1].TrimEnd('\r');
                if(code==0)records.Add((value,[]));else if(records.Count>0)records[^1].Fields.Add((code,value));
            }
            static string One(List<(int Code,string Value)> fields,int code)=>Assert.Single(fields,p=>p.Code==code).Value;
            var entities=records.Where(r=>r.Type is "LWPOLYLINE" or "POINT" or "LINE").ToArray();
            Assert.Equal(2,entities.Count(r=>r.Type=="LWPOLYLINE"));
            Assert.Equal(3,entities.Count(r=>r.Type=="POINT"));
            Assert.Equal(2,entities.Count(r=>r.Type=="LINE"));
            var layers=records.Where(r=>r.Type=="LAYER").Select(r=>One(r.Fields,2)).ToArray();
            Assert.Equal(layers.Length,layers.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.All(entities,e=>Assert.Contains(layers,l=>string.Equals(l,One(e.Fields,8),StringComparison.OrdinalIgnoreCase)));
            var handled=records.Where(r=>r.Type is "TABLE" or "LTYPE" or "LAYER" or "LWPOLYLINE" or "POINT" or "LINE")
                .Select(r=>One(r.Fields,5)).ToArray();
            Assert.Equal(handled.Length,handled.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.All(handled,h=>Assert.True(ulong.TryParse(h,NumberStyles.HexNumber,CultureInfo.InvariantCulture,out _)));
            foreach(var item in plan.Items)
            {
                var marker=Assert.Single(entities,e=>e.Type=="POINT" && One(e.Fields,8)==item.MarkerLayer &&
                    double.Parse(One(e.Fields,10),CultureInfo.InvariantCulture)==item.Position.X &&
                    double.Parse(One(e.Fields,20),CultureInfo.InvariantCulture)==-item.Position.Y);
                Assert.Equal("0",One(marker.Fields,30));
            }
            foreach(var line in plan.Grainlines)
            {
                var entity=Assert.Single(entities,e=>e.Type=="LINE" && One(e.Fields,8)==line.MarkerLayer);
                Assert.Equal(line.Start.X,double.Parse(One(entity.Fields,10),CultureInfo.InvariantCulture));
                Assert.Equal(-line.Start.Y,double.Parse(One(entity.Fields,20),CultureInfo.InvariantCulture));
                Assert.Equal(line.End.X,double.Parse(One(entity.Fields,11),CultureInfo.InvariantCulture));
                Assert.Equal(-line.End.Y,double.Parse(One(entity.Fields,21),CultureInfo.InvariantCulture));
            }
            Assert.True(DocumentSession.ContentEquals(before,project));Assert.True(plan.Matches(project));
        }
        finally { if(File.Exists(path))File.Delete(path); }
    }
    [Fact]
    public void CombinedReviewAvoidsCollisionWithContourAndDoesNotOverwrite()
    {
        var project=Project();var piece=project.Pieces[0];
        project.Pieces[1].Name="TCD_GRAIN_"+piece.Id.ToString("N");
        piece.Grainline=new Grainline { Start=new(2,3),End=new(4,8) };
        var before=project.Clone();var plan=DxfNotchReviewExport.Prepare(project,false);
        Assert.False(string.Equals(project.Pieces[1].Name,plan.Grainlines.Single().MarkerLayer,StringComparison.OrdinalIgnoreCase));
        var path=PathForTest();
        try
        {
            var expected=ProjectStorage.CaptureVersion(path);File.WriteAllText(path,"korunan");
            Assert.Throws<ProjectConflictException>(()=>DxfNotchReviewExport.Write(plan,path,expected));
            Assert.Equal("korunan",File.ReadAllText(path));Assert.True(DocumentSession.ContentEquals(before,project));
        }
        finally { if(File.Exists(path))File.Delete(path); }
    }
    [Fact]
    public void PlanIsDetachedAndNeverOverwritesDestination()
    {
        var project=Project();var plan=DxfNotchReviewExport.Prepare(project,false);var path=PathForTest();
        try
        {
            var expected=ProjectStorage.CaptureVersion(path);File.WriteAllText(path,"korunan");
            Assert.Throws<ProjectConflictException>(()=>DxfNotchReviewExport.Write(plan,path,expected));Assert.Equal("korunan",File.ReadAllText(path));
            project.Pieces[0].Notches![0].VertexIndex=1;Assert.False(plan.Matches(project));
        }
        finally { if(File.Exists(path))File.Delete(path); }
    }
    [Theory]
    [InlineData("Ön\u202Eparça")]
    [InlineData("Ön\u200Fparça")]
    [InlineData("Ön\u2066parça")]
    public void MisleadingContourLayerNamesAreRejectedWithoutChangingSource(string name)
    {
        var project=Project();project.Pieces[0].Name=name;var before=project.Clone();
        var error=Assert.Throws<InvalidDataException>(()=>DxfNotchReviewExport.Prepare(project,false));
        Assert.Contains("katman",error.Message);Assert.True(DocumentSession.ContentEquals(before,project));
    }
    [Fact]
    public void OrdinaryTurkishAndSupplementaryCharactersRemainUsable()
    {
        var project=Project();project.Pieces[0].Name="Ön İğne 😀";
        var plan=DxfNotchReviewExport.Prepare(project,false);
        Assert.Equal(3,plan.Items.Length);Assert.True(plan.Matches(project));
    }
    [Fact]
    public void CancellationAndInvalidMarksAreRejectedBeforeOutput()
    {
        var project=Project();using var cancel=new CancellationTokenSource();cancel.Cancel();
        Assert.Throws<OperationCanceledException>(()=>DxfNotchReviewExport.Prepare(project,false,cancel.Token));
        project.Pieces[0].Notches![0].Position=new(1,1);
        Assert.Throws<InvalidDataException>(()=>DxfNotchReviewExport.Prepare(project,false));
    }
    [Theory]
    [InlineData(false,"20")] [InlineData(true,"-20")]
    public void SelectedReviewIncludesOnlyChosenMarkedPieceAndBindsFullSource(bool flip,string y)
    {
        var source=Project();source.Pieces[0].Grainline=new Grainline { Start=new(2,3),End=new(4,8),NeedsReview=true };
        source.Pieces[1].Name="Gizli\u202Ekontrol";
        source.GradingRules.Add(new GradingRuleSet { SourcePieceId=source.Pieces[1].Id,SourceClosed=false,SourcePoints=[..source.Pieces[1].Points],Deltas=[new(0,0),new(0,0)] });
        source.ImportProvenance=new DxfImportProvenance { SourceFileName="source.dxf",SourceSha256=new string('A',64),SourceByteLength=12,MillimetersPerUnit=1 };
        source.NestingSnapshot=new NestingSnapshot { FabricWidth=100,UsedLength=100,LayoutFingerprint=new string('A',64),Placements=[new NestingPlacementSnapshot(Guid.NewGuid(),1,Guid.NewGuid())] };
        var before=source.Clone();var plan=DxfNotchReviewExport.PrepareSelected(source,[source.Pieces[0].Id],flip);
        Assert.Equal(2,plan.Items.Length);Assert.Single(plan.Grainlines);Assert.All(plan.Items,item=>Assert.Equal(source.Pieces[0].Id,item.PieceId));
        Assert.Contains(plan.Omissions,item=>item.Kind==DxfExportOmissionKind.GradingRules);
        Assert.Contains(plan.Omissions,item=>item.Kind==DxfExportOmissionKind.ImportProvenance);
        Assert.Contains(plan.Omissions,item=>item.Kind==DxfExportOmissionKind.NestingSnapshot);
        Assert.True(plan.Matches(before));Assert.True(DocumentSession.ContentEquals(before,source));
        var path=PathForTest();try
        {
            var text=Write(plan,path);Assert.Equal(2,text.Split("\nPOINT\n").Length-1);
            Assert.Contains($"100\nAcDbPoint\n10\n10\n20\n{y}\n",text);
            Assert.DoesNotContain("Gizli",text);
            Assert.Throws<InvalidDataException>(()=>DxfFormat.Export(source,path));
        }
        finally { if(File.Exists(path))File.Delete(path); }
        source.Pieces[1].Points[0]=new(11,20);Assert.False(plan.Matches(source));
    }

    [Fact]
    public void SelectedReviewRejectsDuplicatesMissingUnmarkedAndCancellation()
    {
        var source=Project();var before=source.Clone();var id=source.Pieces[0].Id;
        Assert.Throws<ArgumentException>(()=>DxfNotchReviewExport.PrepareSelected(source,[],false));
        Assert.Throws<ArgumentException>(()=>DxfNotchReviewExport.PrepareSelected(source,[id,id],false));
        Assert.Throws<ArgumentException>(()=>DxfNotchReviewExport.PrepareSelected(source,[Guid.NewGuid()],false));
        source.Pieces[1].Notches=null;
        Assert.Throws<ArgumentException>(()=>DxfNotchReviewExport.PrepareSelected(source,[source.Pieces[1].Id],false));
        using var cancellation=new CancellationTokenSource();cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(()=>DxfNotchReviewExport.PrepareSelected(before,[id],false,cancellation.Token));
    }

    [Theory]
    [InlineData(false,"21")] [InlineData(true,"-21")]
    public void MixedSelectionInventoriesAndExportsUnmarkedContourWithGrainline(bool flip,string grainY)
    {
        var source=Project();source.Pieces[1].Notches=null;
        source.Pieces[1].Grainline=new Grainline { Start=new(12,21),End=new(15,24),NeedsReview=true };
        var before=source.Clone();var selected=source.Pieces.Select(piece=>piece.Id).ToArray();
        var plan=DxfNotchReviewExport.PrepareSelected(source,selected,flip);
        Assert.Equal(2,plan.Contours.Length);Assert.Equal(selected,plan.Contours.Select(item=>item.PieceId));
        var unmarked=plan.Contours.Single(item=>item.PieceId==source.Pieces[1].Id);
        Assert.Equal(0,unmarked.MarkerCount);Assert.True(unmarked.HasGrainline);
        Assert.All(plan.Items,item=>Assert.Equal(source.Pieces[0].Id,item.PieceId));
        Assert.Single(plan.Grainlines);Assert.True(plan.Matches(before));
        var path=PathForTest();try
        {
            var text=Write(plan,path);
            Assert.Equal(2,text.Split("\nLWPOLYLINE\n").Length-1);
            Assert.Equal(2,text.Split("\nPOINT\n").Length-1);
            Assert.Equal(1,text.Split("\nLINE\n").Length-1);
            Assert.Contains($"100\nAcDbLine\n10\n12\n20\n{grainY}\n",text);
            Assert.True(DocumentSession.ContentEquals(before,source));
            Assert.Throws<InvalidDataException>(()=>DxfFormat.Export(source,path));
        }
        finally { if(File.Exists(path))File.Delete(path); }
    }

}
