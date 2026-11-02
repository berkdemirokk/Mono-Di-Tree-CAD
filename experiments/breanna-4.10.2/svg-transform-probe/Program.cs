using System.Globalization;
using System.Diagnostics;
using System.Text.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

XNamespace Ns="http://www.w3.org/2000/svg";
try {
 using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(3));using var buffer=new MemoryStream();byte[] chunk=new byte[8192];var stdin=Console.OpenStandardInput();while(true){int n=await Task.Run(()=>stdin.Read(chunk,0,chunk.Length)).WaitAsync(deadline.Token);if(n==0)break;if(buffer.Length+n>1_000_000)throw new InvalidDataException();buffer.Write(chunk,0,n);}using var text=new StringReader(new UTF8Encoding(false,true).GetString(buffer.ToArray()));using var reader=XmlReader.Create(text,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=1_000_000});
 var elapsed=Stopwatch.StartNew();var doc=XDocument.Load(reader);if(elapsed.ElapsedMilliseconds>3000)throw new InvalidDataException(); var root=doc.Root??throw new InvalidDataException();
 if(root.Name!=Ns+"svg" || doc.Nodes().OfType<XProcessingInstruction>().Any() || root.Attributes().Any(a=>!a.IsNamespaceDeclaration && a.Name!=XNamespace.Xml+"lang" && (a.Name.Namespace!=XNamespace.None || !new[]{"freesewing","width","height","viewBox","preserveAspectRatio"}.Contains(a.Name.LocalName))) || root.Attribute("transform")!=null || root.Attribute("style")!=null) throw new InvalidDataException();
 double width=Mm(root,"width"),height=Mm(root,"height");var vb=Numbers((string?)root.Attribute("viewBox")??throw new InvalidDataException());
 if(vb.Length!=4||width<=0||height<=0||vb[2]<=0||vb[3]<=0)throw new InvalidDataException();
 double sx=width/vb[2],sy=height/vb[3];string preserve=(string?)root.Attribute("preserveAspectRatio")??"xMidYMid meet";
 double ox=0,oy=0;if(preserve=="xMidYMid meet"){sx=sy=Math.Min(sx,sy);ox=(width-vb[2]*sx)/2;oy=(height-vb[3]*sy)/2;}else if(preserve!="none")throw new InvalidDataException();
 var viewport=new M(sx,0,0,sy,ox-vb[0]*sx,oy-vb[1]*sy);Check(viewport);
 if(doc.Descendants().Any(e=>e.Name.Namespace==Ns && new[]{"animate","animateTransform","animateMotion","set"}.Contains(e.Name.LocalName)))throw new InvalidDataException();
 if(doc.Descendants(Ns+"style").Any(e=>!string.IsNullOrWhiteSpace(e.Value)))throw new InvalidDataException();
 string[] ids={"breanna.front","breanna.back","breanna.sleeve"};var output=new List<object>();
 foreach(var id in ids){
  var groups=doc.Descendants(Ns+"g").Where(e=>(string?)e.Attribute("id")=="fs-stack-"+id+"-part-"+id).ToArray();if(groups.Length!=1)throw new InvalidDataException();var group=groups[0];
  var stack=group.Parent;if(stack?.Name!=Ns+"g"||(string?)stack.Attribute("id")!="fs-stack-"+id)throw new InvalidDataException();
  var fabricPaths=group.Descendants(Ns+"path").Where(e=>((string?)e.Attribute("class")??"").Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries).Contains("fabric")).ToArray();if(fabricPaths.Any(e=>e.Parent!=group || ((string?)e.Attribute("class")!="fabric" && ((string?)e.Attribute("class")!="fabric dotted stroke-sm" || !OpenGuide((string?)e.Attribute("d"))))))throw new InvalidDataException();var candidates=fabricPaths.Where(e=>(string?)e.Attribute("class")=="fabric").ToArray();if(candidates.Length!=1)throw new InvalidDataException();var path=candidates[0];
  var chain=path.AncestorsAndSelf().Reverse().Skip(1).ToArray();var matrix=viewport;foreach(var node in chain){if(node.Name!=Ns+"g"&&node!=path)throw new InvalidDataException();if(node.Attributes().Any(a=>!a.IsNamespaceDeclaration && (a.Name.Namespace!=XNamespace.None || !(node==path?new[]{"id","class","d","transform"}:new[]{"id","transform"}).Contains(a.Name.LocalName))))throw new InvalidDataException();matrix=Mul(matrix,Transform((string?)node.Attribute("transform")));}
  var ops=Path((string?)path.Attribute("d")??throw new InvalidDataException(),matrix);var pts=ops.SelectMany(x=>x.Points).ToArray();if(pts.Length==0)throw new InvalidDataException();
  output.Add(new{id,segments=ops.Select(x=>new{type=x.Kind,points=x.Points}),bounds=new[]{pts.Min(p=>p[0]),pts.Min(p=>p[1]),pts.Max(p=>p[0]),pts.Max(p=>p[1])}});
 }
 Console.Write(JsonSerializer.Serialize(new{purpose="diagnostic-only",unit="mm",topologyVerified=false,transferable=false,parts=output}));
}catch{Console.Error.Write("INVALID_SVG");Environment.ExitCode=1;}



static double Mm(XElement root,string name){var s=(string?)root.Attribute(name)??throw new InvalidDataException();if(!s.EndsWith("mm",StringComparison.Ordinal))throw new InvalidDataException();var n=Numbers(s[..^2]);if(n.Length!=1)return double.NaN;return n[0];}
static double[] Numbers(string s){
 const string num=@"[+-]?(?:(?:\d+\.?\d*)|(?:\.\d+))(?:[eE][+-]?\d+)?";
 if(!Regex.IsMatch(s,@"^\s*"+num+@"(?:(?:\s*,\s*|\s+)"+num+@")*\s*$"))throw new InvalidDataException();
 return Regex.Matches(s,num).Select(m=>double.Parse(m.Value,CultureInfo.InvariantCulture)).Select(x=>double.IsFinite(x)&&Math.Abs(x)<=1e8?x:throw new InvalidDataException()).ToArray();
}
static M Mul(M x,M y){var z=new M(x.A*y.A+x.C*y.B,x.B*y.A+x.D*y.B,x.A*y.C+x.C*y.D,x.B*y.C+x.D*y.D,x.A*y.E+x.C*y.F+x.E,x.B*y.E+x.D*y.F+x.F);Check(z);return z;}
static void Check(M m){var vals=new[]{m.A,m.B,m.C,m.D,m.E,m.F};if(vals.Any(v=>!double.IsFinite(v)||Math.Abs(v)>1e8)||!double.IsFinite(m.A*m.D-m.B*m.C)||Math.Abs(m.A*m.D-m.B*m.C)<1e-12)throw new InvalidDataException();}
static M Transform(string? s){var result=new M(1,0,0,1,0,0);if(s==null)return result;if(Regex.IsMatch(s,@"^\s*,|,\s*$|,\s*,"))throw new InvalidDataException();int pos=0;var re=new Regex(@"\G\s*(?:,\s*)?([A-Za-z]+)\s*\(([^()]*)\)");while(pos<s.Length){var m=re.Match(s,pos);if(!m.Success)throw new InvalidDataException();var n=Numbers(m.Groups[2].Value);M z;switch(m.Groups[1].Value){
 case "matrix" when n.Length==6:z=new(n[0],n[1],n[2],n[3],n[4],n[5]);break;
 case "translate" when n.Length is 1 or 2:z=new(1,0,0,1,n[0],n.Length==2?n[1]:0);break;
 case "scale" when n.Length is 1 or 2:z=new(n[0],0,0,n.Length==2?n[1]:n[0],0,0);break;
 case "rotate" when n.Length is 1 or 3:double a=n[0]*Math.PI/180,c=Math.Cos(a),v=Math.Sin(a);z=new(c,v,-v,c,0,0);if(n.Length==3)z=Mul(Mul(new(1,0,0,1,n[1],n[2]),z),new(1,0,0,1,-n[1],-n[2]));break;
 default:throw new InvalidDataException();}result=Mul(result,z);pos+=m.Length;if(s.AsSpan(pos).Trim().IsEmpty){pos=s.Length;break;}}
 if(pos==0)throw new InvalidDataException();return result;}
static bool OpenGuide(string? s){if(s is null)return false;try{var p=Path(s,new M(1,0,0,1,0,0),false);var a=p[0].Points[0];return p.Skip(1).All(op=>{var b=op.Points[^1];return Math.Abs(a[0]-b[0])>1e-8||Math.Abs(a[1]-b[1])>1e-8;});}catch{return false;}}
static List<Op> Path(string s,M m,bool closed=true){
 if(s.Length>65536)throw new InvalidDataException();var deadline=Stopwatch.StartNew();
 if(Regex.IsMatch(s,@"^\s*,|,\s*$|,\s*,|,\s*[MLCzZ]|[MLCzZ]\s*,",RegexOptions.None,TimeSpan.FromMilliseconds(500)))throw new InvalidDataException();var token=new Regex(@"\G(?:,\s*)?([MLCzZ]|[+-]?(?:(?:\d+\.?\d*)|(?:\.\d+))(?:[eE][+-]?\d+)?)",RegexOptions.None,TimeSpan.FromMilliseconds(500));var t=new List<string>();int pos=0;while(pos<s.Length){if(deadline.ElapsedMilliseconds>2000)throw new InvalidDataException();while(pos<s.Length&&char.IsWhiteSpace(s[pos]))pos++;if(pos==s.Length)break;var z=token.Match(s,pos);if(!z.Success)throw new InvalidDataException();if(t.Count>=24576)throw new InvalidDataException();t.Add(z.Groups[1].Value);pos+=z.Length;}var result=new List<Op>();int i=0;while(i<t.Count){var k=t[i++];int count=k switch{"M"=>2,"L"=>2,"C"=>6,"z" or "Z"=>0,_=>throw new InvalidDataException()};if(i+count>t.Count)throw new InvalidDataException();var points=new List<double[]>();for(int j=0;j<count;j+=2){if(!double.TryParse(t[i++],NumberStyles.Float,CultureInfo.InvariantCulture,out double x)||!double.TryParse(t[i++],NumberStyles.Float,CultureInfo.InvariantCulture,out double y))throw new InvalidDataException();double u=m.A*x+m.C*y+m.E,v=m.B*x+m.D*y+m.F;if(!double.IsFinite(u)||!double.IsFinite(v)||Math.Abs(u)>1e8||Math.Abs(v)>1e8)throw new InvalidDataException();points.Add(new[]{u,v});}result.Add(new(k,points));if(result.Count>4096)throw new InvalidDataException();}
 if(result.Count<3||result[0].Kind!="M"||result.Skip(1).SkipLast(1).Any(x=>x.Kind is "M" or "z" or "Z")||(closed?result[^1].Kind is not ("z" or "Z"):result[^1].Kind is not ("L" or "C")))throw new InvalidDataException();return result;
}
record struct M(double A,double B,double C,double D,double E,double F);
record Op(string Kind,List<double[]> Points);
