using System.Collections.Immutable;
namespace FittedResearch;

// Research-only presence model. Missing values remain missing, never estimated.
// Presence does not imply anatomical validity or that a calculation is enabled.
internal sealed class ResearchMeasurements
{
    internal enum Operation { PrecutBackWaist,PrecutFrontBodice,CompleteCurrentBody }
    internal sealed record Requirements(Operation For,ImmutableArray<string> Missing)
    {
        public bool AllFieldsPresent=>Missing.Length==0;
        public bool CalculationEnabled=>false;
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
    internal Requirements Check(Operation operation)
    {
        ImmutableArray<string> needed=operation switch {
            Operation.PrecutBackWaist=>["back_width","waist_back_width","waist_line","shoulder_w","shoulder_incl","bum_points","bust_line","vert_bust_line"],
            Operation.PrecutFrontBodice=>["bust","back_width","waist","waist_back_width","bust_points","shoulder_w","shoulder_incl","waist_line","waist_over_bust_line","bust_line","vert_bust_line"],
            Operation.CompleteCurrentBody=>ResearchBody.Fields,
            _=>throw new ArgumentException("Unsupported operation.")
        };
        return new(operation,needed.Where(k=>!values.ContainsKey(k)).ToImmutableArray());
    }
    internal ResearchBody BuildComplete()
    {
        if(!Check(Operation.CompleteCurrentBody).AllFieldsPresent)throw new ArgumentException("Incomplete research body.");
        return ResearchBody.Create(values,"cm","degree");
    }
}
