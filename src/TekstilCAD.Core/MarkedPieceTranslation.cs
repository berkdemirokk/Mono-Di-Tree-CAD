namespace TekstilCAD.Core;

/// <summary>Creates a detached translation and retains visual marks at their exact contour vertices.</summary>
public static class MarkedPieceTranslation
{
    public static (List<Point2> Points, List<NotchMark>? Notches) Translate(PatternPiece source, double dx, double dy, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        if (source.Points is null || source.Points.Count == 0) throw new ArgumentException("Parça noktaları eksik.", nameof(source));
        var marks = source.Notches;
        if (marks is not null)
        {
            if (marks.Count == 0) throw new ArgumentException("Boş işaret listesi geçersiz.", nameof(source));
            var indices = new HashSet<int>();
            foreach (var mark in marks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (mark is null || mark.VertexIndex < 0 || mark.VertexIndex >= source.Points.Count ||
                    (source.Closed && source.Points.Count > 1 && source.Points[0] == source.Points[^1] && mark.VertexIndex == source.Points.Count - 1) ||
                    !indices.Add(mark.VertexIndex) || mark.Position != source.Points[mark.VertexIndex])
                    throw new ArgumentException("İşaret kaynak konturla eşleşmiyor; taşıma uygulanmadı.", nameof(source));
            }
        }
        var translated = Geometry.Translate(source.Points, dx, dy);
        for (var i = 0; i < translated.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((dx != 0 && translated[i].X == source.Points[i].X) || (dy != 0 && translated[i].Y == source.Points[i].Y))
                throw new ArgumentException("Taşıma miktarı bir noktada sayısal hassasiyetle temsil edilemiyor.", nameof(source));
        }
        cancellationToken.ThrowIfCancellationRequested();
        var translatedMarks = marks?.Select(mark => new NotchMark { VertexIndex = mark.VertexIndex, Position = translated[mark.VertexIndex] }).ToList();
        cancellationToken.ThrowIfCancellationRequested();
        return (translated, translatedMarks);
    }
}
