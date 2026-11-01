namespace TekstilCAD.Core;

/// <summary>A temporary two-point comparison, not a garment or production approval.</summary>
public sealed record PhotoTraceMeasurementResult(double EstimatedMm,double KnownMm,double DifferenceMm);

public static class PhotoTraceMeasurement
{
    /// <summary>Compares a separate image segment to a user-provided physical length.
    /// The same calibration segment cannot be reused as the comparison segment.</summary>
    public static PhotoTraceMeasurementResult Compare(int previewWidth,int previewHeight,int sourceWidth,int sourceHeight,
        Point2 referenceStart,Point2 referenceEnd,double referenceLengthMm,Point2 checkStart,Point2 checkEnd,double knownLengthMm)
    {
        if(!double.IsFinite(knownLengthMm) || knownLengthMm<=0 || knownLengthMm>Geometry.MaxCoordinate)
            throw new ArgumentOutOfRangeException(nameof(knownLengthMm),"Kontrol mesafesi pozitif ve desteklenen aralıkta olmalıdır.");
        if((checkStart==referenceStart && checkEnd==referenceEnd) || (checkStart==referenceEnd && checkEnd==referenceStart))
            throw new ArgumentException("Kontrol için kalibrasyondaki aynı iki ucu kullanmayın.");
        var points=PhotoTraceCalibration.Convert(previewWidth,previewHeight,sourceWidth,sourceHeight,referenceStart,referenceEnd,referenceLengthMm,[checkStart,checkEnd]);
        var estimated=Geometry.Distance(points[0],points[1]);
        return new(estimated,knownLengthMm,estimated-knownLengthMm);
    }
}
