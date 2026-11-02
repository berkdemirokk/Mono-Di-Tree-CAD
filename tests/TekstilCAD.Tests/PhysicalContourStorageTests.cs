using System.Text.Json.Nodes;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;
using TekstilCAD.Patterns;
using TekstilCAD.Grading;
using TekstilCAD.Nesting;

namespace TekstilCAD.Tests;

public class PhysicalContourStorageTests
{
    [Fact] public void PublishedLegacyFixtureHasUnknownPhysicalFeatures()
    {
        var root=new DirectoryInfo(AppContext.BaseDirectory);
        while(root is not null && !File.Exists(Path.Combine(root.FullName,"AGENTS.md")))root=root.Parent;
        Assert.NotNull(root);
        var path=Path.Combine(root!.FullName,"tests","TekstilCAD.Tests","Fixtures","studio-0.3-native.tcad");
        var doc=ProjectStorage.Load(path);
        Assert.All(doc.Pieces,p=>Assert.Null(p.PhysicalFeatures));
        var saved=Path.GetTempFileName();
        try { ProjectStorage.Save(doc,saved); Assert.True(DocumentSession.ContentEquals(doc,ProjectStorage.Load(saved))); }
        finally { File.Delete(saved); }
    }
    private static ProjectDocument Document()
    {
        var p=new PatternPiece { Points=[new(0,0),new(4,0),new(5,1),new(6,0),new(10,0),new(10,10),new(0,10)] };
        p.PhysicalFeatures=PhysicalContourFeatures.Create(p,[2]);
        return new() { Pieces=[p] };
    }

    [Fact] public void RoundTripAndOldProjectPreserveMeaning()
    {
        var path=Path.GetTempFileName();
        try
        {
            var doc=Document(); ProjectStorage.Save(doc,path);
            Assert.Equal(10,JsonNode.Parse(File.ReadAllText(path))![0]!.GetValue<int>());
            var loaded=ProjectStorage.Load(path);
            Assert.True(DocumentSession.ContentEquals(doc,loaded));
            loaded.Pieces[0].PhysicalFeatures!.EdgeIds[0]=Guid.NewGuid();
            Assert.NotEqual(doc.Pieces[0].PhysicalFeatures!.EdgeIds[0],loaded.Pieces[0].PhysicalFeatures!.EdgeIds[0]);
            doc.Pieces[0].PhysicalFeatures=null; ProjectStorage.Save(doc,path);
            Assert.Null(ProjectStorage.Load(path).Pieces[0].PhysicalFeatures);
        }
        finally { File.Delete(path); }
    }

    [Fact] public void RejectMissingIdentityAndWrongVersion()
    {
        var path=Path.GetTempFileName();
        try
        {
            ProjectStorage.Save(Document(),path);
            var json=JsonNode.Parse(File.ReadAllText(path))!;
            json[1]!["Pieces"]![0]!["PhysicalFeatures"]!.AsObject().Remove("OwnerPieceId");
            File.WriteAllText(path,json.ToJsonString());
            Assert.Throws<InvalidDataException>(()=>ProjectStorage.Load(path));
            ProjectStorage.Save(Document(),path); json=JsonNode.Parse(File.ReadAllText(path))!;
            json[0]=7; File.WriteAllText(path,json.ToJsonString());
            Assert.Throws<InvalidDataException>(()=>ProjectStorage.Load(path));
        }
        finally { File.Delete(path); }
    }

    [Fact] public void SessionRejectsGeometryDeletionAndCopyAndUndoPreservesFeatures()
    {
        var original=Document(); var session=new DocumentSession(original);
        Assert.Throws<InvalidOperationException>(()=>session.Execute(d=>d.Pieces[0].Points[0]=new(1,1)));
        Assert.Throws<InvalidOperationException>(()=>session.Execute(d=>d.Pieces.Clear()));
        Assert.Throws<InvalidOperationException>(()=>session.Execute(d=> { var p=d.Pieces[0].Clone(); p.Id=Guid.NewGuid();d.Pieces.Add(p); }));
        Assert.Throws<InvalidOperationException>(()=>session.Execute(_=>{},d=>d.Pieces[0].PhysicalFeatures=null));
        Assert.True(session.Execute(d=>d.Pieces[0].Name="Yeni"));
        Assert.True(session.Undo()); Assert.True(DocumentSession.ContentEquals(original,session.Document));
        Assert.True(session.Redo()); Assert.Equal("Yeni",session.Document.Pieces[0].Name);
        Assert.Equal(original.Pieces[0].PhysicalFeatures!.OwnerPieceId,session.Document.Pieces[0].PhysicalFeatures!.OwnerPieceId);
    }
    [Fact] public void ConvexCornerCannotBeDeclaredAsPhysicalVAndPatternToolsDoNotDropFeatures()
    {
        var doc=Document(); var piece=doc.Pieces[0]; var physical=piece.PhysicalFeatures!;
        physical.Notches[0].FirstEdgeId=physical.EdgeIds[4]; physical.Notches[0].SecondEdgeId=physical.EdgeIds[5];
        Assert.Throws<InvalidDataException>(()=>ProjectStorage.Validate(doc));
        doc=Document(); piece=doc.Pieces[0];
        physical=piece.PhysicalFeatures!;
        physical.Notches.Add(new() { Id=Guid.NewGuid(), FirstEdgeId=physical.EdgeIds[1], SecondEdgeId=physical.EdgeIds[2] });
        Assert.Throws<InvalidDataException>(()=>ProjectStorage.Validate(doc));
        doc=Document(); piece=doc.Pieces[0];
        Assert.Throws<ArgumentException>(()=>PatternTools.Offset(piece,0));
        Assert.Throws<ArgumentException>(()=>PatternTools.DeletePoint(piece,0));
    }
    [Fact] public void DxfAnalysisAndExportRejectPhysicalIdentityLoss()
    {
        var doc=Document(); var path=Path.GetTempFileName();
        try
        {
            File.WriteAllText(path,"preserve");
            Assert.Throws<InvalidDataException>(()=>DxfFormat.AnalyzeExport(doc));
            Assert.Throws<InvalidDataException>(()=>DxfFormat.Export(doc,path));
            Assert.Equal("preserve",File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }
    [Fact] public void GradingAndPathExtractionRejectPhysicalIdentityLoss()
    {
        var piece=Document().Pieces[0]; var increments=piece.Points.Select(_=>new Point2(0,0)).ToArray();
        Assert.Throws<ArgumentException>(()=>GradingEngine.Generate(piece,increments,0,"M"));
        Assert.Throws<ArgumentException>(()=>GradingEngine.GenerateSizes(piece,increments,[new GradeSize("M",0)]));
        Assert.Throws<ArgumentException>(()=>ContourPathExtraction.Extract(piece,1,3));
    }
    [Fact] public void PhysicalIdsMustBeUniqueAcrossPieces()
    {
        var doc=Document(); var other=Document().Pieces[0];
        other.PhysicalFeatures!.EdgeIds[0]=doc.Pieces[0].PhysicalFeatures!.EdgeIds[0];
        doc.Pieces.Add(other);
        Assert.Throws<InvalidDataException>(()=>ProjectStorage.Validate(doc));
    }
    [Fact] public void RotationAndNestingRejectPhysicalIdentityLossWithoutChangingInput()
    {
        var doc=Document(); var before=doc.Clone(); var piece=doc.Pieces[0];
        Assert.Throws<ArgumentException>(()=>OrthogonalRotation.Rotate(piece,0));
        Assert.Throws<ArgumentException>(()=>OrthogonalRotation.Rotate(piece,90));
        Assert.ThrowsAny<ArgumentException>(()=>NestingEngine.Arrange([piece],50,1));
        piece.Metadata=new() { Role="Kesim", Quantity=1, Material="Kumaş" };
        piece.Grainline=new() { Start=new(0,0), End=new(0,1) };
        Assert.ThrowsAny<ArgumentException>(()=>ProductionNestingPlan.Arrange(doc,[piece.Id],50,1));
        Assert.ThrowsAny<ArgumentException>(()=>GrainConstrainedNesting.Arrange(doc,[piece.Id],50,1,100,0,GrainDirectionPolicy.Direction.OneWay,0));
        Assert.ThrowsAny<ArgumentException>(()=>OrthogonalGrainNesting.Arrange(doc,[piece.Id],50,1,100,0,[0],GrainDirectionPolicy.Direction.OneWay,0));
        Assert.ThrowsAny<ArgumentException>(()=>PerCopyOrthogonalNesting.Arrange(doc,[piece.Id],new Dictionary<(Guid,int),int> { [(piece.Id,1)]=0 },50,1,100,0,GrainDirectionPolicy.Direction.OneWay,0));
        Assert.ThrowsAny<ArgumentException>(()=>AutomaticPerCopyNesting.Arrange(doc,[piece.Id],new Dictionary<Guid,IReadOnlyList<int>> { [piece.Id]=new[] { 0 } },50,1,100,0,GrainDirectionPolicy.Direction.OneWay,0));
        piece.Metadata=null;
        piece.Grainline=null;
        Assert.True(DocumentSession.ContentEquals(before,doc));
    }
    [Fact] public void SvgHpglAndPdfRejectPhysicalIdentityLossWithoutWriting()
    {
        var doc=Document(); var path=Path.GetTempFileName();
        try
        {
            File.WriteAllText(path,"preserve");
            Assert.Throws<InvalidDataException>(()=>SvgFormat.Export(doc,path));
            Assert.Throws<InvalidDataException>(()=>HpglFormat.Export(doc,path));
            var version=ProjectStorage.CaptureVersion(path);
            Assert.Throws<InvalidDataException>(()=>PdfFormat.Export(doc,path,version));
            Assert.Throws<InvalidDataException>(()=>PdfTiledFormat.Export(doc,path,version));
            Assert.Equal("preserve",File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }
}
