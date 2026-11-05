using System.Collections.Immutable;

namespace FittedResearch;

// Rebuild only the selected design's pre-match waist interfaces from C#
// generated synthetic body and skirt edges. No source JSON is read here.
internal static class DerivedWaistInputs
{
    internal sealed record Input(string Snapshot,ImmutableArray<InterfaceMatcher.Edge> Upper,ImmutableArray<InterfaceMatcher.Edge> Lower);
    internal static Input Create(string id)
    {
        if(id is not ("synthetic-base" or "synthetic-distinct"))throw new ArgumentException("Unsupported synthetic fixture");
        var fr=FrontBodiceCandidate.Create(id,false);var fl=FrontBodiceCandidate.Create(id,true);
        var br=BackBodiceCandidate.Create(id,false);var bl=BackBodiceCandidate.Create(id,true);
        InterfaceMatcher.Edge Front(FrontBodiceCandidate.Path p,int index)
        {
            var e=p.Edges[index];if(e.Kind!="L")throw new InvalidOperationException("Unexpected front waist curve");
            return new(p.Panel,"Edge",new(e.Start.X,e.Start.Y),new(e.End.X,e.End.Y),[],false,false,1);
        }
        InterfaceMatcher.Edge Back(BackBodiceCandidate.Path p,int index)
        {
            var e=p.Edges[index];if(e.Kind!="Q" || e.Controls.Count!=1)throw new InvalidOperationException("Unexpected back waist edge");
            var a=new InterfaceMatcher.P(e.Start.X,e.Start.Y);var b=new InterfaceMatcher.P(e.End.X,e.End.Y);var d=b-a;var rel=e.Controls[0];
            var control=a+d*rel.X+new InterfaceMatcher.P(-d.Y,d.X)*rel.Y;
            return new(p.Panel,"CurveEdge",a,b,[control],false,false,1);
        }
        var upper=ImmutableArray.Create(Front(fr,8),Front(fr,11),Front(fl,0),Front(fl,3),Back(bl,6),Back(bl,9),Back(bl,12),Back(br,0),Back(br,3),Back(br,6));
        var pieces=PartialFittedEngine.Create(id).Segments;
        PartialFittedEngine.Segment Part(string panel,int edge)=>pieces.Single(e=>e.Panel==panel&&e.Edge==edge);
        InterfaceMatcher.Edge L(string panel,PartialFittedEngine.P a,PartialFittedEngine.P b)=>new(panel,"Edge",new(a.X,a.Y),new(b.X,b.Y),[],true,false,1);
        var f2=Part("skirt_front",2);var f4=Part("skirt_front",4);var f5=Part("skirt_front",5);var f8=Part("skirt_front",8);var f9=Part("skirt_front",9);var f11=Part("skirt_front",11);
        var b1=Part("skirt_back",1);var b3=Part("skirt_back",3);var b4=Part("skirt_back",4);var b6=Part("skirt_back",6);var b7=Part("skirt_back",7);var b10=Part("skirt_back",10);var b11=Part("skirt_back",11);var b13=Part("skirt_back",13);var b14=Part("skirt_back",14);var b16=Part("skirt_back",16);
        var lower=ImmutableArray.Create(L("skirt_front",f9.End,f11.Start),L("skirt_front",f5.End,f8.Start),L("skirt_front",f2.End,f4.Start),L("skirt_back",b14.End,b16.Start),L("skirt_back",b11.End,b13.Start),L("skirt_back",b7.End,b10.Start),L("skirt_back",b4.End,b6.Start),L("skirt_back",b1.End,b3.Start));
        return new(id,upper,lower);
    }
}
