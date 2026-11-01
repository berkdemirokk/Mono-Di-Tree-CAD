using System.Globalization;
using System.Text.RegularExpressions;

namespace TekstilCAD.FileFormats;

public sealed record ExplicitDressMeasurement(string Label,decimal Millimeters,IReadOnlyList<string> SourceExpressions);
public sealed record DressMeasurementReview(IReadOnlyList<ExplicitDressMeasurement> Measurements,IReadOnlyList<string> Questions,IReadOnlyList<string> Unrecognized);

/// <summary>Conservative, offline extraction; it never infers a garment or measurements.</summary>
public static partial class DressMeasurementParser
{
    private static readonly string[] Required=["göğüs çevresi","bel çevresi","kalça çevresi","elbise boyu"];
    private const string Labels=@"(?:göğüs(?:\s+çevresi)?|bel(?:\s+çevresi)?|kalça(?:\s+çevresi)?|elbise\s+boyu|kol\s+boyu)";
    private const string ExactLabels=@"(?:göğüs\s+çevresi|bel\s+çevresi|kalça\s+çevresi|elbise\s+boyu|kol\s+boyu)";
    private static readonly Regex SegmentSplit=new(@"[;\r\n]+|,(?=\s*"+Labels+@"\b)",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
    private static readonly Regex LabelPattern=new(@"\b"+Labels+@"\b",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
    private static readonly Regex Exact=new(@"^\s*(?<label>"+ExactLabels+@")\s*(?::|=|\s)\s*(?<number>[0-9]{1,5}(?:[.,][0-9]{1,8})?)\s*(?<unit>mm|cm)\s*(?:olsun)?\s*[.!?]?\s*$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
    // Invariant regex case folding does not match Turkish capital dotted İ. Keep
    // this transformation length-preserving so match offsets still index the
    // original text, which is retained as evidence.
    private static string FoldCase(string text)=>text.Replace('İ','i').ToLowerInvariant();
    private static string Canonical(string label)
    {
        label=Regex.Replace(FoldCase(label),@"\s+"," ");
        return label switch { "göğüs" or "göğüs çevresi"=>"göğüs çevresi", "bel" or "bel çevresi"=>"bel çevresi", "kalça" or "kalça çevresi"=>"kalça çevresi", "elbise boyu"=>"elbise boyu", "kol boyu"=>"kol boyu",_=>throw new InvalidOperationException() };
    }
    public static DressMeasurementReview Review(string description,string measurements)
    {
        if(description is null || measurements is null || description.Length>DesignRequestStorage.MaximumTextLength || measurements.Length>DesignRequestStorage.MaximumTextLength)
            throw new ArgumentException("İstek metni sınırı aşıyor.");
        var values=new Dictionary<string,List<(decimal Value,string Source)>>(StringComparer.Ordinal);
        var unclear=new HashSet<string>(StringComparer.Ordinal);var other=new List<string>();
        foreach(var text in new[] { description,measurements })
        {
        var folded=FoldCase(text);var start=0;
        foreach(Match separator in SegmentSplit.Matches(folded).Cast<Match>().Append(Match.Empty))
        {
            var end=separator.Success ? separator.Index : text.Length;
            var segment=text[start..end].Trim();start=separator.Success ? separator.Index+separator.Length : text.Length;
            if(segment.Length==0)continue;
            var search=FoldCase(segment);
            var labels=LabelPattern.Matches(search);var match=Exact.Match(search);
            if(!match.Success)
            {
                foreach(Match label in labels)unclear.Add(Canonical(label.Value));
                other.Add(segment);continue;
            }
            var key=Canonical(match.Groups["label"].Value);
            if(!decimal.TryParse(match.Groups["number"].Value.Replace(',','.'),NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var value))
            { unclear.Add(key);other.Add(segment);continue; }
            if(match.Groups["unit"].Value.Equals("cm",StringComparison.OrdinalIgnoreCase))value*=10m;
            if(value<=0 || value>10_000m) { unclear.Add(key);other.Add(segment);continue; }
            if(!values.TryGetValue(key,out var list))values[key]=list=[];
            list.Add((value,segment));
        }
        }
        var output=new List<ExplicitDressMeasurement>();var questions=new List<string>();
        foreach(var key in Required.Append("kol boyu"))
        {
            values.TryGetValue(key,out var found);
            bool conflict=found is not null && found.Any(x=>x.Value!=found[0].Value);
            if(unclear.Contains(key) || conflict)
            { questions.Add($"{key}: ifade belirsiz veya birbiriyle çelişiyor; tek değeri ve birimini açıkça belirtin.");continue; }
            if(found is not null)output.Add(new(key,found[0].Value,found.Select(x=>x.Source).ToArray()));
            else if(Required.Contains(key))questions.Add($"{key}: açık ölçü ve birim nedir?");
        }
        return new(output.AsReadOnly(),questions.AsReadOnly(),other.AsReadOnly());
    }
}
