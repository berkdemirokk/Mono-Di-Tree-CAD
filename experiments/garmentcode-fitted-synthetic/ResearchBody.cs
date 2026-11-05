using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace FittedResearch;

// Local-only input contract; decimal values are cm or degrees. This is not a
// fitting protocol or an authorization to process or export personal data.
internal interface IResearchPatternBody
{
    decimal this[string key] { get; }
    ResearchBody.DerivedValues Derive();
    string Snapshot(ResearchDesign design);
}

internal sealed class ResearchBody : IResearchPatternBody
{
    internal static readonly ImmutableArray<string> Fields=["arm_length","arm_pose_angle","armscye_depth","back_width","bum_points","bust","bust_line","bust_points","crotch_hip_diff","head_l","height","hip_back_width","hip_inclination","hips","hips_line","leg_circ","neck_w","shoulder_incl","shoulder_w","underbust","vert_bust_line","waist","waist_back_width","waist_line","waist_over_bust_line","wrist"];
    readonly ImmutableDictionary<string,decimal> values;
    private ResearchBody(ImmutableDictionary<string,decimal> input)=>values=input;
    internal decimal this[string key]=>values[key];
    decimal IResearchPatternBody.this[string key]=>this[key];
    DerivedValues IResearchPatternBody.Derive()=>Derive();
    string IResearchPatternBody.Snapshot(ResearchDesign design)=>Snapshot(design);
    internal static ResearchBody Create(IEnumerable<KeyValuePair<string,decimal>> input,string lengthUnit,string angleUnit)
    {
        ArgumentNullException.ThrowIfNull(input);
        if(lengthUnit!="cm" || angleUnit!="degree")throw new ArgumentException("Explicit cm and degree units required.");
        var b=ImmutableDictionary.CreateBuilder<string,decimal>(StringComparer.Ordinal);
        foreach(var pair in input) { if(!Fields.Contains(pair.Key,StringComparer.Ordinal)||!b.TryAdd(pair.Key,pair.Value))throw new ArgumentException("Unknown or duplicate field."); }
        if(b.Count!=Fields.Length || Fields.Any(k=>!b.ContainsKey(k)))throw new ArgumentException("All 26 measurements are required.");
        foreach(var pair in b)
        {
            bool angle=pair.Key is "arm_pose_angle" or "shoulder_incl" or "hip_inclination";
            if(angle ? pair.Value<0 || pair.Value>180 : pair.Value<=0 || pair.Value>1000)throw new ArgumentException("Measurement outside numerical limits.");
        }
        ValidatePatternScope(key=>b[key]);
        return new(b.ToImmutable());
    }
    internal static void RequireSupported(IResearchPatternBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if(body is not ResearchBody && !ResearchMeasurements.IsPartialPatternBody(body))throw new ArgumentException("Unsupported body implementation.");
    }
    internal static void ValidatePatternScope(Func<string,decimal> get)
    {
        if(get("shoulder_incl")>=45 || get("hip_inclination")>=89 || get("back_width")>=get("bust") || get("waist_back_width")>=get("waist") || get("hip_back_width")>=get("hips"))throw new ArgumentException("Unsupported or contradictory measurements.");
        // Scope restriction for this model, not a proof of an invalid person:
        // waist_line is a surface length and this sum is not a physical law.
        if(get("head_l")+get("waist_line")+get("hips_line")>=get("height"))
            throw new ArgumentException("Unsupported research scope: source-derived leg length is not positive.");
        // These source-derived lengths must remain positive, including after
        // conversion to binary64, before any candidate geometry is calculated.
        if(get("shoulder_w")<=2 || (double)get("shoulder_w")-2<=0 ||
           (double)get("height")-(double)get("head_l")-(double)get("waist_line")<=0 ||
           (double)get("height")-(double)get("head_l")-(double)get("waist_line")-(double)get("hips_line")<=0)
            throw new ArgumentException("Unsupported research scope: nonpositive source-derived length.");
    }
    internal DerivedValues Derive()
    {
        return DeriveValues(key=>values[key]);
    }
    internal static DerivedValues DeriveValues(Func<string,decimal> get)
    {
        double V(string key)=>(double)get(key);
        return new(V("height")-V("head_l")-V("waist_line"),V("height")-V("head_l")-V("waist_line")-V("hips_line"),V("shoulder_w")-2,
            (1-1d/3)*V("vert_bust_line")+V("bust_line")/3,V("hip_inclination")/2,V("shoulder_incl"),V("armscye_depth")+2.5);
    }
    internal string Snapshot(ResearchDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);var b=new StringBuilder("research-body-v1|").Append(design.ModelId).Append('|').Append(design.SourceCommit);
        foreach(string key in Fields) b.Append('|').Append(key).Append('=').Append(values[key].ToString("G29",CultureInfo.InvariantCulture));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(b.ToString())));
    }
    internal sealed record DerivedValues(double WaistLevel,double LegLength,double BaseSleeveBalance,double BustLine,double HipInclination,double ShoulderInclination,double ArmscyeDepth);
}

// The design is deliberately closed to one pinned research model. Additional
// design variants must have their own implemented/validated contract.
internal sealed class ResearchDesign
{
    private ResearchDesign() { }
    internal static ResearchDesign PinnedFitted { get; }=new();
    internal string ModelId=>"research_fitted_shirt_pencil_skirt_v1";
    internal string SourceCommit=>"d449629979028123a5c4dc9e732a2ec19b7fce31";
    internal bool ExportAllowed=>false;
}

internal static class SyntheticBodyCatalog
{
    internal static ResearchBody Create(string id)
    {
        var v=new Dictionary<string,decimal>(StringComparer.Ordinal) {
            ["arm_length"]=54,["arm_pose_angle"]=45,["armscye_depth"]=13,["back_width"]=48,["bum_points"]=18,["bust"]=100,["bust_line"]=26,["bust_points"]=17,["crotch_hip_diff"]=9,["head_l"]=26,["height"]=172,["hip_back_width"]=55,["hip_inclination"]=10,["hips"]=104,["hips_line"]=23,["leg_circ"]=60,["neck_w"]=19,["shoulder_incl"]=22,["shoulder_w"]=36,["underbust"]=86,["vert_bust_line"]=21,["waist"]=80,["waist_back_width"]=36,["waist_line"]=37,["waist_over_bust_line"]=41,["wrist"]=17 };
        switch(id)
        {
            case "synthetic-base":break;
            case "synthetic-distinct":v["bust"]=96;v["back_width"]=43;v["waist"]=74;v["waist_back_width"]=34;v["hips"]=102;v["hip_back_width"]=54;v["waist_line"]=39;v["waist_over_bust_line"]=42;break;
            case "synthetic-third":v["bust"]=104;v["back_width"]=50;v["waist"]=84;v["waist_back_width"]=38;v["waist_line"]=38;v["shoulder_incl"]=18;v["bum_points"]=19;v["bust_line"]=25;v["vert_bust_line"]=22;break;
            case "synthetic-small-difference":v["back_width"]=44;v["waist_back_width"]=38;v["waist_line"]=40;v["shoulder_incl"]=30;v["bum_points"]=18;v["bust_line"]=24;v["vert_bust_line"]=22;break;
            case "synthetic-fifth":v["bust"]=102;v["back_width"]=46;v["waist"]=78;v["waist_back_width"]=35;v["waist_line"]=38;v["shoulder_incl"]=15;v["bum_points"]=17;v["bust_line"]=25;v["vert_bust_line"]=20;break;
            default:throw new ArgumentException("Unsupported synthetic case");
        }
        return ResearchBody.Create(v,"cm","degree");
    }
}
