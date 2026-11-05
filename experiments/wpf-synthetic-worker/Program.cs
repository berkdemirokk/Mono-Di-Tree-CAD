using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using BodiceWpfReader;
using System.Diagnostics;
using System.Text;
namespace SyntheticBridge;
internal sealed class WindowPreview:Window
{
    readonly ComboBox choices=new(){ItemsSource=new[]{"synthetic-base","synthetic-scaled","synthetic-distinct"},SelectedIndex=-1};
    readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap};readonly BodiceResearch.Preview preview=new(){Height=350,Visibility=Visibility.Collapsed};readonly Coordinator coordinator=new();long selectionVersion;
    internal WindowPreview()
    {
        Title="Gövde worker araştırması — yalnız sentetik";Width=900;Height=600;var root=new StackPanel{Margin=new Thickness(20)};Content=root;
        root.Children.Add(new TextBlock{Text="YALNIZ SENTETİK: Gerçek kişi ölçüsü girmeyin. Ağ yalıtımı, çalışan bağımlılıkların kimliği ve lisansları doğrulanmadı. Üretim, CAD, export, baskı ve plotter kapalı; dikilebilirlik ve prova doğrulanmadı.",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.DarkRed});
        root.Children.Add(new TextBlock{Text="Sabit uydurma senaryo seçin:",Margin=new Thickness(0,14,0,5)});root.Children.Add(choices);
        choices.SelectionChanged+=(_,_)=>{selectionVersion++;coordinator.Cancel();Clear();status.Text="Senaryo değişti; yeni hesap başlatın.";};
        var row=new StackPanel{Orientation=Orientation.Horizontal};var run=new Button{Content="Sentetik gövdeyi hesapla",Margin=new Thickness(0,10,10,10)};var cancel=new Button{Content="İptal",Margin=new Thickness(0,10,0,10)};row.Children.Add(run);row.Children.Add(cancel);root.Children.Add(row);root.Children.Add(status);root.Children.Add(preview);
        cancel.Click+=(_,_)=>{selectionVersion++;coordinator.Cancel();Clear();status.Text="İptal edildi.";};
        run.Click+=async(_,_)=>{var id=choices.SelectedItem as string;if(!Envelope.ValidId(id)){status.Text="Senaryo seçin.";return;}long mine=++selectionVersion;Clear();status.Text="Hesaplanıyor…";try{await coordinator.Start(id!,ct=>Worker.Run(Worker.Real,id!,TimeSpan.FromSeconds(15),ct),result=>{preview.Data=result;preview.Visibility=Visibility.Visible;preview.InvalidateVisual();status.Text="Yalnız sentetik kaynak görünümü; üretim ve uyum doğrulanmadı.";});}catch{if(mine==selectionVersion){Clear();status.Text="Sentetik araştırma sonucu doğrulanamadı.";}}};
        Closed+=(_,_)=>{selectionVersion++;coordinator.Cancel();};
    }
    void Clear(){preview.Data=null;preview.Visibility=Visibility.Collapsed;preview.InvalidateVisual();}
}
internal static class Program
{
    static readonly string Node=@"C:\Users\pc\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe";
    static readonly string Fake=@"C:\Projects\TekstilCAD\work\wpf-synthetic-worker-1105\experiments\wpf-synthetic-worker\fake-worker.mjs";
    [STAThread]static int Main(string[] args){if(args.SequenceEqual(new[]{"--verify"})){try{var r=VerifyAsync().GetAwaiter().GetResult();VerifyRender(r);Console.WriteLine("PASS: native allocation failure injection, worker outputs, strict identity/schema, timeout, cancellation, overflow, orphan pipe cleanup, stale suppression, WPF geometry; no window shown");return 0;}catch(Exception e){Console.Error.WriteLine("FAIL: "+e);return 1;}}return new Application().Run(new WindowPreview());}
    static Command TestCommand(string mode)=>new(Node,new[]{Fake,mode});
    static async Task<Preview> VerifyAsync()
    {
        foreach(var stage in new[]{"limits","attrs","handles","jobs","environment","attribute-list"})
        {
            try{await JobRunner.Run(TestCommand("invalid"),Encoding.UTF8.GetBytes("{}"),CancellationToken.None,name=>{if(name==stage)throw new InvalidOperationException("injected failure");});throw new Exception("injection did not fire: "+stage);}
            catch(InvalidOperationException e) when(e.Message=="injected failure"){}
            if(NativeAllocation.ActiveCount!=0||JobRunner.ActiveAttributeLists!=0)throw new Exception("native allocation not released: "+stage);
        }
        var a=await Worker.Run(Worker.Real,"synthetic-base",TimeSpan.FromSeconds(15),CancellationToken.None);var b=await Worker.Run(Worker.Real,"synthetic-scaled",TimeSpan.FromSeconds(15),CancellationToken.None);
        if(a.Parts.Count!=2||b.Parts.Count!=2||a.Parts[0].Segments[0].To==b.Parts[0].Segments[0].To)throw new Exception("source scenarios not distinct");
        foreach(var mode in new[]{"invalid","wrong","bad-unit","duplicate"}){try{await Worker.Run(TestCommand(mode),"synthetic-base",TimeSpan.FromSeconds(4),CancellationToken.None);throw new Exception("invalid response accepted");}catch(System.IO.InvalidDataException){}}
        var sw=Stopwatch.StartNew();try{await Worker.Run(TestCommand("delay"),"synthetic-base",TimeSpan.FromMilliseconds(250),CancellationToken.None);throw new Exception("timeout accepted");}catch(OperationCanceledException){}if(sw.Elapsed>TimeSpan.FromSeconds(4))throw new Exception("timeout too slow");
        using(var cancel=new CancellationTokenSource(250)){try{await Worker.Run(TestCommand("delay"),"synthetic-base",TimeSpan.FromSeconds(10),cancel.Token);throw new Exception("cancel accepted");}catch(OperationCanceledException){}}
        sw.Restart();try{await Worker.Run(TestCommand("overflow"),"synthetic-base",TimeSpan.FromSeconds(10),CancellationToken.None);throw new Exception("overflow accepted");}catch(System.IO.InvalidDataException){}if(sw.Elapsed>TimeSpan.FromSeconds(4))throw new Exception("overflow was not fail-fast");
        string dir=@"C:\Projects\TekstilCAD\work\wpf-synthetic-worker-1105\work\evidence";System.IO.Directory.CreateDirectory(dir);string sentinel=System.IO.Path.Combine(dir,"orphan-"+Guid.NewGuid().ToString("N")+".pid");Process? child=null;
        try{using var orphanCancel=new CancellationTokenSource();var running=Task.Run(()=>Worker.Run(new Command(Node,new[]{Fake,"orphan",sentinel}),"synthetic-base",TimeSpan.FromSeconds(12),orphanCancel.Token));var until=Stopwatch.StartNew();while(!System.IO.File.Exists(sentinel)&&until.Elapsed<TimeSpan.FromSeconds(5))await Task.Delay(20);if(!System.IO.File.Exists(sentinel))throw new Exception("orphan sentinel missing");using var record=System.Text.Json.JsonDocument.Parse(await System.IO.File.ReadAllTextAsync(sentinel));int pid=record.RootElement.GetProperty("child").GetInt32(),parent=record.RootElement.GetProperty("parent").GetInt32();child=Process.GetProcessById(pid);using var leader=Process.GetProcessById(parent);if(child.HasExited||leader.HasExited)throw new Exception("processes not both alive");await System.IO.File.WriteAllTextAsync(sentinel+".release","release");await leader.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));if(child.HasExited)throw new Exception("descendant did not outlive parent");if(running.IsCompleted)throw new Exception("pipe not held after parent exit");orphanCancel.Cancel();try{await running;throw new Exception("held pipe accepted");}catch(OperationCanceledException){}await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));}
        finally{if(child is not null){try{if(!child.HasExited)child.Kill(true);}finally{child.Dispose();}}if(System.IO.File.Exists(sentinel))System.IO.File.Delete(sentinel);if(System.IO.File.Exists(sentinel+".release"))System.IO.File.Delete(sentinel+".release");}
        var coordinator=new Coordinator();var deferred=new TaskCompletionSource<Preview>(TaskCreationOptions.RunContinuationsAsynchronously);int published=0;var old=coordinator.Start("synthetic-base",_=>deferred.Task,_=>published++);coordinator.Cancel();deferred.SetResult(a);if(await old||published!=0)throw new Exception("stale published");
        var deferred2=new TaskCompletionSource<Preview>(TaskCreationOptions.RunContinuationsAsynchronously);var old2=coordinator.Start("synthetic-base",_=>deferred2.Task,_=>published++);if(!await coordinator.Start("synthetic-scaled",_=>Task.FromResult(b),_=>published++))throw new Exception("new unpublished");deferred2.SetResult(a);if(await old2||published!=1)throw new Exception("identity change published stale");return a;
    }
    static void VerifyRender(Preview data)
    {
        var view=new BodiceResearch.Preview{Data=data,Width=800,Height=350};view.Measure(new Size(800,350));view.Arrange(new Rect(0,0,800,350));var bmp=new RenderTargetBitmap(800,350,96,96,PixelFormats.Pbgra32);bmp.Render(view);var pixels=new byte[800*350*4];bmp.CopyPixels(pixels,800*4,0);bool ink=false;for(int i=0;i<pixels.Length;i+=4)if(pixels[i]>120&&pixels[i+1]<180&&pixels[i+2]<110&&pixels[i]>pixels[i+1]){ink=true;break;}if(!ink)throw new Exception("blank render");
    }
}
