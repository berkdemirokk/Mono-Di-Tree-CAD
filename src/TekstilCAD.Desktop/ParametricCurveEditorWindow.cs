using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using TekstilCAD.Core;

namespace TekstilCAD.Desktop;

/// <summary>
/// Edit the authoritative controls of one analytic open contour. This
/// deliberately never edits the cached polyline vertices independently.
/// </summary>
internal sealed class ParametricCurveEditorWindow : Window
{
    private readonly IReadOnlyList<CurveSegment> source;
    private readonly Action<IReadOnlyList<CurveSegment>> onApply;
    private readonly List<Dictionary<string, TextBox>> editors = [];

    public ParametricCurveEditorWindow(Window owner, PatternPiece piece,
                                     Action<IReadOnlyList<CurveSegment>> onApply)
    {
        Owner = owner;
        Title = "Parametrik kontrol noktaları · " + piece.Name;
        Width = 530;
        Height = 650;
        MinWidth = 420;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        source = piece.Curves ?? throw new ArgumentException("Parça parametrik değil.", nameof(piece));
        if (source.Count is < 1 or > 32)
            throw new InvalidOperationException("Tek ekranda düzenleme en fazla 32 segment içindir; geometri korunuyor.");
        this.onApply = onApply;

        var root = new DockPanel { Margin = new Thickness(18) };
        Content = root;
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0)
        };
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        var cancel = new Button { Content = "Vazgeç", Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(4) };
        cancel.Click += (_, _) => Close();
        cancel.IsCancel = true;
        buttons.Children.Add(cancel);
        var save = new Button { Content = "Kontrol noktalarını uygula", Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(4) };
        save.Click += (_, _) => Apply();
        save.IsDefault = true;
        buttons.Children.Add(save);

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        root.Children.Add(scroll);
        var body = new StackPanel();
        scroll.Content = body;
        body.Children.Add(new TextBlock
        {
            Text = "Kontrol noktaları milimetre, yay açıları radyan cinsindendir. " +
                   "Kaydetme öncesi geometri ve nokta bütçesi doğrulanır. " +
                   "Bu işlem fabrika üretim onayı değildir.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14)
        });

        for (var i = 0; i < source.Count; i++)
        {
            var group = new GroupBox
            {
                Header = "Segment " + (i + 1) + " · " + NameOf(source[i]),
                Margin = new Thickness(0, 0, 0, 10)
            };
            body.Children.Add(group);
            var panel = new StackPanel { Margin = new Thickness(10) };
            group.Content = panel;
            var fields = new Dictionary<string, TextBox>(StringComparer.Ordinal);
            editors.Add(fields);
            foreach (var (key, value) in Values(source[i]))
            {
                var label = new TextBlock { Text = key, Margin = new Thickness(0, 5, 0, 2) };
                panel.Children.Add(label);
                var entry = new TextBox
                {
                    Text = MeasurementInput.Format(value),
                    MinHeight = 30,
                    Padding = new Thickness(6)
                };
                AutomationProperties.SetName(entry, "Segment " + (i + 1) + " " + key);
                fields.Add(key, entry);
                panel.Children.Add(entry);
            }
        }
    }

    private static string NameOf(CurveSegment curve) => curve switch
    {
        CurveSegment.Line => "Düz çizgi",
        CurveSegment.CubicBezier => "Kübik Bézier",
        CurveSegment.Arc => "Dairesel yay",
        _ => throw new InvalidDataException("Tanımlanmayan segment.")
    };

    private static IEnumerable<(string Key, double Value)> Values(CurveSegment curve)
    {
        switch (curve)
        {
            case CurveSegment.Line line:
                return Coordinates("Başlangıç", line.Start).Concat(Coordinates("Bitiş", line.End));
            case CurveSegment.CubicBezier bez:
                return Coordinates("P0", bez.P0)
                    .Concat(Coordinates("P1", bez.P1))
                    .Concat(Coordinates("P2", bez.P2))
                    .Concat(Coordinates("P3", bez.P3));
            case CurveSegment.Arc arc:
                return Coordinates("Merkez", arc.Center).Concat(new (string, double)[]
                {
                    ("Yarıçap", arc.Radius),
                    ("Başlangıç açısı (radyan)", arc.StartAngle),
                    ("Tarama açısı (radyan)", arc.SweepAngle)
                });
            default:
                throw new InvalidDataException("Bilinmeyen segment türü.");
        }
    }

    private static IEnumerable<(string Key, double Value)> Coordinates(string name, Point2 point) =>
        new (string, double)[] { (name + " X", point.X), (name + " Y", point.Y) };

    private CurveSegment ReadSegment(int index)
    {
        var fields = editors[index];
        double V(string key)
        {
            if (!MeasurementInput.TryParseMillimeters(fields[key].Text, out var value))
                throw new InvalidDataException("Geçersiz sayısal değer: segment " + (index + 1) + " · " + key);
            return value;
        }
        Point2 P(string key) => new(V(key + " X"), V(key + " Y"));
        return source[index] switch
        {
            CurveSegment.Line => new CurveSegment.Line(P("Başlangıç"), P("Bitiş")),
            CurveSegment.CubicBezier => new CurveSegment.CubicBezier(P("P0"), P("P1"), P("P2"), P("P3")),
            CurveSegment.Arc => new CurveSegment.Arc(
                P("Merkez"), V("Yarıçap"), V("Başlangıç açısı (radyan)"), V("Tarama açısı (radyan)")),
            _ => throw new InvalidDataException("Bilinmeyen segment türü.")
        };
    }

    internal void SetTextForVerification(int segmentIndex, string key, string text) => editors[segmentIndex][key].Text = text;

    internal void ApplyForVerification() => Apply(fromVerification: true);

    private void Apply(bool fromVerification = false)
    {
        try
        {
            var segments = Enumerable.Range(0, source.Count).Select(ReadSegment).ToArray();
            // Same checked sampling path used by native project persistence.
            _ = ParametricContour.Sample(segments);
            onApply(segments);
            if (!fromVerification) DialogResult = true;
        }
        catch (Exception error) when (error is ArgumentException or InvalidDataException or InvalidOperationException)
        {
            MessageBox.Show(this, error.Message, "Parametrik kontur değiştirilemedi",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
