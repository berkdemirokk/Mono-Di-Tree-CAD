namespace FittedResearch;

internal static class MeasurementConsistencyTests
{
    internal static void Verify()
    {
        var required=ResearchMeasurements.FieldsFor(ResearchMeasurements.Operation.PinnedGarment);
        var body=SyntheticBodyCatalog.Create("synthetic-base");
        var baseline=required.Select(k=>new KeyValuePair<string,decimal?>(k,body[k])).ToArray();
        KeyValuePair<string,decimal?>[] With(string key,decimal? value)=>baseline.Select(x=>x.Key==key?new KeyValuePair<string,decimal?>(key,value):x).ToArray();
        MeasurementConsistencyAudit.Report Assess(IEnumerable<KeyValuePair<string,decimal?>> pairs,string? cm="cm",string? degrees="degree")=>MeasurementConsistencyAudit.Assess(pairs,cm,degrees);
        bool Review(MeasurementConsistencyAudit.Report r,string code)=>r.ReviewIssues.Any(x=>x.Code==code);
        void Need(bool ok,string message) {if(!ok)throw new InvalidDataException(message);}
        foreach(string id in new[]{"synthetic-base","synthetic-distinct","synthetic-third","synthetic-fifth"}) {
            var sample=SyntheticBodyCatalog.Create(id);var report=Assess(required.Select(k=>new KeyValuePair<string,decimal?>(k,sample[k])));
            Need(!report.HasBlockingIssues&&!report.NeedsReview&&report.ReviewCoverageComplete&&report.Status=="unresolved"&&!report.Accepted&&!report.ProductionAllowed&&!report.ExportAllowed&&!report.PersonalDataCollectionEnabled,"baseline consistency gate: "+id);
            try {report.RequireResearchComposition();throw new Exception("Review report composed");}catch(InvalidOperationException) { }
        }
        var checks=new[]{
            ("bust_points",50m,"review_bust_points_vs_circumference"),
            ("bum_points",52m,"review_bum_points_vs_circumference"),
            ("bust_line",41m,"review_front_path_order"),
            ("head_l",172m,"review_head_vs_height"),
            ("vert_bust_line",172m,"review_vertical_bust_vs_height"),
            ("hips_line",172m,"review_hips_line_vs_height"),
            ("neck_w",36m,"review_neck_vs_shoulders"),
            ("armscye_depth",37m,"review_armhole_vs_back_length")
        };
        foreach(var (field,boundary,code) in checks) {
            Need(!Review(Assess(With(field,boundary-.000001m)),code),"false review below "+code);
            Need(!Review(Assess(With(field,boundary)),code),"false review at "+code);
            var above=Assess(With(field,boundary+.000001m));
            Need(Review(above,code)&&above.Status=="unresolved"&&!above.Accepted&&!above.ProductionAllowed,"missing review above "+code);
            var missing=Assess(With(field,null));Need(!Review(missing,code)&&missing.SkippedReviewCodes.Contains(code)&&!missing.ReviewCoverageComplete,"missing dependency compared "+code);
            var invalidResult=Assess(With(field,decimal.MaxValue));Need(!Review(invalidResult,code)&&invalidResult.SkippedReviewCodes.Contains(code),"out-of-range dependency compared "+code);
            var duplicate=Assess(With(field,boundary+.000001m).Append(new(field,boundary+.000001m)));Need(!Review(duplicate,code)&&duplicate.SkippedReviewCodes.Contains(code),"duplicate dependency compared "+code);
            var wrongUnit=Assess(With(field,boundary+.000001m),"mm");Need(!Review(wrongUnit,code)&&wrongUnit.SkippedReviewCodes.Length==8,"unknown length unit compared "+code);
        }
        var contradictory=With("bust_points",51m).Where(x=>x.Key!="head_l").ToArray();var partial=Assess(contradictory);
        Need(partial.HasBlockingIssues&&Review(partial,"review_bust_points_vs_circumference"),"independent review suppressed by missing unrelated field");
        Need(Review(Assess(With("bust_points",51m),"cm","radian"),"review_bust_points_vs_circumference"),"independent review suppressed by unrelated angle unit");
        var duplicates=With("bust_points",51m).Append(new KeyValuePair<string,decimal?>("bust",100m)).ToArray();
        Need(!Review(Assess(duplicates),"review_bust_points_vs_circumference"),"duplicate second dependency compared");
        var invalid=With("bust_points",51m).Select(x=>x.Key=="bust"?new KeyValuePair<string,decimal?>("bust",0):x);
        Need(!Review(Assess(invalid),"review_bust_points_vs_circumference"),"invalid second dependency compared");
        bool Block(MeasurementConsistencyAudit.Report r,string code)=>r.ContractIssues.Any(i=>i.Code==code&&i.Severity==MeasurementContractAudit.Kind.Blocking);
        var scopes=new[]{("back_width",100m,"unsupported_back_bust_relation"),("waist_back_width",80m,"unsupported_back_waist_relation"),("hip_back_width",104m,"unsupported_back_hip_relation"),("shoulder_incl",45m,"unsupported_shoulder_angle"),("hip_inclination",89m,"unsupported_hip_angle"),("shoulder_w",2m,"unsupported_sleeve_balance"),("height",86m,"unsupported_derived_length")};
        foreach(var (field,value,code) in scopes) {
            var full=Assess(With(field,value));
            Need(full.ContractIssues.Count(i=>i.Code==code)==1,"duplicate complete scope finding: "+code);
            var incomplete=Assess(With(field,value).Where(x=>x.Key!="neck_w"));
            Need(Block(incomplete,code)&&Block(incomplete,"missing_measurement"),"unrelated missing field suppressed scope: "+code);
        }
        var wrongCmScope=Assess(With("back_width",100m),"mm");Need(!Block(wrongCmScope,"unsupported_back_bust_relation"),"scope compared across wrong units");
        var badAngleScope=Assess(With("shoulder_incl",45m),"cm","radian");Need(!Block(badAngleScope,"unsupported_shoulder_angle"),"angle scope compared across wrong units");
        var duplicatedScope=Assess(With("back_width",100m).Append(new KeyValuePair<string,decimal?>("back_width",100m)));
        Need(!Block(duplicatedScope,"unsupported_back_bust_relation")&&Block(duplicatedScope,"duplicate_field"),"duplicate value used in scope relation");
        var orderA=Assess(contradictory);var orderB=Assess(contradictory.Reverse());
        string[] Signature(MeasurementConsistencyAudit.Report r)=>r.ContractIssues.Select(i=>i.Code+":"+string.Join(",",i.Fields)).Concat(r.ReviewIssues.Select(i=>i.Code+":"+string.Join(",",i.Fields))).Concat(r.SkippedReviewCodes).ToArray();
        Need(Signature(orderA).SequenceEqual(Signature(orderB)),"input order changed diagnostic report");
        var one=Assess(With("bust_points",51m));var first=one.ReviewIssues.Single(i=>i.Code=="review_bust_points_vs_circumference");
        Need(first.Fields.SequenceEqual(new[]{"bust_points","bust"})&&!one.HasBlockingIssues&&one.NeedsReview,"review must remain distinct from blocking");
        Need(required.Length==20&&ResearchBody.Fields.Length==26&&MeasurementConsistencyAudit.MaxInputEntries==52,"input budget contract");
        var fullBody=ResearchBody.Fields.Select(k=>new KeyValuePair<string,decimal?>(k,body[k]));
        var fullReport=Assess(fullBody);Need(!fullReport.HasBlockingIssues&&fullReport.ReviewCoverageComplete,"known optional fields regressed");
        var atLimit=baseline.Concat(Enumerable.Repeat(new KeyValuePair<string,decimal?>("bust",100m),32)).ToArray();
        var atLimitReport=Assess(atLimit);
        Need(atLimit.Length==52&&!Block(atLimitReport,"input_limit_exceeded")&&Block(atLimitReport,"duplicate_field"),"exact input limit or duplicate handling");
        var aboveLimit=Assess(atLimit.Append(new KeyValuePair<string,decimal?>("bust",100m)));
        Need(Block(aboveLimit,"input_limit_exceeded")&&aboveLimit.HasBlockingIssues&&!aboveLimit.ReviewCoverageComplete&&aboveLimit.SkippedReviewCodes.Length==8&&!aboveLimit.NeedsReview&&aboveLimit.ReviewIssues.Length==0&&aboveLimit.ContractIssues.All(x=>x.Code is not ("missing_measurement" or "duplicate_field"))&&!aboveLimit.Accepted&&!aboveLimit.ProductionAllowed&&!aboveLimit.ExportAllowed,"over-limit prefix must fail closed");
        Need(aboveLimit.SkippedReviewCodes.SequenceEqual(Assess(baseline,"mm").SkippedReviewCodes),"overflow review coverage mismatch");
        var overWithReview=Assess(With("bust_points",51m).Concat(Enumerable.Repeat(new KeyValuePair<string,decimal?>("bust",100m),33)));
        Need(Block(overWithReview,"input_limit_exceeded")&&overWithReview.ReviewIssues.Length==0&&overWithReview.SkippedReviewCodes.Length==8,"overflow leaked partial reviews");
        var aboveBadUnits=Assess(atLimit.Append(new KeyValuePair<string,decimal?>("bust",100m)),"mm","rad");
        Need(Block(aboveBadUnits,"input_limit_exceeded")&&Block(aboveBadUnits,"length_unit_not_cm")&&Block(aboveBadUnits,"angle_unit_not_degree"),"over-limit unit diagnostics");
        int visited=0;bool disposed=false;
        IEnumerable<KeyValuePair<string,decimal?>> Infinite() {
            try {while(true) {visited++;yield return new("bust",100m);}}
            finally {disposed=true;}
        }
        var endless=Assess(Infinite());
        Need(visited==53&&disposed&&Block(endless,"input_limit_exceeded")&&!endless.ReviewCoverageComplete&&!endless.Accepted,"infinite input consumption or disposal");
        int bigVisited=0;
        IEnumerable<KeyValuePair<string,decimal?>> Large() {for(int i=0;i<1000000;i++){bigVisited++;yield return new("unknown",1m);}}
        var oversized=Assess(Large());Need(bigVisited==53&&Block(oversized,"input_limit_exceeded"),"large input consumption");
        Console.WriteLine("PASS: conditional measurement reviews, boundary, missing, duplicate, invalid unit and independent-field cases; research only.");
    }
}
