using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using TekstilCAD.Core;

namespace TekstilCAD.Desktop;

/// <summary>Read-only comparison of two explicitly selected polygonal contour paths.</summary>
internal sealed class SeamLengthComparisonWindow : Window
{
    private readonly MainWindow workspace;
    private readonly ProjectDocument original, snapshot;
    private readonly Guid? selectedId, workshopSourceId;
    private readonly Func<Guid?> workshopSource;
    private readonly DispatcherTimer monitor=new() { Interval=TimeSpan.FromMilliseconds(400) };
    private readonly Side first,second;
    private readonly TextBlock result=new() { TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,0) };
    private readonly Button compare=new() { Content="Kenarları karşılaştır",Padding=new Thickness(14,8,14,8),Margin=new Thickness(0,10,0,0),HorizontalAlignment=HorizontalAlignment.Left };
    private readonly TextBox expectedEase=new() { Text="0",Width=120,HorizontalAlignment=HorizontalAlignment.Left };
    private readonly TextBox tolerance=new() { Text="0",Width=120,HorizontalAlignment=HorizontalAlignment.Left };
    private bool stale;
    private sealed record PieceChoice(PatternPiece Piece,string Label);
    private sealed record PointChoice(int Index,string Label);
    private sealed class Side
    {
        internal readonly ComboBox Piece=new() { DisplayMemberPath=nameof(PieceChoice.Label),MinWidth=240 };
        internal readonly ComboBox Start=Selector(),End=Selector();
        internal readonly ComboBox Direction=new() { ItemsSource=new[]{"İleri (indeks artar)","Geri (indeks azalır)"},SelectedIndex=0 };
        internal readonly PathPreview Preview=new();
        private static ComboBox Selector()
        {
            var box=new ComboBox { DisplayMemberPath=nameof(PointChoice.Label),IsTextSearchEnabled=true,MaxDropDownHeight=220 };
            box.ItemsPanel=new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingStackPanel)));
            VirtualizingPanel.SetIsVirtualizing(box,true);ScrollViewer.SetCanContentScroll(box,true);return box;
        }
    }
    internal SeamLengthComparisonWindow(MainWindow workspace,Window owner,Func<Guid?> workshopSource)
    {
        this.workspace=workspace;this.workshopSource=workshopSource;
        original=workspace.Document;snapshot=original.Clone();selectedId=workspace.SelectedId;workshopSourceId=workshopSource();
        Owner=owner;Title="Dikiş kenarı uzunluklarını karşılaştır";Width=980;Height=740;MinWidth=650;MinHeight=500;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;StudioTheme.Apply(this);
        var root=new DockPanel { Margin=new Thickness(16) };Content=root;
        var close=new Button { Content="Kapat",IsCancel=true,Padding=new Thickness(14,7,14,7),HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,8,0,0) };
        close.Click+=(_,_)=>Close();DockPanel.SetDock(close,Dock.Bottom);root.Children.Add(close);
        var body=new StackPanel();root.Children.Add(new ScrollViewer { Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled });
        body.Children.Add(new TextBlock { Text="Dikiş kenarı uzunluklarını karşılaştır",FontSize=20,FontWeight=FontWeights.SemiBold });
        body.Children.Add(new TextBlock { Text="İki parçanın mevcut kontur noktaları arasındaki çoklu çizgi yolunu karşılaştırır. Kapalı konturda yönü seçin; açık konturda iki nokta arasındaki tek yol kullanılır. Kapanışın tekrarlanan son noktası ilk noktayla aynıdır; aynı nokta seçimi tam tur değil sıfırdır. Parça başına en fazla 10.000 kayıtlı nokta desteklenir. Önizlemede kırmızı seçilen yolu, mavi başlangıcı, yeşil bitişi gösterir. Sonuç fiziksel ölçüm veya üretim kabulü değildir.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,10) });
        var choices=snapshot.Pieces.Select(p=>new PieceChoice(p,$"{p.Name} · {p.Id:D}")).ToArray();
        first=new Side();second=new Side();
        var grid=new Grid();grid.ColumnDefinitions.Add(new ColumnDefinition());grid.ColumnDefinitions.Add(new ColumnDefinition());
        AddSide(grid,first,"1. parça",choices,0);AddSide(grid,second,"2. parça",choices,1);body.Children.Add(grid);
        AutomationProperties.SetName(result,"Dikiş kenarı karşılaştırma sonucu");AutomationProperties.SetLiveSetting(result,AutomationLiveSetting.Polite);
        body.Children.Add(new TextBlock { Text="Hedef fark (2 − 1), mm · ikinci kenar daha kısaysa negatif",Margin=new Thickness(0,10,0,2) });
        AutomationProperties.SetName(expectedEase,"Hedef fark, milimetre");body.Children.Add(expectedEase);
        body.Children.Add(new TextBlock { Text="İzin verilen mutlak sapma, mm",Margin=new Thickness(0,6,0,2) });
        AutomationProperties.SetName(tolerance,"İzin verilen mutlak sapma, milimetre");body.Children.Add(tolerance);
        expectedEase.TextChanged+=(_,_)=>ClearResult();tolerance.TextChanged+=(_,_)=>ClearResult();
        compare.Click+=(_,_)=>Calculate();body.Children.Add(compare);body.Children.Add(result);
        if(choices.Length>0)first.Piece.SelectedIndex=Math.Max(0,Array.FindIndex(choices,c=>c.Piece.Id==workshopSourceId));
        if(choices.Length>0)second.Piece.SelectedIndex=choices.Length>1 ? (first.Piece.SelectedIndex+1)%choices.Length : 0;
        ClearResult();monitor.Tick+=(_,_)=>CheckCurrent();Loaded+=(_,_)=>monitor.Start();Closed+=(_,_)=>monitor.Stop();
    }
    private void AddSide(Grid grid,Side side,string title,PieceChoice[] choices,int column)
    {
        var panel=new StackPanel { Margin=new Thickness(column==0?0:8,0,column==0?8:0,0) };Grid.SetColumn(panel,column);grid.Children.Add(panel);
        panel.Children.Add(new TextBlock { Text=title,FontWeight=FontWeights.SemiBold });
        AutomationProperties.SetName(side.Piece,title);side.Piece.ItemsSource=choices;panel.Children.Add(side.Piece);
        panel.Children.Add(new TextBlock { Text="Başlangıç noktası (indeks · koordinat)",Margin=new Thickness(0,6,0,2) });AutomationProperties.SetName(side.Start,title+" başlangıç noktası");panel.Children.Add(side.Start);
        panel.Children.Add(new TextBlock { Text="Bitiş noktası (indeks · koordinat)",Margin=new Thickness(0,6,0,2) });AutomationProperties.SetName(side.End,title+" bitiş noktası");panel.Children.Add(side.End);
        panel.Children.Add(new TextBlock { Text="Kapalı kontur yönü",Margin=new Thickness(0,6,0,2) });AutomationProperties.SetName(side.Direction,title+" kapalı kontur yönü");panel.Children.Add(side.Direction);
        side.Preview.Height=170;side.Preview.Margin=new Thickness(0,8,0,0);AutomationProperties.SetName(side.Preview,title+" seçilen kontur yolu önizlemesi");panel.Children.Add(side.Preview);
        side.Piece.SelectionChanged+=(_,_)=> { var p=(side.Piece.SelectedItem as PieceChoice)?.Piece;var list=p is { Points.Count: <= ContourMeasurement.MaxComparisonVertices } ? p.Points.Select((point,i)=>new PointChoice(i,$"{i} · X {MeasurementInput.Format(point.X)} · Y {MeasurementInput.Format(point.Y)} mm"+(p.Closed && i==p.Points.Count-1 && i>0 && point==p.Points[0]?" · ilk nokta ile aynı":""))).ToArray() : [];side.Start.ItemsSource=list;side.End.ItemsSource=list;side.Start.SelectedIndex=list.Length>0?0:-1;side.End.SelectedIndex=list.Length>1?1:-1;side.Direction.IsEnabled=p?.Closed==true;ClearResult(); };
        side.Start.SelectionChanged+=(_,_)=>ClearResult();side.End.SelectionChanged+=(_,_)=>ClearResult();side.Direction.SelectionChanged+=(_,_)=>ClearResult();
    }
    private void ClearResult()
    {
        if(stale)return;result.Text="Karşılaştırma için iki yolu seçip düğmeye basın.";first?.Preview.Clear();second?.Preview.Clear();
    }
    private bool CheckCurrent()
    {
        if(stale)return false;
        if(!ReferenceEquals(workspace.Document,original) || !DocumentSession.ContentEquals(snapshot,workspace.Document) || workspace.SelectedId!=selectedId || workshopSource()!=workshopSourceId || workspace.HasPendingCanvasOrPropertyWork)
        { stale=true;compare.IsEnabled=false;first.Preview.Clear();second.Preview.Clear();result.Text="Proje, seçim veya bekleyen çalışma değişti; sonuç geçersiz. Pencereyi kapatıp güncel verilerle yeniden açın.";return false; }
        return true;
    }
    private static ContourPath Path(Side side)
    {
        var p=(side.Piece.SelectedItem as PieceChoice)?.Piece ?? throw new InvalidOperationException("Her iki parçayı seçin.");
        if(p.Points.Count>ContourMeasurement.MaxComparisonVertices)throw new InvalidOperationException("Parça 10.000 nokta ölçüm sınırını aşıyor.");
        if(side.Start.SelectedItem is not PointChoice a || side.End.SelectedItem is not PointChoice b)throw new InvalidOperationException("Her iki yolun başlangıç ve bitiş noktalarını seçin.");
        return new ContourPath(p.Points,p.Closed,a.Index,b.Index,p.Closed?(side.Direction.SelectedIndex==0?ContourDirection.Forward:ContourDirection.Backward):null);
    }
    private void Calculate()
    {
        if(!CheckCurrent())return;
        try
        {
            var a=Path(first);var b=Path(second);var value=ContourMeasurement.ComparePaths(a,b);
            if(!MeasurementInput.TryParseMillimeters(expectedEase.Text,out var target))throw new ArgumentException("Hedef fark için geçerli bir sayı girin.");
            if(!MeasurementInput.TryParseMillimeters(tolerance.Text,out var allowed) || allowed<0)throw new ArgumentException("Tolerans için sıfır veya pozitif bir sayı girin.");
            var assessment=ContourMeasurement.AssessEase(value,target,allowed);
            first.Preview.Set(a);second.Preview.Set(b);
            result.Text=$"1. yol: {MeasurementInput.Format(value.FirstLength)} mm\n2. yol: {MeasurementInput.Format(value.SecondLength)} mm\nİşaretli fark (2 − 1): {MeasurementInput.Format(value.SignedDifference)} mm\nMutlak fark: {MeasurementInput.Format(value.AbsoluteDifference)} mm\nHedeften sapma: {MeasurementInput.Format(assessment.Deviation)} mm\nTolerans: {(assessment.WithinTolerance ? "içinde" : "dışında")} (±{MeasurementInput.Format(allowed)} mm)\nGeometrik karşılaştırmadır; fiziksel ölçüm veya üretim kabulü değildir.";
        }
        catch(Exception ex) when(ex is ArgumentException or InvalidOperationException or OverflowException)
        { first.Preview.Clear();second.Preview.Clear();result.Text="Karşılaştırma yapılamadı: "+ex.Message; }
    }
    private sealed class PathPreview : FrameworkElement
    {
        private ContourPath? path;
        internal void Set(ContourPath value) { path=value;InvalidateVisual(); }
        internal void Clear() { path=null;InvalidateVisual(); }
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);dc.DrawRectangle(Brushes.White,new Pen(Brushes.LightGray,1),new Rect(0,0,ActualWidth,ActualHeight));
            if(path is not { } p || p.Points.Count<2 || ActualWidth<20 || ActualHeight<20)return;
            int n=p.Points.Count;if(p.Closed && n>1 && p.Points[0]==p.Points[n-1])n--;
            if(n<2)return;
            var xs=p.Points.Take(n).Select(q=>q.X);var ys=p.Points.Take(n).Select(q=>q.Y);
            double minX=xs.Min(),maxX=xs.Max(),minY=ys.Min(),maxY=ys.Max();
            double scale=Math.Min((ActualWidth-20)/Math.Max(maxX-minX,1e-12),(ActualHeight-20)/Math.Max(maxY-minY,1e-12));
            Point Map(Point2 q)=>new(10+(q.X-minX)*scale,10+(q.Y-minY)*scale);
            var outline=new Pen(Brushes.Gray,1);for(int i=0;i<n-1;i++)dc.DrawLine(outline,Map(p.Points[i]),Map(p.Points[i+1]));
            if(p.Closed)dc.DrawLine(outline,Map(p.Points[n-1]),Map(p.Points[0]));
            int start=p.StartIndex==n?0:p.StartIndex,end=p.EndIndex==n?0:p.EndIndex;
            int step=p.Closed?(p.Direction==ContourDirection.Forward?1:-1):(end>=start?1:-1);
            var selected=new Pen(Brushes.Crimson,3);int current=start;
            while(current!=end) { int next=p.Closed?(current+step+n)%n:current+step;dc.DrawLine(selected,Map(p.Points[current]),Map(p.Points[next]));current=next; }
            dc.DrawEllipse(Brushes.RoyalBlue,null,Map(p.Points[start]),4,4);dc.DrawEllipse(Brushes.DarkGreen,null,Map(p.Points[end]),3,3);
        }
    }
}
