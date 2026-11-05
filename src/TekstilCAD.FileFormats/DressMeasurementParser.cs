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
    // A question is not an asserted measurement. It enters the existing unclear
    // branch, including every member of a conjunction and related conflict groups.
    private static readonly Regex Exact=new(@"^\s*(?<label>"+ExactLabels+@")\s*(?::|=|\s)\s*(?<number>[0-9]{1,5}(?:[.,][0-9]{1,8})?)\s*(?<unit>mm|cm)\s*(?:olsun)?\s*[.!]?\s*$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
    // Narrow clarification grammar: no "olsun", qualifier, negation or joined group.
    private static readonly Regex Clarification=new(@"\A\s*(?<label>"+ExactLabels+@")\s*(?::|=|\s)\s*(?<number>[0-9]{1,5}(?:[.,][0-9]{1,8})?)\s*(?<unit>mm|cm)\s*(?:(?<question>\?)|[.!])?\s*\z",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
    private static readonly Regex ModelMeasurement=new(@"\A(?<model>.+? (?:istiyorum|olsun)) (?<measurement>.+)\z",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
    // Returns an offset into the original segment; never rewrites stored input.
    internal static bool TrySplitModelMeasurement(string segment,out int measurementOffset)
    {
        measurementOffset=0;
        var match=ModelMeasurement.Match(FoldCase(segment));
        if(!match.Success)return false;
        var offset=match.Groups["measurement"].Index;
        var measurement=Exact.Match(FoldCase(segment[offset..]));
        if(!measurement.Success || !decimal.TryParse(measurement.Groups["number"].Value.Replace(',','.'),NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var value))return false;
        if(measurement.Groups["unit"].Value.Equals("cm",StringComparison.OrdinalIgnoreCase))value*=10m;
        if(value<=0 || value>10_000m || ChatModelChoice.Review(segment[..match.Groups["model"].Length]).ModelId is null)return false;
        measurementOffset=offset;return true;
    }
    // Explicit, bounded vocabulary only: never infer Turkish conditional suffixes
    // (for example the ordinary word "elbise" must not trigger a condition).
    private static readonly Regex UnscopedCondition=new(@"(?<![\p{L}\p{M}\p{N}_])(?:eğer|şayet|gerekirse|koşuluyla|şartıyla|takdirde|olursa|olmazsa|değilse|ise)(?![\p{L}\p{M}\p{N}_])",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
    private sealed record ClauseEvidence(int Field,int Offset,int Order,decimal? Value,bool Question);
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
        var evidence=new Dictionary<string,List<ClauseEvidence>>(StringComparer.Ordinal);
        var texts=new[]{description,measurements};
        // Neither separators nor the two text fields establish the scope/end of
        // a condition. Keep this cause independent of question clarification.
        var hasUnscopedCondition=texts.Any(text=>UnscopedCondition.IsMatch(FoldCase(text)));
        for(int field=0;field<texts.Length;field++)
        {
        var text=texts[field];
        var folded=FoldCase(text);var start=0;var order=0;
        foreach(Match separator in SegmentSplit.Matches(folded).Cast<Match>().Append(Match.Empty))
        {
            if(separator.Success && JoinedLineBreak(folded,separator))continue;
            var end=separator.Success ? separator.Index : text.Length;var segmentOffset=start;
            var segment=text[start..end].Trim();start=separator.Success ? separator.Index+separator.Length : text.Length;
            if(segment.Length==0)continue;
            order++;
            var compound=CompoundChatRequest.Parse(segment);
            if(compound is not null && compound.Measurements.Count>0)
            {
                var keys=new HashSet<string>(StringComparer.Ordinal);
                foreach(var atom in compound.Measurements)
                {
                    var key=atom.Label!;keys.Add(key);
                    if(!values.TryGetValue(key,out var entries))values[key]=entries=[];
                    entries.Add((atom.Millimeters!.Value,segment));
                    if(!evidence.TryGetValue(key,out var occurrences))evidence[key]=occurrences=[];
                    occurrences.Add(new(field,segmentOffset,order,null,false));
                }
                if(keys.Count>1)coupled.Add(keys);
                continue;
            }
            var combined=TrySplitModelMeasurement(segment,out var measurementOffset);
            var measurementSegment=combined ? segment[measurementOffset..] : segment;
            var search=FoldCase(measurementSegment);
            if(OtherChestMeasurement.IsMatch(search)) { other.Add(segment);continue; }
            var clarification=Clarification.Match(search);decimal? explicitValue=null;
            if(!combined&&clarification.Success&&decimal.TryParse(clarification.Groups["number"].Value.Replace(',','.'),NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var number))
            {
                if(clarification.Groups["unit"].Value.Equals("cm",StringComparison.OrdinalIgnoreCase))number*=10m;
                if(number>0&&number<=10_000m)explicitValue=number;
            }
            // Retain ALL occurrences, including unrecognized clauses, so a valid-looking
            // final reply can never erase an earlier conflict or conditional statement.
            foreach(var label in LabelPattern.Matches(search).Cast<Match>().Select(m=>Canonical(m.Value)).Distinct(StringComparer.Ordinal))
            {
                if(!evidence.TryGetValue(label,out var occurrences))evidence[label]=occurrences=[];
                occurrences.Add(new(field,segmentOffset,order,explicitValue,clarification.Success&&clarification.Groups["question"].Success));
            }
            // A conjunction is accepted only if every constituent is an exact,
            // valid measurement and repeated labels agree. Otherwise the whole
            // original segment remains unrecognized and all its labels are unclear.
            var parts=new List<string>();var partStart=0;
            foreach(Match conjunction in Conjunction.Matches(search))
            { parts.Add(measurementSegment[partStart..conjunction.Index].Trim());partStart=conjunction.Index+conjunction.Length; }
            parts.Add(measurementSegment[partStart..].Trim());
            var parsed=new List<(string Key,decimal Value,string Source)>();
            foreach(var part in parts)
            {
                var match=Exact.Match(FoldCase(part));
                if(!match.Success || !decimal.TryParse(match.Groups["number"].Value.Replace(',','.'),NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var value))
                    break;
                var key=Canonical(match.Groups["label"].Value);
                if(match.Groups["unit"].Value.Equals("cm",StringComparison.OrdinalIgnoreCase))value*=10m;
                if(value<=0 || value>10_000m)break;
                parsed.Add((key,value,combined ? segment : part));
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
        foreach(var (label,occurrences) in evidence)
        {
            if(occurrences.Count!=2)continue;
            var question=occurrences[0];var answer=occurrences[1];
            if(question.Question&&!answer.Question&&question.Value.HasValue&&question.Value==answer.Value&&
                question.Field==answer.Field&&question.Offset<answer.Offset&&answer.Order==question.Order+1&&!coupled.Any(group=>group.Contains(label)))
                unclear.Remove(label);
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
            if(hasUnscopedCondition&&(Required.Contains(key)||found is not null||unclear.Contains(key)))
            {
                questions.Add($"{key}: ifade belirsiz; koşullu ifadenin hangi ölçülere uygulandığı belli değil. Noktalama veya ayrı alan koşulu sonlandırmaz; ölçüler kesin kabul edilmedi."+(conflict?" Ayrıca farklı değerler birbiriyle çelişiyor.":""));
                continue;
            }
            if(unclear.Contains(key) || conflict)
            { questions.Add($"{key}: ifade belirsiz veya birbiriyle çelişiyor; tek değeri ve birimini açıkça belirtin.");continue; }
            if(found is not null)output.Add(new(key,found[0].Value,found.Select(x=>x.Source).ToArray()));
            else if(Required.Contains(key))questions.Add($"{key}: açık ölçü ve birim nedir?");
        }
        return new(output.AsReadOnly(),questions.AsReadOnly(),other.AsReadOnly());
    }
}
