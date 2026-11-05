namespace FittedResearch;

// In-memory diagnostic projection for a future research view. It deliberately
// contains neither raw measurements, the measurement-derived hash nor geometry.
// Derived counts can still reveal information and are not anonymous.
internal static class ResearchViewSummary
{
    internal sealed record PanelStatus(string Panel,BodyCurveStudy.ClosureEvidence Closure,int CurvePairs,int UnresolvedPairs,int NonMonotone)
    {
        public string Status=>"unresolved";
    }
    internal sealed class Summary
    {
        internal Summary(IReadOnlyList<PanelStatus> panels,int ruleCount) { Panels=panels;RuleCount=ruleCount; }
        public IReadOnlyList<PanelStatus> Panels { get; } public int RuleCount { get; }
        public string Status=>"unresolved"; public string ReferenceAssessment=>"Unverified";
        public bool ExportAllowed=>false; public bool ProductionAllowed=>false;
    }
    internal static PanelStatus ToPanelStatus(BodyCurveStudy.Result t)=>new(t.Panel,t.Closure,t.Pairs,t.Unresolved,t.NonMonotone);
    internal static Summary Calculate(ResearchBody body,ResearchDesign design)
    {
        var graph=BodySeamStudy.Calculate(body,design);var topology=BodyCurveStudy.Calculate(body,design);
        var panels=topology.Select(ToPanelStatus).ToArray();
        return new(Array.AsReadOnly(panels),graph.Rules.Count);
    }
}
