using TekstilCAD.FileFormats;
using System.Security.Cryptography;

namespace TekstilCAD.MachineGateway;

/// <summary>File-only comparison with caller declarations; no device is contacted or qualified.</summary>
public static class PlotterFileCheck
{
    public enum Status { Incomplete, UnitMismatch, Rejected, Passed }
    public sealed record Result(Status State, string Message, PlotterHpglPreflight.Result? Inspection = null);
    public sealed record FileSummary(string Sha256, int ByteCount, int CommandCount, int LongestCommandBytes,
        double UnitsPerMillimeter, HpglOutputBounds Bounds);

    /// <summary>Summarizes the exact prepared ASCII bytes; never checks a device profile.</summary>
    public static FileSummary Summarize(HpglPreparedOutput output, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);cancellationToken.ThrowIfCancellationRequested();
        var text=output.Text;var buffer=new byte[8192];var position=0;var commandLength=0;var longest=0;var commands=0;
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        while(position<text.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();var count=Math.Min(buffer.Length,text.Length-position);
            for(var i=0;i<count;i++)
            {
                var c=text[position+i];if(c>127)throw new InvalidDataException("HPGL ASCII olmalıdır.");
                buffer[i]=(byte)c;
                if(c is '\r' or '\n') { if(commandLength!=0)throw new InvalidDataException("HPGL komutu eksik.");continue; }
                commandLength++;
                if(c==';') { longest=Math.Max(longest,commandLength);commands++;commandLength=0; }
            }
            hash.AppendData(buffer,0,count);position+=count;
        }
        if(commandLength!=0 || commands==0)throw new InvalidDataException("HPGL komutları tamamlanmamış.");
        cancellationToken.ThrowIfCancellationRequested();
        return new(Convert.ToHexString(hash.GetHashAndReset()),text.Length,commands,longest,output.UnitsPerMillimeter,output.Bounds);
    }

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
