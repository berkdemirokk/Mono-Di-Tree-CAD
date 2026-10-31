using System.Collections.Immutable;
using System.Globalization;

namespace TekstilCAD.FileFormats;

/// <summary>
/// Reads layer properties from an already decoded DXF snapshot. Layer state
/// does not prove the cutting meaning of any color: every returned state has
/// CuttingSemanticsApproved=false until an explicit independent decision.
/// This reader does not interpret viewport-specific overrides.
/// </summary>
internal static class DxfInsertLayerTable
{
    public static ImmutableArray<DxfInsertLayerState> Read(IReadOnlyList<DxfIndexEntity> entities,
        Func<string, string> decodeName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(decodeName);
        var layers = ImmutableArray.CreateBuilder<DxfInsertLayerState>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool inTables = false, inLayer = false, layerTableSeen = false, viewportData = false;
        foreach (var e in entities)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (e is null || e.Groups.IsDefault) throw new InvalidDataException("DXF varlığı geçersiz.");
            if (e.Type is "VPORT" or "VIEWPORT") viewportData = true;
            if (e.Type == "SECTION")
            {
                if (inTables || inLayer) throw new InvalidDataException("DXF TABLES bölümü kapatılmamış.");
                inTables = Single(e, 2, true)!.Value.Value == "TABLES";
                continue;
            }
            if (e.Type == "ENDSEC")
            {
                if (inLayer) throw new InvalidDataException("DXF LAYER tablosu kapatılmamış.");
                inTables = false;
                continue;
            }
            if (!inTables) continue;
            if (e.Type == "TABLE")
            {
                if (inLayer) throw new InvalidDataException("DXF LAYER tablosu kapatılmamış.");
                if (Single(e, 2, true)!.Value.Value == "LAYER")
                {
                    if (layerTableSeen) throw new InvalidDataException("DXF LAYER tablosu yinelenmiş.");
                    layerTableSeen = inLayer = true;
                }
                continue;
            }
            if (e.Type == "ENDTAB") { inLayer = false; continue; }
            if (!inLayer) continue;
            if (e.Type != "LAYER") throw new InvalidDataException("DXF LAYER tablosunda beklenmeyen kayıt var.");
            var name = decodeName(Single(e, 2, true)!.Value.RawValue);
            if (string.IsNullOrWhiteSpace(name) || !names.Add(name))
                throw new InvalidDataException("DXF katman adı boş veya yinelenmiş.");
            var flags = Integer(e, 70); var color = Integer(e, 62); var plot = Integer(e, 290);
            var rgb = Integer(e, 420); var namedColor = Single(e, 430, false);
            if (flags is < 0 || (flags.HasValue && (flags.Value & ~119) != 0) ||
                color is < -255 or > 255 || color == 0 || plot is not (null or 0 or 1) ||
                rgb is < 0 or > 0xffffff || (namedColor.HasValue && string.IsNullOrWhiteSpace(namedColor.Value.Value)))
                throw new InvalidDataException("DXF katman durumu veya rengi geçersiz.");
            var line = Single(e, 6, false)?.Value;
            if (line is not null && string.IsNullOrWhiteSpace(line)) throw new InvalidDataException("DXF katman çizgi tipi boş.");
            if (line is not null && (line.Equals("BYLAYER", StringComparison.OrdinalIgnoreCase) || line.Equals("BYBLOCK", StringComparison.OrdinalIgnoreCase))) line = null;
            // Bit 1: frozen; bit 2: frozen by default in new viewports; bit 4:
            // locked (does not hide); bits 16/32 are xref-dependent and unknown.
            bool? hidden = flags is null || color is null || (flags.Value & (2 | 16 | 32)) != 0
                ? null : (flags.Value & 1) != 0 || color.Value < 0;
            layers.Add(new(name, hidden, plot is null ? null : plot == 1, line, false));
        }
        if (inLayer || inTables) throw new InvalidDataException("DXF TABLES veya LAYER bölümü kapatılmamış.");
        // The current view can carry visibility overrides outside the layer
        // table. Do not silently claim those layers visible.
        if (viewportData)
            for (int i = 0; i < layers.Count; i++) { cancellationToken.ThrowIfCancellationRequested(); layers[i] = layers[i] with { Hidden = null }; }
        return layers.ToImmutable();
    }

    private static DxfIndexGroup? Single(DxfIndexEntity e, int code, bool required)
    {
        DxfIndexGroup? result = null;
        foreach (var g in e.Groups)
        {
            if (g.Code != code) continue;
            if (result.HasValue) throw new InvalidDataException($"DXF {e.Type} {code} alanı yinelenmiş.");
            result = g;
        }
        if (required && (!result.HasValue || string.IsNullOrWhiteSpace(result.Value.Value)))
            throw new InvalidDataException($"DXF {e.Type} {code} alanı eksik.");
        return result;
    }
    private static int? Integer(DxfIndexEntity e, int code)
    {
        var item = Single(e, code, false);
        if (!item.HasValue) return null;
        if (!int.TryParse(item.Value.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
            throw new InvalidDataException($"DXF {e.Type} {code} alanı geçersiz.");
        return number;
    }
}
