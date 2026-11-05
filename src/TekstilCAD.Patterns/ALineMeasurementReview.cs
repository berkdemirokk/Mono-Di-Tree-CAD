using System.Globalization;

namespace TekstilCAD.Patterns;

public enum ALineLengthUnit { Millimeter, Centimeter }

/// <summary>Explicit unit conversion and review gate; not an anthropometric validity certificate.</summary>
public sealed class ALineMeasurementReview
{
    private ALinePanelInput? reviewed;
    private bool confirmed;
    public static decimal ParseLengthMm(string? text,ALineLengthUnit? unit,int fieldIndex)
    {
        if(unit is not (ALineLengthUnit.Millimeter or ALineLengthUnit.Centimeter))throw new ArgumentException("Önce ölçü birimini seçin: cm veya mm.");
        var raw=text?.Trim();var separator=raw?.LastIndexOfAny(['.',',']) ?? -1;
        if(raw is not null && separator>=0 && raw.Length-separator-1==3 && raw[(separator+1)..].All(char.IsAsciiDigit))
            throw new ArgumentException($"{fieldIndex+1}. alanda üç basamaklı ayırıcı belirsiz. Binlik ayırıcı kullanmayın; 1000 veya 1 gibi açık değer girin. Ondalık değeri gerekirse eşdeğer cm/mm biriminde gruplama olmadan yazın ve tüm alanların birimini kontrol edin.");
        if(raw is null || !decimal.TryParse(raw.Replace(',','.'),NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var value))
            throw new ArgumentException($"{fieldIndex+1}. alana seçilen birimde açık değer girin; bolluk için sıfır yazılabilir.");
        return checked(value*(unit==ALineLengthUnit.Centimeter ? 10m : 1m));
    }
    public static ALinePanelInput Normalize(IReadOnlyList<string> fields, ALineLengthUnit? unit)
    {
        ArgumentNullException.ThrowIfNull(fields);
        if(unit is not (ALineLengthUnit.Millimeter or ALineLengthUnit.Centimeter))
            throw new ArgumentException("Önce ölçü birimini seçin: cm veya mm.");
        if(fields.Count!=11)throw new ArgumentException("On bir ölçü/bolluk alanı gerekli.");
        var values=new double[11];
        for(int i=0;i<values.Length;i++)
        {
            values[i]=(double)ParseLengthMm(fields[i],unit,i);
        }
        return new(values[0],values[1],values[2],values[3],values[4],values[5],values[6],values[7],values[8],values[9],values[10]);
    }
    public void Stage(ALinePanelInput input) { Invalidate();_ = ALinePanelPreview.Create(input);reviewed=input; }
    public void Confirm() { if(reviewed is null)throw new InvalidOperationException("Önce dönüştürülmüş ölçüleri inceleyin.");confirmed=true; }
    public void Invalidate() { reviewed=null;confirmed=false; }
    public void RequireConfirmed(ALinePanelInput current)
    {
        if(!confirmed || reviewed!=current)throw new InvalidOperationException("Geçerli ölçü özetini yeniden inceleyip onaylayın.");
    }
}
