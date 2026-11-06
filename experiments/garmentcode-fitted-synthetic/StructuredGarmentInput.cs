using System.Collections.Immutable;
namespace FittedResearch;

// Already-parsed, in-memory research declarations only. Nothing here is a
// public intake, measurement protocol, or physical garment approval.
internal static class StructuredGarmentInput
{
    internal const string PinnedModel="research_fitted_shirt_pencil_skirt_v1";
    internal const string ALineModel="a_line_lower_preview_v1";
    internal const int MaxEntries=40;
    internal sealed record Measurement(string? Field,decimal? Value,string? Unit,string? DefinitionId)
    { public override string ToString()=>"Measurement declaration (value hidden)"; }
    internal sealed record ALine(decimal? FrontWaist,decimal? BackWaist,decimal? FrontHip,decimal? BackHip,decimal? FrontWaistEase,decimal? BackWaistEase,decimal? FrontHipEase,decimal? BackHipEase,decimal? HipDepth,decimal? WaistToHem,decimal? FinishedHem,string? Unit)
    { public override string ToString()=>"A-line declaration (values hidden)"; }
    internal sealed record Issue(string Code,string Field);
    internal sealed class Report
    {
        internal Report(ImmutableArray<Issue> issues,MeasurementConsistencyAudit.Report? consistency) {Issues=issues;Consistency=consistency;}
        public ImmutableArray<Issue> Issues { get; } public MeasurementConsistencyAudit.Report? Consistency { get; }
        public bool HasBlockingIssues=>Issues.Length>0||Consistency is null||Consistency.HasBlockingIssues;
        public bool NeedsReview=>Consistency?.NeedsReview??false;
        public string Status=>"unresolved";public bool Accepted=>false;public bool ExportAllowed=>false;public bool ProductionAllowed=>false;public bool PersonalDataCollectionEnabled=>false;
        public void RequireResearchComposition()=>throw new InvalidOperationException("Input declarations are not physical or production approval");
    }
    internal sealed record SyntheticBinding(string Id,string Snapshot,int PanelCount,int RuleCount)
    {
        public string Status=>"unresolved";public bool Accepted=>false;public bool ExportAllowed=>false;public bool ProductionAllowed=>false;
        public void RequireResearchComposition()=>throw new InvalidOperationException("Synthetic binding is not a production garment");
    }
    static readonly ImmutableArray<string> Required=ResearchMeasurements.FieldsFor(ResearchMeasurements.Operation.PinnedGarment);
    internal static string DefinitionFor(string field) {
        if(!Required.Contains(field,StringComparer.Ordinal))throw new ArgumentException("Unsupported source field");
        return "garmentcode:d449629979028123a5c4dc9e732a2ec19b7fce31:"+field+":v1";
    }
    static string UnitFor(string field)=>field is "shoulder_incl" or "hip_inclination"?"degree":"cm";
    static (Report report,ImmutableArray<Measurement> entries) Analyze(IEnumerable<Measurement> input,string? model,ALine? lower)
    {
        ArgumentNullException.ThrowIfNull(input);
        var list=new List<Measurement>(MaxEntries);bool overflow;
        using(var e=input.GetEnumerator()) {while(list.Count<MaxEntries&&e.MoveNext())list.Add(e.Current);overflow=list.Count==MaxEntries&&e.MoveNext();}
        if(overflow)return (new Report(ImmutableArray.Create(new Issue("input_limit_exceeded","")),null),ImmutableArray<Measurement>.Empty);
        var issues=new List<Issue>();
        void Add(string code,string field="")=>issues.Add(new(code,field));
        if(string.IsNullOrWhiteSpace(model))Add("missing_model");
        else if(model is not (PinnedModel or ALineModel))Add("unsupported_model");
        if(model!=ALineModel&&lower is not null)Add("unexpected_aline_input","aline");
        if(model==ALineModel) {
            if(lower is null||lower.Unit!="cm")Add("aline_unit_required","aline");
            var inputs=new[]{("front_waist",lower?.FrontWaist,false),("back_waist",lower?.BackWaist,false),("front_hip",lower?.FrontHip,false),("back_hip",lower?.BackHip,false),("front_waist_ease",lower?.FrontWaistEase,true),("back_waist_ease",lower?.BackWaistEase,true),("front_hip_ease",lower?.FrontHipEase,true),("back_hip_ease",lower?.BackHipEase,true),("hip_depth",lower?.HipDepth,false),("waist_to_hem",lower?.WaistToHem,false),("finished_hem",lower?.FinishedHem,false)};
            foreach(var (key,value,ease) in inputs)if(value is null)Add("missing_aline_field",key);else if(value>1000||(ease?value<0:value<=0))Add("invalid_aline_field",key);
            bool Valid(decimal? x,bool ease=false)=>x is decimal v&&v<=1000&&(ease?v>=0:v>0);
            if(lower is not null&&lower.Unit=="cm") {
                if(Valid(lower.HipDepth)&&Valid(lower.WaistToHem)&&lower.HipDepth>=lower.WaistToHem)Add("aline_hip_depth_not_below_hem","hip_depth");
                void WaistHip(string label,decimal? waist,decimal? waistEase,decimal? hip,decimal? hipEase) {
                    if(Valid(waist)&&Valid(waistEase,true)&&Valid(hip)&&Valid(hipEase,true)&&waist+waistEase>hip+hipEase)Add("aline_waist_exceeds_hip",label);
                }
                WaistHip("front",lower.FrontWaist,lower.FrontWaistEase,lower.FrontHip,lower.FrontHipEase);
                WaistHip("back",lower.BackWaist,lower.BackWaistEase,lower.BackHip,lower.BackHipEase);
                if(Valid(lower.FrontHip)&&Valid(lower.FrontHipEase,true)&&Valid(lower.BackHip)&&Valid(lower.BackHipEase,true)&&Valid(lower.FinishedHem)&&lower.FinishedHem<=2*Math.Max((lower.FrontHip+lower.FrontHipEase)!.Value,(lower.BackHip+lower.BackHipEase)!.Value))Add("aline_hem_not_wider","finished_hem");
            }
            // This separate lower-only model is not mapped to the pinned source.
            Add("aline_source_mapping_unverified","aline");
        }
        var raw=new List<KeyValuePair<string,decimal?>>();
        foreach(var m in list) {
            if(m is null||m.Field is null||!Required.Contains(m.Field,StringComparer.Ordinal)) {Add("unknown_measurement");continue;}
            bool unit=m.Unit==UnitFor(m.Field);bool definition=m.DefinitionId==DefinitionFor(m.Field);
            if(!unit)Add("unit_mismatch",m.Field);
            if(!definition)Add("definition_mismatch",m.Field);
            raw.Add(new(m.Field,unit&&definition?m.Value:null));
        }
        var consistency=MeasurementConsistencyAudit.Assess(raw,"cm","degree");
        return (new Report(issues.OrderBy(x=>x.Code,StringComparer.Ordinal).ThenBy(x=>x.Field,StringComparer.Ordinal).ToImmutableArray(),consistency),list.ToImmutableArray());
    }
    internal static Report Assess(IEnumerable<Measurement> input,string? model,ALine? lower=null)=>Analyze(input,model,lower).report;
    internal static SyntheticBinding BindSynthetic(IEnumerable<Measurement> input,string id,string? model)
    {
        if(id is not ("synthetic-base" or "synthetic-distinct"))throw new ArgumentException("Unsupported synthetic fixture");
        var (report,entries)=Analyze(input,model,null);
        if(model!=PinnedModel||report.HasBlockingIssues||report.NeedsReview||report.Consistency is null||!report.Consistency.ReviewCoverageComplete||entries.Length!=20)throw new ArgumentException("Incomplete or unverified structured declaration");
        var expected=SyntheticBodyCatalog.Create(id);
        if(entries.Any(m=>m.Field is null||m.Value is null||m.Value.Value!=expected[m.Field]))throw new ArgumentException("Only exact synthetic measurements supported");
        var inputBody=ResearchMeasurements.Create(entries.Select(m=>new KeyValuePair<string,decimal>(m.Field!,m.Value!.Value)),"cm","degree").BuildPinnedGarment();
        var graph=BodySeamStudy.Calculate(inputBody,ResearchDesign.PinnedFitted);var topology=BodyCurveStudy.Calculate(inputBody,ResearchDesign.PinnedFitted);
        if(graph.Snapshot!=inputBody.Snapshot(ResearchDesign.PinnedFitted)||graph.Rules.Count!=23||topology.Count!=6||topology.Any(t=>t.Closure!=BodyCurveStudy.ClosureEvidence.ClosedRepresented||t.Unresolved!=0||t.NonMonotone!=0))throw new ArgumentException("Research geometry identity or represented topology mismatch");
        return new(id,graph.Snapshot,topology.Count,graph.Rules.Count);
    }
}
