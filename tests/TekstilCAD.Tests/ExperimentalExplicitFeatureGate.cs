using System.Numerics;
using System.Reflection;
using TekstilCAD.Core;
using TekstilCAD.Patterns;

namespace TekstilCAD.Tests;

// Test-only gate for explicitly selected features, not automatic discovery or
// a production approval. Caller must keep inputs unchanged while copied.
internal static class ExperimentalExplicitFeatureGate
{
    internal sealed record V(int Start,int Witness,int End,double WidthToleranceMm,double DepthToleranceMm);
    internal sealed record Channel(int FirstEdge,int SecondEdge,double MinimumMm);
    internal sealed record Result(bool SelectedChecksPassed,int CheckedFeatures,string Reason)
    {
        internal bool AllFeaturesCertified=>false;
        internal bool ProductionApproved=>false;
    }
    internal static Result Evaluate(PatternPiece source,double allowance,IReadOnlyList<V> notches,IReadOnlyList<Channel> channels)
    {
        ArgumentNullException.ThrowIfNull(source);ArgumentNullException.ThrowIfNull(notches);ArgumentNullException.ThrowIfNull(channels);
        Result Fail(string why)=>new(false,0,why);
        int notchCount=notches.Count,channelCount=channels.Count;
        if(source.Points is null || source.Points.Count<3 || source.Points.Count>707 || source.Notches is not null ||
           notchCount<0 || notchCount>64 || channelCount<0 || channelCount>64 ||
           notchCount+channelCount==0 || notchCount+channelCount>64)
            return Fail("No explicit features, marked source, or invalid work budget; complete discovery is unknown.");
        var p=source.Clone();var v=notches.ToArray();var ch=channels.ToArray();int n=p.Points.Count;
        var witnesses=new HashSet<int>();var pairs=new HashSet<(int,int)>();
        foreach(var f in v)
        {
            if(f is null || f.Start<0 || f.Start>=n || f.Witness<0 || f.Witness>=n || f.End<0 || f.End>=n ||
               !double.IsFinite(f.WidthToleranceMm) || f.WidthToleranceMm<0 || !double.IsFinite(f.DepthToleranceMm) || f.DepthToleranceMm<0 ||
               !witnesses.Add(f.Witness))return Fail("Invalid or duplicate V feature.");
            int span=(f.End-f.Start+n)%n,at=(f.Witness-f.Start+n)%n;
            if(span<2 || at==0 || at>=span)return Fail("V witness is outside its directed source span.");
        }
        foreach(var f in ch)
        {
            if(f is null || f.FirstEdge<0 || f.FirstEdge>=n || f.SecondEdge<0 || f.SecondEdge>=n ||
               !double.IsFinite(f.MinimumMm) || f.MinimumMm<=0)return Fail("Invalid channel requirement.");
            int a=Math.Min(f.FirstEdge,f.SecondEdge),b=Math.Max(f.FirstEdge,f.SecondEdge);
            if(a==b || b==a+1 || (a==0 && b==n-1) || !pairs.Add((a,b)))return Fail("Channel edges are adjacent or repeated.");
        }
        var evaluated=ExperimentalExactGridRoundingTests.Evaluate(p,allowance);
        if(!evaluated.Ok)return Fail("Original or indexed geometry guard: "+evaluated.Reason);
        var candidate=evaluated.Candidate.Select(x=>new Point2(x.X/1_000_000d,x.Y/1_000_000d)).ToArray();
        var measure=typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetUserFeatureToleranceExperiment",true)!
            .GetMethod("TryVerify",BindingFlags.NonPublic|BindingFlags.Static)!;
        foreach(var f in v)
        {
            object?[] args=[p.Points,candidate,f.Start,f.Witness,f.End,f.WidthToleranceMm,f.DepthToleranceMm,null,CancellationToken.None];
            if(!(bool)measure.Invoke(null,args)!)return Fail("Selected V feature: "+args[7]);
        }
        foreach(var f in ch)
            if(!SegmentsAtLeast(candidate[f.FirstEdge],candidate[(f.FirstEdge+1)%n],candidate[f.SecondEdge],candidate[(f.SecondEdge+1)%n],f.MinimumMm))
                return Fail("Selected channel is intersecting or below its explicit minimum.");
        return new(true,v.Length+ch.Length,"Only explicitly selected V and channel requirements passed; completeness and production are not certified.");
    }
    private readonly record struct P(BigInteger X,BigInteger Y);
    private static BigInteger Units(double value)
    {
        long bits=BitConverter.DoubleToInt64Bits(value);int e=(int)((bits>>52)&0x7ff);long f=bits&0x000f_ffff_ffff_ffffL;
        BigInteger m=e==0?f:f|(1L<<52);if(bits<0)m=-m;return m<<(e==0?0:e-1);
    }
    private static P Convert(Point2 p)=>new(Units(p.X),Units(p.Y));
    private static P Diff(P a,P b)=>new(b.X-a.X,b.Y-a.Y);
    private static BigInteger Cross(P a,P b)=>a.X*b.Y-a.Y*b.X;
    private static BigInteger Dot(P a,P b)=>a.X*b.X+a.Y*b.Y;
    private static bool On(P a,P b,P p)=>Cross(Diff(a,b),Diff(a,p)).IsZero &&
        p.X>=BigInteger.Min(a.X,b.X) && p.X<=BigInteger.Max(a.X,b.X) && p.Y>=BigInteger.Min(a.Y,b.Y) && p.Y<=BigInteger.Max(a.Y,b.Y);
    private static bool Intersects(P a,P b,P c,P d)
    {
        int ac=Cross(Diff(a,b),Diff(a,c)).Sign,ad=Cross(Diff(a,b),Diff(a,d)).Sign;
        int ca=Cross(Diff(c,d),Diff(c,a)).Sign,cb=Cross(Diff(c,d),Diff(c,b)).Sign;
        return (ac==0 && On(a,b,c)) || (ad==0 && On(a,b,d)) || (ca==0 && On(c,d,a)) || (cb==0 && On(c,d,b)) || ac*ad<0 && ca*cb<0;
    }
    private static bool AtLeast(P p,P a,P b,BigInteger thresholdSquared)
    {
        var v=Diff(a,b);var w=Diff(a,p);var len=Dot(v,v);var projection=Dot(w,v);
        if(len.IsZero)return false;
        if(projection<=0)return Dot(w,w)>=thresholdSquared;
        if(projection>=len){var z=Diff(b,p);return Dot(z,z)>=thresholdSquared;}
        var cross=Cross(v,w);return cross*cross>=thresholdSquared*len;
    }
    internal static bool SegmentsAtLeast(Point2 a,Point2 b,Point2 c,Point2 d,double minimumMm)
    {
        if(!a.IsFinite || !b.IsFinite || !c.IsFinite || !d.IsFinite || !double.IsFinite(minimumMm) || minimumMm<=0)return false;
        var x=Convert(a);var y=Convert(b);var z=Convert(c);var w=Convert(d);
        if(Intersects(x,y,z,w))return false;var t=Units(minimumMm);var t2=t*t;
        return AtLeast(x,z,w,t2) && AtLeast(y,z,w,t2) && AtLeast(z,x,y,t2) && AtLeast(w,x,y,t2);
    }
}
