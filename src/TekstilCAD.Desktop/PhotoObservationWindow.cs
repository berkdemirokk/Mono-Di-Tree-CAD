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
    internal readonly CheckBox Confirm=new() { Content="Bunlar fotoğrafa bakarak kendi seçtiğim gözlemlerdir.",Margin=new Thickness(0,8,0,8) };
    internal readonly TextBlock Status=new() { TextWrapping=TextWrapping.Wrap };
    internal IReadOnlyList<string>? Approved { get; private set; }
    internal PhotoObservationWindow(Window owner,BitmapSource image,string model)
    {
        Owner=owner;Title="Fotoğrafa bakarak özellikleri belirt";Width=850;Height=650;MinWidth=500;MinHeight=450;StudioTheme.Apply(this);
        var root=new DockPanel { Margin=new Thickness(16) };Content=root;
        var description=new TextBlock { Text="Fotoğrafı inceleyin ve yalnız gördüğünüz özellikleri seçin. Bilinmeyenleri boş bırakın. Bunlar kullanıcı gözlemidir; otomatik analiz, ölçü veya kalıp değildir.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,8) };DockPanel.SetDock(description,Dock.Top);root.Children.Add(description);
        var panel=new StackPanel { Width=250,Margin=new Thickness(10,0,0,0) };DockPanel.SetDock(panel,Dock.Right);root.Children.Add(panel);
        panel.Children.Add(new TextBlock { Text="Mevcut model fikri: "+model,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,8) });
        void Field(string label,ComboBox box) { panel.Children.Add(new TextBlock { Text=label,Margin=new Thickness(0,6,0,3) });AutomationProperties.SetName(box,label);panel.Children.Add(box); }
        Field("Siluet",Silhouette);Field("Kol",Sleeve);Field("Yaka",Neck);panel.Children.Add(Confirm);
        var apply=new Button { Content="Seçilen gözlemleri tarife ekle",Padding=new Thickness(8) };panel.Children.Add(apply);panel.Children.Add(Status);
        apply.Click+=(_,_)=> { var observations=Build(Silhouette.SelectedIndex,Sleeve.SelectedIndex,Neck.SelectedIndex);if(observations.Count==0) { Status.Text="En az bir görünür özellik seçin.";return; }if(Confirm.IsChecked!=true) { Status.Text="Gözlemlerin size ait olduğunu onaylayın.";return; }Approved=observations;DialogResult=true; };
        var photo=new Image { Source=image,Stretch=Stretch.Uniform };AutomationProperties.SetName(photo,"Referans fotoğrafı; otomatik yorumlanmaz");root.Children.Add(photo);
    }
    internal static IReadOnlyList<string> Build(int silhouette,int sleeve,int neck)
    {
        string[][] values=[ ["","A kesim","Düz","Bedene oturan"],["","Kolsuz","Kısa kollu","Uzun kollu"],["","Yuvarlak yaka","V yaka","Yakalı"] ];
        int[] selections=[silhouette,sleeve,neck];string[] names=["Siluet","Kol","Yaka"];var result=new List<string>();
        for(int i=0;i<3;i++) { if(selections[i]<0 || selections[i]>=values[i].Length)throw new ArgumentOutOfRangeException(nameof(selections));if(selections[i]>0)result.Add($"Fotoğraf gözlemi · {names[i]}: {values[i][selections[i]]}"); }
        return result.AsReadOnly();
    }
}
