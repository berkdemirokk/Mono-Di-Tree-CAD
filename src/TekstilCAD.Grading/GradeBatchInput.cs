using System.Globalization;

namespace TekstilCAD.Grading;

/// <summary>A requested size and an optional, explicitly entered cutting quantity.</summary>
public sealed record GradeBatchEntry(GradeSize Size, int? Quantity);

public static class GradeBatchInput
{
    public static IReadOnlyList<GradeBatchEntry> Parse(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Length > 1_000_000) throw new ArgumentException("Beden listesi çok uzun; en fazla 1.000.000 karakter girin.");
        var result = new List<GradeBatchEntry>();
        var names = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        // Treat CRLF, LF and CR as line boundaries. Removing CR can silently
        // join adjacent fields (for example, "S;1\r0" becomes step 10).
        var lines = input.Split(["\r\n", "\n", "\r"], StringSplitOptions.None);
        for (var index = 0; index < lines.Length; index++)
        {
            if (string.IsNullOrWhiteSpace(lines[index])) continue;
            var line = index + 1;
            var fields = lines[index].Split(';');
            if (fields.Length is < 2 or > 3) throw new ArgumentException($"{line}. satır: Beden adı ve adımı noktalı virgülle ayırın; isteğe bağlı üçüncü alan adettir.");
            var name = fields[0].Trim();
            if (name.Length is < 1 or > 128) throw new ArgumentException($"{line}. satır: Beden adı 1–128 karakter olmalıdır.");
            if (names.TryGetValue(name, out var previous)) throw new ArgumentException($"{line}. satır: Aynı beden adı {previous}. satırda da var; farklı bir ad girin.");
            names.Add(name, line);
            if (!TekstilCAD.Core.MeasurementInput.TryParseMillimeters(fields[1], out var step)) throw new ArgumentException($"{line}. satır: Geçerli bir adım sayısı girin; virgül veya nokta kullanabilirsiniz.");
            int? quantity = null;
            if (fields.Length == 3 && !string.IsNullOrWhiteSpace(fields[2]))
            {
                if (!int.TryParse(fields[2].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value is < 1 or > 100000)
                    throw new ArgumentException($"{line}. satır: Adet 1 ile 100000 arasında tam sayı olmalıdır.");
                quantity = value;
            }
            result.Add(new(new GradeSize(name, step), quantity));
            if (result.Count > 1000) throw new ArgumentException("Bir grupta en fazla 1000 beden olabilir.");
        }
        if (result.Count == 0) throw new ArgumentException("En az bir beden ve adım girin.");
        return result;
    }
    /// <summary>Apply only the metadata explicitly requested for this generated size.</summary>
    public static void ApplyMetadata(TekstilCAD.Core.PatternPiece generated, TekstilCAD.Core.PatternPiece source,
        GradeBatchEntry entry, bool includeSize, bool copyRoleAndMaterial)
    {
        ArgumentNullException.ThrowIfNull(generated);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(entry);
        var role = copyRoleAndMaterial ? source.Metadata?.Role : null;
        var material = copyRoleAndMaterial ? source.Metadata?.Material : null;
        generated.Metadata = includeSize || entry.Quantity.HasValue || role is not null || material is not null
            ? new TekstilCAD.Core.PieceMetadata { Size = includeSize ? entry.Size.Name : null, Role = role, Material = material, Quantity = entry.Quantity }
            : null;
    }}

