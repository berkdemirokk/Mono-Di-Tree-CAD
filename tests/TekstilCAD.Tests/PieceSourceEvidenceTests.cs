using TekstilCAD.Core;

namespace TekstilCAD.Tests;

public class PieceSourceEvidenceTests
{
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
