namespace TekstilCAD.Core;

/// <summary>
/// Experimental evidence value. This type is deliberately not yet attached to
/// PatternPiece: import, persistence and every geometry/output boundary must be
/// integrated together before it can be used to authorize any workflow.
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
