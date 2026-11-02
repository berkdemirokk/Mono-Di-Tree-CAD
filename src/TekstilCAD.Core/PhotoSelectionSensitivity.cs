namespace TekstilCAD.Core;

/// <summary>
/// A conditional triangle-inequality envelope for one-distance scaling.
/// Floating-point results are approximate, not certified interval arithmetic.
/// It assumes the entered reference length is exact and every endpoint's X and
/// Y selection errors are bounded independently by the supplied preview pixels.
/// It does not cover perspective, lens, drape, physical measurement or image errors.
/// </summary>
public sealed record PhotoSelectionSensitivityResult(double LowerMm,double? UpperMm);

public static class PhotoSelectionSensitivity
{
    public static PhotoSelectionSensitivityResult Estimate(int previewWidth,int previewHeight,int sourceWidth,int sourceHeight,
        Point2 referenceStart,Point2 referenceEnd,double referenceLengthMm,Point2 checkStart,Point2 checkEnd,double perAxisPixels)
    {
        if(!double.IsFinite(perAxisPixels) || perAxisPixels<0 || perAxisPixels>1000)
            throw new ArgumentOutOfRangeException(nameof(perAxisPixels),"Piksel sapması 0–1000 aralığında olmalıdır.");
        // Reuse the production conversion's coordinate, reference and dimension validation.
        _=PhotoTraceCalibration.Convert(previewWidth,previewHeight,sourceWidth,sourceHeight,referenceStart,referenceEnd,referenceLengthMm,[checkStart,checkEnd]);
        var xRatio=(double)sourceWidth/previewWidth;var yRatio=(double)sourceHeight/previewHeight;
        static double Distance(Point2 a,Point2 b,double x,double y)
        { var dx=(b.X-a.X)*x;var dy=(b.Y-a.Y)*y;return Math.Sqrt(dx*dx+dy*dy); }
        var reference=Distance(referenceStart,referenceEnd,xRatio,yRatio);
        var measured=Distance(checkStart,checkEnd,xRatio,yRatio);
        // Each endpoint may move by (±r, ±r); their difference can move by
        // (±2r, ±2r) before the anisotropic preview-to-source conversion.
        var delta=2*perAxisPixels*Math.Sqrt(xRatio*xRatio+yRatio*yRatio);
        var lower=referenceLengthMm*(Math.Max(0,measured-delta)/(reference+delta));
        double? upper=reference>delta ? referenceLengthMm*((measured+delta)/(reference-delta)) : null;
        if(!double.IsFinite(lower) || (upper is double value && !double.IsFinite(value)))
            throw new ArgumentException("Bu değerlerle duyarlılık aralığı hesaplanamıyor.");
        return new(lower,upper);
    }
}
