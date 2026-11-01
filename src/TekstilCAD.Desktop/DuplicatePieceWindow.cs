using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using TekstilCAD.Core;

namespace TekstilCAD.Desktop;

internal readonly record struct DuplicateOptions(bool CopyMetadata,bool CopyGrainline,bool CopyGrading=false);

internal sealed class DuplicatePieceWindow : Window
{
    internal CheckBox CopyMetadata { get; } = new() { Content="Rol, beden, malzeme ve adedi de kopyala",IsChecked=false,Margin=new Thickness(0,8,0,4) };
    internal CheckBox CopyGrainline { get; } = new() { Content="İplik yönünü de kopyala",IsChecked=false,Margin=new Thickness(0,8,0,4) };
    internal CheckBox CopyGrading { get; } = new() { Content="Serileme kuralını da kopyala",IsChecked=false,Margin=new Thickness(0,8,0,4) };
    internal DuplicateOptions Options => new(CopyMetadata.IsChecked==true,CopyGrainline.IsChecked==true,CopyGrading.IsChecked==true);
    internal DuplicatePieceWindow(Window owner,PatternPiece source,string shownName,GradingRuleSet? rule=null)
    {
        Owner=owner;Title="Parçayı çoğalt";Width=640;Height=510;MinWidth=460;MinHeight=380;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;StudioTheme.Apply(this);
        var root=new DockPanel { Margin=new Thickness(16) };Content=root;
        var actions=new WrapPanel { HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,8,0,0) };
        var cancel=new Button { Content="Vazgeç",IsCancel=true,IsDefault=true,Padding=new Thickness(12,8,12,8),Margin=new Thickness(0,0,8,0) };
        var accept=new Button { Content="Parçayı çoğalt",Padding=new Thickness(12,8,12,8) };
        cancel.Click+=(_,_)=>DialogResult=false;accept.Click+=(_,_)=>DialogResult=true;
        actions.Children.Add(cancel);actions.Children.Add(accept);DockPanel.SetDock(actions,Dock.Bottom);root.Children.Add(actions);
        var panel=new StackPanel();var viewer=new ScrollViewer { Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Focusable=true };
        KeyboardNavigation.SetIsTabStop(viewer,true);AutomationProperties.SetName(viewer,"Parça çoğaltma seçenekleri");root.Children.Add(viewer);
        panel.Children.Add(new TextBlock { Text=$"‘{shownName}’ · {source.Id:D}",TextWrapping=TextWrapping.Wrap,FontWeight=FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text="Geometri yeni kimlikle aynı konuma kopyalanır; iki parça üst üste görünür. Kaynak değişmez. Pastal yerleşimi ve kaynak geçmişi yeni parçaya aktarılmaz.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,4) });
        if(source.Notches is { Count:>0 })
            panel.Children.Add(new TextBlock { Text=$"{source.Notches.Count} köşe işareti de aynı köşelere bağımsız olarak kopyalanır. İşaretler kesim derinliği veya makine çıktısı değildir.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,4,0,4) });
        CopyMetadata.IsEnabled=source.Metadata is not null;CopyGrainline.IsEnabled=source.Grainline is not null;
        AutomationProperties.SetName(CopyMetadata,"Rol beden malzeme ve adet bilgisini kopyala");AutomationProperties.SetName(CopyGrainline,"İplik yönünü kopyala");
        panel.Children.Add(CopyMetadata);
        panel.Children.Add(new TextBlock { Text=source.Metadata is null ? "Kaynakta üretim bilgisi yok." : "Adet de yeni parçaya kopyalanır; toplam üretim miktarını kontrol edin.",TextWrapping=TextWrapping.Wrap,Foreground=StudioTheme.Warning });
        panel.Children.Add(CopyGrainline);
        panel.Children.Add(new TextBlock { Text=source.Grainline is null ? "Kaynakta iplik yönü yok." : "İplik yönü aynı koordinatlarla ve mevcut inceleme durumuyla kopyalanır; kumaş yönünü kontrol edin.",TextWrapping=TextWrapping.Wrap,Foreground=StudioTheme.Muted });
        CopyGrading.IsEnabled=RuleMatches(source,rule);
        AutomationProperties.SetName(CopyGrading,"Serileme kuralını kopyala");panel.Children.Add(CopyGrading);
        panel.Children.Add(new TextBlock { Text=rule is null ? "Kaynakta serileme kuralı yok." : CopyGrading.IsEnabled ? "Kural bağımsız olarak yeni parçanın kimliğine bağlanır; beden artışlarını kontrol edin." : "Kaynak geometri kuralın kaydedildiği geometriyle tam eşleşmiyor; kural kopyalanamaz.",TextWrapping=TextWrapping.Wrap,Foreground=StudioTheme.Warning });
    }
    internal static bool RuleMatches(PatternPiece source,GradingRuleSet? rule) => rule is not null && rule.SourcePieceId==source.Id &&
        rule.SourceClosed==source.Closed && rule.SourcePoints is not null && rule.Deltas is not null && source.Points is not null &&
        rule.SourcePoints.Count==rule.Deltas.Count && rule.SourcePoints.SequenceEqual(source.Points);
}
