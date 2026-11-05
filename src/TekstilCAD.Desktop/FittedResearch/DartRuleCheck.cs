using System.Text.Json;

namespace FittedResearch;

// Numerical and structural checks over synthetic generated straight dart legs.
// No result here establishes physical closure or production suitability.
internal static class DartRuleCheck
{
    internal static void Validate(PartialDartRelations.Rule rule,JsonElement source,IReadOnlyList<PartialFittedEngine.Segment> segments)
    {
        if(source.GetProperty("owner").GetString()!=rule.Owner || source.GetProperty("index").GetInt32()!=rule.SourceRule) throw new InvalidDataException("Dart rule identity");
        void Side(string name,int expected,bool flip)
        {
            var side=source.GetProperty(name);var edges=side.GetProperty("edges");
            if(edges.GetArrayLength()!=1) throw new InvalidDataException("Dart side edge count");
            var e=edges[0];
            if(e.GetProperty("panel").GetString()!=rule.Owner || e.GetProperty("edge").GetInt32()!=expected || e.GetProperty("flipped").GetBoolean()!=flip || e.GetProperty("right_wrong").GetBoolean()) throw new InvalidDataException("Dart edge identity or fabric side");
            var r=side.GetProperty("ruffle");
            if(r.GetArrayLength()!=1 || r[0].GetProperty("coeff").GetDouble()!=1 || r[0].GetProperty("sec")[0].GetInt32()!=0 || r[0].GetProperty("sec")[1].GetInt32()!=1) throw new InvalidDataException("Dart ruffle semantics");
        }
        Side("a",rule.EdgeA,rule.FlipA); Side("b",rule.EdgeB,rule.FlipB);
        var a=segments.Single(s=>s.Panel==rule.Owner && s.Edge==rule.EdgeA);
        var b=segments.Single(s=>s.Panel==rule.Owner && s.Edge==rule.EdgeB);
        double D(PartialFittedEngine.P p,PartialFittedEngine.P q) { double x=p.X-q.X,y=p.Y-q.Y;return Math.Sqrt(x*x+y*y); }
        if(a.SourceKind!="Edge" || b.SourceKind!="Edge" || D(rule.ApexAtStartA?a.Start:a.End,rule.ApexAtStartB?b.Start:b.End)>1e-9 || Math.Abs(D(a.Start,a.End)-D(b.Start,b.End))>1e-9) throw new InvalidDataException("Dart legs do not meet numerical check");
    }
}
