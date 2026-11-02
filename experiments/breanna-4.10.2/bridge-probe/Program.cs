using TekstilCAD.Patterns;
var values=new Dictionary<string,decimal> { ["biceps"]=30,["bustFront"]=50,["bustSpan"]=19,["highBust"]=90,["highBustFront"]=45,["hpsToBust"]=27,["hpsToWaistBack"]=42,["hpsToWaistFront"]=45,["waist"]=74,["waistToHips"]=20,["neck"]=37,["shoulderToShoulder"]=39,["shoulderSlope"]=15,["shoulderToWrist"]=58,["wrist"]=16 };
var entries=BreannaMeasurementGate.Definitions.Select(d=>new BreannaMeasurementInput(d.Key,values[d.Key],d.IsAngle?BreannaUnit.Degree:BreannaUnit.Centimeter));
Console.Write(BreannaMeasurementEnvelope.Serialize(entries));
