using TekstilCAD.Core;

namespace TekstilCAD.Tests;

// Test-only review manifest: index identity is valid only for the exact source
// snapshot. A human's review assertion is not proof of semantic completeness.
internal static class ExperimentalFullEdgeFeatureManifest
{
    internal sealed record Edge(int Index,bool Reviewed,IReadOnlyList<string> FeatureIds);
    internal sealed record V(string Id,int Start,int Witness,int End,double WidthToleranceMm,double DepthToleranceMm);
    internal sealed record Channel(string Id,int FirstEdge,int SecondEdge,double MinimumMm);
    internal sealed record Manifest(Guid SourceId,string SnapshotHash,string Reviewer,IReadOnlyList<Edge> Edges,
        IReadOnlyList<V> Notches,IReadOnlyList<Channel> Channels,double MaxMiterDeviationMm);
    internal sealed record Result(bool Evaluated,IReadOnlyList<Point2> Preview,int ReviewedEdges,int DeclaredFeatures,string Reason)
    {
        internal bool SemanticCompletenessCertified=>false;
        internal bool ProductionApproved=>false;
    }
    internal static Result Evaluate(PatternPiece source,double allowance,Manifest? manifest)
    {
        ArgumentNullException.ThrowIfNull(source);
        Result Fail(string reason)=>new(false,Array.Empty<Point2>(),0,0,reason);
        if(manifest is null || source.Points is null || source.Points.Count<3 || source.Points.Count>707 ||
           source.Notches is not null || manifest.Edges is null || manifest.Notches is null || manifest.Channels is null ||
           string.IsNullOrWhiteSpace(manifest.Reviewer) || manifest.Reviewer.Length>128 ||
           !double.IsFinite(manifest.MaxMiterDeviationMm) || manifest.MaxMiterDeviationMm<=0)
            return Fail("Source, reviewer, tolerance, or explicit manifest missing.");
        int n=source.Points.Count,nv=manifest.Notches.Count,nc=manifest.Channels.Count;
        if(manifest.Edges.Count!=n || nv<0 || nv>64 || nc<0 || nc>64 || nv+nc>64)
            return Fail("Edge or feature count is incomplete or exceeds the budget.");
        var p=source.Clone();var edges=manifest.Edges.ToArray();var vs=manifest.Notches.ToArray();var channels=manifest.Channels.ToArray();
        if(!p.Closed || p.Points[0]==p.Points[^1] || p.Id!=manifest.SourceId ||
           !string.Equals(ExperimentalPhysicalNotchValidation.Fingerprint(p),manifest.SnapshotHash,StringComparison.Ordinal))
            return Fail("Source identity or geometry snapshot changed.");
        var expected=Enumerable.Range(0,n).Select(_=>new HashSet<string>(StringComparer.Ordinal)).ToArray();
        var names=new HashSet<string>(StringComparer.Ordinal);
        static bool ValidId(string? id)=>!string.IsNullOrWhiteSpace(id) && id.Length<=64;
        foreach(var v in vs)
        {
            if(v is null || !ValidId(v.Id) || !names.Add(v.Id) || v.Start<0 || v.Start>=n || v.Witness<0 || v.Witness>=n || v.End<0 || v.End>=n ||
               !double.IsFinite(v.WidthToleranceMm) || v.WidthToleranceMm<0 || !double.IsFinite(v.DepthToleranceMm) || v.DepthToleranceMm<0)
                return Fail("Invalid or duplicate V definition.");
            int span=(v.End-v.Start+n)%n,at=(v.Witness-v.Start+n)%n;
            if(span<2 || at==0 || at>=span)return Fail("V witness outside source span.");
            for(int j=0;j<span;j++)expected[(v.Start+j)%n].Add(v.Id);
        }
        foreach(var channel in channels)
        {
            if(channel is null || !ValidId(channel.Id) || !names.Add(channel.Id) || channel.FirstEdge<0 || channel.FirstEdge>=n ||
               channel.SecondEdge<0 || channel.SecondEdge>=n || !double.IsFinite(channel.MinimumMm) || channel.MinimumMm<=0)
                return Fail("Invalid or duplicate channel definition.");
            int a=Math.Min(channel.FirstEdge,channel.SecondEdge),b=Math.Max(channel.FirstEdge,channel.SecondEdge);
            if(a==b || b==a+1 || (a==0 && b==n-1))return Fail("Channel edges are identical or adjacent.");
            expected[a].Add(channel.Id);expected[b].Add(channel.Id);
        }
        var seen=new bool[n];
        foreach(var e in edges)
        {
            if(e is null || !e.Reviewed || e.Index<0 || e.Index>=n || seen[e.Index] || e.FeatureIds is null || e.FeatureIds.Count<0 || e.FeatureIds.Count>64)
                return Fail("An edge is missing, repeated, unreviewed, or malformed.");
            seen[e.Index]=true;var ids=e.FeatureIds.ToArray();
            if(ids.Any(x=>!ValidId(x)) || ids.Distinct(StringComparer.Ordinal).Count()!=ids.Length ||
               !expected[e.Index].SetEquals(ids))return Fail("An edge's feature definitions are missing, unexpected or duplicated.");
        }
        if(seen.Any(x=>!x))return Fail("Source edge coverage is incomplete.");
        if(vs.Length+channels.Length==0)return Fail("No declared physical features; absence cannot be independently established from this model.");
        // Legacy grid compatibility is deliberately required in this sketch.
        foreach(var point in p.Points)
        {
            if(!ExperimentalExactGridRounding.TryRound(point.X,out var x) || !ExperimentalExactGridRounding.TryRound(point.Y,out var y) ||
               x!=(long)Math.Round(point.X*1_000_000,MidpointRounding.AwayFromZero) || y!=(long)Math.Round(point.Y*1_000_000,MidpointRounding.AwayFromZero))
                return Fail("Exact and existing source grid differ.");
        }
        var result=ExperimentalExactGridRoundingTests.Evaluate(p,allowance);
        if(!result.Ok)return Fail("Existing or indexed guard: "+result.Reason);
        var candidate=result.Candidate.Select(q=>new Point2(q.X/1_000_000d,q.Y/1_000_000d)).ToArray();
        if(!ExperimentalSourceMiterIntervalBound.TryVerifyRequested(p.Points,candidate,allowance,manifest.MaxMiterDeviationMm,out _,out var bound))
            return Fail("Requested allowance bound: "+bound);
        if(vs.Length+channels.Length>0)
        {
            var chosenV=vs.Select(v=>new ExperimentalExplicitFeatureGate.V(v.Start,v.Witness,v.End,v.WidthToleranceMm,v.DepthToleranceMm)).ToArray();
            var chosenC=channels.Select(c=>new ExperimentalExplicitFeatureGate.Channel(c.FirstEdge,c.SecondEdge,c.MinimumMm)).ToArray();
            var gate=ExperimentalExplicitFeatureGate.Evaluate(p,allowance,chosenV,chosenC);
            if(!gate.SelectedChecksPassed)return Fail(gate.Reason);
        }
        return new(true,Array.AsReadOnly(candidate),n,vs.Length+channels.Length,
            "All source edge entries were supplied as reviewed and declared feature tests passed; reviewer completeness, other features and production suitability are not certified.");
    }
}
