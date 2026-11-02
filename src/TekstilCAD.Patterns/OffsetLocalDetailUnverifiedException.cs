using System.Collections.Immutable;
using TekstilCAD.Core;

namespace TekstilCAD.Patterns;

/// <summary>Sample on a source contour that failed the conservative inverse comparison.</summary>
public enum OffsetLocalSample { Vertex, EdgeMidpoint }

/// <summary>The conservative offset check could not verify preservation of local contour detail.</summary>
public sealed class OffsetLocalDetailUnverifiedException : ArgumentException
{
    public OffsetLocalDetailUnverifiedException()
        : base("Dikiş payında yerel kontur ayrıntısının korunduğu doğrulanamadı; işlem uygulanmadı.") { }

    internal OffsetLocalDetailUnverifiedException(IEnumerable<Point2> preview,int? sourceIndex,OffsetLocalSample? sample,Point2? samplePoint) : this()
    { Preview = preview.ToImmutableArray(); SourceIndex=sourceIndex; Sample=sample; SamplePoint=samplePoint; }

    /// <summary>Unverified display-only geometry, not an approved pattern piece.</summary>
    public ImmutableArray<Point2> Preview { get; } = ImmutableArray<Point2>.Empty;
    /// <summary>Zero-based original source vertex or edge-start index; null when the reverse comparison failed.</summary>
    public int? SourceIndex { get; }
    public OffsetLocalSample? Sample { get; }
    /// <summary>Sample position in millimetres on the quantized source, if the source-to-recovered check failed.</summary>
    public Point2? SamplePoint { get; }
}
