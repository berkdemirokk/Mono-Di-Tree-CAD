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
    private long referenceRevision;
    private bool referenceLoadRunning,closed,referenceDecoded;
    internal Func<string,DesignReference>? ReferenceLoaderForVerification { get; set; }
    internal Func<byte[],ReferencePhotoPreview>? ExistingReferenceDecoderForVerification { get; set; }
    private bool dirty,loading,changingModel,hasSavedRequest;
    private int requestVersion=1;
    private string? portableModelId;
    internal bool CanPreviewALineForVerification => requestVersion==2 ? portableModelId=="aline" : selectedModelText=="A kesim elbise; detaylar: " && !modelAmbiguous;
    private static readonly string[] PortableModelIds=["wrap","aline","corset","sheath","straight","shirt"];
    private string? selectedModelText;private string modelSnapshot="";private int selectedModelStart;
    private bool modelAmbiguous,selectedModelFromLoaded;
    internal Func<string,bool>? ConfirmModelChangeForVerification { get; set; }
    internal Func<string,bool>? ConfirmV2ConversionForVerification { get; set; }
    private readonly List<(Button Button,TextBlock Caption,string Label,string Template)> modelCards=[];
    private readonly Dictionary<string,TextBlock> measurementTiles=new(StringComparer.Ordinal);
    internal TextBox PromptForVerification=>prompt;
    internal TextBox MeasurementsForVerification=>measurements;
    internal DesignRequest CaptureForVerification()=>Capture();
    internal void LoadForVerification(DesignRequest value)=>Apply(value);
    internal void OpenPathForVerification(string path)=>OpenPath(path);
    internal bool DirtyForVerification=>dirty;
    internal TextBox ReviewForVerification=>measurementReview;
    internal TextBox ChatInputForVerification=>chatInput;
    internal TextBox ConversationForVerification=>conversation;
    internal TextBlock StatusForVerification=>status;
    internal TextBlock ReferenceLabelForVerification=>referenceLabel;
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
        Action("İstek aç…",Open);Action("Yeni istek dosyası kaydet…",Save);Action("Çizim araçlarına geç",Close);
        var reviewAction=Action("Ölçüleri incele",()=> { ReviewMeasurements();measurementReview.BringIntoView();SetStatus(string.IsNullOrWhiteSpace(chatInput.Text) ? "Açık ölçüler incelendi; kalıp oluşturulmadı." : "Gönderilmemiş mesaj ölçü incelemesine dahil edilmedi; mesaj korundu."); });
        reviewAction.Background=Accent;reviewAction.Foreground=Surface;reviewAction.BorderBrush=Accent;reviewAction.FontWeight=FontWeights.SemiBold;AutomationProperties.SetHelpText(reviewAction,"Açıkça yazılmış ölçüleri inceler ve sonuca gider; kalıp oluşturmaz.");
        AutomationProperties.SetLiveSetting(status,AutomationLiveSetting.Polite);
        AutomationProperties.SetName(statusViewport,"İşlem durumu");
        statusViewport.Content=status;DockPanel.SetDock(statusViewport,Dock.Bottom);root.Children.Add(statusViewport);
        var header=new StackPanel { Margin=new Thickness(0,0,0,10) };
        header.Children.Add(new TextBlock { Text="MONO Dİ TREE CAD",FontFamily=new FontFamily("Georgia"),Foreground=Accent,FontSize=18,FontWeight=FontWeights.SemiBold });
        var headerSubtitle=new TextBlock { Text="Yeni Tasarım İsteği",Foreground=Muted,FontSize=11 };header.Children.Add(headerSubtitle);DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);
        var body=new StackPanel();bodyScroller.Content=body;root.Children.Add(bodyScroller);
        var badge=new Border { Background=ColorBrush("#FFD9DF"),CornerRadius=new CornerRadius(10),Padding=new Thickness(9,4,9,4),HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,0,0,8),Child=new TextBlock { Text="●  ATÖLYE PROJESİ · YENİ ÇALIŞMA",Foreground=Accent,FontSize=10,FontWeight=FontWeights.SemiBold } };body.Children.Add(badge);
        var modelHeading=new TextBlock { Text="Modelini seç",FontFamily=new FontFamily("Georgia"),Foreground=Accent,FontSize=27,FontWeight=FontWeights.Normal };body.Children.Add(modelHeading);
        var intro=new TextBlock { Text="Bir model fikri seçin veya doğrudan tarif edin. Bildiğiniz ölçülerle devam edebilirsiniz.",Foreground=Muted,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,3,0,12) };body.Children.Add(intro);
        var shortcuts=new WrapPanel { Margin=new Thickness(0,0,0,4) };body.Children.Add(shortcuts);
        var drawingNotes=new Button { Content="Kaydedilmiş notlarla çizime geç",HorizontalAlignment=HorizontalAlignment.Left,Padding=new Thickness(12,7,12,7),Margin=new Thickness(0,0,8,4) };
        AutomationProperties.SetHelpText(drawingNotes,"Kaydedilmiş isteğin salt okunur notlarını ana çizim penceresiyle birlikte açar. Kalıp oluşturmaz.");
        drawingNotes.Click+=(_,_)=>OpenDrawingNotes();shortcuts.Children.Add(drawingNotes);
        var chatShortcut=new Button { Content="Ölçülerle konuş",HorizontalAlignment=HorizontalAlignment.Left,Padding=new Thickness(12,7,12,7),Margin=new Thickness(0,0,0,4) };
        AutomationProperties.SetHelpText(chatShortcut,"Mevcut mesajı koruyarak ölçü yazışma alanına gider; otomatik kalıp oluşturmaz.");
        chatShortcut.Click+=(_,_)=> { chatInput.BringIntoView();chatInput.Focus(); };shortcuts.Children.Add(chatShortcut);
        var galleryHeading=new TextBlock { Text="01  ·  TEMEL SİLUET FİKİRLERİ",FontWeight=FontWeights.SemiBold,Foreground=Ink,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,6) };body.Children.Add(galleryHeading);
        var galleryHelp=new TextBlock { Text="Kartlar bir model fikri seçer; hazır kalıp değildir.",Foreground=Muted,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,6) };body.Children.Add(galleryHelp);
        var models=new StackPanel { Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,0,6) };modelScroller.Content=models;body.Children.Add(modelScroller);
        AutomationProperties.SetName(modelScroller,"Model fikirleri galerisi");AutomationProperties.SetHelpText(modelScroller,"Diğer model fikirlerini görmek için yatay kaydırın veya kartlar arasında klavyeyle ilerleyin.");
        var modelImages=new List<Viewbox>();var modelTitles=new List<TextBlock>();var modelDescriptions=new List<TextBlock>();
        root.SizeChanged+=(_,_)=>
        {
            var compact=root.ActualHeight<=520;var visible=compact ? Visibility.Collapsed : Visibility.Visible;
            badge.Visibility=intro.Visibility=galleryHeading.Visibility=galleryHelp.Visibility=headerSubtitle.Visibility=visible;
            header.Margin=compact ? new Thickness(0,0,0,4) : new Thickness(0,0,0,10);
            modelHeading.Text=compact ? "Model fikri seç" : "Modelini seç";modelHeading.FontSize=compact ? 20 : 27;
            foreach(var item in modelImages)item.Height=compact ? 70 : 150;
            foreach(var item in modelTitles)item.MinHeight=compact ? 20 : 34;
            foreach(var item in modelDescriptions)item.Visibility=visible;
        };
        void ModelChoice(string label,string description,string template,string silhouette,string? detail,string? code=null)
        {
            var art=new Canvas { Width=code is null ? 80 : 120,Height=code is null ? 80 : 160,IsHitTestVisible=false };
            var dress=new System.Windows.Shapes.Path { Data=Geometry.Parse(silhouette),Fill=Brushes.Transparent,Stroke=Accent,StrokeThickness=1.2 };
            art.Children.Add(dress);
            if(detail is not null)art.Children.Add(new System.Windows.Shapes.Path { Data=Geometry.Parse(detail),Stroke=Accent,StrokeThickness=1.2,StrokeLineJoin=PenLineJoin.Round });
            var image=new Viewbox { Width=112,Height=95,Child=art,IsHitTestVisible=false,Margin=new Thickness(0,4,0,4) };modelImages.Add(image);
            var content=new StackPanel { Width=166,IsHitTestVisible=false };
            content.Children.Add(new Border { Background=Raised,CornerRadius=new CornerRadius(6),Child=image,Margin=new Thickness(0,0,0,5) });
            if(code is not null)content.Children.Add(new TextBlock { Text="KOD: "+code,FontSize=10,Foreground=Muted,Margin=new Thickness(0,0,0,3) });
            var title=new TextBlock { Text=label,TextAlignment=TextAlignment.Center,FontFamily=new FontFamily("Georgia"),FontSize=14,Foreground=Accent,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap,MinHeight=34 };modelTitles.Add(title);content.Children.Add(title);
            var modelDescription=new TextBlock { Text=description,TextAlignment=TextAlignment.Center,FontSize=11,Foreground=Muted,TextWrapping=TextWrapping.Wrap,MinHeight=32 };modelDescriptions.Add(modelDescription);content.Children.Add(modelDescription);
            var caption=new TextBlock { Text="Seç",TextAlignment=TextAlignment.Center,FontSize=11,Foreground=Muted };content.Children.Add(caption);
            var button=new Button { Content=content,Background=Surface,BorderBrush=BorderColor,BorderThickness=new Thickness(2),Margin=new Thickness(0,0,8,8),Padding=new Thickness(6),HorizontalContentAlignment=HorizontalAlignment.Center,ToolTip="Model fikrini seç; hazır kalıp değildir." };
            AutomationProperties.SetName(button,label);
            AutomationProperties.SetHelpText(button,"Model fikrini seçer veya değiştirir; mevcut metin korunur ve kalıp oluşturulmaz.");
            button.Click+=(_,_)=>
            {
                if(requestVersion==2)
                {
                    var index=modelCards.FindIndex(card=>ReferenceEquals(card.Button,button));
                    if(index<0 || index>=PortableModelIds.Length)return;
                    portableModelId=PortableModelIds[index];dirty=true;SetLastModel(button);
                    SetStatus("Model kimliği seçildi; tarif korunuyor. Model ile tarifin uyumunu kontrol edin.");return;
                }
                if(selectedModelText==template)
                {
                    if(selectedModelFromLoaded)
                    {
                        selectedModelFromLoaded=false;dirty=true;SetLastModel(button);
                        SetStatus("Model seçimi onaylandı; mevcut tarif korundu.");
                    }
                    else SetStatus("Bu model zaten seçili; mevcut tarif korundu.");
                    return;
                }
                if(modelAmbiguous || selectedModelFromLoaded)
                {
                    var warning=modelAmbiguous ? "Önceki model metni elle değiştirilmiş veya belirsiz. Yeni model fikri mevcut metne eklenir; önceki metin korunur ve çelişebilir. Devam edilsin mi?" :
                        "Kaydedilmiş tarifte bulunan model metni yeni seçimle değiştirilecek; diğer metinler korunacak. Devam edilsin mi?";
                    var before=prompt.Text;var previousModel=selectedModelText;var previousStart=selectedModelStart;var wasAmbiguous=modelAmbiguous;var wasLoaded=selectedModelFromLoaded;
                    var confirmed=ConfirmModelChangeForVerification?.Invoke(warning) ?? (MessageBox.Show(this,warning,"Model fikri",MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No)==MessageBoxResult.Yes);
                    if(!confirmed || prompt.Text!=before || previousModel!=selectedModelText || previousStart!=selectedModelStart || wasAmbiguous!=modelAmbiguous || wasLoaded!=selectedModelFromLoaded)
                    { SetStatus("Model seçimi uygulanmadı; mevcut tarif korundu.");return; }
                }
                var replacing=selectedModelText is not null;
                var addition=(prompt.Text.Length==0 ? "" : "\n")+template;
                var newLength=replacing ? (long)prompt.Text.Length-selectedModelText!.Length+template.Length : (long)prompt.Text.Length+addition.Length+addition.Count(c=>c=='\n');
                if(newLength>prompt.MaxLength) { SetStatus("Model seçilemedi: açıklama uzunluk sınırına ulaşır. Mevcut metin korundu.");return; }
                changingModel=true;
                try
                {
                    if(replacing) { prompt.Select(selectedModelStart,selectedModelText!.Length);prompt.SelectedText=template; }
                    else { prompt.AppendText(addition);selectedModelStart=prompt.Text.Length-template.Length; }
                    selectedModelText=template;modelSnapshot=prompt.Text;modelAmbiguous=false;selectedModelFromLoaded=false;SetLastModel(button);
                }
                finally { changingModel=false; }
                prompt.CaretIndex=selectedModelStart+template.Length;prompt.Focus();SetStatus("Model fikri seçildi. Tarifi ve ölçüleri düzenleyebilirsiniz; kalıp oluşturulmadı.");
            };
            modelCards.Add((button,caption,label,template));models.Children.Add(button);
        }
        ModelChoice("Anvelop elbise","Akıcı drape · Kemerli bel","Anvelop elbise; detaylar: ","M48,20 C54,28 66,28 72,20 M48,20 L32,26 26,40 34,44 38,32 40,58 32,82 20,148 100,148 88,82 80,58 82,32 86,44 94,40 88,26 72,20","M48,20 Q56,46 76,68 M72,20 Q60,48 42,70 M38,72 Q60,76 82,72 M40,73 C42,98 32,125 24,146 M78,73 C72,100 84,130 96,146 M60,74 C58,95 62,124 60,148","K-01");
        ModelChoice("A kesim elbise","Geniş etek · Doğal döküm","A kesim elbise; detaylar: ","M46,22 Q60,28 74,22 M46,22 L34,26 30,38 38,41 41,32 44,56 42,70 14,148 106,148 78,70 76,56 79,32 82,41 90,38 86,26 74,22","M48,56 L48,70 M72,56 L72,70 M42,70 Q60,74 78,70 M32,95 Q60,102 88,95 M60,28 L60,148","A-04");
        ModelChoice("Balenli korpus","Yapısal gövde · Vurgulu bel","Balenli korpus; detaylar: ","M40,32 Q50,36 60,30 Q70,36 80,32 M40,32 L42,54 44,74 32,148 88,148 76,74 78,54 80,32","M50,34 L52,74 M70,34 L68,74 M60,30 L60,74 M44,74 Q60,80 76,74 M42,54 Q60,58 78,54 M60,74 L60,148","B-02");
        ModelChoice("Minimal kılıf","Düz form · Kolsuz hat","Minimal kılıf elbise; detaylar: ","M46,18 Q60,22 74,18 M46,18 L38,22 36,36 42,36 43,62 40,148 80,148 77,62 78,36 84,36 82,22 74,18","M50,48 L50,72 M70,48 L70,72 M60,20 L60,148 M40,140 L40,148","S-07");
        var lowerPanelPreview=new Button { Content="A kesim bel-altı kesitini incele…",Margin=new Thickness(0,6,0,6),Padding=new Thickness(8) };
        AutomationProperties.SetHelpText(lowerPanelPreview,"A kesim fikri için açık ön/arka ölçülerle deneysel kesit gösterir; tam elbise kalıbı, projeye ekleme veya çıktı oluşturmaz.");
        lowerPanelPreview.Click+=(_,_)=>
        {
            if(!CanPreviewALineForVerification)
            { SetStatus("Önce A kesim model fikrini açıkça seçin; mevcut tarif korunur.");return; }
            if(!string.IsNullOrWhiteSpace(chatInput.Text)) { SetStatus("Önce bekleyen mesajı gönderin; ölçü notları önizlemeye anlık kopyalanacak.");return; }
            new ALinePanelPreviewWindow(this,prompt.Text,measurements.Text).ShowDialog();
        };
        body.Children.Add(lowerPanelPreview);
        ModelChoice("Düz elbise","Düz siluet","Düz elbise; detaylar: ","M 34,5 L 25,10 20,22 27,26 29,39 25,73 55,73 51,39 53,26 60,22 55,10 46,5 Q 40,14 34,5 Z",null);
        ModelChoice("Gömlek elbise","Yakalı ön · Bel hattı","Gömlek elbise; detaylar: ","M 34,5 L 25,10 19,22 27,27 29,40 23,73 57,73 51,40 53,27 61,22 55,10 46,5 L 40,10 Z","M 34,5 L 34,15 40,10 46,15 46,5 M 40,10 L 40,73 M 30,40 L 50,40");
        var convertV2=new Button { Content="V2 model kimliğiyle çalış…",HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,0,0,8) };
        convertV2.Click+=(_,_)=>ConvertToV2();body.Children.Add(convertV2);
        var clearPortableModel=new Button { Content="V2 model seçimini kaldır",HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,0,0,8) };
        clearPortableModel.Click+=(_,_)=> { if(requestVersion==2 && portableModelId is not null) { portableModelId=null;dirty=true;SetLastModel(null);SetStatus("Model kimliği kaldırıldı; tarif korundu."); } };
        body.Children.Add(clearPortableModel);
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
        body.Children.Add(new TextBlock { Text="Sorulan ölçüyü yalnızca değer ve birimle yanıtlayabilirsiniz: 72 cm. Diğer ölçüleri açık etiket ve birimle yazın: bel çevresi 72 cm. Ölçüleri ayrı satır veya noktalı virgülle ayırabilir, açık etiketli ve birimli iki ölçüyü “ve” ile birleştirebilirsiniz. Bu çevrimdışı yardımcı genel konuşma veya görsel yorumlama yapmaz.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,8) });
        var notesCard=new StackPanel();
        notesCard.Children.Add(new TextBlock { Text="Tasarımcı Notları & Drapaj Detayları",FontFamily=new FontFamily("Georgia"),Foreground=Accent,FontSize=19,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,6) });
        notesCard.Children.Add(new Label { Foreground=Ink,Content="Tasarımcı notları",Target=prompt });AutomationProperties.SetName(prompt,"Kadın elbise modeli ve özellikleri");notesCard.Children.Add(prompt);
        var details=new WrapPanel { Margin=new Thickness(0,8,0,0) };notesCard.Children.Add(details);
        foreach(var detail in new[] { "Midi boy", "Balon kol", "Gizli pens", "V yaka", "Astar payı dahil" })
        {
            var chip=new Button { Content=detail,Padding=new Thickness(9,4,9,4),Margin=new Thickness(0,0,6,6),Background=Raised,Foreground=Accent,BorderBrush=BorderColor };
            AutomationProperties.SetName(chip,"Tarife ekle: "+detail);AutomationProperties.SetHelpText(chip,"Mevcut açıklamaya bu tasarım notunu ekler; kalıp oluşturmaz.");
            chip.Click+=(_,_)=> { if(prompt.Text.Split(['\r','\n'],StringSplitOptions.RemoveEmptyEntries).Any(line=>string.Equals(line.Trim(),detail,StringComparison.OrdinalIgnoreCase))) { SetStatus("Bu tasarım notu zaten var; mevcut metin korundu.");return; } var addition=(prompt.Text.Length==0 ? "" : "\n")+detail; if((long)prompt.Text.Length+addition.Length+addition.Count(c=>c=='\n')>prompt.MaxLength) { SetStatus("Not eklenemedi: açıklama uzunluk sınırına ulaşır. Mevcut metin korundu.");return; } prompt.AppendText(addition);prompt.Focus();SetStatus("Tasarım notu eklendi; kalıp oluşturulmadı."); };details.Children.Add(chip);
        }
        editBody.Children.Add(new Border { Child=notesCard,Background=Surface,BorderBrush=BorderColor,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(10),Padding=new Thickness(14),Margin=new Thickness(0,8,0,0) });
        var measurementHeading=new DockPanel { Margin=new Thickness(0,14,0,4) };
        var enterMeasurements=new Button { Content="Ölçüleri gir…",Padding=new Thickness(10,4,10,4),Margin=new Thickness(8,0,0,0) };
        AutomationProperties.SetHelpText(enterMeasurements,"Bilinen ölçüleri santimetre cinsinden ayrı alanlara girip mevcut notlara ekler; farklı değerler çelişki olarak gösterilir.");
        enterMeasurements.Click+=(_,_)=>new AtelierMeasurementEntryWindow(this).ShowDialog();DockPanel.SetDock(enterMeasurements,Dock.Right);measurementHeading.Children.Add(enterMeasurements);
        measurementHeading.Children.Add(new TextBlock { Text="Ölçülerini ekle",FontFamily=new FontFamily("Georgia"),FontSize=21,Foreground=Accent,VerticalAlignment=VerticalAlignment.Center });editBody.Children.Add(measurementHeading);
        editBody.Children.Add(new TextBlock { Text="Yalnız açıkça yazılan değerler gösterilir; beden veya eksik ölçü varsayılmaz.",Foreground=Muted,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,8) });
        var experimentalMeasurements=new Button { Content="Deneysel beden ölçüleri…",HorizontalAlignment=HorizontalAlignment.Left,Padding=new Thickness(10,5,10,5),Margin=new Thickness(0,0,0,8) };
        AutomationProperties.SetHelpText(experimentalMeasurements,"On beş deneysel ölçü alanını açar. Girilenler isteğin ölçü notlarına eklenebilir; beden önizlemesi veya kalıp oluşturulmaz.");
        experimentalMeasurements.Click+=(_,_)=>new ExperimentalBodyMeasurementsWindow(this).ShowDialog();editBody.Children.Add(experimentalMeasurements);
        var measurementOverview=new StackPanel();
        measurementOverview.Children.Add(new TextBlock { Text="İSTEK ÖLÇÜLERİ  ·  Metrik (cm)",Foreground=Accent,FontSize=11,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,8) });
        var measurementCards=new WrapPanel();measurementOverview.Children.Add(measurementCards);
        editBody.Children.Add(new Border { Child=measurementOverview,Background=Surface,BorderBrush=BorderColor,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(10),Padding=new Thickness(14,12,6,4),Margin=new Thickness(0,0,0,8) });
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
        var remove=new Button { Content="Görseli kaldır",Padding=new Thickness(10,5,10,5),Margin=new Thickness(0,4,0,4) };refActions.Children.Add(remove);remove.Click+=(_,_)=>RemoveReference();
        var verifyAndSave=new Button { Content="Görseli denetleyip yeni istek kaydet…",Padding=new Thickness(10,5,10,5),Margin=new Thickness(8,4,0,4) };
        AutomationProperties.SetHelpText(verifyAndSave,"Kayıtlı referansı Windows'ta açılabilirlik için denetler; başarılıysa yeni dosya kaydetme penceresini açar. Telefon uyumluluğunu garanti etmez.");
        verifyAndSave.Click+=async (_,_)=>await ValidateAndSaveAsync();refActions.Children.Add(verifyAndSave);
        body.Children.Add(new TextBlock { Text="PNG/JPEG görsel seçilirken çizim için çözümlenebilirliği kontrol edilir; en fazla 8 MiB, 12.000 piksel ve 24 milyon piksel desteklenir. Bu sınırları aşan görsel otomatik küçültülmez. Tüm kaynak baytları ve metadata isteğe eklenir; görsel içeriği, ölçüler ve üretime uygunluk doğrulanmaz. Eski isteklerdeki görseller açılmadan doğrulanmamış olabilir.",TextWrapping=TextWrapping.Wrap,Foreground=Muted,Margin=new Thickness(0,10,0,0) });

        prompt.TextChanged+=(_,_)=> { measurementReview.Clear();if(!changingModel)UpdateModelTracking();if(!loading) { dirty=true;RefreshConversation(); } };measurements.TextChanged+=(_,_)=> { measurementReview.Clear();if(!loading) { dirty=true;RefreshConversation(); } };UpdateReference();RefreshConversation();Closing+=OnClosing;Closed+=(_,_)=>closed=true;
    }
    private void SetStatus(string message)
    {
        status.Text=message;
        statusViewport.Visibility=string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
        statusViewport.ScrollToTop();
        if(UIElementAutomationPeer.FromElement(status) is { } peer)
            peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
    internal bool TryAppendMeasurementLines(string expectedPrompt,string expectedMeasurements,IReadOnlyList<string> lines,out string error)
    {
        error="";
        if(prompt.Text!=expectedPrompt || measurements.Text!=expectedMeasurements) { error="İstek değişti; pencereyi kapatıp yeniden açın.";return false; }
        if(lines.Count==0) { error="Eklemek için en az bir ölçü girin.";return false; }
        var addition=(measurements.Text.Length==0 ? "" : "\n")+string.Join("\n",lines);
        if((long)measurements.Text.Length+addition.Length+addition.Count(c=>c=='\n')>measurements.MaxLength)
        { error="Ölçü notları sınırına ulaşıldı; mevcut notlar ve girilen değerler korundu.";return false; }
        measurements.AppendText(addition);SetStatus("Ölçüler notlara eklendi. Çelişkileri ve ölçülerin doğruluğunu kontrol edin.");return true;
    }
    internal RequestNotesWindow? CreateDrawingNotes()
    {
        if(referenceLoadRunning) { SetStatus("Görsel denetimi sürüyor; çizime geçmeden önce tamamlanmasını bekleyin.");return null; }
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
    private void ClearModelTracking() { selectedModelText=null;modelSnapshot="";selectedModelStart=0;modelAmbiguous=false;selectedModelFromLoaded=false;SetLastModel(null); }
    private void ConvertToV2()
    {
        if(requestVersion==2) { SetStatus("İstek zaten V2 biçiminde.");return; }
        if(referenceLoadRunning) { SetStatus("Önce görsel denetimini tamamlayın.");return; }
        var before=Capture();var chat=chatInput.Text;var revision=referenceRevision;var oldDirty=dirty;
        var selected=selectedModelText;var fromLoaded=selectedModelFromLoaded;var ambiguous=modelAmbiguous;var start=selectedModelStart;var snapshot=modelSnapshot;
        var message="V2, model kimliğini ayrı saklar. Tarif, ölçüler ve görsel değiştirilmez. Önceki V1 model satırı tarifte kalabilir; kimlik ile tarifin uyumunu kontrol edin. Eski sürümler V2 dosyasını açamayabilir. Devam edilsin mi?";
        var confirmed=ConfirmV2ConversionForVerification?.Invoke(message) ?? (MessageBox.Show(this,message,"V2 istek",MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No)==MessageBoxResult.Yes);
        if(!confirmed)return;
        if(closed || referenceLoadRunning || revision!=referenceRevision || oldDirty!=dirty || chat!=chatInput.Text || !SameRequest(before,Capture()) || selected!=selectedModelText || fromLoaded!=selectedModelFromLoaded || ambiguous!=modelAmbiguous || start!=selectedModelStart || snapshot!=modelSnapshot)
        { SetStatus("İstek değişti; V2 dönüşümü uygulanmadı.");return; }
        // Only an explicit current-session choice is carried forward. A V1 text
        // match loaded from disk is a suggestion, not a portable identity.
        var index=!fromLoaded && !ambiguous && selected is not null ? modelCards.FindIndex(card=>card.Template==selected) : -1;
        var id=index>=0 && index<PortableModelIds.Length ? PortableModelIds[index] : null;
        ClearModelTracking();requestVersion=2;portableModelId=id;dirty=true;
        if(index>=0 && id is not null)SetLastModel(modelCards[index].Button);
        SetStatus(id is null ? "V2 seçildi; isterseniz model kartı seçin. Tarif ve görsel korundu." : "V2 seçildi; model kimliği ve özgün tarif korundu. İkisinin uyumunu kontrol edin.");
    }
    private void UpdateModelTracking()
    {
        if(selectedModelText is null)return;
        var current=prompt.Text;var before=modelSnapshot;var prefix=0;
        while(prefix<before.Length && prefix<current.Length && before[prefix]==current[prefix])prefix++;
        var suffix=0;while(suffix<before.Length-prefix && suffix<current.Length-prefix && before[before.Length-1-suffix]==current[current.Length-1-suffix])suffix++;
        var oldEnd=before.Length-suffix;
        if(oldEnd<=selectedModelStart)selectedModelStart+=current.Length-before.Length;
        else if(prefix<selectedModelStart+selectedModelText.Length) { ClearModelTracking();modelAmbiguous=true;return; }
        if(selectedModelStart<0 || selectedModelStart+selectedModelText.Length>current.Length ||
            !current.AsSpan(selectedModelStart,selectedModelText.Length).SequenceEqual(selectedModelText.AsSpan())) { ClearModelTracking();modelAmbiguous=true;return; }
        modelSnapshot=current;
    }
    private void RecoverLoadedModelCandidate()
    {
        var matches=new List<(Button Button,string Template,int Start)>();
        foreach(var (button,_,_,template) in modelCards)
        {
            var at=0;while((at=prompt.Text.IndexOf(template,at,StringComparison.Ordinal))>=0)
            { matches.Add((button,template,at));at+=template.Length; }
        }
        if(matches.Count==1)
        {
            var match=matches[0];selectedModelText=match.Template;selectedModelStart=match.Start;modelSnapshot=prompt.Text;
            selectedModelFromLoaded=true;SetLastModel(match.Button);
        }
        else if(matches.Count>1 || modelCards.Any(card=>prompt.Text.Contains(card.Label,StringComparison.OrdinalIgnoreCase)))modelAmbiguous=true;
    }
    private void SetLastModel(Button? selected)
    {
        foreach(var (button,caption,_,_) in modelCards)
        {
            var active=ReferenceEquals(button,selected);
            button.Background=active ? Raised : Surface;
            button.BorderBrush=active ? Accent : BorderColor;
            caption.Text=active ? (selectedModelFromLoaded ? "Tarifte bulundu" : "Seçili") : "Seç";
            caption.Foreground=active ? Accent : Muted;AutomationProperties.SetItemStatus(button,active ? (selectedModelFromLoaded ? "Tarifte bulundu" : "Seçili") : "Seçili değil");
            AutomationProperties.SetHelpText(button,(active ? (selectedModelFromLoaded ? "Tarifte bulunan model metni; değiştirme için onay gerekir. " : "Seçili model fikri. ") : "")+"Model fikrini seçer veya değiştirir; mevcut metin korunur ve kalıp oluşturulmaz.");
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
        prompt.AppendText(addition);
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
    private void UpdateReference()=>referenceLabel.Text=reference is null ? "Görsel seçilmedi." : $"{reference.Name} · {reference.MediaType} · {reference.Data.Length} bayt"+
        (referenceDecoded ? "\nWindows görsel denetiminden geçti; telefon uyumluluğu ayrıca doğrulanmalıdır." : "\nBu kayıttaki görsel açılabilirlik açısından yeniden denetlenmedi; telefonda açılamayabilir. Özgün istek dosyasını saklayın; gerekirse yeni bir PNG/JPEG görsel seçip isteği ayrı bir dosyaya kaydedin. Mevcut görsel baytları değiştirilmedi.");
    private DesignRequest Capture()=>new(prompt.Text,measurements.Text,reference is null ? null : reference with { Data=(byte[])reference.Data.Clone() },portableModelId,requestVersion);
    private void Apply(DesignRequest value)
    {
        DesignRequestStorage.Validate(value);loading=true;
        try { ClearModelTracking();requestVersion=value.Version;portableModelId=value.ModelId;prompt.Text=value.Prompt;measurements.Text=value.Measurements;reference=value.Reference is null ? null : value.Reference with { Data=(byte[])value.Reference.Data.Clone() };referenceDecoded=value.Version==2 && value.Reference is not null;referenceRevision++;chatInput.Clear();UpdateReference();dirty=false;hasSavedRequest=true;if(value.Version==1)RecoverLoadedModelCandidate();else if(value.ModelId is not null) { var index=Array.IndexOf(PortableModelIds,value.ModelId);if(index>=0)SetLastModel(modelCards[index].Button); }RefreshConversation(); }
        finally { loading=false; }
    }
    private bool ConfirmDiscard()=>!dirty && string.IsNullOrEmpty(chatInput.Text) || MessageBox.Show(this,"Kaydedilmemiş istek değişiklikleri kaybolacak. Devam edilsin mi?","İstek",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)==MessageBoxResult.Yes;
    private void OnClosing(object? sender,CancelEventArgs e) { if(!ConfirmDiscard())e.Cancel=true; }
    private void RemoveReference()
    {
        referenceRevision++;
        if(reference is not null) { reference=null;referenceDecoded=false;dirty=true;UpdateReference(); }
        if(referenceLoadRunning)SetStatus("Bekleyen görsel seçimi iptal edildi; mevcut referans kaldırıldı.");
    }
    private void ChooseReference()
    {
        if(referenceLoadRunning) { SetStatus("Görsel denetimi sürüyor; tamamlanmasını bekleyin.");return; }
        var dialog=new OpenFileDialog { Title="Referans görseli seç",Filter="PNG veya JPEG (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg",Multiselect=false };
        if(dialog.ShowDialog(this)!=true)return;
        _=ChooseReferenceFileAsync(dialog.FileName);
    }
    internal async Task<bool> ValidateExistingReferenceAsync()
    {
        if(closed || referenceLoadRunning) { if(!closed)SetStatus("Görsel denetimi sürüyor; tamamlanmasını bekleyin.");return false; }
        if(reference is null) { SetStatus("İstekte görsel yok; görsel denetimi gerekmedi.");return true; }
        var revision=referenceRevision;var bytes=(byte[])reference.Data.Clone();var decoder=ExistingReferenceDecoderForVerification;
        referenceLoadRunning=true;SetStatus("Kayıtlı görsel Windows'ta denetleniyor; özgün baytlar korunuyor.");
        try
        {
            _=await Task.Run(()=>decoder is null ? ReferencePhotoDecoder.DecodeWithGeometry(bytes) : decoder(bytes));
            if(closed)return false;
            if(referenceRevision!=revision) { SetStatus("Referans değişti; önceki denetim kullanılmadı.");return false; }
            referenceDecoded=true;UpdateReference();SetStatus("Görsel Windows'ta açıldı; telefon uyumluluğu ayrıca doğrulanmalıdır. Özgün baytlar değişmedi.");return true;
        }
        catch(Exception ex) when(ex is InvalidDataException or IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            if(!closed)SetStatus(referenceRevision!=revision ? "Referans değişti; önceki denetim kullanılmadı." : "Kayıtlı görsel çözümlenemedi veya desteklenen 8 MiB / 12.000 piksel / 24 milyon piksel sınırını aşıyor. İstek ve görsel korunuyor; özgün dosyayı saklayın ve gerekirse yeni bir PNG/JPEG seçin.");return false;
        }
        finally { referenceLoadRunning=false; }
    }
    private async Task ValidateAndSaveAsync()
    {
        var description=prompt.Text;var notes=measurements.Text;var message=chatInput.Text;var revision=referenceRevision;
        if(!await ValidateExistingReferenceAsync())return;
        if(closed)return;
        if(prompt.Text!=description || measurements.Text!=notes || chatInput.Text!=message || referenceRevision!=revision)
        { SetStatus("İstek denetim sırasında değişti; kaydetme açılmadı. Tekrar deneyin.");return; }
        Save();
    }
    internal async Task<bool> ChooseReferenceFileAsync(string path)
    {
        if(closed || referenceLoadRunning)return false;
        var revision=referenceRevision;referenceLoadRunning=true;SetStatus("Görsel çizim için denetleniyor; önceki seçim korunuyor.");
        try
        {
            var loader=ReferenceLoaderForVerification;
            var candidate=await Task.Run(()=>loader is null ? ReferencePhotoFileLoader.LoadRequestReference(path) : loader(path));
            if(closed)return false;
            if(referenceRevision!=revision) { SetStatus("Referans değişti; bekleyen görsel eklenmedi.");return false; }
            reference=candidate;referenceDecoded=loader is null;referenceRevision++;dirty=true;UpdateReference();SetStatus("Görsel çizim için açılabilir; orijinal baytlar isteğe eklendi ve kaynak dosya değiştirilmedi.");return true;
        }
        catch(Exception ex) when(ex is InvalidDataException or IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        { if(!closed)SetStatus(referenceRevision!=revision ? "Referans değişti; bekleyen görsel eklenmedi." : "Görsel açılamadı. PNG/JPEG için 8 MiB, 12.000 piksel ve 24 milyon piksel sınırlarını kontrol edin; bu sınırları aşan görsel otomatik küçültülmez. Önceki görsel korundu.");return false; }
        finally { referenceLoadRunning=false; }
    }
    private void Open()
    {
        if(!ConfirmDiscard())return;
        var before=Capture();var beforeChat=chatInput.Text;var beforeRevision=referenceRevision;var beforeDirty=dirty;
        var dialog=new OpenFileDialog { Title="Kalıp isteği aç",Filter="Kalıp isteği (*.tcad-request.json)|*.tcad-request.json",Multiselect=false };
        if(dialog.ShowDialog(this)!=true)return;
        if(!SameRequest(before,Capture()) || beforeChat!=chatInput.Text || beforeRevision!=referenceRevision || beforeDirty!=dirty || closed) { SetStatus("İstek değişti; açma işlemi iptal edildi.");return; }
        try { OpenPath(dialog.FileName); }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.Text.DecoderFallbackException) { SetStatus("İstek açılamadı; mevcut alanlar korundu: "+ex.Message); }
    }
    private void OpenPath(string path)
    {
        var loaded=DesignRequestStorage.Load(path,ReferencePhotoDecoder.ValidateV2);Apply(loaded);
        SetStatus("İstek açıldı; proje ve çizim değiştirilmedi."+(loaded.Version==2 && loaded.ModelId is not null ? " Model kimliği ile serbest tarifin uyumunu kontrol edin." : ""));
    }
    private void Save()
    {
        if(referenceLoadRunning) { SetStatus("Görsel denetimi sürüyor; kaydetmeden önce tamamlanmasını bekleyin.");return; }
        if(!string.IsNullOrEmpty(chatInput.Text)) { SetStatus("Kaydetmeden önce mesajı gönderin veya silin; mevcut mesaj korunuyor.");return; }
        try { DesignRequestStorage.Validate(Capture()); }
        catch(InvalidDataException ex) { SetStatus(ex.Message);return; }
        var before=Capture();var beforeChat=chatInput.Text;var beforeRevision=referenceRevision;
        var dialog=new SaveFileDialog { Title="Yeni kalıp isteği dosyası",Filter="Kalıp isteği (*.tcad-request.json)|*.tcad-request.json",FileName="kalip-istegi.tcad-request.json",AddExtension=false,OverwritePrompt=false,InitialDirectory=AppPaths.PrepareProjectsDirectory() };
        if(dialog.ShowDialog(this)!=true)return;
        if(!SameRequest(before,Capture()) || beforeChat!=chatInput.Text || beforeRevision!=referenceRevision || closed) { SetStatus("İstek değişti; kaydetme iptal edildi.");return; }
        try { if(before.Version==2)DesignRequestStorage.SaveNewV2(dialog.FileName,before,ReferencePhotoDecoder.ValidateV2);else DesignRequestStorage.SaveNew(dialog.FileName,before);dirty=false;hasSavedRequest=true;SetStatus("İstek yeni dosyaya kaydedildi; kalıp oluşturulmadı."+(reference is not null && !referenceDecoded ? " Kayıttan gelen görsel yeniden denetlenmedi; telefonda açılamayabilir. Görsel bölümündeki açıklamaya bakın." : "")); }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException) { SetStatus("İstek kaydedilemedi; alanlar korundu: "+ex.Message); }
    }
    private static bool SameRequest(DesignRequest a,DesignRequest b)=>a.Prompt==b.Prompt && a.Measurements==b.Measurements && a.ModelId==b.ModelId && a.Version==b.Version &&
        (a.Reference is null ? b.Reference is null : b.Reference is not null && a.Reference.Name==b.Reference.Name && a.Reference.MediaType==b.Reference.MediaType && a.Reference.Data.AsSpan().SequenceEqual(b.Reference.Data));
}

