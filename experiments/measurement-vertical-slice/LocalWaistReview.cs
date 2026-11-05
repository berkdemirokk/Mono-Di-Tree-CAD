using System.Globalization;
using TekstilCAD.Core;
namespace MeasurementSlice;

internal enum InputComparison { WithinDeclaredTolerance,OutsideDeclaredTolerance }
internal sealed record LocalWaistReport(decimal UpperBodyWaistMm,decimal LowerBodyArcSumMm,decimal DifferenceMm,decimal ToleranceMm,double FrontLowerSeamMm,double BackLowerSeamMm,InputComparison Comparison,string Text)
{
    internal string SewingStatus=>"unresolved";
    internal bool ProductionAllowed=>false;
}
// Pure local diagnostics. Body-input comparison never establishes a sewing relationship.
internal static class LocalWaistReview
{
    internal static LocalWaistReport Inspect(string? model,string? unit,bool confirmed,bool symmetry,bool sameReference,string toleranceText,IReadOnlyDictionary<string,string> fields)
    {
        if(!sameReference)throw new InvalidOperationException("İki bel ölçüsünün aynı beden ve aynı bel hizasına ait olduğunu belirtin; emin değilseniz karşılaştırmayın.");
        if(unit is not ("mm" or "cm")||string.IsNullOrWhiteSpace(toleranceText)||toleranceText.Length>24||!decimal.TryParse(toleranceText.Trim().Replace(',','.'),NumberStyles.AllowLeadingSign|NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var tolerance)||tolerance<0)throw new InvalidOperationException("Açık ve sıfır veya pozitif karşılaştırma toleransı girin.");
        tolerance=checked(tolerance*(unit=="cm"?10m:1m));if(tolerance>10000m)throw new InvalidOperationException("Tolerans teknik sınır dışında.");
        var normalized=Contract.Normalize(model,unit,confirmed,symmetry,fields);decimal upper=normalized.Upper["waist"],front=normalized.LowerValues[0],back=normalized.LowerValues[1],sum=front+back,diff=upper-sum;
        var comparison=Math.Abs(diff)<=tolerance?InputComparison.WithinDeclaredTolerance:InputComparison.OutsideDeclaredTolerance;
        var lower=normalized.Lower;double f=Geometry.Distance(lower.Front[0],lower.Front[1]),b=Geometry.Distance(lower.Back[0],lower.Back[1]);
        string relation=comparison==InputComparison.WithinDeclaredTolerance?"Girdi toplamları sizin belirttiğiniz sayısal tolerans içinde.":"Girdi toplamları sizin belirttiğiniz sayısal tolerans dışında.";
        string direction=diff>0?"Üst için girilen beden çevresi alt yaylar toplamından büyük.":diff<0?"Üst için girilen beden çevresi alt yaylar toplamından küçük.":"İki beden ölçüsü sayısal olarak eşit.";
        string instruction=comparison==InputComparison.WithinDeclaredTolerance?"Bel hizası ve ölçüm yöntemini yine kontrol edin.":"Üst beden bel çevresini, alt ön/arka yayların aynı bel hizasında ve yan noktalarda ölçüldüğünü ve birimi kontrol edin; doğru kaynağı belirleyip ilgili alanı kendiniz düzeltin. Fark otomatik dağıtılmadı.";
        string text=$"YEREL GİRDİ İNCELEMESİ (dikiş onayı değil)\nÜst için girilen beden bel çevresi {upper:F3} mm; alt için ön + arka beden bel yayları {front:F3} + {back:F3} = {sum:F3} mm. Üst − alt {diff:+0.000;-0.000;0.000} mm; açık toleransınız {tolerance:F3} mm. {relation} {direction}\n{instruction}\nAlt panelin hesaplanan ön ve arka yarım bel çizgileri yaklaşık {f:F3} / {b:F3} mm'dir; bu çizgilere ayrı girilen alt bolluklar dahildir. Üst girdideki beden çevresi, üst parçanın dikiş hattı uzunluğu değildir. Üst dikiş yolu, ön pensin kapatılması, üst–alt bağlantı yönü ve dikilebilirlik çözümlenmedi. Hiçbir üretim veya prova onayı verilmedi.";
        return new(upper,sum,diff,tolerance,f,b,comparison,text);
    }
}
