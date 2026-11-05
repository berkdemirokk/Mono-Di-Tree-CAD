using System.Globalization;
using System.Text;
using FittedResearch;

// This program intentionally exposes no measurement or arbitrary output path input.
if (args.Length != 1 || args[0] is not ("synthetic-base" or "synthetic-distinct" or "synthetic-third"))
{
    Console.Error.WriteLine("Usage: SyntheticSvg synthetic-base|synthetic-distinct|synthetic-third");
    return 2;
}
try
{
    var body = SyntheticBodyCatalog.Create(args[0]);
    var design = ResearchDesign.PinnedFitted;
    var fr = FrontBodiceCandidate.CreateFromBody(body, design);
    var fl = FrontBodiceCandidate.CreateLeftFromBody(body, design);
    var br = BackBodiceCandidate.CreateFromBody(body, design, false);
    var bl = BackBodiceCandidate.CreateFromBody(body, design, true);
    var sf = FrontSkirtCandidate.CreateFromMatchedBody(body, design);
    var sb = BackSkirtCandidate.CreateFromBody(body, design);
    E F(FrontBodiceCandidate.Edge e) => new(e.Kind,e.Start.X,e.Start.Y,e.End.X,e.End.Y,e.Controls.Select(c => (c.X,c.Y)).ToArray(),e.ArcControlY);
    E B(BackBodiceCandidate.Edge e) => new(e.Kind,e.Start.X,e.Start.Y,e.End.X,e.End.Y,e.Controls.Select(c => (c.X,c.Y)).ToArray(),e.ArcControlY);
    E S(FrontSkirtCandidate.Edge e) => new(e.Kind,e.Start.X,e.Start.Y,e.End.X,e.End.Y,e.Control is { } c ? [(c.X,c.Y)] : [],null);
    E T(BackSkirtCandidate.Edge e) => new(e.Kind,e.Start.X,e.Start.Y,e.End.X,e.End.Y,e.Control is { } c ? [(c.X,c.Y)] : [],null);
    var panels = new (string Name,E[] Edges)[] { ("right_ftorso",fr.Edges.Select(F).ToArray()),("left_ftorso",fl.Edges.Select(F).ToArray()),("right_btorso",br.Edges.Select(B).ToArray()),("left_btorso",bl.Edges.Select(B).ToArray()),("skirt_front",sf.Edges.Select(S).ToArray()),("skirt_back",sb.Edges.Select(T).ToArray()) };
    string svg = Render(panels,args[0]);
    string file = SafeResearchOutput.WriteNew(args[0], svg);
    Console.WriteLine($"SENTETIK / DOGRULANMAMIS; uretim ve plotter icin degildir: {file}");
    return 0;
}
catch (Exception e) { Console.Error.WriteLine($"Arastirma cizimi olusturulamadi: {e.Message}"); return 1; }

static string N(double v) => double.IsFinite(v) ? v.ToString("G17",CultureInfo.InvariantCulture) : throw new InvalidDataException("Non-finite coordinate");
static string Render((string Name,E[] Edges)[] panels,string id)
{
    var s = new StringBuilder("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 1200 850\" role=\"img\"><title>Sentetik dogrulanmamis arastirma</title><rect width=\"1200\" height=\"850\" fill=\"white\"/><text x=\"20\" y=\"30\" font-family=\"sans-serif\" font-size=\"20\" fill=\"#a00\">SENTETIK / DOGRULANMAMIS - URETIM VE PLOTTER ICIN DEGIL</text>");
    s.Append($"<text x=\"20\" y=\"55\" font-family=\"sans-serif\">{id} - yerel panel koordinatlari; olcekli kalip degildir</text>");
    for (int i=0;i<panels.Length;i++)
    {
        var (name,edges)=panels[i]; if(edges.Length==0) throw new InvalidDataException("Empty panel");
        // Fixed viewport deliberately contains no physical units or print scale.
        double ox=(i%3)*400+200, oy=(i/3)*390+250;
        s.Append($"<text x=\"{i%3*400+20}\" y=\"{i/3*390+85}\" font-family=\"sans-serif\">{name}</text><g transform=\"translate({ox} {oy}) scale(2 -2)\" fill=\"none\" stroke=\"#155a85\" stroke-width=\"0.7\"><path d=\"M {N(edges[0].Ax)} {N(edges[0].Ay)} ");
        foreach(var e in edges)
        {
            double dx=e.Bx-e.Ax,dy=e.By-e.Ay;
            string P((double X,double Y) c)=>$"{N(e.Ax+dx*c.X-dy*c.Y)} {N(e.Ay+dy*c.X+dx*c.Y)}";
            string end=$"{N(e.Bx)} {N(e.By)}";
            if(e.Kind=="L" && e.Controls.Length==0) s.Append($"L {end} ");
            else if(e.Kind=="Q" && e.Controls.Length==1) s.Append($"Q {P(e.Controls[0])} {end} ");
            else if(e.Kind=="C" && e.Controls.Length==2) s.Append($"C {P(e.Controls[0])} {P(e.Controls[1])} {end} ");
            else if(e.Kind=="A" && e.ArcY is double y && y!=0) { double radius=Math.Sqrt(dx*dx+dy*dy)*(0.25+y*y)/(2*Math.Abs(y)); s.Append($"A {N(radius)} {N(radius)} 0 {(Math.Abs(y)>.5?1:0)} {(y>0?0:1)} {end} "); }
            else throw new InvalidDataException("Unsupported edge");
        }
        s.Append("\"/></g>");
    }
    return s.Append("</svg>").ToString();
}
internal sealed record E(string Kind,double Ax,double Ay,double Bx,double By,(double X,double Y)[] Controls,double? ArcY);
