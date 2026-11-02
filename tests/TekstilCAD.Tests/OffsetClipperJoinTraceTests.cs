using System.Collections;
using System.Reflection;
using Clipper2Lib;
using TekstilCAD.Patterns;
using Xunit.Abstractions;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Tests;

/// <summary>Version-specific diagnostic of Clipper's raw join and union stages; never production approval.</summary>
public sealed class OffsetClipperJoinTraceTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false, 1_000_000L, false)]
    [InlineData(true, 1_000_000L, false)]
    [InlineData(false, 10_000_000L, false)]
    [InlineData(false, 100_000_000L, false)]
    [InlineData(false, 10_000_000L, true)]
    [InlineData(true, 10_000_000L, true)]
    [InlineData(false, 100_000_000L, true)]
    public void TraceQuantizedInputRawJoinAndUnion(bool reverse, long scale, bool preserveGrid)
    {
        var path = new Path64(Enumerable.Range(0,512).Select(i =>
        {
            double a=Math.Tau*i/512, r=1000+200*Math.Cos(3*a);
            if(preserveGrid)
            {
                long factor=scale/1_000_000;
                return new Point64((long)Math.Round(r*Math.Cos(a)*1_000_000,MidpointRounding.AwayFromZero)*factor,
                    (long)Math.Round(r*Math.Sin(a)*1_000_000,MidpointRounding.AwayFromZero)*factor);
            }
            return new Point64((long)Math.Round(r*Math.Cos(a)*scale,MidpointRounding.AwayFromZero),
                (long)Math.Round(r*Math.Sin(a)*scale,MidpointRounding.AwayFromZero));
        }));
        if(reverse) path.Reverse();
        var before=path.ToArray();
        var offset = new ClipperOffset(2.0);
        offset.AddPath(path,JoinType.Miter,EndType.Polygon);
        var type=typeof(ClipperOffset); const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var groups=(IList)type.GetField("_groupList",flags)!.GetValue(offset)!;
        type.GetField("_delta",flags)!.SetValue(offset,(double)scale);
        type.GetField("_mitLimSqr",flags)!.SetValue(offset,0.5);
        type.GetMethod("DoGroupOffset",flags)!.Invoke(offset,[groups[0]]);
        var raw=Assert.Single((Paths64)type.GetField("_solution",flags)!.GetValue(offset)!);
        var clean=Assert.Single(Clipper.InflatePaths(new Paths64 {path},scale,JoinType.Miter,EndType.Polygon,2.0));
        int rawShort=CountShort(raw,scale),cleanShort=CountShort(clean,scale);
        int newVertices=clean.Count(p=>!raw.Any(q=>q.X==p.X && q.Y==p.Y));
        var shortEndpoints=new List<Point64>();
        for(int i=0;i<clean.Count;i++)
        {
            var a=clean[i];var b=clean[(i+1)%clean.Count];
            double dx=(double)b.X-a.X,dy=(double)b.Y-a.Y;
            if(Math.Sqrt(dx*dx+dy*dy)<0.001*scale) { shortEndpoints.Add(a); shortEndpoints.Add(b); }
        }
        int newShortEndpoints=shortEndpoints.Count(p=>!raw.Any(q=>q.X==p.X && q.Y==p.Y));
        var reference=Enumerable.Range(0,512).Select(i =>
        {
            double a=Math.Tau*i/512, r=1000+200*Math.Cos(3*a);
            return new Point64((long)Math.Round(r*Math.Cos(a)*1_000_000,MidpointRounding.AwayFromZero),
                (long)Math.Round(r*Math.Sin(a)*1_000_000,MidpointRounding.AwayFromZero));
        }).ToArray();
        if(reverse) Array.Reverse(reference);
        int sourceGridChanges=Enumerable.Range(0,512).Count(i =>
            (long)Math.Round(path[i].X/(scale/1_000_000.0),MidpointRounding.AwayFromZero)!=reference[i].X ||
            (long)Math.Round(path[i].Y/(scale/1_000_000.0),MidpointRounding.AwayFromZero)!=reference[i].Y);
        output.WriteLine($"reverse={reverse}; preserve_grid={preserveGrid}; grid_per_mm={scale}; source={path.Count}; source_grid_changes={sourceGridChanges}; raw={raw.Count}; raw_short={rawShort}; union={clean.Count}; union_short={cleanShort}; union_vertices_absent_from_raw={newVertices}; short_endpoints_absent_from_raw={newShortEndpoints}/{shortEndpoints.Count}");
        if(sourceGridChanges==0)
        {
            double factor=scale/1_000_000.0;
            var down=new Path64(clean.Select(p=>new Point64((long)Math.Round(p.X/factor,MidpointRounding.AwayFromZero),
                (long)Math.Round(p.Y/factor,MidpointRounding.AwayFromZero))));
            var sourceGrid=new Path64(reference);
            var back=Clipper.InflatePaths(new Paths64 {down},-1_000_000,JoinType.Miter,EndType.Polygon,2.0);
            var matches=typeof(PatternTools).GetMethod("LocallyMatches",BindingFlags.Static|BindingFlags.NonPublic)!;
            bool both=back.Count==1 && (bool)matches.Invoke(null,[sourceGrid,back[0],8d])! &&
                (bool)matches.Invoke(null,[back[0],sourceGrid,8d])!;
            output.WriteLine($"reverse={reverse}; preserve_grid={preserveGrid}; forward_down_vertices={down.Count}; forward_down_short={CountShort(down,1_000_000)}; recovered_paths={back.Count}; current_local_guard_8={both}");
            if(preserveGrid && scale>=10_000_000)
            {
                Assert.Equal(512,down.Count);
                Assert.Equal(0,CountShort(down,1_000_000));
                Assert.True(both,"The existing 8-grid local comparison must pass without increasing tolerance.");
            }
        }
        Assert.Equal(before,path);
        Assert.True(raw.Count>=path.Count);
        if(scale==1_000_000)
        {
            Assert.Equal(515,clean.Count); Assert.Equal(3,cleanShort);
            var method=typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetCandidateProof",true)!
                .GetMethod("TryCreate",BindingFlags.Static|BindingFlags.NonPublic)!;
            var gridPath=path.Select(p=>new GridPoint(p.X,p.Y)).ToArray();
            object?[] args=[gridPath,scale,null,null,CancellationToken.None];
            Assert.True((bool)method.Invoke(null,args)!, (string?)args[3]);
            var indexed=Assert.IsAssignableFrom<IReadOnlyList<GridPoint>>(args[2]);
            var candidate=new Path64(indexed.Select(p=>new Point64(p.X,p.Y)));
            output.WriteLine($"reverse={reverse}; indexed_miter_vertices={candidate.Count}; indexed_miter_short={CountShort(candidate,scale)}");
            Assert.Equal(512,candidate.Count); Assert.Equal(0,CountShort(candidate,scale));
        }
    }

    private static int CountShort(Path64 path,long scale)
    {
        int count=0;
        for(int i=0;i<path.Count;i++)
        {
            double x=path[(i+1)%path.Count].X-path[i].X,y=path[(i+1)%path.Count].Y-path[i].Y;
            if(Math.Sqrt(x*x+y*y)<0.001*scale) count++;
        }
        return count;
    }
}
