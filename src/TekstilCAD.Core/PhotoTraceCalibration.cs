namespace TekstilCAD.Core;

/// <summary>
/// Uniformly scales points marked by a user on a raster into millimetres.
/// This is a manual drafting aid: it does not infer garment geometry, correct
/// perspective, or validate fit, seams or production suitability.
/// </summary>
public static class PhotoTraceCalibration
{
    // Match the reference image admission limits. Coordinate values refer to the
    // displayed, EXIF-oriented image pixel centres, not the original file grid.
    public const int MaximumImageDimension=12_000;
    public const long MaximumImagePixels=24_000_000;
    public const int MaximumTracePoints=10_000;
    public const double MinimumReferencePixels=1.0;

    /// <summary>
    /// Returns a detached, read-only list of open draft coordinates in mm. The
    /// first calibration point is (0,0) and Y increases downwards, as on the CAD
    /// canvas. Subpixel positions are allowed within 0..width-1 and 0..height-1.
    /// The caller must keep tracePoints stable while this method reads it, and
    /// must separately validate topology and obtain production approval.
    /// </summary>
    public static IReadOnlyList<Point2> Convert(int imageWidth,int imageHeight,Point2 referenceStart,Point2 referenceEnd,
        double referenceLengthMm,IReadOnlyList<Point2> tracePoints)
        => Convert(imageWidth,imageHeight,imageWidth,imageHeight,referenceStart,referenceEnd,referenceLengthMm,tracePoints);

    /// <summary>
    /// Converts points on a downsampled, already-oriented preview using the
    /// corresponding already-oriented source dimensions. Pixel-centre differences
    /// are scaled by sourceWidth/previewWidth and sourceHeight/previewHeight
    /// before the common millimetre calibration is applied. This accounts for
    /// integer rounding in the preview dimensions; it does not correct lens or
    /// perspective distortion or validate the result for production.
    /// </summary>
    public static IReadOnlyList<Point2> Convert(int previewWidth,int previewHeight,int sourceWidth,int sourceHeight,
        Point2 referenceStart,Point2 referenceEnd,double referenceLengthMm,IReadOnlyList<Point2> tracePoints)
    {
        ArgumentNullException.ThrowIfNull(tracePoints);
        ValidateDimensions(previewWidth,previewHeight,nameof(previewWidth));
        ValidateDimensions(sourceWidth,sourceHeight,nameof(sourceWidth));
        if(previewWidth>sourceWidth || previewHeight>sourceHeight)
            throw new ArgumentOutOfRangeException(nameof(previewWidth),"Önizleme kaynak boyutundan büyük olamaz.");
        if(!double.IsFinite(referenceLengthMm) || referenceLengthMm<=0 || referenceLengthMm>Geometry.MaxCoordinate)
            throw new ArgumentOutOfRangeException(nameof(referenceLengthMm),"Bilinen mesafe pozitif, sonlu ve desteklenen aralıkta olmalıdır.");
        ValidatePixel(referenceStart,previewWidth,previewHeight,nameof(referenceStart));
        ValidatePixel(referenceEnd,previewWidth,previewHeight,nameof(referenceEnd));
        var dx=referenceEnd.X-referenceStart.X;var dy=referenceEnd.Y-referenceStart.Y;
        var previewDistance=Math.Sqrt(dx*dx+dy*dy);
        if(!double.IsFinite(previewDistance) || previewDistance<MinimumReferencePixels)
            throw new ArgumentException("Kalibrasyon noktaları en az bir piksel aralıklı olmalıdır.",nameof(referenceEnd));
        var xRatio=(double)sourceWidth/previewWidth;var yRatio=(double)sourceHeight/previewHeight;
        var sourceDx=dx*xRatio;var sourceDy=dy*yRatio;
        var referencePixels=Math.Sqrt(sourceDx*sourceDx+sourceDy*sourceDy);
        var scale=referenceLengthMm/referencePixels;
        if(!double.IsFinite(scale) || scale<=0)
            throw new ArgumentOutOfRangeException(nameof(referenceLengthMm),"Kalibrasyon ölçeği hesaplanamıyor.");
        int count=tracePoints.Count;
        if(count<2 || count>MaximumTracePoints)
            throw new ArgumentOutOfRangeException(nameof(tracePoints),"Taslak çizgi 2–10000 nokta içermelidir.");
        var result=new Point2[count];
        for(int i=0;i<count;i++)
        {
            var pixel=tracePoints[i];ValidatePixel(pixel,previewWidth,previewHeight,nameof(tracePoints));
            var world=new Point2((pixel.X-referenceStart.X)*xRatio*scale,(pixel.Y-referenceStart.Y)*yRatio*scale);
            if(!world.IsFinite || Math.Abs(world.X)>Geometry.MaxCoordinate || Math.Abs(world.Y)>Geometry.MaxCoordinate)
                throw new ArgumentOutOfRangeException(nameof(tracePoints),"Ölçeklenen nokta CAD koordinat sınırını aşıyor.");
            if(i>0 && Geometry.Distance(result[i-1],world)<=Geometry.Epsilon)
                throw new ArgumentException("Ardışık taslak noktaları sıfır veya çok kısa bir kenar oluşturuyor.",nameof(tracePoints));
            result[i]=world;
        }
        return Array.AsReadOnly(result);
    }

    private static void ValidateDimensions(int width,int height,string parameter)
    {
        if(width<1 || height<1 || width>MaximumImageDimension || height>MaximumImageDimension || (long)width*height>MaximumImagePixels)
            throw new ArgumentOutOfRangeException(parameter,"Görsel boyutları desteklenen sınırların dışında.");
    }

    private static void ValidatePixel(Point2 point,int width,int height,string parameter)
    {
        if(!point.IsFinite || point.X<0 || point.Y<0 || point.X>width-1 || point.Y>height-1)
            throw new ArgumentOutOfRangeException(parameter,"Piksel noktası görsel sınırları içinde ve sonlu olmalıdır.");
    }
}
