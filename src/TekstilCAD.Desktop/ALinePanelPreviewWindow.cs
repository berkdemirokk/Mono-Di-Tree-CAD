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
        var proposalAgreement=new CheckBox { Content=new TextBlock { Text="Doldurulacak, korunacak ve eksik alanları kontrol ettim; yalnız açık kaynak değerlerinin seçtiğim birimde boş alanlara aktarılmasını onaylıyorum.",TextWrapping=TextWrapping.Wrap },IsEnabled=false };
        var proposalApply=new Button { Content="Onaylanan istek ölçülerini geçici forma aktar",IsEnabled=false,Padding=new Thickness(8) };
        body.Children.Insert(2,proposal);body.Children.Insert(3,proposalAgreement);body.Children.Insert(4,proposalApply);
        body.Children.Insert(5,new TextBlock { Text="Dil desteği sınırlıdır: formdaki 11 tam etiket ve açık cm/mm değerleri tanınır. Genel ölçülere atıf yapan serbest metnin anlamı varsayılmaz; öneri ve çizim durdurulur. Ölçü içeren istekte bunlar/hepsi/tümü gibi zamirlerin neyi anlattığı da varsayılmaz; diğer alandaki olumlu cümle bu belirsizliği kaldırmaz. Yalnız bir metin alanının tamamını oluşturan “ölçüler geçerli” veya “ölçüleri kullanmak istiyorum” sabit olumlu ifadeleri kabul edilir (başında “bu” olabilir). Bunlara eklenen başka cümleler kabul edilmez. Bu yardımcı tüm Türkçeyi anlamaz; özetin tamamını ölçü kaynağınızla karşılaştırın.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,4,0,8) });
        ProposalForVerification=proposal;ProposalAgreementForVerification=proposalAgreement;ProposalApplyForVerification=proposalApply;
        ALineRequestMeasurementReview? requestFields=null;string? sourceError=null;
        try
        {
            requestFields=ALineRequestMeasurements.Review(promptSnapshot,measurementSnapshot);
        }
        catch(Exception ex) when(ex is ArgumentException or InvalidOperationException or OverflowException or System.Text.RegularExpressions.RegexMatchTimeoutException) { sourceError=ex.Message; }
        ALineLengthUnit? CurrentUnit()=>unit.SelectedIndex switch { 0=>ALineLengthUnit.Centimeter,1=>ALineLengthUnit.Millimeter,_=>(ALineLengthUnit?)null };
        ALineTransferPlan? transferPlan=null;
        void RefreshProposal()
        {
            transferPlan=null;proposalAgreement.IsChecked=false;proposalAgreement.IsEnabled=false;proposalApply.IsEnabled=false;
            proposal.Text="Açılış anındaki istekten aktarım planı. Ana isteği değiştirirseniz pencereyi yeniden açın. Eksik alanlar ve bolluklar türetilmez; bu aktarım geometri veya üretim onayı değildir.\n";
            if(requestFields is null) { proposal.Text+="İstekten ölçü önerilemedi: "+sourceError;return; }
            var selectedUnit=CurrentUnit();var suffix=selectedUnit==ALineLengthUnit.Centimeter ? "cm" : "mm";
            proposal.Text+=string.Join(Environment.NewLine,labels.Select((label,i)=>
                label+": "+(fields[i].Text.Length>0 ? "KORUNACAK — "+fields[i].Text : requestFields.Millimeters[i].HasValue ? "DOLDURULABİLİR" : "EKSİK — boş kalacak")+
                (requestFields.Millimeters[i] is decimal mm ? $"; kaynak {mm:G} mm = {mm/10m:G} cm" : "; kaynak belirtilmedi")));
            try
            {
                var candidate=ALineTransferPlan.Create(requestFields.Millimeters,requestFields.Issues,fields.Select(f=>f.Text).ToArray(),selectedUnit);
                var after=candidate.ApplyTo(fields.Select(f=>f.Text).ToArray(),selectedUnit);
                var totals=ALineBodyTotals.Review(promptSnapshot,measurementSnapshot);
                foreach(var (label,a,b,total) in new[]{("bel çevresi",0,1,totals.WaistMm),("kalça çevresi",2,3,totals.HipMm)})
                {
                    decimal? left=after[a].Length>0 ? ALineMeasurementReview.ParseLengthMm(after[a],selectedUnit,a) : null;
                    decimal? right=after[b].Length>0 ? ALineMeasurementReview.ParseLengthMm(after[b],selectedUnit,b) : null;
                    if(total is decimal known && (left>=known || right>=known || (left.HasValue && right.HasValue && left+right!=known)))
                        throw new ArgumentException(label+": aktarım sonrası açık yaylar toplam beden ölçüsüyle çelişiyor; eksik yay türetilmedi, alanlar değiştirilmedi.");
                }
                if(after.All(value=>value.Length>0)) { var input=ALineMeasurementReview.Normalize(after,selectedUnit);ValidateRequestFields(input);_ = ALinePanelPreview.Create(input); }
                proposal.Text+="\n"+string.Join(Environment.NewLine,candidate.Rows.Where(row=>row.ProposedText is not null).Select(row=>labels[row.Index]+": DOLDURULACAK — "+row.ProposedText+" "+suffix));
                proposal.Text+="\nOndalık değerler kayıpsız aktarılır; üç ondalık basamak gerektiğinde 0.1250 gibi sonuna sıfır eklenir. Bu, 0.125 mm ile aynı değerdir; yuvarlama yapılmaz.";
                if(!candidate.HasWrites) { proposal.Text+="\nAktarılacak boş alan yok; dolu alanlar korunuyor.";return; }
                transferPlan=candidate;proposalAgreement.IsEnabled=true;
            }
            catch(Exception ex) when(ex is ArgumentException or InvalidOperationException or OverflowException or System.Text.RegularExpressions.RegexMatchTimeoutException) { proposal.Text+="\nAktarım kapalı: "+ex.Message; }
        }
        proposalAgreement.Checked+=(_,_)=>proposalApply.IsEnabled=proposalAgreement.IsEnabled;
        proposalAgreement.Unchecked+=(_,_)=>proposalApply.IsEnabled=false;
        proposalApply.Click+=(_,_)=>
        {
            var accepted=transferPlan;
            if(proposalAgreement.IsChecked!=true || !proposalAgreement.IsEnabled || accepted is null)return;
            try
            {
                var after=accepted.ApplyTo(fields.Select(f=>f.Text).ToArray(),CurrentUnit());
                Clear();
                foreach(var row in accepted.Rows.Where(row=>row.ProposedText is not null))fields[row.Index].Text=after[row.Index];
                RefreshProposal();
                status.Text="Yalnız açık kaynak değerleri seçili birimde boş alanlara aktarıldı; dolu ve eksik alanlar korundu. Simetriyi ve tam form özetini ayrıca onaylayın. İstek değişmedi; çıktı kapalı.";
            }
            catch(InvalidOperationException ex) { RefreshProposal();status.Text=ex.Message; }
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
        foreach(var field in fields)field.TextChanged+=(_,_)=> { Clear();RefreshProposal(); };
        unit.SelectionChanged+=(_,_)=> { Clear();RefreshProposal(); };
        agreement.Checked+=(_,_)=>Clear();agreement.Unchecked+=(_,_)=>Clear();
        confirm.Unchecked+=(_,_)=> { canvas.Children.Clear();blockers.Text=""; };
        RefreshProposal();
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
