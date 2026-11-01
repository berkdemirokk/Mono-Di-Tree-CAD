namespace TekstilCAD.FileFormats;

/// <summary>Checks that an export filename identifies the format chosen by the user.</summary>
public static class ExportDestination
{
    /// <param name="format">One-based export format index: DXF, SVG, HPGL, PDF, A4 PDF, or Y-inverted DXF.</param>
    public static void ValidateExtension(string path, int format)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var (label, extensions) = format switch
        {
            1 => ("DXF", new[] { ".dxf" }),
            2 => ("SVG", new[] { ".svg" }),
            3 => ("HPGL", new[] { ".plt", ".hpgl" }),
            4 => ("PDF", new[] { ".pdf" }),
            5 => ("A4 PDF", new[] { ".pdf" }),
            6 => ("DXF", new[] { ".dxf" }),
            _ => throw new ArgumentOutOfRangeException(nameof(format), "Bilinmeyen dışa aktarım biçimi.")
        };
        var extension = Path.GetExtension(path);
        if (!extensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException($"Seçilen {label} biçimi için dosya uzantısı {string.Join(" veya ", extensions)} olmalıdır. Dosya adı veya biçim seçimini düzeltin; dosya değiştirilmedi.");
    }
}
