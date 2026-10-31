using System.Collections.Immutable;
using System.Globalization;

namespace TekstilCAD.FileFormats;

// This is a strict internal policy for the INSERT subset, not a DXF layer
// table reader. The caller must construct each state from a validated snapshot.
internal sealed record DxfInsertLayerState(
    string Name, bool? Hidden, bool? PlotEnabled, string? LineType,
    bool CuttingSemanticsApproved);

internal sealed record DxfInsertLayerContext(
    string EffectiveLayer, string EffectiveLineType, bool Visible, bool PlotEligible,
    ImmutableArray<int> InsertPath, bool IsInsert);

/// <summary>
/// Resolves nested layer-zero and linetype inheritance conservatively. The
/// caller must supply complete layer states and independently approve their
/// color/cutting meaning. This resolver does not validate geometry, viewport
/// overrides, application data, or authorise a contour for production.
/// </summary>
internal sealed class DxfInsertLayerResolver
{
    private readonly Dictionary<string, DxfInsertLayerState> layers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<string, string> decodeName;

    public DxfInsertLayerResolver(IEnumerable<DxfInsertLayerState> states, Func<string, string> decodeName)
    {
        ArgumentNullException.ThrowIfNull(states);
        this.decodeName = decodeName ?? throw new ArgumentNullException(nameof(decodeName));
        foreach (var state in states)
        {
            if (state is null || string.IsNullOrWhiteSpace(state.Name) || !layers.TryAdd(state.Name, state))
                throw new InvalidDataException("DXF katmanı eksik veya yinelenmiş.");
        }
    }

    public DxfInsertLayerContext Resolve(DxfIndexEntity entity, DxfInsertLayerContext? parentInsert,
        bool isBlockMember, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(entity);
        if (entity.Type is not ("INSERT" or "LWPOLYLINE" or "LINE" or "POLYLINE" or "VERTEX" or "SEQEND"))
            throw new InvalidDataException("DXF katman çözümlemesi için varlık türü desteklenmiyor.");
        if (entity.SourceOrdinal < 0 || entity.Groups.IsDefault || (isBlockMember && (parentInsert is null || !parentInsert.IsInsert)) || (!isBlockMember && parentInsert is not null))
            throw new InvalidDataException("DXF INSERT üst bağlamı geçersiz.");

        string? Field(int code)
        {
            string? value = null;
            bool seen = false;
            foreach (var group in entity.Groups)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (group.Code != code) continue;
                if (seen) throw new InvalidDataException($"DXF {entity.Type} için {code} alanı yinelenmiş.");
                seen = true;
                value = code == 8 ? group.RawValue : group.Value;
                if (string.IsNullOrWhiteSpace(value)) throw new InvalidDataException($"DXF {entity.Type} için {code} alanı boş.");
            }
            return value;
        }
        int? IntegerField(int code)
        {
            var text = Field(code);
            if (text is null) return null;
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                throw new InvalidDataException($"DXF {entity.Type} için {code} alanı geçersiz.");
            return value;
        }

        var rawLayer = Field(8);
        var layer = rawLayer is null ? "0" : decodeName(rawLayer);
        if (string.IsNullOrWhiteSpace(layer)) throw new InvalidDataException("DXF katman adı geçersiz.");
        if (isBlockMember && layer.Equals("0", StringComparison.OrdinalIgnoreCase)) layer = parentInsert!.EffectiveLayer;
        if (!layers.TryGetValue(layer, out var state) || state.Hidden is null || state.PlotEnabled is null || !state.CuttingSemanticsApproved)
            throw new InvalidDataException("DXF katmanı veya kesim anlamı doğrulanamadı: " + layer);
        // The display name comes from the validated table, preserving the
        // spelling of the actual effective layer rather than a lookup token.
        layer = state.Name;
        var space = IntegerField(67);
        var layout = Field(410);
        if (space is not (null or 0) || (layout is not null && !layout.Equals("Model", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("DXF kâğıt alanı veya belirsiz yerleşim desteklenmiyor.");
        var invisible = IntegerField(60);
        if (invisible is not (null or 0 or 1)) throw new InvalidDataException("DXF görünürlük alanı geçersiz.");

        // Color may encode cut/sew meaning. Only BYLAYER is admitted, and
        // even that requires the table state to have explicit approval above.
        var aci = IntegerField(62);
        if (aci is not (null or 256) || Field(420) is not null || Field(430) is not null)
            throw new InvalidDataException("DXF açık renk veya BYBLOCK renk anlamı doğrulanmadı.");

        var requestedType = Field(6) ?? "BYLAYER";
        var lineType = requestedType.Equals("BYLAYER", StringComparison.OrdinalIgnoreCase) ? state.LineType
            : requestedType.Equals("BYBLOCK", StringComparison.OrdinalIgnoreCase) ? parentInsert?.EffectiveLineType
            : requestedType;
        if (!string.Equals(lineType, "CONTINUOUS", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("DXF çizgi tipi mirası bilinmiyor veya sürekli değil.");

        var path = parentInsert?.InsertPath ?? ImmutableArray<int>.Empty;
        if (path.IsDefault || path.Length > 64) throw new InvalidDataException("DXF INSERT yolu geçersiz.");
        if (entity.Type == "INSERT")
        {
            if (path.Length >= 64) throw new InvalidDataException("DXF INSERT yolu çok uzun.");
            path = path.Add(entity.SourceOrdinal);
        }
        var visible = (parentInsert?.Visible ?? true) && !state.Hidden.Value && invisible != 1;
        var plot = (parentInsert?.PlotEligible ?? true) && visible && state.PlotEnabled.Value;
        return new(layer, "CONTINUOUS", visible, plot, path, entity.Type == "INSERT");
    }
}
