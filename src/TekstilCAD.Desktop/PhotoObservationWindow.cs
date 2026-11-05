using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TekstilCAD.Desktop;

/// <summary>Manual visual observations only; no image analysis or measurement.</summary>
internal sealed class PhotoObservationWindow : Window
{
    internal readonly ComboBox Silhouette=new() { ItemsSource=new[]{"Bilinmiyor / görünmüyor","A kesim","Düz","Bedene oturan"},SelectedIndex=0 };
    internal readonly ComboBox Sleeve=new() { ItemsSource=new[]{"Bilinmiyor / görünmüyor","Kolsuz","Kısa kollu","Uzun kollu"},SelectedIndex=0 };
    internal readonly ComboBox Neck=new() { ItemsSource=new[]{"Bilinmiyor / görünmüyor","Yuvarlak yaka","V yaka","Yakalı"},SelectedIndex=0 };
    internal readonly CheckBox Confirm=new() { Content=new TextBlock { Text="Bunlar kendi gözlemlerimdir; mevcut tarif ve seçili modelle olası çelişkileri kendim karşılaştırdım. Otomatik karşılaştırma yapılmaz.",TextWrapping=TextWrapping.Wrap },Margin=new Thickness(0,8,0,8) };
    internal readonly TextBlock Status=new() { TextWrapping=TextWrapping.Wrap };
    internal readonly TextBox ExistingDescription=new() { IsReadOnly=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,MinHeight=70,MaxHeight=140,Margin=new Thickness(0,0,0,8) };
    internal ScrollViewer ControlsForVerification { get; private set; }=null!;
    internal Image PhotoForVerification { get; private set; }=null!;
    internal IReadOnlyList<string>? Approved { get; private set; }
    internal string? ApprovedDescription { get; private set; }
    internal Func<string,bool>? ConfirmEditForVerification { get; set; }
    private long selectionRevision;
    private readonly string originalDescription;
    private readonly int maximumDescriptionLength;
    private readonly Func<bool>? sourceCurrent;
    internal PhotoObservationWindow(Window owner,BitmapSource image,string model,string descriptionText,int maxLength=TekstilCAD.FileFormats.DesignRequestStorage.MaximumTextLength,Func<bool>? isSourceCurrent=null)
    {
        originalDescription=descriptionText;maximumDescriptionLength=maxLength;sourceCurrent=isSourceCurrent;
        Owner=owner;Title="Fotoğrafa bakarak özellikleri belirt";Width=850;Height=650;MinWidth=500;MinHeight=450;StudioTheme.Apply(this);
        var root=new DockPanel { Margin=new Thickness(16) };Content=root;
        var description=new TextBlock { Text="Fotoğrafı inceleyin ve yalnız gördüğünüz özellikleri seçin. Bilinmeyenleri boş bırakın. Gözlemleri mevcut tarif ve seçili modelle kendiniz karşılaştırın; serbest tarifle çelişkiler otomatik bulunmaz. Bunlar kullanıcı gözlemidir; otomatik analiz, ölçü veya kalıp değildir.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,8) };DockPanel.SetDock(description,Dock.Top);root.Children.Add(description);
        var panel=new StackPanel { Margin=new Thickness(10,0,0,0) };
        var scroller=new ScrollViewer { Content=panel,Width=250,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled };ControlsForVerification=scroller;DockPanel.SetDock(scroller,Dock.Right);root.Children.Add(scroller);
        panel.Children.Add(new TextBlock { Text="Mevcut model fikri: "+model,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,8) });
        panel.Children.Add(new TextBlock { Text="Mevcut tarif",Margin=new Thickness(0,0,0,3) });ExistingDescription.Text=descriptionText;AutomationProperties.SetName(ExistingDescription,"Mevcut tarif; salt okunur");panel.Children.Add(ExistingDescription);
        void Field(string label,ComboBox box) { panel.Children.Add(new TextBlock { Text=label,Margin=new Thickness(0,6,0,3) });AutomationProperties.SetName(box,label);panel.Children.Add(box); }
        Field("Siluet",Silhouette);Field("Kol",Sleeve);Field("Yaka",Neck);panel.Children.Add(Confirm);
        foreach(var box in new[]{Silhouette,Sleeve,Neck})box.SelectionChanged+=(_,_)=>selectionRevision++;
        Confirm.Checked+=(_,_)=>selectionRevision++;Confirm.Unchecked+=(_,_)=>selectionRevision++;
        panel.Children.Add(new TextBlock { Text="Kayıtlı gözlemi düzeltme: önce yukarıdan yeni değeri seçin. Bilinmiyor önceki değeri silmez; kaldırma ayrı onay ister. Diğer seçimler bu işlemde eklenmez.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,4) });
        string[] editNames=["Siluet","Kol","Yaka"];
        for(var i=0;i<3;i++)
        {
            var field=i;var actions=new WrapPanel();panel.Children.Add(actions);
            var change=new Button { Content=editNames[i]+" değiştir…",Margin=new Thickness(0,2,4,2) };
            var remove=new Button { Content=editNames[i]+" kaldır…",Margin=new Thickness(0,2,0,2) };
            AutomationProperties.SetName(change,"Kayıtlı "+editNames[i]+" gözlemini değiştir");AutomationProperties.SetName(remove,"Kayıtlı "+editNames[i]+" gözlemini kaldır");
            actions.Children.Add(change);actions.Children.Add(remove);
            change.Click+=(_,_)=> { if(TryEdit(field,false))DialogResult=true; };
            remove.Click+=(_,_)=> { if(TryEdit(field,true))DialogResult=true; };
        }
        var apply=new Button { Content="Seçilen gözlemleri tarife ekle",Padding=new Thickness(8) };panel.Children.Add(apply);panel.Children.Add(Status);
        apply.Click+=(_,_)=> { if(TryApprove())DialogResult=true; };
        var photo=new Image { Source=image,Stretch=Stretch.Uniform };PhotoForVerification=photo;AutomationProperties.SetName(photo,"Referans fotoğrafı; otomatik yorumlanmaz");root.Children.Add(photo);
    }
    internal bool TryApprove()
    {
        Approved=null;ApprovedDescription=null;
        var observations=Build(Silhouette.SelectedIndex,Sleeve.SelectedIndex,Neck.SelectedIndex);
        if(observations.Count==0) { Status.Text="En az bir görünür özellik seçin.";return false; }
        if(Confirm.IsChecked!=true) { Status.Text="Gözlemlerin size ait olduğunu onaylayın.";return false; }
        if(sourceCurrent?.Invoke()==false) { Status.Text="İstek veya görsel değişti; seçimleriniz korunuyor. Güncel isteği kontrol etmek için bu pencereyi kapatıp yeniden açın.";return false; }
        try
        {
            var result=DesignRequestWindow.AppendPhotoObservations(originalDescription,observations,maximumDescriptionLength);
            if(result==originalDescription) { Status.Text="Seçilen gözlemlerin tamamı tarifte zaten var; tekrar eklenmedi. Yeni bir özellik seçebilir veya pencereyi kapatabilirsiniz.";return false; }
            ApprovedDescription=result;
        }
        catch(ArgumentException ex) { Status.Text=ex.Message;return false; }
        Approved=observations;return true;
    }
    internal bool TryEdit(int field,bool remove)
    {
        Approved=null;ApprovedDescription=null;
        if(Confirm.IsChecked!=true) { Status.Text="Gözlemlerin size ait olduğunu onaylayın.";return false; }
        try
        {
            if(sourceCurrent?.Invoke()==false)throw new ArgumentException("İstek veya görsel değişti; gözlem değiştirilmedi.");
            var selections=new[]{Silhouette.SelectedIndex,Sleeve.SelectedIndex,Neck.SelectedIndex};
            if(field is <0 or >2)throw new ArgumentException("Gözlem alanı geçersiz.");
            var plan=PhotoObservationEdit.Plan(originalDescription,field,selections[field],remove,maximumDescriptionLength);
            var revision=selectionRevision;
            var message="Önceki: "+plan.Previous+"\nYeni: "+(plan.Replacement??"[Gözlem kaldırılacak]")+"\nYalnız bu kayıtlı gözlem değişir. Diğer tarif, model, ölçüler ve görsel korunur. Onaylıyor musunuz?";
            var accepted=ConfirmEditForVerification?.Invoke(message) ?? MessageBox.Show(this,message,"Fotoğraf gözlemini değiştir",MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No)==MessageBoxResult.Yes;
            if(!accepted) { Status.Text="Değişiklik iptal edildi; seçimleriniz korundu.";return false; }
            if(revision!=selectionRevision || sourceCurrent?.Invoke()==false)throw new ArgumentException("İstek, görsel veya seçimler onay sırasında değişti; işlem uygulanmadı.");
            ApprovedDescription=plan.Description;return true;
        }
        catch(ArgumentException ex) { Status.Text=ex.Message;return false; }
    }
    internal static IReadOnlyList<string> Build(int silhouette,int sleeve,int neck)
    {
        string[][] values=[ ["","A kesim","Düz","Bedene oturan"],["","Kolsuz","Kısa kollu","Uzun kollu"],["","Yuvarlak yaka","V yaka","Yakalı"] ];
        int[] selections=[silhouette,sleeve,neck];string[] names=["Siluet","Kol","Yaka"];var result=new List<string>();
        for(int i=0;i<3;i++) { if(selections[i]<0 || selections[i]>=values[i].Length)throw new ArgumentOutOfRangeException(nameof(selections));if(selections[i]>0)result.Add($"Fotoğraf gözlemi · {names[i]}: {values[i][selections[i]]}"); }
        return result.AsReadOnly();
    }
}
