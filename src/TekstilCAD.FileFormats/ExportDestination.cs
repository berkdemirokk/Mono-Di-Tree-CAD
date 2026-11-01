namespace TekstilCAD.FileFormats;

/// <summary>Checks that an export filename identifies the format chosen by the user.</summary>
public static class ExportDestination
{
    /// <param name="format">The export dialog's one-based format index: 1 DXF, 2 SVG, 3 HPGL, 4 PDF.</param>
    public static void ValidateExtension(string path, int format)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var (label, extensions) = format switch
        {
            1 => ("DXF", new[] { ".dxf" }),
            2 => ("SVG", new[] { ".svg" }),
            3 => ("HPGL", new[] { ".plt", ".hpgl" }),
            4 => ("PDF", new[] { ".pdf" }),
            _ => throw new ArgumentOutOfRangeException(nameof(format), "Bilinmeyen dışa aktarım biçimi.")
        };
        var extension = Path.GetExtension(path);
        if (!extensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException($"Seçilen {label} biçimi için dosya uzantısı {string.Join(" veya ", extensions)} olmalıdır. Dosya adı veya biçim seçimini düzeltin; dosya değiştirilmedi.");
    }
}
