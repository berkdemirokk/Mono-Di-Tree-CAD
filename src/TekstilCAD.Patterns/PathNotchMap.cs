using TekstilCAD.Core;
namespace TekstilCAD.Patterns;

internal static class PathNotchMap
{
    internal static int[] Snapshot(PatternPiece piece,IReadOnlyList<Point2> points,CancellationToken token)
    {
        if(piece.Notches is not { } marks)return [];
        if(marks.Count<1 || marks.Count>points.Count || marks.Count>LargeOpenPathValidator.MaximumStoredPoints)
            throw new ArgumentException("İşaret listesi geçersiz.");
        var indices=new int[marks.Count];var seen=new HashSet<int>();
        for(var i=0;i<marks.Count;i++)
        {
            token.ThrowIfCancellationRequested();var mark=marks[i];
            if(mark is null || mark.VertexIndex<0 || mark.VertexIndex>=points.Count || !seen.Add(mark.VertexIndex) || mark.Position!=points[mark.VertexIndex])
                throw new ArgumentException("İşaret indeksi veya kaynak köşe konumu geçersiz.");
            indices[i]=mark.VertexIndex;
        }
        return indices;
    }
    internal static List<NotchMark>? Build(IEnumerable<int> indices,IReadOnlyList<Point2> points,CancellationToken token)
    {
        var seen=new HashSet<int>();var result=new List<NotchMark>();
        foreach(var index in indices)
        {
            token.ThrowIfCancellationRequested();
            if(!seen.Add(index))throw new ArgumentException("İki işaret aynı sonuç köşesine denk geliyor; işlem uygulanmadı.");
            result.Add(new NotchMark { VertexIndex=index,Position=points[index] });
        }
        return result.Count==0 ? null : result;
    }
}
