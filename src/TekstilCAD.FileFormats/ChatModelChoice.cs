using System.Text.RegularExpressions;
using System.Text;

namespace TekstilCAD.FileFormats;

public sealed record ChatModelSuggestion(string? ModelId,string? Label,string Explanation);

/// <summary>Bounded exact intent grammar. Unrecognized prose is never rewritten or interpreted as approval.</summary>
public static class ChatModelChoice
{
    public static bool CanReevaluate(string? lastMessage,string currentPrompt,string unsentMessage)
        => !string.IsNullOrWhiteSpace(lastMessage) && unsentMessage.Length==0 && currentPrompt.EndsWith(lastMessage,StringComparison.Ordinal) &&
           currentPrompt.IndexOf(lastMessage,StringComparison.Ordinal)==currentPrompt.Length-lastMessage.Length;
    private static readonly (string Id,string Label,string[] Names)[] Models=[
        ("wrap","Anvelop elbise",["anvelop elbise","anvelop"]),
        ("aline","A kesim elbise",["a kesim elbise","a kesim","a-kesim elbise","a-kesim"]),
        ("corset","Balenli korpus",["balenli korpus","balenli korsaj"]),
        ("sheath","Minimal kılıf",["minimal kılıf elbise","minimal kılıf","kılıf elbise"]),
        ("straight","Düz elbise",["düz elbise","düz kesim elbise"]),
        ("shirt","Gömlek elbise",["gömlek elbise","gömlek tipi elbise"])
    ];
    private static string Fold(string value)=>Regex.Replace(value.Normalize(NormalizationForm.FormC).ToLowerInvariant().Replace('İ','i').Replace('ı','i'),@"\s+"," ",RegexOptions.None,TimeSpan.FromMilliseconds(100)).Trim();
    public static ChatModelSuggestion Review(string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if(message.Length>DesignRequestStorage.MaximumTextLength)throw new ArgumentException("Mesaj çok uzun.");
        string folded;
        try { folded=Fold(message); }
        catch(ArgumentException) { return new(null,null,"Mesaj geçerli Unicode değil; model önerilmedi, özgün metin korunur."); }
        var found=Models.Where(m=>m.Names.Any(n=>Regex.IsMatch(folded,@"(?<!\p{L})"+Regex.Escape(Fold(n))+@"(?!\p{L})",RegexOptions.None,TimeSpan.FromMilliseconds(100)))).ToArray();
        if(found.Length==0)return new(null,null,"");
        if(found.Length!=1)return new(null,null,"Birden fazla model adı var; seçim yapılmadı. Tek model adını ayrı mesajda yazın.");
        var model=found[0];
        // Each clause must be positively understood. Unknown text is not discarded:
        // it prevents a suggestion while the caller retains the original message.
        var names=string.Join("|",model.Names.Select(n=>Regex.Escape(Fold(n))));
        var pattern=@"\A(?:lütfen )?(?:(?:modelim|model olarak|model seçimi)[: ]+)?(?:"+names+@")(?:(?: istiyorum)|(?: seç)|(?: seçelim)|(?: olsun))?[.!]?\z";
        var combinedPattern=@"\A(?:lütfen )?(?:(?:modelim|model olarak|model seçimi)[: ]+)?(?:"+names+@")(?: istiyorum| olsun) (?<detail>.+)\z";
        var clauses=Regex.Split(message,@";|\r\n|[\r\n]|,(?![0-9])",RegexOptions.None,TimeSpan.FromMilliseconds(100)).Select(Fold).ToArray();
        var attributes=new Dictionary<string,string>();var measurementClauses=new List<string>();int modelClauses=0;
        bool clear=clauses.Length<=32 && clauses.All(c=>c.Length>0);
        foreach(var clause in clauses)
        {
            if(!clear)break;
            if(Regex.IsMatch(clause,pattern,RegexOptions.None,TimeSpan.FromMilliseconds(100))) { modelClauses++;continue; }
            var combined=Regex.Match(clause,combinedPattern,RegexOptions.None,TimeSpan.FromMilliseconds(100));
            var detail=Regex.Replace(combined.Success?combined.Groups["detail"].Value:clause,@"(?: olsun| istiyorum)?[.!]?\z","",RegexOptions.None,TimeSpan.FromMilliseconds(100));
            var category=detail switch {
                "uzun kollu" or "kisa kollu" or "kolsuz" or "truvakar kollu"=>"kol",
                "v yaka" or "yuvarlak yaka" or "bisiklet yaka" or "hakim yaka"=>"yaka",
                "diz alti" or "diz üstü" or "midi boy" or "maksi boy" or "mini boy"=>"boy",
                "kirmizi" or "mavi" or "siyah" or "beyaz" or "yeşil" or "bej"=>"renk",
                "pamuklu" or "keten" or "saten" or "şifon"=>"kumaş",_=>null };
            if(category is not null)
            {
                if(combined.Success)modelClauses++;
                if(attributes.TryGetValue(category,out var prior) && prior!=detail) { clear=false;break; }
                attributes[category]=detail;continue;
            }
            if(combined.Success) { clear=false;break; }
            var measurements=DressMeasurementParser.Review("",clause);
            if(measurements.Measurements.Count==1 && measurements.Unrecognized.Count==0)
                measurementClauses.Add(clause);
            else clear=false;
        }
        if(measurementClauses.Count>0)
        {
            var review=DressMeasurementParser.Review("",string.Join(";",measurementClauses));
            if(review.Questions.Any(q=>q.Contains("çeliş") || q.Contains("belirsiz")))clear=false;
        }
        if(!clear || modelClauses!=1)
            return new(null,null,"Model veya ayrıntılar seçim için açık değil; seçim yapılmadı. Örnek: A kesim elbise istiyorum, uzun kollu; bel çevresi 72 cm. Anlaşılmayan ayrıntılar için modeli ayrı mesajda seçebilirsiniz.");
        return new(model.Id,model.Label,"Model fikri tanındı; seçmek için onay gerekir. Kalıp oluşturulmaz.");
    }
}
