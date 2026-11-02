namespace TekstilCAD.Core;

/// <summary>A temporary two-point comparison, not a garment or production approval.</summary>
public sealed record PhotoTraceMeasurementResult(double EstimatedMm,double KnownMm,double DifferenceMm);

public static class PhotoTraceMeasurement
{
    /// <summary>Measures the supplied ordered draft polyline, not the unknown continuous garment curve.</summary>
    public static PhotoTraceMeasurementResult ComparePolyline(int previewWidth,int previewHeight,int sourceWidth,int sourceHeight,
        Point2 referenceStart,Point2 referenceEnd,double referenceLengthMm,IReadOnlyList<Point2> trace,double knownLengthMm)
    {
        ValidateKnown(knownLengthMm);ArgumentNullException.ThrowIfNull(trace);
        if(trace.Count==2 && ((trace[0]==referenceStart && trace[1]==referenceEnd) || (trace[1]==referenceStart && trace[0]==referenceEnd)))
            throw new ArgumentException("Kontrol için kalibrasyondaki aynı iki ucu kullanmayın.");
        return CompareConverted(PhotoTraceCalibration.Convert(previewWidth,previewHeight,sourceWidth,sourceHeight,referenceStart,referenceEnd,referenceLengthMm,trace),knownLengthMm);
    }
    public static PhotoTraceMeasurementResult ComparePlanePolyline(int previewWidth,int previewHeight,PhotoPlaneCalibration plane,IReadOnlyList<Point2> trace,double knownLengthMm)
    {
        ValidateKnown(knownLengthMm);ArgumentNullException.ThrowIfNull(plane);ArgumentNullException.ThrowIfNull(trace);
        if(trace.Count==2 && plane.Corners.Contains(trace[0]) && plane.Corners.Contains(trace[1]))
            throw new ArgumentException("Kontrol için iki referans köşesini birlikte kullanmayın.");
        return CompareConverted(PhotoPlaneRectification.Convert(previewWidth,previewHeight,plane.Corners,plane.WidthMm,plane.HeightMm,trace),knownLengthMm);
    }
    private static void ValidateKnown(double known)
    {
        if(!double.IsFinite(known) || known<=0 || known>Geometry.MaxCoordinate)
            throw new ArgumentOutOfRangeException(nameof(known),"Kontrol mesafesi pozitif ve desteklenen aralıkta olmalıdır.");
    }
    private static PhotoTraceMeasurementResult CompareConverted(IReadOnlyList<Point2> points,double known)
    {
        double length=0;
        for(var i=1;i<points.Count;i++)length+=Geometry.Distance(points[i-1],points[i]);
        if(!double.IsFinite(length) || length<=0 || length>Geometry.MaxCoordinate)
            throw new ArgumentOutOfRangeException(nameof(points),"Çizgi uzunluğu desteklenen aralıkta değil.");
        return new(length,known,length-known);
    }
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
