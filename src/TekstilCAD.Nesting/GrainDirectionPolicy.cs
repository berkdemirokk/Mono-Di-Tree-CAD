using TekstilCAD.Core;

namespace TekstilCAD.Nesting;

/// <summary>Opt-in validation of a placed piece's grainline against fabric length (+Y).
/// This does not run automatically during nesting or certify a production layout.</summary>
public static class GrainDirectionPolicy
{
    public enum Direction { OneWay, TwoWay }

    /// <summary>Reject missing, unreviewed, invalid or misaligned grainlines. In one-way
    /// mode Start-to-End points towards +Y; in two-way mode either Y direction is accepted.
    /// Tolerance is in degrees, from 0 to 90 inclusive. Computed angles exactly at a
    /// non-axis tolerance boundary are rejected conservatively because rounding can
    /// make a slightly excessive angle appear equal to the boundary.</summary>
    public static void Validate(PatternPiece piece, Direction direction, double toleranceDegrees)
    {
        ArgumentNullException.ThrowIfNull(piece);
        ValidateParameters(direction, toleranceDegrees);
        if (piece.Id == Guid.Empty)
            throw new ArgumentException("Kalıp kimliği eksik.", nameof(piece));
        var line = piece.Grainline;
        if (line is null)
            throw NestingPieceError.Create(piece.Id, "İplik yönü eksik.", nameof(piece));
        if (line.NeedsReview)
            throw NestingPieceError.Create(piece.Id, "İplik yönü kontrol bekliyor.", nameof(piece));
        ValidateLine(piece, line, direction, toleranceDegrees);
    }

    internal static void ValidateParameters(Direction direction, double toleranceDegrees)
    {
        if (direction != Direction.OneWay && direction != Direction.TwoWay)
            throw new ArgumentOutOfRangeException(nameof(direction));
        if (!double.IsFinite(toleranceDegrees) || toleranceDegrees < 0 || toleranceDegrees > 90)
            throw new ArgumentOutOfRangeException(nameof(toleranceDegrees));
    }

    private static void ValidateLine(PatternPiece piece, Grainline line, Direction direction, double toleranceDegrees)
    {
        const double limit = 100_000_000;
        if (!line.Start.IsFinite || !line.End.IsFinite ||
            Math.Abs(line.Start.X) > limit || Math.Abs(line.Start.Y) > limit ||
            Math.Abs(line.End.X) > limit || Math.Abs(line.End.Y) > limit || line.Start == line.End)
            throw NestingPieceError.Create(piece.Id, "İplik yönü geçersiz.", nameof(piece));

        double dx = line.End.X - line.Start.X, dy = line.End.Y - line.Start.Y;
        if (direction == Direction.TwoWay) dy = Math.Abs(dy);
        // Handle exact axes explicitly, including both inclusive tolerance endpoints.
        bool accepted = dx == 0 && dy > 0 ||
            dy == 0 && toleranceDegrees == 90 ||
            dx != 0 && toleranceDegrees > 0 &&
            Math.Atan2(Math.Abs(dx), dy) * (180 / Math.PI) < toleranceDegrees - 1e-12;
        if (!accepted)
            throw NestingPieceError.Create(piece.Id, "İplik yönü kumaş boy yönüyle uyumlu değil.", nameof(piece));
    }
}

