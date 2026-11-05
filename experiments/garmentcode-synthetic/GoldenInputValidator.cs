using System.Text.Json;

namespace GarmentCodeSlice;

// For archived Python-generated fixture metadata only, never user input.
internal static class GoldenInputValidator
{
    internal const decimal FixtureTolerance=0.000000000001m;
    internal static Input Validate(JsonElement item)
    {
        string id=item.GetProperty("id").GetString() ?? throw new InvalidDataException("Missing fixture ID");
        decimal scale=id switch { "synthetic-base"=>1m,"synthetic-scaled"=>1.2m,_=>throw new InvalidDataException("Unknown fixture ID") };
        void Field(JsonElement obj,string name,decimal expected)
        {
            if(!obj.TryGetProperty(name,out var v) || v.ValueKind!=JsonValueKind.Number || !v.TryGetDecimal(out decimal actual) || Math.Abs(actual-expected)>FixtureTolerance)
                throw new InvalidDataException("Fixture input mismatch: "+name);
        }
        Field(item,"scale",scale);
        var body=item.GetProperty("body_cm"); var shirt=item.GetProperty("shirt"); var skirt=item.GetProperty("skirt");
        foreach(var (name,value) in new (string,decimal)[]{("bust",100m*scale),("back_width",44m*scale),("waist",80m*scale),("waist_back_width",36m*scale),("waist_line",40m*scale),("_shoulder_incl",12m),("height",170m*scale),("head_l",24m*scale),("shoulder_w",40m*scale),("hips",100m*scale),("hips_line",20m*scale),("hip_back_width",48m*scale),("_waist_level",106m*scale)}) Field(body,name,value);
        Field(shirt,"width",1.1m); Field(shirt,"flare",1m); Field(shirt,"length",1m);
        Field(skirt,"length_cm",60m*scale); Field(skirt,"flare_cm",12m*scale);
        // Normalize validated Python JSON metadata to the exact allowed decimals.
        return new Input(100m*scale,44m*scale,80m*scale,36m*scale,40m*scale,12m,1.1m,1m,1m,60m*scale,12m*scale);
    }
}
