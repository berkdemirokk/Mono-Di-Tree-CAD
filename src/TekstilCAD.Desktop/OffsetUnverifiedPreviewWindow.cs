using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;
using TekstilCAD.Core;
using TekstilCAD.Patterns;

namespace TekstilCAD.Desktop;

/// <summary>Ephemeral visualization only; it has no document or export reference.</summary>
internal sealed class OffsetUnverifiedPreviewWindow : Window
{
    internal Button? FocusSampleForVerification { get; private set; }
    internal Point2? MarkedSampleForVerification { get; private set; }
    internal static Point MapPoint(Point2 p,double cx,double cy,double scale,double width,double height,double panX,double panY)
        => new(width/2+panX+(p.X-cx)*scale,height/2+panY+(p.Y-cy)*scale);

    internal OffsetUnverifiedPreviewWindow(Window owner, IReadOnlyList<Point2> source, IReadOnlyList<Point2> candidate,Point2? samplePoint=null,int? sampleIndex=null,OffsetLocalSample? sampleKind=null)
    {
        Owner=owner; Title="Doğrulanmamış dikiş payı önizlemesi";
        Width=900; Height=700; MinWidth=450; MinHeight=350;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var panel=new DockPanel { Margin=new Thickness(12) };
        var warning=new TextBlock { TextWrapping=TextWrapping.Wrap, Focusable=true, Margin=new Thickness(0,0,0,10),
            Text="DOĞRULANMAMIŞ ÖNİZLEME — Kaynak: mavi; aday: turuncu. Bu görüntü ölçü veya çentik korunumu kanıtı değildir. Projeye eklenmez, kaydedilmez ve dışa aktarılmaz. Yakınlaştırmak için fare tekerleğini, görüntüyü taşımak için sürüklemeyi kullanın." };
        AutomationProperties.SetName(warning,warning.Text);
        DockPanel.SetDock(warning,Dock.Top); panel.Children.Add(warning);
        var sourceSnapshot=source.ToArray();var candidateSnapshot=candidate.ToArray();
        var sample=samplePoint is { } p && sampleIndex is >=0 && sampleKind is not null ? p : (Point2?)null;
        MarkedSampleForVerification=sample;
        if(sample is not null)
        {
            warning.Text+=$" Mor halka, ızgaraya yuvarlanmış kaynakta ilk doğrulanamayan {(sampleKind==OffsetLocalSample.Vertex ? "köşeyi" : "kenar orta noktasını")} gösterir (başlangıç indeksi {sampleIndex}; sıfırdan başlar). Bu tek başına kusur kanıtı değildir.";
            AutomationProperties.SetName(warning,warning.Text);
        }
        var actions=new WrapPanel { HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,10,0,0) };
        var drawing=new PreviewDrawing(sourceSnapshot,candidateSnapshot,sample);
        if(sample is not null)
        {
            var focus=new Button { Content="Örneğe odaklan",MinWidth=140,Margin=new Thickness(0,0,8,0) };
            AutomationProperties.SetHelpText(focus,"Doğrulanamayan kaynak örneğini merkeze alır; sonuç veya üretim onayı vermez.");
            focus.Click+=(_,_)=>drawing.FocusSample();FocusSampleForVerification=focus;actions.Children.Add(focus);
        }
        var measure=new Button { Content="Seçili noktaları ölç…",MinWidth=160,Margin=new Thickness(0,0,8,0) };
        AutomationProperties.SetName(measure,"Doğrulanmamış adayın seçili noktalarını ölç");
        measure.Click+=(_,_)=>new OffsetPreviewMeasurementWindow(this,sourceSnapshot,candidateSnapshot).ShowDialog();actions.Children.Add(measure);
        var close=new Button { Content="Kapat", IsCancel=true, MinWidth=90 };
        close.Click += (_,_) => Close();actions.Children.Add(close);DockPanel.SetDock(actions,Dock.Bottom);panel.Children.Add(actions);
        AutomationProperties.SetName(drawing,"Doğrulanmamış kaynak ve aday kontur görseli");
        AutomationProperties.SetHelpText(drawing,"Artı ve eksi ile yakınlaştırın; ok tuşlarıyla taşıyın, Home ile ilk görünüme dönün. Renk: kaynak mavi, aday turuncu, varsa doğrulanamayan örnek mor. Bu görüntü güvenlik kanıtı değildir.");
        panel.Children.Add(drawing); Content=panel;
    }

    private sealed class PreviewDrawing : FrameworkElement
    {
        private readonly Point2[] source, candidate;
        private readonly double cx,cy,span;
        private readonly Point2? sample;
        private double zoom=1, panX,panY;
        private Point? drag;
        internal PreviewDrawing(IReadOnlyList<Point2> source,IReadOnlyList<Point2> candidate,Point2? sample)
        {
            this.source=source.ToArray(); this.candidate=candidate.ToArray();
            this.sample=sample;
            var all=this.source.Concat(this.candidate).ToArray();
            double minX=all.Min(p=>p.X),maxX=all.Max(p=>p.X),minY=all.Min(p=>p.Y),maxY=all.Max(p=>p.Y);
            cx=(minX+maxX)/2; cy=(minY+maxY)/2; span=Math.Max(Math.Max(maxX-minX,maxY-minY),1e-9);
            ClipToBounds=true; Focusable=true;
            MouseWheel += (_,e)=> { var old=zoom; zoom=Math.Clamp(zoom*(e.Delta>0?1.25:0.8),1,10000);
                var p=e.GetPosition(this); double factor=zoom/old;
                panX=p.X-ActualWidth/2-(p.X-ActualWidth/2-panX)*factor;
                panY=p.Y-ActualHeight/2-(p.Y-ActualHeight/2-panY)*factor; InvalidateVisual(); e.Handled=true; };
            MouseLeftButtonDown += (_,e)=> { drag=e.GetPosition(this); CaptureMouse(); };
            MouseMove += (_,e)=> { if(drag is not Point old) return; var p=e.GetPosition(this); panX+=p.X-old.X; panY+=p.Y-old.Y; drag=p; InvalidateVisual(); };
            MouseLeftButtonUp += (_,_)=> { drag=null; ReleaseMouseCapture(); };
            LostMouseCapture += (_,_)=>drag=null;
            KeyDown += (_,e)=> {
                switch(e.Key)
                {
                    case Key.Add: case Key.OemPlus: zoom=Math.Min(10000,zoom*1.25); break;
                    case Key.Subtract: case Key.OemMinus: zoom=Math.Max(1,zoom/1.25); break;
                    case Key.Left: panX+=30; break; case Key.Right: panX-=30; break;
                    case Key.Up: panY+=30; break; case Key.Down: panY-=30; break;
                    case Key.Home: zoom=1;panX=panY=0;break;
                    default: return;
                }
                InvalidateVisual(); e.Handled=true;
            };
        }
        internal void FocusSample()
        {
            if(sample is not Point2 point)return;
            zoom=Math.Max(zoom,80);var scale=Math.Max(1,Math.Min(ActualWidth,ActualHeight)-30)/span*zoom;
            panX=-(point.X-cx)*scale;panY=-(point.Y-cy)*scale;InvalidateVisual();
        }
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc); dc.DrawRectangle(Brushes.White,null,new Rect(RenderSize));
            double scale=Math.Max(1,Math.Min(ActualWidth,ActualHeight)-30)/span*zoom;
            Point Map(Point2 p)=>MapPoint(p,cx,cy,scale,ActualWidth,ActualHeight,panX,panY);
            void Draw(Point2[] points,Brush brush,double thickness)
            {
                if(points.Length<2)return;
                var geometry=new StreamGeometry(); using(var c=geometry.Open())
                { c.BeginFigure(Map(points[0]),false,true); for(int i=1;i<points.Length;i++)c.LineTo(Map(points[i]),true,false); }
                geometry.Freeze(); dc.DrawGeometry(null,new Pen(brush,thickness),geometry);
            }
            Draw(source,Brushes.SteelBlue,1.5); Draw(candidate,Brushes.DarkOrange,1.5);
            if(sample is Point2 samplePoint)
            {
                var p=Map(samplePoint);var pen=new Pen(Brushes.MediumVioletRed,2);
                dc.DrawEllipse(null,pen,p,8,8);dc.DrawLine(pen,new Point(p.X-12,p.Y),new Point(p.X+12,p.Y));dc.DrawLine(pen,new Point(p.X,p.Y-12),new Point(p.X,p.Y+12));
            }
        }
    }
}


