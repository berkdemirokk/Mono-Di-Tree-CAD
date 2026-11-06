namespace TekstilCAD.Patterns;

/// <summary>
/// Experimental symmetric lower-section candidate. It redistributes only explicitly
/// supplied front/back ease, never body arcs, total ease or source data. It is not a
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
        if(!double.IsFinite(waist.Front) || !double.IsFinite(waist.Back) || !double.IsFinite(hip.Front) || !double.IsFinite(hip.Back) ||
           waist.Front<0 || waist.Back<0 || hip.Front<0 || hip.Back<0 ||
           waist.Front>10000 || waist.Back>10000 || hip.Front>10000 || hip.Back>10000)
            return new(input,null,"Negatif veya sınır dışı bolluk gerekeceğinden bu yöntemle aday bulunamadı; kaynak ölçüler korunuyor.");
        var candidate=input with { FrontWaistEase=waist.Front,BackWaistEase=waist.Back,FrontHipEase=hip.Front,BackHipEase=hip.Back };
        ALinePanelPreview preview;
        try { preview=ALinePanelPreview.Create(candidate); }
        catch(ArgumentException ex) { return new(input,null,"Bolluk dağılımı geçerli kesit üretmiyor: "+ex.Message); }
        // Floating-point arithmetic must not quietly turn a failed candidate into
        // a matching one. This numerical bound is not a manufacturing tolerance.
        var bound=1e-10*Math.Max(1,Math.Max(preview.FrontSideLengthMm,preview.BackSideLengthMm));
        if(preview.WaistToHipMismatchMm>bound || preview.HipToHemMismatchMm>bound)
            return new(input,null,"Sayısal kontrolde yan aralıklar eşleşmedi; aday oluşturulmadı.");
        return new(input,candidate,"Deneysel aday yalnız ön/arka bolluk dağılımını değiştirir; toplam bolluk ve beden yayları korunur. Ön ve arka bitmiş bel/kalça genişlikleri eşit varsayılır. Dikiş, beden uyumu veya üretim doğrulanmadı; kaynak değiştirilmedi.");
    }
}
