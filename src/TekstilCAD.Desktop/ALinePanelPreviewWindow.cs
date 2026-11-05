using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.IO;
using System.Globalization;
using Microsoft.Win32;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;
using TekstilCAD.Patterns;

namespace TekstilCAD.Desktop;

/// <summary>Isolated snapshot with explicit unconfirmed form-draft persistence; no CAD or production output.</summary>
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
    internal TextBlock WaistComparisonForVerification { get; }=new() { TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,8) };
    internal Button FormToRequestForVerification { get; private set; }=null!;
    internal Func<bool>? ConfirmFormToRequestForVerification { get; set; }
    internal CheckBox ProposalAgreementForVerification { get; }
    internal Button ProposalApplyForVerification { get; }
    internal TextBlock ProposalForVerification { get; }
    private Action<string> saveWork = null!;
    private Func<string,Func<bool>,bool> loadWork = null!;
    private Func<Func<string?>,bool> saveSelectedWork = null!;
    private Func<Func<string?>,Func<bool>,bool> loadSelectedWork = null!;
    internal Func<string,bool>? ConfirmSourceMismatchForVerification { get; set; }
    internal Func<bool>? ConfirmSourceCorrectionForVerification { get; set; }
    internal ComboBox CorrectionFieldsForVerification { get; private set; }=null!;
    internal Button CorrectSourceForVerification { get; private set; }=null!;
    private Func<bool> canCloseWork = null!;
    internal Func<bool>? ConfirmDiscardWorkForVerification { get; set; }
    internal Func<bool>? ConfirmUnitConversionForVerification { get; set; }
    internal static string[] ConvertUnitFields(IReadOnlyList<string> fields,ALineLengthUnit oldUnit,ALineLengthUnit newUnit)
    {
        if(fields.Count!=11)throw new ArgumentException("On bir alan gerekli.");
        var converted=new string[fields.Count];
        for(int i=0;i<fields.Count;i++)
        {
            if(fields[i].Length==0) { converted[i]="";continue; }
            decimal mm=ALineMeasurementReview.ParseLengthMm(fields[i],oldUnit,i);
            var value=newUnit==ALineLengthUnit.Centimeter ? mm/10m : mm;
            string text=value.ToString("0.############################",CultureInfo.InvariantCulture);
            var dot=text.IndexOf('.');if(dot>=0 && text.Length-dot-1==3)text+="0";
            if(text.Length>24 || ALineMeasurementReview.ParseLengthMm(text,newUnit,i)!=mm)throw new ArgumentException("Alan kayıpsız dönüştürülemiyor; birim ve değerler korunuyor.");
            converted[i]=text;
        }
        return converted;
    }
    internal void SaveWorkForVerification(string path)=>saveWork(path);
    internal bool SaveSelectedWorkForVerification(Func<string?> selectPath)=>saveSelectedWork(selectPath);
    internal bool CanCloseWorkForVerification()=>canCloseWork();
    internal bool LoadWorkForVerification(string path,Func<bool> confirm)=>loadWork(path,confirm);
    internal bool TrySaveWork(Func<string?> selectPath)
    {
        try
        {
            if(!saveSelectedWork(selectPath))return false;
            StatusForVerification.Text="Ölçü çalışması yeni dosyaya kaydedildi. Ölçüler ve model varsayımları onaylanmadı; çıktı kapalı.";
            return true;
        }
        catch(Exception ex) when(ex is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { StatusForVerification.Text="Çalışma kaydedilemedi; form korundu. Yeni bir dosya adı kullanın. "+ex.Message;return false; }
    }
    internal bool TryOpenWork(Func<string?> selectPath,Func<bool> approve)
    {
        try
        {
            return loadSelectedWork(selectPath,approve);
        }
        catch(Exception ex) when(ex is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { StatusForVerification.Text="Çalışma açılamadı; mevcut form korundu. "+ex.Message;return false; }
    }
    internal ALinePanelPreviewWindow(Window owner,string promptSnapshot,string measurementSnapshot)
    {
        long editVersion=0;bool closed=false;bool restoringUnit=false,loadingWork=false,unitConfirmation=false;int committedUnit=-1;
        string CurrentSource()=>ALineWorkDraftStorage.SourceIdentity(owner is DesignRequestWindow requestOwner ? requestOwner.CaptureForVerification() : new DesignRequest(promptSnapshot,measurementSnapshot,null));
        (long,long) SourceRevision()=>owner is DesignRequestWindow requestOwner ? requestOwner.ALineSourceRevision : (0,0);
        bool SourceAvailable()=>owner is not DesignRequestWindow requestOwner || requestOwner.ALineSourceAvailable;
        var sourceIdentity=CurrentSource();string? workOrigin=sourceIdentity;
        var sourceLabel=new TextBlock { TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,8) };
        void RefreshSourceLabel()=>sourceLabel.Text="Kaynak istek özeti: "+sourceIdentity+" ("+ALineWorkDraftStorage.SourceAlgorithm+"). Bu özet şifreleme değildir; kaynak metni ve görseli dosyaya eklenmez.";
        void AdoptAuthorizedSourceChange() { var previous=sourceIdentity;sourceIdentity=CurrentSource();if(workOrigin==previous)workOrigin=sourceIdentity;RefreshSourceLabel(); }
        RefreshSourceLabel();
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
        body.Children.Add(new TextBlock { Text="Tam elbise kalıbı değildir. Sağ/sol simetri varsayılır: ön ve arka yaylar ikiye bölünür; sağ/sol asimetri desteklenmez. Ön/arka yayları BEDEN üzerinde, amaçlanan yan dikiş konumları arasında ayrı ölçün; bu yaylara bolluk dahil etmeyin. Bollukları ayrı girin. Etek çevresi bitmiş giysi ölçüsüdür; bolluk dahildir. Elbise boyu yerine BELDEN etek boyunu girin. Otomatik kayıt yoktur; pencere kapanınca yalnız açıkça dosyaya kaydettiğiniz çalışma geri açılabilir.",TextWrapping=TextWrapping.Wrap });
        var unit=new ComboBox { ItemsSource=new[]{"cm — santimetre","mm — milimetre"},SelectedIndex=-1,Margin=new Thickness(0,4,0,8) };
        var unitLabel=new Label { Content="Tüm alanlar için birim seçin (zorunlu)",Target=unit };
        AutomationProperties.SetName(unit,"Ölçü aktarımından önce tüm alanlar için birim seçin (zorunlu)");
        AutomationProperties.SetLabeledBy(unit,unitLabel);
        AutomationProperties.SetHelpText(unit,"Önce cm veya mm seçin, ardından aktarım özetini inceleyip ayrıca onaylayın. Varsayılan birim yoktur; birim seçmek alanları doldurmaz.");
        body.Children.Add(unitLabel);body.Children.Add(unit);
        string[] labels=["Ön bel yayı","Arka bel yayı","Ön kalça yayı","Arka kalça yayı","Ön bel yayı bolluğu","Arka bel yayı bolluğu","Ön kalça yayı bolluğu","Arka kalça yayı bolluğu","Belden kalçaya düşüklük","Belden etek boyu","Bitmiş etek çevresi (bolluk dahil)"];
        var fields=new List<TextBox>();
        foreach(var label in labels)
        {
            var box=new TextBox { MaxLength=24,Margin=new Thickness(0,0,0,6) };fields.Add(box);
            body.Children.Add(new Label { Content=label+" (seçilen birimde)",Target=box });body.Children.Add(box);
        }
        body.Children.Add(WaistComparisonForVerification);
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
        body.Children.Insert(4,proposal);body.Children.Insert(5,proposalAgreement);body.Children.Insert(6,proposalApply);
        body.Children.Insert(7,new TextBlock { Text="Dil desteği sınırlıdır: formdaki 11 tam etiket ve açık cm/mm değerleri tanınır. Genel ölçülere atıf yapan serbest metnin anlamı varsayılmaz; öneri ve çizim durdurulur. Ölçü içeren istekte bunlar/hepsi/tümü gibi zamirlerin neyi anlattığı da varsayılmaz; diğer alandaki olumlu cümle bu belirsizliği kaldırmaz. Yalnız bir metin alanının tamamını oluşturan “ölçüler geçerli” veya “ölçüleri kullanmak istiyorum” sabit olumlu ifadeleri kabul edilir (başında “bu” olabilir). Bunlara eklenen başka cümleler kabul edilmez. Bu yardımcı tüm Türkçeyi anlamaz; özetin tamamını ölçü kaynağınızla karşılaştırın.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,4,0,8) });
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
        void ValidateRequestTotals(ALinePanelInput input)
        {
            var review=DressMeasurementParser.Review(promptSnapshot,measurementSnapshot);
            foreach(var (label,total) in new[]{("bel çevresi",input.FrontWaistArc+input.BackWaistArc),("kalça çevresi",input.FrontHipArc+input.BackHipArc)})
            {
                if(review.Questions.Any(q=>q.StartsWith(label+":",StringComparison.Ordinal) && !q.Contains("açık ölçü ve birim")))
                    throw new ArgumentException(label+": istek notlarındaki belirsizliği önce düzeltin.");
                var existing=review.Measurements.FirstOrDefault(m=>m.Label==label);
                if(existing is not null && Math.Abs((double)existing.Millimeters-total)>0.000001)
                    throw new ArgumentException(label+": ön ve arka yay toplamı kayıtlı istek ölçüsünden farklı; önce kaynağı düzeltin.");
            }
        }
        Closed+=(_,_)=>closed=true;
        void Clear() { editVersion++;reviewGate.Invalidate();confirm.IsChecked=false;confirm.IsEnabled=false;summary.Text="";canvas.Children.Clear();blockers.Text="";status.Text="Girdiler değişti; ölçüleri yeniden inceleyip onaylayın. Çıktı kapalı."; }
        void RefreshWaistComparison()=>WaistComparisonForVerification.Text=ALineWaistSourceComparison.Describe(promptSnapshot,measurementSnapshot,fields[0].Text,fields[1].Text,CurrentUnit());
        var correctionFields=new ComboBox { ItemsSource=labels,SelectedIndex=-1,Margin=new Thickness(0,4,0,4) };
        CorrectionFieldsForVerification=correctionFields;body.Children.Add(new Label { Content="Düzeltilecek açık istek ölçüsü",Target=correctionFields });body.Children.Add(correctionFields);
        var correctSource=new Button { Content="Kaynak ölçüyü düzelt…",Padding=new Thickness(8),HorizontalAlignment=HorizontalAlignment.Left };
        CorrectSourceForVerification=correctSource;body.Children.Add(correctSource);
        correctSource.Click+=(_,_)=>
        {
            try
            {
                if(owner is not DesignRequestWindow request)throw new InvalidOperationException("Kaynak istek penceresi bulunamadı.");
                if(!SourceAvailable() || CurrentSource()!=sourceIdentity)throw new InvalidOperationException("Kaynak istek değişti; pencereyi yeniden açın.");
                int index=correctionFields.SelectedIndex;if(index<0)throw new ArgumentException("Düzeltilecek alanı seçin.");
                var form=fields.Select(f=>f.Text).ToArray();var chosenUnit=CurrentUnit();var version=editVersion;
                var plan=ALineSourceCorrection.Create(promptSnapshot,measurementSnapshot,index,form[index],chosenUnit);
                bool approved=request.CorrectALineSource(promptSnapshot,measurementSnapshot,plan,
                    ()=>ConfirmSourceCorrectionForVerification?.Invoke() ?? MessageBox.Show(this,$"Kaynak: {plan.Place}\nAlan: {plan.Label}\nEski: {plan.OldText}\nYeni: {plan.NewText}\n\nYalnız bu açık değer güncellenecek. Form korunur; ölçü ve simetri onayları sıfırlanır.","Kaynak ölçüyü düzelt",MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No)==MessageBoxResult.Yes,
                    ()=>!closed && editVersion==version && correctionFields.SelectedIndex==index && CurrentUnit()==chosenUnit && form.SequenceEqual(fields.Select(f=>f.Text)));
                if(!approved) { status.Text="Düzeltme iptal edildi veya istek/form değişti; değerler korundu.";return; }
                promptSnapshot=plan.Prompt;measurementSnapshot=plan.Notes;AdoptAuthorizedSourceChange();
                requestFields=ALineRequestMeasurements.Review(promptSnapshot,measurementSnapshot);sourceError=null;
                agreement.IsChecked=false;Clear();RefreshProposal();RefreshWaistComparison();
                status.Text="Kaynak ölçü güncellendi; form korundu. Simetri ve ölçüleri yeniden inceleyip onaylayın.";
            }
            catch(Exception ex) when(ex is ArgumentException or InvalidOperationException or OverflowException or System.Text.RegularExpressions.RegexMatchTimeoutException) { status.Text=ex.Message; }
        };
        var addFormToRequest=new Button { Content="Açık form değerlerini istek notlarına ekle…",Padding=new Thickness(8),HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,6,0,0) };
        FormToRequestForVerification=addFormToRequest;
        body.Children.Add(addFormToRequest);
        addFormToRequest.Click+=(_,_)=>
        {
            try
            {
                if(owner is not DesignRequestWindow request)throw new InvalidOperationException("Kaynak istek penceresi bulunamadı.");
                if(!SourceAvailable() || CurrentSource()!=sourceIdentity)throw new InvalidOperationException("Kaynak istek değişti; pencereyi yeniden açın.");
                var form=fields.Select(f=>f.Text).ToArray();var selectedUnit=CurrentUnit();var version=editVersion;
                var plan=ALineFormToRequest.Create(promptSnapshot,measurementSnapshot,form,selectedUnit);
                if(plan.Added.Count==0) { status.Text="Eklenecek yeni açık değer yok; kaynak ve form değiştirilmedi.";return; }
                string List(IReadOnlyList<string> rows)=>rows.Count==0 ? "Yok" : string.Join("\n",rows);
                bool applied=request.AppendALineForm(promptSnapshot,measurementSnapshot,plan,
                    ()=>ConfirmFormToRequestForVerification?.Invoke() ?? MessageBox.Show(this,$"Eklenecek:\n{List(plan.Added)}\n\nKaynakta korunacak:\n{List(plan.Preserved)}\n\nEksik kalacak:\n{List(plan.Missing)}\n\nÖlçüler isteğe eklenir; dosyaya kaydetmek ayrıca gerekir. Ölçü ve simetri onayları sıfırlanır.","Açık form ölçülerini isteğe ekle",MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No)==MessageBoxResult.Yes,
                    ()=>!closed && editVersion==version && CurrentUnit()==selectedUnit && form.SequenceEqual(fields.Select(f=>f.Text)));
                if(!applied) { status.Text="Aktarım iptal edildi veya istek/form değişti; değerler korundu.";return; }
                measurementSnapshot=plan.Notes;AdoptAuthorizedSourceChange();requestFields=ALineRequestMeasurements.Review(promptSnapshot,measurementSnapshot);sourceError=null;
                agreement.IsChecked=false;Clear();RefreshProposal();RefreshWaistComparison();
                status.Text="Açık değerler istek notlarına eklendi; istek henüz kaydedilmedi. Form korundu, ölçüleri yeniden onaylayın.";
            }
            catch(Exception ex) when(ex is ArgumentException or InvalidOperationException or OverflowException or System.Text.RegularExpressions.RegexMatchTimeoutException) { status.Text=ex.Message; }
        };
        foreach(var field in fields)field.TextChanged+=(_,_)=> { Clear();RefreshProposal();RefreshWaistComparison(); };
        unit.SelectionChanged+=(_,_)=>
        {
            if(restoringUnit)return;
            var requested=unit.SelectedIndex;
            if(!loadingWork && committedUnit>=0 && requested!=committedUnit && fields.Any(f=>f.Text.Length>0))
            {
                restoringUnit=true;try { unit.SelectedIndex=committedUnit; } finally { restoringUnit=false; }
                Clear();RefreshProposal();RefreshWaistComparison();
                if(unitConfirmation || requested is not (0 or 1)) { status.Text="Birim değiştirilmedi; form korundu.";return; }
                var before=fields.Select(f=>f.Text).ToArray();int old=committedUnit;long version=editVersion;
                try
                {
                    var converted=ConvertUnitFields(before,old==0 ? ALineLengthUnit.Centimeter : ALineLengthUnit.Millimeter,requested==0 ? ALineLengthUnit.Centimeter : ALineLengthUnit.Millimeter);
                    unitConfirmation=true;
                    bool approved;
                    try { approved=ConfirmUnitConversionForVerification?.Invoke() ?? MessageBox.Show(this,$"Dolu alanlar {(old==0?"cm":"mm")} biriminden {(requested==0?"cm":"mm")} birimine kayıpsız dönüştürülecek. Boş alanlar boş kalır. Önceki ölçü onayı ve önizleme sıfırlanır. Devam edilsin mi?","Birim dönüşümü",MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No)==MessageBoxResult.Yes; }
                    finally { unitConfirmation=false; }
                    if(!approved || closed || editVersion!=version || committedUnit!=old || unit.SelectedIndex!=old || !before.SequenceEqual(fields.Select(f=>f.Text))) { status.Text="Dönüşüm iptal edildi veya form değişti; değerler korunuyor.";return; }
                    restoringUnit=true;
                    try { unit.SelectedIndex=requested;committedUnit=requested;for(int i=0;i<fields.Count;i++)fields[i].Text=converted[i]; }
                    finally { restoringUnit=false; }
                    Clear();RefreshProposal();RefreshWaistComparison();status.Text="Dolu alanlar kayıpsız dönüştürüldü; ölçüleri yeniden inceleyip onaylayın.";
                }
                catch(Exception ex) when(ex is ArgumentException or OverflowException) { status.Text="Birim değişmedi; alanlar korundu. "+ex.Message; }
                return;
            }
            committedUnit=requested;Clear();RefreshProposal();RefreshWaistComparison();
        };
        agreement.Checked+=(_,_)=>Clear();agreement.Unchecked+=(_,_)=>Clear();
        confirm.Unchecked+=(_,_)=> { editVersion++;canvas.Children.Clear();blockers.Text=""; };
        confirm.Checked+=(_,_)=>editVersion++;
        proposalAgreement.Checked+=(_,_)=>editVersion++;
        proposalAgreement.Unchecked+=(_,_)=>editVersion++;
        RefreshProposal();RefreshWaistComparison();
        reviewButton.Click+=(_,_)=>
        {
            Clear();
            try
            {
                if(agreement.IsChecked!=true)throw new ArgumentException("Önce simetri ve kesit varsayımlarını inceleyin.");
                var input=ReadInput();ValidateRequestFields(input);ValidateRequestTotals(input);_ = ALinePanelPreview.Create(input);reviewGate.Stage(input);
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
            editVersion++;
            canvas.Children.Clear();blockers.Text="";
            try
            {
                if(agreement.IsChecked!=true)throw new ArgumentException("Gösterim varsayımını okuyup seçin.");
                var input=ReadInput();ValidateRequestFields(input);ValidateRequestTotals(input);
                if(confirm.IsChecked!=true)throw new ArgumentException("Önce cm/mm özetini inceleyip ölçüleri onaylayın.");
                reviewGate.Confirm();reviewGate.RequireConfirmed(input);
                var preview=ALinePanelPreview.Create(input);
                var scale=Math.Min(240/(input.FinishedHemCircumference/4),280/input.WaistToHem);
                void Draw(IReadOnlyList<Point2> points,double x,string label,Brush stroke)
                {
                    var polygon=new Polygon { Stroke=stroke,StrokeThickness=2,Fill=Brushes.Transparent,Points=new PointCollection(points.Select(p=>new Point(x+p.X*scale,35+p.Y*scale))) };
                    canvas.Children.Add(polygon);var text=new TextBlock { Text=label,Foreground=Brushes.Black };Canvas.SetLeft(text,x);Canvas.SetTop(text,5);canvas.Children.Add(text);
                }
                Draw(preview.Front,20,"Ön yarım · merkez sol",Brushes.DarkBlue);Draw(preview.Back,320,"Arka yarım · merkez sol",Brushes.DarkRed);
                var difference=preview.SideMismatchMm;
                var formattedZero=0d.ToString("F3");
                var differenceText=difference==0 ? "hesaplanan fark 0 mm" : difference.ToString("F3")==formattedZero ? $"hesaplanan fark sıfırdan büyük (üç ondalığa yuvarlanınca {formattedZero} mm)" : $"hesaplanan fark yaklaşık {difference:F3} mm";
                status.Text=$"Ön yan çizgi yaklaşık {preview.FrontSideLengthMm:F3} mm; arka yaklaşık {preview.BackSideLengthMm:F3} mm; {differenceText}. Uzunluklar ve fark gösterimde 0,001 mm'ye yuvarlanır. Dikiş eşleşmesi onaylanmadı. Görünüm ölçekli baskı değildir.";
                blockers.Text=string.Join(Environment.NewLine,preview.Unresolved);
            }
            catch(Exception ex) when(ex is ArgumentException or InvalidOperationException or OverflowException or System.Text.RegularExpressions.RegexMatchTimeoutException)
            { status.Text=ex.Message; }
        };
        ALineWorkDraft CaptureWork()=>new(unit.SelectedIndex switch { 0=>"cm",1=>"mm",_=>null },fields.Select(f=>f.Text).ToArray(),workOrigin);
        static bool SameWork(ALineWorkDraft a,ALineWorkDraft b)=>a.Unit==b.Unit && a.SourceHash==b.SourceHash && a.Fields.SequenceEqual(b.Fields);
        var durableWork=CaptureWork();bool closingPrompt=false;
        canCloseWork=()=>
        {
            if(closingPrompt)return false;
            var before=CaptureWork();if(SameWork(before,durableWork))return true;
            var version=editVersion;closingPrompt=true;
            try
            {
                var accepted=ConfirmDiscardWorkForVerification?.Invoke() ?? MessageBox.Show(this,
                    "Bu ölçü çalışmasında dosyaya kaydedilmemiş alan veya birim değişiklikleri var. Kaydetmeden kapatırsanız kaybolacak. Kaydetmek için İptal'i seçin. Kaydetmeden kapatılsın mı?",
                    "Kaydedilmemiş ölçü çalışması",MessageBoxButton.OKCancel,MessageBoxImage.Warning,MessageBoxResult.Cancel)==MessageBoxResult.OK;
                if(!accepted)return false;
                if(closed || editVersion!=version || !SameWork(before,CaptureWork()))
                { status.Text="Çalışma kapatma onayı sırasında değişti; pencere açık tutuldu. Güncel alanları yeniden inceleyin.";return false; }
                return true;
            }
            finally { closingPrompt=false; }
        };
        Closing+=(_,e)=> { if(!canCloseWork())e.Cancel=true; };
        saveSelectedWork=selectPath=>
        {
            var before=CaptureWork();var version=editVersion;var sourceRevision=SourceRevision();
            var path=selectPath();if(path is null)return false;
            if(closed || editVersion!=version || !SameWork(before,CaptureWork()))
                throw new InvalidOperationException("Dosya seçilirken çalışma değişti; hiçbir dosya yazılmadı. Güncel çalışmayı yeniden kaydedin.");
            if(!SourceAvailable() || SourceRevision()!=sourceRevision || CurrentSource()!=sourceIdentity)throw new InvalidOperationException("Kaynak istek değişti; ölçü çalışması kaydedilmedi. Pencereyi güncel istekten yeniden açın.");
            ALineWorkDraftStorage.SaveNew(path,before);
            durableWork=before;return true;
        };
        saveWork=path=>saveSelectedWork(()=>path);
        loadWork=(path,approve)=>
        {
            // Parse and validate everything before asking to replace the form.
            // A changed form during the modal confirmation must not be overwritten.
            var draft=ALineWorkDraftStorage.Load(path);var before=CaptureWork();var version=editVersion;var sourceRevision=SourceRevision();
            if(!SourceAvailable() || CurrentSource()!=sourceIdentity)throw new InvalidOperationException("Kaynak istek değişti; form korunuyor.");
            if(draft.SourceHash is { } id && id!=sourceIdentity)
            {
                var warning="Dosya başka bir istek anlık görüntüsüne ait. Dosya kimliği: "+id+"; mevcut istek kimliği: "+sourceIdentity+". Bu farklılığı inceleyip yine de açılsın mı?";
                if(!(ConfirmSourceMismatchForVerification?.Invoke(warning) ?? (MessageBox.Show(this,warning,"Farklı kaynak istek",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)==MessageBoxResult.Yes)))return false;
                if(closed || version!=editVersion || !SourceAvailable() || SourceRevision()!=sourceRevision || CurrentSource()!=sourceIdentity || !SameWork(before,CaptureWork()))throw new InvalidOperationException("Farklı kaynak onayı sırasında çalışma değişti; form korundu.");
            }
            if(!approve())return false;
            var current=CaptureWork();
            if(closed || !SourceAvailable() || SourceRevision()!=sourceRevision || CurrentSource()!=sourceIdentity || version!=editVersion || current.Unit!=before.Unit || !current.Fields.SequenceEqual(before.Fields))
                throw new InvalidOperationException("Form onay sırasında değişti; dosya uygulanmadı. Güncel çalışmayı koruyarak yeniden açın.");
            Clear();agreement.IsChecked=false;
            loadingWork=true;
            try { unit.SelectedIndex=draft.Unit switch { "cm"=>0,"mm"=>1,_=>-1 };committedUnit=unit.SelectedIndex;
                for(int i=0;i<fields.Count;i++)fields[i].Text=draft.Fields[i]; }
            finally { loadingWork=false; }
            Clear();RefreshProposal();
            workOrigin=draft.SourceHash;durableWork=CaptureWork();
            status.Text="Çalışma alanları açıldı; birim dönüştürülmedi, kaynak istek değiştirilmedi. Kayıtlı değerler onaysızdır. Mevcut istekle çelişkileri, simetriyi ve cm/mm özetini yeniden kontrol edin. Çizim ve çıktı onaylanmadı."+(draft.SourceHash is null ? " Eski dosyada kaynak kimliği yok; hangi isteğe ait olduğu doğrulanamaz." : " Dosya kaynak kimliği: "+draft.SourceHash+"; mevcut istek kimliği: "+sourceIdentity+".");
            return true;
        };
        loadSelectedWork=(selectPath,approve)=>
        {
            var before=CaptureWork();var version=editVersion;var source=CurrentSource();var sourceRevision=SourceRevision();
            var path=selectPath();if(path is null)return false;
            if(closed || version!=editVersion || !SourceAvailable() || sourceRevision!=SourceRevision() || source!=CurrentSource() || source!=sourceIdentity || !SameWork(before,CaptureWork()))
                throw new InvalidOperationException("Dosya seçilirken form veya kaynak istek değişti; form korundu.");
            return loadWork(path,approve);
        };
        var draftTools=new WrapPanel { Margin=new Thickness(0,0,0,6) };
        var saveDraft=new Button { Content="Ölçü çalışmasını kaydet…",Padding=new Thickness(8),Margin=new Thickness(0,0,8,0) };
        var openDraft=new Button { Content="Ölçü çalışmasını aç…",Padding=new Thickness(8) };
        draftTools.Children.Add(saveDraft);draftTools.Children.Add(openDraft);body.Children.Insert(0,draftTools);
        body.Children.Insert(1,sourceLabel);
        body.Children.Insert(2,new TextBlock { Text="Çalışma dosyası 11 alanı ve birimi açık metin olarak içerir; kişisel ölçüler şifrelenmez. Yalnız Kaydet ile yeni dosya oluşturulur; mevcut dosyanın üzerine yazılmaz. Bu dosya kalıp, istek veya üretim onayı değildir. Açma, bu formdaki alanları değiştirir; ana sohbet ve kaynak istek korunur.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,8) });
        saveDraft.Click+=(_,_)=>TrySaveWork(()=>
        {
            var dialog=new SaveFileDialog { Title="A kesim ölçü çalışması · yeni açık metin dosyası",Filter="A kesim çalışma taslağı|*"+ALineWorkDraftStorage.Extension,
                FileName="A kesim çalışma"+ALineWorkDraftStorage.Extension,DefaultExt=ALineWorkDraftStorage.Extension,AddExtension=true,OverwritePrompt=false };
            return dialog.ShowDialog(this)==true ? dialog.FileName : null;
        });
        openDraft.Click+=(_,_)=>TryOpenWork(()=>
        {
            var dialog=new OpenFileDialog { Title="A kesim ölçü çalışmasını aç",Filter="A kesim çalışma taslağı|*"+ALineWorkDraftStorage.Extension,CheckFileExists=true,Multiselect=false };
            return dialog.ShowDialog(this)==true ? dialog.FileName : null;
        },()=>MessageBox.Show(this,"Dosyadaki 11 alan ve birim bu formun mevcut değerlerinin yerini alacak. Kaydetmediğiniz alanlar kaybolabilir. Ana istek değişmeyecek; simetri ve ölçü onayları sıfırlanacak. Devam edilsin mi?","Çalışma alanlarını değiştir",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)==MessageBoxResult.Yes);
    }
}
