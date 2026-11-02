using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using TekstilCAD.Core;

namespace TekstilCAD.Desktop;

internal sealed class PhotoPlaneSetupWindow : Window
{
    internal TextBox WidthInput { get; } = new() { MaxLength=32,MinWidth=120,Margin=new Thickness(0,3,0,8) };
    internal TextBox HeightInput { get; } = new() { MaxLength=32,MinWidth=120,Margin=new Thickness(0,3,0,8) };
    internal TextBlock Error { get; } = new() { TextWrapping=TextWrapping.Wrap,Foreground=StudioTheme.Warning };
    internal bool UsePlane { get; private set; }
    internal double WidthMm { get; private set; }
    internal double HeightMm { get; private set; }
    internal PhotoPlaneSetupWindow(Window owner,double? width=null,double? height=null)
    {
        Owner=owner;Title="Fotoğraf düzlem referansı";Width=560;Height=450;MinWidth=460;MinHeight=360;WindowStartupLocation=WindowStartupLocation.CenterOwner;StudioTheme.Apply(this);
        if(width is double w)WidthInput.Text=MeasurementInput.Format(w);if(height is double h)HeightInput.Text=MeasurementInput.Format(h);
        var root=new DockPanel { Margin=new Thickness(16) };Content=root;
        var actions=new WrapPanel { Margin=new Thickness(0,8,0,0) };DockPanel.SetDock(actions,Dock.Bottom);root.Children.Add(actions);
        var accept=new Button { Content="Düzlemi kullan",Padding=new Thickness(9,6,9,6),Margin=new Thickness(0,0,6,4) };
        var uniform=new Button { Content="Tek mesafeye dön",Padding=new Thickness(9,6,9,6),Margin=new Thickness(0,0,6,4) };
        var cancel=new Button { Content="Vazgeç",IsCancel=true,IsDefault=true,Padding=new Thickness(9,6,9,6) };actions.Children.Add(accept);actions.Children.Add(uniform);actions.Children.Add(cancel);
        var viewer=new ScrollViewer { VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled };root.Children.Add(viewer);
        var body=new StackPanel();viewer.Content=body;
        body.Children.Add(new TextBlock { Text="Çizilecek alanı çevreleyen, gerçek eni ve boyu bilinen düz bir dikdörtgen kullanın. Dört köşeyi sırayla seçin: sol üst, sağ üst, sağ alt, sol alt. Dörtgen dışındaki çizgi noktaları reddedilir. Lens, kumaş dökümü ve görünmeyen parçalar düzeltilmez.",TextWrapping=TextWrapping.Wrap });
        body.Children.Add(new Label { Content="Referans eni (mm)",Target=WidthInput,Foreground=StudioTheme.Text });body.Children.Add(WidthInput);
        body.Children.Add(new Label { Content="Referans boyu (mm)",Target=HeightInput,Foreground=StudioTheme.Text });body.Children.Add(HeightInput);
        AutomationProperties.SetName(WidthInput,"Düzlem referans eni milimetre");AutomationProperties.SetName(HeightInput,"Düzlem referans boyu milimetre");
        AutomationProperties.SetLiveSetting(Error,AutomationLiveSetting.Assertive);body.Children.Add(Error);
        accept.Click+=(_,_)=> { if(!TryRead(out var widthMm,out var heightMm))return;WidthMm=widthMm;HeightMm=heightMm;UsePlane=true;DialogResult=true; };
        uniform.Click+=(_,_)=> { UsePlane=false;DialogResult=true; };cancel.Click+=(_,_)=>DialogResult=false;
    }
    internal bool TryRead(out double width,out double height)
    {
        width=height=0;
        if(!MeasurementInput.TryParseMillimeters(WidthInput.Text,out var w) || !MeasurementInput.TryParseMillimeters(HeightInput.Text,out var h) ||
           w<=0 || h<=0 || w>Geometry.MaxCoordinate || h>Geometry.MaxCoordinate)
        { Error.Text="En ve boy için pozitif, en fazla 100000000 mm geçerli değerler girin.";return false; }
        width=w;height=h;Error.Text="";return true;
    }
}
