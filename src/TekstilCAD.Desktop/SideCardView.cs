using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using TekstilCAD.Core;
using TekstilCAD.Core.Guide;

namespace TekstilCAD.Desktop;

/// <summary>
/// The side card of the simple view: "Sıradaki adım", the four start tasks of an empty project, the selected piece
/// (name, size, position as tiles that fill the command box) and the "İlk adımlar" list. It only shows what it is
/// given and reports clicks; the window decides what a click does.
/// </summary>
internal sealed class SideCardView : Border
{
    private readonly Action<string> fillBox;
    private readonly Action<StartTask> startTask;
    private readonly Action dismissFirstSteps;
    private readonly TextBlock nextStep = new(), pieceName = new(), noSelection = new(), stepsHeading = new();
    private readonly StackPanel startPanel = new(), piecePanel = new(), stepsPanel = new(), stepsList = new();
    private readonly Button widthTile = Tile(), heightTile = Tile(), positionTile = Tile(), dismiss = new();
    private string startKey = "";

    internal SideCardView(Action<string> fillBox, Action<StartTask> startTask, Action dismissFirstSteps)
    {
        this.fillBox = fillBox; this.startTask = startTask; this.dismissFirstSteps = dismissFirstSteps;
        var stack = new StackPanel();
        // Next step: not a live region (the command bar owns the live row); focusable so it can be read by keyboard.
        nextStep.FontSize = 14; nextStep.TextWrapping = TextWrapping.Wrap; nextStep.Foreground = StudioTheme.Text; nextStep.Margin = new Thickness(0, 4, 0, 0); nextStep.Focusable = true;
        AutomationProperties.SetName(nextStep, "Sıradaki adım");
        stack.Children.Add(new Border
        {
            Background = StudioTheme.AccentSoft, CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 10, 12, 10),
            Child = new StackPanel { Children = { new TextBlock { Text = "Sıradaki adım", FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = StudioTheme.Accent }, nextStep } },
        });

        startPanel.Margin = new Thickness(0, 14, 0, 0);
        stack.Children.Add(startPanel);

        noSelection.FontSize = 14; noSelection.Foreground = StudioTheme.Muted; noSelection.TextWrapping = TextWrapping.Wrap; noSelection.Margin = new Thickness(2, 14, 0, 0);
        stack.Children.Add(noSelection);

        piecePanel.Margin = new Thickness(0, 14, 0, 0);
        pieceName.FontSize = 16; pieceName.FontWeight = FontWeights.SemiBold; pieceName.Foreground = StudioTheme.Text; pieceName.TextTrimming = TextTrimming.CharacterEllipsis; pieceName.Focusable = true;
        piecePanel.Children.Add(pieceName);
        var tiles = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        tiles.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        tiles.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        tiles.RowDefinitions.Add(new()); tiles.RowDefinitions.Add(new());
        Grid.SetColumn(heightTile, 1); Grid.SetRow(positionTile, 1); Grid.SetColumnSpan(positionTile, 2);
        widthTile.Margin = new Thickness(0, 0, 4, 8); heightTile.Margin = new Thickness(4, 0, 0, 8); positionTile.Margin = new Thickness(0);
        tiles.Children.Add(widthTile); tiles.Children.Add(heightTile); tiles.Children.Add(positionTile);
        piecePanel.Children.Add(tiles);
        stack.Children.Add(piecePanel);

        stepsPanel.Margin = new Thickness(0, 14, 0, 0);
        stepsHeading.FontSize = 14; stepsHeading.FontWeight = FontWeights.SemiBold; stepsHeading.Foreground = StudioTheme.Text; stepsHeading.Focusable = true;
        stepsPanel.Children.Add(stepsHeading);
        stepsPanel.Children.Add(stepsList);
        dismiss.Content = FirstStepsTracker.DismissLabel; dismiss.MinHeight = 32; dismiss.Padding = new Thickness(12, 3, 12, 3); dismiss.Margin = new Thickness(0, 6, 0, 0);
        dismiss.HorizontalAlignment = HorizontalAlignment.Left; dismiss.FontSize = 12; dismiss.Cursor = Cursors.Hand;
        AutomationProperties.SetName(dismiss, "İlk adımlar listesini gizle");
        dismiss.Click += (_, _) => this.dismissFirstSteps();
        stepsPanel.Children.Add(dismiss);
        stack.Children.Add(stepsPanel);

        Margin = new Thickness(0, 0, 0, 12); Child = stack;
        AutomationProperties.SetName(this, "Sıradaki adım ve seçili parça");
    }

    private static Button Tile() => new()
    {
        MinHeight = 48, Padding = new Thickness(12, 6, 12, 6), HorizontalContentAlignment = HorizontalAlignment.Left,
        HorizontalAlignment = HorizontalAlignment.Stretch, Cursor = Cursors.Hand,
    };

    internal string NextStepText => nextStep.Text;
    internal IReadOnlyList<Button> TaskButtons => startPanel.Children.OfType<Button>().ToList();
    internal bool StartTasksVisible => startPanel.Visibility == Visibility.Visible;
    internal bool PieceVisible => piecePanel.Visibility == Visibility.Visible;
    internal string StepsHeadingText => stepsHeading.Text;
    internal bool FirstStepsVisible => stepsPanel.Visibility == Visibility.Visible;
    internal Button WidthTile => widthTile;
    internal Button HeightTile => heightTile;
    internal Button PositionTile => positionTile;
    internal string PieceNameText => pieceName.Text;

    internal void Update(GuideStep step, IReadOnlyList<StartTask> tasks, PatternPiece? selected, int pieceCount, FirstStepsTracker steps)
    {
        nextStep.Text = step.Sentence;
        ShowTasks(tasks);
        startPanel.Visibility = tasks.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        noSelection.Text = pieceCount == 0 ? "" : $"Projede {pieceCount} parça var. Seçili parça yok.";
        noSelection.Visibility = pieceCount > 0 && selected is null ? Visibility.Visible : Visibility.Collapsed;
        piecePanel.Visibility = selected is null ? Visibility.Collapsed : Visibility.Visible;
        if (selected is not null)
        {
            var bounds = Geometry.Bounds(selected.Points);
            var width = MeasurementInput.Format(bounds.MaxX - bounds.MinX); var height = MeasurementInput.Format(bounds.MaxY - bounds.MinY);
            var left = MeasurementInput.Format(bounds.MinX); var top = MeasurementInput.Format(bounds.MinY);
            pieceName.Text = MainWindow.DisplayName(selected.Name); pieceName.ToolTip = pieceName.Text;
            SetTile(widthTile, "Genişlik", width + " mm", "genişliği " + width + " yap");
            SetTile(heightTile, "Yükseklik", height + " mm", "boyu " + height + " yap");
            SetTile(positionTile, "Konum", "soldan " + left + " mm, yukarıdan " + top + " mm", "25 sağa taşı");
        }

        stepsPanel.Visibility = steps.IsVisible ? Visibility.Visible : Visibility.Collapsed;
        if (steps.IsVisible)
        {
            stepsHeading.Text = steps.Heading;
            AutomationProperties.SetName(stepsHeading, steps.Heading);
            stepsList.Children.Clear();
            for (var i = 0; i < FirstStepsTracker.Total; i++)
            {
                var done = steps.IsDone((FirstStep)i); var label = FirstStepsTracker.LabelOf((FirstStep)i);
                var line = new TextBlock
                {
                    Text = (done ? "✓ " : "○ ") + label, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 3, 0, 0),
                    Foreground = done ? StudioTheme.Success : StudioTheme.Muted, Focusable = true,
                };
                AutomationProperties.SetName(line, (done ? "Tamamlandı: " : "Bekliyor: ") + label);
                stepsList.Children.Add(line);
            }
        }
    }

    private void ShowTasks(IReadOnlyList<StartTask> tasks)
    {
        var key = string.Join('|', tasks.Select(t => $"{t.Id}/{t.Title}/{t.Description}/{t.Enabled}"));
        if (key == startKey) return;
        startKey = key;
        startPanel.Children.Clear();
        if (tasks.Count == 0) return;
        startPanel.Children.Add(new TextBlock { Text = StartTasks.Heading, FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = StudioTheme.Text, Margin = new Thickness(0, 0, 0, 8), Focusable = true });
        foreach (var task in tasks)
        {
            var content = new StackPanel();
            content.Children.Add(new TextBlock { Text = task.Title, FontSize = 14, FontWeight = FontWeights.SemiBold });
            content.Children.Add(new TextBlock { Text = task.Description, FontSize = 12, Foreground = StudioTheme.Muted, TextTrimming = TextTrimming.CharacterEllipsis });
            if (!task.Enabled && task.DisabledReason is not null)
                content.Children.Add(new TextBlock { Text = task.DisabledReason, FontSize = 12, Foreground = StudioTheme.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
            var button = new Button
            {
                Content = content, MinHeight = 48, Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 0, 8), HorizontalContentAlignment = HorizontalAlignment.Left,
                HorizontalAlignment = HorizontalAlignment.Stretch, IsEnabled = task.Enabled, Cursor = Cursors.Hand, Tag = task,
            };
            AutomationProperties.SetName(button, task.Title);
            AutomationProperties.SetHelpText(button, task.Enabled ? task.Description : task.DisabledReason ?? task.Description);
            button.ToolTip = task.Enabled ? task.Description : task.DisabledReason;
            button.Click += (_, _) => startTask(task);
            startPanel.Children.Add(button);
        }
    }

    private void SetTile(Button tile, string label, string value, string typed)
    {
        tile.Content = new StackPanel { Children =
        {
            new TextBlock { Text = label, FontSize = 12, Foreground = StudioTheme.Muted },
            new TextBlock { Text = value, FontSize = 16, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap },
        } };
        AutomationProperties.SetName(tile, label + " " + value + "; komut kutusuna yazılır: " + typed);
        tile.ToolTip = "Komut kutusuna şunu yazar: " + typed;
        tile.Tag = typed;
        tile.Click -= TileClicked; tile.Click += TileClicked;
    }

    private void TileClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string typed }) fillBox(typed);
    }
}
