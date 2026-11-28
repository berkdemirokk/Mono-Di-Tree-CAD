namespace TekstilCAD.Core.Commands;

/// <summary>How the canvas draws the dashed preview frame.</summary>
public enum GhostStyle
{
    /// <summary>Blue dashed frame: what a change will look like.</summary>
    Preview,
    /// <summary>Red dashed frame: what will be removed.</summary>
    Danger,
}

/// <summary>Where a label sits relative to its anchor point.</summary>
public enum GhostLabelPlacement { Above, Below, Left, Right, Center }

/// <summary>One outline of the preview, in millimetres (document axes, Y grows downward).</summary>
/// <param name="Points">The outline in order.</param>
/// <param name="Closed">True when the last point connects back to the first.</param>
public sealed record GhostPolygon(IReadOnlyList<Point2> Points, bool Closed);

/// <summary>A short text next to the frame, for example "120 mm". <see cref="Anchor"/> is in millimetres.</summary>
public sealed record GhostLabel(Point2 Anchor, string Text, GhostLabelPlacement Placement);
public sealed record GhostLine(Point2 Start, Point2 End);

/// <summary>
/// What the canvas draws for a preview: dashed outlines and measure labels, all in millimetres. Pure data; the canvas
/// (a later step) turns it into pixels. A scene is empty when a command changes nothing that can be drawn.
/// </summary>
public sealed record GhostScene(GhostStyle Style, IReadOnlyList<GhostPolygon> Polygons, IReadOnlyList<GhostLabel> Labels)
{
    public IReadOnlyList<GhostLine> Lines { get; init; } = Array.Empty<GhostLine>();
    /// <summary>Nothing to draw.</summary>
    public static GhostScene Empty { get; } = new(GhostStyle.Preview, Array.Empty<GhostPolygon>(), Array.Empty<GhostLabel>());

    /// <summary>True when there is nothing to draw.</summary>
    public bool IsEmpty => Polygons.Count == 0 && Labels.Count == 0 && Lines.Count == 0;

    /// <summary>The bounding box of all outline points, or null when there are none.</summary>
    public Bounds2? Bounds
    {
        get
        {
            var any = false;
            double minX = 0, minY = 0, maxX = 0, maxY = 0;
            foreach (var polygon in Polygons)
            {
                foreach (var p in polygon.Points)
                {
                    if (!any) { minX = maxX = p.X; minY = maxY = p.Y; any = true; continue; }
                    if (p.X < minX) minX = p.X;
                    if (p.X > maxX) maxX = p.X;
                    if (p.Y < minY) minY = p.Y;
                    if (p.Y > maxY) maxY = p.Y;
                }
            }
            foreach(var line in Lines)
            foreach(var p in new[]{line.Start,line.End})
            {
                if(!any){minX=maxX=p.X;minY=maxY=p.Y;any=true;continue;}
                minX=Math.Min(minX,p.X);maxX=Math.Max(maxX,p.X);minY=Math.Min(minY,p.Y);maxY=Math.Max(maxY,p.Y);
            }
            return any ? new Bounds2(minX, minY, maxX, maxY) : null;
        }
    }
}
