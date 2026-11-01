using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using TekstilCAD.Core;

namespace TekstilCAD.Desktop;

/// <summary>Explicitly selects source identities for a separate DXF; it never edits the project.</summary>
internal sealed class SelectedDxfExportWindow : Window
{
    private sealed record Choice(Guid Id,string Label);
    internal ListBox Choices { get; } = new() { SelectionMode=SelectionMode.Multiple,MinHeight=180 };
    internal Button ContinueButton { get; } = new() { Content="Seçilenlerle devam et",Padding=new Thickness(12,7,12,7),IsEnabled=false };
    internal IReadOnlyList<Guid> SelectedIds => Choices.SelectedItems.Cast<Choice>().Select(choice=>choice.Id).ToArray();

    internal SelectedDxfExportWindow(Window owner,ProjectDocument snapshot)
    {
        Owner=owner;Title="DXF için kalıp seç";Width=640;Height=600;MinWidth=480;MinHeight=400;WindowStartupLocation=WindowStartupLocation.CenterOwner;StudioTheme.Apply(this);
        var root=new DockPanel { Margin=new Thickness(16) };Content=root;
        var buttons=new WrapPanel { HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,8,0,0) };
        var cancel=new Button { Content="Vazgeç",IsCancel=true,Padding=new Thickness(12,7,12,7),Margin=new Thickness(0,0,8,0) };
        cancel.Click+=(_,_)=>DialogResult=false;ContinueButton.Click+=(_,_)=>DialogResult=true;
        buttons.Children.Add(cancel);buttons.Children.Add(ContinueButton);DockPanel.SetDock(buttons,Dock.Bottom);root.Children.Add(buttons);
        var intro=new TextBlock { Text="Yalnız açıkça seçtiğiniz parçalar yeni DXF dosyasına yazılır; diğer parçalar kaynak projede kalır. Parça kimliği, üretim bilgileri, serileme kuralları ve kaynak geçmişi DXF'te korunmaz. Normal DXF köşe işareti veya iplik yönü içeren parçaları reddeder; bunlar için ayrı inceleme çıktısını kullanın.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,8) };
        DockPanel.SetDock(intro,Dock.Top);root.Children.Add(intro);
        AutomationProperties.SetName(Choices,"DXF'e aktarılacak parçaları seçin");
        AutomationProperties.SetHelpText(Choices,"Boşluk ile parçayı seçin veya seçimi kaldırın; ok tuşlarıyla listede gezinin. Her satır tam parça kimliğini içerir.");
        VirtualizingPanel.SetIsVirtualizing(Choices,true);VirtualizingPanel.SetVirtualizationMode(Choices,VirtualizationMode.Recycling);ScrollViewer.SetCanContentScroll(Choices,true);
        Choices.DisplayMemberPath=nameof(Choice.Label);Choices.ItemsSource=snapshot.Pieces.Select(p=>new Choice(p.Id,$"{p.Id:D} · {CadCanvas.CanvasPieceLabel(p.Name)}")).ToArray();
        Choices.SelectionChanged+=(_,_)=>ContinueButton.IsEnabled=Choices.SelectedItems.Count>0;
        root.Children.Add(Choices);
    }
}
