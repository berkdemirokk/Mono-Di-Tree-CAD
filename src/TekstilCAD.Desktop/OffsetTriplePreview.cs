using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;
using TekstilCAD.Core;

namespace TekstilCAD.Desktop;

/// <summary>Read-only contour selection; a click succeeds only for one nearby vertex.</summary>
internal sealed class OffsetTriplePreview : FrameworkElement
{
    private readonly Point2[] points;private readonly double cx,cy,span;
    private readonly int[] chosen=[-1,-1,-1];
    private double zoom=1,panX,panY;
    internal event Action<int>? PointRequested;
    internal IReadOnlyList<int> Selected=>chosen;
    internal OffsetTriplePreview(IReadOnlyList<Point2> snapshot,string name)
    {
        points=snapshot.ToArray();Height=210;MinHeight=150;ClipToBounds=true;Focusable=true;
        if(points.Length==0 || points.Any(p=>!p.IsFinite))throw new ArgumentException("Önizleme noktaları geçersiz.");
        var minX=points.Min(p=>p.X);var maxX=points.Max(p=>p.X);var minY=points.Min(p=>p.Y);var maxY=points.Max(p=>p.Y);
        cx=minX+(maxX-minX)/2;cy=minY+(maxY-minY)/2;span=Math.Max(Math.Max(maxX-minX,maxY-minY),1e-9);
        AutomationProperties.SetName(this,name);AutomationProperties.SetHelpText(this,"Yakınlaştırmak için fare tekerleği veya artı/eksi, kaydırmak için ok tuşları; yakın veya çakışık noktaları listeden seçin.");
        MouseWheel+=(_,e)=> { var old=zoom;zoom=Math.Clamp(zoom*(e.Delta>0?1.25:0.8),1,10000);var p=e.GetPosition(this);var ratio=zoom/old;
            panX=p.X-ActualWidth/2-(p.X-ActualWidth/2-panX)*ratio;panY=p.Y-ActualHeight/2-(p.Y-ActualHeight/2-panY)*ratio;InvalidateVisual();e.Handled=true; };
        KeyDown+=(_,e)=> { switch(e.Key) { case Key.Add:case Key.OemPlus:zoom=Math.Min(10000,zoom*1.25);break;case Key.Subtract:case Key.OemMinus:zoom=Math.Max(1,zoom/1.25);break;
            case Key.Left:panX+=30;break;case Key.Right:panX-=30;break;case Key.Up:panY+=30;break;case Key.Down:panY-=30;break;case Key.Home:zoom=1;panX=panY=0;break;default:return; }InvalidateVisual();e.Handled=true; };
    }
    internal void SetSelection(int role,int index)
    {
        chosen[role]=index>=0 && index<points.Length ? index : -1;
        AutomationProperties.SetHelpText(this,$"A {chosen[0]}, T {chosen[1]}, B {chosen[2]}; −1 seçilmedi. Fare tekerleği veya artı/eksi ile yakınlaştırın, oklarla kaydırın; yakın noktaları listeden seçin.");
        InvalidateVisual();
    }
    internal void RequestPointForVerification(int index)=>PointRequested?.Invoke(index);
    private double Scale=>Math.Max(0,Math.Min(ActualWidth-40,ActualHeight-40))/span*zoom;
    internal Point Map(Point2 p)=>new(ActualWidth/2+panX+(p.X-cx)*Scale,ActualHeight/2+panY+(p.Y-cy)*Scale);
    internal int HitTestPoint(Point cursor)
    {
        if(Scale<=0 || !double.IsFinite(cursor.X) || !double.IsFinite(cursor.Y))return -1;
        var found=-1;for(var i=0;i<points.Length;i++) { var p=Map(points[i]);var dx=p.X-cursor.X;var dy=p.Y-cursor.Y;
            if(dx*dx+dy*dy>81)continue;if(found>=0)return -2;found=i; }
        return found;
    }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e) { base.OnMouseLeftButtonDown(e);Focus();PointRequested?.Invoke(HitTestPoint(e.GetPosition(this)));e.Handled=true; }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);var w=ActualWidth;var h=ActualHeight;if(w<=0 || h<=0)return;
        dc.DrawRectangle(Brushes.WhiteSmoke,null,new Rect(0,0,w,h));if(Scale<=0)return;
        var geometry=new StreamGeometry();using(var c=geometry.Open()) { c.BeginFigure(Map(points[0]),false,true);for(var i=1;i<points.Length;i++)c.LineTo(Map(points[i]),true,false); }
        geometry.Freeze();dc.DrawGeometry(null,new Pen(Brushes.SlateGray,1.5),geometry);
        foreach(var point in points)dc.DrawEllipse(Brushes.Gray,null,Map(point),2,2);
        Brush[] colors=[Brushes.RoyalBlue,Brushes.DarkOrange,Brushes.ForestGreen];string[] names=["A","T","B"];
        for(var i=0;i<3;i++)if(chosen[i]>=0)
        {
            var point=Map(points[chosen[i]]);if(point.X < -10 || point.X > w+10 || point.Y < -10 || point.Y > h+10)continue;dc.DrawEllipse(Brushes.White,new Pen(colors[i],2.5),point,7,7);
            var text=new FormattedText($"{names[i]} {chosen[i]}",CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),12,colors[i],VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(text,new Point(Math.Clamp(point.X+8,0,Math.Max(0,w-text.Width)),Math.Clamp(point.Y-18+i*14,0,Math.Max(0,h-text.Height))));
        }
    }
}
