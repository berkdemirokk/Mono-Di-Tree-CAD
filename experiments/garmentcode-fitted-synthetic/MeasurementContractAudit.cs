using System.Collections.Immutable;
namespace FittedResearch;

// Research-only structured diagnostics. Decimal inputs are already parsed;
// no parsing, guessing, logging, persistence, UI or person-data collection.
internal static class MeasurementContractAudit
{
    internal enum Kind { Blocking, Unverified }
    internal sealed record Issue(string Code,Kind Severity,ImmutableArray<string> Fields);
    internal sealed class Report
    {
        internal Report(ImmutableArray<Issue> issues) { Issues=issues; }
        public ImmutableArray<Issue> Issues { get; }
        public bool HasBlockingIssues=>Issues.Any(i=>i.Severity==Kind.Blocking);
        public string Status=>"unresolved";
        public bool PersonalDataCollectionEnabled=>false;
        public bool ProductionAllowed=>false;
        public bool ExportAllowed=>false;
    }
    internal static Report Assess(IEnumerable<KeyValuePair<string,decimal?>> input,string? lengthUnit,string? angleUnit)
    {
        ArgumentNullException.ThrowIfNull(input);
        var issues=new List<Issue>();
        void Add(string code,Kind kind,params string[] fields)=>issues.Add(new(code,kind,fields.ToImmutableArray()));
        if(lengthUnit!="cm")Add("length_unit_not_cm",Kind.Blocking);
        if(angleUnit!="degree")Add("angle_unit_not_degree",Kind.Blocking);
        var required=ResearchMeasurements.FieldsFor(ResearchMeasurements.Operation.PinnedGarment);
        var seen=new HashSet<string>(StringComparer.Ordinal);var repeated=new HashSet<string>(StringComparer.Ordinal);
        var values=new Dictionary<string,decimal?>(StringComparer.Ordinal);
        foreach(var pair in input) {
            if(pair.Key is null || !ResearchBody.Fields.Contains(pair.Key,StringComparer.Ordinal)) { Add("unknown_or_derived_field",Kind.Blocking);continue; }
            if(!seen.Add(pair.Key)) { if(repeated.Add(pair.Key))Add("duplicate_field",Kind.Blocking,pair.Key);values.Remove(pair.Key);continue; }
            values.Add(pair.Key,pair.Value);
            if(pair.Value is { } supplied) {
                bool angle=pair.Key is "arm_pose_angle" or "shoulder_incl" or "hip_inclination";
                if(angle ? supplied<0||supplied>180 : supplied<=0||supplied>1000)
                    Add("outside_technical_range",Kind.Blocking,pair.Key);
            }
        }
        foreach(var key in required) {
            if(!values.TryGetValue(key,out var value)||!value.HasValue) Add("missing_measurement",Kind.Blocking,key);
        }
        // Evaluate relations only when all required inputs pass the basic
        // technical contract; never infer a missing or invalid value.
        if(!issues.Any(i=>i.Severity==Kind.Blocking)) {
            decimal V(string key)=>values[key]!.Value;
            if(V("back_width")>=V("bust"))Add("unsupported_back_bust_relation",Kind.Blocking,"back_width","bust");
            if(V("waist_back_width")>=V("waist"))Add("unsupported_back_waist_relation",Kind.Blocking,"waist_back_width","waist");
            if(V("hip_back_width")>=V("hips"))Add("unsupported_back_hip_relation",Kind.Blocking,"hip_back_width","hips");
            if(V("shoulder_incl")>=45)Add("unsupported_shoulder_angle",Kind.Blocking,"shoulder_incl");
            if(V("hip_inclination")>=89)Add("unsupported_hip_angle",Kind.Blocking,"hip_inclination");
            if(V("shoulder_w")<=2 || (double)V("shoulder_w")-2<=0)Add("unsupported_sleeve_balance",Kind.Blocking,"shoulder_w");
            decimal sum=V("head_l")+V("waist_line")+V("hips_line");
            double waist=(double)V("height")-(double)V("head_l")-(double)V("waist_line");
            double leg=waist-(double)V("hips_line");
            if(sum>=V("height") || waist<=0 || leg<=0)Add("unsupported_derived_length",Kind.Blocking,"height","head_l","waist_line","hips_line");
        }
        // No state is a validated collection protocol, even with no blockers.
        Add("measurement_protocol_unverified",Kind.Unverified);
        Add("landmarks_and_paths_unverified",Kind.Unverified,"neck_w","bum_points","shoulder_w","shoulder_incl","hip_inclination");
        Add("fit_and_physical_proof_missing",Kind.Unverified);
        return new(issues.ToImmutableArray());
    }
}
