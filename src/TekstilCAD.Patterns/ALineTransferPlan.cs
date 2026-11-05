using System.Globalization;

namespace TekstilCAD.Patterns;

public sealed record ALineTransferRow(int Index,string ExistingText,string? ProposedText,decimal? SourceMillimeters);

/// <summary>Immutable, unit-bound empty-field copy plan. Does not approve geometry or infer missing values.</summary>
public sealed class ALineTransferPlan
{
    public IReadOnlyList<ALineTransferRow> Rows { get; }
    public ALineLengthUnit Unit { get; }
    public bool HasWrites=>Rows.Any(r=>r.ProposedText is not null);
    private ALineTransferPlan(ALineTransferRow[] rows,ALineLengthUnit unit) { Rows=Array.AsReadOnly(rows);Unit=unit; }
    public static ALineTransferPlan Create(IReadOnlyList<decimal?> source,IReadOnlyList<string> sourceIssues,IReadOnlyList<string> current,ALineLengthUnit? unit)
    {
        ArgumentNullException.ThrowIfNull(source);ArgumentNullException.ThrowIfNull(sourceIssues);ArgumentNullException.ThrowIfNull(current);
        if(source.Count!=11 || current.Count!=11)throw new ArgumentException("On bir alan gerekli.");
        if(sourceIssues.Count>0)throw new ArgumentException(string.Join(Environment.NewLine,sourceIssues));
        if(unit is not (ALineLengthUnit.Millimeter or ALineLengthUnit.Centimeter))throw new ArgumentException("Aktarımdan önce tüm formun birimini açıkça seçin: cm veya mm.");
        if(!source.Any(v=>v.HasValue))throw new ArgumentException("İstekte aktarılacak açık A kesim alanı yok; eksik ölçü veya bolluk türetilmedi.");
        var rows=new ALineTransferRow[11];var combined=new decimal?[11];
        for(int i=0;i<rows.Length;i++)
        {
            var value=source[i];var old=current[i] ?? throw new ArgumentException("Geçersiz form alanı.");
            bool Valid(decimal mm)=>mm<=10_000m && (i is >=4 and <=7 ? mm>=0 : mm>0);
            if(value is decimal sourceMm && !Valid(sourceMm))throw new ArgumentException($"{i+1}. kaynak alanı ölçü sınırını aşıyor.");
            string? proposed=null;
            // Even whitespace counts as user input: never replace it silently.
            if(old.Length>0)
            {
                var existing=ALineMeasurementReview.ParseLengthMm(old,unit,i);
                if(!Valid(existing))throw new ArgumentException($"{i+1}. dolu alan ölçü sınırını aşıyor; değiştirilmedi.");
                if(value is decimal known && known!=existing)throw new ArgumentException($"{i+1}. dolu alan açık istek ölçüsüyle çelişiyor; hiçbir alan değiştirilmedi.");
                combined[i]=existing;
            }
            else if(value is decimal mm)
            {
                combined[i]=mm;
                proposed=(unit==ALineLengthUnit.Centimeter ? mm/10m : mm).ToString("0.############################",CultureInfo.InvariantCulture);
                var separator=proposed.IndexOf('.');
                // A trailing zero makes exactly three fractional digits unambiguous
                // under the existing manual input grammar; no value is rounded.
                if(separator>=0 && proposed.Length-separator-1==3)proposed+="0";
                if(proposed.Length>24 || ALineMeasurementReview.ParseLengthMm(proposed,unit,i)!=mm)
                    throw new ArgumentException($"{i+1}. alan seçili birimde kayıpsız temsil edilemiyor; aktarılmadı.");
            }
            rows[i]=new(i,old,proposed,value);
        }
        if(combined[8].HasValue && combined[9].HasValue && combined[8]>=combined[9])throw new ArgumentException("Kalça düşüklüğü belden etek boyundan küçük olmalı; aktarım yapılmadı.");
        foreach(var (waist,hip,waistEase,hipEase) in new[]{(0,2,4,6),(1,3,5,7)})
        {
            var w=combined[waist]+combined[waistEase];var h=combined[hip]+combined[hipEase];
            if(w.HasValue && h.HasValue && w>h)throw new ArgumentException("Açık bel ve bolluk toplamı açık kalça ve bolluk toplamından geniş; aktarım yapılmadı.");
            if(h.HasValue && combined[10].HasValue && combined[10]/4m<=h/2m)throw new ArgumentException("Bitmiş etek çevresi açık kalça ve bolluk kesitinden geniş olmalı; aktarım yapılmadı.");
        }
        return new(rows,unit.Value);
    }
    public IReadOnlyList<string> ApplyTo(IReadOnlyList<string> current,ALineLengthUnit? unit)
    {
        if(unit!=Unit || current.Count!=Rows.Count || Rows.Any(row=>!string.Equals(current[row.Index],row.ExistingText,StringComparison.Ordinal)))
            throw new InvalidOperationException("Form veya birim değişti; aktarım özetini yeniden onaylayın.");
        return Array.AsReadOnly(Rows.Select(row=>row.ProposedText ?? row.ExistingText).ToArray());
    }
}
