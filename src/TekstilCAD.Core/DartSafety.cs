namespace TekstilCAD.Core;

public static class DartSafety
{
    public static void RequireNoDarts(PatternPiece piece)
    {
        ArgumentNullException.ThrowIfNull(piece);
        if (piece.Darts is not null)
            throw new ArgumentException($"\"{piece.Name}\" pens işaretleri içeriyor; bu kontur işlemi pens kollarını koruyamaz.", nameof(piece));
    }
}
