namespace TekstilCAD.FileFormats;

/// <summary>Bounded informational check of distinct paths in captured integer output coordinates.</summary>
public static class HpglPieceInteraction
{
    public enum State { Clear, Found, Incomplete }
    public enum Finding { Boundary, SecondInsideFirst, FirstInsideSecond }
    public sealed record Result(State Status,Guid? FirstPiece,Guid? SecondPiece,long Operations,
        Finding? Kind=null,int? FirstEdge=null,int? SecondEdge=null);
    private readonly record struct P(int X,int Y);
    private readonly record struct Bounds(int MinX,int MinY,int MaxX,int MaxY);
    private sealed record Shape(Guid Id,bool Closed,P[] Points,Bounds Box);

    /// <remarks>Counts pair checks, segment pairs and containment edges against one budget. Touches, overlap,
    /// crossing and containment are all reported alike; none implies a production error or approval.
    /// Only the captured quantized geometry is examined. The caller must keep the input list stable.</remarks>
    public static Result Analyze(IReadOnlyList<HpglPieceGeometryPreview> pieces,long maxOperations=1_000_000,CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(pieces);
        if(maxOperations<0 || maxOperations>1_000_000)throw new ArgumentOutOfRangeException(nameof(maxOperations));
        cancellationToken.ThrowIfCancellationRequested();
        var shapes=new List<Shape>(pieces.Count);
        foreach(var piece in pieces)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var points=piece.Points.Select(p=>new P(p.PlotX,p.PlotY)).ToArray();
            var box=new Bounds(points.Min(p=>p.X),points.Min(p=>p.Y),points.Max(p=>p.X),points.Max(p=>p.Y));
            shapes.Add(new Shape(piece.PieceId,piece.Closed,points,box));
        }
        long ops=0;
        bool Step() { cancellationToken.ThrowIfCancellationRequested();if(ops>=maxOperations)return false;ops++;return true; }
        Result Incomplete()=>new(State.Incomplete,null,null,ops);
        for(int i=0;i<shapes.Count;i++)for(int j=i+1;j<shapes.Count;j++)
        {
            if(!Step())return Incomplete();
            var a=shapes[i];var b=shapes[j];
            if(Separated(a.Box,b.Box))continue;
            var ae=a.Closed ? a.Points.Length : a.Points.Length-1;
            var be=b.Closed ? b.Points.Length : b.Points.Length-1;
            for(int x=0;x<ae;x++)for(int y=0;y<be;y++)
            {
                if(!Step())return Incomplete();
                var p=a.Points[x];var q=a.Points[(x+1)%a.Points.Length];
                var r=b.Points[y];var s=b.Points[(y+1)%b.Points.Length];
                if(Intersects(p,q,r,s))return new(State.Found,a.Id,b.Id,ops,Finding.Boundary,x,y);
            }
            // With no boundary contact, one point is sufficient for containment of a connected path.
            if(a.Closed) { var inside=Contains(a,b.Points[0]);if(inside is null)return Incomplete();if(inside.Value)return new(State.Found,a.Id,b.Id,ops,Finding.SecondInsideFirst); }
            if(b.Closed) { var inside=Contains(b,a.Points[0]);if(inside is null)return Incomplete();if(inside.Value)return new(State.Found,a.Id,b.Id,ops,Finding.FirstInsideSecond); }
        }
        return new(State.Clear,null,null,ops);

        bool? Contains(Shape polygon,P p)
        {
            var inside=false;
            for(int k=0;k<polygon.Points.Length;k++)
            {
                if(!Step())return null;
                var x=polygon.Points[k];var y=polygon.Points[(k+1)%polygon.Points.Length];
                if(On(x,y,p))return true;
                if((x.Y>p.Y)!=(y.Y>p.Y))
                {
                    var cross=Cross(x,y,p);
                    if((y.Y>x.Y && cross>0) || (y.Y<x.Y && cross<0))inside=!inside;
                }
            }
            return inside;
        }
    }
    private static bool Separated(Bounds a,Bounds b)=>a.MaxX<b.MinX || b.MaxX<a.MinX || a.MaxY<b.MinY || b.MaxY<a.MinY;
    private static Int128 Cross(P a,P b,P c)=>((Int128)b.X-a.X)*((Int128)c.Y-a.Y)-((Int128)b.Y-a.Y)*((Int128)c.X-a.X);
    private static bool On(P a,P b,P p)=>Cross(a,b,p)==0 && p.X>=Math.Min(a.X,b.X) && p.X<=Math.Max(a.X,b.X) && p.Y>=Math.Min(a.Y,b.Y) && p.Y<=Math.Max(a.Y,b.Y);
    private static bool Opposite(Int128 a,Int128 b)=>(a<0 && b>0)||(a>0 && b<0);
    private static bool Intersects(P a,P b,P c,P d)
    {
        if(Math.Max(a.X,b.X)<Math.Min(c.X,d.X) || Math.Max(c.X,d.X)<Math.Min(a.X,b.X) || Math.Max(a.Y,b.Y)<Math.Min(c.Y,d.Y) || Math.Max(c.Y,d.Y)<Math.Min(a.Y,b.Y))return false;
        return (Opposite(Cross(a,b,c),Cross(a,b,d)) && Opposite(Cross(c,d,a),Cross(c,d,b))) || On(a,b,c) || On(a,b,d) || On(c,d,a) || On(c,d,b);
    }
}
