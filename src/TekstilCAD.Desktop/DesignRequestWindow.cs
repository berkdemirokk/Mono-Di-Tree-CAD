using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Microsoft.Win32;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Desktop;

/// <summary>Offline request preparation, independent from the open CAD project.</summary>
internal sealed class DesignRequestWindow : Window
{
    private readonly TextBox prompt=new() { AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MinHeight=95,MaxLength=DesignRequestStorage.MaximumTextLength,VerticalScrollBarVisibility=ScrollBarVisibility.Auto };
    private readonly TextBox measurements=new() { AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MinHeight=95,MaxLength=DesignRequestStorage.MaximumTextLength,VerticalScrollBarVisibility=ScrollBarVisibility.Auto };
    private readonly TextBlock referenceLabel=new() { TextWrapping=TextWrapping.Wrap };
    private readonly TextBox measurementReview=new() { IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MinHeight=100,VerticalScrollBarVisibility=ScrollBarVisibility.Auto };
    private readonly TextBlock status=new() { TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0) };
    private DesignReference? reference;
    private bool dirty,loading;
    internal TextBox PromptForVerification=>prompt;
    internal TextBox MeasurementsForVerification=>measurements;
    internal DesignRequest CaptureForVerification()=>Capture();
    internal void LoadForVerification(DesignRequest value)=>Apply(value);
    internal bool DirtyForVerification=>dirty;
    internal TextBox ReviewForVerification=>measurementReview;
    internal void ReviewMeasurementsForVerification()=>ReviewMeasurements();
    internal DesignRequestWindow(Window owner)
    {
        Owner=owner;Title="Kalıp isteği hazırla";Width=680;Height=680;MinWidth=480;MinHeight=420;WindowStartupLocation=WindowStartupLocation.CenterOwner;StudioTheme.Apply(this);
        var root=new DockPanel { Margin=new Thickness(16) };Content=root;
        var actions=new WrapPanel { Margin=new Thickness(0,8,0,0) };DockPanel.SetDock(actions,Dock.Bottom);root.Children.Add(actions);
        Button Action(string text,Action action) { var b=new Button { Content=text,Padding=new Thickness(12,7,12,7),Margin=new Thickness(0,0,8,4) };b.Click+=(_,_)=>action();actions.Children.Add(b);return b; }
        Action("İstek aç…",Open);Action("Yeni istek dosyası kaydet…",Save);Action("Kapat",Close);
        var body=new StackPanel();root.Children.Add(new ScrollViewer { Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Focusable=true });
        body.Children.Add(new TextBlock { Text="Kalıp isteği hazırla",FontSize=20,FontWeight=FontWeights.SemiBold });
        body.Children.Add(new TextBlock { Text="Kadın elbisenin modelini ve istediğiniz özellikleri kendi sözlerinizle yazın; ölçüleri birimleriyle belirtin. Örneğin elbise boyu, göğüs, bel, kalça ve kol ölçülerini biliniyorsa ekleyin. Eksik ölçüler tahmin edilmez. İsteğe bağlı tek PNG/JPEG görseli dosyaya kopyalanır. Bu çevrimdışı istek kalıp oluşturmaz; veri hiçbir servise gönderilmez. Açık proje ve çizim değiştirilmez.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,10) });
        body.Children.Add(new TextBlock { Text="İsteğe bağlı başlangıç: Bir model seçin; açıklama alanına örnek metin eklenir. Seçim kalıp oluşturmaz.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,4) });
        var models=new WrapPanel { Margin=new Thickness(0,0,0,6) };body.Children.Add(models);
        void ModelChoice(string label,string template)
        {
            var button=new Button { Content=label,Margin=new Thickness(0,0,8,4),Padding=new Thickness(10,5,10,5) };
            AutomationProperties.SetHelpText(button,"Açıklamaya örnek model metni ekler; mevcut metin korunur ve kalıp oluşturulmaz.");
            button.Click+=(_,_)=>
            {
                var addition=(prompt.Text.Length==0 ? "" : "\n")+template;
                if(prompt.Text.Length+addition.Length>prompt.MaxLength) { status.Text="Örnek eklenemedi: açıklama uzunluk sınırına ulaşır. Mevcut metin korundu.";return; }
                prompt.AppendText(addition);prompt.Focus();status.Text="Model açıklaması eklendi. Özellikleri düzenleyebilir, bilinen ölçüleri girebilirsiniz; kalıp oluşturulmadı.";
            };
            models.Children.Add(button);
        }
        ModelChoice("Düz elbise", "Düz elbise; detaylar: ");
        ModelChoice("A kesim elbise", "A kesim elbise; detaylar: ");
        body.Children.Add(new Label { Content="Kadın elbise modeli ve özellikleri",Target=prompt });AutomationProperties.SetName(prompt,"Kadın elbise modeli ve özellikleri");body.Children.Add(prompt);
        body.Children.Add(new Label { Content="Ölçüler ve birimler",Target=measurements });AutomationProperties.SetName(measurements,"Ölçüler ve birimler");body.Children.Add(measurements);
        body.Children.Add(new TextBlock { Text="Açık ölçüleri satır veya noktalı virgülle ayırın: göğüs çevresi 96 cm; bel çevresi 72 cm; kalça çevresi 100 cm; elbise boyu 110 cm. Yalnız açık çevre veya boy etiketi ve mm/cm birimi kullanılır; göğüs, bel veya kalça tek başına çevre varsayılmaz. Virgül/nokta ondalık ayracıdır. Kol boyu yalnız açıkça belirtilirse alınır.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,4) });
        var checkMeasurements=new Button { Content="Açık ölçüleri denetle",HorizontalAlignment=HorizontalAlignment.Left,Padding=new Thickness(10,5,10,5),Margin=new Thickness(0,4,0,4) };
        body.Children.Add(checkMeasurements);checkMeasurements.Click+=(_,_)=>ReviewMeasurements();
        body.Children.Add(new Label { Content="Ölçü denetimi sonucu",Target=measurementReview });AutomationProperties.SetName(measurementReview,"Ölçü denetimi sonucu");body.Children.Add(measurementReview);
        body.Children.Add(new TextBlock { Text="Referans görseli",FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,12,0,4) });body.Children.Add(referenceLabel);
        var refActions=new WrapPanel();body.Children.Add(refActions);
        var choose=new Button { Content="Görsel seç…",Padding=new Thickness(10,5,10,5),Margin=new Thickness(0,4,8,4) };refActions.Children.Add(choose);choose.Click+=(_,_)=>ChooseReference();
        var remove=new Button { Content="Görseli kaldır",Padding=new Thickness(10,5,10,5),Margin=new Thickness(0,4,0,4) };refActions.Children.Add(remove);remove.Click+=(_,_)=> { if(reference is not null) { reference=null;dirty=true;UpdateReference(); } };
        body.Children.Add(new TextBlock { Text="Görsel dosyanın tüm baytları ve varsa içindeki metadata isteğe eklenir. Yalnızca PNG/JPEG imzası kontrol edilir; görsel içeriği, ölçüler veya üretime uygunluk doğrulanmaz. Kaydetme yeni ve ayrı bir dosya oluşturur; kalıp çizimi ve plotter çıktısı için sonraki işlem ve kullanıcı kontrolü gerekir.",TextWrapping=TextWrapping.Wrap,Foreground=StudioTheme.Warning,Margin=new Thickness(0,10,0,0) });
        AutomationProperties.SetLiveSetting(status,AutomationLiveSetting.Polite);body.Children.Add(status);
        prompt.TextChanged+=(_,_)=> { measurementReview.Clear();if(!loading)dirty=true; };measurements.TextChanged+=(_,_)=> { measurementReview.Clear();if(!loading)dirty=true; };UpdateReference();Closing+=OnClosing;
    }
    private void ReviewMeasurements()
    {
        try
        {
            var review=DressMeasurementParser.Review(prompt.Text,measurements.Text);
            var lines=new List<string> { "Açık bulunan ölçüler (mm):" };
            lines.AddRange(review.Measurements.Select(m=>$"{m.Label}: {m.Millimeters.ToString(System.Globalization.CultureInfo.InvariantCulture)} mm — kaynak: {string.Join(" / ",m.SourceExpressions)}"));
            if(review.Measurements.Count==0)lines.Add("Açıkça tanınan ölçü yok.");
            lines.Add("Kontrol edilmesi gerekenler:");lines.AddRange(review.Questions);
            if(review.Unrecognized.Count>0)lines.Add($"{review.Unrecognized.Count} ifade bu dar ölçü sözlüğüyle çözümlenmedi; asıl metni kontrol edin.");
            lines.Add("İfadeler yalnız ön denetimdir. Ölçünün niteliğini, modeli ve üretime uygunluğu kullanıcı doğrulamalıdır; kalıp oluşturulmadı.");
            measurementReview.Text=string.Join(Environment.NewLine,lines);
        }
        catch(ArgumentException ex) { measurementReview.Text="Ölçü denetlenemedi: "+ex.Message; }
    }
    private void UpdateReference()=>referenceLabel.Text=reference is null ? "Görsel seçilmedi." : $"{reference.Name} · {reference.MediaType} · {reference.Data.Length} bayt";
    private DesignRequest Capture()=>new(prompt.Text,measurements.Text,reference is null ? null : reference with { Data=(byte[])reference.Data.Clone() });
    private void Apply(DesignRequest value)
    {
        DesignRequestStorage.Validate(value);loading=true;
        try { prompt.Text=value.Prompt;measurements.Text=value.Measurements;reference=value.Reference is null ? null : value.Reference with { Data=(byte[])value.Reference.Data.Clone() };UpdateReference();dirty=false; }
        finally { loading=false; }
    }
    private bool ConfirmDiscard()=>!dirty || MessageBox.Show(this,"Kaydedilmemiş istek değişiklikleri kaybolacak. Devam edilsin mi?","İstek",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)==MessageBoxResult.Yes;
    private void OnClosing(object? sender,CancelEventArgs e) { if(!ConfirmDiscard())e.Cancel=true; }
    private void ChooseReference()
    {
        var dialog=new OpenFileDialog { Title="Referans görseli seç",Filter="PNG veya JPEG (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg",Multiselect=false };
        if(dialog.ShowDialog(this)!=true)return;
        try
        {
            using var file=new FileStream(dialog.FileName,FileMode.Open,FileAccess.Read,FileShare.Read);
            if(file.Length>DesignRequestStorage.MaximumReferenceBytes)throw new InvalidDataException("Görsel 8 MiB sınırını aşıyor.");
            using var bytes=new MemoryStream();var buffer=new byte[81920];int count;
            while((count=file.Read(buffer))>0) { if(bytes.Length+count>DesignRequestStorage.MaximumReferenceBytes)throw new InvalidDataException("Görsel 8 MiB sınırını aşıyor.");bytes.Write(buffer,0,count); }
            var data=bytes.ToArray();var type=data.Length>=8 && data.AsSpan(0,8).SequenceEqual(new byte[] {137,80,78,71,13,10,26,10}) ? "image/png" : "image/jpeg";
            var candidate=new DesignReference(Path.GetFileName(dialog.FileName),type,data);DesignRequestStorage.Validate(new DesignRequest("", "",candidate));reference=candidate;dirty=true;UpdateReference();status.Text="Görsel isteğe eklendi; kaynak dosya değiştirilmedi.";
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException) { status.Text="Görsel eklenemedi; önceki seçim korundu: "+ex.Message; }
    }
    private void Open()
    {
        if(!ConfirmDiscard())return;
        var dialog=new OpenFileDialog { Title="Kalıp isteği aç",Filter="Kalıp isteği (*.tcad-request.json)|*.tcad-request.json",Multiselect=false };
        if(dialog.ShowDialog(this)!=true)return;
        try { var loaded=DesignRequestStorage.Load(dialog.FileName);Apply(loaded);status.Text="İstek açıldı; proje ve çizim değiştirilmedi."; }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.Text.DecoderFallbackException) { status.Text="İstek açılamadı; mevcut alanlar korundu: "+ex.Message; }
    }
    private void Save()
    {
        try { DesignRequestStorage.Validate(Capture()); }
        catch(InvalidDataException ex) { status.Text=ex.Message;return; }
        var dialog=new SaveFileDialog { Title="Yeni kalıp isteği dosyası",Filter="Kalıp isteği (*.tcad-request.json)|*.tcad-request.json",FileName="kalip-istegi.tcad-request.json",AddExtension=false,OverwritePrompt=false,InitialDirectory=AppPaths.PrepareProjectsDirectory() };
        if(dialog.ShowDialog(this)!=true)return;
        try { DesignRequestStorage.SaveNew(dialog.FileName,Capture());dirty=false;status.Text="İstek yeni dosyaya kaydedildi; kalıp oluşturulmadı."; }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException) { status.Text="İstek kaydedilemedi; alanlar korundu: "+ex.Message; }
    }
}
