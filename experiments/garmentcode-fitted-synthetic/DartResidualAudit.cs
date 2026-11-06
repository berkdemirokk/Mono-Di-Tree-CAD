using System.Text.Json;
using System.Numerics;
namespace FittedResearch;

// Exact source-fixture binary64 segment comparisons, tied to the checked seam
// rule. This is research evidence, never a geometry fix or acceptance gate.
internal static class DartResidualAudit
{
    internal sealed record Result(string Panel,int EdgeA,int EdgeB,ProjectedLengthBounds.Interval SourceA,ProjectedLengthBounds.Interval SourceB,ProjectedLengthBounds.Interval CandidateA,ProjectedLengthBounds.Interval CandidateB,bool SourceReportedLengthsEqual,bool? SourceHorizontalCentered)
    {
        public bool SourceDisjoint=>SourceA.Disjoint(SourceB);public bool CandidateDisjoint=>CandidateA.Disjoint(CandidateB);
        public string Status=>"unresolved";public bool ExportAllowed=>false;public bool ProductionAllowed=>false;
    }
    internal static Result Inspect(SyntheticFullSeamGraph.Rule rule,JsonElement sourceRule,JsonElement reference,BodySeamProjection.Lengths candidate)
    {
        ArgumentNullException.ThrowIfNull(rule);ArgumentNullException.ThrowIfNull(candidate);
        if(rule.Kind is not ("BodiceFrontHalf" or "BodiceBackHalf" or "FittedSkirtPanel")||rule.Owner!=candidate.Owner||rule.Kind!=candidate.Kind||rule.Index!=candidate.Index)throw new ArgumentException("Unsupported dart identity");
        SyntheticFullSeamGraph.Compare(rule,sourceRule);
        if(rule.A.Edges.Count!=1||rule.B.Edges.Count!=1||rule.A.Ruffles.Count!=1||rule.B.Ruffles.Count!=1||rule.A.Ruffles[0]!=new SyntheticFullSeamGraph.Ruffle(0,1,1)||rule.B.Ruffles[0]!=new SyntheticFullSeamGraph.Ruffle(0,1,1))throw new ArgumentException("Unsupported dart sections");
        var a=rule.A.Edges[0];var b=rule.B.Edges[0];if(a.Panel!=b.Panel||a.Panel!=rule.Owner||a.Edge==b.Edge)throw new ArgumentException("Unsupported dart connection");
        (BezierLengthBounds.Point start,BezierLengthBounds.Point end,double reported) Edge(SyntheticFullSeamGraph.Ref x) {
            var edge=reference.GetProperty("panels").GetProperty(x.Panel).GetProperty("edges")[x.Edge];
            if(edge.GetProperty("kind").GetString()!="Edge")throw new ArgumentException("Source dart is not a line");
            BezierLengthBounds.Point Point(JsonElement p) {if(p.ValueKind!=JsonValueKind.Array||p.GetArrayLength()!=2)throw new ArgumentException("Invalid source point");var x=p[0].GetDouble();var y=p[1].GetDouble();if(!double.IsFinite(x)||!double.IsFinite(y))throw new ArgumentException("Non-finite source point");return new(x,y);}
            var reported=edge.GetProperty("length").GetDouble();if(!double.IsFinite(reported)||reported<=0)throw new ArgumentException("Invalid source reported length");
            return (Point(edge.GetProperty("start")),Point(edge.GetProperty("end")),reported);
        }
        var ea=Edge(a);var eb=Edge(b);
        if(ea.start==ea.end||eb.start==eb.end||new[]{ea.start,ea.end}.Intersect(new[]{eb.start,eb.end}).Count()!=1)throw new ArgumentException("Source dart apex mismatch");
        var tip=new[]{ea.start,ea.end}.Intersect(new[]{eb.start,eb.end}).Single();
        var footA=ea.start==tip?ea.end:ea.start;var footB=eb.start==tip?eb.end:eb.start;
        BigInteger Scaled(double x) {
            long bits=BitConverter.DoubleToInt64Bits(x);int exponent=(int)((bits>>52)&0x7ff);ulong fraction=(ulong)bits&0x000f_ffff_ffff_ffffUL;
            var value=new BigInteger(exponent==0?fraction:fraction|(1UL<<52))<<(exponent==0?0:exponent-1);return bits<0?-value:value;
        }
        bool? centered=footA.Y==footB.Y?2*Scaled(tip.X)==Scaled(footA.X)+Scaled(footB.X):null;
        ProjectedLengthBounds.Interval Bound((BezierLengthBounds.Point start,BezierLengthBounds.Point end,double reported) e)=>ProjectedLengthBounds.Project(BezierLengthBounds.Enclose([e.start,e.end],0),1);
        return new(a.Panel,a.Edge,b.Edge,Bound(ea),Bound(eb),candidate.A,candidate.B,ea.reported==eb.reported,centered);
    }
}
