using System.Text.Json;
namespace FittedResearch;

// Fixed synthetic finite differences only. Snapshot equality is not evidence
// of physical correctness or of equivalence to the Python optimizer.
internal static class SyntheticDependencyProbe
{
    internal sealed record Result(string Field,decimal Change,IReadOnlyList<string> ChangedPanels,bool SnapshotChanged)
    {
        public string Status=>"unresolved";public bool Accepted=>false;public bool ExportAllowed=>false;public bool ProductionAllowed=>false;
    }
    static Dictionary<string,BodyCurveStudy.Edge[]> Panels(IResearchPatternBody body)
    {
        var design=ResearchDesign.PinnedFitted;
        var fr=FrontBodiceCandidate.CreateFromBody(body,design);var fl=FrontBodiceCandidate.CreateLeftFromBody(body,design);
        var br=BackBodiceCandidate.CreateFromBody(body,design,false);var bl=BackBodiceCandidate.CreateFromBody(body,design,true);
        var sf=FrontSkirtCandidate.CreateFromMatchedBody(body,design);var sb=BackSkirtCandidate.CreateFromBody(body,design);
        BodyCurveStudy.Edge F(string panel,FrontBodiceCandidate.Edge e)=>new(panel,e.Id,e.Kind,e.Start.X,e.Start.Y,e.End.X,e.End.Y,e.Controls.Select(c=>(c.X,c.Y)).ToArray(),e.ArcControlY);
        BodyCurveStudy.Edge B(string panel,BackBodiceCandidate.Edge e)=>new(panel,e.Id,e.Kind,e.Start.X,e.Start.Y,e.End.X,e.End.Y,e.Controls.Select(c=>(c.X,c.Y)).ToArray(),e.ArcControlY);
        BodyCurveStudy.Edge S(FrontSkirtCandidate.Edge e)=>new("skirt_front",e.Id,e.Kind,e.Start.X,e.Start.Y,e.End.X,e.End.Y,e.Control is {} c?new[]{(c.X,c.Y)}:[],null);
        BodyCurveStudy.Edge T(BackSkirtCandidate.Edge e)=>new("skirt_back",e.Id,e.Kind,e.Start.X,e.Start.Y,e.End.X,e.End.Y,e.Control is {} c?new[]{(c.X,c.Y)}:[],null);
        return new(StringComparer.Ordinal) {
            ["right_ftorso"]=fr.Edges.Select(e=>F("right_ftorso",e)).ToArray(),["left_ftorso"]=fl.Edges.Select(e=>F("left_ftorso",e)).ToArray(),
            ["right_btorso"]=br.Edges.Select(e=>B("right_btorso",e)).ToArray(),["left_btorso"]=bl.Edges.Select(e=>B("left_btorso",e)).ToArray(),
            ["skirt_front"]=sf.Edges.Select(S).ToArray(),["skirt_back"]=sb.Edges.Select(T).ToArray()
        };
    }
    static bool Equal(BodyCurveStudy.Edge a,BodyCurveStudy.Edge b)=>a.Panel==b.Panel&&a.Id==b.Id&&a.Kind==b.Kind&&a.Ax==b.Ax&&a.Ay==b.Ay&&a.Bx==b.Bx&&a.By==b.By&&a.ArcY==b.ArcY&&a.RelativeControls.SequenceEqual(b.RelativeControls);
    internal static IReadOnlyList<Result> Run()
    {
        var fields=ResearchMeasurements.FieldsFor(ResearchMeasurements.Operation.PinnedGarment);var source=SyntheticBodyCatalog.Create("synthetic-base");
        var original=fields.Select(k=>new KeyValuePair<string,decimal>(k,source[k])).ToArray();
        IResearchPatternBody Build(IEnumerable<KeyValuePair<string,decimal>> data)=>ResearchMeasurements.Create(data,"cm","degree").BuildPinnedGarment();
        var baseline=Build(original);var prior=Panels(baseline);var output=new List<Result>();
        foreach(var field in fields) {
            decimal delta=field is "shoulder_incl" or "hip_inclination"?1m:.1m;
            var modified=original.Select(x=>x.Key==field?new KeyValuePair<string,decimal>(x.Key,x.Value+delta):x).ToArray();
            // Unsupported perturbations are not treated as evidence of zero effect.
            var changed=Build(modified);var after=Panels(changed);
            var differing=prior.Keys.OrderBy(x=>x,StringComparer.Ordinal).Where(k=>prior[k].Length!=after[k].Length||prior[k].Zip(after[k]).Any(p=>!Equal(p.First,p.Second))).ToArray();
            output.Add(new(field,delta,Array.AsReadOnly(differing),baseline.Snapshot(ResearchDesign.PinnedFitted)!=changed.Snapshot(ResearchDesign.PinnedFitted)));
        }
        return output.AsReadOnly();
    }
    internal static void CheckArtifactIdentity(string text, string expectedHash)
    {
        var normalized=text.TrimStart('\uFEFF').Replace("\r\n", "\n");
        var actual=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(normalized)));
        if (!StringComparer.Ordinal.Equals(actual,expectedHash)) throw new InvalidDataException("Dependency artifact identity mismatch");
    }
    internal const string TraceHash="4FE20E00F8F4F38307A6F43BB4C61F03804C7B88A18F5EDC9CBB450566C6216E";
    internal const string GoldenHash="7FC3C5DCF7AD38E33BC37AB6466290FE28E17CC375AFF2AF5F61D8CE66D4A207";
    internal static void CompareSource(IReadOnlyList<Result> probe,JsonElement trace)
    {
        void Need(bool ok,string message) {if(!ok)throw new InvalidDataException(message);}
        Need(trace.GetProperty("source_commit").GetString()==ResearchDesign.PinnedFitted.SourceCommit&&trace.GetProperty("fixture").GetString()=="synthetic-base","source dependency identity");
        var panels=new[]{"left_btorso","left_ftorso","right_btorso","right_ftorso","skirt_back","skirt_front"};
        var baselines=trace.GetProperty("baseline_contour_equal_to_golden");
        Need(panels.All(p=>baselines.GetProperty(p).GetBoolean())&&baselines.EnumerateObject().Count()==6,"source dependency baseline");
        var source=trace.GetProperty("cases").EnumerateArray().ToArray();
        Need(probe.Count==20&&source.Length==20&&source.Select(x=>x.GetProperty("field").GetString()).Distinct(StringComparer.Ordinal).Count()==20,"source dependency case count");
        for(int i=0;i<probe.Count;i++) {
            var row=probe[i];var reference=source[i];
            Need(reference.GetProperty("field").GetString()==row.Field&&decimal.TryParse(reference.GetProperty("delta").GetString(),System.Globalization.NumberStyles.Number,System.Globalization.CultureInfo.InvariantCulture,out var delta)&&delta==row.Change,"source dependency field or delta");
            var sourcePanels=reference.GetProperty("changed_panels").EnumerateArray().Select(x=>x.GetString()!).ToArray();
            Need(sourcePanels.Distinct(StringComparer.Ordinal).Count()==sourcePanels.Length&&sourcePanels.All(p=>panels.Contains(p,StringComparer.Ordinal)),"source dependency panels");
            var extra=sourcePanels.Except(row.ChangedPanels,StringComparer.Ordinal).ToArray();var missing=row.ChangedPanels.Except(sourcePanels,StringComparer.Ordinal).ToArray();
            bool expectedMicro=row.Field is "bust_points" or "hips";
            Need(missing.Length==0&&(!expectedMicro?extra.Length==0:extra.Length==1&&extra[0]=="skirt_back"),"source vs C# dependency set");
            if(expectedMicro) {
                var d=reference.GetProperty("panel_deltas").GetProperty("skirt_back");double cm=d.GetProperty("endpoint_max_cm").GetDouble(),controls=d.GetProperty("control_max").GetDouble();
                Need(!d.GetProperty("structure_changed").GetBoolean()&&double.IsFinite(cm)&&cm>0&&cm<1e-12&&controls==0,"source-only microscopic dependency changed");
            }
        }
    }
}
