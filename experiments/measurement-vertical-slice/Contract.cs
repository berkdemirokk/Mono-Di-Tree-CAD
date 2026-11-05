using System.Globalization;
using TekstilCAD.Patterns;
using FourPieceResearch;
using SyntheticBridge;
using UpperPreview=BodiceWpfReader.Preview;
namespace MeasurementSlice;

// Development-only input contract. Never sends entered values to a worker.
internal static class Contract
{
    internal const string Model="sentetik-06-on-arka-penssiz-v1";
    internal static readonly string[] LowerKeys=["frontWaist","backWaist","frontHip","backHip","frontWaistEase","backWaistEase","frontHipEase","backHipEase","hipDepth","waistToHem","hemCircumference"];
    internal static readonly string[] LowerLabels=["Ön bel yayı","Arka bel yayı","Ön kalça yayı","Arka kalça yayı","Ön bel yayı bolluğu","Arka bel yayı bolluğu","Ön kalça yayı bolluğu","Arka kalça yayı bolluğu","Belden kalça hizasına","Belden etek ucuna","Bitmiş etek çevresi"];
    internal static Dictionary<string,decimal> UpperSample(string id){var values=Scenario.Get(id).UpperDeclaration.Split(',').Select(x=>decimal.Parse(x.Trim(),CultureInfo.InvariantCulture)).ToArray();var defs=BreannaMeasurementGate.Definitions;if(values.Length!=defs.Count)throw new InvalidOperationException();return defs.Select((d,i)=>(d.Key,values[i])).ToDictionary(x=>x.Key,x=>x.Item2,StringComparer.Ordinal);}
    internal static decimal[] LowerSample(string id){var p=Scenario.Get(id).Lower;return new[]{p.FrontWaistArc,p.BackWaistArc,p.FrontHipArc,p.BackHipArc,p.FrontWaistEase,p.BackWaistEase,p.FrontHipEase,p.BackHipEase,p.HipDepth,p.WaistToHem,p.FinishedHemCircumference}.Select(Convert.ToDecimal).ToArray();}
    internal static Dictionary<string,string> SampleInput(string id,string unit){if(unit is not ("mm" or "cm"))throw new InvalidOperationException();var d=new Dictionary<string,string>(StringComparer.Ordinal);foreach(var def in BreannaMeasurementGate.Definitions){var n=UpperSample(id)[def.Key];d.Add("upper."+def.Key,(def.IsAngle||unit=="mm"?n:n/10).ToString(CultureInfo.InvariantCulture));}var l=LowerSample(id);for(int i=0;i<l.Length;i++)d.Add("lower."+LowerKeys[i],(unit=="mm"?l[i]:l[i]/10).ToString(CultureInfo.InvariantCulture));return d;}
    internal sealed record Parsed(IReadOnlyDictionary<string,decimal> Upper,decimal[] LowerRaw,decimal[] LowerValues,string Unit);
    internal sealed record Normalized(IReadOnlyDictionary<string,decimal> Upper,decimal[] LowerValues,ALinePanelInput LowerInput,ALinePanelPreview Lower);
    internal static Parsed ParseInputs(string? model,string? unit,bool confirmed,bool symmetry,IReadOnlyDictionary<string,string> source)
    {
        if(model!=Model||unit is not ("mm" or "cm")||!confirmed||!symmetry||source is null)throw new InvalidOperationException("Girdi tamamlanmadı.");
        var fields=new Dictionary<string,string>(source,StringComparer.Ordinal);var defs=BreannaMeasurementGate.Definitions;
        if(fields.Count!=defs.Count+LowerKeys.Length||fields.Keys.Any(k=>!defs.Any(d=>k=="upper."+d.Key)&&!LowerKeys.Any(x=>k=="lower."+x)))throw new InvalidOperationException("Ölçü alanları eksik veya fazla.");
        var entries=new List<BreannaMeasurementInput>();foreach(var def in defs){if(!fields.TryGetValue("upper."+def.Key,out var text)||!ExplicitNumber.TryParse(text,out var n))throw new InvalidOperationException("Üst ölçü geçersiz veya belirsiz.");entries.Add(new(def.Key,n,def.IsAngle?BreannaUnit.Degree:unit=="mm"?BreannaUnit.Millimeter:BreannaUnit.Centimeter));}
        var reviewed=BreannaMeasurementGate.Review(entries);if(!reviewed.IsValid)throw new InvalidOperationException("Üst ölçü ilişkisi geçersiz.");
        var raw=LowerKeys.Select(k=>fields.TryGetValue("lower."+k,out var s)&&ExplicitNumber.TryParse(s,out var n)&&n>=0?n:throw new InvalidOperationException("Alt ölçü geçersiz veya belirsiz.")).ToArray();
        var decimals=raw.Select(n=>checked(n*(unit=="cm"?10m:1m))).ToArray();
        return new(reviewed.Values!,raw,decimals,unit);
    }
    static Normalized CreateGeometry(Parsed parsed)
    {
        var lower=ALineMeasurementReview.Normalize(parsed.LowerRaw.Select(ExplicitNumber.Format).ToArray(),parsed.Unit=="mm"?ALineLengthUnit.Millimeter:ALineLengthUnit.Centimeter);
        return new(parsed.Upper,parsed.LowerValues,lower,ALinePanelPreview.Create(lower));
    }
    internal static Normalized Normalize(string? model,string? unit,bool confirmed,bool symmetry,IReadOnlyDictionary<string,string> source)=>CreateGeometry(ParseInputs(model,unit,confirmed,symmetry,source));
    internal static (string Id,ALinePanelPreview Lower) Validate(string? model,string? unit,bool confirmed,bool symmetry,IReadOnlyDictionary<string,string> source)
    {
        var parsed=ParseInputs(model,unit,confirmed,symmetry,source);
        foreach(var id in new[]{"synthetic-base","synthetic-scaled","synthetic-distinct"}){var expected=UpperSample(id);if(parsed.Upper.Count==expected.Count&&expected.All(k=>parsed.Upper.TryGetValue(k.Key,out var v)&&v==k.Value)&&parsed.LowerValues.SequenceEqual(LowerSample(id)))return (id,CreateGeometry(parsed).Lower);}
        throw new InvalidOperationException("Yalnız sabit uydurma örnekler destekleniyor; gerçek kişi verisi girmeyin.");
    }
}
internal sealed record SliceResult(string Id,UpperPreview Upper,ALinePanelPreview Lower){internal bool ProductionAllowed=>false;internal bool ExportAllowed=>false;}
internal static class Slice
{
    internal static async Task<SliceResult> Run(string? model,string? unit,bool confirmed,bool symmetry,IReadOnlyDictionary<string,string> fields,Func<string,CancellationToken,Task<UpperPreview>> launch,CancellationToken cancel)
    {
        var (id,lower)=Contract.Validate(model,unit,confirmed,symmetry,fields);cancel.ThrowIfCancellationRequested();var upper=await launch(id,cancel);cancel.ThrowIfCancellationRequested();if(upper.ProductionAllowed||upper.ExportAllowed||lower.CanApplyOrExport)throw new InvalidOperationException("Çıktı kapısı açık.");return new(id,upper,lower);
    }
}
