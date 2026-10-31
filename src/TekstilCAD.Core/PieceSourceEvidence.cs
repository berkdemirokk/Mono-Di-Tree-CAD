namespace TekstilCAD.Core;

/// <summary>
/// Experimental standalone evidence value. It is intentionally not attached to
/// PatternPiece: import, persistence and every geometry and output boundary
/// require integration before it can be used to authorize any workflow.
/// A review is an acknowledgement of a particular geometry, not a guarantee.
/// </summary>
public sealed class PieceSourceEvidence
{
    public string SourceSha256 { get; }
    public string SourceEntity { get; }
    public double MillimetersPerUnit { get; }
    public string Method { get; }
    public double? NominalToleranceMm { get; }
    public string? ReviewedGeometrySha256 { get; }

    public PieceSourceEvidence(string sourceSha256, string sourceEntity, double millimetersPerUnit,
        string method, double? nominalToleranceMm = null, string? reviewedGeometrySha256 = null)
    {
        SourceSha256 = ValidateHash(sourceSha256, nameof(sourceSha256));
        SourceEntity = ValidateText(sourceEntity, nameof(sourceEntity));
        Method = ValidateText(method, nameof(method));
        if (!double.IsFinite(millimetersPerUnit) || millimetersPerUnit <= 0)
            throw new ArgumentOutOfRangeException(nameof(millimetersPerUnit));
        if (nominalToleranceMm is double tolerance && (!double.IsFinite(tolerance) || tolerance <= 0))
            throw new ArgumentOutOfRangeException(nameof(nominalToleranceMm));
        MillimetersPerUnit = millimetersPerUnit;
        NominalToleranceMm = nominalToleranceMm;
        ReviewedGeometrySha256 = reviewedGeometrySha256 is null ? null : ValidateHash(reviewedGeometrySha256, nameof(reviewedGeometrySha256));
    }

    /// <summary>Conservatively remove review after a geometry-affecting operation.</summary>
    public PieceSourceEvidence InvalidateReview() => new(SourceSha256, SourceEntity, MillimetersPerUnit, Method, NominalToleranceMm);

    /// <summary>Returns false when geometry is invalid or differs from the reviewed geometry.</summary>
    public bool IsReviewCurrentFor(PatternPiece piece) => ReviewedGeometrySha256 is not null &&
        TryComputeGeometrySha256(piece, out var hash) && string.Equals(ReviewedGeometrySha256, hash, StringComparison.Ordinal);

    /// <summary>Explicitly discard a stale review without mutating this value or the piece.</summary>
    public PieceSourceEvidence InvalidateIfChanged(PatternPiece piece) =>
        ReviewedGeometrySha256 is not null && !IsReviewCurrentFor(piece) ? InvalidateReview() : this;

    /// <summary>Versioned digest of ordered finite coordinates and closure; not a production certification.
    /// The caller must prevent concurrent mutation of the piece while computing the digest.</summary>
    public static bool TryComputeGeometrySha256(PatternPiece piece, out string? hash)
    {
        ArgumentNullException.ThrowIfNull(piece);
        hash = null;
        if (piece.Points is null || piece.Points.Any(p => !p.IsFinite)) return false;
        using var sha = System.Security.Cryptography.SHA256.Create();
        using var stream = new System.Security.Cryptography.CryptoStream(Stream.Null, sha, System.Security.Cryptography.CryptoStreamMode.Write);
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        writer.Write("TekstilCAD.piece-geometry.v1");
        writer.Write(piece.Closed);
        writer.Write(piece.Points.Count);
        foreach (var p in piece.Points)
        {
            // Normalize signed zero; all other finite IEEE-754 bits are preserved.
            writer.Write(BitConverter.DoubleToInt64Bits(p.X == 0 ? 0 : p.X));
            writer.Write(BitConverter.DoubleToInt64Bits(p.Y == 0 ? 0 : p.Y));
        }
        writer.Flush();
        stream.FlushFinalBlock();
        hash = Convert.ToHexString(sha.Hash!);
        return true;
    }

    private static string ValidateHash(string value, string name)
    {
        if (value is null || value.Length != 64 || value.Any(c => !Uri.IsHexDigit(c)))
            throw new ArgumentException("SHA-256 must contain exactly 64 hexadecimal characters.", name);
        return value.ToUpperInvariant();
    }

    private static string ValidateText(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 256 || value.Any(char.IsControl))
            throw new ArgumentException("Evidence text is empty or exceeds its bounds.", name);
        return value;
    }
}
