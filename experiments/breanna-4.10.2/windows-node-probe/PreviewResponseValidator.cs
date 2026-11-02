using System.Text;
using System.Text.Json;

// Structural checks only; this does not authenticate the worker or certify geometry.
internal static class PreviewResponseValidator
{
    private static readonly UTF8Encoding Utf8 = new(false,true);
    private static readonly string[] MeasurementKeys={"biceps","bustFront","bustSpan","highBust","highBustFront","hpsToBust","hpsToWaistBack","hpsToWaistFront","waist","waistToHips","neck","shoulderToShoulder","shoulderSlope","shoulderToWrist","wrist"};
    private static readonly string[] OptionKeys={"shoulderDart","waistDart","primaryBustDart","secondaryBustDart","acrossBackFactor","armholeDepthFactor","backNeckCutout","bicepsEase","shoulderDartSize","shoulderDartLength","waistDartSize","waistDartLength","verticalEase","frontArmholeDeeper","shoulderEase","collarEase","chestEase","waistEase","primaryBustDartShaping","primaryBustDartLength","secondaryBustDartLength","shoulderSlopeReduction","frontScyeDart","sleevecapEase","sleevecapTopFactorX","sleevecapTopFactorY","sleevecapBackFactorX","sleevecapBackFactorY","sleevecapFrontFactorX","sleevecapFrontFactorY","sleevecapQ1Offset","sleevecapQ2Offset","sleevecapQ3Offset","sleevecapQ4Offset","sleevecapQ1Spread1","sleevecapQ1Spread2","sleevecapQ2Spread1","sleevecapQ2Spread2","sleevecapQ3Spread1","sleevecapQ3Spread2","sleevecapQ4Spread1","sleevecapQ4Spread2","cuffEase","sleeveLengthBonus"};
    [System.Diagnostics.CodeAnalysis.DoesNotReturn] private static void Fail() => throw new InvalidDataException("Geçersiz deneysel yanıt.");
    private static void Exact(JsonElement v,params string[] names)
    {
        if(v.ValueKind!=JsonValueKind.Object)Fail();var keys=v.EnumerateObject().Select(p=>p.Name).ToArray();
        if(keys.Length!=names.Length||keys.Distinct(StringComparer.Ordinal).Count()!=keys.Length||names.Any(k=>!keys.Contains(k,StringComparer.Ordinal)))Fail();
    }
    private static void NoDuplicates(JsonElement value)
    {
        if(value.ValueKind==JsonValueKind.Object){var names=new HashSet<string>(StringComparer.Ordinal);foreach(var p in value.EnumerateObject()){if(!names.Add(p.Name))Fail();NoDuplicates(p.Value);}}
        else if(value.ValueKind==JsonValueKind.Array)foreach(var item in value.EnumerateArray())NoDuplicates(item);
    }
    private static void Str(JsonElement v,string expected){if(v.ValueKind!=JsonValueKind.String||v.GetString()!=expected)Fail();}
    private static void False(JsonElement v){if(v.ValueKind!=JsonValueKind.False)Fail();}
    private static void Point(JsonElement p){Exact(p,"x","y");foreach(var key in new[]{"x","y"})if(p.GetProperty(key).ValueKind!=JsonValueKind.Number||!p.GetProperty(key).TryGetDouble(out var d)||!double.IsFinite(d)||Math.Abs(d)>100000000)Fail();}
    private static bool ValidPath(string? name,string? kind) => (name,kind) switch {
        ("seam","sewing-boundary") or ("saBase","construction") or ("sleevecap","construction") => true,
        ("secondaryBustDartHint" or "primaryBustDartHint" or "waistDartHint","internal-guide") => true,
        ("__macro_cutonfold_cutonfold_line" or "__macro_grainline_grainline_line" or "__macro_scalebox_scalebox_imperial" or "__macro_scalebox_scalebox_metric","annotation") => true,
        _ => false
    };
    private static void Strings(JsonElement value,int max){if(value.ValueKind!=JsonValueKind.Array||value.GetArrayLength()>max)Fail();foreach(var item in value.EnumerateArray())if(item.ValueKind!=JsonValueKind.String||item.GetString()!.Length>1024)Fail();}
    internal static void Validate(byte[] request,byte[] response)
    {
        if(response.Length is 0 or >1024*1024)Fail();
        try {
            string req=Utf8.GetString(request),res=Utf8.GetString(response);
            using var requestDoc=JsonDocument.Parse(req,new JsonDocumentOptions{MaxDepth=64});using var responseDoc=JsonDocument.Parse(res,new JsonDocumentOptions{MaxDepth=64});
            var job=requestDoc.RootElement;var root=responseDoc.RootElement;NoDuplicates(job);NoDuplicates(root);
            Exact(job,"schema","engine","measurementEnvelope","options","unit","angleUnit","toleranceMm","layout");Str(job.GetProperty("schema"),"tekstilcad.experimental.breanna-job.v1");Str(job.GetProperty("unit"),"mm");Str(job.GetProperty("angleUnit"),"degree");Str(job.GetProperty("layout"),"local-unplaced");
            var engine=job.GetProperty("engine");Exact(engine,"model","version");Str(engine.GetProperty("model"),"breanna");Str(engine.GetProperty("version"),"4.10.2");
            var tolerance=job.GetProperty("toleranceMm");if(tolerance.ValueKind!=JsonValueKind.Number||!tolerance.TryGetDouble(out var t)||!double.IsFinite(t)||t<=0||t>1)Fail();
            Exact(job.GetProperty("options"),OptionKeys);BreannaOptionSchema4102.Validate(job.GetProperty("options"));
            Exact(root,"protocol","ok","preview");Str(root.GetProperty("protocol"),"tekstilcad.experimental.preview-stdio.v1");if(root.GetProperty("ok").ValueKind!=JsonValueKind.True)Fail();
            var preview=root.GetProperty("preview");Exact(preview,"schema","warning","input","preview");Str(preview.GetProperty("schema"),"tekstilcad.experimental.bodice-request.v1");Str(preview.GetProperty("warning"),"DENEYSEL ÖNİZLEME; üretim, kesim ve plotter için uygun değildir");
            var input=preview.GetProperty("input");Exact(input,"unit","angleUnit","layout","toleranceMm","measurements","options");
            foreach(var key in new[]{"unit","angleUnit","layout","toleranceMm","options"})if(!JsonElement.DeepEquals(input.GetProperty(key),job.GetProperty(key)))Fail();
            using var envelope=JsonDocument.Parse(job.GetProperty("measurementEnvelope").GetString()!,new JsonDocumentOptions{MaxDepth=16});NoDuplicates(envelope.RootElement);Exact(envelope.RootElement,"schema","unit","angleUnit","measurements");Str(envelope.RootElement.GetProperty("schema"),"tekstilcad.experimental.breanna-measurements.v1");Str(envelope.RootElement.GetProperty("unit"),"mm");Str(envelope.RootElement.GetProperty("angleUnit"),"degree");var measurements=envelope.RootElement.GetProperty("measurements");Exact(measurements,MeasurementKeys);foreach(var key in MeasurementKeys){var value=measurements.GetProperty(key);if(value.ValueKind!=JsonValueKind.Number||!value.TryGetDecimal(out var d)||decimal.Round(d,3)!=d||(key=="shoulderSlope"?d<=-90||d>=90:d<=0||d>100000))Fail();}if(measurements.GetProperty("highBustFront").GetDecimal()>=measurements.GetProperty("highBust").GetDecimal()||measurements.GetProperty("hpsToBust").GetDecimal()>=measurements.GetProperty("hpsToWaistFront").GetDecimal())Fail();if(!JsonElement.DeepEquals(input.GetProperty("measurements"),measurements))Fail();
            var model=preview.GetProperty("preview");Exact(model,"purpose","transferable","unit","curve","cutBoundaryAvailable","lossManifest","parts");Str(model.GetProperty("purpose"),"diagnostic-only");False(model.GetProperty("transferable"));False(model.GetProperty("cutBoundaryAvailable"));Str(model.GetProperty("unit"),"mm");Str(model.GetProperty("curve"),"cubic-bezier");
            var parts=model.GetProperty("parts");if(parts.ValueKind!=JsonValueKind.Array||parts.GetArrayLength()!=3)Fail();var partNames=new HashSet<string>(StringComparer.Ordinal);int count=0;
            foreach(var part in parts.EnumerateArray()){
                Exact(part,"name","paths");var name=part.GetProperty("name").GetString();if(name is not ("breanna.front" or "breanna.back" or "breanna.sleeve")||!partNames.Add(name))Fail();
                var paths=part.GetProperty("paths");if(paths.ValueKind!=JsonValueKind.Array||paths.GetArrayLength()>64)Fail();int boundaries=0;
                foreach(var path in paths.EnumerateArray()){
                    Exact(path,"name","kind","segments");if(path.GetProperty("name").ValueKind!=JsonValueKind.String||path.GetProperty("name").GetString()!.Length>256)Fail();var kind=path.GetProperty("kind").GetString();if(!ValidPath(path.GetProperty("name").GetString(),kind))Fail();if(kind=="sewing-boundary")boundaries++;
                    var segments=path.GetProperty("segments");if(segments.ValueKind!=JsonValueKind.Array||segments.GetArrayLength()<1)Fail();count+=segments.GetArrayLength();if(count>4096)Fail();int i=0;string? last=null;
                    foreach(var seg in segments.EnumerateArray()){if(seg.ValueKind!=JsonValueKind.Object)Fail();if(!seg.TryGetProperty("type",out var type))throw new InvalidDataException("Geçersiz deneysel yanıt.");if(type.ValueKind!=JsonValueKind.String)Fail();last=type.GetString();if(i==0&&last!="move"||i>0&&last=="move")Fail();
                        if(last is "move" or "line"){Exact(seg,"type","to");Point(seg.GetProperty("to"));}else if(last=="cubic"){Exact(seg,"type","to","cp1","cp2");Point(seg.GetProperty("to"));Point(seg.GetProperty("cp1"));Point(seg.GetProperty("cp2"));}else if(last=="close"){Exact(seg,"type");if(i!=segments.GetArrayLength()-1)Fail();}else Fail();i++;}
                    if(kind=="sewing-boundary"&&last!="close")Fail();
                }if(boundaries!=1)Fail();
            }
            var losses=model.GetProperty("lossManifest");if(losses.ValueKind!=JsonValueKind.Array||losses.GetArrayLength()!=3)Fail();var lossNames=new HashSet<string>(StringComparer.Ordinal);
            foreach(var loss in losses.EnumerateArray()){
                Exact(loss,"part","omittedSnippets","omittedPointNames","omittedPartAttributes","omittedPathAttributes","layoutTransform","topology");var n=loss.GetProperty("part").GetString();if(n is null||!partNames.Contains(n)||!lossNames.Add(n))Fail();Strings(loss.GetProperty("omittedSnippets"),512);foreach(var item in loss.GetProperty("omittedSnippets").EnumerateArray())if(item.GetString() is not ("logo" or "bustNotch" or "armholePitch" or "frontNotch" or "backNotch"))Fail();Strings(loss.GetProperty("omittedPointNames"),4096);Strings(loss.GetProperty("omittedPartAttributes"),128);if(loss.GetProperty("omittedPartAttributes").GetArrayLength()!=0)Fail();Str(loss.GetProperty("layoutTransform"),"not inspected or applied");Str(loss.GetProperty("topology"),"unverified");
                var attrs=loss.GetProperty("omittedPathAttributes");if(attrs.ValueKind!=JsonValueKind.Object||attrs.EnumerateObject().Count()>64)Fail();foreach(var item in attrs.EnumerateObject()){if(!new[]{"sewing-boundary","construction","internal-guide","annotation"}.Any(kind=>ValidPath(item.Name,kind)))Fail();Strings(item.Value,128);foreach(var attr in item.Value.EnumerateArray())if(attr.GetString() is not ("class" or "marker-start" or "marker-end" or "data-text" or "data-text-class"))Fail();}
            }
        }catch(Exception ex) when(ex is JsonException or DecoderFallbackException or InvalidOperationException or KeyNotFoundException or ArgumentException or FormatException){throw new InvalidDataException("Geçersiz deneysel yanıt.");}
    }
}
