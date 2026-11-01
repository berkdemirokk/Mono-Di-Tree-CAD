using System.Collections.Immutable;
using TekstilCAD.Core;

namespace TekstilCAD.Patterns;

/// <summary>The conservative offset check could not verify preservation of local contour detail.</summary>
public sealed class OffsetLocalDetailUnverifiedException : ArgumentException
{
    public OffsetLocalDetailUnverifiedException()
        : base("Dikiş payında yerel kontur ayrıntısının korunduğu doğrulanamadı; işlem uygulanmadı.") { }

    internal OffsetLocalDetailUnverifiedException(IEnumerable<Point2> preview) : this()
        => Preview = preview.ToImmutableArray();

    /// <summary>Unverified display-only geometry, not an approved pattern piece.</summary>
    public ImmutableArray<Point2> Preview { get; } = ImmutableArray<Point2>.Empty;
}
