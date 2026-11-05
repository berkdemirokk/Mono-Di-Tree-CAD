using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class DesignRequestV2Tests
{
    private static string Json(string model, string reference="null", string prompt="tarif") =>
        $$"""{"format":"tekstilcad.design-request","version":2,"prompt":"{{prompt}}","measurements":"ölçü","reference":{{reference}},"model":{{model}}}""";

    [Theory]
    [InlineData("wrap")][InlineData("aline")][InlineData("corset")]
    [InlineData("sheath")][InlineData("straight")][InlineData("shirt")]
    public void KnownIdentityAndTextArePreserved(string id)
    {
        var request=DesignRequestStorage.Deserialize(Json($$"""{"id":"{{id}}"}"""));
        Assert.Equal(id,request.ModelId); Assert.Equal("tarif",request.Prompt); Assert.Equal("ölçü",request.Measurements);
        Assert.Throws<InvalidOperationException>(()=>DesignRequestStorage.Serialize(request));
    }
    [Fact] public void NullIdentityAndV1RemainDistinctFromInventedIdentity()
    {
        Assert.Null(DesignRequestStorage.Deserialize(Json("null")).ModelId);
        var v1=DesignRequestStorage.Deserialize("""{"format":"tekstilcad.design-request","version":1,"prompt":"model: x","measurements":"","reference":null}""");
        Assert.Null(v1.ModelId); Assert.Equal("model: x",v1.Prompt);
    }
    [Theory]
    [InlineData("{}")][InlineData("{\"id\":\"unknown\"}")][InlineData("{\"id\":null}")]
    [InlineData("{\"id\":\"wrap\",\"id\":\"aline\"}")][InlineData("{\"id\":\"wrap\",\"extra\":1}")]
    public void InvalidIdentityIsRejected(string model) => Assert.Throws<InvalidDataException>(()=>DesignRequestStorage.Deserialize(Json(model)));
    [Fact] public void ExtraOrDuplicateRootAndEmptyRequestAreRejected()
    {
        Assert.Throws<InvalidDataException>(()=>DesignRequestStorage.Deserialize(Json("null").Replace("\"model\":null", "\"model\":null,\"extra\":1")));
        Assert.Throws<InvalidDataException>(()=>DesignRequestStorage.Deserialize(Json("null").Replace("\"model\":null", "\"model\":null,\"model\":null")));
        Assert.Throws<InvalidDataException>(()=>DesignRequestStorage.Deserialize(Json("{\"id\":\"wrap\"}",prompt:"")));
    }
    [Fact] public void V2ImageFailsClosedUntilDecoderIsAvailable()
    {
        var image="""{"name":"x.png","mediaType":"image/png","dataBase64":"iVBORw0KGgo="}""";
        Assert.Throws<InvalidDataException>(()=>DesignRequestStorage.Deserialize(Json("null",image)));
    }
    [Fact] public void V2RoundTripPreservesVersionAndIdentity()
    {
        var original=DesignRequestStorage.Deserialize(Json("{\"id\":\"aline\"}"));
        var restored=DesignRequestStorage.Deserialize(DesignRequestStorage.SerializeV2(original));
        Assert.Equal(2,restored.Version); Assert.Equal("aline",restored.ModelId); Assert.Equal(original.Prompt,restored.Prompt);
        Assert.Throws<InvalidOperationException>(()=>DesignRequestStorage.Serialize(DesignRequestStorage.Deserialize(Json("null"))));
    }
    [Fact] public void ImageValidationReceivesDetachedBytesAndFailureDoesNotReturnRequest()
    {
        var image="""{"name":"x.png","mediaType":"image/png","dataBase64":"iVBORw0KGgo="}""";
        var request=DesignRequestStorage.Deserialize(Json("null",image),r=>r.Data[0]=0);
        Assert.Equal(137,request.Reference!.Data[0]);
        Assert.Throws<InvalidDataException>(()=>DesignRequestStorage.Deserialize(Json("null",image),_=>throw new InvalidDataException("invalid")));
        Assert.Throws<InvalidDataException>(()=>DesignRequestStorage.SerializeV2(request));
        var serialized=DesignRequestStorage.SerializeV2(request,r=>r.Data[0]=0);
        Assert.Equal(137,DesignRequestStorage.Deserialize(serialized,_=>{}).Reference!.Data[0]);
    }
}
