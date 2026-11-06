namespace FittedResearch;
internal static class StructuredGarmentInputTests
{
    internal static void Verify()
    {
        var fields=ResearchMeasurements.FieldsFor(ResearchMeasurements.Operation.PinnedGarment);
        StructuredGarmentInput.Measurement[] Fixture(string id) {
            var b=SyntheticBodyCatalog.Create(id);
            return fields.Select(k=>new StructuredGarmentInput.Measurement(k,b[k],k is "shoulder_incl" or "hip_inclination"?"degree":"cm",StructuredGarmentInput.DefinitionFor(k))).ToArray();
        }
        void Need(bool condition,string error) {if(!condition)throw new InvalidDataException(error);}
        bool Issue(StructuredGarmentInput.Report report,string code,string? field=null)=>report.Issues.Any(i=>i.Code==code&&(field is null||i.Field==field));
        bool Contract(StructuredGarmentInput.Report report,string code,string? field=null)=>report.Consistency?.ContractIssues.Any(i=>i.Code==code&&(field is null||i.Fields.Contains(field,StringComparer.Ordinal)))??false;
        void RejectBinding(IEnumerable<StructuredGarmentInput.Measurement> entries,string id="synthetic-base",string? model=StructuredGarmentInput.PinnedModel) {try {StructuredGarmentInput.BindSynthetic(entries,id,model);throw new Exception("Invalid declaration bound");}catch(ArgumentException) { }}
        var data=Fixture("synthetic-base");
        Need(fields.Length==20&&fields.Distinct().Count()==20&&data.Select(x=>x.DefinitionId).Distinct().Count()==20,"structured field definitions");
        foreach(string id in new[]{"synthetic-base","synthetic-distinct"}) {
            var samples=Fixture(id);var report=StructuredGarmentInput.Assess(samples,StructuredGarmentInput.PinnedModel);
            Need(!report.HasBlockingIssues&&!report.NeedsReview&&report.Consistency is {ReviewCoverageComplete:true}&&!report.Accepted&&!report.ExportAllowed&&!report.ProductionAllowed&&!report.PersonalDataCollectionEnabled&&report.Status=="unresolved","structured synthetic report");
            try {report.RequireResearchComposition();throw new Exception("Structured report composed");}catch(InvalidOperationException) { }
            var bound=StructuredGarmentInput.BindSynthetic(samples,id,StructuredGarmentInput.PinnedModel);
            Need(bound.Id==id&&bound.PanelCount==6&&bound.RuleCount==23&&bound.Snapshot.Length==64&&!bound.Accepted&&!bound.ExportAllowed&&!bound.ProductionAllowed&&bound.Status=="unresolved","synthetic geometry binding");
            var expected=ResearchMeasurements.Create(samples.Select(x=>new KeyValuePair<string,decimal>(x.Field!,x.Value!.Value)),"cm","degree").BuildPinnedGarment();
            Need(bound.Snapshot==expected.Snapshot(ResearchDesign.PinnedFitted),"structured values detached from geometry snapshot");
            try {bound.RequireResearchComposition();throw new Exception("Structured binding composed");}catch(InvalidOperationException) { }
            var reordered=StructuredGarmentInput.BindSynthetic(samples.Reverse(),id,StructuredGarmentInput.PinnedModel);
            Need(bound.Snapshot==reordered.Snapshot,"input order changed body snapshot");
        }
        foreach(var entry in data) {
            var missing=data.Where(x=>x.Field!=entry.Field).ToArray();var report=StructuredGarmentInput.Assess(missing,StructuredGarmentInput.PinnedModel);
            Need(Contract(report,"missing_measurement",entry.Field)&&report.HasBlockingIssues,"missing field "+entry.Field);RejectBinding(missing);
            var wrongUnit=data.Select(x=>x==entry?x with {Unit=x.Unit=="cm"?"degree":"cm"}:x).ToArray();
            Need(Issue(StructuredGarmentInput.Assess(wrongUnit,StructuredGarmentInput.PinnedModel),"unit_mismatch",entry.Field),"wrong unit "+entry.Field);RejectBinding(wrongUnit);
            var wrongDefinition=data.Select(x=>x==entry?x with {DefinitionId="other"}:x).ToArray();
            Need(Issue(StructuredGarmentInput.Assess(wrongDefinition,StructuredGarmentInput.PinnedModel),"definition_mismatch",entry.Field),"wrong definition "+entry.Field);RejectBinding(wrongDefinition);
            var dupe=data.Append(entry).ToArray();Need(Contract(StructuredGarmentInput.Assess(dupe,StructuredGarmentInput.PinnedModel),"duplicate_field",entry.Field),"duplicate field "+entry.Field);RejectBinding(dupe);
            var nullValue=data.Select(x=>x==entry?x with {Value=null}:x).ToArray();Need(Contract(StructuredGarmentInput.Assess(nullValue,StructuredGarmentInput.PinnedModel),"missing_measurement",entry.Field),"null field "+entry.Field);RejectBinding(nullValue);
        }
        Need(Issue(StructuredGarmentInput.Assess(data,null),"missing_model"),"missing design choice");RejectBinding(data,model:null);
        Need(Issue(StructuredGarmentInput.Assess(data,"unknown"),"unsupported_model"),"unknown design choice");
        var foreign=data.Select(m=>m.Field=="bust"?m with {Value=100.0000000000000001m}:m).ToArray();RejectBinding(foreign);
        var swappedDefinition=data.Select(m=>m.Field=="bust"?m with {DefinitionId=StructuredGarmentInput.DefinitionFor("waist")}:m).ToArray();
        Need(Issue(StructuredGarmentInput.Assess(swappedDefinition,StructuredGarmentInput.PinnedModel),"definition_mismatch","bust"),"foreign valid definition accepted");RejectBinding(swappedDefinition);
        var extreme=data.Select(m=>m.Field=="bust"?m with {Value=decimal.MaxValue}:m).ToArray();
        Need(Contract(StructuredGarmentInput.Assess(extreme,StructuredGarmentInput.PinnedModel),"outside_technical_range","bust"),"extreme value accepted");RejectBinding(extreme);
        RejectBinding(data,"synthetic-distinct");RejectBinding(data,"unknown");
        var extra=data.Append(new StructuredGarmentInput.Measurement("wrist",17,"cm","x"));Need(Issue(StructuredGarmentInput.Assess(extra,StructuredGarmentInput.PinnedModel),"unknown_measurement"),"out-of-scope field accepted");
        Need(Issue(StructuredGarmentInput.Assess(data.Append(null!),StructuredGarmentInput.PinnedModel),"unknown_measurement"),"null entry accepted");
        var lowerMissing=StructuredGarmentInput.Assess(data,StructuredGarmentInput.ALineModel);
        Need(lowerMissing.Issues.Count(i=>i.Code=="missing_aline_field")==11&&Issue(lowerMissing,"aline_unit_required")&&Issue(lowerMissing,"aline_source_mapping_unverified"),"A-line missing information");
        var lower=new StructuredGarmentInput.ALine(36,36,50,50,0,0,0,0,20,70,160,"cm");
        var aLine=StructuredGarmentInput.Assess(data,StructuredGarmentInput.ALineModel,lower);
        Need(!aLine.Issues.Any(i=>i.Code is "missing_aline_field" or "invalid_aline_field" or "aline_unit_required")&&Issue(aLine,"aline_source_mapping_unverified")&&aLine.HasBlockingIssues,"A-line source mapping must remain unresolved");
        var badLower=StructuredGarmentInput.Assess(data,StructuredGarmentInput.ALineModel,lower with {BackHipEase=-1,WaistToHem=0,Unit="mm"});
        Need(Issue(badLower,"invalid_aline_field","back_hip_ease")&&Issue(badLower,"invalid_aline_field","waist_to_hem")&&Issue(badLower,"aline_unit_required"),"A-line field boundaries");
        Need(!Issue(StructuredGarmentInput.Assess(data,StructuredGarmentInput.ALineModel,lower with {HipDepth=69.999m}),"aline_hip_depth_not_below_hem")&&Issue(StructuredGarmentInput.Assess(data,StructuredGarmentInput.ALineModel,lower with {HipDepth=70}),"aline_hip_depth_not_below_hem"),"A-line hip/hem boundary");
        Need(!Issue(StructuredGarmentInput.Assess(data,StructuredGarmentInput.ALineModel,lower with {FrontWaist=50}),"aline_waist_exceeds_hip","front")&&Issue(StructuredGarmentInput.Assess(data,StructuredGarmentInput.ALineModel,lower with {FrontWaist=50.001m}),"aline_waist_exceeds_hip","front"),"A-line waist/hip boundary");
        Need(Issue(StructuredGarmentInput.Assess(data,StructuredGarmentInput.ALineModel,lower with {FinishedHem=100}),"aline_hem_not_wider")&&!Issue(StructuredGarmentInput.Assess(data,StructuredGarmentInput.ALineModel,lower with {FinishedHem=100.001m}),"aline_hem_not_wider"),"A-line hem boundary");
        Need(!Issue(StructuredGarmentInput.Assess(data,StructuredGarmentInput.ALineModel,lower with {FrontWaist=60,Unit="mm"}),"aline_waist_exceeds_hip"),"A-line wrong unit compared");
        var partialLower=StructuredGarmentInput.Assess(data,StructuredGarmentInput.ALineModel,lower with {FrontWaist=null,FrontHip=20});
        Need(Issue(partialLower,"missing_aline_field","front_waist")&&!Issue(partialLower,"aline_waist_exceeds_hip","front"),"A-line missing comparison dependency");
        Need(Issue(StructuredGarmentInput.Assess(data,StructuredGarmentInput.PinnedModel,lower),"unexpected_aline_input"),"unused lower input");
        int visited=0;bool disposed=false;
        IEnumerable<StructuredGarmentInput.Measurement> Infinite() {try {while(true){visited++;yield return data[0];}}finally {disposed=true;}}
        var overflow=StructuredGarmentInput.Assess(Infinite(),StructuredGarmentInput.PinnedModel);
        Need(visited==41&&disposed&&Issue(overflow,"input_limit_exceeded")&&overflow.HasBlockingIssues&&overflow.Consistency is null&&!overflow.Accepted,"bounded structured enumeration");
        var atLimit=data.Concat(data).ToArray();Need(atLimit.Length==40&&!Issue(StructuredGarmentInput.Assess(atLimit,StructuredGarmentInput.PinnedModel),"input_limit_exceeded")&&Contract(StructuredGarmentInput.Assess(atLimit,StructuredGarmentInput.PinnedModel),"duplicate_field"),"exact structured limit");
        Need(Issue(StructuredGarmentInput.Assess(atLimit.Append(data[0]),StructuredGarmentInput.PinnedModel),"input_limit_exceeded"),"oversized structured input");
        Need(!data[0].ToString().Contains(data[0].Value!.Value.ToString(),StringComparison.Ordinal),"measurement ToString leaks value");
        Need(!lower.ToString().Contains("160",StringComparison.Ordinal),"A-line ToString leaks value");
        Console.WriteLine("PASS: structured source definitions, explicit units, conditional A-line gaps and exact synthetic-only geometry binding.");
    }
}
