using Clipper2Lib;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Patterns;

/// <summary>Experimental observation of whether changing the integer-grid origin changes
/// Clipper's forward and inverse boundaries. No outcome authorizes production, proves
/// local feature preservation, or establishes an error bound.</summary>
internal static class OffsetOriginSensitivityDiagnostic
{
    internal sealed record Observation(int GlobalForwardPaths,int LocalForwardPaths,int GlobalReversePaths,int LocalReversePaths,
        bool ForwardExactlyEqual,bool ReverseExactlyEqual);
    private const int MaximumVertices=1024;
    private const long CoordinateLimit=1L<<60;

    /// <remarks>Inputs are grid units. The caller must not mutate the contour while copied.
    /// Coordinates, origin and delta are intentionally bounded well below Int64 limits.
    /// The local result is shifted back before exact comparison; start vertex may differ,
    /// but reversal, resegmentation, or any changed coordinate counts as a difference.</remarks>
    internal static Observation Compare(IReadOnlyList<GridPoint> source,long delta,GridPoint origin,CancellationToken token=default)
    {
        ArgumentNullException.ThrowIfNull(source);token.ThrowIfCancellationRequested();
        if(source.Count is <3 or >MaximumVertices || delta<=0 || delta>CoordinateLimit/4 ||
           origin.X < -CoordinateLimit || origin.X > CoordinateLimit || origin.Y < -CoordinateLimit || origin.Y > CoordinateLimit)
            throw new ArgumentException("Invalid input or diagnostic resource limit.");
        var global=new Path64();var local=new Path64();
        for(var i=0;i<source.Count;i++)
        {
            token.ThrowIfCancellationRequested();var p=source[i];
            if(p.X < -CoordinateLimit || p.X > CoordinateLimit || p.Y < -CoordinateLimit || p.Y > CoordinateLimit)
                throw new ArgumentException("Coordinate exceeds diagnostic limit.");
            long x=checked(p.X-origin.X),y=checked(p.Y-origin.Y);
            if(x < -CoordinateLimit || x > CoordinateLimit || y < -CoordinateLimit || y > CoordinateLimit)
                throw new ArgumentException("Local coordinate exceeds diagnostic limit.");
            global.Add(new Point64(p.X,p.Y));local.Add(new Point64(x,y));
        }
        Paths64 Forward(Path64 p) { token.ThrowIfCancellationRequested();return Clipper.InflatePaths(new Paths64 { p },delta,JoinType.Miter,EndType.Polygon,2); }
        Paths64 Reverse(Paths64 p) { token.ThrowIfCancellationRequested();return p.Count==1 ? Clipper.InflatePaths(p,-delta,JoinType.Miter,EndType.Polygon,2) : new Paths64(); }
        var globalForward=Forward(global);var localForward=Forward(local);
        var globalReverse=Reverse(globalForward);var localReverse=Reverse(localForward);
        bool Same(Paths64 a,Paths64 b)
        {
            token.ThrowIfCancellationRequested();
            if(a.Count!=1 || b.Count!=1 || a[0].Count!=b[0].Count || a[0].Count==0)return false;
            var ga=a[0];var lb=b[0];var start=-1;
            for(var i=0;i<lb.Count;i++)
            {
                token.ThrowIfCancellationRequested();
                if(checked(lb[i].X+origin.X)==ga[0].X && checked(lb[i].Y+origin.Y)==ga[0].Y)
                { if(start>=0)return false;start=i; }
            }
            if(start<0)return false;
            for(var i=0;i<ga.Count;i++)
            {
                token.ThrowIfCancellationRequested();var point=lb[(start+i)%lb.Count];
                if(checked(point.X+origin.X)!=ga[i].X || checked(point.Y+origin.Y)!=ga[i].Y)return false;
            }
            return true;
        }
        return new(globalForward.Count,localForward.Count,globalReverse.Count,localReverse.Count,Same(globalForward,localForward),Same(globalReverse,localReverse));
    }
}
