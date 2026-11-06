using TekstilCAD.Core;

namespace TekstilCAD.Patterns;

/// <summary>Creates two detached open paths; source and project-level references remain unchanged.</summary>
public static class OpenPathSplit
{
    public static (PatternPiece First,PatternPiece Second) Split(PatternPiece source,int index,CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(source);cancellationToken.ThrowIfCancellationRequested();
        if(source.Id==Guid.Empty || source.Name is null || source.Points is null || source.Closed ||
           source.Points.Count<3 || source.Points.Count>LargeOpenPathValidator.MaximumStoredPoints || index<=0 || index>=source.Points.Count-1)
            throw new ArgumentException("En az üç noktalı açık konturda uç olmayan bir nokta seçin.");
        if(source.Notches is not null)_=MarkedPieceTranslation.Translate(source,0,0,cancellationToken);
        var points=source.Points.ToArray();LargeOpenPathValidator.Validate(points,cancellationToken);
        var first=points.Take(index+1).ToList();var second=points.Skip(index).ToList();
        var firstMarks=new List<NotchMark>();var secondMarks=new List<NotchMark>();
        if(source.Notches is not null)foreach(var mark in source.Notches)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if(mark.VertexIndex<=index)firstMarks.Add(mark.Clone());
            if(mark.VertexIndex>=index)secondMarks.Add(new NotchMark { VertexIndex=mark.VertexIndex-index,Position=mark.Position });
        }
        cancellationToken.ThrowIfCancellationRequested();
        var prefix=source.Id.ToString("N");
        return (new PatternPiece { Name="Açık bölüm A · "+prefix,Closed=false,Points=first,Notches=firstMarks.Count==0 ? null : firstMarks,ExperimentalALine=source.ExperimentalALine },
            new PatternPiece { Name="Açık bölüm B · "+prefix,Closed=false,Points=second,Notches=secondMarks.Count==0 ? null : secondMarks,ExperimentalALine=source.ExperimentalALine });
    }
}
