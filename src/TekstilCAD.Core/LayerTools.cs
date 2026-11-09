namespace TekstilCAD.Core;

/// <summary>
/// Layer list operations that keep one invariant: a document always has exactly
/// one default layer (<see cref="Layer.DefaultId"/>), and every piece points to
/// an existing layer or to the default one.
/// </summary>
public static class LayerTools
{
    public const int MaximumNameLength = 128;

    /// <summary>Layers of the document, materializing the implicit default layer.</summary>
    public static IReadOnlyList<Layer> Layers(ProjectDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.Layers ?? Layer.Defaults();
    }

    public static Layer? Find(ProjectDocument document, Guid layerId) =>
        Layers(document).FirstOrDefault(layer => layer.Id == layerId);

    public static Layer DefaultLayer(ProjectDocument document) =>
        Find(document, Layer.DefaultId) ?? throw new InvalidDataException("Varsayılan katman bulunamadı.");

    public static Guid LayerIdOf(PatternPiece piece) => piece.LayerId;

    /// <summary>True when the piece belongs to a visible layer. Unknown ids are treated as visible.</summary>
    public static bool IsPieceVisible(ProjectDocument document, PatternPiece piece) =>
        Find(document, piece.LayerId)?.Visible ?? true;

    /// <summary>True when the piece belongs to a locked layer. Unknown ids are treated as unlocked.</summary>
    public static bool IsPieceLocked(ProjectDocument document, PatternPiece piece) =>
        Find(document, piece.LayerId)?.Locked ?? false;

    /// <summary>Pieces of a layer in document order.</summary>
    public static List<PatternPiece> PiecesIn(ProjectDocument document, Guid layerId) =>
        document.Pieces.Where(piece => piece.LayerId == layerId).ToList();

    /// <summary>Adds a layer. The explicit list is created on demand, with the default layer first.</summary>
    public static Layer Add(ProjectDocument document, string name)
    {
        ArgumentNullException.ThrowIfNull(document);
        var validated = ValidateName(name);
        var layers = document.Layers;
        if (layers is null)
        {
            layers = Layer.Defaults();
            document.Layers = layers;
        }
        if (layers.Any(layer => string.Equals(layer.Name, validated, StringComparison.Ordinal)))
            throw new ArgumentException("Bu adda bir katman zaten var.", nameof(name));
        if (layers.Count >= ProjectStorageLimits.MaximumLayers)
            throw new InvalidOperationException("Katman sayısı sınıra ulaştı.");
        var layer = new Layer { Name = validated };
        layers.Add(layer);
        return layer;
    }

    public static void Rename(ProjectDocument document, Guid layerId, string name)
    {
        ArgumentNullException.ThrowIfNull(document);
        var layer = Find(document, layerId) ?? throw new ArgumentException("Katman bulunamadı.", nameof(layerId));
        var validated = ValidateName(name);
        if (Layers(document).Any(other => other.Id != layerId && string.Equals(other.Name, validated, StringComparison.Ordinal)))
            throw new ArgumentException("Bu adda bir katman zaten var.", nameof(name));
        layer.Name = validated;
    }

    public static void SetVisible(ProjectDocument document, Guid layerId, bool visible)
    {
        ArgumentNullException.ThrowIfNull(document);
        var layer = Find(document, layerId) ?? throw new ArgumentException("Katman bulunamadı.", nameof(layerId));
        layer.Visible = visible;
    }

    public static void SetLocked(ProjectDocument document, Guid layerId, bool locked)
    {
        ArgumentNullException.ThrowIfNull(document);
        var layer = Find(document, layerId) ?? throw new ArgumentException("Katman bulunamadı.", nameof(layerId));
        layer.Locked = locked;
    }

    /// <summary>Moves a piece to a layer. The target must exist.</summary>
    public static void Assign(ProjectDocument document, PatternPiece piece, Guid layerId)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(piece);
        if (Find(document, layerId) is null) throw new ArgumentException("Katman bulunamadı.", nameof(layerId));
        piece.LayerId = layerId;
    }

    /// <summary>
    /// Removes a layer and moves its pieces to the default layer. The default
    /// layer itself cannot be removed. When only the default layer is left, the
    /// explicit list is dropped again so the project keeps its compact form.
    /// </summary>
    public static bool Delete(ProjectDocument document, Guid layerId)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (layerId == Layer.DefaultId) throw new InvalidOperationException("Varsayılan katman silinemez.");
        var layers = document.Layers;
        if (layers is null || layers.Count == 0) return false;
        var layer = layers.FirstOrDefault(candidate => candidate.Id == layerId);
        if (layer is null) return false;
        layers.Remove(layer);
        foreach (var piece in document.Pieces)
            if (piece.LayerId == layerId) piece.LayerId = Layer.DefaultId;
        if (layers.Count == 1) document.Layers = null;
        return true;
    }

    /// <summary>Next unused "Katman N" name, so repeated adds stay distinguishable.</summary>
    public static string SuggestName(ProjectDocument document)
    {
        var names = Layers(document).Select(layer => layer.Name).ToHashSet(StringComparer.Ordinal);
        for (var index = 1; ; index++)
        {
            var candidate = $"Katman {index}";
            if (!names.Contains(candidate)) return candidate;
        }
    }

    public static string ValidateName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name.Length is < 1 or > MaximumNameLength) throw new ArgumentException($"Katman adı 1–{MaximumNameLength} karakter olmalıdır.", nameof(name));
        if (name != name.Trim()) throw new ArgumentException("Katman adı başta veya sonda boşluk içeremez.", nameof(name));
        if (name.Any(char.IsControl)) throw new ArgumentException("Katman adı kontrol karakteri içeremez.", nameof(name));
        if (!IsWellFormedUtf16(name)) throw new ArgumentException("Katman adı geçerli Unicode metni olmalıdır.", nameof(name));
        return name;
    }

    // Unpaired surrogates would be silently replaced on save; the project format
    // rejects them, so the editor rejects them first with a readable message.
    private static bool IsWellFormedUtf16(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            if (char.IsHighSurrogate(value[i]))
            {
                if (i + 1 >= value.Length || !char.IsLowSurrogate(value[i + 1])) return false;
                i++;
            }
            else if (char.IsLowSurrogate(value[i])) return false;
        }
        return true;
    }
}

/// <summary>Layer limits shared by the editor and the project format.</summary>
public static class ProjectStorageLimits
{
    public const int MaximumLayers = 10_000;
}
