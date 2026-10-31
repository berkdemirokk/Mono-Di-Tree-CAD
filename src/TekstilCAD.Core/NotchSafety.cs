namespace TekstilCAD.Core;

/// <summary>Reject operations that cannot yet retain vertex-anchored notch marks.</summary>
public static class NotchSafety
{
    public static void RequireNoNotches(PatternPiece piece)
    {
        ArgumentNullException.ThrowIfNull(piece);
        if(piece.Notches is not null)
            throw new ArgumentException("Çentik işaretli parçada bu işlem henüz desteklenmiyor; işaretlerin kaybolmasını önlemek için işlem uygulanmadı.",nameof(piece));
    }
}
