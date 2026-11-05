using System.Security.Cryptography;
using TekstilCAD.Core;

namespace DartClosureGeometry;

public static class ContourDiagnostic
{
    public sealed record Result(string Status, IReadOnlyList<string> Issues)
    {
        public bool ProductionAllowed => false;
        public bool ExportAllowed => false;
    }
    // Caller must keep all collections stable throughout Capture. The snapshot
    // owns its arrays and is immutable afterwards. Hash is identity, not origin.
    public sealed class Snapshot
    {
        internal readonly Point2[] Points;
        internal readonly bool[] Moving;
        internal readonly string[] Roles;
        internal readonly string Part;
        internal Snapshot(string part,Point2[] points,bool[] moving,string[] roles) { Part=part; Points=points; Moving=moving; Roles=roles; }
        public string Identity()
        {
            using var bytes=new MemoryStream();
            using(var w=new BinaryWriter(bytes,System.Text.Encoding.UTF8,true))
            {
                w.Write("dart-contour-v2"); w.Write(Part); w.Write(Points.Length);
                for(int i=0;i<Points.Length;i++) { w.Write(BitConverter.DoubleToInt64Bits(Points[i].X)); w.Write(BitConverter.DoubleToInt64Bits(Points[i].Y)); w.Write(Moving[i]); w.Write(Roles[i]); }
            }
            return Convert.ToHexString(SHA256.HashData(bytes.ToArray()));
        }
    }
    public static Snapshot Capture(string part,IReadOnlyList<Point2> source,IReadOnlyList<bool> moving,IReadOnlyList<string> roles)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(part); ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(moving); ArgumentNullException.ThrowIfNull(roles);
        if(source.Count<5 || source.Count>512 || moving.Count!=source.Count || roles.Count!=source.Count) throw new ArgumentException("Incomplete contour snapshot.");
        var p=source.ToArray(); var m=moving.ToArray(); var r=roles.ToArray();
        if(p.Length!=m.Length || p.Length!=r.Length || r.Any(string.IsNullOrWhiteSpace)) throw new ArgumentException("Incomplete contour snapshot.");
        return new(part,p,m,r);
    }
    public static Result Inspect(Snapshot snapshot,string expectedIdentity)
    {
        Result Block(string reason) => new("blocked",Array.AsReadOnly(new[]{reason}));
        if(snapshot is null || string.IsNullOrWhiteSpace(expectedIdentity) || !StringComparer.OrdinalIgnoreCase.Equals(snapshot.Identity(),expectedIdentity)) return Block("identity-mismatch");
        var source=snapshot.Points; var moving=snapshot.Moving;
        string[] expected=["center","legA","tip","legB","side"];
        if(!snapshot.Roles.Take(5).SequenceEqual(expected) || !moving[0] || !moving[1] || moving[2] || moving[3] || moving[4]) return Block("role-or-partition-invalid");
        try { Geometry.ValidateContour(source); } catch(ArgumentException) { return Block("source-contour-invalid"); }
        DartAnalysis dart;
        try { dart=DartClosure.Analyze(source[0],source[1],source[2],source[3],source[4],0); } catch(ArgumentException) { return Block("dart-invalid"); }
        Point2 tip=source[2],a=source[1],b=source[3];
        double al=Geometry.Distance(tip,a),bl=Geometry.Distance(tip,b);
        double ux=(a.X-tip.X)/al,uy=(a.Y-tip.Y)/al,vx=(b.X-tip.X)/bl,vy=(b.Y-tip.Y)/bl;
        double c=ux*vx+uy*vy,s=ux*vy-uy*vx,n=Math.Sqrt(c*c+s*s); c/=n;s/=n;
        Point2 Map(Point2 p) => new(tip.X+c*(p.X-tip.X)-s*(p.Y-tip.Y),tip.Y+s*(p.X-tip.X)+c*(p.Y-tip.Y));
        var mapped=source.Select((p,i)=>moving[i]?Map(p):p).ToArray();
        var issues=new List<string>(); var edges=new List<(int index,Point2 from,Point2 to)>();
        for(int i=0;i<source.Length;i++)
        {
            if(i is 1 or 2) continue;
            int j=(i+1)%source.Length;
            if(moving[i]!=moving[j]) issues.Add($"partition-boundary:{i}");
            edges.Add((i,mapped[i],mapped[j]));
        }
        for(int i=0;i<edges.Count;i++) for(int j=i+1;j<edges.Count;j++)
        {
            var x=edges[i]; var y=edges[j];
            bool dartJoin=x.index==0 && y.index==3;
            bool normalJoin=(x.index+1)%source.Length==y.index || (y.index+1)%source.Length==x.index;
            var kind= normalJoin && (y.index+1)%source.Length==x.index
                ? SegmentContact.Classify(y.from,y.to,x.from,x.to,true)
                : SegmentContact.Classify(x.from,x.to,y.from,y.to,dartJoin||normalJoin);
            if(kind!=SegmentContact.Kind.Disjoint) issues.Add($"{kind}:{x.index}:{y.index}");
        }
        if(!dart.ExactBinarySquaredLegsEqual || dart.EndpointGap!=0) issues.Add("dart-endpoint-unresolved");
        issues.Add("rotation-and-physical-closure-unresolved");
        return new("blocked",issues.AsReadOnly());
    }
}
