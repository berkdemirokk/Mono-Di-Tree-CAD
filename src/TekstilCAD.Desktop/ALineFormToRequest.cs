using TekstilCAD.FileFormats;
using TekstilCAD.Patterns;

namespace TekstilCAD.Desktop;

internal sealed record ALineFormToRequestPlan(string Notes,IReadOnlyList<string> Added,IReadOnlyList<string> Preserved,IReadOnlyList<string> Missing);
internal static class ALineFormToRequest
{
    internal static ALineFormToRequestPlan Create(string prompt,string notes,IReadOnlyList<string> fields,ALineLengthUnit? unit)
    {
        if(fields.Count!=11 || unit is not (ALineLengthUnit.Millimeter or ALineLengthUnit.Centimeter))throw new ArgumentException("On bir alan ve açık birim gerekli.");
        var source=ALineRequestMeasurements.Review(prompt,notes);
        if(source.Issues.Count>0)throw new ArgumentException("Kaynak ölçüler belirsiz veya çelişkili; aktarım yapılmadı.");
        var added=new List<string>();var kept=new List<string>();var missing=new List<string>();var expected=new decimal?[11];
        for(int i=0;i<11;i++)
        {
            string label=ALineRequestMeasurements.Labels[i],text=fields[i] ?? throw new ArgumentException("Alan geçersiz.");
            if(text.Length==0) { if(source.Millimeters[i].HasValue)kept.Add(label);else missing.Add(label);continue; }
            var mm=ALineMeasurementReview.ParseLengthMm(text,unit,i);
            if(mm>10000 || (i is >=4 and <=7 ? mm<0 : mm<=0))throw new ArgumentException(label+": değer geçersiz; aktarım yapılmadı.");
            expected[i]=mm;
            if(source.Millimeters[i] is decimal existing)
            {
                if(existing!=mm)throw new ArgumentException(label+": kaynakla çelişiyor; önce kaynak düzeltme yolunu kullanın.");
                kept.Add(label);continue;
            }
            added.Add(label+": "+text.Trim()+" "+(unit==ALineLengthUnit.Centimeter ? "cm" : "mm"));
        }
        string updated=notes;
        if(added.Count>0)updated+=(updated.Length==0 || updated.EndsWith('\n') ? "" : "\n")+string.Join("\n",added);
        if(updated.Length>DesignRequestStorage.MaximumTextLength)throw new ArgumentException("Ölçü notları sınırı aşılır; aktarım yapılmadı.");
        var after=ALineRequestMeasurements.Review(prompt,updated);
        if(after.Issues.Count>0 || Enumerable.Range(0,11).Any(i=>expected[i].HasValue && after.Millimeters[i]!=expected[i]))throw new ArgumentException("Aktarım kaynakla veya toplam beden ölçüsüyle çelişiyor; hiçbir alan değiştirilmedi.");
        return new(updated,added.AsReadOnly(),kept.AsReadOnly(),missing.AsReadOnly());
    }
}
