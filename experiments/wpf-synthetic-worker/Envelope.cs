using System.IO;
using System.Text;
using System.Text.Json;
using BodiceWpfReader;

namespace SyntheticBridge;
internal static class Envelope
{
    static readonly string[] Ids=["synthetic-base","synthetic-scaled","synthetic-distinct"];
    internal static bool ValidId(string? s)=>s is not null&&Ids.Contains(s,StringComparer.Ordinal);
    static void Fields(JsonElement e,params string[] expected)
    {
        if(e.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("Object required");
        var seen=new HashSet<string>(StringComparer.Ordinal);foreach(var p in e.EnumerateObject())if(!expected.Contains(p.Name,StringComparer.Ordinal)||!seen.Add(p.Name))throw new InvalidDataException("Invalid fields");
        if(seen.Count!=expected.Length)throw new InvalidDataException("Missing field");
    }
    internal static Preview Read(byte[] bytes,string id)
    {
        if(!ValidId(id)||bytes.Length==0||bytes.Length>2_000_000)throw new InvalidDataException("Invalid response");
        try {using var doc=JsonDocument.Parse(bytes,new JsonDocumentOptions{MaxDepth=20});var root=doc.RootElement;
            Fields(root,"scope","networkIsolation","dependencyClosure","runtime","modules","unavailable","result");
            if(root.GetProperty("networkIsolation").GetString()!="not-isolated"||root.GetProperty("dependencyClosure").GetString()!="unresolved")throw new InvalidDataException("Unexpected status");
            var r=root.GetProperty("result");Fields(r,"id","approved","unit","coordinateSpace","parts");if(r.GetProperty("id").GetString()!=id)throw new InvalidDataException("Changed identity");
            using var ms=new MemoryStream();using(var w=new Utf8JsonWriter(ms)){w.WriteStartObject();w.WriteString("status","ARAŞTIRMA — ÜRETİM ONAYI DEĞİL");foreach(var key in new[]{"unit","coordinateSpace","parts","approved"}){w.WritePropertyName(key);r.GetProperty(key).WriteTo(w);}w.WriteEndObject();}
            return BodiceReader.Read(ms.ToArray());
        }catch(JsonException ex){throw new InvalidDataException("Invalid response",ex);}catch(InvalidOperationException ex){throw new InvalidDataException("Invalid response",ex);}
    }
}
