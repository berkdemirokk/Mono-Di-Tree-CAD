using System.Text.Json;
namespace FittedResearch;

// Diagnostic only. A source-matched relation list is rebound to changed
// represented edges, but it is never a production or composition graph.
internal static class CombinedGraphStudy
{
    internal sealed record Result(IReadOnlyList<BodySeamProjection.Lengths> Before,IReadOnlyList<BodySeamProjection.Lengths> After,IReadOnlyList<ProjectedLengthBounds.Interval> WaistBefore,IReadOnlyList<ProjectedLengthBounds.Interval> WaistAfter,BodyCurveStudy.Result LeftAfter)
    {
        public string Status=>"unresolved";public bool ExportAllowed=>false;public bool ProductionAllowed=>false;
        public BackWaistCandidate.ReferenceState ReferenceAssessment=>BackWaistCandidate.ReferenceState.Unverified;
        public void RequireResearchComposition()=>throw new InvalidOperationException("Modified graph is not approved for composition");
    }
    static BodyCurveStudy.Edge Mirror(BodyCurveStudy.Edge e,int id)=>new("left_btorso",id,e.Kind,-e.Bx,e.By,-e.Ax,e.Ay,Array.AsReadOnly(e.RelativeControls.Reverse().Select(c=>(1-c.X,c.Y)).ToArray()),e.ArcY);
    static bool Equal(BodyCurveStudy.Edge a,BodyCurveStudy.Edge b)=>a.Panel==b.Panel&&a.Id==b.Id&&a.Kind==b.Kind&&a.Ax==b.Ax&&a.Ay==b.Ay&&a.Bx==b.Bx&&a.By==b.By&&a.ArcY==b.ArcY&&a.RelativeControls.SequenceEqual(b.RelativeControls);
    // Only this factory can issue the panel pair. It reconstructs the fixed
    // synthetic candidate and makes immutable copies of both contours.
    internal sealed class VerifiedPanels
    {
        private VerifiedPanels(string snapshot,IReadOnlyList<BodyCurveStudy.Edge> right,IReadOnlyList<BodyCurveStudy.Edge> left,ShoulderSeamStudy.CertifiedPanelPair? shoulder) {Snapshot=snapshot;Right=right;Left=left;Shoulder=shoulder;}
        internal string Snapshot { get; } internal IReadOnlyList<BodyCurveStudy.Edge> Right { get; } internal IReadOnlyList<BodyCurveStudy.Edge> Left { get; }
        internal ShoulderSeamStudy.CertifiedPanelPair? Shoulder { get; }
        internal static void RequireMirror(IReadOnlyList<BodyCurveStudy.Edge> right,IReadOnlyList<BodyCurveStudy.Edge> left) {
            if(right.Count!=13||left.Count!=13||Enumerable.Range(0,13).Any(i=>!Equal(Mirror(right[12-i],i),left[i])))throw new ArgumentException("Changed mirror mismatch");
        }
        internal static VerifiedPanels Create(string id,bool shoulder=false) {
            if(id is not ("synthetic-base" or "synthetic-distinct"))throw new ArgumentException("Unsupported changed fixture");
            var candidate=CombinedWaistCurvatureExperiment.Create(id,.3,1.5);
            var right=candidate.RightAfter.Select(e=>e with {RelativeControls=Array.AsReadOnly(e.RelativeControls.ToArray())}).ToArray();
            var left=Enumerable.Range(0,13).Select(i=>Mirror(right[12-i],i)).ToArray();
            RequireMirror(right,left);
            return new(SyntheticBodyCatalog.Create(id).Snapshot(ResearchDesign.PinnedFitted),Array.AsReadOnly(right),Array.AsReadOnly(left),shoulder?ShoulderSeamStudy.CertifiedPanelPair.Create(id):null);
        }
    }
    internal static Result Create(string id,JsonElement reference,bool shoulder=false)
    {
        if(id is not ("synthetic-base" or "synthetic-distinct") || reference.GetProperty("id").GetString()!=id)throw new ArgumentException("Unsupported source fixture identity");
        var body=SyntheticBodyCatalog.Create(id);var design=ResearchDesign.PinnedFitted;var graph=BodySeamStudy.Calculate(body,design);
        var source=reference.GetProperty("stitches").EnumerateArray().ToArray();if(source.Length!=23||graph.Rules.Count!=23)throw new ArgumentException("Incomplete source graph");
        foreach(var rule in graph.Rules) {
            var r=rule.Kind=="MetaGarment"?rule with {Owner=id}:rule;
            var matches=source.Where(x=>x.GetProperty("owner").GetString()==r.Owner&&x.GetProperty("owner_type").GetString()==r.Kind&&x.GetProperty("index").GetInt32()==r.Index).ToArray();
            if(matches.Length!=1)throw new ArgumentException("Source graph rule identity mismatch");
            SyntheticFullSeamGraph.Compare(r,matches[0]);
        }
        var initial=BodyCurveStudy.Calculate(body,design);
        if(initial.Count!=6||initial.Any(t=>t.Closure!=BodyCurveStudy.ClosureEvidence.ClosedRepresented||t.Unresolved!=0||t.NonMonotone!=0))throw new ArgumentException("Baseline topology unresolved");
        var verified=VerifiedPanels.Create(id,shoulder);var right=verified.Right;
        var originalRight=BackBodiceCandidate.CreateFromBody(body,design,false);var originalLeft=BackBodiceCandidate.CreateFromBody(body,design,true);
        var left=verified.Left;
        for(int i=0;i<13;i++) {
            var r=originalRight.Edges[12-i];var l=originalLeft.Edges[i];
            var original=new BodyCurveStudy.Edge("right_btorso",r.Id,r.Kind,r.Start.X,r.Start.Y,r.End.X,r.End.Y,r.Controls.Select(c=>(c.X,c.Y)).ToArray(),r.ArcControlY);
            var baseline=new BodyCurveStudy.Edge("left_btorso",l.Id,l.Kind,l.Start.X,l.Start.Y,l.End.X,l.End.Y,l.Controls.Select(c=>(c.X,c.Y)).ToArray(),l.ArcControlY);
            if(!Equal(Mirror(original,i),baseline))throw new ArgumentException("Original mirror identity mismatch");
            if(i is not (4 or 5 or 6) && !Equal(left[i],baseline))throw new ArgumentException("Unexpected left contour change");
        }
        var after=BodyCurveStudy.Inspect("left_btorso",left,13);
        if(after.Closure!=BodyCurveStudy.ClosureEvidence.ClosedRepresented||after.Unresolved!=0||after.NonMonotone!=0)throw new ArgumentException("Left transformed topology unresolved");
        var before=BodySeamProjection.Calculate(body,design);var projected=BodySeamProjection.CalculateChanged(body,design,graph,verified);
        if(before.Rules.Count!=23||projected.Rules.Count!=23)throw new ArgumentException("Incomplete graph projection");
        foreach(var (a,b) in before.Rules.Zip(projected.Rules))if(a.Owner!=b.Owner||a.Kind!=b.Kind||a.Index!=b.Index)throw new ArgumentException("Projection identity mismatch");
        foreach(var (a,b) in before.Rules.Zip(projected.Rules)) {
            bool expectedChange=a.Kind=="MetaGarment" || a.Kind=="BodiceHalf"&&(a.Index==1||shoulder&&a.Index==0)&&(a.Owner=="left"||a.Owner=="right");
            if(!expectedChange&&(a.A!=b.A||a.B!=b.B))throw new ArgumentException("Unexpected seam length change");
        }
        // Verify fail-closed identity and closure guards with valid edge changes.
        var rules=graph.Rules.ToArray();var row=rules.Single(r=>r.Kind=="BodiceHalf"&&r.Owner=="left"&&r.Index==1);int n=Array.IndexOf(rules,row);
        var refs=row.B.Edges.ToArray();refs[0]=refs[0] with {Edge=6};rules[n]=row with {B=row.B with {Edges=Array.AsReadOnly(refs)}};
        var wrong=new BodySeamStudy.UnverifiedGraph(graph.Snapshot,Array.AsReadOnly(rules));
        try {BodySeamProjection.CalculateChanged(body,design,wrong,verified);throw new InvalidOperationException("Wrong graph accepted");}catch(ArgumentException) { }
        refs=row.B.Edges.ToArray();refs[0]=refs[0] with {Flipped=!refs[0].Flipped};rules[n]=row with {B=row.B with {Edges=Array.AsReadOnly(refs)}};
        wrong=new BodySeamStudy.UnverifiedGraph(graph.Snapshot,Array.AsReadOnly(rules));
        try {BodySeamProjection.CalculateChanged(body,design,wrong,verified);throw new InvalidOperationException("Wrong direction accepted");}catch(ArgumentException) { }
        refs=row.B.Edges.ToArray();refs[0]=refs[0] with {Panel="right_btorso"};rules[n]=row with {B=row.B with {Edges=Array.AsReadOnly(refs)}};
        wrong=new BodySeamStudy.UnverifiedGraph(graph.Snapshot,Array.AsReadOnly(rules));
        try {BodySeamProjection.CalculateChanged(body,design,wrong,verified);throw new InvalidOperationException("Wrong panel accepted");}catch(ArgumentException) { }
        var broken=left.ToArray();broken[4]=broken[4] with {Ax=broken[4].Ax+.01};
        try {VerifiedPanels.RequireMirror(right,broken);throw new InvalidOperationException("Wrong mirror accepted");}catch(ArgumentException) { }
        var asymmetric=left.ToArray();var control=asymmetric[4].RelativeControls.Single();
        asymmetric[4]=asymmetric[4] with {RelativeControls=Array.AsReadOnly(new[]{(control.X,control.Y+.01)})};
        if(BodyCurveStudy.Inspect("left_btorso",asymmetric,13).Closure!=BodyCurveStudy.ClosureEvidence.ClosedRepresented)throw new InvalidOperationException("Mirror regression must remain closed");
        try {VerifiedPanels.RequireMirror(right,asymmetric);throw new InvalidOperationException("Closed asymmetric panel accepted");}catch(ArgumentException) { }
        var other=SyntheticBodyCatalog.Create(id=="synthetic-base"?"synthetic-distinct":"synthetic-base");
        var otherGraph=BodySeamStudy.Calculate(other,design);
        try {BodySeamProjection.CalculateChanged(other,design,otherGraph,verified);throw new InvalidOperationException("Other source snapshot accepted");}catch(ArgumentException) { }
        return new(before.Rules,projected.Rules,before.WaistParts,projected.WaistParts,after);
    }
}
