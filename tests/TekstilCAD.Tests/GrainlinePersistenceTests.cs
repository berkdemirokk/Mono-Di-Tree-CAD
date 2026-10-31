using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class GrainlinePersistenceTests
{
    private static ProjectDocument Sample() => new()
    {
        Name="İplik yönü",
        Pieces=[new PatternPiece { Name="Ön", Points=[new(0,0),new(80,0),new(0,120)],
            Grainline=new Grainline { Start=new(10,20), End=new(11,90), NeedsReview=true } }]
    };

    private static ProjectDocument Read(string text) => ProjectStorage.DeserializeProject(Encoding.UTF8.GetBytes(text));
    private static JsonArray Saved() => JsonNode.Parse(ProjectStorage.SerializeProject(Sample()))!.AsArray();
    private static JsonObject Line(JsonArray root) => root[1]!["Pieces"]![0]!["Grainline"]!.AsObject();

    [Fact]
    public void V4RoundTripPreservesGrainlineAndDoesNotAliasSource()
    {
        var original=Sample(); ProjectStorage.Validate(original);
        var json=ProjectStorage.SerializeProject(original);
        var root=JsonNode.Parse(json)!.AsArray();
        Assert.Equal(4,root[0]!.GetValue<int>());
        var loaded=Read(json);
        Assert.True(DocumentSession.ContentEquals(original,loaded));
        Assert.NotSame(original.Pieces[0].Grainline,loaded.Pieces[0].Grainline);
        loaded.Pieces[0].Grainline!.Start=new(7,8);
        Assert.Equal(new Point2(10,20),original.Pieces[0].Grainline!.Start);
        Assert.Throws<JsonException>(()=>JsonSerializer.Deserialize<ProjectDocument>(json));
    }

    [Theory]
    [InlineData(0,false)] [InlineData(0,true)] [InlineData(1,false)] [InlineData(1,true)]
    [InlineData(2,false)] [InlineData(2,true)] [InlineData(3,false)] [InlineData(3,true)]
    public void OlderFormatsRejectInjectedGrainlineIncludingNull(int version,bool useNull)
    {
        var root=Saved();
        if(useNull)root[1]!["Pieces"]![0]!["Grainline"]=null;
        var body=root[1]!.ToJsonString();
        var json=version==0 ? body : $"[{version},{body}]";
        Assert.Throws<InvalidDataException>(()=>Read(json));
    }

    [Theory]
    [InlineData("line-null")] [InlineData("start-missing")] [InlineData("start-null")]
    [InlineData("end-missing")] [InlineData("x-missing")] [InlineData("y-string")]
    [InlineData("review-missing")] [InlineData("review-null")] [InlineData("review-number")]
    [InlineData("finite-false")] [InlineData("unknown-field")]
    public void V4RejectsIncompleteOrMalformedGrainline(string mutation)
    {
        var root=Saved();var piece=root[1]!["Pieces"]![0]!.AsObject();var line=Line(root);
        switch(mutation)
        {
            case "line-null": piece["Grainline"]=null;break;
            case "start-missing": line.Remove("Start");break;
            case "start-null": line["Start"]=null;break;
            case "end-missing": line.Remove("End");break;
            case "x-missing": line["Start"]!.AsObject().Remove("X");break;
            case "y-string": line["End"]!["Y"]="5";break;
            case "review-missing": line.Remove("NeedsReview");break;
            case "review-null": line["NeedsReview"]=null;break;
            case "review-number": line["NeedsReview"]=1;break;
            case "finite-false": line["Start"]!["IsFinite"]=false;break;
            case "unknown-field": line["Other"]=123;break;
        }
        Assert.Throws<InvalidDataException>(()=>Read(root.ToJsonString()));
    }

    [Fact]
    public void DuplicateGrainlinePropertyIsRejected()
    {
        var text=ProjectStorage.SerializeProject(Sample());
        var needle="\"NeedsReview\": true";
        Assert.Contains(needle,text);
        var duplicate=text.Replace(needle,"\"NeedsReview\": true, \"NeedsReview\": false",StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(()=>Read(duplicate));
    }

    [Fact]
    public void InvalidGrainlineCoordinatesOrCollapsedLineCannotBeSaved()
    {
        var document=Sample();var line=document.Pieces[0].Grainline!;
        line.Start=new(double.NaN,0); Assert.Throws<InvalidDataException>(()=>ProjectStorage.Validate(document));
        line.Start=new(100_000_001,0); Assert.Throws<InvalidDataException>(()=>ProjectStorage.Validate(document));
        line.Start=line.End; Assert.Throws<InvalidDataException>(()=>ProjectStorage.Validate(document));
    }

    [Fact]
    public void InvalidSerializedGrainlineBoundsAndDuplicateCoordinatesAreRejected()
    {
        var outOfBounds=Saved();Line(outOfBounds)["End"]!["X"]=100_000_001;
        Assert.Throws<InvalidDataException>(()=>Read(outOfBounds.ToJsonString()));
        var collapsed=Saved();Line(collapsed)["End"]=Line(collapsed)["Start"]!.DeepClone();
        Assert.Throws<InvalidDataException>(()=>Read(collapsed.ToJsonString()));
        var text=ProjectStorage.SerializeProject(Sample());
        var needle="\"X\": 10";
        Assert.Contains(needle,text);
        Assert.Throws<InvalidDataException>(()=>Read(text.Replace(needle,"\"X\": 10, \"X\": 11",StringComparison.Ordinal)));
    }

    [Fact]
    public void V4WithPastalRetainsAndValidatesSnapshotStructure()
    {
        var document=Sample();var id=document.Pieces[0].Id;
        document.NestingSnapshot=new NestingSnapshot { FabricWidth=200, UsedLength=130, Gap=1, EdgeMargin=0,
            Algorithm=NestingSnapshot.ShelfAlgorithm, LayoutFingerprint=NestingSnapshot.ComputeLayoutFingerprint(document),
            Placements=[new NestingPlacementSnapshot(id,1,id)] };
        var text=ProjectStorage.SerializeProject(document);
        Assert.True(DocumentSession.ContentEquals(document,Read(text)));
        var root=JsonNode.Parse(text)!.AsArray();root[1]!["NestingSnapshot"]!.AsObject().Remove("FabricWidth");
        Assert.Throws<InvalidDataException>(()=>Read(root.ToJsonString()));
    }

    [Fact]
    public void LegacyWithoutGrainlineRemainsUnversionedAndReadsBack()
    {
        var document=Sample();document.Pieces[0].Grainline=null;
        var text=ProjectStorage.SerializeProject(document);
        Assert.Equal(JsonValueKind.Object,JsonDocument.Parse(text).RootElement.ValueKind);
        Assert.DoesNotContain("Grainline",text,StringComparison.Ordinal);
        Assert.True(DocumentSession.ContentEquals(document,Read(text)));
    }

    [Fact]
    public void InvalidInputFileIsNotModifiedByFailedLoad()
    {
        var root=new DirectoryInfo(AppContext.BaseDirectory);
        while(root is not null && !File.Exists(Path.Combine(root.FullName,"AGENTS.md")))root=root.Parent;
        Assert.NotNull(root);
        var directory=Path.Combine(root!.FullName,"work","grainline-persistence-tests",Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);var path=Path.Combine(directory,"source.tcad");
        var json=Saved();Line(json).Remove("NeedsReview");
        File.WriteAllText(path,json.ToJsonString());var before=File.ReadAllBytes(path);
        Assert.Throws<InvalidDataException>(()=>ProjectStorage.Load(path));
        Assert.Equal(before,File.ReadAllBytes(path));
    }

    [Fact]
    public void RecoveryRetainsGrainlineAfterWriterSessionEnds()
    {
        var root=new DirectoryInfo(AppContext.BaseDirectory);
        while(root is not null && !File.Exists(Path.Combine(root.FullName,"AGENTS.md")))root=root.Parent;
        Assert.NotNull(root);
        var directory=Path.Combine(root!.FullName,"work","grainline-recovery-tests",Guid.NewGuid().ToString("N"));
        var document=Sample();string recoveryPath;
        using(var writer=new RecoveryStore(directory)) { writer.Save(document);recoveryPath=writer.RecoveryPath; }
        using var reader=new RecoveryStore(directory);
        var recovered=reader.LoadCandidate(recoveryPath);
        Assert.True(DocumentSession.ContentEquals(document,recovered.Document));
        Assert.True(recovered.Document.Pieces[0].Grainline!.NeedsReview);
    }
}
