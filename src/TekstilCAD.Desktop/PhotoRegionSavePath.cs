using System.IO;

namespace TekstilCAD.Desktop;

internal static class PhotoRegionSavePath
{
    internal static string Complete(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var name=Path.GetFileName(path);
        if(string.IsNullOrWhiteSpace(name) || name.EndsWith('.') || char.IsWhiteSpace(name[^1]))
            throw new InvalidDataException("Dosya adı boş olamaz, nokta veya boşlukla bitemez. Bölge dosyası için .tdregion uzantısını kullanın.");
        var extension=Path.GetExtension(name);
        if(extension.Length==0)path+=".tdregion";
        else if(!extension.Equals(".tdregion",StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Dosya uzantısı .tdregion olmalı. Dosya adını düzeltip yeniden kaydedin; farklı uzantı otomatik değiştirilmedi.");
        return Path.GetFullPath(path);
    }
}
