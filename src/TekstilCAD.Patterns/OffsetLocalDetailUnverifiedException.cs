using System.Collections.Immutable;
using TekstilCAD.Core;

namespace TekstilCAD.Patterns;

/// <summary>Kind of sample that failed the conservative inverse comparison.</summary>
public enum OffsetLocalSample { Vertex, EdgeMidpoint }
/// <summary>The contour from which the first unmatched sample was taken.</summary>
public enum OffsetComparisonSide { Source, Recovered }

/// <summary>The conservative offset check could not verify preservation of local contour detail.</summary>
public sealed class OffsetLocalDetailUnverifiedException : ArgumentException
{
    public OffsetLocalDetailUnverifiedException()
        : base("Dikiş payında yerel kontur ayrıntısının korunduğu doğrulanamadı; işlem uygulanmadı.") { }

    internal OffsetLocalDetailUnverifiedException(IEnumerable<Point2> preview,int index,OffsetLocalSample sample,Point2 samplePoint,OffsetComparisonSide side) : this()
    { Preview = preview.ToImmutableArray(); ComparisonIndex=index; Sample=sample; SamplePoint=samplePoint; ComparisonSide=side; }

    /// <summary>Unverified display-only geometry, not an approved pattern piece.</summary>
    public ImmutableArray<Point2> Preview { get; } = ImmutableArray<Point2>.Empty;
    /// <summary>Zero-based original source vertex or edge-start index; null when the reverse comparison failed.</summary>
    public int? SourceIndex => ComparisonSide == OffsetComparisonSide.Source ? ComparisonIndex : null;
    /// <summary>Zero-based vertex or edge-start index within the indicated comparison contour.</summary>
    public int? ComparisonIndex { get; }
    public OffsetComparisonSide? ComparisonSide { get; }
    public OffsetLocalSample? Sample { get; }
    /// <summary>Sample position in millimetres on the indicated grid contour.</summary>
    public Point2? SamplePoint { get; }
}
