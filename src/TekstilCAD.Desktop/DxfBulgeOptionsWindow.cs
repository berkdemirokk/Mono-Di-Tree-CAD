using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using TekstilCAD.Core;

namespace TekstilCAD.Desktop;

/// <summary>Explicit opt-in for experimental DXF bulge sampling; no file or project is changed here.</summary>
internal sealed class DxfBulgeOptionsWindow : Window
{
    internal CheckBox EnableApproximation { get; }=new() { Content="Bulge yaylarını ve 2B CIRCLE dairelerini yaklaştır",IsChecked=false,Margin=new Thickness(0,10,0,6) };
    internal TextBox ToleranceInput { get; }=new() { IsEnabled=false,MaxLength=32,Margin=new Thickness(0,4,0,4),Padding=new Thickness(8) };
    internal TextBlock Error { get; }=new() { TextWrapping=TextWrapping.Wrap,Foreground=StudioTheme.Warning,Visibility=Visibility.Collapsed,Margin=new Thickness(0,4,0,0) };
    /// <summary>Null means the user accepted with approximation disabled; inspect only when ShowDialog returns true.</summary>
    internal double? SelectedToleranceMm { get; private set; }

    internal DxfBulgeOptionsWindow(Window owner)
    {
        Owner=owner;Title="DXF eğri yaklaştırma seçeneği";Width=610;Height=500;MinWidth=460;MinHeight=360;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;StudioTheme.Apply(this);
        var root=new DockPanel { Margin=new Thickness(16) };Content=root;
        var actions=new WrapPanel { HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,8,0,0) };
        var cancel=new Button { Content="Vazgeç",IsCancel=true,IsDefault=true,Padding=new Thickness(12,8,12,8),Margin=new Thickness(0,0,8,0) };
        var accept=new Button { Content="Seçimi onayla",Padding=new Thickness(12,8,12,8) };
        cancel.Click+=(_,_)=>DialogResult=false;
        accept.Click+=(_,_)=> { if(!TryAccept(out var selected))return;SelectedToleranceMm=selected;DialogResult=true; };
        actions.Children.Add(cancel);actions.Children.Add(accept);DockPanel.SetDock(actions,Dock.Bottom);root.Children.Add(actions);
        var panel=new StackPanel();var viewer=new ScrollViewer { Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Focusable=true };
        KeyboardNavigation.SetIsTabStop(viewer,true);AutomationProperties.SetName(viewer,"DXF eğri seçeneği ve açıklamalar");root.Children.Add(viewer);
        panel.Children.Add(new TextBlock { Text="DXF bulge yayları ve CIRCLE daireleri",FontSize=20,FontWeight=FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text="Seçim kapalıyken mevcut içe aktarma davranışı korunur. Açarsanız desteklenen bulge yayları ve 2B CIRCLE daireleri çizgi parçalarıyla yaklaşık gösterilir; özgün parametrik eğri saklanmaz. Hesaplama seçilen değeri nominal sapma hedefi olarak kullanır; bu değer kesin geometrik hata sınırı, fiziksel ölçüm veya üretim onayı değildir.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,4) });
        panel.Children.Add(new TextBlock { Text="Bu seçenek yalnız destek açıkça belirtilen 2B çoklu çizgiler ve model alanındaki CIRCLE kayıtları içindir. BLOCK/INSERT dönüşümleri, 3B veya doğrulanamayan düzlemler ile ARC ve diğer eğri türleri bu seçimle otomatik desteklenmez; ilgili uyarı veya ret devam eder. Kaynak çizimi ve sonuçtaki konturları ayrıca inceleyin.",TextWrapping=TextWrapping.Wrap,Foreground=StudioTheme.Warning,Margin=new Thickness(0,4,0,6) });
        AutomationProperties.SetName(EnableApproximation,"DXF bulge yayları ve CIRCLE dairelerini yaklaştırmayı etkinleştir");panel.Children.Add(EnableApproximation);
        panel.Children.Add(new TextBlock { Text="Nominal sapma hedefi (mm; sıfırdan büyük)",TextWrapping=TextWrapping.Wrap });
        AutomationProperties.SetName(ToleranceInput,"Bulge ve CIRCLE nominal sapma hedefi milimetre");
        AutomationProperties.SetHelpText(ToleranceInput,"Pozitif bir sayı girin; virgül veya nokta ondalık ayırıcıdır. Bu ayar blok koordinat toleransından ayrıdır ve üretim toleransı değildir.");
        panel.Children.Add(ToleranceInput);
        panel.Children.Add(new TextBlock { Text="Bu değer blok yerleşiminin koordinat toleransından ayrıdır. Gerekli nokta veya hesaplama sınırı aşılırsa işlem reddedilir; seçtiğiniz değer otomatik artırılmaz.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,4,0,0) });
        AutomationProperties.SetName(Error,"DXF bulge seçeneği hata bildirimi");AutomationProperties.SetLiveSetting(Error,AutomationLiveSetting.Assertive);panel.Children.Add(Error);
        EnableApproximation.Checked+=(_,_)=> { ToleranceInput.IsEnabled=true;ClearError(); };
        EnableApproximation.Unchecked+=(_,_)=> { ToleranceInput.IsEnabled=false;ClearError(); };
        ToleranceInput.TextChanged+=(_,_)=>ClearError();
    }
    internal bool TryAccept(out double? toleranceMm)
    {
        toleranceMm=null;ClearError();
        if(EnableApproximation.IsChecked!=true)return true;
        if(!MeasurementInput.TryParseMillimeters(ToleranceInput.Text,out var value) || value<=0)
        {
            Error.Text="Sıfırdan büyük, geçerli bir milimetre değeri girin; virgül veya nokta kullanabilirsiniz.";Error.Visibility=Visibility.Visible;
            AutomationProperties.SetName(Error,"DXF bulge seçeneği hata bildirimi: "+Error.Text);
            if(UIElementAutomationPeer.CreatePeerForElement(Error) is { } peer)peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
            ToleranceInput.Focus();return false;
        }
        toleranceMm=value;return true;
    }
    private void ClearError()
    {
        Error.Text="";Error.Visibility=Visibility.Collapsed;AutomationProperties.SetName(Error,"DXF bulge seçeneği hata bildirimi");
    }
}
