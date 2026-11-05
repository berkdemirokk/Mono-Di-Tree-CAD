using System.Globalization;
using System.Text.RegularExpressions;
using TekstilCAD.Core;
namespace MeasurementSlice;

// Start with the application's measurement lexical rule, then reject three-digit
// separator forms that can also be read as grouped integers. Decimal arithmetic
// is retained for exact user-visible comparison and conversion.
internal static class ExplicitNumber
{
    static readonly Regex Syntax=new(@"\A[+-]?[0-9]+(?:[.,][0-9]+)?\z",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
    internal static bool TryParse(string? input,out decimal value)
    {
        value=0;if(input is null||input.Length>24)return false;var text=input.Trim();
        if(!Syntax.IsMatch(text)||!MeasurementInput.TryParseMillimeters(text,out _))return false;
        int index=text.IndexOfAny([',','.']);if(index>=0&&text.Length-index-1==3)return false;
        return decimal.TryParse(text.Replace(',','.'),NumberStyles.AllowLeadingSign|NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out value);
    }
    internal static string Format(decimal value)
    {
        string result=value.ToString("0.############################",CultureInfo.InvariantCulture);
        if(!TryParse(result,out var roundtrip)){result+="0";if(!TryParse(result,out roundtrip))throw new InvalidOperationException("Dönüşüm açık biçimde gösterilemiyor.");}
        if(roundtrip!=value||result.Length>24)throw new InvalidOperationException("Dönüşüm kayıpsız gösterilemiyor.");return result;
    }
}
