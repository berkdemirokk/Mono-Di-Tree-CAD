using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;
using TekstilCAD.Core;

namespace TekstilCAD.Desktop;

/// <summary>Ephemeral visualization only; it has no document or export reference.</summary>
internal sealed class OffsetUnverifiedPreviewWindow : Window
{
    internal static Point MapPoint(Point2 p,double cx,double cy,double scale,double width,double height,double panX,double panY)
        => new(width/2+panX+(p.X-cx)*scale,height/2+panY+(p.Y-cy)*scale);

    internal OffsetUnverifiedPreviewWindow(Window owner, IReadOnlyList<Point2> source, IReadOnlyList<Point2> candidate)
    {
        Owner=owner; Title="Doğrulanmamış dikiş payı önizlemesi";
        Width=900; Height=700; MinWidth=450; MinHeight=350;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var panel=new DockPanel { Margin=new Thickness(12) };
        var warning=new TextBlock { TextWrapping=TextWrapping.Wrap, Focusable=true, Margin=new Thickness(0,0,0,10),
            Text="DOĞRULANMAMIŞ ÖNİZLEME — Kaynak: mavi; aday: turuncu. Bu görüntü ölçü veya çentik korunumu kanıtı değildir. Projeye eklenmez, kaydedilmez ve dışa aktarılmaz. Yakınlaştırmak için fare tekerleğini, görüntüyü taşımak için sürüklemeyi kullanın." };
        AutomationProperties.SetName(warning,warning.Text);
        DockPanel.SetDock(warning,Dock.Top); panel.Children.Add(warning);
        var close=new Button { Content="Kapat", IsCancel=true, HorizontalAlignment=HorizontalAlignment.Right, MinWidth=90, Margin=new Thickness(0,10,0,0) };
        close.Click += (_,_) => Close(); DockPanel.SetDock(close,Dock.Bottom); panel.Children.Add(close);
        var drawing=new PreviewDrawing(source,candidate);
        AutomationProperties.SetName(drawing,"Doğrulanmamış kaynak ve aday kontur görseli");
        AutomationProperties.SetHelpText(drawing,"Artı ve eksi ile yakınlaştırın; ok tuşlarıyla taşıyın, Home ile ilk görünüme dönün. Renk: kaynak mavi, aday turuncu. Bu görüntü güvenlik kanıtı değildir.");
        panel.Children.Add(drawing); Content=panel;
    }

    private sealed class PreviewDrawing : FrameworkElement
    {
        private readonly Point2[] source, candidate;
        private readonly double cx,cy,span;
        private double zoom=1, panX,panY;
        private Point? drag;
        internal PreviewDrawing(IReadOnlyList<Point2> source,IReadOnlyList<Point2> candidate)
        {
            this.source=source.ToArray(); this.candidate=candidate.ToArray();
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
        }
    }
}


