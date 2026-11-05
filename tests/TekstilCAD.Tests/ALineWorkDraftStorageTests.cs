using TekstilCAD.FileFormats;
using TekstilCAD.Patterns;

namespace TekstilCAD.Tests;

public class ALineWorkDraftStorageTests
{
    private static ALineWorkDraft Draft(string? unit="cm")=>new(unit,[" 36,0 ","","50","50","0","0.1250","","","20","70",new string('1',24)]);
    [Theory]
    [InlineData(null)]
    [InlineData("cm")]
    [InlineData("mm")]
    public void IncompleteTextZeroAndUnitRoundTripWithoutApproval(string? unit)
    {
        var source=Draft(unit);var json=ALineWorkDraftStorage.Serialize(source);var restored=ALineWorkDraftStorage.Deserialize(json);
        Assert.Equal(source.Unit,restored.Unit);Assert.Equal(source.Fields,restored.Fields);
        Assert.Equal(ALinePanelPreview.ModelId,ALineWorkDraftStorage.ModelVersion);
        Assert.DoesNotContain("approval",json);Assert.DoesNotContain("symmetry",json);
    }
    [Theory]
    [InlineData("\"version\":1","\"version\":2")]
    [InlineData("\"version\":1","\"version\":1,\"version\":1")]
    [InlineData("\"version\":1","\"version\":1,\"approved\":true")]
    [InlineData("\"unit\":\"cm\"","\"unit\":\"inch\"")]
    [InlineData("A-04.lower-section.experimental.v1","A-04.lower-section.experimental.v2")]
    public void UnknownOrDuplicateSchemaCannotBecomeADraft(string from,string to)
    {
        var json=ALineWorkDraftStorage.Serialize(Draft()).Replace(from,to,StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(()=>ALineWorkDraftStorage.Deserialize(json));
    }
    [Fact]
    public void SourceIdentityRoundTripsAndIncludesModelPhotoAndRequest()
    {
        var request=new DesignRequest("tasarım","bel 72 cm",new DesignReference("photo.png","image/png",[1,2,3]),"aline",2);
        var id=ALineWorkDraftStorage.SourceIdentity(request);var json=ALineWorkDraftStorage.Serialize(Draft() with { SourceHash=id });
        var loaded=ALineWorkDraftStorage.Deserialize(json);
        Assert.Equal(id,loaded.SourceHash);Assert.Equal(Draft().Fields,loaded.Fields);
        Assert.DoesNotContain("tasarım",json);Assert.DoesNotContain("photo.png",json);
        Assert.NotEqual(id,ALineWorkDraftStorage.SourceIdentity(request with { Prompt="başka" }));
        Assert.NotEqual(id,ALineWorkDraftStorage.SourceIdentity(request with { ModelId="wrap" }));
        Assert.NotEqual(id,ALineWorkDraftStorage.SourceIdentity(request with { Reference=request.Reference! with { Data=[1,2,4] } }));
        Assert.Throws<InvalidDataException>(()=>ALineWorkDraftStorage.Deserialize(json.Replace(ALineWorkDraftStorage.SourceAlgorithm,"unknown")));
        Assert.Throws<InvalidDataException>(()=>ALineWorkDraftStorage.Serialize(Draft() with { SourceHash="bad" }));
        Assert.Null(ALineWorkDraftStorage.Deserialize(ALineWorkDraftStorage.Serialize(Draft())).SourceHash);
    }
    [Fact]
    public void LimitsAndNewFileAtomicPolicyPreserveExistingFile()
    {
        var path=Path.Combine(Path.GetTempPath(),"aline-draft-"+Guid.NewGuid().ToString("N")+ALineWorkDraftStorage.Extension);
        try
        {
            ALineWorkDraftStorage.SaveNew(path,Draft());var original=File.ReadAllBytes(path);
            Assert.Throws<ProjectConflictException>(()=>ALineWorkDraftStorage.SaveNew(path,Draft("mm")));
            Assert.Equal(original,File.ReadAllBytes(path));
            Assert.Equal(Draft().Fields,ALineWorkDraftStorage.Load(path).Fields);
            Assert.Throws<InvalidDataException>(()=>ALineWorkDraftStorage.Serialize(new("cm",Enumerable.Repeat("0",10).ToArray())));
            Assert.Throws<InvalidDataException>(()=>ALineWorkDraftStorage.Serialize(new("cm",Enumerable.Repeat(new string('1',25),11).ToArray())));
            Assert.Throws<InvalidDataException>(()=>ALineWorkDraftStorage.Deserialize(new string(' ',ALineWorkDraftStorage.MaximumFileBytes+1)));
            File.WriteAllBytes(path,new byte[ALineWorkDraftStorage.MaximumFileBytes+1]);
            Assert.Throws<InvalidDataException>(()=>ALineWorkDraftStorage.Load(path));
        }
        finally { if(File.Exists(path))File.Delete(path); }
    }
}
