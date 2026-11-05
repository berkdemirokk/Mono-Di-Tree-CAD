using System.Security.Cryptography;
using TekstilCAD.Core;

namespace DartClosureGeometry;

// Diagnostic for a complete, straight-segment closed source contour. It never
// upgrades a numerical construction to an accepted contour or sewing pattern.
public static class ContourDiagnostic
{
    public sealed record Result(string Status, IReadOnlyList<string> Issues)
    {
        public bool ProductionAllowed => false;
        public bool ExportAllowed => false;
    }

    // Stable identity of the actual binary64 vertex sequence, not provenance.
    public static string Identity(IReadOnlyList<Point2> vertices)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        using var bytes = new MemoryStream();
        using (var writer = new BinaryWriter(bytes, System.Text.Encoding.UTF8, true))
        {
            writer.Write(vertices.Count);
            foreach (var p in vertices) { writer.Write(BitConverter.DoubleToInt64Bits(p.X)); writer.Write(BitConverter.DoubleToInt64Bits(p.Y)); }
        }
        return Convert.ToHexString(SHA256.HashData(bytes.ToArray()));
    }

    // Sequence must begin center,A,tip,B,side; one flag per source vertex.
    // True vertices rotate around tip with center. False vertices remain fixed.
    public static Result Inspect(IReadOnlyList<Point2> source, string expectedIdentity, IReadOnlyList<bool> moving)
    {
        var issues = new List<string>();
        Result Block(string reason) => new("blocked", Array.AsReadOnly(new[] { reason }));
        if (source is null || moving is null || source.Count < 5 || source.Count > 512 || moving.Count != source.Count)
            return Block("Missing, oversized, or incomplete closed source contour/partition.");
        if (string.IsNullOrWhiteSpace(expectedIdentity) || !StringComparer.OrdinalIgnoreCase.Equals(Identity(source), expectedIdentity))
            return Block("Source contour identity mismatch.");
        if (!moving[0] || !moving[1] || moving[2] || moving[3] || moving[4])
            return Block("Unexpected dart roles or rotation partition.");
        try { Geometry.ValidateContour(source); }
        catch (ArgumentException) { return Block("Source contour invalid or self-intersecting."); }
        var center = source[0]; var a = source[1]; var tip = source[2]; var b = source[3];
        DartAnalysis dart;
        try { dart = DartClosure.Analyze(center, a, tip, b, source[4], 0); }
        catch (ArgumentException) { return Block("Dart source geometry invalid."); }
        double al = Geometry.Distance(tip, a), bl = Geometry.Distance(tip, b);
        double ux = (a.X-tip.X)/al, uy = (a.Y-tip.Y)/al, vx = (b.X-tip.X)/bl, vy = (b.Y-tip.Y)/bl;
        double c = ux*vx+uy*vy, s = ux*vy-uy*vx, norm = Math.Sqrt(c*c+s*s); c/=norm; s/=norm;
        Point2 Map(Point2 p) => new(tip.X+c*(p.X-tip.X)-s*(p.Y-tip.Y), tip.Y+s*(p.X-tip.X)+c*(p.Y-tip.Y));
        var mapped = source.Select((p,i) => moving[i] ? Map(p) : p).ToArray();
        // Other edges may not stretch across a rotation boundary. Dart legs are
        // removed from the candidate, and no gap is bridged or snapped.
        var edges = new List<(int index, Point2 from, Point2 to)>();
        for (int i=0; i<source.Count; i++)
        {
            if (i is 1 or 2) continue;
            int j=(i+1)%source.Count;
            if (moving[i] != moving[j]) issues.Add($"Edge {i} crosses an unspecified moving/fixed boundary.");
            edges.Add((i,mapped[i],mapped[j]));
        }
        // Pairwise checking includes outer edges and every supplied other edge.
        for (int i=0; i<edges.Count; i++) for (int j=i+1; j<edges.Count; j++)
        {
            var x=edges[i]; var y=edges[j];
            bool adjacent=(x.index+1)%source.Count==y.index || (y.index+1)%source.Count==x.index;
            if (adjacent)
            {
                Point2 first, common, last;
                if ((x.index+1)%source.Count==y.index) { first=x.from; common=x.to; last=y.to; }
                else { first=y.from; common=y.to; last=x.to; }
                if (Geometry.HasSelfIntersections(new[] { first,common,last },false)) issues.Add($"Adjacent edges {x.index} and {y.index} overlap or degenerate.");
            }
            else if (Geometry.SegmentsIntersect(x.from,x.to,y.from,y.to))
                issues.Add($"Edges {x.index} and {y.index} intersect or overlap.");
        }
        if (!dart.ExactBinarySquaredLegsEqual || dart.EndpointGap != 0)
            issues.Add("Dart endpoints are not proven coincident; no bridging performed.");
        // Floating-point rotation is not certified, and the split/closure itself
        // is unspecified. Even a clean line-segment check is not acceptance.
        issues.Add("Rotation error and complete physical closure are not certified.");
        return new("blocked", issues.AsReadOnly());
    }
}
