using TekstilCAD.FileFormats;
using TekstilCAD.Patterns;

namespace TekstilCAD.Desktop;

/// <summary>Partial, informational comparison of explicitly recorded body waist values.</summary>
internal static class ALineWaistSourceComparison
{
    internal static string Describe(string prompt,string notes,string front,string back,ALineLengthUnit? unit)
    {
        const string scope=" Beden yaylarına bolluk dahil değildir; bolluk ayrı girilir. Belden kalçaya ölçümün referans noktası en geniş kalça hizasıyla aynı olmayabilir. Bu karşılaştırma ölçüm veya üretim onayı değildir.";
        try
        {
            var waist=ALineBodyTotals.Review(prompt,notes).WaistMm;
            if(waist is null)return "İstekte açık ve tek bir bel çevresi yok veya ifade belirsiz; karşılaştırma yapılamadı."+scope;
            if(string.IsNullOrWhiteSpace(front)||string.IsNullOrWhiteSpace(back)||unit is null)return "İstekte açık bel çevresi var; A kesim ön ve arka beden yayları veya birimi eksik."+scope;
            decimal a=ALineMeasurementReview.ParseLengthMm(front,unit,0),b=ALineMeasurementReview.ParseLengthMm(back,unit,1);
            if(a<=0 || b<=0 || a>10000 || b>10000)return "A kesim beden yayları geçersiz; karşılaştırma yapılamadı."+scope;
            decimal sum=checked(a+b);
            return $"İstekteki bel çevresi: {waist.Value:G} mm. A kesim ön + arka beden yayları: {sum:G} mm. "+
                (sum==waist.Value ? "Girilen sayılar eşit; ölçüm yöntemleri doğrulanmadı." : "Girilen sayılar farklı; kaynakları kontrol edin.")+scope;
        }
        catch(Exception ex) when(ex is ArgumentException or OverflowException or System.Text.RegularExpressions.RegexMatchTimeoutException)
        { return "Bel ölçüsü veya A kesim yayları belirsiz; karşılaştırma yapılamadı."+scope; }
    }
}
