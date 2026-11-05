using System.Globalization;

namespace TekstilCAD.Patterns;

public enum ALineLengthUnit { Millimeter, Centimeter }

/// <summary>Explicit unit conversion and review gate; not an anthropometric validity certificate.</summary>
public sealed class ALineMeasurementReview
{
    private ALinePanelInput? reviewed;
    private bool confirmed;
    public static ALinePanelInput Normalize(IReadOnlyList<string> fields, ALineLengthUnit? unit)
    {
        ArgumentNullException.ThrowIfNull(fields);
        if(unit is not (ALineLengthUnit.Millimeter or ALineLengthUnit.Centimeter))
            throw new ArgumentException("Önce ölçü birimini seçin: cm veya mm.");
        if(fields.Count!=11)throw new ArgumentException("On bir ölçü/bolluk alanı gerekli.");
        var values=new double[11];
        for(int i=0;i<values.Length;i++)
        {
            if(fields[i] is null || !decimal.TryParse(fields[i].Trim().Replace(',','.'),NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var value))
                throw new ArgumentException($"{i+1}. alana seçilen birimde açık değer girin; bolluk için sıfır yazılabilir.");
            values[i]=(double)checked(value*(unit==ALineLengthUnit.Centimeter ? 10m : 1m));
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
