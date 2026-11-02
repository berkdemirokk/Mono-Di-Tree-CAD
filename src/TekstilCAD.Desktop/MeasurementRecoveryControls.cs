using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;

namespace TekstilCAD.Desktop;

/// <summary>Opt-in local form recovery. Work is serialized off the UI thread.</summary>
internal sealed class MeasurementRecoveryControls
{
    private readonly Window owner;
    private readonly string kind;
    private readonly TextBox[] inputs;
    private readonly DispatcherTimer debounce;
    private MeasurementFormRecoveryStore? store;
    private Task pending=Task.CompletedTask;
    private bool closed,starting,busy,hasSubmitted;
    private long revision;
    private long savedRevision=-1,failedRevision=-1;
    private string? lastWriteError;
    internal Func<string,string,MeasurementFormRecoveryStore>? StoreFactoryForVerification { get; set; }
    internal Func<string,string,string[]>? LoadForVerification { get; set; }
    internal bool StartingForVerification=>starting;
    internal MeasurementFormRecoveryStore? StoreForVerification=>store;
    internal Task PendingForVerification=>pending;
    internal Task ReleaseForVerification { get; private set; } = Task.CompletedTask;
    internal CheckBox EnabledForVerification { get; } = new() { Content="Bu formu yerel kurtarma için sakla",Foreground=DesignRequestWindow.Ink,Margin=new Thickness(0,4,0,4) };
    internal TextBlock StatusForVerification { get; } = new() { TextWrapping=TextWrapping.Wrap,Foreground=DesignRequestWindow.Muted,Margin=new Thickness(0,4,0,4) };
    internal StackPanel Panel { get; } = new() { Margin=new Thickness(0,8,0,8) };

    internal MeasurementRecoveryControls(Window owner,string kind,IEnumerable<TextBox> inputs)
    {
        this.owner=owner;this.kind=kind;this.inputs=inputs.ToArray();
        debounce=new DispatcherTimer(DispatcherPriority.Background,owner.Dispatcher) { Interval=TimeSpan.FromMilliseconds(700) };
        debounce.Tick+=(_,_)=> { debounce.Stop();QueueCurrent(); };
        Panel.Children.Add(new TextBlock { Text="Kurtarma isteğe bağlıdır. Yalnız bu formdaki değerler Windows kullanıcı hesabına bağlı şifrelemeyle bu bilgisayarda saklanır; istek ve görsel kaydedilmez. Kayıtlar silinene kadar kalır; aynı hesaptaki uygulamalar erişebilir. Ani kapanışta son giriş kaybolabilir.",TextWrapping=TextWrapping.Wrap });
        AutomationProperties.SetHelpText(EnabledForVerification,"İşaretlenirse yalnız form alanları şifrelenerek yerel kurtarma klasörüne yazılır. Kapatılması önceki kopyaları silmez.");Panel.Children.Add(EnabledForVerification);
        var actions=new WrapPanel();var restore=new Button { Content="Önceki taslağı seç…",Padding=new Thickness(8,4,8,4),Margin=new Thickness(0,3,6,3) };
        var delete=new Button { Content="Eski taslağı sil…",Padding=new Thickness(8,4,8,4),Margin=new Thickness(0,3,6,3) };
        var retry=new Button { Content="Kaydı yeniden dene",Padding=new Thickness(8,4,8,4),Margin=new Thickness(0,3,0,3) };
        AutomationProperties.SetHelpText(restore,"Seçilen eski kurtarma dosyasını yalnız forma yükler; notları değiştirmez. Hangi isteğe ait olduğunu kendiniz kontrol edin.");
        AutomationProperties.SetHelpText(delete,"Seçtiğiniz kullanılmayan kurtarma dosyasını onayınızla siler; diğer kayıtları değiştirmez.");
        AutomationProperties.SetHelpText(retry,"Yerel kurtarma açıkken bu formun geçerli değerlerini yeniden kaydetmeyi dener.");
        restore.Click+=async (_,_)=>await ChooseAndRestoreAsync();delete.Click+=async (_,_)=>await ChooseAndDeleteAsync();
        retry.Click+=(_,_)=> { if(EnabledForVerification.IsChecked!=true || store is null) { StatusForVerification.Text="Yeniden denemek için yerel kurtarmayı açın.";return; }
            if(!pending.IsCompleted) { StatusForVerification.Text="Önce süren yazının tamamlanmasını bekleyin.";return; }
            debounce.Stop();QueueCurrent();if(!pending.IsCompleted)StatusForVerification.Text="Yerel kurtarma yeniden deneniyor."; };
        actions.Children.Add(restore);actions.Children.Add(delete);actions.Children.Add(retry);Panel.Children.Add(actions);
        AutomationProperties.SetName(StatusForVerification,"Ölçü formu kurtarma durumu");AutomationProperties.SetLiveSetting(StatusForVerification,AutomationLiveSetting.Polite);Panel.Children.Add(StatusForVerification);
        StatusForVerification.Text="Yerel kurtarma kapalı.";
        EnabledForVerification.Checked+=async (_,_)=>await StartAsync();EnabledForVerification.Unchecked+=(_,_)=> { revision++;debounce.Stop();StatusForVerification.Text="Yerel kurtarma kapalı; başlamış kayıt tamamlanabilir ve önceki kopyalar silinmez."; };
        foreach(var input in this.inputs)input.TextChanged+=(_,_)=> { revision++;if(EnabledForVerification.IsChecked==true && store is not null) { StatusForVerification.Text="Son giriş yerel kurtarmaya yazılmayı bekliyor.";debounce.Stop();debounce.Start(); } };
        owner.Closed+=(_,_)=>Close();
    }
    private string[] Capture()=>inputs.Select(input=>input.Text).ToArray();
    private async Task StartAsync()
    {
        if(starting || closed)return;
        if(store is not null) { StatusForVerification.Text="Yerel kurtarma açık; önceki kopyalar korunur.";QueueCurrent();return; }
        starting=true;StatusForVerification.Text="Yerel kurtarma hazırlanıyor.";
        try
        {
            var factory=StoreFactoryForVerification;
            var created=await Task.Run(()=>factory is null ? new MeasurementFormRecoveryStore(MeasurementFormRecoveryStore.DirectoryPath,kind) : factory(MeasurementFormRecoveryStore.DirectoryPath,kind));
            if(closed || EnabledForVerification.IsChecked!=true) { created.Dispose();return; }
            store=created;StatusForVerification.Text="Yerel kurtarma açık; ilk kayıt hazırlanıyor.";QueueCurrent();
        }
        catch(Exception ex) when(IsExpected(ex)) { if(!closed) { EnabledForVerification.IsChecked=false;StatusForVerification.Text="Yerel kurtarma açılamadı; girişler formda korunuyor: "+ex.Message;StatusForVerification.BringIntoView(); } }
        finally { starting=false; }
    }
    internal void QueueCurrent()
    {
        if(store is null || EnabledForVerification.IsChecked!=true)return;
        var snapshot=Capture();if(snapshot.All(string.IsNullOrEmpty) && !hasSubmitted) { Interlocked.Exchange(ref savedRevision,revision);StatusForVerification.Text="Form boş; bu oturumda kurtarma kaydı oluşturulmadı.";return; }
        hasSubmitted=true;
        var serial=revision;var writer=store;var before=pending;
        pending=WriteAsync(before,writer,snapshot,serial);
    }
    private async Task WriteAsync(Task before,MeasurementFormRecoveryStore writer,string[] snapshot,long serial)
    {
        await before.ConfigureAwait(false);
        string? error=null;
        try { await Task.Run(()=>writer.Save(snapshot)).ConfigureAwait(false);Volatile.Write(ref lastWriteError,null);Interlocked.Exchange(ref failedRevision,-1);Interlocked.Exchange(ref savedRevision,serial); }
        catch(Exception ex) when(IsExpected(ex)) { error=ex.Message;Volatile.Write(ref lastWriteError,error);Interlocked.Exchange(ref failedRevision,serial); }
        if(!owner.Dispatcher.HasShutdownStarted)
        {
            try { _=owner.Dispatcher.BeginInvoke(new Action(()=> { if(!closed && serial==revision && EnabledForVerification.IsChecked==true)StatusForVerification.Text=error is null ? "Formun son görüntüsü yerel kurtarmaya yazıldı; notlara eklenmedi." : "Kurtarma yazılamadı; girişler formda korunuyor: "+error; })); }
            catch(InvalidOperationException) when(owner.Dispatcher.HasShutdownStarted) { }
        }
    }
    private static bool IsExpected(Exception ex)=>ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.Security.Cryptography.CryptographicException or System.Security.SecurityException or NotSupportedException;
    internal bool ReadyToClose()
    {
        bool Block(string message) { StatusForVerification.Text=message;StatusForVerification.BringIntoView();return false; }
        if(busy)return Block("Taslak okuma veya silme işlemi sürüyor; tamamlanmasını bekleyip tekrar deneyin.");
        if(EnabledForVerification.IsChecked!=true)return true;
        if(starting || store is null)return Block("Yerel kurtarma hazırlanıyor; bekleyin veya kurtarmayı kapatın, sonra tekrar deneyin.");
        if(!pending.IsCompleted)return Block("Yerel kurtarma yazılıyor; bekleyin veya kurtarmayı kapatın, sonra tekrar deneyin.");
        if(Interlocked.Read(ref failedRevision)==revision)
            return Block("Yerel kurtarma yazılamadı; girişler formda korunuyor. Kaydı yeniden deneyin veya kurtarmayı kapatın: "+Volatile.Read(ref lastWriteError));
        if(Interlocked.Read(ref savedRevision)!=revision)
        {
            debounce.Stop();QueueCurrent();
            if(pending.IsCompleted && Interlocked.Read(ref savedRevision)==revision)return true;
            return Block("Son giriş yerel kurtarmaya yazılıyor; tamamlanmasını bekleyip tekrar deneyin.");
        }
        return true;
    }
    private string? Choose(string title)
    {
        var directory=MeasurementFormRecoveryStore.DirectoryPath;
        if(!Directory.Exists(directory)) { StatusForVerification.Text="Bu hesapta kayıtlı ölçü formu taslağı yok.";return null; }
        var dialog=new OpenFileDialog { Title=title,InitialDirectory=directory,Filter="Ölçü formu kurtarma (*.mform)|*.mform",Multiselect=false };
        return dialog.ShowDialog(owner)==true ? dialog.FileName : null;
    }
    private async Task ChooseAndRestoreAsync()
    {
        if(busy)return;var path=Choose("Kurtarılacak ölçü formunu seç");if(path is null)return;
        var previous=Capture();
        if(previous.Any(s=>s.Length>0) && MessageBox.Show(owner,"Geçerli form alanları seçilen taslakla değiştirilecek. Devam edilsin mi? Ana istek notları değiştirilmez.","Ölçü taslağını yükle",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)!=MessageBoxResult.Yes)return;
        await RestoreFromPathAsync(path,previous);
    }
    internal async Task<bool> RestoreFromPathAsync(string path,string[] previous)
    {
        if(busy || closed)return false;
        busy=true;StatusForVerification.Text="Seçilen taslak okunuyor; mevcut girişler korunuyor.";
        try
        {
            var loader=LoadForVerification;
            var values=await Task.Run(()=>loader is null ? MeasurementFormRecoveryStore.Load(MeasurementFormRecoveryStore.DirectoryPath,path,kind) : loader(path,kind));
            if(closed)return false;
            if(values.Length!=inputs.Length)throw new InvalidDataException("Kurtarma formu alan sayısı geçersiz.");
            if(!Capture().SequenceEqual(previous)) { StatusForVerification.Text="Form okuma sırasında değişti; taslak yüklenmedi.";return false; }
            for(int i=0;i<inputs.Length;i++)inputs[i].Text=values[i];
            StatusForVerification.Text="Taslak yalnız forma yüklendi. Hangi isteğe ait olduğunu kontrol edin; notlara henüz eklenmedi. Özgün kurtarma dosyası korundu.";return true;
        }
        catch(Exception ex) when(IsExpected(ex)) { if(!closed)StatusForVerification.Text="Taslak yüklenemedi; mevcut girişler ve kurtarma dosyası korundu: "+ex.Message;return false; }
        finally { busy=false; }
    }
    private async Task ChooseAndDeleteAsync()
    {
        if(busy)return;var path=Choose("Silinecek eski ölçü formu taslağını seç");if(path is null)return;
        if(MessageBox.Show(owner,"Yalnız seçilen eski kurtarma dosyası silinecek. Devam edilsin mi?","Kurtarma dosyasını sil",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)!=MessageBoxResult.Yes)return;
        busy=true;try { await Task.Run(()=>MeasurementFormRecoveryStore.Delete(MeasurementFormRecoveryStore.DirectoryPath,path,kind));if(!closed)StatusForVerification.Text="Seçilen eski kurtarma dosyası silindi; form ve diğer kayıtlar değiştirilmedi."; }
        catch(Exception ex) when(IsExpected(ex)) { if(!closed)StatusForVerification.Text="Kurtarma dosyası silinemedi; kayıt korundu: "+ex.Message; }
        finally { busy=false; }
    }
    private void Close()
    {
        if(closed)return;debounce.Stop();closed=true;
        var writer=store;if(writer is not null) { var before=pending;ReleaseForVerification=Task.Run(async()=> { try { await before.ConfigureAwait(false); } finally { writer.Dispose(); } }); }
    }
    internal void FinishForVerification()=>Close();
}
