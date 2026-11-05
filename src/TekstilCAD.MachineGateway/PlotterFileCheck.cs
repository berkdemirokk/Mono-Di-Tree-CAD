using TekstilCAD.FileFormats;

namespace TekstilCAD.MachineGateway;

/// <summary>File-only comparison with caller declarations; no device is contacted or qualified.</summary>
public static class PlotterFileCheck
{
    public enum Status { Incomplete, UnitMismatch, Rejected, Passed }
    public sealed record Result(Status State, string Message, PlotterHpglPreflight.Result? Inspection = null);

    public static Result Inspect(HpglPreparedOutput output, PlotterCapabilityProfile? profile, bool originZeroDeclared, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(output);
        if(profile is null || !profile.IsComplete || !originZeroDeclared)
            return new(Status.Incomplete,"Beyan eksik: tüm profil alanları ve X/Y sıfır orijini doğrulanmalıdır.");
        if(profile.UnitsPerMillimeter != output.UnitsPerMillimeter)
            return new(Status.UnitMismatch,"Beyan edilen birim/mm ile hazırlanan dosyanın birimi farklı.");
        try
        {
            var result=PlotterHpglPreflight.Inspect(output,profile,cancellationToken);
            return new(Status.Passed,"Dosya girilen sınırlara göre kontrolden geçti. Cihaz uyumluluğu, fiziksel ölçek ve kalibrasyon doğrulanmadı; makineye gönderim yapılmadı.",result);
        }
        catch(InvalidDataException ex)
        { return new(Status.Rejected,"Dosya girilen sınırlara göre kabul edilmedi: "+ex.Message+" Koordinat/orijin, alan ve komut sınırlarını kontrol edin."); }
    }
}
