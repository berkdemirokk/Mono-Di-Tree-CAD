using System.Collections.Immutable;
using TekstilCAD.Core;

namespace TekstilCAD.FileFormats;

/// <summary>
/// A checked, two-dimensional affine transform. This type does not validate DXF
/// extrusion, elevation, entities, units, or whether transformed contours survive
/// rounding; callers must validate those separately before accepting geometry.
/// Coordinates are in source units until ApplyMillimeters is called. The CAD
/// coordinate limit is expressed in millimeters, not arbitrary DXF units.
/// </summary>
internal readonly struct DxfInsertTransform
{
    private readonly double a, b, c, d, x, y, originX, originY;
    private readonly bool initialized;

    private DxfInsertTransform(double a, double b, double c, double d, double x, double y, double originX, double originY)
    {
        if (!double.IsFinite(a) || !double.IsFinite(b) || !double.IsFinite(c) ||
            !double.IsFinite(d) || !double.IsFinite(x) || !double.IsFinite(y) ||
            !double.IsFinite(originX) || !double.IsFinite(originY))
            throw new ArgumentOutOfRangeException(nameof(a), "Transform coefficients must be finite.");

        // Normalization avoids overflow while checking whether the represented
        // matrix is singular. A numerically collapsed matrix is rejected.
        double magnitude = Math.Max(Math.Max(Math.Abs(a), Math.Abs(b)), Math.Max(Math.Abs(c), Math.Abs(d)));
        if (magnitude == 0 || (a / magnitude * (d / magnitude) - b / magnitude * (c / magnitude)) == 0)
            throw new ArgumentOutOfRangeException(nameof(a), "Transform matrix is singular or numerically collapsed.");
        this.a = a; this.b = b; this.c = c; this.d = d; this.x = x; this.y = y;
        this.originX = originX; this.originY = originY;
        initialized = true;
    }

    /// <summary>Creates T(insertion) * R(counterclockwise degrees) * S * T(-blockBase).</summary>
    public static DxfInsertTransform Create(Point2 blockBase, Point2 insertion, double scaleX, double scaleY, double rotationDegrees)
    {
        ValidateFinite(blockBase);
        ValidateFinite(insertion);
        if (!double.IsFinite(scaleX) || scaleX == 0) throw new ArgumentOutOfRangeException(nameof(scaleX));
        if (!double.IsFinite(scaleY) || scaleY == 0) throw new ArgumentOutOfRangeException(nameof(scaleY));
        if (!double.IsFinite(rotationDegrees)) throw new ArgumentOutOfRangeException(nameof(rotationDegrees));

        double angle = rotationDegrees % 360;
        // Cardinal rotations must not inject artificial residual coordinates.
        (double sin, double cos) = angle switch
        {
            0 => (0, 1), 90 or -270 => (1, 0),
            180 or -180 => (0, -1), 270 or -90 => (-1, 0),
            _ => Math.SinCos(angle * (Math.PI / 180))
        };
        double a = cos * scaleX, b = -sin * scaleY, c = sin * scaleX, d = cos * scaleY;
        return new(a, b, c, d, insertion.X, insertion.Y, blockBase.X, blockBase.Y);
    }

    /// <summary>Applies this transform in source units, rejecting non-finite values.</summary>
    public Point2 Apply(Point2 point)
    {
        if (!initialized) throw new InvalidOperationException("An uninitialized transform is invalid.");
        ValidateFinite(point);
        // Subtract the block origin before multiplication. Folding it into the
        // translation loses small insertions next to large source origins.
        double dx = point.X - originX, dy = point.Y - originY;
        Point2 result = new(a * dx + b * dy + x, c * dx + d * dy + y);
        ValidateFinite(result);
        return result;
    }

    /// <summary>Transforms source coordinates, converts to millimeters and enforces the CAD domain.</summary>
    public Point2 ApplyMillimeters(Point2 point, double millimetersPerUnit)
    {
        if (!double.IsFinite(millimetersPerUnit) || millimetersPerUnit <= 0)
            throw new ArgumentOutOfRangeException(nameof(millimetersPerUnit));
        var transformed = Apply(point);
        Point2 result = new(transformed.X * millimetersPerUnit, transformed.Y * millimetersPerUnit);
        Geometry.Validate(result);
        return result;
    }

    private static void ValidateFinite(Point2 point)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
            throw new ArgumentOutOfRangeException(nameof(point), "Coordinates must be finite.");
    }

    /// <summary>Composes parent after this transform; floating-point arithmetic still rounds.</summary>
    public DxfInsertTransform Then(DxfInsertTransform parent)
    {
        if (!initialized) throw new InvalidOperationException("An uninitialized transform is invalid.");
        var anchor = parent.Apply(new Point2(x, y));
        return new(parent.a * a + parent.b * c, parent.a * b + parent.b * d,
            parent.c * a + parent.d * c, parent.c * b + parent.d * d,
            anchor.X, anchor.Y, originX, originY);
    }

    /// <summary>True when the represented matrix reverses orientation.</summary>
    public bool IsReflection
    {
        get
        {
            double magnitude = Math.Max(Math.Max(Math.Abs(a), Math.Abs(b)), Math.Max(Math.Abs(c), Math.Abs(d)));
            if (magnitude == 0) throw new InvalidOperationException("An uninitialized transform is invalid.");
            return a / magnitude * (d / magnitude) - b / magnitude * (c / magnitude) < 0;
        }
    }
}

/// <summary>A source occurrence in an INSERT path. Occurrence numbers are assigned by the source reader.</summary>
internal readonly record struct DxfInsertOccurrence(string BlockName, int Occurrence);

/// <summary>Identity for one source vertex within one particular nested INSERT instance.</summary>
internal sealed class DxfInsertProvenance : IEquatable<DxfInsertProvenance>
{
    public string SourceSnapshotHash { get; }
    public ImmutableArray<DxfInsertOccurrence> InsertPath { get; }
    public int EntityOccurrence { get; }
    public int VertexOrdinal { get; }

    public DxfInsertProvenance(string sourceSnapshotHash, IEnumerable<DxfInsertOccurrence> insertPath, int entityOccurrence, int vertexOrdinal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceSnapshotHash);
        ArgumentNullException.ThrowIfNull(insertPath);
        if (entityOccurrence < 0) throw new ArgumentOutOfRangeException(nameof(entityOccurrence));
        if (vertexOrdinal < 0) throw new ArgumentOutOfRangeException(nameof(vertexOrdinal));
        var path = ImmutableArray.CreateBuilder<DxfInsertOccurrence>();
        foreach (var occurrence in insertPath)
        {
            if (path.Count == 64) throw new ArgumentOutOfRangeException(nameof(insertPath), "INSERT path exceeds 64 occurrences.");
            if (string.IsNullOrWhiteSpace(occurrence.BlockName) || occurrence.Occurrence < 0)
                throw new ArgumentException("Every path occurrence needs a block name and a nonnegative ordinal.", nameof(insertPath));
            path.Add(occurrence);
        }
        if (path.Count == 0) throw new ArgumentException("An INSERT path cannot be empty.", nameof(insertPath));
        SourceSnapshotHash = sourceSnapshotHash;
        InsertPath = path.ToImmutable();
        EntityOccurrence = entityOccurrence;
        VertexOrdinal = vertexOrdinal;
    }

    public bool Equals(DxfInsertProvenance? other) => other is not null &&
        StringComparer.Ordinal.Equals(SourceSnapshotHash, other.SourceSnapshotHash) &&
        EntityOccurrence == other.EntityOccurrence && VertexOrdinal == other.VertexOrdinal &&
        InsertPath.AsSpan().SequenceEqual(other.InsertPath.AsSpan());

    public override bool Equals(object? obj) => obj is DxfInsertProvenance other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(SourceSnapshotHash, StringComparer.Ordinal);
        foreach (var occurrence in InsertPath) hash.Add(occurrence);
        hash.Add(EntityOccurrence);
        hash.Add(VertexOrdinal);
        return hash.ToHashCode();
    }
}
