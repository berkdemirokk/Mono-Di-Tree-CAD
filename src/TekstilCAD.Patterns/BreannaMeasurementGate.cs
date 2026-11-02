using System.Collections.ObjectModel;
namespace TekstilCAD.Patterns;

public enum BreannaUnit { Millimeter, Centimeter, Degree }
public sealed record BreannaMeasurementDefinition(string Key, string Label, string Instructions, string ReferenceUrl, bool IsAngle = false);
public sealed record BreannaMeasurementInput(string Key, decimal Value, BreannaUnit Unit) { public override string ToString() => "Breanna ölçü girdisi (değer gizli)"; }
public sealed record BreannaMeasurementError(string Key, string Message);
public sealed record BreannaMeasurementResult(IReadOnlyDictionary<string, decimal>? Values, IReadOnlyList<BreannaMeasurementError> Errors)
{
    public override string ToString() => "Breanna ölçü sonucu (değerler gizli)";
    public bool IsValid => Values is not null && Errors.Count == 0;
    public bool IsProductionApproved => false;
    public bool IsPlotterApproved => false;
    public string Status => "Deneysel önizleme girdisi; üretim ve plotter için onaysız.";
}

/// <summary>Measurement intake only. It neither generates nor approves a pattern.</summary>
public static class BreannaMeasurementGate
{
    private static BreannaMeasurementDefinition D(string key,string label,string instruction,string slug,bool angle=false) =>
        new(key,label,instruction,$"https://freesewing.eu/docs/measurements/{slug}/",angle);
    public static IReadOnlyList<BreannaMeasurementDefinition> Definitions { get; } = Array.AsReadOnly(new[] {
        D("biceps","Üst kol çevresi","Üst kolun en geniş yerinden çevresini ölçün.","biceps"),
        D("bustFront","Ön göğüs","Göğsün en dolgun yerinden, bir yan dikiş noktasından diğerine ön taraftan ölçün.","bustfront"),
        D("bustSpan","Göğüs noktaları arası","İki göğüs noktası arasındaki mesafeyi ölçün.","bustspan"),
        D("highBust","Üst göğüs çevresi","Göğsün üstünden geçen üst göğüs çevresini ölçün.","highbust"),
        D("highBustFront","Ön üst göğüs","Üst göğüs hattında yan dikiş noktaları arasında ön kısmı ölçün.","highbustfront"),
        D("hpsToBust","Omuz üst noktasından göğse","Boyun dibindeki omuz üst noktasından göğüs noktasına ölçün.","hpstobust"),
        D("hpsToWaistBack","Omuz üst noktasından arka bele","Boyun dibindeki omuz üst noktasından sırt üzerinden bele ölçün.","hpstowaistback"),
        D("hpsToWaistFront","Omuz üst noktasından ön bele","Boyun dibindeki omuz üst noktasından ön taraftan bele ölçün.","hpstowaistfront"),
        D("waist","Bel çevresi","Bel çevresini ölçün.","waist"),
        D("waistToHips","Belden kalçaya","Bel hattı ile kalça hattı arasındaki düşey mesafeyi ölçün.","waisttohips"),
        D("neck","Boyun çevresi","Boyun çevresini ölçün.","neck"),
        D("shoulderToShoulder","Omuzdan omuza","Bir omuz ucundan sırt üzerinden diğer omuz ucuna ölçün.","shouldertoshoulder"),
        D("shoulderSlope","Omuz eğimi","Omuz çizgisinin yataya göre aşağı eğimini dereceyle belirtin.","shoulderslope",true),
        D("shoulderToWrist","Omuzdan bileğe","Omuz ucundan bileğe kol boyunca ölçün.","shouldertowrist"),
        D("wrist","Bilek çevresi","Bilek çevresini ölçün.","wrist")
    });
    public static BreannaMeasurementResult Review(IEnumerable<BreannaMeasurementInput> input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var errors=new List<BreannaMeasurementError>(); var values=new Dictionary<string,decimal>(StringComparer.Ordinal);
        var defs=Definitions.ToDictionary(d=>d.Key,StringComparer.Ordinal); var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var entry in input)
        {
            if(entry is null || entry.Key is null || !defs.TryGetValue(entry.Key,out var def)) { errors.Add(new("", "Bilinmeyen ölçü."));continue; }
            if(!seen.Add(entry.Key)) { errors.Add(new(entry.Key,"Ölçü birden fazla kez girildi."));continue; }
            if(def.IsAngle ? entry.Unit!=BreannaUnit.Degree : entry.Unit!=BreannaUnit.Millimeter && entry.Unit!=BreannaUnit.Centimeter)
            { errors.Add(new(entry.Key,"Ölçü birimi uygun değil."));continue; }
            decimal value;
            try { value=checked(entry.Value*(entry.Unit==BreannaUnit.Centimeter ? 10m : 1m)); }
            catch(OverflowException) { errors.Add(new(entry.Key,"Ölçü değeri çok büyük."));continue; }
            if(def.IsAngle ? value<=-90 || value>=90 : value<=0 || value>100000m)
            { errors.Add(new(entry.Key,"Ölçü teknik sınır dışında."));continue; }
            values.Add(entry.Key,value);
        }
        foreach(var def in Definitions) if(!seen.Contains(def.Key))errors.Add(new(def.Key,"Ölçü gerekli."));
        if(values.TryGetValue("highBustFront",out var front)&&values.TryGetValue("highBust",out var total)&&front>=total)
            errors.Add(new("highBustFront","Ön üst göğüs toplam üst göğüsten küçük olmalı."));
        if(values.TryGetValue("hpsToBust",out var bust)&&values.TryGetValue("hpsToWaistFront",out var waist)&&bust>=waist)
            errors.Add(new("hpsToBust","Göğüs yüksekliği ön bel uzunluğundan küçük olmalı."));
        return errors.Count==0 ? new(new ReadOnlyDictionary<string,decimal>(values),errors.AsReadOnly()) : new(null,errors.AsReadOnly());
    }
}
