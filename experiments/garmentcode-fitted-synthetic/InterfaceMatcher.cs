using System.Text.Json;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace FittedResearch;

// Bounded numerical research port of GarmentCode connector.py. MIT,
// copyright (c) 2024 Maria Korosteleva. No physical sewing approval.
internal static class InterfaceMatcher
{
    internal readonly record struct P(double X,double Y)
    {
        public static P operator +(P a,P b)=>new(a.X+b.X,a.Y+b.Y);
        public static P operator -(P a,P b)=>new(a.X-b.X,a.Y-b.Y);
        public static P operator *(P a,double f)=>new(a.X*f,a.Y*f);
        public double Norm()=>Math.Sqrt(X*X+Y*Y);
    }
    internal sealed record Edge(string Panel,string Kind,P Start,P End,ImmutableArray<P> Controls,bool Flipped,bool RightWrong,double Coefficient)
    {
        public double Projected => Math.Abs(Coefficient-1)<.001 ? Length() : Length()/Coefficient;
        private P Eval(double t)
        {
            double u=1-t;
            return Controls.Length switch { 0=>Start*u+End*t, 1=>Start*(u*u)+Controls[0]*(2*u*t)+End*(t*t), 2=>Start*(u*u*u)+Controls[0]*(3*u*u*t)+Controls[1]*(3*u*t*t)+End*(t*t*t), _=>throw new ArgumentException("Unsupported curve") };
        }
        private P Derivative(double t)
        {
            double u=1-t;
            return Controls.Length switch { 0=>End-Start, 1=>(Controls[0]-Start)*(2*u)+(End-Controls[0])*(2*t), 2=>(Controls[0]-Start)*(3*u*u)+(Controls[1]-Controls[0])*(6*u*t)+(End-Controls[1])*(3*t*t), _=>throw new ArgumentException("Unsupported curve") };
        }
        public double Length(double until=1)
        {
            if(Controls.Length==0)return (End-Start).Norm()*until;
            const int n=512; double h=until/n,sum=Derivative(0).Norm()+Derivative(until).Norm();
            for(int i=1;i<n;i++) sum+=(i%2==0?2:4)*Derivative(i*h).Norm();
            return sum*h/3;
        }
        public (Edge first,Edge second) SplitNative(double fraction)
        {
            if(!double.IsFinite(fraction)||fraction<=0||fraction>=1)throw new ArgumentException("Invalid split");
            double t=fraction;
            if(Controls.Length>0)
            {
                double target=Length()*fraction,lo=0,hi=1;
                for(int i=0;i<48;i++) { double mid=(lo+hi)/2; if(Length(mid)<target)lo=mid;else hi=mid; } t=(lo+hi)/2;
            }
            P L(P a,P b)=>a*(1-t)+b*t;
            if(Controls.Length==0) { P m=Eval(t);return(this with {End=m},this with {Start=m}); }
            if(Controls.Length==1) { P a=L(Start,Controls[0]),b=L(Controls[0],End),m=L(a,b);return(this with {End=m,Controls=[a]},this with {Start=m,Controls=[b]}); }
            if(Controls.Length==2) { P a=L(Start,Controls[0]),b=L(Controls[0],Controls[1]),c=L(Controls[1],End),d=L(a,b),e=L(b,c),m=L(d,e);return(this with {End=m,Controls=[a,d]},this with {Start=m,Controls=[e,c]}); }
            throw new ArgumentException("Unsupported curve");
        }
    }
    internal sealed record Result(string Snapshot,ImmutableArray<Edge> Upper,ImmutableArray<Edge> Lower)
    {
        public string Status=>"unresolved"; public bool ExportAllowed=>false;
    }
    internal static Edge Read(JsonElement item)
    {
        P Pt(JsonElement p) => new(p[0].GetDouble(),p[1].GetDouble());
        P a=Pt(item.GetProperty("start")),b=Pt(item.GetProperty("end")),d=b-a;
        string kind=item.GetProperty("kind").GetString()!;
        ImmutableArray<P> cp=kind switch {
            "Edge"=>[],
            "CurveEdge"=>item.GetProperty("relative_controls").EnumerateArray().Select(v=> { P c=Pt(v);return a+d*c.X+new P(-d.Y,d.X)*c.Y; }).ToImmutableArray(),
            _=>throw new ArgumentException("Unsupported curve kind")
        };
        var edge=new Edge(item.GetProperty("panel").GetString()!,kind,a,b,cp,item.GetProperty("flipped").GetBoolean(),item.GetProperty("right_wrong").GetBoolean(),item.GetProperty("coefficient").GetDouble());
        if(!double.IsFinite(edge.Coefficient)||edge.Coefficient<=0||!double.IsFinite(edge.Length())||edge.Length()<1e-9||Math.Abs(edge.Length()-item.GetProperty("length_cm").GetDouble())>1e-6) throw new ArgumentException("Invalid or inconsistent edge");
        return edge;
    }
    static double[] Fractions(List<Edge> edges)
    {
        if(edges.Count==0||edges.Count>512)throw new ArgumentException("Invalid interface");
        var lengths=edges.Select(e=>e.Projected).ToArray(); double sum=lengths.Sum();
        if(!double.IsFinite(sum)||sum<=0||lengths.Any(x=>!double.IsFinite(x)||x<=0))throw new ArgumentException("Invalid projected lengths");
        return lengths.Select(x=>x/sum).ToArray();
    }
    internal static Result Match(string snapshot,IReadOnlyList<Edge> upper,IReadOnlyList<Edge> lower)
    {
        if(snapshot is not ("synthetic-base" or "synthetic-distinct" or "test")) throw new ArgumentException("Unsupported snapshot");
        var a=upper.ToList();var b=lower.ToList();
        if(snapshot!="test")
        {
            var expected=ReadPinnedInput(snapshot);
            if(!Same(a,expected.upper)||!Same(b,expected.lower)) throw new ArgumentException("Synthetic interface input does not match pinned snapshot");
        }
        var af=Fractions(a);var bf=Fractions(b);
        bool matching=af.Length==bf.Length && af.Zip(bf).All(p=>Math.Abs(p.First-p.Second)<=.05+1e-5*Math.Abs(p.Second));
        if(!matching)
        {
            double tol=Math.Min(.01,Math.Min(af.Min(),bf.Min())/2);
            SplitAt(a,bf,tol); SplitAt(b,af,tol);
        }
        return new(snapshot,a.ToImmutableArray(),b.ToImmutableArray());
    }
    static string PinnedText(string resource,string expectedHash)
    {
        using var stream=typeof(InterfaceMatcher).Assembly.GetManifestResourceStream("FittedCheck."+resource) ?? throw new InvalidDataException("Missing pinned resource");
        using var reader=new StreamReader(stream,Encoding.UTF8,true);
        string text=reader.ReadToEnd().Replace("\r\n","\n").Trim();
        if(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))!=expectedHash) throw new InvalidDataException("Pinned resource mismatch: "+resource);
        return text;
    }
    static (List<Edge> upper,List<Edge> lower) ReadPinnedInput(string id)
    {
        string text=PinnedText("pre-seam.json","2B9AD965C55321B2A94A290C9C52F97A7DFBB5B5D4FE0954CD7565F0C9402BB6");
        using var document=JsonDocument.Parse(text);
        if(document.RootElement.GetProperty("source_commit").GetString()!="d449629979028123a5c4dc9e732a2ec19b7fce31") throw new InvalidDataException("Source mismatch");
        var item=document.RootElement.GetProperty("cases").EnumerateArray().Single(x=>x.GetProperty("id").GetString()==id);
        return (item.GetProperty("upper").EnumerateArray().Select(Read).ToList(),item.GetProperty("lower").EnumerateArray().Select(Read).ToList());
    }
    static bool Same(IEnumerable<Edge> left,IEnumerable<Edge> right)
    {
        var a=left.ToArray();var b=right.ToArray();
        return a.Length==b.Length && a.Zip(b).All(pair=>pair.First.Panel==pair.Second.Panel && pair.First.Kind==pair.Second.Kind && pair.First.Start==pair.Second.Start && pair.First.End==pair.Second.End && pair.First.Flipped==pair.Second.Flipped && pair.First.RightWrong==pair.Second.RightWrong && pair.First.Coefficient==pair.Second.Coefficient && pair.First.Controls.SequenceEqual(pair.Second.Controls));
    }
    internal static void ValidateBound(string id,Result candidate)
    {
        if(candidate is null || id!=candidate.Snapshot || id is not ("synthetic-base" or "synthetic-distinct")) throw new ArgumentException("Snapshot mismatch");
        var original=ReadPinnedInput(id); var recomputed=Match(id,original.upper,original.lower);
        if(!Same(candidate.Upper,recomputed.Upper)||!Same(candidate.Lower,recomputed.Lower)) throw new ArgumentException("Matched interface changed");
        string reference=PinnedText("golden.json","920EF52FAB12542006C29F6642DD0EA970BDDE984B160FA7DC1A7BF870B08D30");
        string contract=PinnedText("expected-seams.json","2C415A46FB25441142BCC7F0EF078CE227F9B9075F86ACD262D436CFC1F25976");
        using var source=JsonDocument.Parse(reference); using var seams=JsonDocument.Parse(contract);
        var item=source.RootElement.GetProperty("cases").EnumerateArray().Single(x=>x.GetProperty("id").GetString()==id);
        var waist=seams.RootElement.GetProperty(id).EnumerateArray().Single(x=>x.GetProperty("owner_type").GetString()=="MetaGarment");
        void Side(ImmutableArray<Edge> edges,JsonElement entries)
        {
            var identities=entries.GetProperty("edges").EnumerateArray().ToArray();
            if(identities.Length!=edges.Length)throw new ArgumentException("Unexpected edge count");
            for(int i=0;i<edges.Length;i++)
            {
                var r=identities[i]; var e=edges[i];string panel=r.GetProperty("panel").GetString()!;int index=r.GetProperty("edge").GetInt32();
                if(panel!=e.Panel || r.GetProperty("flipped").GetBoolean()!=e.Flipped || r.GetProperty("right_wrong").GetBoolean()!=e.RightWrong) throw new ArgumentException("Edge identity mismatch");
                var sourceEdge=item.GetProperty("panels").GetProperty(panel).GetProperty("edges")[index];
                P Pos(JsonElement p)=>new(p[0].GetDouble(),p[1].GetDouble());
                if((Pos(sourceEdge.GetProperty("start"))-e.Start).Norm()>2e-5 || (Pos(sourceEdge.GetProperty("end"))-e.End).Norm()>2e-5) throw new ArgumentException("Source edge coordinates mismatch");
            }
        }
        Side(candidate.Upper,waist.GetProperty("a"));Side(candidate.Lower,waist.GetProperty("b"));
    }
    static void SplitAt(List<Edge> target,double[] other,double tol)
    {
        int input=0,add=0,guard=0; double covered=0,otherCovered=0; var fractions=Fractions(target);
        while(input<target.Count && add<other.Length)
        {
            if(++guard>2048)throw new InvalidOperationException("Iteration bound");
            double next=Math.Min(covered+fractions[input],1),nextOther=Math.Min(otherCovered+other[add],1);
            if(Math.Abs(next-nextOther)<tol) { input++;add++;covered=next;otherCovered=nextOther; }
            else if(next<nextOther) { input++;covered=next; }
            else
            {
                double f=(fractions[input]-(next-nextOther))/fractions[input]; var original=target[input];
                if(original.Flipped) f=1-f;
                var (first,second)=original.SplitNative(f); target.RemoveAt(input);
                if(original.Flipped) { target.Insert(input,second);target.Insert(input+1,first); }
                else { target.Insert(input,first);target.Insert(input+1,second); }
                fractions=Fractions(target);covered+=fractions[input];otherCovered=nextOther;input++;add++;
            }
        }
        if(add!=other.Length)throw new InvalidOperationException("Incomplete interface match");
    }
}


