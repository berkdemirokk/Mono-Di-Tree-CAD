using System.Text.RegularExpressions;

namespace TekstilCAD.FileFormats;

public sealed record ChatModelSuggestion(string? ModelId,string? Label,string Explanation);

/// <summary>Bounded exact intent grammar. Unrecognized prose is never rewritten or interpreted as approval.</summary>
public static class ChatModelChoice
{
    private static readonly (string Id,string Label,string[] Names)[] Models=[
        ("wrap","Anvelop elbise",["anvelop elbise","anvelop"]),
        ("aline","A kesim elbise",["a kesim elbise","a kesim","a-kesim elbise","a-kesim"]),
        ("corset","Balenli korpus",["balenli korpus","balenli korsaj"]),
        ("sheath","Minimal kılıf",["minimal kılıf elbise","minimal kılıf","kılıf elbise"]),
        ("straight","Düz elbise",["düz elbise","düz kesim elbise"]),
        ("shirt","Gömlek elbise",["gömlek elbise","gömlek tipi elbise"])
    ];
    private static string Fold(string value)=>Regex.Replace(value.ToLowerInvariant().Replace('İ','i').Replace('ı','i'),@"\s+"," ",RegexOptions.None,TimeSpan.FromMilliseconds(100)).Trim();
    public static ChatModelSuggestion Review(string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if(message.Length>DesignRequestStorage.MaximumTextLength)throw new ArgumentException("Mesaj çok uzun.");
        var folded=Fold(message);
        var found=Models.Where(m=>m.Names.Any(n=>Regex.IsMatch(folded,@"(?<!\p{L})"+Regex.Escape(Fold(n))+@"(?!\p{L})",RegexOptions.None,TimeSpan.FromMilliseconds(100)))).ToArray();
        if(found.Length==0)return new(null,null,"");
        if(found.Length!=1)return new(null,null,"Birden fazla model adı var; seçim yapılmadı. Tek model adını ayrı mesajda yazın.");
        var model=found[0];
        // Anchored positive grammar intentionally excludes negations, questions,
        // quotations, alternatives and model mentions inside a longer instruction.
        var names=string.Join("|",model.Names.Select(n=>Regex.Escape(Fold(n))));
        var pattern=@"\A(?:lütfen )?(?:(?:modelim|model olarak|model seçimi)[: ]+)?(?:"+names+@")(?:(?: istiyorum)|(?: seç)|(?: seçelim)|(?: olsun))?[.!]?\z";
        if(!Regex.IsMatch(folded,pattern,RegexOptions.None,TimeSpan.FromMilliseconds(100)))
            return new(null,null,"Model ifadesi seçim için açık değil; seçim yapılmadı. Örneğin: A kesim elbise istiyorum. Ayrıntıları ayrı mesajda yazabilirsiniz.");
        return new(model.Id,model.Label,"Model fikri tanındı; seçmek için onay gerekir. Kalıp oluşturulmaz.");
    }
}
