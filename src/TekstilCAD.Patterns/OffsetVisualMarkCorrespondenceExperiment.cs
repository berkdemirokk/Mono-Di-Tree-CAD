using System.Numerics;
using TekstilCAD.Core;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Patterns;

/// <summary>
/// Experimental validation of a hypothetical vertex-to-vertex visual-marker
/// correspondence on a caller-supplied candidate. NotchMark contains neither
/// direction nor depth or cut semantics. No production caller uses this method;
/// no manufacturing feature, mark direction or production approval is implied.
/// Both inputs must remain stable while they are copied and read.
/// </summary>
internal static class OffsetVisualMarkCorrespondenceExperiment
{
    internal static bool TryVerify(PatternPiece source,PatternPiece candidate,long delta,long vertexBudget,
        long edgeBand,long chordBudget,long heightBudget,long minimumSeparation,out string reason,CancellationToken token=default)
    {
        ArgumentNullException.ThrowIfNull(source);ArgumentNullException.ThrowIfNull(candidate);
        token.ThrowIfCancellationRequested();reason="Invalid closed contour or marker data.";
        if(!source.Closed || !candidate.Closed || source.Points is null || candidate.Points is null ||
           source.Points.Count<3 || source.Points.Count>707 || candidate.Points.Count!=source.Points.Count)return false;
        var sp=source.Points.ToArray();var cp=candidate.Points.ToArray();int n=sp.Length;
        if(sp[0]==sp[^1] || cp[0]==cp[^1]){reason="Repeated closing vertex is unsupported.";return false;}
        if(!CopyMarks(source.Notches,sp,out var sm) || !CopyMarks(candidate.Notches,cp,out var cm))return false;
        if((sm is null)!=(cm is null) || (sm is not null && sm.Count!=cm!.Count))
        {reason="Marker set was added, dropped, or changed in size.";return false;}
        var s=new GridPoint[n];var c=new GridPoint[n];
        for(int i=0;i<n;i++)
        {
            token.ThrowIfCancellationRequested();
            if(!Grid(sp[i],out s[i]) || !Grid(cp[i],out c[i]))
            {reason="A coordinate is outside bounds or not exactly represented on the grid.";return false;}
        }
        IReadOnlyList<GridPoint>[] paths=[c];
        if(!OffsetBudgetEvidenceExperiment.TryVerify(s,paths,delta,vertexBudget,edgeBand,chordBudget,heightBudget,
            minimumSeparation,out reason,token))return false;
        if(!OffsetCandidateProof.TryCreate(s,delta,out var ideal,out reason,token))return false;
        var limit=(BigInteger)vertexBudget*vertexBudget;int start=-1;
        for(int shift=0;shift<n;shift++)
        {
            token.ThrowIfCancellationRequested();bool match=true;
            for(int i=0;i<n;i++)
            {
                BigInteger dx=(BigInteger)c[(shift+i)%n].X-ideal[i].X,dy=(BigInteger)c[(shift+i)%n].Y-ideal[i].Y;
                if(dx*dx+dy*dy>limit){match=false;break;}
            }
            if(match){if(start>=0){reason="Marker mapping is ambiguous.";return false;}start=shift;}
        }
        if(start<0){reason="No unique indexed boundary mapping.";return false;}
        if(sm is not null)
        {
            var targets=cm!.ToHashSet();
            foreach(int index in sm)
            {
                token.ThrowIfCancellationRequested();
                if(!targets.Remove((start+index)%n)){reason="A marker is on the wrong candidate vertex or lost.";return false;}
            }
            if(targets.Count!=0){reason="Unexpected candidate marker.";return false;}
        }
        reason="Hypothetical indexed visual-marker correspondence only; direction, cutting geometry, garment features and production suitability are not certified.";
        return true;
    }
    private static bool CopyMarks(List<NotchMark>? input,Point2[] points,out List<int>? indices)
    {
        indices=null;if(input is null)return true;if(input.Count==0 || input.Count>points.Length)return false;
        var found=new HashSet<int>();indices=[];
        foreach(var mark in input)
        {
            if(mark is null || mark.VertexIndex<0 || mark.VertexIndex>=points.Length ||
               mark.Position!=points[mark.VertexIndex] || !found.Add(mark.VertexIndex))return false;
            indices.Add(mark.VertexIndex);
        }
        return true;
    }
    private static bool Grid(Point2 p,out GridPoint result)
    {
        result=default;if(!p.IsFinite || Math.Abs(p.X)>Geometry.MaxCoordinate || Math.Abs(p.Y)>Geometry.MaxCoordinate)return false;
        long x=(long)Math.Round(p.X*1_000_000d,MidpointRounding.AwayFromZero);
        long y=(long)Math.Round(p.Y*1_000_000d,MidpointRounding.AwayFromZero);
        if(x/1_000_000d!=p.X || y/1_000_000d!=p.Y)return false;
        result=new(x,y);return true;
    }
}
