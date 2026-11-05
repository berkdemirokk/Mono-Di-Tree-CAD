using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
namespace FittedResearch;

// Research-only presence model. Missing values remain missing, never estimated.
// Presence does not imply anatomical validity or production authorization.
internal sealed class ResearchMeasurements
{
    internal enum Operation { PrecutBackWaist,PrecutFrontBodice,PinnedGarment,CompleteCurrentBody }
    internal sealed record Requirements(Operation For,ImmutableArray<string> Missing)
    {
        public bool AllFieldsPresent=>Missing.Length==0;
        public bool CanAttemptPartialCalculation=>AllFieldsPresent && (For is Operation.PrecutBackWaist or Operation.PrecutFrontBodice or Operation.PinnedGarment);
        public string Status=>"unresolved";
    }
    private readonly ImmutableDictionary<string,decimal> values;
    private ResearchMeasurements(ImmutableDictionary<string,decimal> input)=>values=input;
    internal static ResearchMeasurements Create(IEnumerable<KeyValuePair<string,decimal>> input,string lengthUnit,string angleUnit)
    {
        ArgumentNullException.ThrowIfNull(input);
        if(lengthUnit!="cm"||angleUnit!="degree")throw new ArgumentException("Explicit cm and degree units required.");
        var builder=ImmutableDictionary.CreateBuilder<string,decimal>(StringComparer.Ordinal);
        foreach(var pair in input) {
            if(!ResearchBody.Fields.Contains(pair.Key,StringComparer.Ordinal)||!builder.TryAdd(pair.Key,pair.Value))throw new ArgumentException("Unknown or duplicate field.");
            bool angle=pair.Key is "arm_pose_angle" or "shoulder_incl" or "hip_inclination";
            if(angle ? pair.Value<0||pair.Value>180 : pair.Value<=0||pair.Value>1000)throw new ArgumentException("Value outside technical limits.");
        }
        return new(builder.ToImmutable());
    }
    static ImmutableArray<string> FieldsFor(Operation operation) => operation switch {
            Operation.PrecutBackWaist=>["back_width","waist_back_width","waist_line","shoulder_w","shoulder_incl","bum_points","bust_line","vert_bust_line"],
            Operation.PrecutFrontBodice=>["bust","back_width","waist","waist_back_width","bust_points","shoulder_w","shoulder_incl","waist_line","waist_over_bust_line","bust_line","vert_bust_line"],
            Operation.PinnedGarment=>["bust","back_width","waist","waist_back_width","bust_points","shoulder_w","shoulder_incl","waist_line","waist_over_bust_line","bust_line","vert_bust_line","bum_points","neck_w","armscye_depth","hips","hip_back_width","hips_line","height","head_l","hip_inclination"],
            Operation.CompleteCurrentBody=>ResearchBody.Fields,
            _=>throw new ArgumentException("Unsupported operation.")
        };
    internal Requirements Check(Operation operation) => new(operation,FieldsFor(operation).Where(k=>!values.ContainsKey(k)).ToImmutableArray());
    internal double Value(string field) => values.TryGetValue(field,out var value) ? (double)value : throw new ArgumentException("Missing measurement: "+field);
    internal string SnapshotFor(Operation operation,ResearchDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);if(!Check(operation).AllFieldsPresent)throw new ArgumentException("Missing operation measurements.");
        var text=new StringBuilder("partial-research-v1|").Append(operation).Append('|').Append(design.ModelId).Append('|').Append(design.SourceCommit);
        foreach(var field in FieldsFor(operation))text.Append('|').Append(field).Append('=').Append(values[field].ToString("G29",CultureInfo.InvariantCulture));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }
    internal IResearchPatternBody BuildPinnedGarment()
    {
        var fields=FieldsFor(Operation.PinnedGarment);
        if(!Check(Operation.PinnedGarment).AllFieldsPresent)throw new ArgumentException("Missing garment measurements.");
        var selected=fields.ToImmutableDictionary(k=>k,k=>values[k],StringComparer.Ordinal);
        ResearchBody.ValidatePatternScope(k=>selected[k]);
        return new PartialPatternBody(selected);
    }
    internal static bool IsPartialPatternBody(IResearchPatternBody value)=>value is PartialPatternBody;
    private sealed class PartialPatternBody : IResearchPatternBody
    {
        readonly ImmutableDictionary<string,decimal> selected;
        internal PartialPatternBody(ImmutableDictionary<string,decimal> input)=>selected=input;
        public decimal this[string key]=>selected.TryGetValue(key,out var value)?value:throw new ArgumentException("Measurement not in garment operation: "+key);
        public ResearchBody.DerivedValues Derive()=>ResearchBody.DeriveValues(key=>this[key]);
        public string Snapshot(ResearchDesign design)
        {
            ArgumentNullException.ThrowIfNull(design);
            var text=new StringBuilder("partial-garment-v1|").Append(design.ModelId).Append('|').Append(design.SourceCommit);
            foreach(var key in FieldsFor(Operation.PinnedGarment))text.Append('|').Append(key).Append('=').Append(selected[key].ToString("G29",CultureInfo.InvariantCulture));
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
        }
    }
    internal ResearchBody BuildComplete()
    {
        if(!Check(Operation.CompleteCurrentBody).AllFieldsPresent)throw new ArgumentException("Incomplete research body.");
        return ResearchBody.Create(values,"cm","degree");
    }
}
