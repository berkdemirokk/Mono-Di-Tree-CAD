using System.Text;
using System.Text.Json;
namespace TekstilCAD.Patterns;
/// <summary>Experimental transport only; no storage, logging, production or plot authority.</summary>
public static class BreannaMeasurementEnvelope
{
    public static string Serialize(IEnumerable<BreannaMeasurementInput> measurements)
    {
        var result=BreannaMeasurementGate.Review(measurements);
        if(!result.IsValid) throw new ArgumentException("Ölçüler eksik veya geçersiz; alan hatalarını ölçü denetiminden inceleyin.",nameof(measurements));
        var values=result.Values!;
        // A fixed wire precision prevents silently losing arbitrarily precise decimal input in JS numbers.
        if(values.Values.Any(v=>decimal.Round(v,3)!=v)) throw new ArgumentException("Aktarım en fazla 0,001 mm veya derece hassasiyetini destekler.",nameof(measurements));
        using var buffer=new MemoryStream();using(var writer=new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();writer.WriteString("schema","tekstilcad.experimental.breanna-measurements.v1");writer.WriteString("unit","mm");writer.WriteString("angleUnit","degree");writer.WriteStartObject("measurements");
            foreach(var def in BreannaMeasurementGate.Definitions) writer.WriteNumber(def.Key,decimal.Round(values[def.Key],3));
            writer.WriteEndObject();writer.WriteEndObject();
        }
        if(buffer.Length>16384)throw new InvalidOperationException("Aktarım boyut sınırı aşıldı.");
        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}
