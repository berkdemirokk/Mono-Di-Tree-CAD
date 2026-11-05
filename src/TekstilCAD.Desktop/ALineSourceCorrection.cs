using System.Text.RegularExpressions;
using System.Globalization;
using System.Text;
using TekstilCAD.FileFormats;
using TekstilCAD.Patterns;

namespace TekstilCAD.Desktop;

internal sealed record ALineCorrectionPlan(string Prompt,string Notes,string Place,string Label,string OldText,string NewText);
internal static class ALineSourceCorrection
{
    private static string NormalizeLabel(string text)=>Regex.Replace(text.Trim().Normalize(NormalizationForm.FormC).ToLower(CultureInfo.GetCultureInfo("tr-TR")),@"[\t\p{Zs}]+"," ",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
    internal static ALineCorrectionPlan Create(string prompt,string notes,int index,string formText,ALineLengthUnit? unit)
    {
        if(index<0||index>=ALineRequestMeasurements.Labels.Count || unit is not (ALineLengthUnit.Centimeter or ALineLengthUnit.Millimeter))throw new ArgumentException("Alan ve birim seçin.");
        var before=ALineRequestMeasurements.Review(prompt,notes);
        if(before.Issues.Count>0 || before.Millimeters[index] is null)throw new ArgumentException("Kaynak ölçü tek ve açık değil; otomatik düzeltilemez.");
        var mm=ALineMeasurementReview.ParseLengthMm(formText,unit,index);
        if(mm>10000 || (index is >=4 and <=7 ? mm<0 : mm<=0))throw new ArgumentException("Form değeri geçersiz.");
        if(mm==before.Millimeters[index])throw new ArgumentException("Kaynak ve form değeri zaten aynı; değişiklik yapılmadı.");
        string selected=formText.Trim(),suffix=unit==ALineLengthUnit.Centimeter ? "cm" : "mm";
        var candidate=selected+" "+suffix;var label=ALineRequestMeasurements.Labels[index];
        var matches=new List<(int Field,int Start,int Length,string Old)>();
        var pattern=new Regex(@"\A(?<lead>[\t\p{Zs}]*)(?<label>[^:=\r\n;]+?)(?<sep>[\t\p{Zs}]*(?::|=|[\t\p{Zs}])[\t\p{Zs}]*)(?<value>[0-9]{1,5}(?:[.,][0-9]{1,8})?[\t\p{Zs}]*(?:cm|mm))(?<tail>[\t\p{Zs}]*[.!]?[\t\p{Zs}]*)\z",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
        foreach(var (text,field) in new[]{(prompt,0),(notes,1)})
        foreach(Match part in Regex.Matches(text,@"[^;\r\n\v\f\u0085\u2028\u2029]+",RegexOptions.None,TimeSpan.FromMilliseconds(100)))
        {
            var m=pattern.Match(part.Value);if(!m.Success || !string.Equals(NormalizeLabel(m.Groups["label"].Value),NormalizeLabel(label),StringComparison.Ordinal))continue;
            var v=m.Groups["value"];matches.Add((field,part.Index+v.Index,v.Length,v.Value));
        }
        if(matches.Count!=1)throw new ArgumentException("Tek bir güvenli kaynak konumu bulunamadı; istek değiştirilmedi.");
        var match=matches[0];var original=match.Field==0 ? prompt : notes;
        var updated=original[..match.Start]+candidate+original[(match.Start+match.Length)..];
        string nextPrompt=match.Field==0 ? updated : prompt,nextNotes=match.Field==1 ? updated : notes;
        var after=ALineRequestMeasurements.Review(nextPrompt,nextNotes);
        if(after.Issues.Count>0 || after.Millimeters[index]!=mm)throw new ArgumentException("Düzeltme kaynakla çelişiyor veya kesin okunamıyor; istek değiştirilmedi.");
        return new(nextPrompt,nextNotes,match.Field==0 ? "Tarif" : "Ölçü notları",label,match.Old,candidate);
    }
}
