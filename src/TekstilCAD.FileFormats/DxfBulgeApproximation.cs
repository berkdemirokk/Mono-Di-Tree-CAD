using System.Collections.Immutable;
using TekstilCAD.Core;

namespace TekstilCAD.FileFormats;

/// <summary>A represented source-unit vertex and the bulge of the segment starting there.</summary>
public readonly record struct DxfBulgeVertex(Point2 Point, double Bulge);
/// <summary>Generated stored polyline in mm. Tolerance is a nominal sampling target, not a production certificate.</summary>
public sealed record DxfBulgeApproximationResult(ImmutableArray<Point2> Points, int AddedVertices, double ToleranceMm);

/// <summary>
/// Experimental 2D bulge tessellation of already decoded binary64 coordinates.
/// The caller validates DXF entity semantics, decimal conversion, OCS and any
/// downstream transforms. A floating-point resolution guard rejects some unsafe
/// cases; it is not a rigorous bound on all floating-point/trigonometric error.
/// The caller must keep the input stable during the call.
/// </summary>
public static class DxfBulgeApproximation
{
    public static DxfBulgeApproximationResult Approximate(IReadOnlyList<DxfBulgeVertex> vertices, bool closed,
        double millimetersPerUnit, double toleranceMm, int remainingPointBudget, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        if (!double.IsFinite(millimetersPerUnit) || millimetersPerUnit <= 0) throw new ArgumentOutOfRangeException(nameof(millimetersPerUnit));
        if (!double.IsFinite(toleranceMm) || toleranceMm <= 0) throw new ArgumentOutOfRangeException(nameof(toleranceMm));
        if (remainingPointBudget < 1 || remainingPointBudget > ProjectStorage.MaximumTotalPoints) throw new ArgumentOutOfRangeException(nameof(remainingPointBudget));
        if (vertices.Count < 2 || vertices.Count > ProjectStorage.MaximumPointsPerPiece || vertices.Count > remainingPointBudget)
            throw new InvalidDataException("Bulge kontur nokta sınırını aşıyor veya yeterli nokta içermiyor.");
        cancellationToken.ThrowIfCancellationRequested();
        var mm = new Point2[vertices.Count];
        var bulges = new double[vertices.Count];
        for (int i=0;i<vertices.Count;i++)
        {
            if ((i & 255)==0) cancellationToken.ThrowIfCancellationRequested();
            var v=vertices[i];
            if (!v.Point.IsFinite || !double.IsFinite(v.Bulge)) throw new InvalidDataException("Bulge veya kaynak koordinatı sonlu değil.");
            var scaled=new Point2(v.Point.X*millimetersPerUnit,v.Point.Y*millimetersPerUnit);
            try { Geometry.Validate(scaled); }
            catch (ArgumentOutOfRangeException ex) { throw new InvalidDataException("Ölçeklenen koordinat çalışma sınırında değil.",ex); }
            if ((v.Point.X!=0 && scaled.X==0) || (v.Point.Y!=0 && scaled.Y==0))
                throw new InvalidDataException("Ölçekleme sırasında sıfır olmayan koordinat kayboldu.");
            mm[i]=scaled;bulges[i]=v.Bulge;
        }
        if (!closed && bulges[^1] != 0) throw new InvalidDataException("Açık konturun son köşesinde kullanılmayan bulge var.");
        int budget=Math.Min(ProjectStorage.MaximumPointsPerPiece,remainingPointBudget);
        var output=new List<Point2>(Math.Min(budget,vertices.Count));output.Add(mm[0]);
        int segments=closed ? vertices.Count : vertices.Count-1;
        for (int i=0;i<segments;i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int next=(i+1)%vertices.Count;var a=mm[i];var b=mm[next];var bulge=bulges[i];
            if (vertices[i].Point!=vertices[next].Point && a==b)
                throw new InvalidDataException("Ölçekleme farklı uçları aynı noktaya düşürdü.");
            if (bulge!=0)
            {
                if (a==b) throw new InvalidDataException("Bulge yayının uçları aynı noktada olamaz.");
                AppendArc(output,a,b,bulge,toleranceMm,budget,cancellationToken);
            }
            // The implicit final closing vertex is never appended a second time.
            if (next!=0)
            {
                if(output.Count>=budget)throw new InvalidDataException("Örneklenen kontur nokta bütçesini aşıyor.");
                output.Add(b);
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new(output.ToImmutableArray(),output.Count-vertices.Count,toleranceMm);
    }

    private static void AppendArc(List<Point2> output, Point2 a, Point2 b, double bulge, double tolerance, int budget, CancellationToken token)
    {
        double dx=b.X-a.X,dy=b.Y-a.Y;
        double chord=Geometry.Distance(a,b),abs=Math.Abs(bulge);
        double reciprocal=1/abs;
        double radius=chord*(abs+reciprocal)/4;
        double angle=4*Math.Atan(abs);
        double factor=(reciprocal-bulge*bulge/abs)/4; // (1-b^2)/(4b), sign applied below
        if (bulge<0) factor=-factor;
        double cx=(a.X/2+b.X/2)-dy*factor, cy=(a.Y/2+b.Y/2)+dx*factor;
        if (!double.IsFinite(radius) || radius<=0 || !double.IsFinite(angle) || angle<=0 || angle>=Math.Tau ||
            !double.IsFinite(cx) || !double.IsFinite(cy)) throw new InvalidDataException("Bulge yayı sayısal olarak güvenle hesaplanamıyor.");
        // Reject when binary64 spacing at the large intermediate values consumes
        // a substantial fraction of the requested tolerance. This is a guard,
        // not a proof of total mathematical or trigonometric error.
        double spacing=Math.Max(Math.Max(Ulp(cx),Ulp(cy)),Math.Max(Ulp(radius),Math.Max(Ulp(a.X),Ulp(a.Y))));
        if (!double.IsFinite(spacing) || spacing>tolerance/1024)
            throw new InvalidDataException("Bulge yayı istenen toleransta sayısal olarak ayırt edilemiyor.");
        double ratio=Math.Min(1, (tolerance/2)/(2*radius));
        if (ratio<=0 || !double.IsFinite(ratio)) throw new InvalidDataException("Bulge örnekleme hassasiyeti yetersiz.");
        double step=4*Math.Asin(Math.Sqrt(ratio));
        if (!double.IsFinite(step) || step<=0) throw new InvalidDataException("Bulge örnekleme aralığı hesaplanamadı.");
        double required=Math.Max(Math.Ceiling(angle/step),Math.Ceiling(angle/Math.PI));
        if (!double.IsFinite(required) || required>budget || required<1) throw new InvalidDataException("Bulge örnekleme nokta sınırını aşıyor.");
        int count=(int)required;
        if ((long)output.Count+count-1>budget) throw new InvalidDataException("Bulge örnekleme nokta bütçesini aşıyor.");
        double startX=a.X-cx,startY=a.Y-cy, signedAngle=Math.CopySign(angle,bulge);
        for(int j=1;j<count;j++)
        {
            if ((j&255)==0) token.ThrowIfCancellationRequested();
            double t=signedAngle*j/count, cos=Math.Cos(t),sin=Math.Sin(t);
            var p=new Point2(cx+startX*cos-startY*sin,cy+startX*sin+startY*cos);
            try { Geometry.Validate(p); } catch (ArgumentOutOfRangeException ex) { throw new InvalidDataException("Bulge örneği çalışma sınırında değil.",ex); }
            if(p==output[^1] || p==b) throw new InvalidDataException("Bulge örnekleri sayısal olarak çakıştı.");
            output.Add(p);
        }
    }
    private static double Ulp(double value)
    {
        var magnitude=Math.Abs(value);
        return Math.BitIncrement(magnitude)-magnitude;
    }
}
