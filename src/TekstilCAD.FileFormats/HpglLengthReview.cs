namespace TekstilCAD.FileFormats;

/// <summary>Approximate geometric comparison of one captured piece and its encoded HPGL path.</summary>
public static class HpglLengthReview
{
    /// <param name="WorstStartIndex">Zero-based source vertex at the beginning of the largest absolute edge-length difference.</param>
    /// <param name="WorstEndIndex">Zero-based source vertex at the end of that edge, including the closing edge when applicable.</param>
    /// <param name="WorstDisplacementIndex">Zero-based index of the first vertex with maximum Euclidean source-to-encoded displacement.</param>
    public sealed record Result(int EdgeCount,double SourceLengthMm,double EncodedLengthMm,
        int WorstStartIndex,int WorstEndIndex,double WorstSignedDifferenceMm,
        int WorstDisplacementIndex,double WorstDisplacementXmm,double WorstDisplacementYmm,double WorstDisplacementMm);

    /// <summary>
    /// Reads only captured coordinates already used for a prepared output. The source
    /// includes any simulation scale. Zero-length repeated vertices are skipped.
    /// Results use binary floating point and do not validate physical plotter output,
    /// total feature preservation, topology, or production suitability.
    /// </summary>
    public static Result Analyze(HpglPieceGeometryPreview geometry,CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(geometry);cancellationToken.ThrowIfCancellationRequested();
        var points=geometry.Points;var limit=geometry.Closed ? points.Count : points.Count-1;
        double sourceSum=0,sourceCorrection=0,outputSum=0,outputCorrection=0,worst=0;var count=0;var worstStart=-1;var worstEnd=-1;
        static void Add(ref double sum,ref double correction,double value)
        { var adjusted=value-correction;var next=sum+adjusted;correction=(next-sum)-adjusted;sum=next; }
        var worstVertex=-1;double worstX=0,worstY=0,worstDistance=0;
        for(var i=0;i<points.Count;i++)
        {
            if((i&255)==0)cancellationToken.ThrowIfCancellationRequested();
            var p=points[i];var dx=p.PlotX/geometry.UnitsPerMillimeter-p.SourceX;var dy=p.PlotY/geometry.UnitsPerMillimeter-p.SourceY;
            var distance=Math.Sqrt(dx*dx+dy*dy);
            if(!double.IsFinite(distance))throw new InvalidOperationException("HPGL nokta kayması hesaplanamadı.");
            if(worstVertex<0 || distance>worstDistance) { worstVertex=i;worstX=dx;worstY=dy;worstDistance=distance; }
        }
        for(int i=0;i<limit;i++)
        {
            if((i&255)==0)cancellationToken.ThrowIfCancellationRequested();
            var j=(i+1)%points.Count;var a=points[i];var b=points[j];
            var sx=b.SourceX-a.SourceX;var sy=b.SourceY-a.SourceY;
            var px=((long)b.PlotX-a.PlotX)/geometry.UnitsPerMillimeter;var py=((long)b.PlotY-a.PlotY)/geometry.UnitsPerMillimeter;
            var source=Math.Sqrt(sx*sx+sy*sy);var encoded=Math.Sqrt(px*px+py*py);
            if(!double.IsFinite(source) || !double.IsFinite(encoded))throw new InvalidOperationException("HPGL uzunluk karşılaştırması hesaplanamadı.");
            if(source==0 && encoded==0)continue;
            Add(ref sourceSum,ref sourceCorrection,source);Add(ref outputSum,ref outputCorrection,encoded);count++;
            var difference=encoded-source;
            if(worstStart<0 || Math.Abs(difference)>Math.Abs(worst)) { worst=difference;worstStart=i;worstEnd=j; }
        }
        cancellationToken.ThrowIfCancellationRequested();
        if(count==0 || !double.IsFinite(sourceSum) || !double.IsFinite(outputSum))throw new InvalidOperationException("Karşılaştırılacak HPGL kenarı yok.");
        return new(count,sourceSum,outputSum,worstStart,worstEnd,worst,worstVertex,worstX,worstY,worstDistance);
    }
}
