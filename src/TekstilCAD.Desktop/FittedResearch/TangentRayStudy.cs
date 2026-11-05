namespace FittedResearch;

// Two admissible solutions to the source initial tangent-only objective.
// Same tangent does not determine the control radius or source optimizer result.
internal static class TangentRayStudy
{
    internal readonly record struct P(double X,double Y);
    internal sealed record Result(string Id,int Degrees,P Target,P Candidate,P Alternative)
    {
        public string Status=>"unresolved"; public bool CanCompose=>false;public bool ExportAllowed=>false;
    }
    internal static Result Create(string id)
    {
        int degrees=id switch { "synthetic-angle-5"=>5,"synthetic-angle-10"=>10,"synthetic-angle-18"=>18,"synthetic-angle-22"=>22,"synthetic-angle-26"=>26,"synthetic-angle-30"=>30,"synthetic-angle-35"=>35,"synthetic-angle-40"=>40,_=>throw new ArgumentException("Unsupported synthetic angle") };
        double ratio=Math.Tan(degrees*Math.PI/180)/4, norm=Math.Sqrt(1+ratio*ratio);
        var t=new P(1/norm,-ratio/norm);
        return new(id,degrees,t,new(.5*t.X,.5*t.Y),new(.75*t.X,.75*t.Y));
    }
}
