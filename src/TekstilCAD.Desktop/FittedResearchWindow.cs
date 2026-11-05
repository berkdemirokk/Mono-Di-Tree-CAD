using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Media;
using System.Windows.Shapes;
using FittedResearch;

namespace TekstilCAD.Desktop;

// Fixed, synthetic-only catalogue. No body measurement or project document enters this window.
internal sealed class FittedResearchWindow : Window
{
    readonly StackPanel panels = new();
    long generation; bool closed;
    readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8) };
    internal FittedResearchWindow(Window owner)
    {
        Owner=owner; Title="Sentetik elbise araştırması"; Width=1050; Height=800;
        var root=new StackPanel { Margin=new Thickness(12) };
        Content=new ScrollViewer { VerticalScrollBarVisibility=ScrollBarVisibility.Auto, HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled, Content=root, Focusable=true };
        var header=new StackPanel(); root.Children.Add(header);
        header.Children.Add(new TextBlock { Text="SENTETİK · DOĞRULANMAMIŞ ARAŞTIRMA", FontWeight=FontWeights.Bold, FontSize=19, Foreground=Brushes.DarkRed, TextWrapping=TextWrapping.Wrap });
        header.Children.Add(new TextBlock { Text="Yalnız sabit kolsuz üst ve kalem etek örnekleri. Kişisel ölçü, prova ve dikiş uygunluğu doğrulanmadı. Çizim ölçekli değildir; CAD, dışa aktarma, baskı ve plotter için kullanılamaz.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,8) });
        var choice=new ComboBox { Margin=new Thickness(0,4,0,4), MinWidth=240, HorizontalAlignment=HorizontalAlignment.Left };
        AutomationProperties.SetName(choice,"Sentetik araştırma örneği");
        AutomationProperties.SetLiveSetting(status,AutomationLiveSetting.Polite);
        choice.Items.Add("Sentetik örnek 1"); choice.Items.Add("Sentetik örnek 2"); header.Children.Add(choice); header.Children.Add(status);
        root.Children.Add(panels);
        Closed+=(_,_)=> { closed=true; generation++; };
        choice.SelectionChanged+=async (_,_)=> {
            long run=++generation; panels.Children.Clear(); SetStatus("Sentetik araştırma hesaplanıyor…");
            string? id=choice.SelectedIndex switch { 0=>"synthetic-base",1=>"synthetic-distinct",_=>null };
            if(id is null) { SetStatus("Bilinmeyen örnek."); return; }
            choice.IsEnabled=false;
            try {
                var result=await Task.Run(()=> (Panels: FittedSyntheticAdapter.Create(id), Comparison: FittedSyntheticAdapter.Comparison(id)));
                if(closed || run!=generation) return;
                panels.Children.Add(CreateComparisonView(result.Comparison));
                foreach(var panel in result.Panels) panels.Children.Add(CreatePanelView(panel));
                SetStatus("Altı panel adayı · sonuç unresolved / Unverified. Her panel kendi görünümüne sığdırılmıştır.");
            } catch(Exception ex) when(ex is not (OutOfMemoryException or StackOverflowException or AccessViolationException)) {
                if(!closed && run==generation) { panels.Children.Clear(); SetStatus("Araştırma önizlemesi oluşturulamadı: "+ex.Message); }
            } finally { if(!closed && run==generation) choice.IsEnabled=true; }
        };
        choice.SelectedIndex=0;
    }
    private void SetStatus(string message)
    {
        status.Text=message;
        if(UIElementAutomationPeer.FromElement(status) is { } peer)
            peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
    internal static GroupBox CreateComparisonView(ImageSource image)
    {
        var content=new DockPanel();
        var description=new TextBlock { Text="Yalnız sağ arka gövde: mavi önce, kırmızı sonra (aynı ölçek). Ön ve diğer paneller değiştirilmez. Bu karşılaştırma tüm elbise, prova, çentik veya üretim sonucu değildir; eşleşme ve topoloji yalnız araştırma tanısıdır.", TextWrapping=TextWrapping.Wrap, Margin=new Thickness(8) };
        DockPanel.SetDock(description,Dock.Top); content.Children.Add(description);
        var view=new Image { Source=image, Stretch=Stretch.Uniform, Margin=new Thickness(15) };
        AutomationProperties.SetName(view,"Sağ arka gövde araştırması: mavi önce, kırmızı sonra; ölçekli üretim çizimi değil");
        content.Children.Add(view);
        return new GroupBox { Header="Birleşik bel ve yan eğriliği · sentetik araştırma", Height=360, Content=content, Margin=new Thickness(4) };
    }
    internal static GroupBox CreatePanelView(FittedSyntheticAdapter.Panel panel)
    {
        var group=new GroupBox { Header=panel.Name, Margin=new Thickness(4), Height=300 };
        var path=new System.Windows.Shapes.Path { Data=panel.Geometry, Stroke=Brushes.SteelBlue, StrokeThickness=1, Stretch=Stretch.Uniform, Margin=new Thickness(15) };
        AutomationProperties.SetName(path,$"{panel.Name}: sentetik, doğrulanmamış panel çizimi; ölçekli değil");
        var content=new DockPanel();
        var description=new TextBlock { Text=$"{panel.Name}: sentetik, doğrulanmamış panel çizimi; ölçekli değil", TextWrapping=TextWrapping.Wrap, Margin=new Thickness(8,4,8,4) };
        DockPanel.SetDock(description,Dock.Top); content.Children.Add(description); content.Children.Add(path);
        group.Content=content;
        return group;
    }
}

internal static class FittedSyntheticAdapter
{
    internal sealed record Panel(string Name,PathGeometry Geometry);
    internal sealed record Edge(string Kind,Point Start,Point End,Point[] Controls,double? ArcY);
    internal static IReadOnlyList<Panel> Create(string id)
    {
        if(id is not ("synthetic-base" or "synthetic-distinct")) throw new ArgumentException("Bilinmeyen sentetik örnek.");
        var body=SyntheticBodyCatalog.Create(id); var d=ResearchDesign.PinnedFitted;
        Edge F(FrontBodiceCandidate.Edge e)=>new(e.Kind,new(e.Start.X,e.Start.Y),new(e.End.X,e.End.Y),e.Controls.Select(c=>new Point(c.X,c.Y)).ToArray(),e.ArcControlY);
        Edge B(BackBodiceCandidate.Edge e)=>new(e.Kind,new(e.Start.X,e.Start.Y),new(e.End.X,e.End.Y),e.Controls.Select(c=>new Point(c.X,c.Y)).ToArray(),e.ArcControlY);
        Edge S(FrontSkirtCandidate.Edge e)=>new(e.Kind,new(e.Start.X,e.Start.Y),new(e.End.X,e.End.Y),e.Control is { } c ? [new(c.X,c.Y)] : [],null);
        Edge T(BackSkirtCandidate.Edge e)=>new(e.Kind,new(e.Start.X,e.Start.Y),new(e.End.X,e.End.Y),e.Control is { } c ? [new(c.X,c.Y)] : [],null);
        return Array.AsReadOnly(new[] {
            new Panel("Ön gövde · sağ",Geometry(FrontBodiceCandidate.CreateFromBody(body,d).Edges.Select(F).ToArray())),
            new Panel("Ön gövde · sol",Geometry(FrontBodiceCandidate.CreateLeftFromBody(body,d).Edges.Select(F).ToArray())),
            new Panel("Arka gövde · sağ",Geometry(BackBodiceCandidate.CreateFromBody(body,d,false).Edges.Select(B).ToArray())),
            new Panel("Arka gövde · sol",Geometry(BackBodiceCandidate.CreateFromBody(body,d,true).Edges.Select(B).ToArray())),
            new Panel("Ön etek",Geometry(FrontSkirtCandidate.CreateFromMatchedBody(body,d).Edges.Select(S).ToArray())),
            new Panel("Arka etek",Geometry(BackSkirtCandidate.CreateFromBody(body,d).Edges.Select(T).ToArray())) });
    }
    internal static ImageSource Comparison(string id)
    {
        var result=CombinedWaistCurvatureExperiment.Create(id,0.3,1.5);
        static PathGeometry Convert(IReadOnlyList<CombinedWaistCurvatureExperiment.SnapshotEdge> snapshot)=>Geometry(snapshot.Select(e=>new Edge(e.Kind,new Point(e.Ax,e.Ay),new Point(e.Bx,e.By),e.Controls.Select(c=>new Point(c.X,c.Y)).ToArray(),e.ArcY)).ToArray());
        var before=Convert(result.Contour.Before); var after=Convert(result.Contour.After);
        var drawing=new DrawingGroup();
        drawing.Children.Add(new GeometryDrawing(null,new Pen(Brushes.SteelBlue,0.12),before));
        drawing.Children.Add(new GeometryDrawing(null,new Pen(Brushes.Firebrick,0.12),after));
        drawing.Freeze();
        var image=new DrawingImage(drawing); image.Freeze(); return image;
    }
    internal static PathGeometry Geometry(IReadOnlyList<Edge> edges)
    {
        if(edges.Count==0) throw new ArgumentException("Boş panel.");
        static bool Finite(Point p)=>double.IsFinite(p.X)&&double.IsFinite(p.Y);
        var f=new PathFigure { StartPoint=edges[0].Start,IsClosed=false,IsFilled=false }; Point current=f.StartPoint;
        foreach(var e in edges) {
            if(!Finite(e.Start)||!Finite(e.End)||e.Start!=current||e.Controls.Any(c=>!Finite(c))) throw new ArgumentException("Geçersiz veya kopuk panel.");
            double dx=e.End.X-e.Start.X,dy=e.End.Y-e.Start.Y;
            Point Control(Point c)=>new(e.Start.X+dx*c.X-dy*c.Y,e.Start.Y+dy*c.X+dx*c.Y);
            if(e.Kind=="L" && e.Controls.Length==0) f.Segments.Add(new LineSegment(e.End,true));
            else if(e.Kind=="Q" && e.Controls.Length==1) f.Segments.Add(new QuadraticBezierSegment(Control(e.Controls[0]),e.End,true));
            else if(e.Kind=="C" && e.Controls.Length==2) f.Segments.Add(new BezierSegment(Control(e.Controls[0]),Control(e.Controls[1]),e.End,true));
            else if(e.Kind=="A" && e.Controls.Length==0 && e.ArcY is double y && double.IsFinite(y) && y!=0) {
                double radius=Math.Sqrt(dx*dx+dy*dy)*(.25+y*y)/(2*Math.Abs(y));
                if(!double.IsFinite(radius)||radius<=0) throw new ArgumentException("Geçersiz yay.");
                f.Segments.Add(new ArcSegment(e.End,new Size(radius,radius),0,Math.Abs(y)>.5,y>0?SweepDirection.Counterclockwise:SweepDirection.Clockwise,true));
            } else throw new ArgumentException("Desteklenmeyen kenar.");
            current=e.End;
        }
        if(current!=f.StartPoint) throw new ArgumentException("Açık panel.");
        // Source coordinates use an upward Y axis, as in the research SVG.
        var geometry=new PathGeometry(new[]{f}) { Transform=new ScaleTransform(1,-1) };
        geometry.Freeze(); // Allows the background result to cross to the UI dispatcher.
        return geometry;
    }
}
