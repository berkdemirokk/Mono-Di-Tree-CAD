namespace TekstilCAD.Patterns;

/// <summary>
/// Experimental symmetric lower-section candidate. It redistributes only explicitly
/// supplied front/back ease; body arcs and source data remain unchanged. Floating-point
/// sums are approximate, not exact decimal preservation. It is not a
/// sewing-pattern, fit, seam or production approval.
/// </summary>
public static class ALineSideBalance
{
    public sealed record Result(ALinePanelInput Source,ALinePanelInput? Candidate,string Explanation)
    {
        public bool Available => Candidate is not null;
    }

    public static Result Propose(ALinePanelInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        try { _=ALinePanelPreview.Create(input); }
        catch(ArgumentException ex) { return new(input,null,"Kaynak kesit geçersiz: "+ex.Message); }

        // Equal finished front/back waist and hip widths make both straight side
        // segments identical, given the existing shared heights and equal hem split.
        // This is only one deliberately restrictive candidate, not a general solver.
        static (double Front,double Back) Balance(double frontArc,double backArc,double frontEase,double backEase)
        {
            var totalEase=frontEase+backEase;
            var front=(backArc-frontArc+totalEase)/2;
            return (front,totalEase-front);
        }
        var waist=Balance(input.FrontWaistArc,input.BackWaistArc,input.FrontWaistEase,input.BackWaistEase);
        var hip=Balance(input.FrontHipArc,input.BackHipArc,input.FrontHipEase,input.BackHipEase);
        if(!double.IsFinite(waist.Front) || !double.IsFinite(waist.Back) || !double.IsFinite(hip.Front) || !double.IsFinite(hip.Back))
            return new(input,null,"Sayısal hesap geçersiz; hiçbir değer değiştirilmedi. Ölçü ve birimleri kontrol edip yeniden deneyin.");
        var issues=new List<string>();
        static void Describe(string area,double frontArc,double backArc,double frontEase,double backEase,(double Front,double Back) proposed,List<string> issues)
        {
            if(proposed.Front<0 || proposed.Back<0)
            {
                var available=frontEase+backEase;var required=Math.Abs(frontArc-backArc);var deficit=required-available;
                issues.Add($"{area}: Negatif {(proposed.Front<0 ? "ön" : "arka")} bolluk gerekir. Ön/arka beden yayı farkı yaklaşık {required:G17} mm; girilen toplam bolluk yaklaşık {available:G17} mm, bu yöntemde eksik toplam yaklaşık {deficit:G17} mm.");
            }
            if(proposed.Front>10000 || proposed.Back>10000)
                issues.Add($"{area}: Hesaplanan {(proposed.Front>10000 ? "ön" : "arka")} bolluk 10000 mm sınırını aşıyor.");
        }
        Describe("Bel",input.FrontWaistArc,input.BackWaistArc,input.FrontWaistEase,input.BackWaistEase,waist,issues);
        Describe("Kalça",input.FrontHipArc,input.BackHipArc,input.FrontHipEase,input.BackHipEase,hip,issues);
        if(issues.Count>0)
            return new(input,null,string.Join(Environment.NewLine,issues)+"\nBu sayılar yalnız bu eşit genişlik varsayımının matematiksel sınırıdır; bolluk artırma veya beden uyumu önerisi değildir. İlgili ön/arka beden yayı ve bolluk alanlarını ölçü kaynağınızla karşılaştırın; yanlış değeri formda veya kaynak istekte kendiniz düzeltin, ardından ölçü özetini ve simetriyi yeniden inceleyip onaylayın. Otomatik değişiklik yapılmadı; başka dağılımların imkânsızlığı kanıtlanmadı.");
        var candidate=input with { FrontWaistEase=waist.Front,BackWaistEase=waist.Back,FrontHipEase=hip.Front,BackHipEase=hip.Back };
        ALinePanelPreview preview;
        try { preview=ALinePanelPreview.Create(candidate); }
        catch(ArgumentException ex) { return new(input,null,"Bolluk dağılımı geçerli kesit üretmiyor: "+ex.Message); }
        // Floating-point arithmetic must not quietly turn a failed candidate into
        // a matching one. This numerical bound is not a manufacturing tolerance.
        var bound=1e-10*Math.Max(1,Math.Max(preview.FrontSideLengthMm,preview.BackSideLengthMm));
        if(preview.WaistToHipMismatchMm>bound || preview.HipToHemMismatchMm>bound)
            return new(input,null,"Sayısal kontrolde yan aralıklar eşleşmedi; aday oluşturulmadı.");
        return new(input,candidate,"Deneysel aday yalnız ön/arka bolluk dağılımını değiştirir; toplam bolluk yaklaşık korunur, beden yayları değiştirilmez. Ön ve arka bitmiş bel/kalça genişlikleri eşit varsayılır. Dikiş, beden uyumu veya üretim doğrulanmadı; kaynak değiştirilmedi.");
    }
}
