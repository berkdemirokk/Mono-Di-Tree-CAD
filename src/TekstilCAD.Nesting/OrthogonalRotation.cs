using TekstilCAD.Core;

namespace TekstilCAD.Nesting;

/// <summary>Exact quarter-turns about the world origin, counterclockwise in degrees.</summary>
public static class OrthogonalRotation
{
    public static PatternPiece Rotate(PatternPiece source, int degrees, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(source);
        if (degrees is not (0 or 90 or 180 or 270))
            throw new ArgumentOutOfRangeException(nameof(degrees));
        if (!source.Closed || source.Points is null)
            throw new ArgumentException("A closed contour is required.", nameof(source));
        Geometry.ValidateContour(source.Points, cancellationToken);
        if (source.Grainline is { } line)
        {
            Geometry.Validate(line.Start);
            Geometry.Validate(line.End);
            if (line.Start == line.End) throw new ArgumentException("Grainline must have distinct endpoints.", nameof(source));
        }

        // Swapping and negating binary64 coordinates is exact, including subnormal values.
        static Point2 Turn(Point2 p, int angle) => angle switch
        {
            0 => p,
            90 => new(-p.Y, p.X),
            180 => new(-p.X, -p.Y),
            270 => new(p.Y, -p.X),
            _ => throw new ArgumentOutOfRangeException(nameof(angle))
        };
        var points = new List<Point2>(source.Points.Count);
        foreach (var point in source.Points)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rotated = Turn(point, degrees);
            Geometry.Validate(rotated);
            points.Add(rotated);
        }
        var result = new PatternPiece { Id = source.Id, Name = source.Name, Closed = source.Closed,
            Metadata = source.Metadata?.Clone(), Points = points };
        if (source.Grainline is { } grainline)
        {
            var start = Turn(grainline.Start, degrees);
            var end = Turn(grainline.End, degrees);
            Geometry.Validate(start); Geometry.Validate(end);
            if (start == end) throw new ArgumentException("Rotated grainline is invalid.", nameof(source));
            result.Grainline = new Grainline { Start = start, End = end, NeedsReview = grainline.NeedsReview };
        }
        Geometry.ValidateContour(result.Points, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }
}
