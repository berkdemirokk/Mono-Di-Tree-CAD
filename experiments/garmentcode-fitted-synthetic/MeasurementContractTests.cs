namespace FittedResearch;

// Deterministic synthetic inputs only. Technical acceptance is not an
// anthropometric validity or physical fitting claim.
internal static class MeasurementContractTests
{
    internal static void Verify()
    {
        var required=ResearchMeasurements.FieldsFor(ResearchMeasurements.Operation.PinnedGarment);
        if(required.Length!=20 || required.Distinct(StringComparer.Ordinal).Count()!=20)throw new InvalidDataException("Pinned measurement field count");
        var source=SyntheticBodyCatalog.Create("synthetic-base");
        var baseline=required.Select(k=>new KeyValuePair<string,decimal?>(k,source[k])).ToArray();
        var baselineReport=MeasurementContractAudit.Assess(baseline,"cm","degree");
        if(baselineReport.HasBlockingIssues || baselineReport.Status!="unresolved" || baselineReport.ProductionAllowed || baselineReport.ExportAllowed || baselineReport.PersonalDataCollectionEnabled)throw new InvalidDataException("Synthetic audit baseline");
        bool Code(MeasurementContractAudit.Report report,string code,string? field=null)=>report.Issues.Any(i=>i.Code==code && i.Severity==MeasurementContractAudit.Kind.Blocking && (field is null || i.Fields.Contains(field,StringComparer.Ordinal)));
        KeyValuePair<string,decimal?>[] With(string key,decimal? value)=>baseline.Select(p=>p.Key==key?new KeyValuePair<string,decimal?>(key,value):p).ToArray();
        void Technical(string key,decimal value,bool expectedInRange)
        {
            var input=With(key,value);var report=MeasurementContractAudit.Assess(input,"cm","degree");
            bool rejected=Code(report,"outside_technical_range",key);
            if(rejected==expectedInRange)throw new InvalidDataException("Audit technical boundary: "+key);
            bool rawAccepted=true;
            try { _=ResearchMeasurements.Create(input.Select(p=>new KeyValuePair<string,decimal>(p.Key,p.Value!.Value)),"cm","degree"); }
            catch(ArgumentException) { rawAccepted=false; }
            if(rawAccepted!=expectedInRange)throw new InvalidDataException("Raw technical boundary: "+key);
            if(report.Status!="unresolved" || report.ProductionAllowed || report.ExportAllowed || report.PersonalDataCollectionEnabled)throw new InvalidDataException("Boundary report gate");
        }
        foreach(string key in required)
        {
            var absent=baseline.Where(p=>p.Key!=key).ToArray();var missing=MeasurementContractAudit.Assess(absent,"cm","degree");
            if(!Code(missing,"missing_measurement",key))throw new InvalidDataException("Missing field not reported: "+key);
            var nullValue=MeasurementContractAudit.Assess(With(key,null),"cm","degree");
            if(!Code(nullValue,"missing_measurement",key))throw new InvalidDataException("Null field not reported: "+key);
            var duplicate=MeasurementContractAudit.Assess(baseline.Append(new(key,source[key])),"cm","degree");
            if(!Code(duplicate,"duplicate_field",key) || !Code(duplicate,"missing_measurement",key))throw new InvalidDataException("Duplicate field not rejected: "+key);
            var conflicting=MeasurementContractAudit.Assess(baseline.Append(new(key,source[key]+1)),"cm","degree");
            if(!Code(conflicting,"duplicate_field",key) || !Code(conflicting,"missing_measurement",key))throw new InvalidDataException("Conflicting duplicate not rejected: "+key);
            try { ResearchMeasurements.Create(baseline.Select(p=>new KeyValuePair<string,decimal>(p.Key,p.Value!.Value)).Append(new(key,source[key])),"cm","degree");throw new InvalidDataException("Duplicate raw field accepted: "+key); } catch(ArgumentException) { }
            bool angle=key is "shoulder_incl" or "hip_inclination";
            if(angle) {
                Technical(key,-.000001m,false);Technical(key,0m,true);Technical(key,.000001m,true);
                Technical(key,179.999999m,true);Technical(key,180m,true);Technical(key,180.000001m,false);
            } else {
                Technical(key,-.000001m,false);Technical(key,0m,false);Technical(key,.000001m,true);
                Technical(key,999.999999m,true);Technical(key,1000m,true);Technical(key,1000.000001m,false);
            }
            Technical(key,decimal.MinValue,false);Technical(key,decimal.MaxValue,false);
        }
        void Scope(string key,decimal value,string code,bool expectedRejected)
        {
            var input=With(key,value);var report=MeasurementContractAudit.Assess(input,"cm","degree");
            if(Code(report,code)!=expectedRejected)throw new InvalidDataException("Scope boundary: "+key);
            bool accepted=true;
            try { _=ResearchMeasurements.Create(input.Select(p=>new KeyValuePair<string,decimal>(p.Key,p.Value!.Value)),"cm","degree").BuildPinnedGarment(); }
            catch(ArgumentException) { accepted=false; }
            if(accepted==expectedRejected)throw new InvalidDataException("Scope audit/build disagreement: "+key);
        }
        Scope("back_width",99.999999m,"unsupported_back_bust_relation",false);Scope("back_width",100m,"unsupported_back_bust_relation",true);Scope("back_width",100.000001m,"unsupported_back_bust_relation",true);
        Scope("waist_back_width",79.999999m,"unsupported_back_waist_relation",false);Scope("waist_back_width",80m,"unsupported_back_waist_relation",true);Scope("waist_back_width",80.000001m,"unsupported_back_waist_relation",true);
        Scope("hip_back_width",103.999999m,"unsupported_back_hip_relation",false);Scope("hip_back_width",104m,"unsupported_back_hip_relation",true);Scope("hip_back_width",104.000001m,"unsupported_back_hip_relation",true);
        Scope("shoulder_incl",44.999999m,"unsupported_shoulder_angle",false);Scope("shoulder_incl",45m,"unsupported_shoulder_angle",true);Scope("shoulder_incl",45.000001m,"unsupported_shoulder_angle",true);
        Scope("hip_inclination",88.999999m,"unsupported_hip_angle",false);Scope("hip_inclination",89m,"unsupported_hip_angle",true);Scope("hip_inclination",89.000001m,"unsupported_hip_angle",true);
        Scope("shoulder_w",1.999999m,"unsupported_sleeve_balance",true);Scope("shoulder_w",2m,"unsupported_sleeve_balance",true);Scope("shoulder_w",2.000001m,"unsupported_sleeve_balance",false);
        Scope("shoulder_w",2.0000000000000000000000000001m,"unsupported_sleeve_balance",true);
        Scope("height",85.999999m,"unsupported_derived_length",true);Scope("height",86m,"unsupported_derived_length",true);Scope("height",86.000001m,"unsupported_derived_length",false);
        var concreteBaseline=baseline.Select(p=>new KeyValuePair<string,decimal>(p.Key,p.Value!.Value)).ToArray();
        foreach(string? wrong in new string?[]{null,""," ","mm","CM","cm ","inch"}) {
            if(!Code(MeasurementContractAudit.Assess(baseline,wrong,"degree"),"length_unit_not_cm"))throw new InvalidDataException("Length unit accepted by audit");
            try { ResearchMeasurements.Create(concreteBaseline,wrong!,"degree");throw new InvalidDataException("Length unit accepted by input contract"); } catch(ArgumentException) { }
        }
        foreach(string? wrong in new string?[]{null,""," ","degrees","Degree","degree ","rad","°"}) {
            if(!Code(MeasurementContractAudit.Assess(baseline,"cm",wrong),"angle_unit_not_degree"))throw new InvalidDataException("Angle unit accepted by audit");
            try { ResearchMeasurements.Create(concreteBaseline,"cm",wrong!);throw new InvalidDataException("Angle unit accepted by input contract"); } catch(ArgumentException) { }
        }
        var unknown=MeasurementContractAudit.Assess(baseline.Append(new("not_a_field",1)),"cm","degree");
        var derived=MeasurementContractAudit.Assess(baseline.Append(new("_leg_length",1)),"cm","degree");
        if(!Code(unknown,"unknown_or_derived_field") || !Code(derived,"unknown_or_derived_field"))throw new InvalidDataException("Unknown/derived fields accepted");
        // The technical acceptance of extreme values never changes the gate.
        var extreme=MeasurementContractAudit.Assess(With("bust_points",1000m),"cm","degree");
        if(extreme.HasBlockingIssues || extreme.Status!="unresolved" || extreme.ProductionAllowed || extreme.PersonalDataCollectionEnabled)throw new InvalidDataException("Extreme value treated as fit proof");
        Console.WriteLine("PASS: 20 synthetic measurement fields: missing, duplicates, technical and scope boundaries; research only.");
    }
}
