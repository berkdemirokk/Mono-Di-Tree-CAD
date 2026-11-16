using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace TekstilCAD.Desktop;

/// <summary>
/// Stand-in for the real command bar: a box with an example placeholder and example chips that fill the box. It applies
/// nothing and says so; it exists only until the command bar view is merged behind <see cref="ICommandBarHost"/>.
/// </summary>
internal sealed class PlaceholderCommandBar : ICommandBarHost
{
    private readonly MainWindow owner;
    private readonly Grid root = new();
    private readonly TextBox box = new();
    private readonly TextBlock placeholder = new();
    private readonly StackPanel chips = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
    private string chipKey = "";

    internal PlaceholderCommandBar(MainWindow owner)
    {
        this.owner = owner;
        root.RowDefinitions.Add(new() { Height = new GridLength(52) });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        box.Height = 52; box.FontSize = 18; box.MaxLength = 200; box.Padding = new Thickness(14, 0, 14, 0);
        box.VerticalContentAlignment = VerticalAlignment.Center;
        box.Background = StudioTheme.Input; box.BorderBrush = StudioTheme.BorderStrong;
        AutomationProperties.SetName(box, "Komut kutusu");
        AutomationProperties.SetHelpText(box, "Yapmak istediğinizi yazın, örneğin 120 x 80 dikdörtgen. Komut kutusu bu sürümde henüz bağlı değil; yazdıklarınız hiçbir şeyi değiştirmez.");
        placeholder.Text = "Ne yapmak istiyorsunuz? Örnek: 120 x 80 dikdörtgen";
        placeholder.FontSize = 18; placeholder.Foreground = StudioTheme.Placeholder; placeholder.IsHitTestVisible = false;
        placeholder.VerticalAlignment = VerticalAlignment.Center; placeholder.Margin = new Thickness(16, 0, 16, 0);
        placeholder.TextTrimming = TextTrimming.CharacterEllipsis;
        box.TextChanged += (_, _) => placeholder.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        box.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            owner.SetStatus("Komut kutusu bu sürümde henüz çalışmıyor · Çizim araçlarını, Aç ve Kaydet düğmelerini kullanın.");
            e.Handled = true;
        };
        root.Children.Add(box); root.Children.Add(placeholder);
        Grid.SetRow(chips, 1); root.Children.Add(chips);
        OnRefreshed();
    }

    public FrameworkElement Element => root;
    public void FocusBox() => box.Focus();
    public void SetText(string text) { box.Text = text; box.CaretIndex = text.Length; box.Focus(); }

    public void OnRefreshed()
    {
        string[] texts = owner.Selected is { Points.Count: > 0 }
            ? ["genişliği 95 yap", "25 sağa taşı", "90 sağa döndür"]
            : owner.Document.Pieces.Count == 0 ? ["120 x 80 dikdörtgen", "150 çizgi", "Dosya aç"] : ["Dosya aç"];
        var key = string.Join('|', texts);
        if (key == chipKey) return;
        chipKey = key;
        chips.Children.Clear();
        chips.Children.Add(new TextBlock { Text = "Deneyin", FontSize = 13, Foreground = StudioTheme.Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 10, 0) });
        foreach (var text in texts)
        {
            var chip = new Button { Content = text, MinHeight = 36, Padding = new Thickness(14, 4, 14, 4), Margin = new Thickness(0, 0, 8, 0), FontSize = 13, Cursor = Cursors.Hand };
            AutomationProperties.SetName(chip, "Örnek: " + text);
            var fill = text;
            chip.Click += (_, _) => SetText(fill);
            chips.Children.Add(chip);
        }
    }
}
