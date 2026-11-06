using System.IO;
using TekstilCAD.FileFormats;
using TekstilCAD.Patterns;

namespace TekstilCAD.Desktop;

/// <summary>Recomputes an unapproved snapshot against its exact source and form.</summary>
internal static class ALineBalanceSnapshotValidation
{
    private static double[] Ease(ALinePanelInput input)=>[input.FrontWaistEase,input.BackWaistEase,input.FrontHipEase,input.BackHipEase];
    internal static ALineBalanceSnapshot Create(ALineSideBalance.Result result)
    {
        if(result.Candidate is not { } candidate)throw new InvalidDataException("Kaydedilecek aday yok.");
        return new(ALineWorkDraftStorage.BalanceMethod,Array.AsReadOnly(Ease(result.Source)),Array.AsReadOnly(Ease(candidate)));
    }
    internal static ALineSideBalance.Result Validate(ALineWorkDraft draft,string sourceHash)
    {
        if(draft.Balance is not { } saved || saved.Method!=ALineWorkDraftStorage.BalanceMethod)
            throw new InvalidDataException("Aday kaydı veya yöntem sürümü uyuşmuyor; çalışma değiştirilmedi.");
        if(draft.SourceHash!=sourceHash)throw new InvalidDataException("Aday farklı bir isteğe bağlı; bu aday kaydı yalnız aynı kaynak istekle açılabilir. İlgili isteği açıp yeniden deneyin; mevcut çalışma değiştirilmedi.");
        try
        {
            var unit=draft.Unit switch { "cm"=>ALineLengthUnit.Centimeter,"mm"=>ALineLengthUnit.Millimeter,_=>throw new InvalidDataException("Aday birimi eksik.") };
            var input=ALineMeasurementReview.Normalize(draft.Fields,unit);var recomputed=ALineSideBalance.Propose(input);
            if(recomputed.Candidate is not { } candidate || !saved.Original.SequenceEqual(Ease(input)) || !saved.Candidate.SequenceEqual(Ease(candidate)))
                throw new InvalidDataException("Aday, kayıtlı formdan yeniden hesaplanan değerlerle uyuşmuyor; çalışma değiştirilmedi.");
            return recomputed;
        }
        catch(Exception ex) when(ex is ArgumentException or InvalidOperationException or OverflowException)
        { throw new InvalidDataException("Adayın formu yeniden doğrulanamadı; çalışma değiştirilmedi.",ex); }
    }
}
