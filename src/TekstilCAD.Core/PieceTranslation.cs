namespace TekstilCAD.Core;

/// <summary>Detached translation using Geometry.Translate and preserving vertex markers.
/// This does not establish manufacturing accuracy or guarantee every small feature survives.
/// Desktop property-field translation uses this helper; nesting has separate rules.</summary>
public static class PieceTranslation
{
    /// <remarks>Input must remain unchanged during snapshot creation. Both original and
    /// translated coordinates use the same representation checks as Geometry.Translate.
    /// Each marker is anchored to the corresponding translated vertex. These checks
    /// do not prove preservation of every small local detail.</remarks>
    public static PatternPiece Translate(PatternPiece source,double dx,double dy,CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(source);cancellationToken.ThrowIfCancellationRequested();
        if(!double.IsFinite(dx) || !double.IsFinite(dy))throw new ArgumentOutOfRangeException(nameof(dx));
        if(source.Points is null || source.Points.Count<2 || source.Points.Count>LargeOpenPathValidator.MaximumStoredPoints)
            throw new ArgumentException("Kaynak kontur veya nokta sayısı geçersiz.");
        var points=source.Points.ToArray();
        var marks=source.Notches;
        if(marks is not null)
        {
            if(marks.Count<1 || marks.Count>points.Length)throw new ArgumentException("Çentik listesi geçersiz.");
            var indices=new HashSet<int>();
            foreach(var mark in marks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if(mark is null || mark.VertexIndex<0 || mark.VertexIndex>=points.Length ||
                   (source.Closed && points[0]==points[^1] && mark.VertexIndex==points.Length-1) ||
                   !indices.Add(mark.VertexIndex) || mark.Position!=points[mark.VertexIndex])
                    throw new ArgumentException("Çentik kaynağın geçerli bir köşesine bağlı değil.");
            }
        }
        var moved=Geometry.Translate(points,dx,dy);
        cancellationToken.ThrowIfCancellationRequested();
        Grainline? grain=null;
        if(source.Grainline is { } line)
        {
            Geometry.Validate(line.Start);Geometry.Validate(line.End);
            if(line.Start==line.End)throw new ArgumentException("İplik yönü geçersiz.");
            var translated=Geometry.Translate([points[0],line.Start,line.End],dx,dy);
            if(translated[0]!=moved[0] || translated[1]==translated[2])throw new ArgumentException("İplik yönü konturla aynı konuma güvenle taşınamadı.");
            grain=new Grainline { Start=translated[1],End=translated[2],NeedsReview=line.NeedsReview };
        }
        var result=source.Clone();result.Points=moved;result.Grainline=grain;
        if(marks is not null)result.Notches=marks.Select(mark=>new NotchMark { VertexIndex=mark.VertexIndex,Position=moved[mark.VertexIndex] }).ToList();
        cancellationToken.ThrowIfCancellationRequested();return result;
    }
}
