using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Media;
using TekstilCAD.Core;

namespace TekstilCAD.Desktop;

/// <summary>Two contours plotted against one shared coordinate origin and scale.</summary>
internal sealed class BulkGradePreview : FrameworkElement
{
    private static readonly Brush BackgroundBrush=FrozenBrush(0x15,0x21,0x30);
    private static readonly Brush BaseBrush=FrozenBrush(0xff,0xc4,0x68);
    private static readonly Brush GradeBrush=FrozenBrush(0x39,0xd6,0xc6);
    private StreamGeometry? baseGeometry, gradeGeometry;
    private (Point Start,Point End)? baseGrain,gradeGrain;
    private Point[] baseMarks=[],gradeMarks=[];
    internal const int MaximumVisibleMarks=500;
    internal bool MarksSuppressed { get; private set; }
    private double xExtent,yExtent;
    internal Guid? SelectedId { get; private set; }
    internal bool HasGeometry => baseGeometry is not null && gradeGeometry is not null;

    internal BulkGradePreview()
    {
        Height=180; MinHeight=150; Margin=new Thickness(0,6,0,0); ClipToBounds=true;
        AutomationProperties.SetName(this,"Temel kalıp ve seçili beden karşılaştırması");
        Clear();
    }

    internal void Clear()
    {
        baseGeometry=null;gradeGeometry=null;baseGrain=null;gradeGrain=null;baseMarks=[];gradeMarks=[];MarksSuppressed=false;SelectedId=null;
        AutomationProperties.SetHelpText(this,"Karşılaştırma yok. Önce bedenleri hesaplayın ve listeden bir beden seçin.");
        InvalidateVisual();
    }

    internal void SetPieces(PatternPiece source,PatternPiece grade,string accessibleName)
    {
        Clear();
        if(source.Points.Count==0 || grade.Points.Count==0 || source.Points.Any(p=>!p.IsFinite) || grade.Points.Any(p=>!p.IsFinite))return;
        var minX=Math.Min(source.Points.Min(p=>p.X),grade.Points.Min(p=>p.X));
        var minY=Math.Min(source.Points.Min(p=>p.Y),grade.Points.Min(p=>p.Y));
        var maxX=Math.Max(source.Points.Max(p=>p.X),grade.Points.Max(p=>p.X));
        var maxY=Math.Max(source.Points.Max(p=>p.Y),grade.Points.Max(p=>p.Y));
        static bool ValidGrain(Grainline? line)=>line is not null && line.Start.IsFinite && line.End.IsFinite && line.Start!=line.End;
        var sourceGrainValid=ValidGrain(source.Grainline);var gradeGrainValid=ValidGrain(grade.Grainline);
        void Include(Point2 p)
        {
            minX=Math.Min(minX,p.X);minY=Math.Min(minY,p.Y);
            maxX=Math.Max(maxX,p.X);maxY=Math.Max(maxY,p.Y);
        }
        if(sourceGrainValid) { Include(source.Grainline!.Start);Include(source.Grainline.End); }
        if(gradeGrainValid) { Include(grade.Grainline!.Start);Include(grade.Grainline.End); }
        var span=Math.Max(maxX-minX,maxY-minY);
        if(!double.IsFinite(span))return;
        if(span==0)span=1;
        xExtent=(maxX-minX)/span;yExtent=(maxY-minY)/span;
        Point Normalize(Point2 v)=>new((v.X-minX)/span,(v.Y-minY)/span);
        StreamGeometry Build(PatternPiece p)
        {
            var result=new StreamGeometry();
            using(var context=result.Open())
            {
                context.BeginFigure(Normalize(p.Points[0]),false,p.Closed);
                for(int i=1;i<p.Points.Count;i++)context.LineTo(Normalize(p.Points[i]),true,false);
            }
            result.Freeze();return result;
        }
        baseGeometry=Build(source);gradeGeometry=Build(grade);SelectedId=grade.Id;
        if(sourceGrainValid)baseGrain=(Normalize(source.Grainline!.Start),Normalize(source.Grainline.End));
        if(gradeGrainValid)gradeGrain=(Normalize(grade.Grainline!.Start),Normalize(grade.Grainline.End));
        MarksSuppressed=(source.Notches?.Count ?? 0)>MaximumVisibleMarks || (grade.Notches?.Count ?? 0)>MaximumVisibleMarks;
        if(!MarksSuppressed)
        {
            baseMarks=source.Notches?.Select(mark=>Normalize(mark.Position)).ToArray() ?? [];
            gradeMarks=grade.Notches?.Select(mark=>Normalize(mark.Position)).ToArray() ?? [];
        }
        var sourceDescription=source.Grainline is null ? "Temel kalıpta iplik yönü yok." : sourceGrainValid ? "Temel iplik yönü kalın sarı çizgidir." : "Temel iplik yönü geçersiz olduğu için gösterilemedi.";
        var gradeDescription=grade.Grainline is null ? "Seçili bedende iplik yönü yok." : gradeGrainValid ? "Seçili bedenin iplik yönü kesikli turkuaz çizgidir; konturla ilişkisini kontrol edin." : "Seçili bedenin iplik yönü geçersiz olduğu için gösterilemedi.";
        var marksDescription=MarksSuppressed ? $"İşaretler {MaximumVisibleMarks} sınırı nedeniyle gösterilmiyor." : "Köşe işaretleri temel kalıpta büyük sarı, seçili bedende küçük turkuaz halkalardır; üst üste gelebilir.";
        AutomationProperties.SetHelpText(this,$"Temel kalıp kesikli sarı, seçili beden düz turkuaz çizgidir. {sourceDescription} {gradeDescription} {marksDescription} İşaretler fiziksel çentik değildir. Çizgiler üst üste olabilir. Seçili beden: {accessibleName}. İkisi aynı koordinat sistemi ve ölçekle gösterilir. Ekran görüntüsü fiziksel ölçü değildir.");
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);var w=ActualWidth;var h=ActualHeight;
        if(w<=0 || h<=0)return;
        dc.DrawRoundedRectangle(BackgroundBrush,null,new Rect(0,0,w,h),5,5);
        if(!HasGeometry)return;
        // The stored extents are normalised by the larger model dimension.
        // Fit each non-zero dimension to the available viewport independently,
        // then use one scale for both axes so geometry is never distorted. The
        // padding also contains the 3-pixel grain endpoint markers and strokes.
        const double pad=16;
        var availableWidth=w-2*pad;var availableHeight=h-2*pad;
        if(availableWidth<=0 || availableHeight<=0)return;
        var horizontalScale=xExtent>0 ? availableWidth/xExtent : double.PositiveInfinity;
        var verticalScale=yExtent>0 ? availableHeight/yExtent : double.PositiveInfinity;
        var scale=Math.Min(horizontalScale,verticalScale);
        // A degenerate point has no preferred dimension; centre it safely.
        if(double.IsPositiveInfinity(scale))scale=Math.Min(availableWidth,availableHeight);
        if(!double.IsFinite(scale) || scale<=0)return;
        // Match the main canvas: model Y increases downwards on screen.
        // This affects presentation only; both paths keep their original data.
        var x=(w-scale*xExtent)/2;var y=(h-scale*yExtent)/2;
        var basePen=new Pen(BaseBrush,2/scale) { DashStyle=DashStyles.Dash };
        var gradePen=new Pen(GradeBrush,1.5/scale);
        dc.PushTransform(new MatrixTransform(scale,0,0,scale,x,y));
        dc.DrawGeometry(null,basePen,baseGeometry);dc.DrawGeometry(null,gradePen,gradeGeometry);
        // Draw the wider base mark first. A narrower dashed grade mark stays
        // distinguishable even when both marks occupy the same coordinates.
        if(baseGrain is { } b)
        {
            var pen=new Pen(BaseBrush,4/scale);
            dc.DrawLine(pen,b.Start,b.End);
            dc.DrawEllipse(BaseBrush,null,b.Start,3/scale,3/scale);
            dc.DrawEllipse(BaseBrush,null,b.End,3/scale,3/scale);
        }
        if(gradeGrain is { } g)
        {
            var pen=new Pen(GradeBrush,2/scale) { DashStyle=DashStyles.Dash };
            dc.DrawLine(pen,g.Start,g.End);
            dc.DrawEllipse(GradeBrush,null,g.Start,1.5/scale,1.5/scale);
            dc.DrawEllipse(GradeBrush,null,g.End,1.5/scale,1.5/scale);
        }
        var baseMarkPen=new Pen(BaseBrush,2/scale);var gradeMarkPen=new Pen(GradeBrush,2/scale);
        foreach(var p in baseMarks)dc.DrawEllipse(null,baseMarkPen,p,6/scale,6/scale);
        foreach(var p in gradeMarks)dc.DrawEllipse(null,gradeMarkPen,p,3/scale,3/scale);
        dc.Pop();
    }
    protected override AutomationPeer OnCreateAutomationPeer()=>new PreviewPeer(this);
    private sealed class PreviewPeer(BulkGradePreview owner):FrameworkElementAutomationPeer(owner)
    {
        protected override string GetClassNameCore()=>nameof(BulkGradePreview);
        protected override AutomationControlType GetAutomationControlTypeCore()=>AutomationControlType.Image;
    }
    private static Brush FrozenBrush(byte r,byte g,byte b) { var brush=new SolidColorBrush(Color.FromRgb(r,g,b));brush.Freeze();return brush; }
}
