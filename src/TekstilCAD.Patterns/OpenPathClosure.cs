using TekstilCAD.Core;

namespace TekstilCAD.Patterns;

/// <summary>Detached result; callers must preserve and review project-level references.</summary>
public sealed record OpenPathClosureResult(PatternPiece Piece, Guid SourceId, string SourceName, double ClosingLength);

public static class OpenPathClosure
{
    /// <summary>Closes the original endpoints with a straight edge without moving or removing points.
    /// The source remains unchanged. Metadata and grainline are rejected because their meaning
    /// on the new piece is unresolved. Project-level rules and provenance are never copied.</summary>
    public static OpenPathClosureResult Close(PatternPiece source, double tolerance, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        NotchSafety.RequireNoNotches(source);
        if(!double.IsFinite(tolerance) || tolerance<0 || tolerance>100_000)throw new ArgumentOutOfRangeException(nameof(tolerance));
        if(source.Id==Guid.Empty || source.Name is null || source.Name.Length>1024)throw new ArgumentException("Kaynak kimliği veya adı geçersiz.");
        if(source.Closed || source.Points is null || source.Points.Count<3 || source.Points.Count>LargeContourValidator.MaximumStoredPoints)
            throw new ArgumentException("Kapatmak için en az üç noktalı açık bir kontur gerekir.");
        if(source.Metadata is not null || source.Grainline is not null)
            throw new ArgumentException("Üretim bilgisi veya iplik yönü olan parça otomatik kapatılamaz.");
        var points=source.Points.ToArray();
        foreach(var point in points) { cancellationToken.ThrowIfCancellationRequested(); Geometry.Validate(point); }
        var length=Geometry.Distance(points[0],points[^1]);
        if(length>tolerance)throw new ArgumentException("Uçlar arasındaki mesafe seçilen toleransı aşıyor.");
        LargeContourValidator.Validate(points,cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return new(new PatternPiece { Name="Kapatılmış kontur",Closed=true,Points=[..points] },source.Id,source.Name,length);
    }
}
