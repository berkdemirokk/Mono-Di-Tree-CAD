using System.Text.Json;

namespace FittedResearch;

// Independent numerical comparison of C#-derived waist candidates against
// fixed source reference. Bounds are diagnostic, never sewing tolerances.
internal static class DerivedWaistAudit
{
    internal sealed record Report(double MaxEndpointCm,double MaxControlCm,double MaxLengthDifferenceCm,double MaxIntegrationDisagreementCm,double MaxEnclosureWidthCm,int SourceValuesInside,int EdgeCount,BezierLengthBounds.Bounds UpperLength,BezierLengthBounds.Bounds LowerLength)
    {
        public bool WithinDiagnosticBounds=>MaxEndpointCm<.002 && MaxControlCm<.002 && MaxLengthDifferenceCm<.002 && MaxIntegrationDisagreementCm<.00001;
        public string Status=>"unresolved";public bool ProductionAllowed=>false;public bool ExportAllowed=>false;
    }
    internal static Report Inspect(InterfaceMatcher.Result derived,JsonElement sourceCase)
    {
        if(derived.Snapshot!=sourceCase.GetProperty("id").GetString())throw new ArgumentException("Snapshot mismatch");
        var seam=sourceCase.GetProperty("stitches").EnumerateArray().Single(x=>x.GetProperty("owner_type").GetString()=="MetaGarment");
        double maxEnd=0,maxControl=0,maxLength=0,maxConvergence=0,maxEnclosure=0; int sourceInside=0,edgeCount=0;
        InterfaceMatcher.P Point(JsonElement v)=>new(v[0].GetDouble(),v[1].GetDouble());
        double Chords(InterfaceMatcher.Edge e,int steps)
        {
            InterfaceMatcher.P Eval(double t)
            {
                double u=1-t;return e.Controls.Length switch {
                    0=>e.Start*u+e.End*t,
                    1=>e.Start*(u*u)+e.Controls[0]*(2*u*t)+e.End*(t*t),
                    2=>e.Start*(u*u*u)+e.Controls[0]*(3*u*u*t)+e.Controls[1]*(3*u*t*t)+e.End*(t*t*t),
                    _=>throw new ArgumentException("Unsupported curve") };
            }
            var previous=Eval(0);double length=0;
            for(int i=1;i<=steps;i++) { var current=Eval((double)i/steps);length+=(current-previous).Norm();previous=current; }
            return length;
        }
        BezierLengthBounds.Bounds Side(IReadOnlyList<InterfaceMatcher.Edge> actual,JsonElement source)
        {
            var refs=source.GetProperty("edges").EnumerateArray().ToArray();
            if(actual.Count!=refs.Length)throw new InvalidDataException("Waist edge count mismatch");
            var coefficients=new List<double>();int covered=0;
            foreach(var part in source.GetProperty("ruffle").EnumerateArray())
            {
                var sec=part.GetProperty("sec");int from=sec[0].GetInt32(),to=sec[1].GetInt32();double coefficient=part.GetProperty("coeff").GetDouble();
                if(from!=covered||to<=from||to>refs.Length||!double.IsFinite(coefficient)||coefficient<=0)throw new InvalidDataException("Waist ruffle range");
                for(int i=from;i<to;i++)coefficients.Add(coefficient);covered=to;
            }
            if(covered!=refs.Length)throw new InvalidDataException("Incomplete waist ruffle");
            var total=new BezierLengthBounds.Bounds(System.Numerics.BigInteger.Zero,System.Numerics.BigInteger.Zero,0);
            for(int i=0;i<refs.Length;i++)
            {
                var r=refs[i];var e=actual[i];string panel=r.GetProperty("panel").GetString()!;int index=r.GetProperty("edge").GetInt32();
                if(e.Panel!=panel||e.Flipped!=r.GetProperty("flipped").GetBoolean()||e.RightWrong!=r.GetProperty("right_wrong").GetBoolean()||e.Coefficient!=coefficients[i])throw new InvalidDataException("Waist edge identity mismatch");
                var sourceEdge=sourceCase.GetProperty("panels").GetProperty(panel).GetProperty("edges")[index];
                var start=Point(sourceEdge.GetProperty("start"));var end=Point(sourceEdge.GetProperty("end"));
                maxEnd=Math.Max(maxEnd,Math.Max((e.Start-start).Norm(),(e.End-end).Norm()));
                string kind=sourceEdge.GetProperty("kind").GetString()!;
                if(kind!=e.Kind)throw new InvalidDataException("Waist curve kind mismatch");
                if(kind=="CurveEdge")
                {
                    var relative=sourceEdge.GetProperty("relative_controls");if(relative.GetArrayLength()!=e.Controls.Length)throw new InvalidDataException("Waist control count");
                    var d=end-start;
                    for(int j=0;j<e.Controls.Length;j++) { var c=Point(relative[j]);var absolute=start+d*c.X+new InterfaceMatcher.P(-d.Y,d.X)*c.Y;maxControl=Math.Max(maxControl,(e.Controls[j]-absolute).Norm()); }
                }
                var controlPoints=new List<BezierLengthBounds.Point> { new(e.Start.X,e.Start.Y) };
                controlPoints.AddRange(e.Controls.Select(c=>new BezierLengthBounds.Point(c.X,c.Y)));controlPoints.Add(new(e.End.X,e.End.Y));
                var enclosure=BezierLengthBounds.Enclose(controlPoints,8);
                maxEnclosure=Math.Max(maxEnclosure,enclosure.ApproximateWidthCm);total=BezierLengthBounds.Bounds.Add(total,enclosure);edgeCount++;
                if(enclosure.Contains(sourceEdge.GetProperty("length").GetDouble()))sourceInside++;
                double chords2048=Chords(e,2048),chords4096=Chords(e,4096),simpson=e.Length();
                maxLength=Math.Max(maxLength,Math.Abs(chords4096-sourceEdge.GetProperty("length").GetDouble()));
                maxConvergence=Math.Max(maxConvergence,Math.Max(Math.Abs(chords4096-chords2048),Math.Abs(chords4096-simpson)));
            }
            return total;
        }
        var upper=Side(derived.Upper,seam.GetProperty("a"));var lower=Side(derived.Lower,seam.GetProperty("b"));
        return new(maxEnd,maxControl,maxLength,maxConvergence,maxEnclosure,sourceInside,edgeCount,upper,lower);
    }
}
