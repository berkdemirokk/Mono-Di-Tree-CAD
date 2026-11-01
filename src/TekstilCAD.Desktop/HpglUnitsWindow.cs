using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using TekstilCAD.Core;

namespace TekstilCAD.Desktop;

/// <summary>Selects file coordinate units only; no physical-device access or calibration.</summary>
internal sealed class HpglUnitsWindow : Window
{
    internal TextBox UnitsInput { get; } = new() { MaxLength=32,Padding=new Thickness(8),Margin=new Thickness(0,4,0,6) };
    internal TextBlock Error { get; } = new() { TextWrapping=TextWrapping.Wrap,Foreground=StudioTheme.Warning,Visibility=Visibility.Collapsed,Margin=new Thickness(0,4,0,4) };
    internal Button CancelButton { get; } = new() { Content="Vazgeç",IsCancel=true,IsDefault=true,Padding=new Thickness(12,8,12,8),Margin=new Thickness(0,0,8,0) };
    internal Button AcceptButton { get; } = new() { Content="Değeri kullan",Padding=new Thickness(12,8,12,8) };
    /// <summary>Null unless a valid value was explicitly accepted.</summary>
    internal double? SelectedUnitsPerMm { get; private set; }

    internal HpglUnitsWindow(Window owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        Owner=owner;Title="HPGL dosya ölçeği";Width=580;Height=430;MinWidth=460;MinHeight=360;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;StudioTheme.Apply(this);
        var root=new DockPanel { Margin=new Thickness(16) };Content=root;
        var actions=new WrapPanel { HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,8,0,0) };
        CancelButton.Click+=(_,_)=> { SelectedUnitsPerMm=null;DialogResult=false; };
        AcceptButton.Click+=(_,_)=> { if(!TryAccept(out var units))return;SelectedUnitsPerMm=units;DialogResult=true; };
        actions.Children.Add(CancelButton);actions.Children.Add(AcceptButton);DockPanel.SetDock(actions,Dock.Bottom);root.Children.Add(actions);
        AutomationProperties.SetName(Error,"HPGL birim değeri hatası");AutomationProperties.SetLiveSetting(Error,AutomationLiveSetting.Assertive);
        DockPanel.SetDock(Error,Dock.Bottom);root.Children.Add(Error);
        var panel=new StackPanel();var viewer=new ScrollViewer { Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Focusable=true };
        KeyboardNavigation.SetIsTabStop(viewer,true);AutomationProperties.SetName(viewer,"HPGL dosya ölçeği açıklamaları");root.Children.Add(viewer);
        panel.Children.Add(new TextBlock { Text="HPGL birim değerini girin",FontSize=20,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap });
        panel.Children.Add(new Label { Content="HPGL birim / mm",Target=UnitsInput,Foreground=StudioTheme.Text,Padding=new Thickness(0,8,0,0) });
        AutomationProperties.SetName(UnitsInput,"HPGL birim / milimetre");
        AutomationProperties.SetHelpText(UnitsInput,"Hedef için bilinen pozitif değeri girin. Ondalık ayırıcı virgül veya nokta olabilir; 40 yalnızca bir örnektir.");
        panel.Children.Add(UnitsInput);
        panel.Children.Add(new TextBlock { Text="0'dan büyük, en fazla 10000 değer girin. 40 birim/mm yalnızca bir örnektir; hedef cihazınız için doğru değer olduğu varsayılmaz.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,2,0,8) });
        panel.Children.Add(new TextBlock { Text="Bu değer milimetreleri dosyadaki tam sayı koordinatlarına dönüştürür; koordinatlar yuvarlanır ve küçük ayrıntılar değişebilir. Dosya koordinat adımı 1 / değer mm'dir; bu fiziksel makine hassasiyeti değildir.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,8) });
        panel.Children.Add(new TextBlock { Text="Fiziksel makine uyumluluğu ve kalibrasyonu doğrulanmadı. Bu işlem cihaz bağlantısı kurmaz veya cihaza iş göndermez; hedef ayarını ve çıktıyı ayrıca kontrol edin.",TextWrapping=TextWrapping.Wrap,Foreground=StudioTheme.Warning });
        UnitsInput.TextChanged+=(_,_)=>ClearError();
    }

    internal static bool TryParseUnits(string? text,out double units)
    {
        units=0;
        if(!MeasurementInput.TryParseMillimeters(text,out var value) || !IsValidUnits(value))return false;
        units=value;return true;
    }
    internal static bool IsValidUnits(double? units)=>units is double value && double.IsFinite(value) && value>0 && value<=10_000 && double.IsFinite(1d/value);

    /// <summary>Validates without accepting or closing, for accessibility and headless verification.</summary>
    internal bool TryAccept(out double units)
    {
        ClearError();
        if(TryParseUnits(UnitsInput.Text,out units))return true;
        Error.Text="Sıfırdan büyük ve en fazla 10000 olan geçerli bir değer girin; mevcut giriş korundu.";Error.Visibility=Visibility.Visible;
        AutomationProperties.SetName(Error,"HPGL birim değeri hatası: "+Error.Text);
        if(UIElementAutomationPeer.CreatePeerForElement(Error) is { } peer)peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        UnitsInput.Focus();return false;
    }

    private void ClearError()
    {
        Error.Text="";Error.Visibility=Visibility.Collapsed;AutomationProperties.SetName(Error,"HPGL birim değeri hatası");
    }
}
