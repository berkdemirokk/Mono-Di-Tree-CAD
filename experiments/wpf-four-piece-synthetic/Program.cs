using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TekstilCAD.Patterns;
using SyntheticBridge;
using UpperPreview=BodiceWpfReader.Preview;

namespace FourPieceResearch;
internal sealed class ResearchWindow:Window
{
    readonly ComboBox selection=new(){ItemsSource=new[]{"synthetic-base","synthetic-scaled","synthetic-distinct"},SelectedIndex=-1};
    readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,8)};
    readonly TextBlock description=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,8)};
    readonly FourView view=new(){Height=600,Visibility=Visibility.Collapsed};readonly Coordinator coordinator=new();long generation;
    internal ResearchWindow(){Title="Dört parça araştırması — yalnız sentetik";Width=1050;Height=850;var root=new StackPanel{Margin=new Thickness(16)};Content=new ScrollViewer{Content=root,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        root.Children.Add(new TextBlock{Text="YALNIZ UYDURMA SENARYOLAR. Gerçek kişi ölçüsü, CAD, baskı, export, cihaz ve üretim yok. Üst ve alt ayrı yöntemlerden gelir: ölçü ve bolluk sözleşmeleri farklıdır; bel bağlantısı, dikiş uyumu ve beden uyumu doğrulanmadı. Parçalar birleştirilmiş bir elbise değildir. Uzman incelemesi ve numune prova gerekir. Worker'ın ağ yalıtımı, bağımlılık kimliği ve lisansları doğrulanmadı.",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.DarkRed});
        root.Children.Add(new TextBlock{Text="Sabit sentetik senaryo:",Margin=new Thickness(0,12,0,4)});root.Children.Add(selection);selection.SelectionChanged+=(_,_)=>InvalidateSelection();
        var row=new StackPanel{Orientation=Orientation.Horizontal};var run=new Button{Content="Dört ayrı araştırma parçasını göster",Margin=new Thickness(0,10,10,10)};var cancel=new Button{Content="İptal",Margin=new Thickness(0,10,0,10)};row.Children.Add(run);row.Children.Add(cancel);root.Children.Add(row);root.Children.Add(status);root.Children.Add(description);root.Children.Add(view);
        cancel.Click+=(_,_)=>{generation++;coordinator.Cancel();Clear();status.Text="İptal edildi.";};
        run.Click+=async(_,_)=>{var id=selection.SelectedItem as string;if(!Envelope.ValidId(id)){status.Text="Senaryo seçin.";return;}long mine=++generation;Clear();status.Text="Sentetik üst ve alt hesaplanıyor…";try{await coordinator.Start(id!,ct=>Worker.Run(Worker.Real,id!,TimeSpan.FromSeconds(15),ct),upper=>Publish(id!,upper));}catch{if(mine==generation){Clear();status.Text="Sentetik sonuç doğrulanamadı.";}}};
        Closed+=(_,_)=>{generation++;coordinator.Cancel();};
    }
    void InvalidateSelection(){generation++;coordinator.Cancel();Clear();status.Text="Senaryo değişti; yeni hesap başlatın.";}
    void Clear(){view.Panels=null;view.Visibility=Visibility.Collapsed;view.InvalidateVisual();description.Text="";}
    void Publish(string id,UpperPreview upper){var scenario=Scenario.Get(id);var lower=ALinePanelPreview.Create(scenario.Lower);if(lower.CanApplyOrExport)throw new InvalidOperationException();view.Panels=Layout.Build(upper,lower);view.Visibility=Visibility.Visible;view.InvalidateVisual();description.Text=$"ÜST: Breanna 4.10.2 '{id}' için sabit uydurma ölçü bildirimi (mm, omuz eğimi derece). Sıra: üst kol, ön göğüs, göğüs arası, üst göğüs çevresi, ön üst göğüs, omuz–göğüs, omuz–arka bel, omuz–ön bel, bel çevresi, bel–kalça kemiği, boyun, omuzdan omuza, omuz eğimi, omuz–bilek, bilek: {scenario.UpperDeclaration}. Ön pens 06:00, ikinci pens yok; arka omuz ve bel pensi yok. Sabit motor bolluğu örnekleri: göğüs %10, bel %10, yaka %3,5; diğer sabit seçenekler worker sözleşmesindedir.\nALT: ALinePanelPreview, ayrı 11 sentetik kesit/bolluk girdisi. {scenario.Explain}\nMavi üst, turuncu alt; tüm dört parça aynı ekran ölçeğinde ama birbirinden ayrı yerleştirilir. Aynı beden ya da ölçü uyumu varsayılmaz. Ön/arka alt etek ucu eşit dağıtılmıştır. Bel hattı, pens ve bağlantılar doğrulanmadı.";status.Text="Yalnız dört bağımsız araştırma görüntüsü; dikiş veya üretim onayı yok.";}
    internal void SelectForTest(string id)=>selection.SelectedItem=id;
    internal void PublishForTest(string id,UpperPreview upper)=>Publish(id,upper);
    internal bool IsPreviewVisible=>view.Visibility==Visibility.Visible;
    internal string Description=>description.Text;
    internal bool RenderHasBothColors(){view.Measure(new Size(1000,600));view.Arrange(new Rect(0,0,1000,600));view.UpdateLayout();var bmp=new RenderTargetBitmap(1000,600,96,96,PixelFormats.Pbgra32);bmp.Render(view);var data=new byte[1000*600*4];bmp.CopyPixels(data,4000,0);bool blue=false,orange=false;for(int i=0;i<data.Length;i+=4){if(data[i]>120&&data[i+1]<180&&data[i+2]<110&&data[i]>data[i+1])blue=true;if(data[i]<80&&data[i+1]>60&&data[i+1]<180&&data[i+2]>180)orange=true;}return blue&&orange;}
}
internal static class Program
{
    [STAThread]static int Main(string[] args){if(args.SequenceEqual(new[]{"--verify"})){try{var data=VerifyAsync().GetAwaiter().GetResult();VerifyUi(data.a,data.b);Console.WriteLine("PASS: two worker scenarios, actual A-line panels, common scale, WPF four-panel render and stale suppression; no window shown");return 0;}catch(Exception e){Console.Error.WriteLine("FAIL: "+e.Message);return 1;}}return new Application().Run(new ResearchWindow());}
    static async Task<(UpperPreview a,UpperPreview b)> VerifyAsync(){var a=await Worker.Run(Worker.Real,"synthetic-base",TimeSpan.FromSeconds(20),CancellationToken.None);var b=await Worker.Run(Worker.Real,"synthetic-scaled",TimeSpan.FromSeconds(20),CancellationToken.None);if(a.Parts[0].Segments[0].To==b.Parts[0].Segments[0].To)throw new Exception("upper scenarios identical");var lowA=ALinePanelPreview.Create(Scenario.Get("synthetic-base").Lower);var lowB=ALinePanelPreview.Create(Scenario.Get("synthetic-scaled").Lower);if(lowA.CanApplyOrExport||lowB.CanApplyOrExport||Math.Abs(lowA.Front[1].X-180)>1e-9||Math.Abs(lowB.Front[1].X-201.6)>1e-9)throw new Exception("lower actual geometry");var p=Layout.Build(a,lowA);if(p.Length!=4||p[2].Geometry.Figures[0].Segments[0] is not LineSegment waist||Math.Abs(waist.Point.X-lowA.Front[1].X)>1e-9)throw new Exception("lower not from engine");double scale=Layout.SharedScale(p,new Size(1000,600));if(!double.IsFinite(scale)||scale<=0||p.Any(x=>x.Geometry.Bounds.Width*scale>470.00001||x.Geometry.Bounds.Height*scale>250.00001))throw new Exception("shared scale");var coordinator=new Coordinator();var tcs=new TaskCompletionSource<UpperPreview>(TaskCreationOptions.RunContinuationsAsynchronously);int count=0;var old=coordinator.Start("synthetic-base",_=>tcs.Task,_=>count++);coordinator.Cancel();tcs.SetResult(a);if(await old||count!=0)throw new Exception("stale publish");return(a,b);}
    static void VerifyUi(UpperPreview a,UpperPreview b){var app=new Application();var window=new ResearchWindow();window.SelectForTest("synthetic-base");window.PublishForTest("synthetic-base",a);if(!window.IsPreviewVisible||!window.RenderHasBothColors()||!window.Description.Contains("farklı")&& !window.Description.Contains("ayrı"))throw new Exception("four panel display");window.SelectForTest("synthetic-scaled");if(window.IsPreviewVisible||window.Description!="")throw new Exception("stale UI");window.PublishForTest("synthetic-scaled",b);if(!window.IsPreviewVisible||!window.Description.Contains("bel bağlantısı",StringComparison.OrdinalIgnoreCase)&&!window.Description.Contains("Bel hattı"))throw new Exception("warning missing");}
}
