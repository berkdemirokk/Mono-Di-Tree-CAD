using TekstilCAD.Core;

namespace TekstilCAD.FileFormats;

/// <summary>
/// Experimental sampling of a decoded 2D circle. This does not parse DXF or
/// establish entity visibility, plane, layer meaning or manufacturing accuracy.
/// Tolerance is a nominal sampling target, not a proven total error bound.
/// </summary>
public static class DxfCircleApproximation
{
    public static DxfBulgeApproximationResult Approximate(Point2 center, double radius,
        double millimetersPerUnit, double toleranceMm, int remainingPointBudget,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!center.IsFinite) throw new ArgumentOutOfRangeException(nameof(center));
        if (!double.IsFinite(radius) || radius <= 0) throw new ArgumentOutOfRangeException(nameof(radius));
        if (!double.IsFinite(millimetersPerUnit) || millimetersPerUnit <= 0) throw new ArgumentOutOfRangeException(nameof(millimetersPerUnit));
        if (!double.IsFinite(toleranceMm) || toleranceMm <= 0) throw new ArgumentOutOfRangeException(nameof(toleranceMm));
        var right=center.X+radius; var left=center.X-radius;
        var top=center.Y+radius; var bottom=center.Y-radius;
        if (!double.IsFinite(right) || !double.IsFinite(left) || !double.IsFinite(top) || !double.IsFinite(bottom) ||
            right<=center.X || left>=center.X || top<=center.Y || bottom>=center.Y)
            throw new InvalidDataException("Daire yarıçapı kaynak koordinatlarda güvenle gösterilemiyor.");
        // Reject loss of the radius at the cardinal vertices when it consumes
        // the requested error budget. This is a guard, not a complete proof of
        // decimal conversion or floating-point/trigonometric error.
        var radiusError=Math.Max(Math.Max(Math.Abs((right-center.X)-radius),Math.Abs((center.X-left)-radius)),
            Math.Max(Math.Abs((top-center.Y)-radius),Math.Abs((center.Y-bottom)-radius)));
        var errorMm=radiusError*millimetersPerUnit;
        if (!double.IsFinite(errorMm) || errorMm>toleranceMm/2048)
            throw new InvalidDataException("Daire yarıçapı istenen toleransta güvenle gösterilemiyor.");
        var quarter=Math.Tan(Math.PI/8);
        return DxfBulgeApproximation.Approximate(
            [new(new Point2(right,center.Y),quarter), new(new Point2(center.X,top),quarter),
             new(new Point2(left,center.Y),quarter), new(new Point2(center.X,bottom),quarter)],
            true,millimetersPerUnit,toleranceMm,remainingPointBudget,cancellationToken);
    }
}
