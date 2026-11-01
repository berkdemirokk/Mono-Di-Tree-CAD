using System.Numerics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;
using Geometry = TekstilCAD.Core.Geometry;

namespace TekstilCAD.Desktop;

/// <summary>Create a detached, explicitly reviewed mirrored counterpart without replacing its source.</summary>
internal sealed class MirrorCopyWindow : Window
{
    private readonly MainWindow workspace;
    internal TextBox Gap { get; } = new() { Text="10",Margin=new Thickness(0,4,0,8) };
    internal ComboBox Side { get; } = new() { ItemsSource=new[] { "Sağına", "Soluna" },SelectedIndex=0,Margin=new Thickness(0,4,0,8) };
    internal CheckBox CopyRule { get; } = new() { Content="Eşleşen kayıtlı serileme kuralını da aynala",Margin=new Thickness(0,4,0,4) };
    internal CheckBox CopyInfo { get; } = new() { Content="Rol, beden ve malzemeyi kopyala (adet kopyalanmaz)",Margin=new Thickness(0,4,0,4) };
    internal Button ApplyButton { get; } = new() { Content="Ayna parçayı ekle",Padding=new Thickness(12,7,12,7),IsEnabled=false,Margin=new Thickness(0,0,8,0) };
    internal TextBlock Status { get; } = new() { TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0) };
    private readonly BulkGradePreview preview=new();
    private CancellationTokenSource? cancellation;
    private MainWindow.GrainlineEditStamp? stamp;
    private PatternPiece? ready;
    private GradingRuleSet? readyRule;
    private bool closed;
    internal Action? BeforeCalculationForVerification { get; set; }
    internal Task CalculateForVerification()=>CalculateAsync();
    internal void ApplyForVerification()=>Apply();
    internal void CancelForVerification()=>cancellation?.Cancel();

    internal MirrorCopyWindow(MainWindow workspace)
    {
        this.workspace=workspace;Owner=workspace;Title="Yanına aynalı parça oluştur";Width=650;Height=640;MinWidth=500;MinHeight=420;WindowStartupLocation=WindowStartupLocation.CenterOwner;StudioTheme.Apply(this);
        var root=new DockPanel { Margin=new Thickness(16) };Content=root;
        var actions=new WrapPanel { Margin=new Thickness(0,8,0,0) };DockPanel.SetDock(actions,Dock.Bottom);root.Children.Add(actions);
        var calculate=new Button { Content="Önizle",Padding=new Thickness(12,7,12,7),Margin=new Thickness(0,0,8,0) };
        var cancel=new Button { Content="Kapat / iptal et",Padding=new Thickness(12,7,12,7) };actions.Children.Add(calculate);actions.Children.Add(ApplyButton);actions.Children.Add(cancel);
        var body=new StackPanel();root.Children.Add(new ScrollViewer { Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Focusable=true });
        body.Children.Add(new TextBlock { Text="Yanına aynalı parça oluştur",FontSize=20,FontWeight=FontWeights.SemiBold });
        body.Children.Add(new TextBlock { Text="Kaynak korunur; yeni parça seçilen yanda, iki parçanın sınır kutuları arasında girilen boşlukla oluşturulur. Aynalama kontur yönünü ters çevirir. Bu işlem yerleşim veya üretim uygunluğu onayı değildir.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,8) });
        body.Children.Add(new TextBlock { Text="Konum" });AutomationProperties.SetName(Side,"Aynalı parçanın kaynak karşısındaki tarafı");body.Children.Add(Side);
        body.Children.Add(new TextBlock { Text="Sınır kutuları arası boşluk (mm)" });AutomationProperties.SetName(Gap,"Aynalı parça ile kaynak arasındaki boşluk milimetre");body.Children.Add(Gap);
        body.Children.Add(CopyRule);body.Children.Add(CopyInfo);
        body.Children.Add(new TextBlock { Text="Görsel köşe işaretleri ve varsa iplik yönü aynalanır; iplik yönü yeniden kontrol bekler. İşaretler fiziksel çentik değildir. Adet yeni parçada ayrıca belirlenmelidir. Serileme yalnız açıkça seçildiğinde ve kaynak/dönüşüm tam doğrulandığında aktarılır.",TextWrapping=TextWrapping.Wrap,Foreground=StudioTheme.Warning,Margin=new Thickness(0,8,0,0) });
        body.Children.Add(preview);AutomationProperties.SetName(Status,"Aynalı parça durumu");AutomationProperties.SetLiveSetting(Status,AutomationLiveSetting.Polite);body.Children.Add(Status);
        Gap.TextChanged+=(_,_)=>Invalidate();Side.SelectionChanged+=(_,_)=>Invalidate();CopyRule.Checked+=(_,_)=>Invalidate();CopyRule.Unchecked+=(_,_)=>Invalidate();CopyInfo.Checked+=(_,_)=>Invalidate();CopyInfo.Unchecked+=(_,_)=>Invalidate();
        calculate.Click+=async (_,_)=>await CalculateAsync();ApplyButton.Click+=(_,_)=>Apply();cancel.Click+=(_,_)=> { if(cancellation is not null) { cancellation.Cancel();SetStatus("İptal istendi; hesap durunca sonuç kullanılmayacak."); } else Close(); };
        Closing+=(_,e)=> { if(cancellation is not null) { e.Cancel=true;cancellation.Cancel();SetStatus("İptal istendi; hesap durunca sonuç kullanılmayacak."); } };
        Closed+=(_,_)=> { closed=true;cancellation?.Cancel(); };
    }
    private void SetStatus(string text)
    {
        Status.Text=text;AutomationProperties.SetName(Status,text);
        if(UIElementAutomationPeer.CreatePeerForElement(Status) is { } peer)peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
    private void Invalidate() { cancellation?.Cancel();stamp=null;ready=null;readyRule=null;ApplyButton.IsEnabled=false;preview.Clear(); }
    // Exact dyadic arithmetic detects absolute position or gap loss that
    // double subtraction can round away. This is a numeric bound, not a
    // manufacturing tolerance.
    private static BigInteger Units(double value)
    {
        var bits=BitConverter.DoubleToInt64Bits(value);var exponent=(int)((bits>>52)&0x7ff);
        var mantissa=(ulong)bits & 0x000f_ffff_ffff_ffffUL;
        if(exponent!=0)mantissa|=1UL<<52;
        var result=new BigInteger(mantissa) << (exponent==0 ? 0 : exponent-1);
        return bits<0 ? -result : result;
    }
    private static bool CloseEnough(BigInteger expected,BigInteger actual) =>
        BigInteger.Abs(actual-expected)*1_000_000_000<=BigInteger.Abs(expected);
    private static void CheckMirrorPoint(Point2 source,Point2 target,double axis)
    {
        if(!CloseEnough(2*Units(axis)-Units(source.X),Units(target.X)) ||
           BitConverter.DoubleToInt64Bits(source.Y)!=BitConverter.DoubleToInt64Bits(target.Y))
            throw new InvalidOperationException("Ayna noktasının konumu sayısal hassasiyetle korunamıyor.");
    }
    private async Task CalculateAsync()
    {
        if(closed || cancellation is not null)return;Invalidate();
        var captured=workspace.CaptureGrainlineEdit();if(captured is null) { SetStatus("Kaynak veya bekleyen çalışma nedeniyle işlem başlatılamadı.");return; }
        if(!MeasurementInput.TryParseMillimeters(Gap.Text,200_000_000,out var gap) || gap<0 || Side.SelectedIndex is not (0 or 1))
        { SetStatus("Boşluk için sıfır veya pozitif geçerli milimetre değeri ve bir taraf seçin.");return; }
        var source=captured.Snapshot.Pieces.Single(p=>p.Id==captured.PieceId);var saved=captured.Snapshot.GradingRules.FirstOrDefault(r=>r.SourcePieceId==source.Id);
        var carry=CopyRule.IsChecked==true;var copyInfo=CopyInfo.IsChecked==true;var side=Side.SelectedIndex;
        if(carry && saved is null) { SetStatus("Kaynakta kayıtlı serileme kuralı yok; kural seçeneğini kaldırın.");return; }
        using var cts=new CancellationTokenSource();cancellation=cts;SetStatus("Ayna parça hesaplanıyor; proje henüz değişmedi. İptal istenirse sonuç uygulanmaz.");
        try
        {
            var hook=BeforeCalculationForVerification;
            var calculated=await Task.Run(()=>
            {
                hook?.Invoke();var token=cts.Token;token.ThrowIfCancellationRequested();
                if(source.Notches is not null)_=MarkedPieceTranslation.Translate(source,0,0,token);
                var bounds=Geometry.Bounds(source.Points);var axis=side==0 ? bounds.MaxX+gap/2 : bounds.MinX-gap/2;
                List<Point2> Transform(IReadOnlyList<Point2> points)=>Geometry.Mirror(points,true,axis);
                var mirrored=Transform(source.Points);token.ThrowIfCancellationRequested();var resultBounds=Geometry.Bounds(mirrored);
                var exactGap=side==0 ? Units(resultBounds.MinX)-Units(bounds.MaxX) : Units(bounds.MinX)-Units(resultBounds.MaxX);
                if(exactGap.Sign<0 || !CloseEnough(Units(gap),exactGap))
                    throw new InvalidOperationException("İstenen boşluk sayısal hassasiyetle korunamıyor; daha küçük koordinat veya farklı boşluk deneyin.");
                for(var i=0;i<mirrored.Count;i++) { if((i & 255)==0)token.ThrowIfCancellationRequested();CheckMirrorPoint(source.Points[i],mirrored[i],axis); }
                var suffix=" · ayna";var length=Math.Min(source.Name.Length,1024-suffix.Length);
                if(length<source.Name.Length && length>0 && char.IsHighSurrogate(source.Name[length-1]))length--;
                var copy=new PatternPiece { Name=source.Name[..length]+suffix,Closed=source.Closed,Points=mirrored };
                if(source.Notches is not null)copy.Notches=source.Notches.Select(mark=>new NotchMark { VertexIndex=mark.VertexIndex,Position=new Point2(mirrored[mark.VertexIndex].X,mark.Position.Y) }).ToList();
                copy.Grainline=GrainlineTransform.Transform(source.Grainline,source.Points[0],mirrored[0],Transform);
                if(copy.Grainline is not null)
                {
                    CheckMirrorPoint(source.Grainline!.Start,copy.Grainline.Start,axis);CheckMirrorPoint(source.Grainline.End,copy.Grainline.End,axis);
                    copy.Grainline.NeedsReview=true;
                }
                if(copyInfo && source.Metadata is { } info && (info.Role is not null || info.Size is not null || info.Material is not null))
                    copy.Metadata=new PieceMetadata { Role=info.Role,Size=info.Size,Material=info.Material };
                GradingRuleSet? mapped=null;
                if(carry)
                {
                    static bool Bits(Point2 a,Point2 b)=>BitConverter.DoubleToInt64Bits(a.X)==BitConverter.DoubleToInt64Bits(b.X) && BitConverter.DoubleToInt64Bits(a.Y)==BitConverter.DoubleToInt64Bits(b.Y);
                    if(saved!.SourcePoints.Count!=source.Points.Count || saved.Deltas.Count!=source.Points.Count || !saved.SourcePoints.Zip(source.Points).All(pair=>Bits(pair.First,pair.Second)) ||
                       (source.Closed && source.Points[0]==source.Points[^1] && (!Bits(source.Points[0],source.Points[^1]) || !Bits(saved.Deltas[0],saved.Deltas[^1]))))
                        throw new InvalidOperationException("Kayıtlı serileme kuralı kaynakla veya yinelenen kapanış artışıyla bitçe eşleşmiyor; ayna kuralı oluşturulmadı.");
                    mapped=saved.OrthogonalFor(source,mirrored,OrthogonalGradingTransform.MirrorVertical);
                    if(mapped is null)throw new InvalidOperationException("Serileme kuralının tam aynalanması kanıtlanamadı; kopya oluşturulmadı.");
                    mapped.SourcePieceId=copy.Id;
                }
                token.ThrowIfCancellationRequested();var candidate=captured.Snapshot.Clone();candidate.Pieces.Add(copy.Clone());if(mapped is not null)candidate.GradingRules.Add(mapped.Clone());
                ProjectStorage.Validate(candidate,token);token.ThrowIfCancellationRequested();return (copy,mapped);
            },cts.Token);
            cts.Token.ThrowIfCancellationRequested();
            if(closed || workspace.GrainlineEditProblem(captured) is not null) { SetStatus("Proje veya seçim değişti; önizleme uygulanmadı.");return; }
            stamp=captured;ready=calculated.copy;readyRule=calculated.mapped;preview.SetPieces(source,ready,"Kaynak ve aynalı parça");
            AutomationProperties.SetHelpText(preview,"Kaynak ve aynalı parça aynı ölçekte gösterilir; ekran çizimi fiziksel ölçüm değildir.");
            ApplyButton.IsEnabled=true;SetStatus("Önizleme hazır. Kaynak sarı, ayna turkuaz; konumları ve iplik yönünü kontrol edin."+(preview.MarksSuppressed ? " 500'den fazla işaret çizilmez; kaynakla karşılaştırın." : ""));
        }
        catch(OperationCanceledException) when(cts.IsCancellationRequested) { if(!closed)SetStatus("Hesap iptal edildi; proje değişmedi."); }
        catch(Exception ex) when(ex is not (OutOfMemoryException or StackOverflowException or AccessViolationException)) { if(!closed)SetStatus("Önizleme hazırlanamadı: "+ex.Message); }
        finally { if(ReferenceEquals(cancellation,cts))cancellation=null; }
    }
    private void Apply()
    {
        if(cancellation is not null || stamp is null || ready is null || workspace.GrainlineEditProblem(stamp) is not null)
        { Invalidate();SetStatus("Proje, seçim veya girdiler değişti; yeniden önizleyin.");return; }
        var piece=ready.Clone();var rule=readyRule?.Clone();var candidate=workspace.Document.Clone();candidate.Pieces.Add(piece);if(rule is not null)candidate.GradingRules.Add(rule);
        if(!workspace.CanRetainCurrentUndoSnapshot || !workspace.CanRetainUndoSnapshot(candidate))
        { SetStatus("Kaynak veya sonuç geri alma/yineleme geçmişine sığmıyor; parça eklenmedi.");return; }
        var committed=false;ApplyButton.IsEnabled=false;
        try
        {
            workspace.CommitObserved("Aynalı parça oluşturuldu",()=> { workspace.Document.Pieces.Add(piece);if(rule is not null)workspace.Document.GradingRules.Add(rule); },()=>committed=true);
            stamp=null;ready=null;readyRule=null;workspace.Select(piece.Id);workspace.FitView();
            SetStatus("Aynalı parça eklendi; kaynak korundu. Tek geri alma işlemi parçayı ve varsa kuralını kaldırır. Ölçüleri kontrol edin.");
        }
        catch(Exception ex) when(ex is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
        { stamp=null;ready=null;readyRule=null;SetStatus(committed ? "Parça eklendi ancak görünüm yenilenemedi; çalışma alanını kontrol edin: "+ex.Message : "Parça eklenmedi: "+ex.Message); }
    }
}
