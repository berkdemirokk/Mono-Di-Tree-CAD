using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Desktop;

/// <summary>Read-only review of the exact layer plan before any export write.</summary>
internal sealed class DxfLayerReviewWindow : Window
{
    internal DataGrid MappingGrid { get; } = new();
    internal Button CancelButton { get; } = new() { Content="Vazgeç", IsCancel=true, IsDefault=true, Padding=new Thickness(14,8,14,8), Margin=new Thickness(0,0,8,0) };
    internal Button ExportButton { get; } = new() { Content="DXF dışa aktar", Padding=new Thickness(14,8,14,8) };
    private sealed record Row(string Original, string Layer, string State, string PieceId);

    internal DxfLayerReviewWindow(MainWindow owner, IReadOnlyList<DxfLayerMapping> mappings)
    {
        Owner=owner; Title="DXF katman eşlemesi"; Width=850; Height=600; MinWidth=500; MinHeight=380;
        WindowStartupLocation=WindowStartupLocation.CenterOwner; StudioTheme.Apply(this);
        var root=new DockPanel { Margin=new Thickness(18) }; Content=root;
        var info=new TextBlock { Text=$"{mappings.Count} parça · {mappings.Count(m=>m.Changed)} ad değişiyor. Projedeki adlar korunur; DXF dosyasındaki tüm katmanları aşağıda inceleyin.", TextWrapping=TextWrapping.Wrap, Margin=new Thickness(0,0,0,12) };
        DockPanel.SetDock(info,Dock.Top); root.Children.Add(info);
        var buttons=new StackPanel { Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0) };
        CancelButton.Click+=(_,_)=>DialogResult=false; ExportButton.Click+=(_,_)=>DialogResult=true;
        buttons.Children.Add(CancelButton); buttons.Children.Add(ExportButton); DockPanel.SetDock(buttons,Dock.Bottom);root.Children.Add(buttons);
        // Full selected values remain readable and copyable without relying on hover.
        var details=new Grid { Margin=new Thickness(0,10,0,0) };
        details.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(1,GridUnitType.Star) });
        details.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(1,GridUnitType.Star) });
        details.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(1,GridUnitType.Star) });
        static TextBox AddDetail(Grid parent,string label,int column)
        {
            var section=new StackPanel { Margin=new Thickness(column==0?0:8,0,0,0) };
            section.Children.Add(new TextBlock { Text=column switch { 0=>"Parça adı",1=>"DXF katman adı",_=>"Parça kimliği" },TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,3) });
            var field=new TextBox { IsReadOnly=true,TextWrapping=TextWrapping.Wrap,AcceptsReturn=true,
                MinHeight=30,MaxHeight=52,VerticalScrollBarVisibility=ScrollBarVisibility.Auto };
            AutomationProperties.SetName(field,label);section.Children.Add(field);
            Grid.SetColumn(section,column);parent.Children.Add(section);return field;
        }
        var originalDetail=AddDetail(details,"Seçili parçanın tam adı",0);
        var layerDetail=AddDetail(details,"Seçili DXF katmanının tam adı",1);
        var identityDetail=AddDetail(details,"Seçili parçanın kimliği",2);
        AutomationProperties.SetHelpText(identityDetail,"Aynı adlı parçaları ayırt etmek için seçili parçanın tam kimliğini kullanın; seçip kopyalayabilirsiniz.");
        DockPanel.SetDock(details,Dock.Bottom);root.Children.Add(details);
        MappingGrid.IsReadOnly=true; MappingGrid.AutoGenerateColumns=false; MappingGrid.CanUserAddRows=false;
        MappingGrid.EnableRowVirtualization=true; MappingGrid.EnableColumnVirtualization=true;
        VirtualizingPanel.SetIsVirtualizing(MappingGrid,true); VirtualizingPanel.SetVirtualizationMode(MappingGrid,VirtualizationMode.Recycling);
        ScrollViewer.SetCanContentScroll(MappingGrid,true);
        static Style NameStyle(string property)
        {
            var style=new Style(typeof(TextBlock));
            style.Setters.Add(new Setter(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis));
            style.Setters.Add(new Setter(FrameworkElement.ToolTipProperty,new Binding(property)));
            style.Setters.Add(new Setter(AutomationProperties.NameProperty,new Binding(property)));
            return style;
        }
        MappingGrid.Columns.Add(new DataGridTextColumn { Header="Projedeki ad", Binding=new Binding(nameof(Row.Original)), ElementStyle=NameStyle(nameof(Row.Original)), MinWidth=110, Width=new DataGridLength(1,DataGridLengthUnitType.Star) });
        MappingGrid.Columns.Add(new DataGridTextColumn { Header="DXF katmanı", Binding=new Binding(nameof(Row.Layer)), ElementStyle=NameStyle(nameof(Row.Layer)), MinWidth=110, Width=new DataGridLength(1,DataGridLengthUnitType.Star) });
        MappingGrid.Columns.Add(new DataGridTextColumn { Header="Durum", Binding=new Binding(nameof(Row.State)), Width=new DataGridLength(110) });
        MappingGrid.ItemsSource=mappings.Select(m=>new Row(SafeText(m.OriginalName),SafeText(m.LayerName),m.Changed?"Değişecek":"Aynı",m.PieceId.ToString("D"))).ToArray();
        MappingGrid.SelectionChanged+=(_,_)=>
        {
            var row=MappingGrid.SelectedItem as Row;
            originalDetail.Text=row?.Original ?? "";
            layerDetail.Text=row?.Layer ?? "";
            identityDetail.Text=row?.PieceId ?? "";
        };
        AutomationProperties.SetName(MappingGrid,"Tüm DXF katman eşlemeleri");
        AutomationProperties.SetHelpText(MappingGrid,"Satır seçildiğinde tam proje ve katman adları ile parça kimliği aşağıdaki alanlarda gösterilir. Kontrol karakterleri Unicode koduyla gösterilir.");
        root.Children.Add(MappingGrid);
    }
    private static string SafeText(string value)
    {
        var b=new StringBuilder();
        // Keep full names reviewable. Escape malformed UTF-16 and invisible controls, including bidi marks.
        for(int i=0;i<value.Length;i++)
        {
            char c=value[i];
            if(char.IsHighSurrogate(c) && i+1<value.Length && char.IsLowSurrogate(value[i+1]))
            {
                var low=value[i+1];
                var pairCategory=CharUnicodeInfo.GetUnicodeCategory(value,i);i++;
                if(pairCategory is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator)
                    b.Append($"\\u{(int)c:X4}\\u{(int)low:X4}");
                else { b.Append(c);b.Append(low); }
                continue;
            }
            var category=char.GetUnicodeCategory(c);
            if(char.IsSurrogate(c) || category is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator) b.Append($"\\u{(int)c:X4}");
            else b.Append(c);
        }
        return b.Length==0?"(boş)":b.ToString();
    }
}
