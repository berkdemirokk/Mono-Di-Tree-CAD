using System.Text.RegularExpressions;

namespace TekstilCAD.Desktop;

internal sealed record PhotoObservationEdit(string Description,string Previous,string? Replacement)
{
    internal static PhotoObservationEdit Plan(string original,int field,int selected,bool remove,int maximumLength)
    {
        if(field is <0 or >2)throw new ArgumentException("Gözlem alanı geçersiz.");
        string[] names=["Siluet","Kol","Yaka"];
        var known=new Dictionary<string,int>(StringComparer.Ordinal);
        for(var i=0;i<3;i++)for(var choice=1;choice<=3;choice++)
        {
            int[] selections=[0,0,0];selections[i]=choice;
            known.Add(PhotoObservationWindow.Build(selections[0],selections[1],selections[2])[0],i);
        }
        var found=new Dictionary<int,Match>();
        foreach(Match line in Regex.Matches(original,@"[^\r\n]+",RegexOptions.None,TimeSpan.FromMilliseconds(100)))
        {
            if(!line.Value.TrimStart().StartsWith("Fotoğraf gözlemi",StringComparison.OrdinalIgnoreCase))continue;
            if(!known.TryGetValue(line.Value,out var category) || !found.TryAdd(category,line))
                throw new ArgumentException("Tekrarlı veya düzenlenmiş fotoğraf gözlemi var; güvenli değişiklik yapılamadı. Mevcut tarifi elle kontrol edin.");
        }
        if(!found.TryGetValue(field,out var target))throw new ArgumentException(names[field]+": değiştirilecek tekil kayıtlı gözlem yok.");
        string? replacement=null;
        if(!remove)
        {
            if(selected is <1 or >3)throw new ArgumentException("Değiştirmek için açık bir yeni özellik seçin. Bilinmiyor seçimi önceki gözlemi silmez.");
            int[] selections=[0,0,0];selections[field]=selected;
            replacement=PhotoObservationWindow.Build(selections[0],selections[1],selections[2])[0];
            if(replacement==target.Value)throw new ArgumentException("Seçilen özellik zaten kayıtlı; değişiklik yok.");
        }
        // Leave every surrounding character and line separator intact.
        var result=original.Remove(target.Index,target.Length).Insert(target.Index,replacement??"");
        if(result.Length>maximumLength)throw new ArgumentException("Tarif sınırı aşılır; gözlem değiştirilmedi.");
        return new(result,target.Value,replacement);
    }
}
