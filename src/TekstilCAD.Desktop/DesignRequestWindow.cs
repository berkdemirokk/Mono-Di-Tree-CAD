using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Desktop;

/// <summary>Offline request preparation, independent from the open CAD project.</summary>
internal sealed class DesignRequestWindow : Window
{
    private static Brush ColorBrush(string color) { var brush=(SolidColorBrush)new BrushConverter().ConvertFromString(color)!;brush.Freeze();return brush; }
    internal static readonly Brush Ink=ColorBrush("#241B1E"), Muted=ColorBrush("#59484C"), Accent=ColorBrush("#4A1525"), Surface=ColorBrush("#FFFFFF"), Raised=ColorBrush("#F6F3F2"), BorderColor=ColorBrush("#D7C1C4"), BackgroundColor=ColorBrush("#FCF9F8");
    private readonly TextBox prompt=new() { AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MinHeight=95,MaxLength=DesignRequestStorage.MaximumTextLength,VerticalScrollBarVisibility=ScrollBarVisibility.Auto };
    private readonly TextBox measurements=new() { AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MinHeight=95,MaxLength=DesignRequestStorage.MaximumTextLength,VerticalScrollBarVisibility=ScrollBarVisibility.Auto };
    private readonly TextBox conversation=new() { IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,Height=128,Background=Surface,Foreground=Ink,BorderThickness=new Thickness(0),Padding=new Thickness(8),VerticalScrollBarVisibility=ScrollBarVisibility.Auto };
    private readonly TextBox chatInput=new() { AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MinHeight=48,MaxHeight=100,Background=BackgroundColor,Foreground=Ink,Padding=new Thickness(10),MaxLength=DesignRequestStorage.MaximumTextLength,VerticalScrollBarVisibility=ScrollBarVisibility.Auto };
    private readonly Button measurementShortcut=new() { Visibility=Visibility.Collapsed,HorizontalAlignment=HorizontalAlignment.Left,Padding=new Thickness(10,5,10,5),Margin=new Thickness(0,0,0,6) };
    private string? suggestedMeasurement;
    private readonly TextBlock referenceLabel=new() { TextWrapping=TextWrapping.Wrap };
    private readonly TextBox measurementReview=new() { IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MinHeight=100,VerticalScrollBarVisibility=ScrollBarVisibility.Auto };
    private readonly TextBlock status=new() { TextWrapping=TextWrapping.Wrap,Foreground=Ink,Margin=new Thickness(8,4,8,4) };
    private readonly ScrollViewer statusViewport=new() { MaxHeight=72,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Visibility=Visibility.Collapsed,Focusable=true };
    private readonly ScrollViewer bodyScroller=new() { VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Focusable=true };
    private readonly ScrollViewer modelScroller=new() { HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled,Focusable=true };
    private DesignReference? reference;
    private bool dirty,loading,preservingLastModel,hasSavedRequest;
    private readonly List<(Button Button,TextBlock Caption)> modelCards=[];
    private readonly Dictionary<string,TextBlock> measurementTiles=new(StringComparer.Ordinal);
    internal TextBox PromptForVerification=>prompt;
    internal TextBox MeasurementsForVerification=>measurements;
    internal DesignRequest CaptureForVerification()=>Capture();
    internal void LoadForVerification(DesignRequest value)=>Apply(value);
    internal bool DirtyForVerification=>dirty;
    internal TextBox ReviewForVerification=>measurementReview;
    internal TextBox ChatInputForVerification=>chatInput;
    internal TextBox ConversationForVerification=>conversation;
    internal TextBlock StatusForVerification=>status;
    internal ScrollViewer StatusViewportForVerification=>statusViewport;
    internal ScrollViewer BodyScrollerForVerification=>bodyScroller;
    internal ScrollViewer ModelScrollerForVerification=>modelScroller;
    internal IReadOnlyDictionary<string,TextBlock> MeasurementTilesForVerification=>measurementTiles;
    internal void ReviewMeasurementsForVerification()=>ReviewMeasurements();
    internal DesignRequestWindow(Window owner)
    {
        Owner=owner;Title="Elbise isteği";Width=900;Height=700;MinWidth=480;MinHeight=420;WindowStartupLocation=WindowStartupLocation.CenterOwner;StudioTheme.Apply(this);
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source=new Uri("DesignRequestTheme.xaml",UriKind.Relative) });Background=BackgroundColor;Foreground=Ink;
        var root=new DockPanel { Margin=new Thickness(16) };Content=root;
        var actions=new WrapPanel { Margin=new Thickness(0,8,0,0) };DockPanel.SetDock(actions,Dock.Bottom);root.Children.Add(actions);
        Button Action(string text,Action action) { var b=new Button { Content=text,Padding=new Thickness(12,7,12,7),Margin=new Thickness(0,0,8,4) };b.Click+=(_,_)=>action();actions.Children.Add(b);return b; }
        Action("İstek aç…",Open);var save=Action("Yeni istek dosyası kaydet…",Save);save.Background=Accent;save.Foreground=Surface;save.BorderBrush=Accent;save.FontWeight=FontWeights.SemiBold;Action("Çizim araçlarına geç",Close);
        AutomationProperties.SetLiveSetting(status,AutomationLiveSetting.Polite);
        AutomationProperties.SetName(statusViewport,"İşlem durumu");
        statusViewport.Content=status;DockPanel.SetDock(statusViewport,Dock.Bottom);root.Children.Add(statusViewport);
        var body=new StackPanel();bodyScroller.Content=body;root.Children.Add(bodyScroller);
        body.Children.Add(new TextBlock { Text="MONO Dİ TREE CAD  ·  KALIP STÜDYOSU",Foreground=Muted,FontSize=11,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,8) });
        body.Children.Add(new TextBlock { Text="Modelini seç",FontFamily=new FontFamily("Georgia"),Foreground=Accent,FontSize=27,FontWeight=FontWeights.Normal });
        body.Children.Add(new TextBlock { Text="Bir model fikri seçin veya doğrudan tarif edin. Bildiğiniz ölçülerle devam edebilirsiniz.",Foreground=Muted,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,3,0,12) });
        var drawingNotes=new Button { Content="Kaydedilmiş notlarla çizime geç",HorizontalAlignment=HorizontalAlignment.Left,Padding=new Thickness(12,7,12,7),Margin=new Thickness(0,0,0,8) };
        AutomationProperties.SetHelpText(drawingNotes,"Kaydedilmiş isteğin salt okunur notlarını ana çizim penceresiyle birlikte açar. Kalıp oluşturmaz.");
        drawingNotes.Click+=(_,_)=>OpenDrawingNotes();body.Children.Add(drawingNotes);
        body.Children.Add(new TextBlock { Text="01  ·  MODEL FİKRİ",FontWeight=FontWeights.SemiBold,Foreground=Ink,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,6) });
        body.Children.Add(new TextBlock { Text="Kartlar tarife fikir ekler; hazır kalıp değildir.",Foreground=Muted,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,6) });
        var models=new StackPanel { Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,0,6) };modelScroller.Content=models;body.Children.Add(modelScroller);
        AutomationProperties.SetName(modelScroller,"Model fikirleri galerisi");AutomationProperties.SetHelpText(modelScroller,"Diğer model fikirlerini görmek için yatay kaydırın veya kartlar arasında klavyeyle ilerleyin.");
        void ModelChoice(string label,string description,string template,string silhouette,string? detail)
        {
            var art=new Canvas { Width=80,Height=80,IsHitTestVisible=false };
            var dress=new System.Windows.Shapes.Path { Data=Geometry.Parse(silhouette),Fill=Brushes.Transparent,Stroke=Accent,StrokeThickness=1.2 };
            art.Children.Add(dress);
            if(detail is not null)art.Children.Add(new System.Windows.Shapes.Path { Data=Geometry.Parse(detail),Stroke=Accent,StrokeThickness=1.2,StrokeLineJoin=PenLineJoin.Round });
            var image=new Viewbox { Width=112,Height=112,Child=art,IsHitTestVisible=false,Margin=new Thickness(0,4,0,4) };
            var content=new StackPanel { Width=166,IsHitTestVisible=false };
            content.Children.Add(new Border { Background=Raised,CornerRadius=new CornerRadius(6),Child=image,Margin=new Thickness(0,0,0,5) });
            content.Children.Add(new TextBlock { Text=label,TextAlignment=TextAlignment.Center,FontFamily=new FontFamily("Georgia"),FontSize=14,Foreground=Accent,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap,MinHeight=34 });
            content.Children.Add(new TextBlock { Text=description,TextAlignment=TextAlignment.Center,FontSize=11,Foreground=Muted,TextWrapping=TextWrapping.Wrap,MinHeight=32 });
            var caption=new TextBlock { Text="Tarife ekle",TextAlignment=TextAlignment.Center,FontSize=11,Foreground=Muted };content.Children.Add(caption);
            var button=new Button { Content=content,Background=Surface,BorderBrush=BorderColor,BorderThickness=new Thickness(2),Margin=new Thickness(0,0,8,8),Padding=new Thickness(6),HorizontalContentAlignment=HorizontalAlignment.Center,ToolTip="Tarife model fikri ekle; hazır kalıp değildir." };
            AutomationProperties.SetName(button,label);
            AutomationProperties.SetHelpText(button,"Açıklamaya örnek model metni ekler; mevcut metin korunur ve kalıp oluşturulmaz.");
            button.Click+=(_,_)=>
            {
                var addition=(prompt.Text.Length==0 ? "" : "\n")+template;
                if((long)prompt.Text.Length+addition.Length+addition.Count(c=>c=='\n')>prompt.MaxLength) { SetStatus("Örnek eklenemedi: açıklama uzunluk sınırına ulaşır. Mevcut metin korundu.");return; }
                prompt.AppendText(addition);SetLastModel(button);prompt.Focus();SetStatus("Model açıklaması eklendi. Özellikleri düzenleyebilir, bilinen ölçüleri girebilirsiniz; kalıp oluşturulmadı.");
            };
            modelCards.Add((button,caption));models.Children.Add(button);
        }
        ModelChoice("Anvelop elbise","Çapraz ön · Bel hattı","Anvelop elbise; detaylar: ","M 34,5 L 25,10 20,22 27,26 30,40 17,73 63,73 50,40 53,26 60,22 55,10 46,5 Q 40,14 34,5 Z","M 34,5 L 49,39 M 46,5 L 31,39 M 30,40 L 50,40 M 49,40 L 58,73");
        ModelChoice("A kesim elbise","Genişleyen etek","A kesim elbise; detaylar: ","M 34,5 L 25,10 20,22 27,26 30,38 14,73 66,73 50,38 53,26 60,22 55,10 46,5 Q 40,14 34,5 Z",null);
        ModelChoice("Balenli korpus","Yapısal gövde · Bel hattı","Balenli korpus; detaylar: ","M 26,14 Q 33,17 40,13 Q 47,17 54,14 L 53,34 51,40 57,73 23,73 29,40 27,34 Z","M 32,15 L 34,40 M 48,15 L 46,40 M 40,13 L 40,40 M 29,40 Q 40,44 51,40 M 40,42 L 40,73");
        ModelChoice("Minimal kılıf","Düz form · Kolsuz hat","Minimal kılıf elbise; detaylar: ","M 34,7 Q 40,10 46,7 L 51,10 53,22 48,22 48,35 51,73 29,73 32,35 32,22 27,22 29,10 Z","M 35,24 L 35,38 M 45,24 L 45,38 M 40,9 L 40,73");
        ModelChoice("Düz elbise","Düz siluet","Düz elbise; detaylar: ","M 34,5 L 25,10 20,22 27,26 29,39 25,73 55,73 51,39 53,26 60,22 55,10 46,5 Q 40,14 34,5 Z",null);
        ModelChoice("Gömlek elbise","Yakalı ön · Bel hattı","Gömlek elbise; detaylar: ","M 34,5 L 25,10 19,22 27,27 29,40 23,73 57,73 51,40 53,27 61,22 55,10 46,5 L 40,10 Z","M 34,5 L 34,15 40,10 46,15 46,5 M 40,10 L 40,73 M 30,40 L 50,40");
        body.Children.Add(new TextBlock { Text="Nasıl bir elbise istiyorsun?",FontFamily=new FontFamily("Georgia"),FontSize=21,Foreground=Accent,Margin=new Thickness(0,14,0,4) });
        body.Children.Add(new TextBlock { Text="Kumaşı, yakayı, boyu ve diğer model ayrıntılarını tarif edin.",Foreground=Muted,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,6) });
        var editBody=new StackPanel();body.Children.Add(editBody);
        body.Children.Add(new TextBlock { Text="Ölçüler için yazışma",FontFamily=new FontFamily("Georgia"),FontSize=19,Foreground=Accent,Margin=new Thickness(0,14,0,4) });
        body.Children.Add(new Label { Foreground=Ink,Content="Konuşma ve sıradaki soru",Target=conversation,Padding=new Thickness(0,4,0,4) });
        AutomationProperties.SetName(conversation,"İsteğiniz ve sıradaki ölçü sorusu");body.Children.Add(new Border { Child=conversation,Background=Surface,BorderBrush=BorderColor,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(8),Padding=new Thickness(3),Margin=new Thickness(0,0,0,6) });
        AutomationProperties.SetHelpText(measurementShortcut,"Sorulan ölçünün etiketini mesajın sonuna ekler; sayı veya birim seçmez. Mevcut metin korunur.");
        body.Children.Add(measurementShortcut);measurementShortcut.Click+=(_,_)=>InsertMeasurementLabel();
        body.Children.Add(new Label { Foreground=Ink,Content="Mesajınız",Target=chatInput,Padding=new Thickness(0,4,0,4) });AutomationProperties.SetName(chatInput,"Elbise isteği veya ölçü mesajı");
        var compose=new DockPanel { LastChildFill=true,Margin=new Thickness(0,0,0,6) };body.Children.Add(compose);
        var send=new Button { Content="Gönder",HorizontalAlignment=HorizontalAlignment.Left,Padding=new Thickness(12,6,12,6),Margin=new Thickness(0,4,0,8) };
        send.Background=Accent;send.Foreground=Surface;send.FontWeight=FontWeights.SemiBold;send.Margin=new Thickness(8,0,0,0);DockPanel.SetDock(send,Dock.Right);
        AutomationProperties.SetHelpText(send,"Mesajı isteğe ekler ve açık ölçülerden sıradaki soruyu gösterir. Kalıp oluşturmaz.");compose.Children.Add(send);compose.Children.Add(chatInput);send.Click+=(_,_)=>SendMessage();
        body.Children.Add(new TextBlock { Text="Sorulan ölçüyü yalnızca değer ve birimle yanıtlayabilirsiniz: 72 cm. Diğer ölçüleri açık etiket ve birimle yazın: bel çevresi 72 cm. Her ölçüyü ayrı satırla veya noktalı virgülle ayırın. Bu çevrimdışı yardımcı genel konuşma veya görsel yorumlama yapmaz.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,8) });
        editBody.Children.Add(new Label { Foreground=Ink,Content="Tasarımcı notları",Target=prompt });AutomationProperties.SetName(prompt,"Kadın elbise modeli ve özellikleri");editBody.Children.Add(prompt);
        editBody.Children.Add(new TextBlock { Text="Ölçülerini ekle",FontFamily=new FontFamily("Georgia"),FontSize=21,Foreground=Accent,Margin=new Thickness(0,14,0,4) });
        editBody.Children.Add(new TextBlock { Text="Yalnız açıkça yazılan değerler gösterilir; beden veya eksik ölçü varsayılmaz.",Foreground=Muted,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,8) });
        var measurementCards=new WrapPanel();editBody.Children.Add(measurementCards);
        void MeasurementCard(string key,string title)
        {
            var content=new StackPanel();content.Children.Add(new TextBlock { Text=title,FontSize=11,Foreground=Muted,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap });
            var value=new TextBlock { Text="Belirtilmedi",FontSize=15,Foreground=Ink,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,5,0,0),TextWrapping=TextWrapping.Wrap };
            AutomationProperties.SetName(value,title+": belirtilmedi");content.Children.Add(value);measurementTiles.Add(key,value);
            measurementCards.Children.Add(new Border { Width=136,MinHeight=70,Background=Raised,CornerRadius=new CornerRadius(8),Padding=new Thickness(10,8,10,8),Margin=new Thickness(0,0,8,8),Child=content });
        }
        MeasurementCard("göğüs çevresi","GÖĞÜS ÇEVRESİ");MeasurementCard("bel çevresi","BEL ÇEVRESİ");MeasurementCard("kalça çevresi","KALÇA ÇEVRESİ");MeasurementCard("elbise boyu","ELBİSE BOYU");
        editBody.Children.Add(new Label { Foreground=Ink,Content="Ölçüler ve birimler",Target=measurements });AutomationProperties.SetName(measurements,"Ölçüler ve birimler");editBody.Children.Add(measurements);
        editBody.Children.Add(new TextBlock { Text="Açık ölçüleri satır veya noktalı virgülle ayırın: göğüs çevresi 96 cm; bel çevresi 72 cm; kalça çevresi 100 cm; elbise boyu 110 cm. Yalnız açık çevre veya boy etiketi ve mm/cm birimi kullanılır; göğüs, bel veya kalça tek başına çevre varsayılmaz. Virgül/nokta ondalık ayracıdır. Kol boyu yalnız açıkça belirtilirse alınır.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,4) });
        var checkMeasurements=new Button { Content="Açık ölçüleri denetle",HorizontalAlignment=HorizontalAlignment.Left,Padding=new Thickness(10,5,10,5),Margin=new Thickness(0,4,0,4) };
        editBody.Children.Add(checkMeasurements);checkMeasurements.Click+=(_,_)=>ReviewMeasurements();
        editBody.Children.Add(new Label { Foreground=Ink,Content="Ölçü denetimi sonucu",Target=measurementReview });AutomationProperties.SetName(measurementReview,"Ölçü denetimi sonucu");editBody.Children.Add(measurementReview);
        body.Children.Add(new TextBlock { Text="03  ·  REFERANS GÖRSELİ",Foreground=Ink,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,12,0,4) });body.Children.Add(referenceLabel);
        var refActions=new WrapPanel();body.Children.Add(refActions);
        var choose=new Button { Content="Görsel seç…",Padding=new Thickness(10,5,10,5),Margin=new Thickness(0,4,8,4) };refActions.Children.Add(choose);choose.Click+=(_,_)=>ChooseReference();
        var remove=new Button { Content="Görseli kaldır",Padding=new Thickness(10,5,10,5),Margin=new Thickness(0,4,0,4) };refActions.Children.Add(remove);remove.Click+=(_,_)=> { if(reference is not null) { reference=null;dirty=true;UpdateReference(); } };
        body.Children.Add(new TextBlock { Text="Görsel dosyanın tüm baytları ve varsa içindeki metadata isteğe eklenir. Yalnızca PNG/JPEG imzası kontrol edilir; görsel içeriği, ölçüler veya üretime uygunluk doğrulanmaz. Kaydetme yeni ve ayrı bir dosya oluşturur; kalıp çizimi ve plotter çıktısı için sonraki işlem ve kullanıcı kontrolü gerekir.",TextWrapping=TextWrapping.Wrap,Foreground=Muted,Margin=new Thickness(0,10,0,0) });

        prompt.TextChanged+=(_,_)=> { measurementReview.Clear();if(!preservingLastModel)SetLastModel(null);if(!loading) { dirty=true;RefreshConversation(); } };measurements.TextChanged+=(_,_)=> { measurementReview.Clear();if(!loading) { dirty=true;RefreshConversation(); } };UpdateReference();RefreshConversation();Closing+=OnClosing;
    }
    private void SetStatus(string message)
    {
        status.Text=message;
        statusViewport.Visibility=string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
        statusViewport.ScrollToTop();
        if(UIElementAutomationPeer.FromElement(status) is { } peer)
            peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
    internal RequestNotesWindow? CreateDrawingNotes()
    {
        if(!string.IsNullOrEmpty(chatInput.Text)) { SetStatus("Çizime geçmeden önce mesajı gönderin veya silin ve isteği kaydedin; mesaj korundu.");return null; }
        if(!hasSavedRequest || dirty) { SetStatus("Çizime geçmeden önce isteği yeni bir dosyaya kaydedin; mevcut alanlar korundu.");return null; }
        if(Owner is not Window owner) { SetStatus("Ana çizim penceresi bulunamadı; istek korundu.");return null; }
        return new RequestNotesWindow(owner,Capture());
    }
    private void OpenDrawingNotes()
    {
        var notes=CreateDrawingNotes();if(notes is null)return;
        var owner=notes.Owner!;bool closed=false;EventHandler markClosed=(_,_)=>closed=true;Closed+=markClosed;
        Close();
        if(!closed) { Closed-=markClosed;return; }
        // Opening after the modal dialog unwinds lets the owner receive input.
        _=owner.Dispatcher.BeginInvoke(new Action(()=> { if(owner.IsLoaded)notes.Show(); }));
    }
    private void SetLastModel(Button? selected)
    {
        foreach(var (button,caption) in modelCards)
        {
            var active=ReferenceEquals(button,selected);
            button.Background=active ? Raised : Surface;
            button.BorderBrush=active ? Accent : BorderColor;
            caption.Text=active ? "Son eklenen" : "Tarife ekle";
            caption.Foreground=active ? Accent : Muted;
            AutomationProperties.SetHelpText(button,(active ? "Son eklenen model fikri. " : "")+"Açıklamaya örnek model metni ekler; mevcut metin korunur ve kalıp oluşturulmaz.");
        }
    }
    private void SendMessage()
    {
        var message=chatInput.Text;
        if(string.IsNullOrWhiteSpace(message)) { SetStatus("Göndermek için bir mesaj yazın.");return; }
        // Only an entire, unambiguous value with an explicit supported unit answers the current question.
        var answerLabel=suggestedMeasurement is not null && System.Text.RegularExpressions.Regex.IsMatch(message,@"\A[ \t]*[0-9]{1,5}(?:[.,][0-9]{1,8})?[ \t]*(?:mm|cm)[ \t]*\z",System.Text.RegularExpressions.RegexOptions.IgnoreCase|System.Text.RegularExpressions.RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100)) ? suggestedMeasurement : null;
        var addition=(prompt.Text.Length==0 ? "" : "\n")+(answerLabel is null ? "" : answerLabel+": ")+message;
        if((long)prompt.Text.Length+addition.Length+addition.Count(c=>c=='\n')>prompt.MaxLength)
        { SetStatus("Mesaj eklenemedi: açıklama uzunluk sınırına ulaşıldı. Mesaj ve mevcut istek korundu.");return; }
        preservingLastModel=true;
        try { prompt.AppendText(addition); }
        finally { preservingLastModel=false; }
        chatInput.Clear();chatInput.Focus();SetStatus(answerLabel is null ? "Mesaj isteğe eklendi; henüz kalıp oluşturulmadı." : $"Yanıt {answerLabel} olarak eklendi; henüz kalıp oluşturulmadı.");
    }
    private void InsertMeasurementLabel()
    {
        var label=suggestedMeasurement;
        if(label is null)return;
        var text=label+": ";
        if(chatInput.Text.EndsWith(text,StringComparison.Ordinal)) { chatInput.Focus();return; }
        var addition=(chatInput.Text.Length==0 ? "" : "\n")+text;
        if((long)chatInput.Text.Length+addition.Length+addition.Count(c=>c=='\n')>chatInput.MaxLength)
        { SetStatus("Ölçü etiketi eklenemedi: mesaj uzunluk sınırına ulaşıldı. Mevcut mesaj korundu.");return; }
        chatInput.AppendText(addition);chatInput.CaretIndex=chatInput.Text.Length;chatInput.Focus();
        SetStatus("Ölçü etiketi eklendi. Değeri ve birimi siz yazın.");
    }
    private void UpdateMeasurementShortcut(DressMeasurementReview review)
    {
        var question=review.Questions.FirstOrDefault();
        suggestedMeasurement=null;
        if(question is not null && question.EndsWith(": açık ölçü ve birim nedir?",StringComparison.Ordinal))
        {
            var label=question[..question.IndexOf(':')];
            if(label is "göğüs çevresi" or "bel çevresi" or "kalça çevresi" or "elbise boyu" or "kol boyu")suggestedMeasurement=label;
        }
        measurementShortcut.Visibility=suggestedMeasurement is null ? Visibility.Collapsed : Visibility.Visible;
        if(suggestedMeasurement is not null)
        {
            measurementShortcut.Content="Etiketi ekle: "+suggestedMeasurement;
            AutomationProperties.SetName(measurementShortcut,"Sorulan ölçü etiketini ekle: "+suggestedMeasurement);
        }
    }
    private void RefreshConversation()
    {
        DressMeasurementReview review;
        try { review=DressMeasurementParser.Review(prompt.Text,measurements.Text); }
        catch(System.Text.RegularExpressions.RegexMatchTimeoutException) { suggestedMeasurement=null;measurementShortcut.Visibility=Visibility.Collapsed;foreach(var (key,tile) in measurementTiles) { tile.Text="Denetlenemedi";AutomationProperties.SetName(tile,key+": denetlenemedi"); } conversation.Text="Ölçü denetimi zaman aşımına uğradı; istek korundu. İfadeleri kısaltıp tekrar deneyin.";return; }
        foreach(var (key,tile) in measurementTiles)
        {
            var found=review.Measurements.FirstOrDefault(m=>m.Label==key);
            var ambiguous=review.Questions.Any(q=>q.StartsWith(key+": ifade belirsiz",StringComparison.Ordinal));
            tile.Text=found is not null ? (found.Millimeters/10m).ToString("0.#########",System.Globalization.CultureInfo.InvariantCulture)+" cm" : ambiguous ? "Kontrol edin" : "Belirtilmedi";
            AutomationProperties.SetName(tile,key+": "+tile.Text);
        }
        UpdateMeasurementShortcut(review);
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
            if(question.Contains("çelişiyor",StringComparison.Ordinal))lines.Add("Çelişen önceki ifadeyi yukarıdaki tarif veya ölçü alanında düzeltin.");
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
        try { SetLastModel(null);prompt.Text=value.Prompt;measurements.Text=value.Measurements;reference=value.Reference is null ? null : value.Reference with { Data=(byte[])value.Reference.Data.Clone() };chatInput.Clear();UpdateReference();dirty=false;hasSavedRequest=true;RefreshConversation(); }
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
            var candidate=new DesignReference(Path.GetFileName(dialog.FileName),type,data);DesignRequestStorage.Validate(new DesignRequest("", "",candidate));reference=candidate;dirty=true;UpdateReference();SetStatus("Görsel isteğe eklendi; kaynak dosya değiştirilmedi.");
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException) { SetStatus("Görsel eklenemedi; önceki seçim korundu: "+ex.Message); }
    }
    private void Open()
    {
        if(!ConfirmDiscard())return;
        var dialog=new OpenFileDialog { Title="Kalıp isteği aç",Filter="Kalıp isteği (*.tcad-request.json)|*.tcad-request.json",Multiselect=false };
        if(dialog.ShowDialog(this)!=true)return;
        try { var loaded=DesignRequestStorage.Load(dialog.FileName);Apply(loaded);SetStatus("İstek açıldı; proje ve çizim değiştirilmedi."); }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.Text.DecoderFallbackException) { SetStatus("İstek açılamadı; mevcut alanlar korundu: "+ex.Message); }
    }
    private void Save()
    {
        if(!string.IsNullOrEmpty(chatInput.Text)) { SetStatus("Kaydetmeden önce mesajı gönderin veya silin; mevcut mesaj korunuyor.");return; }
        try { DesignRequestStorage.Validate(Capture()); }
        catch(InvalidDataException ex) { SetStatus(ex.Message);return; }
        var dialog=new SaveFileDialog { Title="Yeni kalıp isteği dosyası",Filter="Kalıp isteği (*.tcad-request.json)|*.tcad-request.json",FileName="kalip-istegi.tcad-request.json",AddExtension=false,OverwritePrompt=false,InitialDirectory=AppPaths.PrepareProjectsDirectory() };
        if(dialog.ShowDialog(this)!=true)return;
        try { DesignRequestStorage.SaveNew(dialog.FileName,Capture());dirty=false;hasSavedRequest=true;SetStatus("İstek yeni dosyaya kaydedildi; kalıp oluşturulmadı."); }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException) { SetStatus("İstek kaydedilemedi; alanlar korundu: "+ex.Message); }
    }
}
