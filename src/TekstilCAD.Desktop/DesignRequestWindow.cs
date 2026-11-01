using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Desktop;

/// <summary>Offline request preparation, independent from the open CAD project.</summary>
internal sealed class DesignRequestWindow : Window
{
    private readonly TextBox prompt=new() { AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MinHeight=95,MaxLength=DesignRequestStorage.MaximumTextLength,VerticalScrollBarVisibility=ScrollBarVisibility.Auto };
    private readonly TextBox measurements=new() { AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MinHeight=95,MaxLength=DesignRequestStorage.MaximumTextLength,VerticalScrollBarVisibility=ScrollBarVisibility.Auto };
    private readonly TextBox conversation=new() { IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,Height=170,VerticalScrollBarVisibility=ScrollBarVisibility.Auto };
    private readonly TextBox chatInput=new() { AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MinHeight=54,MaxHeight=100,MaxLength=DesignRequestStorage.MaximumTextLength,VerticalScrollBarVisibility=ScrollBarVisibility.Auto };
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
    internal TextBox ChatInputForVerification=>chatInput;
    internal TextBox ConversationForVerification=>conversation;
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
        body.Children.Add(new TextBlock { Text="Bir başlangıç modeli seçin, sonra isteğinizi ve bildiğiniz ölçüleri yazın. Eksik ölçüler tahmin edilmez. Bu çevrimdışı adım henüz kalıp veya plotter çıktısı oluşturmaz.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,10) });
        body.Children.Add(new TextBlock { Text="Model fikri seçin · Kartlar yalnız açıklama başlangıcıdır; hazır kalıp değildir.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,4) });
        var models=new WrapPanel { Margin=new Thickness(0,0,0,6) };body.Children.Add(models);
        void ModelChoice(string label,string template,string silhouette,string? detail)
        {
            var art=new Canvas { Width=80,Height=80,IsHitTestVisible=false };
            var dress=new System.Windows.Shapes.Path { Data=Geometry.Parse(silhouette),Fill=new SolidColorBrush(Color.FromRgb(0x9e,0xc8,0xc0)),Stroke=new SolidColorBrush(Color.FromRgb(0xd1,0xe8,0xe2)),StrokeThickness=1.5 };
            art.Children.Add(dress);
            if(detail is not null)art.Children.Add(new System.Windows.Shapes.Path { Data=Geometry.Parse(detail),Stroke=new SolidColorBrush(Color.FromRgb(0x35,0x62,0x60)),StrokeThickness=1.5,StrokeLineJoin=PenLineJoin.Round });
            var image=new Viewbox { Width=80,Height=80,Child=art,IsHitTestVisible=false,Margin=new Thickness(0,1,0,4) };
            var content=new StackPanel { Width=112,IsHitTestVisible=false };
            content.Children.Add(image);
            content.Children.Add(new TextBlock { Text=label,TextAlignment=TextAlignment.Center,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap,MinHeight=34 });
            content.Children.Add(new TextBlock { Text="Tarife ekle",TextAlignment=TextAlignment.Center,FontSize=11,Foreground=StudioTheme.Muted });
            var button=new Button { Content=content,Margin=new Thickness(0,0,8,8),Padding=new Thickness(6),HorizontalContentAlignment=HorizontalAlignment.Center,ToolTip="Tarife model fikri ekle; hazır kalıp değildir." };
            AutomationProperties.SetName(button,label);
            AutomationProperties.SetHelpText(button,"Açıklamaya örnek model metni ekler; mevcut metin korunur ve kalıp oluşturulmaz.");
            button.Click+=(_,_)=>
            {
                var addition=(prompt.Text.Length==0 ? "" : "\n")+template;
                if((long)prompt.Text.Length+addition.Length+addition.Count(c=>c=='\n')>prompt.MaxLength) { status.Text="Örnek eklenemedi: açıklama uzunluk sınırına ulaşır. Mevcut metin korundu.";return; }
                prompt.AppendText(addition);chatInput.Focus();status.Text="Model açıklaması eklendi. Özellikleri düzenleyebilir, bilinen ölçüleri girebilirsiniz; kalıp oluşturulmadı.";
            };
            models.Children.Add(button);
        }
        ModelChoice("Düz elbise", "Düz elbise; detaylar: ","M 34,5 L 25,10 20,22 27,26 29,39 25,73 55,73 51,39 53,26 60,22 55,10 46,5 Q 40,14 34,5 Z",null);
        ModelChoice("A kesim elbise", "A kesim elbise; detaylar: ","M 34,5 L 25,10 20,22 27,26 30,38 14,73 66,73 50,38 53,26 60,22 55,10 46,5 Q 40,14 34,5 Z",null);
        ModelChoice("Anvelop elbise", "Anvelop elbise; detaylar: ","M 34,5 L 25,10 20,22 27,26 30,40 17,73 63,73 50,40 53,26 60,22 55,10 46,5 Q 40,14 34,5 Z","M 34,5 L 49,39 M 46,5 L 31,39 M 30,40 L 50,40 M 49,40 L 58,73");
        ModelChoice("Gömlek elbise", "Gömlek elbise; detaylar: ","M 34,5 L 25,10 19,22 27,27 29,40 23,73 57,73 51,40 53,27 61,22 55,10 46,5 L 40,10 Z","M 34,5 L 34,15 40,10 46,15 46,5 M 40,10 L 40,73 M 30,40 L 50,40");
        body.Children.Add(new Label { Foreground=StudioTheme.Text,Content="Konuşma ve sıradaki soru",Target=conversation });
        AutomationProperties.SetName(conversation,"İsteğiniz ve sıradaki ölçü sorusu");body.Children.Add(conversation);
        body.Children.Add(new Label { Foreground=StudioTheme.Text,Content="Mesajınız",Target=chatInput });AutomationProperties.SetName(chatInput,"Elbise isteği veya ölçü mesajı");body.Children.Add(chatInput);
        var send=new Button { Content="Gönder",HorizontalAlignment=HorizontalAlignment.Left,Padding=new Thickness(12,6,12,6),Margin=new Thickness(0,4,0,8) };
        AutomationProperties.SetHelpText(send,"Mesajı isteğe ekler ve açık ölçülerden sıradaki soruyu gösterir. Kalıp oluşturmaz.");body.Children.Add(send);send.Click+=(_,_)=>SendMessage();
        AutomationProperties.SetLiveSetting(status,AutomationLiveSetting.Polite);body.Children.Add(status);
        body.Children.Add(new TextBlock { Text="Ölçüyü açık etiket ve birimle yazın: bel çevresi 72 cm. Her ölçüyü ayrı satırla veya noktalı virgülle ayırın. Bu çevrimdışı yardımcı genel konuşma veya görsel yorumlama yapmaz.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,8) });
        var editBody=new StackPanel();body.Children.Add(new Expander { Header="İstek ve ölçüleri düzenle",Content=editBody,Margin=new Thickness(0,8,0,8) });
        editBody.Children.Add(new Label { Foreground=StudioTheme.Text,Content="Kadın elbise modeli ve özellikleri",Target=prompt });AutomationProperties.SetName(prompt,"Kadın elbise modeli ve özellikleri");editBody.Children.Add(prompt);
        editBody.Children.Add(new Label { Foreground=StudioTheme.Text,Content="Ölçüler ve birimler",Target=measurements });AutomationProperties.SetName(measurements,"Ölçüler ve birimler");editBody.Children.Add(measurements);
        editBody.Children.Add(new TextBlock { Text="Açık ölçüleri satır veya noktalı virgülle ayırın: göğüs çevresi 96 cm; bel çevresi 72 cm; kalça çevresi 100 cm; elbise boyu 110 cm. Yalnız açık çevre veya boy etiketi ve mm/cm birimi kullanılır; göğüs, bel veya kalça tek başına çevre varsayılmaz. Virgül/nokta ondalık ayracıdır. Kol boyu yalnız açıkça belirtilirse alınır.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,4) });
        var checkMeasurements=new Button { Content="Açık ölçüleri denetle",HorizontalAlignment=HorizontalAlignment.Left,Padding=new Thickness(10,5,10,5),Margin=new Thickness(0,4,0,4) };
        editBody.Children.Add(checkMeasurements);checkMeasurements.Click+=(_,_)=>ReviewMeasurements();
        editBody.Children.Add(new Label { Foreground=StudioTheme.Text,Content="Ölçü denetimi sonucu",Target=measurementReview });AutomationProperties.SetName(measurementReview,"Ölçü denetimi sonucu");editBody.Children.Add(measurementReview);
        body.Children.Add(new TextBlock { Text="Referans görseli",FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,12,0,4) });body.Children.Add(referenceLabel);
        var refActions=new WrapPanel();body.Children.Add(refActions);
        var choose=new Button { Content="Görsel seç…",Padding=new Thickness(10,5,10,5),Margin=new Thickness(0,4,8,4) };refActions.Children.Add(choose);choose.Click+=(_,_)=>ChooseReference();
        var remove=new Button { Content="Görseli kaldır",Padding=new Thickness(10,5,10,5),Margin=new Thickness(0,4,0,4) };refActions.Children.Add(remove);remove.Click+=(_,_)=> { if(reference is not null) { reference=null;dirty=true;UpdateReference(); } };
        body.Children.Add(new TextBlock { Text="Görsel dosyanın tüm baytları ve varsa içindeki metadata isteğe eklenir. Yalnızca PNG/JPEG imzası kontrol edilir; görsel içeriği, ölçüler veya üretime uygunluk doğrulanmaz. Kaydetme yeni ve ayrı bir dosya oluşturur; kalıp çizimi ve plotter çıktısı için sonraki işlem ve kullanıcı kontrolü gerekir.",TextWrapping=TextWrapping.Wrap,Foreground=StudioTheme.Warning,Margin=new Thickness(0,10,0,0) });

        prompt.TextChanged+=(_,_)=> { measurementReview.Clear();if(!loading) { dirty=true;RefreshConversation(); } };measurements.TextChanged+=(_,_)=> { measurementReview.Clear();if(!loading) { dirty=true;RefreshConversation(); } };UpdateReference();RefreshConversation();Closing+=OnClosing;
    }
    private void SendMessage()
    {
        var message=chatInput.Text;
        if(string.IsNullOrWhiteSpace(message)) { status.Text="Göndermek için bir mesaj yazın.";return; }
        var addition=(prompt.Text.Length==0 ? "" : "\n")+message;
        if((long)prompt.Text.Length+addition.Length+addition.Count(c=>c=='\n')>prompt.MaxLength)
        { status.Text="Mesaj eklenemedi: açıklama uzunluk sınırına ulaşıldı. Mesaj ve mevcut istek korundu.";return; }
        prompt.AppendText(addition);chatInput.Clear();chatInput.Focus();status.Text="Mesaj isteğe eklendi; henüz kalıp oluşturulmadı.";
    }
    private void RefreshConversation()
    {
        DressMeasurementReview review;
        try { review=DressMeasurementParser.Review(prompt.Text,measurements.Text); }
        catch(System.Text.RegularExpressions.RegexMatchTimeoutException) { conversation.Text="Ölçü denetimi zaman aşımına uğradı; istek korundu. İfadeleri kısaltıp tekrar deneyin.";return; }
        var lines=new List<string> { "Siz:",string.IsNullOrEmpty(prompt.Text) ? "Henüz istek yazılmadı." : prompt.Text };
        if(!string.IsNullOrEmpty(measurements.Text)) { lines.Add("Ölçü notlarınız:");lines.Add(measurements.Text); }
        lines.Add("Yardımcı:");
        if(review.Measurements.Count>0)lines.Add("Açıkça bulunanlar: "+string.Join(", ",review.Measurements.Select(m=>$"{m.Label}: {m.Millimeters.ToString(System.Globalization.CultureInfo.InvariantCulture)} mm"))+".");
        if(review.Questions.Count>0)
        {
            var question=review.Questions[0];
            var example=question.StartsWith("göğüs çevresi",StringComparison.Ordinal) ? "göğüs çevresi 96 cm" :
                question.StartsWith("bel çevresi",StringComparison.Ordinal) ? "bel çevresi 72 cm" :
                question.StartsWith("kalça çevresi",StringComparison.Ordinal) ? "kalça çevresi 100 cm" :
                question.StartsWith("elbise boyu",StringComparison.Ordinal) ? "elbise boyu 110 cm" : "kol boyu 62 cm";
            lines.Add(question+" Örnek: "+example+".");
            if(question.Contains("çelişiyor",StringComparison.Ordinal))lines.Add("Çelişen önceki ifadeyi İstek ve ölçüleri düzenle bölümünde düzeltin.");
        }
        else lines.Add("Temel ölçüler açıkça yazıldı. Ölçülerin niteliğini ve model ayrıntılarını kontrol edin.");
        if(review.Unrecognized.Count>0)lines.Add("Diğer ifadeler ölçü olarak yorumlanmadı; açıklamanız aynen korundu.");
        lines.Add("Bu aşamada kalıp veya plotter çıktısı oluşturulmaz.");
        conversation.Text=string.Join(Environment.NewLine,lines);conversation.ScrollToEnd();
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
        try { prompt.Text=value.Prompt;measurements.Text=value.Measurements;reference=value.Reference is null ? null : value.Reference with { Data=(byte[])value.Reference.Data.Clone() };chatInput.Clear();UpdateReference();dirty=false;RefreshConversation(); }
        finally { loading=false; }
    }
    private bool ConfirmDiscard()=>!dirty && string.IsNullOrEmpty(chatInput.Text) || MessageBox.Show(this,"Kaydedilmemiş istek değişiklikleri kaybolacak. Devam edilsin mi?","İstek",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)==MessageBoxResult.Yes;
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
        if(!string.IsNullOrEmpty(chatInput.Text)) { status.Text="Kaydetmeden önce mesajı gönderin veya silin; mevcut mesaj korunuyor.";return; }
        try { DesignRequestStorage.Validate(Capture()); }
        catch(InvalidDataException ex) { status.Text=ex.Message;return; }
        var dialog=new SaveFileDialog { Title="Yeni kalıp isteği dosyası",Filter="Kalıp isteği (*.tcad-request.json)|*.tcad-request.json",FileName="kalip-istegi.tcad-request.json",AddExtension=false,OverwritePrompt=false,InitialDirectory=AppPaths.PrepareProjectsDirectory() };
        if(dialog.ShowDialog(this)!=true)return;
        try { DesignRequestStorage.SaveNew(dialog.FileName,Capture());dirty=false;status.Text="İstek yeni dosyaya kaydedildi; kalıp oluşturulmadı."; }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException) { status.Text="İstek kaydedilemedi; alanlar korundu: "+ex.Message; }
    }
}
