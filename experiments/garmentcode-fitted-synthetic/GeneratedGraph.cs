using TekstilCAD.Core;

namespace FittedResearch;
// Standalone fail-closed graph diagnostic. Source comparisons must be supplied
// from an independently pinned contract; this is not a provenance verifier.
internal static class GeneratedGraph
{
    internal sealed record Edge(string Snapshot,string Panel,int Id,string Kind,Point2 Start,Point2 End);
    internal sealed record Seam(string Snapshot,string PanelA,int EdgeA,bool FlipA,string PanelB,int EdgeB,bool FlipB);
    internal sealed record Verdict(IReadOnlyList<string> Issues) { public string Status=>"unresolved"; public bool ExportAllowed=>false; }
    internal static Verdict Inspect(string snapshot,IReadOnlyList<Edge> edges,IReadOnlyList<Seam> seams,IReadOnlyList<Seam> expected)
    {
        var issues=new List<string>();
        if(string.IsNullOrEmpty(snapshot)||edges is null||seams is null||expected is null) return new(Array.AsReadOnly(new[]{"missing-input"}));
        // Own a local snapshot. Caller must keep collections stable during copy.
        var es=edges.ToArray();var ss=seams.ToArray();var contract=expected.ToArray();
        if(es.Length==0 || ss.Length==0 || contract.Length==0) issues.Add("incomplete-graph");
        if(es.Any(e=>e.Snapshot!=snapshot)||ss.Any(s=>s.Snapshot!=snapshot)||contract.Any(s=>s.Snapshot!=snapshot)) issues.Add("snapshot-mismatch");
        var keys=new HashSet<(string,int)>();
        foreach(var e in es)
            if(string.IsNullOrWhiteSpace(e.Panel)||e.Id<0||!keys.Add((e.Panel,e.Id))) issues.Add("edge-identity-invalid");
        foreach(var s in ss) if(!keys.Contains((s.PanelA,s.EdgeA))||!keys.Contains((s.PanelB,s.EdgeB))) issues.Add("dangling-seam");
        if(!ss.SequenceEqual(contract)) issues.Add("seam-identity-or-direction-mismatch");
        foreach(var group in es.GroupBy(e=>e.Panel))
        {
            var sorted=group.OrderBy(e=>e.Id).ToArray();
            if(sorted.Select(e=>e.Id).Where((id,i)=>id!=i).Any()) { issues.Add("incomplete-edge-order:"+group.Key); continue; }
            if(sorted.Any(e=>e.Kind!="L")) { issues.Add("curve-topology-unproven:"+group.Key); continue; }
            if(sorted.Length<3 || sorted.Where((e,i)=>e.End!=sorted[(i+1)%sorted.Length].Start).Any()) { issues.Add("open-contour:"+group.Key); continue; }
            try { Geometry.ValidateContour(sorted.Select(e=>e.Start).ToArray()); }
            catch(ArgumentException) { issues.Add("self-intersection-or-degenerate:"+group.Key); }
        }
        issues.Add("physical-sewing-and-production-unresolved");
        return new(issues.AsReadOnly());
    }
}
