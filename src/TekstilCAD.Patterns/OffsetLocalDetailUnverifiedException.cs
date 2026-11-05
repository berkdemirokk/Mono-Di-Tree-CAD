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
    /// <summary>Source-only compatibility accessor. Null for a recovered-contour failure even when
    /// Sample and SamplePoint are present. Use ComparisonIndex together with ComparisonSide for display.</summary>
    public int? SourceIndex => ComparisonSide == OffsetComparisonSide.Source ? ComparisonIndex : null;
    /// <summary>Zero-based vertex or edge-start index within the indicated comparison contour.</summary>
    public int? ComparisonIndex { get; }
    /// <summary>Identifies the contour containing ComparisonIndex and SamplePoint; recovered indices
    /// must never be interpreted as indices into the original source or the forward Preview.</summary>
    public OffsetComparisonSide? ComparisonSide { get; }
    /// <summary>Sample kind on ComparisonSide, not necessarily on the source contour.</summary>
    public OffsetLocalSample? Sample { get; }
    /// <summary>Sample position in millimetres on the indicated grid contour.</summary>
    public Point2? SamplePoint { get; }
}
