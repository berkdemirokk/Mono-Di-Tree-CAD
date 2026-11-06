using System.Collections.Immutable;
namespace FittedResearch;

// Structured, already-parsed research input only. Review findings express
// conditional relationships, not diagnoses, anatomical validity, or fit.
internal static class MeasurementConsistencyAudit
{
    internal sealed record Review(string Code,ImmutableArray<string> Fields);
    internal sealed class Report
    {
        internal Report(ImmutableArray<MeasurementContractAudit.Issue> contract,ImmutableArray<Review> review,ImmutableArray<string> skipped) {ContractIssues=contract;ReviewIssues=review;SkippedReviewCodes=skipped;}
        public ImmutableArray<MeasurementContractAudit.Issue> ContractIssues { get; }
        public ImmutableArray<Review> ReviewIssues { get; }
        public ImmutableArray<string> SkippedReviewCodes { get; }
        public bool HasBlockingIssues=>ContractIssues.Any(i=>i.Severity==MeasurementContractAudit.Kind.Blocking);
        public bool NeedsReview=>ReviewIssues.Length>0;
        public bool ReviewCoverageComplete=>SkippedReviewCodes.Length==0;
        public string Status=>"unresolved";public bool Accepted=>false;
        public bool PersonalDataCollectionEnabled=>false;public bool ProductionAllowed=>false;public bool ExportAllowed=>false;
        public void RequireResearchComposition()=>throw new InvalidOperationException("Measurement consistency is not fit or collection authorization");
    }
    internal static Report Assess(IEnumerable<KeyValuePair<string,decimal?>> input,string? lengthUnit,string? angleUnit)
    {
        ArgumentNullException.ThrowIfNull(input);
        var pairs=input.ToArray();var original=MeasurementContractAudit.Assess(pairs,lengthUnit,angleUnit);
        var contract=original.Issues.ToList();
        var values=new Dictionary<string,decimal>(StringComparer.Ordinal);
        foreach(var group in pairs.Where(p=>p.Key is not null).GroupBy(p=>p.Key,StringComparer.Ordinal)) {
            var entries=group.ToArray();
            bool angle=group.Key is "arm_pose_angle" or "shoulder_incl" or "hip_inclination";
            if(entries.Length==1&&entries[0].Value is decimal v&&(angle?v>=0&&v<=180:v>0&&v<=1000))values[group.Key]=v;
        }
        // Existing audit checks these scope predicates only after the whole
        // basic contract passes. Preserve the same predicates while allowing
        // independent fields to be diagnosed when unrelated fields are absent.
        void Block(string code,params string[] fields) {
            if(!contract.Any(i=>i.Code==code&&i.Fields.SequenceEqual(fields)))
                contract.Add(new(code,MeasurementContractAudit.Kind.Blocking,fields.ToImmutableArray()));
        }
        void ScopePair(string code,string a,string b) {
            if(lengthUnit=="cm"&&values.TryGetValue(a,out var x)&&values.TryGetValue(b,out var y)&&x>=y)Block(code,a,b);
        }
        ScopePair("unsupported_back_bust_relation","back_width","bust");
        ScopePair("unsupported_back_waist_relation","waist_back_width","waist");
        ScopePair("unsupported_back_hip_relation","hip_back_width","hips");
        if(angleUnit=="degree"&&values.TryGetValue("shoulder_incl",out var shoulderAngle)&&shoulderAngle>=45)Block("unsupported_shoulder_angle","shoulder_incl");
        if(angleUnit=="degree"&&values.TryGetValue("hip_inclination",out var hipAngle)&&hipAngle>=89)Block("unsupported_hip_angle","hip_inclination");
        if(lengthUnit=="cm"&&values.TryGetValue("shoulder_w",out var sw)&&(sw<=2||(double)sw-2<=0))Block("unsupported_sleeve_balance","shoulder_w");
        if(lengthUnit=="cm"&&values.TryGetValue("height",out var height)&&values.TryGetValue("head_l",out var head)&&values.TryGetValue("waist_line",out var waist)&&values.TryGetValue("hips_line",out var hips)) {
            double level=(double)height-(double)head-(double)waist;
            if(head+waist+hips>=height||level<=0||level-(double)hips<=0)Block("unsupported_derived_length","height","head_l","waist_line","hips_line");
        }
        var review=new List<Review>();var skipped=new List<string>();
        void Check(string code,string first,string second,Func<decimal,decimal,bool> relation) {
            // Never compare lengths without the requested explicit unit or
            // when either particular field is missing, repeated or invalid.
            if(lengthUnit!="cm"||!values.TryGetValue(first,out var a)||!values.TryGetValue(second,out var b)) {skipped.Add(code);return;}
            if(relation(a,b))review.Add(new(code,ImmutableArray.Create(first,second)));
        }
        Check("review_bust_points_vs_circumference","bust_points","bust",(a,b)=>2*a>b);
        Check("review_bum_points_vs_circumference","bum_points","hips",(a,b)=>2*a>b);
        Check("review_front_path_order","bust_line","waist_over_bust_line",(a,b)=>a>b);
        Check("review_head_vs_height","head_l","height",(a,b)=>a>b);
        Check("review_vertical_bust_vs_height","vert_bust_line","height",(a,b)=>a>b);
        Check("review_hips_line_vs_height","hips_line","height",(a,b)=>a>b);
        Check("review_neck_vs_shoulders","neck_w","shoulder_w",(a,b)=>a>b);
        Check("review_armhole_vs_back_length","armscye_depth","waist_line",(a,b)=>a>b);
        return new(contract.OrderBy(i=>i.Code,StringComparer.Ordinal).ThenBy(i=>string.Join("\u001f",i.Fields),StringComparer.Ordinal).ToImmutableArray(),review.OrderBy(i=>i.Code,StringComparer.Ordinal).ToImmutableArray(),skipped.OrderBy(x=>x,StringComparer.Ordinal).ToImmutableArray());
    }
}
