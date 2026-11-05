using System.IO;
using System.Text.Json;
using System.Windows;

namespace BodiceWpfReader;

public sealed class Segment
{
    internal Segment(string type,Point? to=null,Point? cp1=null,Point? cp2=null) { Type=type;To=to;Cp1=cp1;Cp2=cp2; }
    public string Type { get; }
    public Point? To { get; }
    public Point? Cp1 { get; }
    public Point? Cp2 { get; }
}
public sealed class Part
{
    internal Part(string name,IReadOnlyList<Segment> segments) { Name=name;Segments=segments; }
    public string Name { get; }
    public IReadOnlyList<Segment> Segments { get; }
}
public sealed class Preview
{
    internal Preview(IReadOnlyList<Part> parts) { Parts=parts; }
    public IReadOnlyList<Part> Parts { get; }
    public bool ProductionAllowed => false;
    public bool ExportAllowed => false;
}

/// <summary>Strict reader for the local synthetic bodice-review calculate response.</summary>
public static class BodiceReader
{
    const int MaximumBytes=1_000_000, MaximumSegments=512;
    const double MaximumCoordinate=1_000_000;
    static void Fields(JsonElement e,params string[] keys)
    {
        if(e.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("Object required");
        var allowed=keys.ToHashSet(StringComparer.Ordinal);var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var p in e.EnumerateObject())if(!allowed.Contains(p.Name)||!seen.Add(p.Name))throw new InvalidDataException("Unknown or duplicate field");
        if(seen.Count!=allowed.Count)throw new InvalidDataException("Missing field");
    }
    static string Text(JsonElement e,string key) => e.GetProperty(key).ValueKind==JsonValueKind.String ? e.GetProperty(key).GetString()! : throw new InvalidDataException("String required");
    static Point ReadPoint(JsonElement e)
    {
        Fields(e,"x","y");
        double Number(string key) { var v=e.GetProperty(key);if(v.ValueKind!=JsonValueKind.Number || !v.TryGetDouble(out var n) || !double.IsFinite(n) || Math.Abs(n)>MaximumCoordinate)throw new InvalidDataException("Invalid coordinate");return n; }
        return new(Number("x"),Number("y"));
    }
    public static Preview Read(ReadOnlyMemory<byte> input)
    {
        if(input.Length==0 || input.Length>MaximumBytes)throw new InvalidDataException("Response size invalid");
        var snapshot=input.ToArray();
        try
        {
            using var doc=JsonDocument.Parse(snapshot,new JsonDocumentOptions { MaxDepth=8 });var root=doc.RootElement;
            Fields(root,"status","unit","coordinateSpace","parts","approved");
            if(Text(root,"status")!="ARAŞTIRMA — ÜRETİM ONAYI DEĞİL" || Text(root,"unit")!="mm" || Text(root,"coordinateSpace")!="part-local" || root.GetProperty("approved").ValueKind!=JsonValueKind.False)throw new InvalidDataException("Unexpected response scope");
            var array=root.GetProperty("parts");if(array.ValueKind!=JsonValueKind.Array || array.GetArrayLength()!=2)throw new InvalidDataException("Exactly two parts required");
            var parts=new List<Part>();var seen=new HashSet<string>(StringComparer.Ordinal);
            foreach(var p in array.EnumerateArray())
            {
                Fields(p,"name","segments");var name=Text(p,"name");
                if(name is not ("breanna.front" or "breanna.back") || !seen.Add(name))throw new InvalidDataException("Invalid part identity");
                var list=p.GetProperty("segments");if(list.ValueKind!=JsonValueKind.Array || list.GetArrayLength()<3 || list.GetArrayLength()>MaximumSegments)throw new InvalidDataException("Invalid segment count");
                var segments=new List<Segment>();int index=0,count=list.GetArrayLength();
                foreach(var s in list.EnumerateArray())
                {
                    if(s.ValueKind!=JsonValueKind.Object || !s.TryGetProperty("type",out var typeValue) || typeValue.ValueKind!=JsonValueKind.String)throw new InvalidDataException("Missing operation");
                    string type=typeValue.GetString()!;
                    if(index==0 && type!="move" || index>0 && type=="move" || index==count-1 && type!="close" || index<count-1 && type=="close")throw new InvalidDataException("Invalid operation order");
                    switch(type)
                    {
                        case "move": case "line": Fields(s,"type","to");segments.Add(new(type,ReadPoint(s.GetProperty("to"))));break;
                        case "cubic": Fields(s,"type","cp1","cp2","to");segments.Add(new(type,ReadPoint(s.GetProperty("to")),ReadPoint(s.GetProperty("cp1")),ReadPoint(s.GetProperty("cp2"))));break;
                        case "close": Fields(s,"type");segments.Add(new(type));break;
                        default: throw new InvalidDataException("Unknown operation");
                    }
                    index++;
                }
                parts.Add(new(name,Array.AsReadOnly(segments.ToArray())));
            }
            return new(Array.AsReadOnly(parts.ToArray()));
        }
        catch(JsonException ex) { throw new InvalidDataException("Invalid JSON",ex); }
    }
}
