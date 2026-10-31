using TekstilCAD.Core;

namespace TekstilCAD.FileFormats;

/// <summary>
/// File-free informational analysis of local turn changes after HPGL rounding.
/// It is not an export validator, approval, feature-preservation certificate,
/// or physical-machine compatibility test. Export does not call this API.
/// </summary>
public static class HpglTurnReview
{
    public const int MaximumSamples = 100;
    /// <summary>SourceVertexIndex is zero-based in the original copied point sequence; signs are -1, 0 or +1.</summary>
    public sealed record Sample(int SourceVertexIndex, int SourceSign, int PlotSign);
    public sealed record Report(Guid PieceId, bool Closed, double UnitsPerMillimeter,
        int ExaminedTurns, int ChangedTurns, int FlattenedTurns, int IntroducedTurns,
        int ReversedTurns, IReadOnlyList<Sample> Samples, bool SamplesTruncated);
    /// <summary>One bounded page in source-index order; NextOffset is null at the end.</summary>
    public sealed record Page(Report Report, int Offset, int? NextOffset);

    /// <summary>
    /// Copies the supplied piece's points before analysis. The caller must prevent
    /// concurrent mutation throughout snapshot capture. A report describes only
    /// that snapshot and the supplied units, not a later export or changed piece.
    /// Dense smooth curves and binary coordinate rounding may produce benign
    /// turn changes. Coordinates and rounding use the same quantizer as export.
    /// </summary>
    public static Report Analyze(PatternPiece piece,double unitsPerMillimeter,int sampleLimit=20,CancellationToken cancellationToken=default)
        => AnalyzeCore(piece,unitsPerMillimeter,0,sampleLimit,cancellationToken,null);

    /// <summary>Returns a bounded page of changed turns. The caller must keep the source
    /// unchanged during capture; offset counts changed turns, not source vertices.</summary>
    public static Page AnalyzePage(PatternPiece piece,double unitsPerMillimeter,int offset=0,int pageSize=20,CancellationToken cancellationToken=default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if(offset<0)throw new ArgumentOutOfRangeException(nameof(offset));
        if(pageSize<1 || pageSize>MaximumSamples)throw new ArgumentOutOfRangeException(nameof(pageSize));
        var report=AnalyzeCore(piece,unitsPerMillimeter,offset,pageSize,cancellationToken,null);
        cancellationToken.ThrowIfCancellationRequested();
        var end=(long)offset+report.Samples.Count;
        int? next=end<report.ChangedTurns ? checked((int)end) : null;
        return new Page(report,offset,next);
    }

    internal readonly record struct IndexedTurn(int SourceVertexIndex,sbyte SourceSign,sbyte PlotSign);

    // Kept private to the prepared output: callers only receive newly allocated,
    // readonly Sample lists. ProjectStorage bounds all source turns to 200,000.
    internal sealed class Index
    {
        private readonly Report summary;
        private readonly IndexedTurn[] turns;
        internal Index(Report summary,IndexedTurn[] turns) { this.summary=summary;this.turns=turns; }
        internal Report Review(int offset,int sampleLimit,CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if(offset<0)throw new ArgumentOutOfRangeException(nameof(offset));
            if(sampleLimit<0 || sampleLimit>MaximumSamples)throw new ArgumentOutOfRangeException(nameof(sampleLimit));
            var count=(int)Math.Min(sampleLimit,Math.Max(0L,(long)turns.Length-offset));
            var samples=new Sample[count];
            for(var i=0;i<count;i++)
            {
                if((i&31)==0)cancellationToken.ThrowIfCancellationRequested();
                var turn=turns[offset+i];samples[i]=new Sample(turn.SourceVertexIndex,turn.SourceSign,turn.PlotSign);
            }
            cancellationToken.ThrowIfCancellationRequested();
            return summary with { Samples=Array.AsReadOnly(samples),SamplesTruncated=summary.ChangedTurns>count };
        }
        internal Page ReviewPage(int offset,int pageSize,CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if(offset<0)throw new ArgumentOutOfRangeException(nameof(offset));
            if(pageSize<1 || pageSize>MaximumSamples)throw new ArgumentOutOfRangeException(nameof(pageSize));
            var report=Review(offset,pageSize,cancellationToken);
            var end=(long)offset+report.Samples.Count;
            int? next=end<report.ChangedTurns ? checked((int)end) : null;
            cancellationToken.ThrowIfCancellationRequested();
            return new Page(report,offset,next);
        }
    }

    internal static (Report Report,Index Index) AnalyzeWithIndex(PatternPiece piece,double unitsPerMillimeter,int sampleLimit,CancellationToken cancellationToken)
    {
        var turns=new List<IndexedTurn>();
        var report=AnalyzeCore(piece,unitsPerMillimeter,0,sampleLimit,cancellationToken,turns);
        cancellationToken.ThrowIfCancellationRequested();
        var index=new Index(report,turns.ToArray());
        cancellationToken.ThrowIfCancellationRequested();
        return (report,index);
    }

    private static Report AnalyzeCore(PatternPiece piece,double unitsPerMillimeter,int sampleOffset,int sampleLimit,CancellationToken cancellationToken,List<IndexedTurn>? index)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(piece);
        if(!double.IsFinite(unitsPerMillimeter) || unitsPerMillimeter<=0)throw new ArgumentOutOfRangeException(nameof(unitsPerMillimeter));
        if(sampleLimit<0 || sampleLimit>MaximumSamples)throw new ArgumentOutOfRangeException(nameof(sampleLimit));
        if(piece.Points is null || piece.Points.Count < (piece.Closed ? 3 : 2) || piece.Points.Count>ProjectStorage.MaximumPointsPerPiece)
            throw new ArgumentException("Parça nokta sayısı inceleme sınırları dışında.",nameof(piece));
        var id=piece.Id;var closed=piece.Closed;var count=piece.Points.Count;
        var snapshot=new Point2[count];var quantized=new HpglFormat.PlotPoint[count];var indices=new List<int>(count);
        for(var i=0;i<count;i++)
        {
            if((i&255)==0)cancellationToken.ThrowIfCancellationRequested();
            var point=piece.Points[i];Geometry.Validate(point);snapshot[i]=point;
            quantized[i]=HpglFormat.Quantize(point,unitsPerMillimeter);
            if(i==0 || point!=snapshot[i-1])indices.Add(i);
        }
        if(closed && indices.Count>1 && snapshot[indices[0]]==snapshot[indices[^1]])indices.RemoveAt(indices.Count-1);
        var samples=new List<Sample>(Math.Min(sampleLimit,indices.Count));
        var changed=0;var flattened=0;var introduced=0;var reversed=0;var examined=0;
        if(indices.Count>=3)
        {
            var exact=HpglFormat.ExactCoordinates(snapshot,cancellationToken);
            var start=closed ? 0 : 1;var end=closed ? indices.Count : indices.Count-1;
            for(var i=start;i<end;i++)
            {
                if((i&255)==0)cancellationToken.ThrowIfCancellationRequested();
                var ai=indices[(i+indices.Count-1)%indices.Count];var bi=indices[i];var ci=indices[(i+1)%indices.Count];
                var a=exact[ai];var b=exact[bi];var c=exact[ci];
                var source=((b.X-a.X)*(c.Y-b.Y)-(b.Y-a.Y)*(c.X-b.X)).Sign;
                var pa=quantized[ai];var pb=quantized[bi];var pc=quantized[ci];
                var cross=((Int128)pb.X-pa.X)*((Int128)pc.Y-pb.Y)-((Int128)pb.Y-pa.Y)*((Int128)pc.X-pb.X);
                var plot=cross>0 ? 1 : cross<0 ? -1 : 0;examined++;
                if(source==plot)continue;
                changed++;
                if(source!=0 && plot==0)flattened++;
                else if(source==0)introduced++;
                else reversed++;
                index?.Add(new IndexedTurn(bi,(sbyte)source,(sbyte)plot));
                if(changed>sampleOffset && samples.Count<sampleLimit)samples.Add(new Sample(bi,source,plot));
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new Report(id,closed,unitsPerMillimeter,examined,changed,flattened,introduced,reversed,samples.AsReadOnly(),changed>samples.Count);
    }
}
