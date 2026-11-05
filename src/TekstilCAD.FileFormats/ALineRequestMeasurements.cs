using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace TekstilCAD.FileFormats;

/// <summary>Explicit experimental section fields, distinct from total body circumferences.</summary>
public sealed record ALineRequestMeasurementReview(IReadOnlyList<decimal?> Millimeters,IReadOnlyList<string> Issues)
{
    public bool Complete => Issues.Count==0 && Millimeters.All(v=>v.HasValue);
}

public static class ALineRequestMeasurements
{
    public static IReadOnlyList<string> Labels { get; }=Array.AsReadOnly(new[] {
        "Ön bel yayı","Arka bel yayı","Ön kalça yayı","Arka kalça yayı",
        "Ön bel yayı bolluğu","Arka bel yayı bolluğu","Ön kalça yayı bolluğu","Arka kalça yayı bolluğu",
        "Belden kalçaya düşüklük","Belden etek boyu","Bitmiş etek çevresi (bolluk dahil)"
    });
    private static string Fold(string text)=>text.Normalize(NormalizationForm.FormC).ToLower(CultureInfo.GetCultureInfo("tr-TR"));
    private static readonly string Names=string.Join("|",Labels.OrderByDescending(s=>s.Length).Select(s=>Regex.Escape(Fold(s))));
    private static readonly Regex Mention=new("(?<![\\p{L}\\p{M}\\p{N}_])(?:"+Names+")(?![\\p{L}\\p{M}\\p{N}_])",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
    private static readonly Regex Exact=new("\\A\\s*(?<label>"+Names+")\\s*(?::|=|\\s)\\s*(?<value>[0-9]{1,5}(?:[.,][0-9]{1,8})?)\\s*(?<unit>cm|mm)\\s*[.!]?\\s*\\z",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
    // Unknown assertions about measurements are not affirmative evidence.
    // Recognize the whole ölçü-rooted word, not an open-ended list of negative
    // conjugations. Input length and regex time are bounded by Review.
    private static readonly Regex GeneralMeasurement=new(@"(?<![\p{L}\p{M}\p{N}_])ölçü[\p{L}\p{M}]*(?![\p{L}\p{M}\p{N}_])",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
    // An unresolved pronoun may refer back/forward to measurements in either
    // field. Do not guess its referent or whether the predicate is affirmative.
    // Bare adjectival bu/şu/o/tüm are not matched (e.g. "bu kumaş", "tüm modeller").
    private static readonly Regex Referent=new(@"(?<![\p{L}\p{M}\p{N}_])(?:bunlar[\p{L}\p{M}]*|şunlar[\p{L}\p{M}]*|onlar(?:ı|ın|a|da|dan|la)?|bunun[\p{L}\p{M}]*|şunun[\p{L}\p{M}]*|onun(?:la|ki)?|bunu|buna|bunda|bundan|şunu|şuna|şunda|şundan|onu|ona|onda|ondan|hepsi[\p{L}\p{M}]*|tümü[\p{L}\p{M}]*)(?![\p{L}\p{M}\p{N}_])",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
    // These are the only free-text affirmative exceptions, and must occupy an
    // entire input field: a following sentence must not silently retract them.
    private static readonly Regex AffirmativeField=new(@"\A\s*(?:bu\s+)?(?:ölçüler\s+geçerli|ölçüleri\s+kullanmak\s+istiyorum)\s*[.!]?\s*\z",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
    // One complete model request and one complete future-intent sentence only.
    // It asserts no numerical measurement and never resolves another clause.
    private static readonly Regex FutureMeasurementPhrase=new(@"\A\s*ölçüler(?:i|imi)\s+(?:sonra|birazdan|daha sonra)\s+(?:vereceğim|paylaşacağım)\s*[.!]?\s*\z",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
    private static readonly Regex ExplicitModelIntent=new(@"\A\s*(?:lütfen\s+)?(?:bana\s+)?(?:(?:modelim|model olarak|model seçimi)[: ]+)?a(?:[ -])kesim(?:\s+elbise)?\s+(?:istiyorum|seç|seçelim|olsun|hazırla|yap|oluştur)\s*\z",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
    private static bool IsFutureMeasurementRequest(string text)
    {
        var parts=Regex.Split(Fold(text),@"[,;]",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
        return parts.Length==2 && ExplicitModelIntent.IsMatch(parts[0]) && FutureMeasurementPhrase.IsMatch(parts[1])
            && ChatModelChoice.Review(parts[0]).ModelId=="aline";
    }
    private static readonly Regex AmbiguousNumber=new(@"(?<![0-9])[0-9]+[.,][0-9]{3}(?![0-9])",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
    private static bool TryRead(string text,out int index,out decimal mm)
    {
        index=-1;mm=0;var match=Exact.Match(Fold(text));
        if(!match.Success || AmbiguousNumber.IsMatch(match.Groups["value"].Value))return false;
        index=Enumerable.Range(0,Labels.Count).Single(i=>Fold(Labels[i])==match.Groups["label"].Value);
        if(!decimal.TryParse(match.Groups["value"].Value.Replace(',','.'),NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out mm))return false;
        if(match.Groups["unit"].Value=="cm")mm*=10m;
        return mm<=10_000m && (index is >=4 and <=7 ? mm>=0 : mm>0);
    }
    // Only a whole affirmative, explicitly unit-bearing clause is isolated from
    // total-circumference parsing. Questions/conditions/mixed clauses stay guarded.
    internal static bool IsExplicitSeparateField(string text)=>TryRead(text,out _,out _);

    public static ALineRequestMeasurementReview Review(string prompt,string notes)
    {
        var totals=DressMeasurementParser.Review(prompt,notes); // Also validates input size and global conditions.
        var values=new decimal?[Labels.Count];var issues=new List<string>();
        var foldedFields=new[]{Fold(prompt),Fold(notes)};
        var measurementContext=totals.Measurements.Count>0 || foldedFields.Any(text=>Mention.IsMatch(text)||GeneralMeasurement.IsMatch(text));
        if(measurementContext && foldedFields.Any(text=>Referent.IsMatch(text)))
            issues.Add("Ölçü içeren istekte bunlar/hepsi/tümü gibi bir referans zamirinin neyi anlattığı açık değil. Önceki veya sonraki alandaki olumlu ifade bunu çözmez. Öneri ve kaynağa bağlı çizim durduruldu; istek metni korunur. İlgili ölçüyü veya kumaş/model adını açıkça belirterek açıklayın ve pencereyi yeniden açın.");
        if(new[]{prompt,notes}.Any(text=>GeneralMeasurement.IsMatch(Fold(text))&&!AffirmativeField.IsMatch(Fold(text))&&!IsFutureMeasurementRequest(text)))
            issues.Add("İstekte desteklenmeyen serbest ölçü ifadesi var; olumlu, olumsuz veya geri çekme anlamı varsayılmadı. Kapsam netleşmeden ölçü önerisi ve kaynağa bağlı çizim yapılamaz. Açılış anındaki metin korunur; açık etiketli ölçüleri düzeltip pencereyi yeniden açın.");
        foreach(var text in new[]{prompt,notes})
        foreach(var segment in Regex.Split(text,@";|[\r\n\v\f\u0085\u2028\u2029]",RegexOptions.None,TimeSpan.FromMilliseconds(100)))
        {
            if(!Mention.IsMatch(Fold(segment)))continue;
            if(AmbiguousNumber.IsMatch(segment)) { issues.Add("A kesim sayısında üç basamaklı ayırıcı belirsiz: "+segment+". Binlik ayırıcı kullanmayın; 1000 mm veya 1 mm gibi açık yazın. Ondalık hassasiyet için eşdeğer cm/mm değerini üç basamaklı gruplama olmadan belirtin.");continue; }
            if(!TryRead(segment,out var index,out var value)) { issues.Add("A kesim alanı açık, tek ve birimli değil: "+segment);continue; }
            if(values[index] is decimal prior && prior!=value)issues.Add(Labels[index]+": farklı değerler çelişiyor.");
            else values[index]=value;
        }
        if(totals.Questions.Any(q=>q.Contains("koşullu",StringComparison.Ordinal)))issues.Add("İstekteki koşulun kapsamı belirsiz; A kesim ölçüleri kesin kabul edilmedi.");
        foreach(var (label,a,b) in new[]{("bel çevresi",0,1),("kalça çevresi",2,3)})
        {
            if(totals.Questions.Any(q=>q.StartsWith(label+":",StringComparison.Ordinal)&&!q.Contains("açık ölçü ve birim",StringComparison.Ordinal)))
                issues.Add(label+": toplam beden ölçüsündeki belirsizliği düzeltin.");
            var total=totals.Measurements.FirstOrDefault(m=>m.Label==label);
            if(total is not null && (values[a]>=total.Millimeters || values[b]>=total.Millimeters))
                issues.Add(label+": tek ön/arka yay toplam beden çevresinden küçük olmalı; diğer yay sıfır varsayılamaz.");
            if(total is not null && values[a].HasValue && values[b].HasValue && values[a]!.Value+values[b]!.Value!=total.Millimeters)
                issues.Add(label+": ön/arka yay toplamı belirtilen beden çevresiyle çelişiyor.");
        }
        return new(Array.AsReadOnly(values),issues.AsReadOnly());
    }
}
