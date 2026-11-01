using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Desktop;

/// <summary>Read-only snapshot for manual drawing; it is not attached to the CAD document.</summary>
internal sealed class RequestNotesWindow : Window
{
    internal TextBox DescriptionForVerification { get; }
    internal TextBox MeasurementsForVerification { get; }
    internal TextBox ReferenceForVerification { get; }

    internal RequestNotesWindow(Window owner,DesignRequest request)
    {
        ArgumentNullException.ThrowIfNull(owner);DesignRequestStorage.Validate(request);
        Owner=owner;Title="Çizim için istek notları";Width=480;Height=620;MinWidth=360;MinHeight=360;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        StudioTheme.Apply(this);
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source=new Uri("DesignRequestTheme.xaml",UriKind.Relative) });
        Background=DesignRequestWindow.BackgroundColor;Foreground=DesignRequestWindow.Ink;
        var root=new DockPanel { Margin=new Thickness(14) };Content=root;
        var close=new Button { Content="Kapat",Padding=new Thickness(12,7,12,7),HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,8,0,0) };
        close.Click+=(_,_)=>Close();DockPanel.SetDock(close,Dock.Bottom);root.Children.Add(close);
        var notice=new TextBlock { Text="Açılış anındaki istek notlarıdır; otomatik güncellenmez. Çizim elle yapılır; kalıp oluşturulmadı ve ölçüler doğrulanmadı.",TextWrapping=TextWrapping.Wrap,Foreground=DesignRequestWindow.Muted,Margin=new Thickness(0,0,0,8) };
        DockPanel.SetDock(notice,Dock.Top);root.Children.Add(notice);
        var grid=new Grid();root.Children.Add(grid);
        for(var i=0;i<6;i++)grid.RowDefinitions.Add(new RowDefinition { Height=i is 1 or 3 ? new GridLength(1,GridUnitType.Star) : GridLength.Auto });
        TextBox ReadOnly(string name,string text)
        {
            var box=new TextBox { Text=text,IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,MinHeight=0,VerticalContentAlignment=VerticalAlignment.Top };
            AutomationProperties.SetName(box,name);return box;
        }
        void AddRow(string title,TextBox box,int row)
        {
            var label=new Label { Content=title,Target=box,Foreground=DesignRequestWindow.Ink,Padding=new Thickness(0,3,0,3) };
            Grid.SetRow(label,row);grid.Children.Add(label);Grid.SetRow(box,row+1);grid.Children.Add(box);
        }
        DescriptionForVerification=ReadOnly("İstek açıklaması",request.Prompt);
        MeasurementsForVerification=ReadOnly("Ölçü notları",request.Measurements);
        var reference=request.Reference;
        ReferenceForVerification=ReadOnly("Referans görsel bilgisi",reference is null ? "Görsel seçilmedi." : $"{SafeName(reference.Name)} · {reference.MediaType} · {reference.Data.Length} bayt\nGörsel gösterilmiyor veya yorumlanmıyor.");
        ReferenceForVerification.MaxHeight=64;
        AddRow("Açıklama",DescriptionForVerification,0);AddRow("Ölçü notları",MeasurementsForVerification,2);AddRow("Referans görsel bilgisi",ReferenceForVerification,4);
    }
    private static string SafeName(string name)
    {
        var result=new StringBuilder(name.Length);
        foreach(var rune in name.EnumerateRunes())
        {
            var category=Rune.GetUnicodeCategory(rune);
            if(category is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator)
                result.Append(rune.Value<=0xffff ? $"\\u{rune.Value:X4}" : $"\\U{rune.Value:X8}");
            else result.Append(rune.ToString());
        }
        return result.ToString();
    }
}
