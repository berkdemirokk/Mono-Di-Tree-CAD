using System.Globalization;
using System.Text.RegularExpressions;

namespace TekstilCAD.FileFormats;

// Entire whitespace-separated phrase only; source ranges always refer to original UTF-16 text.
internal static class CompoundChatRequest
{
    internal sealed record Atom(int Start,int Length,string? Category,string? Label,decimal? Millimeters);
    internal sealed record Result(string ModelId,IReadOnlyList<Atom> Measurements);
    private static readonly Regex Head=new(@"\A(?<model>.+? (?:istiyorum|olsun)) ",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
    private static readonly Regex Measurement=new(@"\G(?<label>göğüs çevresi|bel çevresi|kalça çevresi|elbise boyu|kol boyu) (?<number>[0-9]{1,5}(?:[.,][0-9]{1,8})?) ?(?<unit>cm|mm)(?![\p{L}\p{M}\p{N}_])",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
    private static readonly (string Text,string Category)[] Details=[
        ("uzun kollu","kol"),("kısa kollu","kol"),("kisa kollu","kol"),("kolsuz","kol"),("truvakar kollu","kol"),
        ("v yaka","yaka"),("yuvarlak yaka","yaka"),("bisiklet yaka","yaka"),("hakim yaka","yaka"),
        ("diz altı","boy"),("diz alti","boy"),("diz üstü","boy"),("midi boy","boy"),("maksi boy","boy"),("mini boy","boy"),
        ("kırmızı","renk"),("kirmizi","renk"),("mavi","renk"),("siyah","renk"),("beyaz","renk"),("yeşil","renk"),("bej","renk"),
        ("pamuklu","kumaş"),("keten","kumaş"),("saten","kumaş"),("şifon","kumaş")];
    internal static Result? Parse(string text)
    {
        if(text.Length>DesignRequestStorage.MaximumTextLength)return null;
        var folded=text.Replace('İ','i').ToLowerInvariant();
        var head=Head.Match(folded);
        if(!head.Success)return null;
        var model=ChatModelChoice.Review(text[..head.Groups["model"].Length]).ModelId;
        if(model is null)return null;
        var attributes=new Dictionary<string,string>();var measurements=new Dictionary<string,decimal>();var atoms=new List<Atom>();
        int position=head.Length,count=0;
        while(position<folded.Length)
        {
            if(++count>32)return null;
            var match=Measurement.Match(folded,position);
            if(match.Success)
            {
                if(!decimal.TryParse(match.Groups["number"].Value.Replace(',','.'),NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var value))return null;
                if(match.Groups["unit"].Value=="cm")value*=10m;
                var label=match.Groups["label"].Value;
                if(value<=0 || value>10000m || (measurements.TryGetValue(label,out var prior) && prior!=value))return null;
                measurements[label]=value;atoms.Add(new(position,match.Length,null,label,value));position+=match.Length;
            }
            else
            {
                var found=Details.FirstOrDefault(d=>folded.AsSpan(position).StartsWith(d.Text,StringComparison.Ordinal) && (position+d.Text.Length==folded.Length || folded[position+d.Text.Length]==' '));
                if(found.Text is null)return null;
                var canonical=found.Text.Replace('ı','i');
                if(attributes.TryGetValue(found.Category,out var prior) && prior!=canonical)return null;
                attributes[found.Category]=canonical;position+=found.Text.Length;
            }
            if(position==folded.Length)break;
            if(folded[position]!=' ')return null;
            while(position<folded.Length && folded[position]==' ')position++;
            if(position==folded.Length)return null;
        }
        return count>0 ? new(model,atoms.AsReadOnly()) : null;
    }
}
