namespace TekstilCAD.FileFormats;

/// <summary>Completes only an extensionless name. Destination safety and atomic
/// new-file semantics remain the responsibility of DesignRequestStorage.SaveNew*.</summary>
public static class DesignRequestSavePath
{
    public const string Extension=".tcad-request.json";
    public static string Complete(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var name=Path.GetFileName(path);
        if(string.IsNullOrWhiteSpace(name) || name.EndsWith('.') || char.IsWhiteSpace(name[^1]))
            throw new InvalidDataException("Dosya adı boş olamaz; nokta veya boşlukla bitemez.");
        if(name.EndsWith(Extension,StringComparison.OrdinalIgnoreCase))return path;
        if(Path.HasExtension(name))
            throw new InvalidDataException("Farklı dosya uzantısı değiştirilmedi. Uzantısız ad girin veya "+Extension+" kullanın.");
        return path+Extension;
    }
}
