using TekstilCAD.Core;
namespace TekstilCAD.Patterns;

/// <summary>Independent open path extracted in the stored order from a closed contour.</summary>
public sealed record ContourPathExtractionResult(PatternPiece Piece,int StartIndex,int EndIndex,int OmittedMarks,bool CrossesClosure,bool Reversed=false);
public static class ContourPathExtraction
{
    public static ContourPathExtractionResult Extract(PatternPiece source,int start,int end,CancellationToken token=default)
        => Extract(source,start,end,false,token);
    public static ContourPathExtractionResult Extract(PatternPiece source,int start,int end,bool reverse,CancellationToken token=default)
    {
        ArgumentNullException.ThrowIfNull(source);token.ThrowIfCancellationRequested();
        if(source.Id==Guid.Empty || !source.Closed || source.Points is null || source.Points.Count<3 || source.Points.Count>LargeContourValidator.MaximumStoredPoints)
            throw new ArgumentException("En fazla 10000 saklı noktalı kapalı bir kontur seçin.");
        var points=source.Points.ToArray();
        if(start<0 || end<0 || start>=points.Length || end>=points.Length)throw new ArgumentOutOfRangeException(nameof(start),"Nokta indeksi konturda bulunmuyor.");
        var repeated=points.Length>1 && points[0]==points[^1];
        static bool Bits(Point2 a,Point2 b)=>BitConverter.DoubleToInt64Bits(a.X)==BitConverter.DoubleToInt64Bits(b.X) && BitConverter.DoubleToInt64Bits(a.Y)==BitConverter.DoubleToInt64Bits(b.Y);
        if(repeated && !Bits(points[0],points[^1]))throw new ArgumentException("Tekrarlı kapanış noktalarının bitleri farklı; kenar çıkarılmadı.");
        if(source.Notches is not null)_=MarkedPieceTranslation.Translate(source,0,0,token);
        LargeContourValidator.Validate(points,token);
        var logical=points.Length-(repeated ? 1 : 0);
        if(repeated && start==logical)start=0;if(repeated && end==logical)end=0;
        if(start==end)throw new ArgumentException("Başlangıç ve bitiş farklı köşeler olmalıdır.");
        var count=end>start ? end-start+1 : logical-start+end+1;
        var path=new List<Point2>(count);var map=new Dictionary<int,int>(count);
        for(var i=0;i<count;i++) { token.ThrowIfCancellationRequested();var original=(start+i)%logical;map.Add(original,reverse ? count-1-i : i);path.Add(points[original]); }
        if(reverse)path.Reverse();
        LargeOpenPathValidator.Validate(path,token);
        var marks=new List<NotchMark>();
        if(source.Notches is not null)foreach(var mark in source.Notches)
        {
            token.ThrowIfCancellationRequested();
            if(map.TryGetValue(mark.VertexIndex,out var mapped))marks.Add(new NotchMark { VertexIndex=mapped,Position=mark.Position });
        }
        token.ThrowIfCancellationRequested();
        var result=new PatternPiece { Name="Kontur kenarı · "+source.Id.ToString("N"),Closed=false,Points=path,Notches=marks.Count==0 ? null : marks };
        return new(result,start,end,(source.Notches?.Count ?? 0)-marks.Count,end<start,reverse);
    }
    /// <summary>Copy a bit-matched saved rule to exactly the extracted interval.</summary>
    public static GradingRuleSet? ExtractGrading(PatternPiece source,GradingRuleSet rule,ContourPathExtractionResult extraction,CancellationToken token=default)
    {
        ArgumentNullException.ThrowIfNull(source);ArgumentNullException.ThrowIfNull(rule);ArgumentNullException.ThrowIfNull(extraction);token.ThrowIfCancellationRequested();
        var result=extraction.Piece;
        if(!source.Closed || source.Id==Guid.Empty || result is null || result.Closed || result.Id==Guid.Empty || result.Id==source.Id ||
           source.Points is null || result.Points is null || rule.SourcePoints is null || rule.Deltas is null ||
           rule.SourcePieceId!=source.Id || !rule.SourceClosed || source.Points.Count<3 || source.Points.Count>LargeContourValidator.MaximumStoredPoints ||
           rule.SourcePoints.Count!=source.Points.Count || rule.Deltas.Count!=source.Points.Count)return null;
        static bool Bits(Point2 a,Point2 b)=>BitConverter.DoubleToInt64Bits(a.X)==BitConverter.DoubleToInt64Bits(b.X) && BitConverter.DoubleToInt64Bits(a.Y)==BitConverter.DoubleToInt64Bits(b.Y);
        var repeated=source.Points[0]==source.Points[^1];var logical=source.Points.Count-(repeated?1:0);
        if(repeated && (!Bits(source.Points[0],source.Points[^1]) || !Bits(rule.Deltas[0],rule.Deltas[^1])))return null;
        if(extraction.StartIndex<0 || extraction.EndIndex<0 || extraction.StartIndex>=logical || extraction.EndIndex>=logical || extraction.StartIndex==extraction.EndIndex || extraction.CrossesClosure!=(extraction.EndIndex<extraction.StartIndex))return null;
        var count=extraction.EndIndex>extraction.StartIndex ? extraction.EndIndex-extraction.StartIndex+1 : logical-extraction.StartIndex+extraction.EndIndex+1;
        if(result.Points.Count!=count)return null;
        for(var i=0;i<source.Points.Count;i++)
        {
            if((i & 255)==0)token.ThrowIfCancellationRequested();
            if(!source.Points[i].IsFinite || !rule.Deltas[i].IsFinite || !Bits(source.Points[i],rule.SourcePoints[i]))return null;
        }
        var deltas=new List<Point2>(count);
        for(var i=0;i<count;i++)
        {
            if((i & 255)==0)token.ThrowIfCancellationRequested();
            var offset=extraction.Reversed ? count-1-i : i;
            var index=(extraction.StartIndex+offset)%logical;
            if(!Bits(source.Points[index],result.Points[i]))return null;
            deltas.Add(rule.Deltas[index]);
        }
        token.ThrowIfCancellationRequested();
        return new GradingRuleSet { SourcePieceId=result.Id,SourceClosed=false,SourcePoints=[..result.Points],Deltas=deltas };
    }

}
