using TekstilCAD.Core;

namespace TekstilCAD.Tests;

public class PieceSourceEvidenceTests
{
    [Fact]
    public void ReviewChecksArePureAndInvalidationIsExplicitAcrossClones()
    {
        var piece = new PatternPiece { Points = [new(0, 0), new(1, 2)], Closed = true };
        Assert.True(PieceSourceEvidence.TryComputeGeometrySha256(piece, out var hash));
        var evidence = new PieceSourceEvidence(new string('a', 64), "entity:42", 1, "sampled", reviewedGeometrySha256: hash);
        var clone = piece.Clone();
        Assert.True(evidence.IsReviewCurrentFor(clone));
        clone.Points[0] = new(2, 0);
        Assert.False(evidence.IsReviewCurrentFor(clone));
        var invalidated = evidence.InvalidateIfChanged(clone);
        Assert.Null(invalidated.ReviewedGeometrySha256);
        Assert.Equal(hash, evidence.ReviewedGeometrySha256);
        Assert.True(evidence.IsReviewCurrentFor(piece));
        clone.Points[0] = new(0, 0);
        Assert.Null(invalidated.InvalidateIfChanged(clone).ReviewedGeometrySha256);
        piece.Closed = false;
        Assert.Null(evidence.InvalidateIfChanged(piece).ReviewedGeometrySha256);
    }

    [Fact]
    public void DigestIsStableOrderedAndRejectsNonfiniteGeometry()
    {
        var a = new PatternPiece { Points = [new(-0.0, 1), new(2, 3)] };
        var b = new PatternPiece { Points = [new(0.0, 1), new(2, 3)] };
        Assert.True(PieceSourceEvidence.TryComputeGeometrySha256(a, out var first));
        Assert.True(PieceSourceEvidence.TryComputeGeometrySha256(b, out var second));
        Assert.Equal(first, second);
        b.Points.Reverse();
        Assert.True(PieceSourceEvidence.TryComputeGeometrySha256(b, out var reversed));
        Assert.NotEqual(first, reversed);
        b.Points[0] = new(double.NaN, 0);
        Assert.False(PieceSourceEvidence.TryComputeGeometrySha256(b, out var invalid));
        Assert.Null(invalid);
    }

    [Fact]
    public void EvidenceIsNotAttachedToOrSerializedWithMetadataBearingPieces()
    {
        var piece = new PatternPiece { Metadata = new PieceMetadata { Role = "front" } };
        Assert.Null(typeof(PatternPiece).GetProperty("SourceEvidence"));
        Assert.DoesNotContain("SourceEvidence", System.Text.Json.JsonSerializer.Serialize(piece));
        Assert.Equal("front", piece.Clone().Metadata!.Role);
    }

    [Fact]
    public void GeometryChangeDropsReviewButKeepsSourceEvidence()
    {
        var source = new string('a', 64);
        var evidence = new PieceSourceEvidence(source, "entity:42", 25.4, "sampled-polyline", null, new string('b', 64));
        var changed = evidence.InvalidateReview();
        Assert.Null(changed.ReviewedGeometrySha256);
        Assert.Equal(source.ToUpperInvariant(), changed.SourceSha256);
        Assert.Equal("entity:42", changed.SourceEntity);
        Assert.Equal(25.4, changed.MillimetersPerUnit);
        Assert.NotNull(evidence.ReviewedGeometrySha256);
    }

    [Fact]
    public void InvalidEvidenceIsRejected()
    {
        var hash = new string('a', 64);
        Assert.Throws<ArgumentException>(() => new PieceSourceEvidence("bad", "1", 1, "sampled"));
        Assert.Throws<ArgumentException>(() => new PieceSourceEvidence(hash, "1", 1, "sampled", reviewedGeometrySha256: "bad"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PieceSourceEvidence(hash, "1", double.NaN, "sampled"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PieceSourceEvidence(hash, "1", 1, "sampled", 0));
    }
}
