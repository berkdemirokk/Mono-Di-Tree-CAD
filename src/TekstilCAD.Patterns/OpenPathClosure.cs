using TekstilCAD.Core;

namespace TekstilCAD.Patterns;

/// <summary>Detached result; callers must preserve and review project-level references.</summary>
public sealed record OpenPathClosureResult(PatternPiece Piece, Guid SourceId, string SourceName, double ClosingLength);

public static class OpenPathClosure
{
    /// <summary>Closes the original endpoints with a straight edge without moving or removing points.
    /// The source remains unchanged. Metadata and grainline require explicit omitSourceProductionData consent because their meaning
    /// on the new piece is unresolved. Project-level rules and provenance are never copied.</summary>
    public static OpenPathClosureResult Close(PatternPiece source, double tolerance, CancellationToken cancellationToken = default, bool omitSourceProductionData = false)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        if(!double.IsFinite(tolerance) || tolerance<0 || tolerance>100_000)throw new ArgumentOutOfRangeException(nameof(tolerance));
        if(source.Id==Guid.Empty || source.Name is null || source.Name.Length>1024)throw new ArgumentException("Kaynak kimliği veya adı geçersiz.");
        if(source.Closed || source.Points is null || source.Points.Count<3 || source.Points.Count>LargeContourValidator.MaximumStoredPoints)
            throw new ArgumentException("Kapatmak için en az üç noktalı açık bir kontur gerekir.");
        if(source.Notches is not null)
        {
            _=MarkedPieceTranslation.Translate(source,0,0,cancellationToken);
            if(source.Points[0]==source.Points[^1] && source.Notches.Any(mark=>mark.VertexIndex==source.Points.Count-1))
                throw new ArgumentException("Son açık uç işaretli ve ilk uçla aynı konumda; kapalı kontur bu ayrı işareti temsil edemez. Kaynak değiştirilmedi.");
        }
        if(!omitSourceProductionData && (source.Metadata is not null || source.Grainline is not null))
            throw new ArgumentException("Üretim bilgisi veya iplik yönü olan parça otomatik kapatılamaz.");
        var points=source.Points.ToArray();
        foreach(var point in points) { cancellationToken.ThrowIfCancellationRequested(); Geometry.Validate(point); }
        var length=Geometry.Distance(points[0],points[^1]);
        if(!EndpointTolerance.Contains(points[0],points[^1],tolerance))throw new ArgumentException("Uçlar arasındaki mesafe seçilen toleransı aşıyor.");
        LargeContourValidator.Validate(points,cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return new(new PatternPiece { Name=OpenPathResultName.Closed(source),Closed=true,Points=[..points],
            Notches=source.Notches?.Select(mark=>mark.Clone()).ToList(),ExperimentalALine=source.ExperimentalALine },source.Id,source.Name,length);
    }
    /// <summary>Copies a matching open rule to the validated closure when endpoint increments agree exactly.</summary>
    public static GradingRuleSet? CloseGrading(PatternPiece source,GradingRuleSet rule,OpenPathClosureResult closure,CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(source);ArgumentNullException.ThrowIfNull(rule);ArgumentNullException.ThrowIfNull(closure);cancellationToken.ThrowIfCancellationRequested();
        var result=closure.Piece;
        if(source.Points is null || result is null || result.Points is null || rule.SourcePoints is null || rule.Deltas is null ||
           source.Id==Guid.Empty || result.Id==Guid.Empty || result.Id==source.Id || closure.SourceId!=source.Id || rule.SourcePieceId!=source.Id ||
           source.Closed || rule.SourceClosed || !result.Closed || source.Points.Count<3 || source.Points.Count>LargeContourValidator.MaximumStoredPoints ||
           result.Points.Count!=source.Points.Count || rule.SourcePoints.Count!=source.Points.Count || rule.Deltas.Count!=source.Points.Count)return null;
        static bool Bits(Point2 a,Point2 b)=>BitConverter.DoubleToInt64Bits(a.X)==BitConverter.DoubleToInt64Bits(b.X) && BitConverter.DoubleToInt64Bits(a.Y)==BitConverter.DoubleToInt64Bits(b.Y);
        if(!Bits(rule.Deltas[0],rule.Deltas[^1]))return null;
        if(source.Points[0]==source.Points[^1] && !Bits(source.Points[0],source.Points[^1]))return null;
        for(var i=0;i<source.Points.Count;i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if(!source.Points[i].IsFinite || !rule.Deltas[i].IsFinite || !Bits(source.Points[i],rule.SourcePoints[i]) || !Bits(source.Points[i],result.Points[i]))return null;
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new GradingRuleSet { SourcePieceId=result.Id,SourceClosed=true,SourcePoints=[..result.Points],Deltas=[..rule.Deltas] };
    }
}
