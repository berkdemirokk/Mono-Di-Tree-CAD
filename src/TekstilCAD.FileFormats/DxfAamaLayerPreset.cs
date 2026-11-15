using System.Collections.Immutable;

namespace TekstilCAD.FileFormats;

/// <summary>One recognised AAMA/ASTM D6673 numbered layer found in the imported source.</summary>
public sealed record DxfAamaLayerProposal(string Layer, int Number, string Meaning, DxfTextileRole Role, int PieceCount);

/// <summary>
/// A proposal only. Nothing is applied and no role is assigned until the user
/// confirms the selections through <see cref="DxfTextileLayerRolePlan"/>.
/// </summary>
public sealed record DxfAamaLayerSuggestion(ImmutableArray<DxfAamaLayerProposal> Proposals, ImmutableArray<string> UnrecognizedLayers)
{
    public ImmutableArray<DxfLayerRoleSelection> ToSelections() =>
        Proposals.Select(proposal => new DxfLayerRoleSelection(proposal.Layer, proposal.Role)).ToImmutableArray();
}

/// <summary>
/// Proposes roles for layers named with the numeric convention used by
/// AAMA/ASTM D6673 pattern DXF files (1 = piece boundary, 7 = grain line, ...).
/// A layer is recognised only when its exact source spelling is the plain
/// decimal number; "01", " 1" or "L1" are left for the user. A numeric layer
/// name does not prove the file follows the standard, so the result must be
/// reviewed. The table follows publicly documented layer numbers and was not
/// checked against real factory files.
/// </summary>
public static class DxfAamaLayerPreset
{
    private static readonly ImmutableSortedDictionary<int, string> Known = new Dictionary<int, string>
    {
        [1] = "Parça sınırı (kesim hattı)",
        [2] = "Dönüş noktaları",
        [3] = "Eğri noktaları",
        [4] = "Çentikler",
        [5] = "Serileme referans çizgisi",
        [6] = "Ayna çizgisi",
        [7] = "İplik yönü",
        [8] = "İç çizgiler",
        [10] = "Ekose/çizgi eşleme noktaları",
        [11] = "İç kesikler",
        [13] = "Delik işaretleri",
        [14] = "Dikiş çizgisi",
        [15] = "Açıklama metni",
        [80] = "T çentik",
        [81] = "Kale çentik",
        [82] = "Çek çentik",
        [83] = "U çentik",
        [84] = "Parça sınırı kalite doğrulama eğrileri",
        [85] = "İç çizgi kalite doğrulama eğrileri",
        [86] = "İç kesik kalite doğrulama eğrileri",
        [87] = "Dikiş çizgisi kalite doğrulama eğrileri",
    }.ToImmutableSortedDictionary();

    public static DxfAamaLayerSuggestion Suggest(DxfImportResult import, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(import);
        cancellationToken.ThrowIfCancellationRequested();
        if (import.PieceSources is not { } sources || sources.IsDefault)
            throw new InvalidDataException("DXF parça kaynakları eksik; AAMA katman önerisi oluşturulamadı.");
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var source in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (source is null || string.IsNullOrEmpty(source.Layer))
                throw new InvalidDataException("DXF parça kaynak eşlemesi geçersiz.");
            counts[source.Layer] = counts.GetValueOrDefault(source.Layer) + 1;
        }
        var proposals = ImmutableArray.CreateBuilder<DxfAamaLayerProposal>();
        var unrecognized = ImmutableArray.CreateBuilder<string>();
        foreach (var (layer, count) in counts.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (TryGetNumber(layer, out var number) && Known.TryGetValue(number, out var meaning))
                proposals.Add(new(layer, number, meaning, number == 1 ? DxfTextileRole.Cutting : DxfTextileRole.Auxiliary, count));
            else
                unrecognized.Add(layer);
        }
        proposals.Sort((left, right) => left.Number.CompareTo(right.Number));
        return new(proposals.ToImmutable(), unrecognized.ToImmutable());
    }

    private static bool TryGetNumber(string layer, out int number)
    {
        number = 0;
        if (layer.Length is 0 or > 2 || layer[0] == '0') return false;
        foreach (var character in layer)
        {
            if (character is < '0' or > '9') return false;
            number = number * 10 + (character - '0');
        }
        return true;
    }
}
