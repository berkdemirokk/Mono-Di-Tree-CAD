using TekstilCAD.Core;

namespace TekstilCAD.Patterns;

// Human-readable source hints only; these names are editable and are not provenance records.
internal static class OpenPathResultName
{
    private static string Source(PatternPiece piece)
    {
        var name=string.IsNullOrWhiteSpace(piece.Name) ? "Adsız" : piece.Name.Trim();
        const int limit=200;
        if(name.Length>limit)
        {
            var length=limit;
            if(char.IsHighSurrogate(name[length-1]) && char.IsLowSurrogate(name[length]))length--;
            name=name[..length]+"…";
        }
        return $"{name} [{piece.Id:D}]";
    }
    internal static string Joined(PatternPiece first,PatternPiece second) => $"Birleştirilmiş · {Source(first)} + {Source(second)}";
    internal static string Closed(PatternPiece source) => $"Kapatılmış · {Source(source)}";
}
