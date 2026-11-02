namespace TekstilCAD.Core;

/// <summary>
/// Experimental planar projective rectification from four explicitly identified
/// corners of a physical rectangle. It does not detect corners, correct lens
/// distortion, flatten draped fabric or establish production suitability.
/// </summary>
public static class PhotoPlaneRectification
{
    private const int MaxPoints=512;
    private const double MinimumCornerSine=1e-6;
    private const double PivotTolerance=1e-12;
    private const double BoundaryTolerance=1e-8;

    /// <summary>
    /// Maps preview pixel centres inside the four-sided reference to millimetres.
    /// Corners correspond in order to (0,0), (width,0), (width,height), (0,height).
    /// Either winding is allowed; a reflected selection yields reflected results.
    /// Callers must keep lists stable while this method copies them. No project
    /// provenance format or desktop workflow is defined by this experiment.
    /// </summary>
    public static IReadOnlyList<Point2> Convert(int previewWidth,int previewHeight,IReadOnlyList<Point2> corners,
        double widthMm,double heightMm,IReadOnlyList<Point2> trace,CancellationToken token=default)
        =>ConvertVersion1(previewWidth,previewHeight,corners,widthMm,heightMm,trace,token);

    // Frozen implementation for versioned project history; future algorithms
    // must use a new historical version rather than changing stored results.
    internal static IReadOnlyList<Point2> ConvertVersion1(int previewWidth,int previewHeight,IReadOnlyList<Point2> corners,
        double widthMm,double heightMm,IReadOnlyList<Point2> trace,CancellationToken token=default)
    {
        ArgumentNullException.ThrowIfNull(corners);ArgumentNullException.ThrowIfNull(trace);token.ThrowIfCancellationRequested();
        if(previewWidth<2 || previewHeight<2 || previewWidth>PhotoTraceCalibration.MaximumImageDimension ||
           previewHeight>PhotoTraceCalibration.MaximumImageDimension || (long)previewWidth*previewHeight>PhotoTraceCalibration.MaximumImagePixels)
            throw new ArgumentOutOfRangeException(nameof(previewWidth),"Önizleme boyutu desteklenmiyor.");
        if(corners.Count!=4)throw new ArgumentException("Referansın dört köşesi sırayla seçilmelidir.",nameof(corners));
        if(trace.Count<2 || trace.Count>MaxPoints)throw new ArgumentOutOfRangeException(nameof(trace),"Taslak 2–512 nokta içermelidir.");
        if(!double.IsFinite(widthMm) || !double.IsFinite(heightMm) || widthMm<=0 || heightMm<=0 || widthMm>Geometry.MaxCoordinate || heightMm>Geometry.MaxCoordinate)
            throw new ArgumentOutOfRangeException(nameof(widthMm),"Referans en ve boyu pozitif ve desteklenen aralıkta olmalıdır.");
        var q=new Point2[4];var input=new Point2[trace.Count];
        for(int i=0;i<4;i++) { token.ThrowIfCancellationRequested();q[i]=corners[i];ValidatePixel(q[i],previewWidth,previewHeight); }
        for(int i=0;i<input.Length;i++) { token.ThrowIfCancellationRequested();input[i]=trace[i];ValidatePixel(input[i],previewWidth,previewHeight); }
        var orientation=0;
        for(int i=0;i<4;i++)
        {
            var a=q[i];var b=q[(i+1)%4];var c=q[(i+2)%4];
            var ux=b.X-a.X;var uy=b.Y-a.Y;var vx=c.X-b.X;var vy=c.Y-b.Y;
            var first=Math.Sqrt(ux*ux+uy*uy);var second=Math.Sqrt(vx*vx+vy*vy);var cross=ux*vy-uy*vx;
            if(first<1 || second<1 || Math.Abs(cross)<=MinimumCornerSine*first*second)
                throw new ArgumentException("Referans köşeleri çok yakın veya neredeyse doğrusal.",nameof(corners));
            var sign=Math.Sign(cross);if(orientation!=0 && sign!=orientation)throw new ArgumentException("Referans köşeleri sırayla dışbükey bir dörtgen oluşturmalı.",nameof(corners));
            orientation=sign;
        }
        // Normalizing image coordinates reduces sensitivity to the pixel origin/size.
        double scale=Math.Max(previewWidth,previewHeight);
        var target=new[] { new Point2(0,0),new Point2(1,0),new Point2(1,1),new Point2(0,1) };
        var matrix=new double[8,9];
        for(int i=0;i<4;i++)
        {
            var x=q[i].X/scale;var y=q[i].Y/scale;var u=target[i].X;var v=target[i].Y;int row=i*2;
            matrix[row,0]=x;matrix[row,1]=y;matrix[row,2]=1;matrix[row,6]=-u*x;matrix[row,7]=-u*y;matrix[row,8]=u;
            matrix[row+1,3]=x;matrix[row+1,4]=y;matrix[row+1,5]=1;matrix[row+1,6]=-v*x;matrix[row+1,7]=-v*y;matrix[row+1,8]=v;
        }
        var h=Solve(matrix,token);
        for(int i=0;i<4;i++)
        {
            token.ThrowIfCancellationRequested();double x=q[i].X/scale,y=q[i].Y/scale,den=h[6]*x+h[7]*y+1;
            if(!double.IsFinite(den) || Math.Abs(den)<=PivotTolerance*(Math.Abs(h[6]*x)+Math.Abs(h[7]*y)+1))
                throw new ArgumentException("Referans köşesinde perspektif dönüşümü kararsız.",nameof(corners));
            var u=(h[0]*x+h[1]*y+h[2])/den;var v=(h[3]*x+h[4]*y+h[5])/den;
            if(!double.IsFinite(u) || !double.IsFinite(v) || Math.Abs(u-target[i].X)>BoundaryTolerance || Math.Abs(v-target[i].Y)>BoundaryTolerance)
                throw new ArgumentException("Referans köşeleri güvenle geri hesaplanamadı.",nameof(corners));
        }
        var result=new Point2[input.Length];
        for(int i=0;i<input.Length;i++)
        {
            token.ThrowIfCancellationRequested();var p=input[i];
            for(int j=0;j<4;j++)
            {
                var a=q[j];var b=q[(j+1)%4];var dx=b.X-a.X;var dy=b.Y-a.Y;
                var side=orientation*(dx*(p.Y-a.Y)-dy*(p.X-a.X));
                if(side < -BoundaryTolerance*Math.Max(1,Math.Sqrt(dx*dx+dy*dy)))
                    throw new ArgumentException("Taslak noktası referans dörtgeninin dışında.",nameof(trace));
            }
            double x=p.X/scale,y=p.Y/scale,den=h[6]*x+h[7]*y+1;
            if(!double.IsFinite(den) || Math.Abs(den)<=PivotTolerance*(Math.Abs(h[6]*x)+Math.Abs(h[7]*y)+1))
                throw new ArgumentException("Perspektif dönüşümü bu noktada kararsız.",nameof(trace));
            double u=(h[0]*x+h[1]*y+h[2])/den,v=(h[3]*x+h[4]*y+h[5])/den;
            if(!double.IsFinite(u) || !double.IsFinite(v) || u < -BoundaryTolerance || u > 1+BoundaryTolerance || v < -BoundaryTolerance || v > 1+BoundaryTolerance)
                throw new ArgumentException("Perspektif dönüşümü referans sınırına güvenle eşlenemedi.",nameof(trace));
            var point=new Point2(Math.Clamp(u,0,1)*widthMm,Math.Clamp(v,0,1)*heightMm);Geometry.Validate(point);
            if(i>0 && Geometry.Distance(result[i-1],point)<=Geometry.Epsilon)
                throw new ArgumentException("Dönüştürülen ardışık noktalar çok yakın.",nameof(trace));
            result[i]=point;
        }
        return Array.AsReadOnly(result);
    }
    private static void ValidatePixel(Point2 p,int width,int height)
    {
        if(!p.IsFinite || p.X<0 || p.Y<0 || p.X>width-1 || p.Y>height-1)
            throw new ArgumentOutOfRangeException(nameof(p),"Piksel noktası görüntü içinde ve sonlu olmalıdır.");
    }
    private static double[] Solve(double[,] matrix,CancellationToken token)
    {
        for(int col=0;col<8;col++)
        {
            token.ThrowIfCancellationRequested();int pivot=col;
            for(int row=col+1;row<8;row++)if(Math.Abs(matrix[row,col])>Math.Abs(matrix[pivot,col]))pivot=row;
            if(!double.IsFinite(matrix[pivot,col]) || Math.Abs(matrix[pivot,col])<PivotTolerance)
                throw new ArgumentException("Referans dörtgeninin perspektifi güvenle hesaplanamıyor.");
            if(pivot!=col)for(int j=col;j<=8;j++)(matrix[col,j],matrix[pivot,j])=(matrix[pivot,j],matrix[col,j]);
            var div=matrix[col,col];for(int j=col;j<=8;j++)matrix[col,j]/=div;
            for(int row=0;row<8;row++)if(row!=col)
            {
                var factor=matrix[row,col];for(int j=col;j<=8;j++)matrix[row,j]-=factor*matrix[col,j];
            }
        }
        var result=new double[8];for(int i=0;i<8;i++) { result[i]=matrix[i,8];if(!double.IsFinite(result[i]))throw new ArgumentException("Perspektif katsayıları geçersiz."); }
        return result;
    }
}
