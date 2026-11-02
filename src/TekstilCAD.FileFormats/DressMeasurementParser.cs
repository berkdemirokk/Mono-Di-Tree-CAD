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
    private static readonly Regex SegmentSplit=new(@";|(?:\r\n|[\r\n\v\f\u0085\u2028\u2029])(?:[^\S\r\n\v\f\u0085\u2028\u2029]*(?:\r\n|[\r\n\v\f\u0085\u2028\u2029]))*|,(?=\s*"+Labels+@"\b)",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
    private static readonly Regex Conjunction=new(@"\s+ve\s+",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
    private static readonly Regex LabelPattern=new(@"\b"+Labels+@"\b",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
    private static readonly Regex Exact=new(@"^\s*(?<label>"+ExactLabels+@")\s*(?::|=|\s)\s*(?<number>[0-9]{1,5}(?:[.,][0-9]{1,8})?)\s*(?<unit>mm|cm)\s*(?:olsun)?\s*[.!?]?\s*$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
    // These distinct experimental measurements mention "göğüs" but are not
    // chest circumference. Only an entire, explicit field is excluded from
    // ambiguity detection; mixed or qualified statements remain conservative.
    private static readonly Regex OtherChestMeasurement=new(@"^\s*(?:ön göğüs|göğüs noktaları arası|üst göğüs çevresi|ön üst göğüs)\s*(?::|=|\s)\s*[0-9]{1,5}(?:[.,][0-9]{1,8})?\s*(?:mm|cm)\s*[.!?]?\s*$",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
    // Invariant regex case folding does not match Turkish capital dotted İ. Keep
    // this transformation length-preserving so match offsets still index the
    // original text, which is retained as evidence.
    private static string FoldCase(string text)=>text.Replace('İ','i').ToLowerInvariant();
    // A newline next to a standalone conjunction is part of the same clause,
    // not an independent measurement. Semicolons always remain boundaries.
    private static bool IsLineBreak(char c)=>c is '\r' or '\n' or '\v' or '\f' or '\u0085' or '\u2028' or '\u2029';
    private static bool HorizontalWhitespace(char c)=>char.IsWhiteSpace(c) && !IsLineBreak(c);
    private static bool JoinedLineBreak(string folded,Match separator)
    {
        for(int i=separator.Index;i<separator.Index+separator.Length;i++)
            if(!char.IsWhiteSpace(folded[i]))return false;
        var left=separator.Index-1;
        while(left>=0 && HorizontalWhitespace(folded[left]))left--;
        bool endsWithVe=left>=1 && folded[left]=='e' && folded[left-1]=='v' &&
            (left==1 || char.IsWhiteSpace(folded[left-2]));
        var right=separator.Index+separator.Length;
        while(right<folded.Length && HorizontalWhitespace(folded[right]))right++;
        bool startsWithVe=right+1<folded.Length && folded[right]=='v' && folded[right+1]=='e' &&
            (right+2==folded.Length || char.IsWhiteSpace(folded[right+2]));
        return endsWithVe || startsWithVe;
    }
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
        var coupled=new List<HashSet<string>>();
        foreach(var text in new[] { description,measurements })
        {
        var folded=FoldCase(text);var start=0;
        foreach(Match separator in SegmentSplit.Matches(folded).Cast<Match>().Append(Match.Empty))
        {
            if(separator.Success && JoinedLineBreak(folded,separator))continue;
            var end=separator.Success ? separator.Index : text.Length;
            var segment=text[start..end].Trim();start=separator.Success ? separator.Index+separator.Length : text.Length;
            if(segment.Length==0)continue;
            var search=FoldCase(segment);
            if(OtherChestMeasurement.IsMatch(search)) { other.Add(segment);continue; }
            // A conjunction is accepted only if every constituent is an exact,
            // valid measurement and repeated labels agree. Otherwise the whole
            // original segment remains unrecognized and all its labels are unclear.
            var parts=new List<string>();var partStart=0;
            foreach(Match conjunction in Conjunction.Matches(search))
            { parts.Add(segment[partStart..conjunction.Index].Trim());partStart=conjunction.Index+conjunction.Length; }
            parts.Add(segment[partStart..].Trim());
            var parsed=new List<(string Key,decimal Value,string Source)>();
            foreach(var part in parts)
            {
                var match=Exact.Match(FoldCase(part));
                if(!match.Success || !decimal.TryParse(match.Groups["number"].Value.Replace(',','.'),NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var value))
                    break;
                var key=Canonical(match.Groups["label"].Value);
                if(match.Groups["unit"].Value.Equals("cm",StringComparison.OrdinalIgnoreCase))value*=10m;
                if(value<=0 || value>10_000m)break;
                parsed.Add((key,value,part));
            }
            if(parsed.Count!=parts.Count || parsed.GroupBy(p=>p.Key).Any(g=>g.Any(p=>p.Value!=g.First().Value)))
            {
                foreach(Match label in LabelPattern.Matches(search))unclear.Add(Canonical(label.Value));
                other.Add(segment);continue;
            }
            if(parts.Count>1)coupled.Add(parsed.Select(p=>p.Key).ToHashSet(StringComparer.Ordinal));
            foreach(var item in parsed)
            {
                if(!values.TryGetValue(item.Key,out var list))values[item.Key]=list=[];
                list.Add((item.Value,item.Source));
            }
        }
        }
        // A conflict elsewhere invalidates every measurement from a joined
        // expression, including chains of joined expressions.
        foreach(var (key,items) in values)if(items.Any(x=>x.Value!=items[0].Value))unclear.Add(key);
        bool changed;do { changed=false;foreach(var group in coupled)if(group.Overlaps(unclear))foreach(var key in group)changed|=unclear.Add(key); } while(changed);
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
