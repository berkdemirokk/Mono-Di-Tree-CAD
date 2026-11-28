using System.Globalization;
using System.Text.RegularExpressions;

namespace TekstilCAD.Core.Commands;

public static partial class CommandParser
{
    private static readonly Regex DartPhrase = new(
        @"^(?<edge>[0-9]+) kenara (?<intake>[0-9]+(?:[,.][0-9]+)?) (?<intakeUnit>mm|milimetre|cm|santimetre) agizli (?<depth>[0-9]+(?:[,.][0-9]+)?) (?<depthUnit>mm|milimetre|cm|santimetre) derinlikte pens ekle$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static ParseResult? TryDartCommand(State state)
    {
        var match=DartPhrase.Match(state.Folded);
        if(!match.Success)return null;
        if(!int.TryParse(match.Groups["edge"].Value,NumberStyles.None,CultureInfo.InvariantCulture,out var edge) || edge is <1 or >4096 ||
           !TryDartMeasure(match.Groups["intake"].Value,match.Groups["intakeUnit"].Value,out var intake) ||
           !TryDartMeasure(match.Groups["depth"].Value,match.Groups["depthUnit"].Value,out var depth) ||
           intake is <1 or >20_000 || depth is <1 or >20_000)
            return ParseResult.Refused(Refusals.Unrecognized(state.Norm));
        return ParseResult.Understood(new DartCommand(edge,intake,depth));
    }

    private static bool TryDartMeasure(string raw,string unit,out double millimeters)
    {
        millimeters=0;
        if(!decimal.TryParse(raw.Replace(',','.'),NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var value))return false;
        var factor=unit is "cm" or "santimetre" ? 10m : 1m;
        var result=value*factor;
        if(result>20_000m)return false;
        millimeters=(double)result;
        return double.IsFinite(millimeters);
    }
}
