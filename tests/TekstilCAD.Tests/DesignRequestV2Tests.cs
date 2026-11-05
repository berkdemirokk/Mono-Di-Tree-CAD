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
}
