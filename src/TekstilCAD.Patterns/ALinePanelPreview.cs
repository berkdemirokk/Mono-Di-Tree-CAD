using TekstilCAD.Core;

namespace TekstilCAD.Patterns;

/// <summary>Measured section interpolation, not a bodice or a sewing-pattern drafting system. All lengths in mm.</summary>
public sealed record ALinePanelInput(double FrontWaistArc, double BackWaistArc,
    double FrontHipArc, double BackHipArc, double FrontWaistEase, double BackWaistEase,
    double FrontHipEase, double BackHipEase, double HipDepth, double WaistToHem,
    double FinishedHemCircumference);

public sealed class ALinePanelPreview
{
    public const string ModelId = "A-04.lower-section.experimental.v1";
    public IReadOnlyList<Point2> Front { get; }
    public IReadOnlyList<Point2> Back { get; }
    public double FrontSideLengthMm { get; }
    public double BackSideLengthMm { get; }
    public double SideMismatchMm => Math.Abs(FrontSideLengthMm-BackSideLengthMm);
    public bool CanApplyOrExport => false;
    public IReadOnlyList<string> Unresolved { get; } = Array.AsReadOnly(new[] {
        "Sağ ve sol beden simetrik varsayılır; ön/arka yaylar ikiye bölünür. Sağ/sol asimetri modellenmez.",
        "Üst beden, kol evi, yaka ve elbiseye birleşim üretilmedi.",
        "Pens dağılımı, bel eğrisi ve vücuda uyum çözülmedi; düz kesit çizgileri kullanıldı.",
        "Yan dikiş uzunlukları ölçülür; eşit uzunluk dikiş eşleşmesi veya prova doğrulaması değildir.",
        "Dikiş payı, kapama, kumaş ve baskı doğrulaması yok; projeye ekleme ve çıktı kapalı."
    });
    private ALinePanelPreview(Point2[] front, Point2[] back)
    {
        Front=Array.AsReadOnly(front); Back=Array.AsReadOnly(back);
        FrontSideLengthMm=Geometry.Distance(front[1],front[2])+Geometry.Distance(front[2],front[3]);
        BackSideLengthMm=Geometry.Distance(back[1],back[2])+Geometry.Distance(back[2],back[3]);
    }
    public static ALinePanelPreview Create(ALinePanelInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        double[] dimensions=[input.FrontWaistArc,input.BackWaistArc,input.FrontHipArc,input.BackHipArc,
            input.HipDepth,input.WaistToHem,input.FinishedHemCircumference];
        double[] ease=[input.FrontWaistEase,input.BackWaistEase,input.FrontHipEase,input.BackHipEase];
        if(dimensions.Any(v=>!double.IsFinite(v)||v<=0||v>10_000) || ease.Any(v=>!double.IsFinite(v)||v<0||v>10_000))
            throw new ArgumentException("Ölçüler pozitif, bolluklar sıfır veya pozitif ve en fazla 10000 mm olmalı.");
        if(input.HipDepth>=input.WaistToHem)throw new ArgumentException($"Kalça düşüklüğü {input.HipDepth:G17} mm, belden etek boyu {input.WaistToHem:G17} mm: düşüklük boydan küçük olmalı. Girilen değerleri kontrol edin.");
        var frontHip=input.FrontHipArc+input.FrontHipEase;
        var backHip=input.BackHipArc+input.BackHipEase;
        var frontWaist=input.FrontWaistArc+input.FrontWaistEase;
        var backWaist=input.BackWaistArc+input.BackWaistEase;
        // The user explicitly accepts equal front/back hem allocation in the UI.
        var halfHem=input.FinishedHemCircumference/4;
        if(frontWaist>frontHip)throw new ArgumentException($"Ön beden: bel yayı {input.FrontWaistArc:G17} + bolluk {input.FrontWaistEase:G17} = {frontWaist:G17} mm; kalça yayı {input.FrontHipArc:G17} + bolluk {input.FrontHipEase:G17} = {frontHip:G17} mm. Bu kesitte bel toplamı kalça toplamından büyük olamaz. Girilen değerleri kontrol edin.");
        if(backWaist>backHip)throw new ArgumentException($"Arka beden: bel yayı {input.BackWaistArc:G17} + bolluk {input.BackWaistEase:G17} = {backWaist:G17} mm; kalça yayı {input.BackHipArc:G17} + bolluk {input.BackHipEase:G17} = {backHip:G17} mm. Bu kesitte bel toplamı kalça toplamından büyük olamaz. Girilen değerleri kontrol edin.");
        if(halfHem<=frontHip/2)throw new ArgumentException($"Ön beden: bitmiş etek çevresi {input.FinishedHemCircumference:G17} mm; eşit dağılımdaki yarım etek {halfHem:G17} mm, yarım kalça ve bolluk {(frontHip/2):G17} mm. Etek yarısı bundan büyük olmalı. Girilen değerleri kontrol edin.");
        if(halfHem<=backHip/2)throw new ArgumentException($"Arka beden: bitmiş etek çevresi {input.FinishedHemCircumference:G17} mm; eşit dağılımdaki yarım etek {halfHem:G17} mm, yarım kalça ve bolluk {(backHip/2):G17} mm. Etek yarısı bundan büyük olmalı. Girilen değerleri kontrol edin.");
        Point2[] Panel(double waist,double hip) => [new(0,0),new(waist/2,0),new(hip/2,input.HipDepth),new(halfHem,input.WaistToHem),new(0,input.WaistToHem)];
        var front=Panel(frontWaist,frontHip);var back=Panel(backWaist,backHip);
        Geometry.ValidateContour(front);Geometry.ValidateContour(back);
        return new(front,back);
    }
}
