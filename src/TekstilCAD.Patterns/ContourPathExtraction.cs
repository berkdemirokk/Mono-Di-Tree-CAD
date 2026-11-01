using TekstilCAD.Core;
namespace TekstilCAD.Patterns;

/// <summary>Independent open path extracted in the stored order from a closed contour.</summary>
public sealed record ContourPathExtractionResult(PatternPiece Piece,int StartIndex,int EndIndex,int OmittedMarks,bool CrossesClosure);
public static class ContourPathExtraction
{
    public static ContourPathExtractionResult Extract(PatternPiece source,int start,int end,CancellationToken token=default)
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
        for(var i=0;i<count;i++) { token.ThrowIfCancellationRequested();var original=(start+i)%logical;map.Add(original,i);path.Add(points[original]); }
        LargeOpenPathValidator.Validate(path,token);
        var marks=new List<NotchMark>();
        if(source.Notches is not null)foreach(var mark in source.Notches)
        {
            token.ThrowIfCancellationRequested();
            if(map.TryGetValue(mark.VertexIndex,out var mapped))marks.Add(new NotchMark { VertexIndex=mapped,Position=mark.Position });
        }
        token.ThrowIfCancellationRequested();
        var result=new PatternPiece { Name="Kontur kenarı · "+source.Id.ToString("N"),Closed=false,Points=path,Notches=marks.Count==0 ? null : marks };
        return new(result,start,end,(source.Notches?.Count ?? 0)-marks.Count,end<start);
    }
}
