using System.IO;
using System.Text;
using BodiceWpfReader;
using Xunit;

public class BodiceReaderTests
{
    const string Front="""{"name":"breanna.front","segments":[{"type":"move","to":{"x":0,"y":0}},{"type":"line","to":{"x":10,"y":0}},{"type":"cubic","cp1":{"x":10,"y":2},"cp2":{"x":4,"y":5},"to":{"x":0,"y":5}},{"type":"close"}]}""";
    const string Back="""{"name":"breanna.back","segments":[{"type":"move","to":{"x":0,"y":0}},{"type":"line","to":{"x":10,"y":0}},{"type":"line","to":{"x":0,"y":10}},{"type":"close"}]}""";
    static string Json(string first=Front,string second=Back)=>"{\"status\":\"ARAŞTIRMA — ÜRETİM ONAYI DEĞİL\",\"unit\":\"mm\",\"coordinateSpace\":\"part-local\",\"parts\":["+first+","+second+"],\"approved\":false}";
    static Preview Read(string s)=>BodiceReader.Read(Encoding.UTF8.GetBytes(s));
    [Fact] public void ReadsTwoNamedPartsAndPreservesCubicControlPoints()
    {
        var r=Read(Json());Assert.Equal(new[]{"breanna.front","breanna.back"},r.Parts.Select(p=>p.Name));
        var curve=r.Parts[0].Segments[2];Assert.Equal("cubic",curve.Type);Assert.Equal(10,curve.Cp1!.Value.X);Assert.Equal(5,curve.Cp2!.Value.Y);
        Assert.False(r.ProductionAllowed);Assert.False(r.ExportAllowed);
    }
    [Fact] public void ResponseTypesHaveNoPublicConstructors()
    {
        Assert.Empty(typeof(Preview).GetConstructors());Assert.Empty(typeof(Part).GetConstructors());Assert.Empty(typeof(Segment).GetConstructors());
    }
    [Fact] public void RejectsUnknownDuplicateMissingAndIncorrectScope()
    {
        foreach(var json in new[]{Json(Front,Front),Json().Replace("\"approved\":false","\"approved\":true"),Json().Replace("\"unit\":\"mm\"","\"unit\":\"cm\""),Json().Replace("\"approved\":false","\"approved\":false,\"approved\":false"),Json().Replace("\"coordinateSpace\":\"part-local\",",""),Json().Replace("\"approved\":false","\"approved\":false,\"extra\":0")}) Assert.Throws<InvalidDataException>(()=>Read(json));
    }
    [Fact] public void RejectsInvalidOperationsNumbersAndUnclosedPaths()
    {
        foreach(var front in new[]{Front.Replace("\"type\":\"close\"","\"type\":\"move\""),Front.Replace("\"type\":\"cubic\"","\"type\":\"arc\""),Front.Replace("\"x\":10","\"x\":1e999"),Front.Replace("\"x\":10","\"x\":1000001"),Front.Replace("\"y\":0}","\"y\":0,\"y\":0}"),Front.Replace(",\"cp2\":{\"x\":4,\"y\":5}","")}) Assert.Throws<InvalidDataException>(()=>Read(Json(front)));
    }
    [Fact] public void RejectsOversizedAndMalformedData()
    {
        Assert.Throws<InvalidDataException>(()=>BodiceReader.Read(new byte[1_000_001]));
        Assert.Throws<InvalidDataException>(()=>Read("{"));
        var many="{\"name\":\"breanna.front\",\"segments\":[{\"type\":\"move\",\"to\":{\"x\":0,\"y\":0}},"+string.Concat(Enumerable.Repeat("{\"type\":\"line\",\"to\":{\"x\":1,\"y\":1}},",511))+"{\"type\":\"close\"}]}";
        Assert.Throws<InvalidDataException>(()=>Read(Json(many)));
    }
}
