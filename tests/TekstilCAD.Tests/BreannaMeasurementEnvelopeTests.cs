using System.Text.Json;
using TekstilCAD.Patterns;
namespace TekstilCAD.Tests;
public class BreannaMeasurementEnvelopeTests
{
    private static List<BreannaMeasurementInput> Input()=>BreannaMeasurementGate.Definitions.Select(d=>new BreannaMeasurementInput(d.Key,d.IsAngle?15:100,d.IsAngle?BreannaUnit.Degree:BreannaUnit.Centimeter)).Select(x=>x.Key switch {"highBustFront"=>x with{Value=45},"hpsToBust"=>x with{Value=30},_=>x}).ToList();
    [Fact] public void EmitsVersionedMillimeterAndDegreeEnvelope(){using var doc=JsonDocument.Parse(BreannaMeasurementEnvelope.Serialize(Input()));Assert.Equal("mm",doc.RootElement.GetProperty("unit").GetString());Assert.Equal("degree",doc.RootElement.GetProperty("angleUnit").GetString());Assert.Equal(1000m,doc.RootElement.GetProperty("measurements").GetProperty("waist").GetDecimal());Assert.Equal(15m,doc.RootElement.GetProperty("measurements").GetProperty("shoulderSlope").GetDecimal());}
    [Fact] public void InvalidOrExcessPrecisionIsRejected(){var x=Input();x.RemoveAt(0);Assert.Throws<ArgumentException>(()=>BreannaMeasurementEnvelope.Serialize(x));x=Input().Select(a=>a.Key=="waist"?a with{Unit=BreannaUnit.Millimeter,Value=100.0001m}:a).ToList();Assert.Throws<ArgumentException>(()=>BreannaMeasurementEnvelope.Serialize(x));}
}
