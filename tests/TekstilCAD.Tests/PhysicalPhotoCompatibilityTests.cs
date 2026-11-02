using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;
using TekstilCAD.Nesting;
using Xunit.Abstractions;

namespace TekstilCAD.Tests;

public sealed class HistoricalReaderFactAttribute : FactAttribute
{
    public HistoricalReaderFactAttribute()
    {
        if(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TEKSTILCAD_OLD_PHYSICAL_READER")) ||
           string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TEKSTILCAD_OLD_PHOTO_READER")))
            Skip="Historical reader binaries were not configured; cross-reader compatibility is unverified.";
    }
}

public sealed class PhysicalPhotoCompatibilityTests : IDisposable
{
    private readonly ITestOutputHelper output;
    private readonly IDisposable versionScope=ProjectStorage.EnableVersion10ForVerification();
    public PhysicalPhotoCompatibilityTests(ITestOutputHelper output) => this.output=output;
    public void Dispose()=>versionScope.Dispose();
    private const string Piece="11111111-1111-4111-8111-111111111111";
    private const string Edge1="22222222-2222-4222-8222-222222222221";
    private const string Edge2="22222222-2222-4222-8222-222222222222";
    private const string Edge3="22222222-2222-4222-8222-222222222223";
    private const string Edge4="22222222-2222-4222-8222-222222222224";
    private const string Edge5="22222222-2222-4222-8222-222222222225";
    private const string Edge6="22222222-2222-4222-8222-222222222226";
    private const string Edge7="22222222-2222-4222-8222-222222222227";
    private const string Notch="33333333-3333-4333-8333-333333333333";
    private static JsonNode Physical8()=>JsonNode.Parse($$$$"""
      [8,{"Name":"synthetic", "Pieces":[{"Id":"{{{{Piece}}}}","Name":"piece","Closed":true,
        "Points":[{"X":0,"Y":0},{"X":4,"Y":0},{"X":5,"Y":1},{"X":6,"Y":0},{"X":10,"Y":0},{"X":10,"Y":10},{"X":0,"Y":10}],
        "PhysicalFeatures":{"OwnerPieceId":"{{{{Piece}}}}",
          "SourcePoints":[{"X":0,"Y":0},{"X":4,"Y":0},{"X":5,"Y":1},{"X":6,"Y":0},{"X":10,"Y":0},{"X":10,"Y":10},{"X":0,"Y":10}],
          "EdgeIds":["{{{{Edge1}}}}","{{{{Edge2}}}}","{{{{Edge3}}}}","{{{{Edge4}}}}","{{{{Edge5}}}}","{{{{Edge6}}}}","{{{{Edge7}}}}"],
          "Notches":[{"Id":"{{{{Notch}}}}","FirstEdgeId":"{{{{Edge2}}}}","SecondEdgeId":"{{{{Edge3}}}}"}]}}],"GradingRules":[]}]
      """)!;
    internal static JsonNode Photo8()=>JsonNode.Parse($$$$"""
      [8,{"Name":"synthetic", "Pieces":[{"Id":"{{{{Piece}}}}","Name":"piece","Closed":false,"Points":[{"X":0,"Y":0},{"X":10,"Y":0}]}],
        "GradingRules":[],"PhotoTraceHistory":[{"AlgorithmVersion":1,"PieceId":"{{{{Piece}}}}",
        "SourceSha256":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA","SourceByteLength":100,"SourceOrientation":1,
        "SourceWidth":20,"SourceHeight":20,"PreviewWidth":20,"PreviewHeight":20,"ReferenceStart":{"X":0,"Y":0},
        "ReferenceEnd":{"X":10,"Y":0},"ReferenceLengthMm":10,"TracePixels":[{"X":0,"Y":0},{"X":10,"Y":0}],
        "GeneratedPoints":[{"X":0,"Y":0},{"X":10,"Y":0}]}]}]
      """)!;
    internal static JsonNode Plane9()
    {
        var result=Photo8();result[0]=9;var history=result[1]!["PhotoTraceHistory"]![0]!;history["AlgorithmVersion"]=2;
        history["PlaneCalibration"]=JsonNode.Parse("{\"Corners\":[{\"X\":0,\"Y\":0},{\"X\":10,\"Y\":0},{\"X\":10,\"Y\":10},{\"X\":0,\"Y\":10}],\"WidthMm\":10,\"HeightMm\":10}");
        return result;
    }
    private static JsonNode Combined10()
    {
        var physical=Physical8();physical[0]=10;physical[1]!["PhotoTraceHistory"]=Plane9()[1]!["PhotoTraceHistory"]!.DeepClone();return physical;
    }
    private static ProjectDocument Read(JsonNode root)=>ProjectStorage.DeserializeProject(Encoding.UTF8.GetBytes(root.ToJsonString()));
    private static ProjectDocument RotatedCombined()
    {
        var piece=new PatternPiece { Points=[new(0,0),new(6,0),new(6,4),new(0,4)],
            Metadata=new PieceMetadata { Role="Kesim",Material="Pamuk",Quantity=2 },Grainline=new Grainline { Start=new(1,1),End=new(1,2) } };
        var source=new ProjectDocument { Pieces=[piece] };var angles=new Dictionary<(Guid,int),int> { [(piece.Id,1)]=0,[(piece.Id,2)]=90 };
        var result=PerCopyOrthogonalNesting.Arrange(source,[piece.Id],angles,100,1,100,0,GrainDirectionPolicy.Direction.TwoWay,90);
        var trace=RotatedNestingRecord.Capture(source,[piece.Id],angles,result,GrainDirectionPolicy.Direction.TwoWay,90,100,1,0,100).ToTrace();
        var layout=result.Plan.Nesting.Layout.Clone();
        layout.NestingSnapshot=new NestingSnapshot { FabricWidth=100,Gap=1,EdgeMargin=0,UsedLength=result.Plan.Nesting.UsedLength,
            LayoutFingerprint=trace.LayoutFingerprint,Placements=result.Plan.Placements.Select(p=>new NestingPlacementSnapshot(p.SourcePieceId,p.CopyNumber,p.PlacedPieceId)).ToList(),RotationTrace=trace };
        layout.Pieces.Add(Read(Physical8()).Pieces[0]);layout.PhotoTraceHistory=Read(Plane9()).PhotoTraceHistory;
        return layout;
    }

    [Fact] public void VersionTenKeepsPhysicalPhotoAndExistingRotatedHistory()
    {
        var doc=RotatedCombined();var json=ProjectStorage.SerializeProject(doc);Assert.Equal(10,JsonNode.Parse(json)![0]!.GetValue<int>());
        var loaded=ProjectStorage.DeserializeProject(Encoding.UTF8.GetBytes(json));
        Assert.True(DocumentSession.ContentEquals(doc,loaded));Assert.NotNull(loaded.NestingSnapshot!.RotationTrace);
        Assert.Equal(Guid.Parse(Notch),loaded.Pieces.Last().PhysicalFeatures!.Notches[0].Id);
        Assert.Equal(2,Assert.Single(loaded.PhotoTraceHistory!).AlgorithmVersion);
    }

    [Fact] public void HandWrittenHistoricalAndCombinedFixturesKeepIndependentFieldOracle()
    {
        foreach(var node in new[]{Physical8(),Photo8(),Plane9(),Combined10()})
        {
            var source=node.ToJsonString();var bytes=Encoding.UTF8.GetBytes(source);var project=ProjectStorage.DeserializeProject(bytes);
            Assert.Equal(source,Encoding.UTF8.GetString(bytes));Assert.Equal("synthetic",project.Name);Assert.Single(project.Pieces);
            Assert.Equal(Guid.Parse(Piece),project.Pieces[0].Id);Assert.Equal("piece",project.Pieces[0].Name);
            if(node[1]!["Pieces"]![0]!["PhysicalFeatures"] is not null)
            {
                var p=project.Pieces[0];var f=Assert.IsType<PhysicalContourFeatures>(p.PhysicalFeatures);
                var expected=new[]{new Point2(0,0),new Point2(4,0),new Point2(5,1),new Point2(6,0),new Point2(10,0),new Point2(10,10),new Point2(0,10)};
                Assert.True(p.Closed);Assert.Equal(expected,p.Points);Assert.Equal(expected,f.SourcePoints);
                Assert.Equal(new[]{Edge1,Edge2,Edge3,Edge4,Edge5,Edge6,Edge7}.Select(Guid.Parse),f.EdgeIds);
                Assert.Equal(Guid.Parse(Piece),f.OwnerPieceId);var notch=Assert.Single(f.Notches);
                Assert.Equal(Guid.Parse(Notch),notch.Id);Assert.Equal(Guid.Parse(Edge2),notch.FirstEdgeId);Assert.Equal(Guid.Parse(Edge3),notch.SecondEdgeId);
            }
            if(node[1]!["PhotoTraceHistory"] is not null)
            {
                var photo=Assert.Single(project.PhotoTraceHistory!);Assert.Equal(Guid.Parse(Piece),photo.PieceId);
                Assert.Equal(node[1]!["PhotoTraceHistory"]![0]!["AlgorithmVersion"]!.GetValue<int>(),photo.AlgorithmVersion);
                Assert.Equal(new string('A',64),photo.SourceSha256);Assert.Equal(100,photo.SourceByteLength);Assert.Equal(1,photo.SourceOrientation);
                Assert.Equal(20,photo.SourceWidth);Assert.Equal(20,photo.SourceHeight);Assert.Equal(20,photo.PreviewWidth);Assert.Equal(20,photo.PreviewHeight);
                Assert.Equal(new Point2(0,0),photo.ReferenceStart);Assert.Equal(new Point2(10,0),photo.ReferenceEnd);Assert.Equal(10,photo.ReferenceLengthMm);
                Assert.Equal(new[]{new Point2(0,0),new Point2(10,0)},photo.TracePixels);Assert.Equal(photo.TracePixels,photo.GeneratedPoints);
                if(photo.AlgorithmVersion==2) { Assert.Equal(10,photo.PlaneCalibration!.WidthMm);Assert.Equal(10,photo.PlaneCalibration.HeightMm);Assert.Equal(new[]{new Point2(0,0),new Point2(10,0),new Point2(10,10),new Point2(0,10)},photo.PlaneCalibration.Corners); }
                else { Assert.Equal(1,photo.AlgorithmVersion);Assert.Null(photo.PlaneCalibration); }
            }
            var emitted=ProjectStorage.SerializeProject(project);Assert.Equal(10,JsonNode.Parse(emitted)![0]!.GetValue<int>());
            var restored=ProjectStorage.DeserializeProject(Encoding.UTF8.GetBytes(emitted));
            Assert.True(DocumentSession.ContentEquals(project,restored));Assert.Equal("synthetic",restored.Name);
            var rp=Assert.Single(restored.Pieces);Assert.Equal(Guid.Parse(Piece),rp.Id);Assert.Equal("piece",rp.Name);
            if(node[1]!["Pieces"]![0]!["PhysicalFeatures"] is not null)
            {
                var expected=new[]{new Point2(0,0),new Point2(4,0),new Point2(5,1),new Point2(6,0),new Point2(10,0),new Point2(10,10),new Point2(0,10)};
                Assert.True(rp.Closed);Assert.Equal(expected,rp.Points);var f=Assert.IsType<PhysicalContourFeatures>(rp.PhysicalFeatures);
                Assert.Equal(Guid.Parse(Piece),f.OwnerPieceId);Assert.Equal(expected,f.SourcePoints);
                Assert.Equal(new[]{Edge1,Edge2,Edge3,Edge4,Edge5,Edge6,Edge7}.Select(Guid.Parse),f.EdgeIds);
                var n=Assert.Single(f.Notches);Assert.Equal(Guid.Parse(Notch),n.Id);Assert.Equal(Guid.Parse(Edge2),n.FirstEdgeId);Assert.Equal(Guid.Parse(Edge3),n.SecondEdgeId);
            }
            else { Assert.False(rp.Closed);Assert.Equal(new[]{new Point2(0,0),new Point2(10,0)},rp.Points);Assert.Null(rp.PhysicalFeatures); }
            if(node[1]!["PhotoTraceHistory"] is not null)
            {
                var p=Assert.Single(restored.PhotoTraceHistory!);Assert.Equal(node[1]!["PhotoTraceHistory"]![0]!["AlgorithmVersion"]!.GetValue<int>(),p.AlgorithmVersion);
                Assert.Equal(Guid.Parse(Piece),p.PieceId);Assert.Equal(new string('A',64),p.SourceSha256);Assert.Equal(100,p.SourceByteLength);Assert.Equal(1,p.SourceOrientation);
                Assert.Equal(20,p.SourceWidth);Assert.Equal(20,p.SourceHeight);Assert.Equal(20,p.PreviewWidth);Assert.Equal(20,p.PreviewHeight);
                Assert.Equal(new Point2(0,0),p.ReferenceStart);Assert.Equal(new Point2(10,0),p.ReferenceEnd);Assert.Equal(10,p.ReferenceLengthMm);
                var points=new[]{new Point2(0,0),new Point2(10,0)};Assert.Equal(points,p.TracePixels);Assert.Equal(points,p.GeneratedPoints);
                if(p.AlgorithmVersion==2) { Assert.Equal(10,p.PlaneCalibration!.WidthMm);Assert.Equal(10,p.PlaneCalibration.HeightMm);Assert.Equal(new[]{new Point2(0,0),new Point2(10,0),new Point2(10,10),new Point2(0,10)},p.PlaneCalibration.Corners); }
                else Assert.Null(p.PlaneCalibration);
            }
            else Assert.Null(restored.PhotoTraceHistory);
        }
    }

    [Fact] public void AmbiguousMixedMissingAndMalformedVariantsFailClosed()
    {
        var both=Physical8();both[1]!["PhotoTraceHistory"]=Photo8()[1]!["PhotoTraceHistory"]!.DeepClone();Assert.Throws<InvalidDataException>(()=>Read(both));
        var none=Photo8();none[1]!.AsObject().Remove("PhotoTraceHistory");Assert.Throws<InvalidDataException>(()=>Read(none));
        var bad=Physical8();bad[1]!["Pieces"]![0]!["PhysicalFeatures"]=null;Assert.Throws<InvalidDataException>(()=>Read(bad));
        bad=Photo8();bad[1]!["PhotoTraceHistory"]=null;Assert.Throws<InvalidDataException>(()=>Read(bad));
        bad=Photo8();bad[1]!["PhotoTraceHistory"]=new JsonArray();Assert.Throws<InvalidDataException>(()=>Read(bad));
        bad=Physical8();bad[1]!["Pieces"]![0]!["PhysicalFeatures"]!["OwnerPieceId"]=Guid.Empty.ToString();Assert.Throws<InvalidDataException>(()=>Read(bad));
        bad=Physical8();bad[1]!["Pieces"]![0]!["PhysicalFeatures"]!["EdgeIds"]![1]=Edge1;Assert.Throws<InvalidDataException>(()=>Read(bad));
        bad=Photo8();bad[1]!["PhotoTraceHistory"]![0]!["Unexpected"]=1;Assert.Throws<InvalidDataException>(()=>Read(bad));
        bad=Photo8();bad[1]!["PhotoTraceHistory"]![0]!["ReferenceStart"]!.AsObject().Remove("X");Assert.Throws<InvalidDataException>(()=>Read(bad));
        bad=Plane9();bad[0]=8;Assert.Throws<InvalidDataException>(()=>Read(bad));
        bad=Physical8();bad[0]=9;Assert.Throws<InvalidDataException>(()=>Read(bad));
        bad=Combined10();bad[0]=8;Assert.Throws<InvalidDataException>(()=>Read(bad));
        bad=Combined10();bad[0]=9;Assert.Throws<InvalidDataException>(()=>Read(bad));
        bad=Physical8();bad[1]!["Pieces"]![0]!["PhysicalFeatures"]!["Unexpected"]=1;Assert.Throws<InvalidDataException>(()=>Read(bad));
        bad=Combined10();bad[1]!["Unexpected"]=1;Assert.Throws<InvalidDataException>(()=>Read(bad));
        bad=Combined10();bad[1]!["PhotoTraceHistory"]![0]!["PlaneCalibration"]!["Corners"]![0]!["Unexpected"]=1;Assert.Throws<InvalidDataException>(()=>Read(bad));
        bad=Photo8();bad[0]=10;bad[1]!.AsObject().Remove("PhotoTraceHistory");Assert.Throws<InvalidDataException>(()=>Read(bad));
        var duplicate=Photo8().ToJsonString().Replace("\"AlgorithmVersion\":1","\"AlgorithmVersion\":1,\"AlgorithmVersion\":1");
        Assert.Throws<InvalidDataException>(()=>ProjectStorage.DeserializeProject(Encoding.UTF8.GetBytes(duplicate)));
    }
    [Fact] public void CombinedHistoryClonesAndRejectsLossAcrossUndo()
    {
        var original=Read(Combined10());var session=new DocumentSession(original);
        Assert.False(new DocumentHistory(maximumPoints:31).CanRetainSnapshot(original));
        Assert.True(new DocumentHistory(maximumPoints:32).CanRetainSnapshot(original));
        Assert.Throws<InvalidOperationException>(()=>session.Execute(d=>d.PhotoTraceHistory=null));
        Assert.Throws<InvalidOperationException>(()=>session.Execute(d=>d.Pieces[0].PhysicalFeatures=null));
        Assert.True(session.Execute(d=>d.Name="renamed",ProjectStorage.Validate));
        Assert.True(session.Undo());Assert.True(DocumentSession.ContentEquals(original,session.Document));
        Assert.True(session.Redo());Assert.Equal("renamed",session.Document.Name);
        var clone=session.Document.Clone();clone.PhotoTraceHistory![0].PlaneCalibration!.Corners[0]=new(1,1);
        Assert.NotEqual(clone.PhotoTraceHistory[0].PlaneCalibration!.Corners[0],session.Document.PhotoTraceHistory![0].PlaneCalibration!.Corners[0]);
    }

    [Fact] public void SyntheticOpenMigrationConflictCancellationAndInvalidSavePreserveBytes()
    {
        var dir=Path.Combine(Path.GetTempPath(),"schema-prototype-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        var source=Path.Combine(dir,"source.tcad");var target=Path.Combine(dir,"target.tcad");
        try
        {
            File.WriteAllText(source,Photo8().ToJsonString());var original=File.ReadAllBytes(source);
            var document=ProjectStorage.Load(source);Assert.Equal(original,File.ReadAllBytes(source));
            var absent=ProjectStorage.CaptureVersion(target);ProjectStorage.SaveIfUnchanged(document,target,absent);
            Assert.Equal(original,File.ReadAllBytes(source));Assert.Equal(10,JsonNode.Parse(File.ReadAllText(target))![0]!.GetValue<int>());
            var observed=ProjectStorage.CaptureVersion(target);File.WriteAllText(target,"external change");
            Assert.Throws<ProjectConflictException>(()=>ProjectStorage.SaveIfUnchanged(document,target,observed));
            Assert.Equal("external change",File.ReadAllText(target));
            var fresh=ProjectStorage.CaptureVersion(target);using var cancelled=new CancellationTokenSource();cancelled.Cancel();
            Assert.ThrowsAny<OperationCanceledException>(()=>ProjectStorage.SaveIfUnchanged(document,target,fresh,cancelled.Token));
            Assert.Equal("external change",File.ReadAllText(target));
            document.PhotoTraceHistory![0].SourceSha256="invalid";
            Assert.Throws<InvalidDataException>(()=>ProjectStorage.Save(document,target));Assert.Equal("external change",File.ReadAllText(target));
            Assert.Equal(original,File.ReadAllBytes(source));
            document=ProjectStorage.Load(source);var sourceVersion=ProjectStorage.CaptureVersion(source);
            ProjectStorage.SaveIfUnchanged(document,source,sourceVersion);Assert.Equal(10,JsonNode.Parse(File.ReadAllText(source))![0]!.GetValue<int>());
        }
        finally { File.Delete(source);File.Delete(target);Directory.Delete(dir); }
    }
    [Fact] public void PhotoHistoryCannotBeSilentlyDroppedByExporters()
    {
        var doc=Read(Photo8());var path=Path.GetTempFileName();
        try
        {
            File.WriteAllText(path,"preserve");var version=ProjectStorage.CaptureVersion(path);
            Assert.Throws<InvalidDataException>(()=>DxfFormat.Export(doc,path));
            Assert.Throws<InvalidDataException>(()=>SvgFormat.Export(doc,path));
            Assert.Throws<InvalidDataException>(()=>HpglFormat.Export(doc,path));
            Assert.Throws<InvalidDataException>(()=>PdfFormat.Export(doc,path,version));
            Assert.Throws<InvalidDataException>(()=>PdfTiledFormat.Export(doc,path,version));
            Assert.Equal("preserve",File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }

    [HistoricalReaderFact] public void ActualOldReadersAcceptOnlyTheirOwnHistoricalVariantsAndNeverWrite()
    {
        var physical=Environment.GetEnvironmentVariable("TEKSTILCAD_OLD_PHYSICAL_READER");var photo=Environment.GetEnvironmentVariable("TEKSTILCAD_OLD_PHOTO_READER");
        Assert.True(File.Exists(physical),"Historical physical reader binary was not found.");
        Assert.True(File.Exists(photo),"Historical photo reader binary was not found.");
        static string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        var physicalHash=Hash(physical!);var photoHash=Hash(photo!);
        var results=new List<object>();
        output.WriteLine($"Physical harness: {Path.GetFullPath(physical!)} SHA256={physicalHash}");
        output.WriteLine($"Photo harness: {Path.GetFullPath(photo!)} SHA256={photoHash}");
        output.WriteLine($"Physical FileFormats SHA256={Hash(Path.Combine(Path.GetDirectoryName(physical!)!,"TekstilCAD.FileFormats.dll"))}");
        output.WriteLine($"Photo FileFormats SHA256={Hash(Path.Combine(Path.GetDirectoryName(photo!)!,"TekstilCAD.FileFormats.dll"))}");
        var dotnet=Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
        var mixed8=Combined10();mixed8[0]=8;var physical10=Physical8();physical10[0]=10;var photo10=Photo8();photo10[0]=10;
        var rotated=JsonNode.Parse(ProjectStorage.SerializeProject(RotatedCombined()))!;
        foreach(var (fixture,kind,pAccept,hAccept) in new[]{(Physical8(),"physical-v8",true,false),(Photo8(),"photo-v8",false,true),(Plane9(),"photo-plane-v9",false,true),
            (Combined10(),"combined-v10",false,false),(physical10,"physical-v10",false,false),(photo10,"photo-v10",false,false),(mixed8,"ambiguous-v8",false,false),(rotated,"combined-v10-rotation",false,false)})
        {
            var path=Path.GetTempFileName();
            try
            {
                File.WriteAllText(path,fixture.ToJsonString());var before=File.ReadAllBytes(path);
                foreach(var (reader,accept) in new[]{(physical!,pAccept),(photo!,hAccept)})
                {
                    var info=new ProcessStartInfo(dotnet) { UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true };
                    info.ArgumentList.Add(reader);info.ArgumentList.Add(path);
                    using var process=Process.Start(info)!;var stdout=process.StandardOutput.ReadToEnd();var stderr=process.StandardError.ReadToEnd();process.WaitForExit();
                    Assert.True(process.ExitCode==(accept ? 0 : 2),$"Reader returned {process.ExitCode}: {stdout} {stderr}");
                    if(accept)
                    {
                        Assert.StartsWith("ACCEPT ",stdout);var decoded=JsonNode.Parse(stdout[7..])!;
                        Assert.Equal("synthetic",decoded["Name"]!.GetValue<string>());Assert.Empty(decoded["GradingRules"]!.AsArray());
                        var piece=Assert.Single(decoded["Pieces"]!.AsArray())!;
                        Assert.Equal(Piece,piece["Id"]!.GetValue<string>());Assert.Equal("piece",piece["Name"]!.GetValue<string>());
                        static void Point(JsonNode? value,double x,double y) { Assert.Equal(x,value!["X"]!.GetValue<double>());Assert.Equal(y,value["Y"]!.GetValue<double>()); }
                        if(reader==physical)
                        {
                            Assert.True(piece["Closed"]!.GetValue<bool>());
                            var expected=new[]{(0d,0d),(4d,0d),(5d,1d),(6d,0d),(10d,0d),(10d,10d),(0d,10d)};
                            var points=piece["Points"]!.AsArray();Assert.Equal(expected.Length,points.Count);
                            for(var i=0;i<expected.Length;i++)Point(points[i],expected[i].Item1,expected[i].Item2);
                            var features=piece["PhysicalFeatures"]!;
                            Assert.Equal(Piece,features["OwnerPieceId"]!.GetValue<string>());
                            var source=features["SourcePoints"]!.AsArray();Assert.Equal(expected.Length,source.Count);
                            for(var i=0;i<expected.Length;i++)Point(source[i],expected[i].Item1,expected[i].Item2);
                            Assert.Equal(new[]{Edge1,Edge2,Edge3,Edge4,Edge5,Edge6,Edge7},features["EdgeIds"]!.AsArray().Select(x=>x!.GetValue<string>()));
                            var notch=Assert.Single(features["Notches"]!.AsArray())!;Assert.Equal(Notch,notch["Id"]!.GetValue<string>());
                            Assert.Equal(Edge2,notch["FirstEdgeId"]!.GetValue<string>());Assert.Equal(Edge3,notch["SecondEdgeId"]!.GetValue<string>());
                        }
                        else
                        {
                            Assert.False(piece["Closed"]!.GetValue<bool>());var geometry=piece["Points"]!.AsArray();Assert.Equal(2,geometry.Count);Point(geometry[0],0,0);Point(geometry[1],10,0);
                            var history=Assert.Single(decoded["PhotoTraceHistory"]!.AsArray())!;
                            Assert.Equal(Piece,history["PieceId"]!.GetValue<string>());
                            Assert.Equal(new string('A',64),history["SourceSha256"]!.GetValue<string>());
                            Assert.Equal(100,history["SourceByteLength"]!.GetValue<int>());Assert.Equal(1,history["SourceOrientation"]!.GetValue<int>());
                            foreach(var key in new[]{"SourceWidth","SourceHeight","PreviewWidth","PreviewHeight"})Assert.Equal(20,history[key]!.GetValue<int>());
                            Point(history["ReferenceStart"],0,0);Point(history["ReferenceEnd"],10,0);Assert.Equal(10,history["ReferenceLengthMm"]!.GetValue<double>());
                            foreach(var key in new[]{"TracePixels","GeneratedPoints"}) { var points=history[key]!.AsArray();Assert.Equal(2,points.Count);Point(points[0],0,0);Point(points[1],10,0); }
                            Assert.Equal(fixture[1]!["PhotoTraceHistory"]![0]!["AlgorithmVersion"]!.GetValue<int>(),history["AlgorithmVersion"]!.GetValue<int>());
                            if(history["AlgorithmVersion"]!.GetValue<int>()==2)
                            {
                                var plane=history["PlaneCalibration"]!;Assert.Equal(10,plane["WidthMm"]!.GetValue<double>());Assert.Equal(10,plane["HeightMm"]!.GetValue<double>());
                                var corners=plane["Corners"]!.AsArray();Assert.Equal(4,corners.Count);Point(corners[0],0,0);Point(corners[1],10,0);Point(corners[2],10,10);Point(corners[3],0,10);
                            }
                            else Assert.Null(history["PlaneCalibration"]);
                        }
                    }
                    Assert.Equal(before,File.ReadAllBytes(path));
                    results.Add(new { reader=Path.GetFullPath(reader), readerSha256=Hash(reader),
                        fileFormatsSha256=Hash(Path.Combine(Path.GetDirectoryName(reader)!,"TekstilCAD.FileFormats.dll")),
                        fixtureKind=kind,fixtureVersion=fixture[0]!.GetValue<int>(),fixtureSha256=Convert.ToHexString(SHA256.HashData(before)),
                        expectedAccept=accept,exitCode=process.ExitCode,sourceUnchanged=true });
                }
            }
            finally { File.Delete(path); }
        }
        Assert.Equal(physicalHash,Hash(physical!));Assert.Equal(photoHash,Hash(photo!));
        if(Environment.GetEnvironmentVariable("TEKSTILCAD_READER_EVIDENCE_PATH") is { Length:>0 } evidencePath)
            File.WriteAllText(evidencePath,System.Text.Json.JsonSerializer.Serialize(new {
                runId=Environment.GetEnvironmentVariable("TEKSTILCAD_READER_RUN_ID"),
                trxPath=Environment.GetEnvironmentVariable("TEKSTILCAD_READER_TRX_PATH"),
                physical=Path.GetFullPath(physical!),physicalSha256=physicalHash,
                photo=Path.GetFullPath(photo!),photoSha256=photoHash,results },new System.Text.Json.JsonSerializerOptions { WriteIndented=true }));
    }
}
