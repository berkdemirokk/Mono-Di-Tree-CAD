using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;
using TekstilCAD.Patterns;

namespace TekstilCAD.Desktop;

/// <summary>Isolated snapshot. No CAD document, persistence, printer, or export reference.</summary>
internal sealed class ALinePanelPreviewWindow : Window
{
    internal ComboBox UnitForVerification { get; }
    internal IReadOnlyList<TextBox> FieldsForVerification { get; }
    internal CheckBox AgreementForVerification { get; }
    internal CheckBox ConfirmationForVerification { get; }
    internal Button ReviewForVerification { get; }
    internal Button DrawForVerification { get; }
    internal TextBlock SummaryForVerification { get; }
    internal TextBlock StatusForVerification { get; }
    internal Canvas CanvasForVerification { get; }
    internal TextBlock KnownTotalsForVerification { get; }
    internal CheckBox ProposalAgreementForVerification { get; }
    internal Button ProposalApplyForVerification { get; }
    internal TextBlock ProposalForVerification { get; }
    internal ALinePanelPreviewWindow(Window owner,string promptSnapshot,string measurementSnapshot)
    {
        Owner=owner;Title="A kesim · deneysel bel-altı kesiti";Width=700;Height=800;MinWidth=500;MinHeight=450;
        StudioTheme.Apply(this);
        var body=new StackPanel { Margin=new Thickness(16) };
        Content=new ScrollViewer { Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto };
        KnownTotalsForVerification=new TextBlock { TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,10) };
        try
        {
            var known=ALineBodyTotals.Review(promptSnapshot,measurementSnapshot);
            string Display(decimal? mm)=>mm is decimal value ? $"{value/10m:G} cm ({value:G} mm)" : "Eksik veya belirsiz; notları kontrol edin";
            KnownTotalsForVerification.Text=$"İstekteki toplam BEDEN bel çevresi: {Display(known.WaistMm)}\nİstekteki toplam BEDEN kalça çevresi: {Display(known.HipMm)}\nBunlar açılış anındaki notlardan okunmuştur; ölçüm doğruluğu onaylanmadı. Toplam çevre ön ve arka dağılımını belirtmez. Bu yüzden ön ve arka yayları yan dikiş konumları arasında ayrı ölçmeniz gerekir; yarı yarıya dağıtılmadı, aşağıdaki alanlar otomatik doldurulmadı.";
        }
        catch(System.Text.RegularExpressions.RegexMatchTimeoutException) { KnownTotalsForVerification.Text="İstek ölçüleri zaman aşımı nedeniyle okunamadı; hiçbir alan doldurulmadı."; }
        body.Children.Add(KnownTotalsForVerification);
        body.Children.Add(new TextBlock { Text="Tam elbise kalıbı değildir. Sağ/sol simetri varsayılır: ön ve arka yaylar ikiye bölünür; sağ/sol asimetri desteklenmez. Ön/arka yayları BEDEN üzerinde, amaçlanan yan dikiş konumları arasında ayrı ölçün; bu yaylara bolluk dahil etmeyin. Bollukları ayrı girin. Etek çevresi bitmiş giysi ölçüsüdür; bolluk dahildir. Elbise boyu yerine BELDEN etek boyunu girin. Pencere kapanınca girdiler saklanmaz.",TextWrapping=TextWrapping.Wrap });
        var unit=new ComboBox { ItemsSource=new[]{"cm — santimetre","mm — milimetre"},SelectedIndex=-1,Margin=new Thickness(0,4,0,8) };
        body.Children.Add(new Label { Content="Tüm alanlar için birim seçin (zorunlu)",Target=unit });body.Children.Add(unit);
        string[] labels=["Ön bel yayı","Arka bel yayı","Ön kalça yayı","Arka kalça yayı","Ön bel yayı bolluğu","Arka bel yayı bolluğu","Ön kalça yayı bolluğu","Arka kalça yayı bolluğu","Belden kalçaya düşüklük","Belden etek boyu","Bitmiş etek çevresi (bolluk dahil)"];
        var fields=new List<TextBox>();
        foreach(var label in labels)
        {
            var box=new TextBox { MaxLength=24,Margin=new Thickness(0,0,0,6) };fields.Add(box);
            body.Children.Add(new Label { Content=label+" (seçilen birimde)",Target=box });body.Children.Add(box);
        }
        var agreement=new CheckBox { Content=new TextBlock { Text="Sağ/sol simetriyi, ön/arka etek dağılımının eşitliğini ve düz kesitli deneysel gösterimi kabul ediyorum. Asimetrik beden için kullanmayacağım.",TextWrapping=TextWrapping.Wrap },Margin=new Thickness(0,8,0,8) };
        body.Children.Add(agreement);
        var reviewGate=new ALineMeasurementReview();
        var reviewButton=new Button { Content="1 · Ölçüleri cm ve mm olarak incele",Padding=new Thickness(10) };body.Children.Add(reviewButton);
        var summary=new TextBlock { TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,8) };body.Children.Add(summary);
        var confirm=new CheckBox { Content=new TextBlock { Text="Gösterilen cm/mm değerlerini ve toplam beden çevrelerini kendi ölçülerimle karşılaştırdım; birim ve ölçüm kaynağı doğru.",TextWrapping=TextWrapping.Wrap },IsEnabled=false };body.Children.Add(confirm);
        var button=new Button { Content="Kesitleri hesapla ve önizle",Padding=new Thickness(10) };body.Children.Add(button);
        var status=new TextBlock { TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,8) };body.Children.Add(status);
        var canvas=new Canvas { Width=600,Height=340,Background=Brushes.White };
        body.Children.Add(new Viewbox { Child=canvas,MaxHeight=340,Stretch=Stretch.Uniform });
        var blockers=new TextBlock { TextWrapping=TextWrapping.Wrap };body.Children.Add(blockers);
        UnitForVerification=unit;FieldsForVerification=fields.AsReadOnly();AgreementForVerification=agreement;
        ConfirmationForVerification=confirm;ReviewForVerification=reviewButton;DrawForVerification=button;
        SummaryForVerification=summary;StatusForVerification=status;CanvasForVerification=canvas;
        var proposal=new TextBlock { TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,8) };
        var proposalAgreement=new CheckBox { Content=new TextBlock { Text="İstekteki 11 açık ölçünün yukarıdaki cm/mm özetini kontrol ettim; geçici forma mm olarak aktarılmasını onaylıyorum.",TextWrapping=TextWrapping.Wrap },IsEnabled=false };
        var proposalApply=new Button { Content="Onaylanan istek ölçülerini geçici forma aktar",IsEnabled=false,Padding=new Thickness(8) };
        body.Children.Insert(2,proposal);body.Children.Insert(3,proposalAgreement);body.Children.Insert(4,proposalApply);
        ProposalForVerification=proposal;ProposalAgreementForVerification=proposalAgreement;ProposalApplyForVerification=proposalApply;
        ALineRequestMeasurementReview? requestFields=null;
        try
        {
            requestFields=ALineRequestMeasurements.Review(promptSnapshot,measurementSnapshot);
            if(requestFields.Complete)
            {
                var suggested=ALineMeasurementReview.Normalize(requestFields.Millimeters.Select(v=>v!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray(),ALineLengthUnit.Millimeter);
                _=ALinePanelPreview.Create(suggested);
                proposal.Text="İstekte açıkça belirtilen ölçü önerisi; hiçbir eksik ölçü veya bolluk türetilmedi:\n"+
                    string.Join(Environment.NewLine,ALineRequestMeasurements.Labels.Select((label,i)=>$"{label}: {requestFields.Millimeters[i]:G} mm = {requestFields.Millimeters[i]/10m:G} cm"));
                proposalAgreement.IsEnabled=true;
            }
            else proposal.Text=requestFields.Issues.Count>0 ? string.Join(Environment.NewLine,requestFields.Issues) : "İstek 11 ayrı A kesim alanını tamamlamıyor; otomatik dağılım yapılmadı. Eksik alanları kendiniz ölçün. Her açık alanı ayrı satırda veya noktalı virgülle, cm/mm birimiyle yazabilirsiniz.";
        }
        catch(Exception ex) when(ex is ArgumentException or InvalidOperationException or OverflowException or System.Text.RegularExpressions.RegexMatchTimeoutException) { proposal.Text="İstekten ölçü önerilemedi: "+ex.Message; }
        proposalAgreement.Checked+=(_,_)=>proposalApply.IsEnabled=proposalAgreement.IsEnabled;
        proposalAgreement.Unchecked+=(_,_)=>proposalApply.IsEnabled=false;
        proposalApply.Click+=(_,_)=>
        {
            if(proposalAgreement.IsChecked!=true || !proposalAgreement.IsEnabled || requestFields?.Complete!=true)return;
            if(fields.Any(f=>f.Text.Length>0)) { status.Text="Formda girilmiş değerler var; üzerlerine yazılmadı. Aktarım için alanları önce kendiniz temizleyin.";return; }
            unit.SelectedIndex=1;
            for(int i=0;i<fields.Count;i++)fields[i].Text=requestFields.Millimeters[i]!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            status.Text="Yalnız açık istek ölçüleri mm olarak aktarıldı. Simetriyi ve form özetini ayrıca inceleyip onaylayın. İstek değişmedi; çıktı kapalı.";
        };
        ALinePanelInput ReadInput()=>ALineMeasurementReview.Normalize(fields.Select(f=>f.Text).ToArray(),unit.SelectedIndex switch { 0=>ALineLengthUnit.Centimeter,1=>ALineLengthUnit.Millimeter,_=>(ALineLengthUnit?)null });
        void ValidateRequestFields(ALinePanelInput input)
        {
            var source=ALineRequestMeasurements.Review(promptSnapshot,measurementSnapshot);
            if(source.Issues.Count>0)throw new ArgumentException(string.Join(Environment.NewLine,source.Issues));
            double[] current=[input.FrontWaistArc,input.BackWaistArc,input.FrontHipArc,input.BackHipArc,input.FrontWaistEase,input.BackWaistEase,input.FrontHipEase,input.BackHipEase,input.HipDepth,input.WaistToHem,input.FinishedHemCircumference];
            for(int i=0;i<current.Length;i++)if(source.Millimeters[i] is decimal value && Math.Abs(current[i]-(double)value)>0.000001)
                throw new ArgumentException(ALineRequestMeasurements.Labels[i]+": form değeri açık istek ölçüsüyle çelişiyor; önce kaynağı düzeltin.");
        }
        void Clear() { reviewGate.Invalidate();confirm.IsChecked=false;confirm.IsEnabled=false;summary.Text="";canvas.Children.Clear();blockers.Text="";status.Text="Girdiler değişti; ölçüleri yeniden inceleyip onaylayın. Çıktı kapalı."; }
        foreach(var field in fields)field.TextChanged+=(_,_)=>Clear();
        unit.SelectionChanged+=(_,_)=>Clear();
        agreement.Checked+=(_,_)=>Clear();agreement.Unchecked+=(_,_)=>Clear();
        confirm.Unchecked+=(_,_)=> { canvas.Children.Clear();blockers.Text=""; };
        reviewButton.Click+=(_,_)=>
        {
            Clear();
            try
            {
                if(agreement.IsChecked!=true)throw new ArgumentException("Önce simetri ve kesit varsayımlarını inceleyin.");
                var input=ReadInput();ValidateRequestFields(input);reviewGate.Stage(input);
                double[] normalized=[input.FrontWaistArc,input.BackWaistArc,input.FrontHipArc,input.BackHipArc,input.FrontWaistEase,input.BackWaistEase,input.FrontHipEase,input.BackHipEase,input.HipDepth,input.WaistToHem,input.FinishedHemCircumference];
                string Both(double mm)=>$"{mm:G9} mm = {mm/10:G9} cm";
                summary.Text=string.Join(Environment.NewLine,labels.Select((label,i)=>label+": "+Both(normalized[i])))+
                    $"\nBEDEN bel toplamı (bolluksuz): {Both(input.FrontWaistArc+input.BackWaistArc)}\nBEDEN kalça toplamı (bolluksuz): {Both(input.FrontHipArc+input.BackHipArc)}\nÖrneğin 72 mm yalnız 7,2 cm'dir. Bu kontrol beden ölçülerinin doğruluğunu veya makul olduğunu kanıtlamaz; yanlış birimi düzeltin.";
                confirm.IsEnabled=true;status.Text="Özetin tamamını ölçü kaynağınızla karşılaştırıp onaylayın; henüz çizim yok.";
            }
            catch(Exception ex) when(ex is ArgumentException or InvalidOperationException or OverflowException or System.Text.RegularExpressions.RegexMatchTimeoutException) { status.Text=ex.Message; }
        };
        button.Click+=(_,_)=>
        {
            canvas.Children.Clear();blockers.Text="";
            try
            {
                if(agreement.IsChecked!=true)throw new ArgumentException("Gösterim varsayımını okuyup seçin.");
                var input=ReadInput();ValidateRequestFields(input);
                if(confirm.IsChecked!=true)throw new ArgumentException("Önce cm/mm özetini inceleyip ölçüleri onaylayın.");
                reviewGate.Confirm();reviewGate.RequireConfirmed(input);
                var review=DressMeasurementParser.Review(promptSnapshot,measurementSnapshot);
                foreach(var (label,total) in new[]{("bel çevresi",input.FrontWaistArc+input.BackWaistArc),("kalça çevresi",input.FrontHipArc+input.BackHipArc)})
                {
                    if(review.Questions.Any(q=>q.StartsWith(label+":",StringComparison.Ordinal) && !q.Contains("açık ölçü ve birim")))
                        throw new ArgumentException(label+": istek notlarındaki belirsizliği önce düzeltin.");
                    var existing=review.Measurements.FirstOrDefault(m=>m.Label==label);
                    if(existing is not null && Math.Abs((double)existing.Millimeters-total)>0.000001)
                        throw new ArgumentException(label+": ön ve arka yay toplamı kayıtlı istek ölçüsünden farklı; önce kaynağı düzeltin.");
                }
                var preview=ALinePanelPreview.Create(input);
                var scale=Math.Min(240/(input.FinishedHemCircumference/4),280/input.WaistToHem);
                void Draw(IReadOnlyList<Point2> points,double x,string label,Brush stroke)
                {
                    var polygon=new Polygon { Stroke=stroke,StrokeThickness=2,Fill=Brushes.Transparent,Points=new PointCollection(points.Select(p=>new Point(x+p.X*scale,35+p.Y*scale))) };
                    canvas.Children.Add(polygon);var text=new TextBlock { Text=label,Foreground=Brushes.Black };Canvas.SetLeft(text,x);Canvas.SetTop(text,5);canvas.Children.Add(text);
                }
                Draw(preview.Front,20,"Ön yarım · merkez sol",Brushes.DarkBlue);Draw(preview.Back,320,"Arka yarım · merkez sol",Brushes.DarkRed);
                status.Text=$"Ön yan çizgi {preview.FrontSideLengthMm:F3} mm; arka {preview.BackSideLengthMm:F3} mm; fark {preview.SideMismatchMm:F3} mm. Dikiş eşleşmesi onaylanmadı. Görünüm ölçekli baskı değildir.";
                blockers.Text=string.Join(Environment.NewLine,preview.Unresolved);
            }
            catch(Exception ex) when(ex is ArgumentException or InvalidOperationException or OverflowException or System.Text.RegularExpressions.RegexMatchTimeoutException)
            { status.Text=ex.Message; }
        };
    }
}
