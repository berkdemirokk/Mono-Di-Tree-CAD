using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using TekstilCAD.Core.Commands;

namespace TekstilCAD.Desktop;

/// <summary>What the box needs to know about the window to offer examples that work right now.</summary>
internal sealed record CommandBarContext(bool EmptyProject,IReadOnlyList<string> PieceNames,bool HasSelection,bool DrawingInProgress,bool HasUnsavedWork);

/// <summary>The window side of the command box: planning, applying and history steps. The view never touches the project itself.</summary>
/// <param name="Preview">Parses and plans the text (text, true when Enter was pressed).</param>
/// <param name="Apply">Applies a shown plan (plan, confirmed).</param>
/// <param name="Context">Facts used to choose the example chips.</param>
/// <param name="HistoryStep">Undoes (false) or redoes (true) one project step and tells what happened.</param>
internal sealed record CommandBarHost(Func<string,bool,CommandPreview> Preview,Func<Plan,bool,CommandOutcome> Apply,
    Func<CommandBarContext> Context,Func<bool,string> HistoryStep,Action<GhostScene?>? ShowGhost=null);

internal enum CommandBarState { Idle, Preview, DangerPreview, Question, Refusal, Result, Voice }

/// <summary>
/// The single command box: a text box, one preview/result/refusal row, example chips and a short history. Enter first shows what
/// would happen and a second Enter applies it; typing drops the shown preview. Hosted in a Popup (Ctrl+K) in the classic layout; the
/// verification builds it in a detached container and never opens a Popup.
/// </summary>
internal sealed class CommandBarView : Border
{
    internal const string Placeholder="Ölçüyü yazın, Enter'a basın. Örnek: 12 x 8 cm dikdörtgen";
    internal const string BoxHelp="Yapmak istediğinizi yazın, örneğin 12 x 8 cm dikdörtgen. Önce ne olacağı gösterilir; Enter ile uygulanır.";
    internal const string EmptyBoxText="Bir komut yazın. Örnek: 12 x 8 cm dikdörtgen";
    internal const string StaleText="Parça değişti; önizleme yenilendi. Enter ile uygulayın.";
    internal const string UnitRule="Birim yazmazsanız ölçü milimetre sayılır; santim için 12 cm yazın. Anlaşılmayan yazı hiçbir şeyi değiştirmez.";
    internal const int HistoryLimit=20;

    private static readonly (string Group,string[] Examples)[] ExampleGroups=
    [
        ("Çiz",["12 x 8 cm dikdörtgen","kare 100","150 çizgi","80 dikey çizgi"]),
        ("Değiştir",["genişliği 95 yap","25 sağa taşı","90 sağa döndür","aynala","adı ön beden olsun"]),
        ("Dosya",["projeyi kaydet","proje aç","yeni"]),
        ("Görünüm ve geri alma",["tümünü göster","geri al","yinele","ayrıntılı görünüm"]),
    ];
    internal static IEnumerable<string> AllExamples=>ExampleGroups.SelectMany(group=>group.Examples);

    private readonly CommandBarHost host;
    private readonly TextBox box=new();
    private readonly TextBlock placeholder=new();
    private readonly Border rowBorder=new();
    private readonly TextBlock rowText=new();
    private readonly TextBlock caveatText=new();
    private readonly WrapPanel rowButtons=new();
    private readonly WrapPanel alternatives=new();
    private readonly WrapPanel chips=new();
    private readonly Button apply=new();
    private readonly Button cancel=new();
    private readonly Button danger=new();
    private readonly Button examplesToggle=new();
    private readonly Button voice=new();
    private readonly Border examplesPanel=new();
    private readonly DispatcherTimer previewTimer=new();
    private readonly List<string> history=[];
    private int historyIndex;
    private string historyDraft="";
    private bool suppressTextChanged;
    private Plan? shownPlan;
    private string? shownText;
    private string lastAnnounced="";
    private bool applying;

    internal CommandBarView(CommandBarHost host)
    {
        this.host=host ?? throw new ArgumentNullException(nameof(host));
        MaxWidth=880;CornerRadius=new CornerRadius(12);Padding=new Thickness(12);
        Background=StudioTheme.Surface;BorderBrush=StudioTheme.Border;BorderThickness=new Thickness(1);
        AutomationProperties.SetName(this,"Komut kutusu paneli");
        Child=Build();
        previewTimer.Interval=TimeSpan.FromMilliseconds(250);
        previewTimer.Tick+=(_,_)=>{ previewTimer.Stop(); RunAutomaticPreview(); };
        PreviewKeyDown+=OnPreviewKeyDown;
        box.TextChanged+=OnTextChanged;
        box.GotKeyboardFocus+=(_,_)=>RefreshChips();
        RefreshChips();
    }

    // ---- state the checks read ------------------------------------------------------------------------------------------

    internal CommandBarState State { get; private set; }=CommandBarState.Idle;
    /// <summary>When true a preview appears 250 ms after the last key (the checks turn this off to stay deterministic).</summary>
    internal bool AutoPreview { get; set; }=true;
    internal TextBox Box=>box;
    internal Button ApplyButton=>apply;
    internal Button CancelButton=>cancel;
    internal Button DangerButton=>danger;
    internal Button ExamplesButton=>examplesToggle;
    internal Button VoiceButton=>voice;
    internal TextBlock RowTextBlock=>rowText;
    internal TextBlock CaveatTextBlock=>caveatText;
    internal string RowText=>rowText.Text;
    internal string CaveatText=>caveatText.Text;
    internal Plan? ShownPlan=>shownPlan;
    internal IReadOnlyList<string> History=>history;
    internal bool ExamplesOpen=>examplesPanel.Visibility==Visibility.Visible;
    /// <summary>The control a key press asked to focus last (an unshown window cannot hold real keyboard focus).</summary>
    internal IInputElement? LastFocusRequest { get; private set; }
    /// <summary>Raised when Esc is pressed in an empty box: the host closes the popup.</summary>
    internal event Action? CloseRequested;
    internal IEnumerable<Button> ChipButtons=>chips.Children.OfType<Button>().Where(b=>!ReferenceEquals(b,examplesToggle) && b.Tag is string);
    internal IEnumerable<Button> AlternativeButtons=>alternatives.Children.OfType<Button>();
    internal IEnumerable<Button> AllButtons=>Descendants<Button>(this);
    internal string Text { get=>box.Text; set=>SetText(value); }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T:DependencyObject
    {
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
        {
            var child=VisualTreeHelper.GetChild(root,i);
            if(child is T match)yield return match;
            foreach(var inner in Descendants<T>(child))yield return inner;
        }
    }

    // ---- build ------------------------------------------------------------------------------------------------------------

    private static Brush Soft(Brush source,byte alpha)
    {
        var color=source is SolidColorBrush solid ? solid.Color : Colors.Gray;
        var brush=new SolidColorBrush(Color.FromArgb(alpha,color.R,color.G,color.B));brush.Freeze();return brush;
    }

    private UIElement Build()
    {
        var root=new StackPanel();
        // box with its placeholder (a TextBox has none of its own)
        var boxGrid=new Grid();
        box.FontSize=18;box.MinHeight=52;box.MaxLength=200;box.VerticalContentAlignment=VerticalAlignment.Center;
        box.Padding=new Thickness(12,4,12,4);box.AcceptsReturn=false;box.AcceptsTab=false;
        AutomationProperties.SetName(box,"Komut kutusu");
        AutomationProperties.SetHelpText(box,BoxHelp);
        AutomationProperties.SetAcceleratorKey(box,"Ctrl+K");
        placeholder.Text=Placeholder;placeholder.FontSize=18;placeholder.Foreground=StudioTheme.Muted;placeholder.IsHitTestVisible=false;
        placeholder.VerticalAlignment=VerticalAlignment.Center;placeholder.Margin=new Thickness(17,0,12,0);placeholder.TextTrimming=TextTrimming.CharacterEllipsis;
        boxGrid.Children.Add(box);boxGrid.Children.Add(placeholder);
        // "Sesle yaz": only points to Windows voice typing (Win + H). It starts nothing and presses no keys.
        voice.MinHeight=44;voice.MinWidth=44;voice.Margin=new Thickness(0,0,4,0);voice.Padding=new Thickness(12,4,12,4);voice.HorizontalAlignment=HorizontalAlignment.Right;voice.VerticalAlignment=VerticalAlignment.Center;
        var voiceContent=new StackPanel { Orientation=Orientation.Horizontal };
        voiceContent.Children.Add(new TextBlock { Text="",FontFamily=new FontFamily("Segoe MDL2 Assets"),FontSize=16,Margin=new Thickness(0,0,8,0),VerticalAlignment=VerticalAlignment.Center });
        voiceContent.Children.Add(new TextBlock { Text="Sesle yaz",FontSize=14,VerticalAlignment=VerticalAlignment.Center });
        voice.Content=voiceContent;
        AutomationProperties.SetName(voice,"Sesle yaz");
        AutomationProperties.SetHelpText(voice,"Sesle yazma için Win + H tuşlarına basın");
        voice.Click+=(_,_)=>ShowVoiceGuidance();
        box.Padding=new Thickness(12,4,150,4);
        placeholder.Margin=new Thickness(17,0,150,0);
        boxGrid.Children.Add(voice);
        root.Children.Add(boxGrid);

        // the one preview / result / refusal row (the only live region)
        rowBorder.CornerRadius=new CornerRadius(8);rowBorder.Padding=new Thickness(12,10,12,10);rowBorder.Margin=new Thickness(0,8,0,0);
        rowBorder.Visibility=Visibility.Collapsed;
        var rowStack=new StackPanel();
        rowText.FontSize=15;rowText.TextWrapping=TextWrapping.Wrap;rowText.Foreground=StudioTheme.Text;
        caveatText.FontSize=13;caveatText.TextWrapping=TextWrapping.Wrap;caveatText.Foreground=StudioTheme.Text;caveatText.Margin=new Thickness(0,6,0,0);
        caveatText.Visibility=Visibility.Collapsed;
        AutomationProperties.SetLiveSetting(rowText,AutomationLiveSetting.Polite);
        AutomationProperties.SetName(rowText,"Komut önizlemesi");
        alternatives.Margin=new Thickness(0,8,0,0);alternatives.Visibility=Visibility.Collapsed;
        rowButtons.Margin=new Thickness(0,8,0,0);rowButtons.Visibility=Visibility.Collapsed;
        ConfigureButton(apply,"Uygula",true);apply.Click+=(_,_)=>ApplyShown(false);
        ConfigureButton(cancel,"Vazgeç",false);cancel.Click+=(_,_)=>Dismiss(true);
        ConfigureButton(danger,"Evet, parçayı sil",false);danger.Click+=(_,_)=>ApplyShown(true);
        danger.Background=Soft(StudioTheme.Danger,0x55);danger.BorderBrush=StudioTheme.Danger;
        apply.Margin=new Thickness(0,0,8,0);cancel.Margin=new Thickness(0,0,8,0);
        rowButtons.Children.Add(apply);rowButtons.Children.Add(cancel);rowButtons.Children.Add(danger);
        rowStack.Children.Add(rowText);rowStack.Children.Add(caveatText);rowStack.Children.Add(alternatives);rowStack.Children.Add(rowButtons);
        rowBorder.Child=rowStack;
        root.Children.Add(rowBorder);

        chips.Margin=new Thickness(0,8,0,0);
        examplesToggle.Content="Örnekleri göster";examplesToggle.MinHeight=36;examplesToggle.FontSize=14;examplesToggle.Padding=new Thickness(12,4,12,4);examplesToggle.Margin=new Thickness(0,0,8,6);
        AutomationProperties.SetName(examplesToggle,"Örnekleri göster");
        examplesToggle.Click+=(_,_)=>ShowExamples(!ExamplesOpen);
        root.Children.Add(chips);

        examplesPanel.Visibility=Visibility.Collapsed;examplesPanel.Margin=new Thickness(0,8,0,0);examplesPanel.Padding=new Thickness(12,8,12,10);
        examplesPanel.Background=StudioTheme.Raised;examplesPanel.CornerRadius=new CornerRadius(8);
        examplesPanel.Child=BuildExamples();
        root.Children.Add(examplesPanel);
        return root;
    }

    private static void ConfigureButton(Button button,string text,bool primary)
    {
        button.Content=text;button.MinHeight=44;button.MinWidth=88;button.FontSize=15;button.Padding=new Thickness(14,6,14,6);button.Visibility=Visibility.Collapsed;
        AutomationProperties.SetName(button,text);
        if(primary)button.Background=StudioTheme.Accent;
        if(primary)button.Foreground=StudioTheme.Background;
    }

    private UIElement BuildExamples()
    {
        var stack=new StackPanel();
        foreach(var (group,examples) in ExampleGroups)
        {
            var title=new TextBlock { Text=group,FontSize=13,FontWeight=FontWeights.SemiBold,Foreground=StudioTheme.Muted,Margin=new Thickness(0,4,0,2) };
            stack.Children.Add(title);
            var wrap=new WrapPanel();
            foreach(var example in examples)wrap.Children.Add(MakeChip(example,false));
            stack.Children.Add(wrap);
        }
        stack.Children.Add(new TextBlock { Text=UnitRule,FontSize=13,Foreground=StudioTheme.Muted,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0) });
        return stack;
    }

    private Button MakeChip(string text,bool accent,string? label=null)
    {
        var chip=new Button { Content=label ?? text,Tag=text,MinHeight=36,FontSize=14,Padding=new Thickness(12,4,12,4),Margin=new Thickness(0,0,8,6) };
        if(accent)chip.BorderBrush=StudioTheme.Accent;
        AutomationProperties.SetName(chip,"Örnek: "+(label ?? text));
        AutomationProperties.SetHelpText(chip,"Kutuya yazar ve önizler; uygulamaz.");
        chip.Click+=(_,_)=>FillAndPreview(text);
        return chip;
    }

    // ---- public operations -----------------------------------------------------------------------------------------------

    internal void FocusBox()
    {
        LastFocusRequest=box;
        box.Focus();Keyboard.Focus(box);box.CaretIndex=box.Text.Length;
    }

    /// <summary>Replaces the typed text (as typing would): the shown preview is dropped.</summary>
    internal void SetText(string text)
    {
        suppressTextChanged=true;
        try { box.Text=text ?? "";box.CaretIndex=box.Text.Length;box.IsUndoEnabled=false;box.IsUndoEnabled=true; }
        finally { suppressTextChanged=false; }
        UpdatePlaceholder();
        previewTimer.Stop();
        if(AutoPreview && box.Text.Trim().Length>0)previewTimer.Start();
        // TextChanged only fires when the text really differs; make a same-text call drop the preview too.
        if(string.Equals(shownText,box.Text,StringComparison.Ordinal)==false)DropPreview();
    }

    /// <summary>Called by the host whenever it opens the box: starts from a clean row and fresh chips.</summary>
    /// <summary>The project or the selection changed behind the preview (another piece was selected): the frame and the preview go.</summary>
    internal void OnContextChanged()
    {
        if(applying)return;
        if(State is CommandBarState.Preview or CommandBarState.DangerPreview)DropPreview();
    }

    /// <summary>The box is being closed: nothing of it may stay on the canvas.</summary>
    internal void ClearPreview()
    {
        previewTimer.Stop();
        shownPlan=null;shownText=null;ClearGhost();
        if(State is CommandBarState.Preview or CommandBarState.DangerPreview)DropPreview();
    }

    internal void Reset()
    {
        ClearText();DropPreview();ShowExamples(false);RefreshChips();
    }

    internal void ShowExamples(bool open)
    {
        examplesPanel.Visibility=open ? Visibility.Visible : Visibility.Collapsed;
        examplesToggle.Content=open ? "Örnekleri gizle" : "Örnekleri göster";
        AutomationProperties.SetName(examplesToggle,(string)examplesToggle.Content);
    }

    /// <summary>All keyboard behaviour of the box; the same code runs for real key presses and for the checks.</summary>
    internal bool HandleKey(Key key,ModifierKeys modifiers)
    {
        if(modifiers==ModifierKeys.Control)
        {
            if(key==Key.K) { box.SelectAll();return true; }
            if(key is Key.Z or Key.Y && box.Text.Length==0)
            {
                var redo=key==Key.Y;
                // An emptied box may still hold typed text in its own undo stack: that comes back first.
                if(redo ? box.CanRedo : box.CanUndo)return false;
                ShowInfo(host.HistoryStep(redo));
                RefreshChips();
                return true;
            }
            return false;
        }
        if(modifiers!=ModifierKeys.None)return false;
        switch(key)
        {
            case Key.Enter:OnEnter();return true;
            case Key.Escape:OnEscape();return true;
            case Key.Up:Recall(-1);return true;
            case Key.Down:Recall(1);return true;
        }
        return false;
    }

    // ---- keyboard ---------------------------------------------------------------------------------------------------------

    private void OnPreviewKeyDown(object sender,KeyEventArgs e)
    {
        var key=e.Key==Key.System ? e.SystemKey : e.Key;
        if(e.OriginalSource==box)
        {
            if(HandleKey(key,Keyboard.Modifiers))e.Handled=true;
            return;
        }
        // On the buttons only Esc and Ctrl+K are the box's business; Enter and Space click the focused button.
        if(Keyboard.Modifiers==ModifierKeys.None && key==Key.Escape) { OnEscape();e.Handled=true; }
        else if(Keyboard.Modifiers==ModifierKeys.Control && key==Key.K) { FocusBox();box.SelectAll();e.Handled=true; }
    }

    private void OnEnter()
    {
        var text=box.Text;
        if(string.IsNullOrWhiteSpace(text)) { ShowInfo(EmptyBoxText);return; }
        previewTimer.Stop();
        Remember(text);
        // A preview the user sees for exactly this text: Enter applies it (a dangerous one moves the focus to Vazgeç first).
        if(shownPlan is { } plan && string.Equals(shownText,text,StringComparison.Ordinal))
        {
            if(plan.RequiresConfirmation) { LastFocusRequest=cancel;cancel.Focus();return; }
            ApplyShown(false);
            return;
        }
        ShowPreviewFor(text,true,null);
    }

    private void OnEscape()
    {
        if(ExamplesOpen) { ShowExamples(false);FocusBox();return; }
        if(State is not CommandBarState.Idle) { Dismiss(true);return; }
        if(box.Text.Length>0)
        {
            // The typed text goes to the history first so Up brings it back.
            Remember(box.Text);ClearText();DropPreview();RefreshChips();FocusBox();return;
        }
        CloseRequested?.Invoke();
    }

    private void Recall(int direction)
    {
        if(history.Count==0)return;
        if(historyIndex>=history.Count)historyDraft=box.Text;
        var next=Math.Clamp(historyIndex+direction,0,history.Count);
        if(next==historyIndex)return;
        historyIndex=next;
        var text=next>=history.Count ? historyDraft : history[next];
        suppressTextChanged=true;
        try { box.Text=text;box.CaretIndex=text.Length; }
        finally { suppressTextChanged=false; }
        DropPreview();
        UpdatePlaceholder();
    }

    private void Remember(string text)
    {
        text=text.Trim();
        if(text.Length==0)return;
        if(history.Count==0 || !string.Equals(history[^1],text,StringComparison.Ordinal))
        {
            history.Add(text);
            if(history.Count>HistoryLimit)history.RemoveAt(0);
        }
        historyIndex=history.Count;
    }

    // ---- text and preview -----------------------------------------------------------------------------------------------

    private void OnTextChanged(object sender,TextChangedEventArgs e)
    {
        UpdatePlaceholder();
        if(suppressTextChanged)return;
        // Whatever was shown belonged to the old text: it can no longer be applied.
        DropPreview();
        historyIndex=history.Count;
        previewTimer.Stop();
        if(AutoPreview && box.Text.Trim().Length>0)previewTimer.Start();
    }

    private void UpdatePlaceholder()=>placeholder.Visibility=box.Text.Length==0 ? Visibility.Visible : Visibility.Collapsed;

    private void ClearText()
    {
        suppressTextChanged=true;
        try { box.Clear();box.IsUndoEnabled=false;box.IsUndoEnabled=true; }
        finally { suppressTextChanged=false; }
        UpdatePlaceholder();
    }

    internal void RunAutomaticPreview()
    {
        var text=box.Text;
        if(string.IsNullOrWhiteSpace(text) || shownPlan is not null)return;
        // While typing only a clear understanding is shown; refusals wait for Enter.
        var preview=host.Preview(text,false);
        if(preview.Kind==CommandPreviewKind.Plan)ShowPreview(preview,text,null);
    }

    private void FillAndPreview(string text)
    {
        previewTimer.Stop();
        suppressTextChanged=true;
        try { box.Text=text;box.CaretIndex=text.Length; }
        finally { suppressTextChanged=false; }
        UpdatePlaceholder();
        DropPreview();
        Remember(text);
        ShowPreviewFor(text,true,null);
        FocusBox();
    }

    private void ShowPreviewFor(string text,bool onEnter,string? note)=>ShowPreview(host.Preview(text,onEnter),text,note);

    private void ShowPreview(CommandPreview preview,string text,string? note)
    {
        switch(preview.Kind)
        {
            case CommandPreviewKind.Plan:
            {
                var plan=preview.Plan!;
                shownPlan=plan;shownText=text;
                var isDanger=plan.RequiresConfirmation;
                State=isDanger ? CommandBarState.DangerPreview : CommandBarState.Preview;
                SetRow(plan.Preview,note is null ? [] : [note],isDanger ? StudioTheme.Danger : StudioTheme.Accent,isDanger ? (byte)0x30 : (byte)0x26);
                apply.Content=plan.Kind is PlanKind.Window or PlanKind.File ? "Pencereyi aç" : "Uygula";
                AutomationProperties.SetName(apply,(string)apply.Content);
                AutomationProperties.SetHelpText(apply,plan.Preview);
                AutomationProperties.SetHelpText(cancel,plan.Preview);
                danger.Content=plan.Kind==PlanKind.DeletePiece ? "Evet, parçayı sil" : "Evet, devam et";
                AutomationProperties.SetName(danger,(string)danger.Content);
                AutomationProperties.SetHelpText(danger,plan.Preview);
                apply.Visibility=isDanger ? Visibility.Collapsed : Visibility.Visible;
                danger.Visibility=isDanger ? Visibility.Visible : Visibility.Collapsed;
                cancel.Visibility=Visibility.Visible;
                rowButtons.Visibility=Visibility.Visible;
                FillAlternatives(plan.Alternatives);
                chips.Visibility=Visibility.Collapsed;
                // The dashed frame on the canvas shows what the sentence says; it is never part of the project.
                host.ShowGhost?.Invoke(plan.Ghost);
                break;
            }
            case CommandPreviewKind.Question:
            case CommandPreviewKind.Refusal:
            {
                shownPlan=null;shownText=null;ClearGhost();
                var notice=preview.Notice!;
                State=preview.Kind==CommandPreviewKind.Question ? CommandBarState.Question : CommandBarState.Refusal;
                SetRow(notice.Message,[],StudioTheme.Warning,0x30);
                HideRowButtons();
                FillAlternatives(notice.Buttons);
                chips.Visibility=Visibility.Collapsed;
                break;
            }
            default:
                DropPreview();
                break;
        }
    }

    private void FillAlternatives(IReadOnlyList<RefusalButton> buttons)
    {
        alternatives.Children.Clear();
        foreach(var button in buttons.Take(3))alternatives.Children.Add(MakeChip(button.FillText,false,button.Label));
        alternatives.Visibility=alternatives.Children.Count>0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void HideRowButtons()
    {
        apply.Visibility=Visibility.Collapsed;cancel.Visibility=Visibility.Collapsed;danger.Visibility=Visibility.Collapsed;
        rowButtons.Visibility=Visibility.Collapsed;
    }

    /// <summary>Drops the shown preview, result or refusal; the box and the history stay.</summary>
    private void ClearGhost()=>host.ShowGhost?.Invoke(null);

    private void DropPreview()
    {
        shownPlan=null;shownText=null;
        ClearGhost();
        if(State==CommandBarState.Idle && rowBorder.Visibility==Visibility.Collapsed)return;
        State=CommandBarState.Idle;
        rowBorder.Visibility=Visibility.Collapsed;caveatText.Visibility=Visibility.Collapsed;
        HideRowButtons();alternatives.Children.Clear();alternatives.Visibility=Visibility.Collapsed;
        rowText.Text="";caveatText.Text="";lastAnnounced="";
        chips.Visibility=Visibility.Visible;
        RefreshChips();
    }

    /// <summary>Vazgeç / Esc: closes what is shown and returns to the box.</summary>
    private void Dismiss(bool focusBox)
    {
        previewTimer.Stop();
        DropPreview();
        if(focusBox)FocusBox();
    }

    private void SetRow(string sentence,IReadOnlyList<string> caveats,Brush tint,byte alpha)
    {
        rowBorder.Visibility=Visibility.Visible;
        rowBorder.Background=Soft(tint,alpha);
        rowText.Text=sentence;
        // Caveats are never cut: the line wraps to as many lines as it needs and the whole text is also the automation name.
        caveatText.Text=string.Join(Environment.NewLine,caveats);
        caveatText.Visibility=caveats.Count>0 ? Visibility.Visible : Visibility.Collapsed;
        Announce(caveats.Count>0 ? sentence+" "+string.Join(" ",caveats) : sentence);
    }

    private void Announce(string full)
    {
        AutomationProperties.SetName(rowText,full);
        if(string.Equals(lastAnnounced,full,StringComparison.Ordinal))return;
        lastAnnounced=full;
        if(AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged))
            UIElementAutomationPeer.FromElement(rowText)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    // ---- apply -----------------------------------------------------------------------------------------------------------------

    private void ApplyShown(bool confirmed)
    {
        if(shownPlan is not { } plan || !string.Equals(shownText,box.Text,StringComparison.Ordinal))return;
        previewTimer.Stop();
        var text=shownText!;
        // The frame goes away the moment the plan is applied; a refusal or a stale plan shows a fresh one if needed.
        ClearGhost();
        applying=true;
        CommandOutcome outcome;
        try { outcome=host.Apply(plan,confirmed); }
        finally { applying=false; }
        switch(outcome.Result)
        {
            case CommandResult.Applied:
            case CommandResult.Done:
                ShowResult(outcome);
                break;
            case CommandResult.Stale:
                // The project or selection changed since the preview: show the new preview and apply nothing.
                ShowPreviewFor(text,true,StaleText);
                break;
            default:
                shownPlan=null;shownText=null;
                State=CommandBarState.Refusal;
                SetRow(outcome.Message,[],StudioTheme.Warning,0x30);
                HideRowButtons();alternatives.Children.Clear();alternatives.Visibility=Visibility.Collapsed;
                chips.Visibility=Visibility.Collapsed;
                break;
        }
    }

    private void ShowResult(CommandOutcome outcome)
    {
        shownPlan=null;shownText=null;ClearGhost();
        ClearText();
        State=CommandBarState.Result;
        var tint=outcome.RefreshFailed ? StudioTheme.Warning : StudioTheme.Accent;
        SetRow("Yapıldı · "+outcome.Message.TrimEnd(),outcome.Caveats ?? [],tint,0x26);
        HideRowButtons();alternatives.Children.Clear();alternatives.Visibility=Visibility.Collapsed;
        chips.Visibility=Visibility.Visible;
        RefreshChips(outcome.Changed && !outcome.RefreshFailed);
        FocusBox();
    }

    internal const string VoiceGuidance="Konuşmak için Win + H tuşlarına basın. Söyledikleriniz bu kutuya yazılır; siz uygulamadan hiçbir şey değişmez.";
    internal const string VoiceNote="Ses tanıma Windows'un özelliğidir; internet gerekebilir.";

    /// <summary>
    /// "Sesle yaz": puts the keyboard cursor in the box and says how to start Windows voice typing. It does not start
    /// recognition, listens to nothing and presses no keys; typing (or dictation) replaces the message.
    /// </summary>
    internal void ShowVoiceGuidance()
    {
        previewTimer.Stop();
        shownPlan=null;shownText=null;ClearGhost();
        State=CommandBarState.Voice;
        SetRow(VoiceGuidance,[VoiceNote],StudioTheme.Accent,0x1A);
        HideRowButtons();alternatives.Children.Clear();alternatives.Visibility=Visibility.Collapsed;
        chips.Visibility=Visibility.Collapsed;
        FocusBox();
    }

    private void ShowInfo(string message)
    {
        shownPlan=null;shownText=null;ClearGhost();
        State=CommandBarState.Refusal;
        SetRow(message,[],StudioTheme.Warning,0x30);
        HideRowButtons();alternatives.Children.Clear();alternatives.Visibility=Visibility.Collapsed;
        chips.Visibility=Visibility.Collapsed;
    }

    // ---- chips -----------------------------------------------------------------------------------------------------------------

    private IEnumerable<string> SuggestedTexts(CommandBarContext context)
    {
        if(context.DrawingInProgress) { yield return "çizimi kapat";yield return "çizimi iptal et";yield break; }
        if(context.HasUnsavedWork)yield return "projeyi kaydet";
        if(context.EmptyProject)
        {
            yield return "12 x 8 cm dikdörtgen";yield return "150 çizgi";yield return "proje aç";
        }
        else if(!context.HasSelection)
        {
            foreach(var name in context.PieceNames.Take(2))yield return $"{name.ToLower(System.Globalization.CultureInfo.GetCultureInfo("tr-TR"))} seç";
            yield return "proje aç";
        }
        else
        {
            yield return "genişliği 95 yap";yield return "25 sağa taşı";yield return "90 sağa döndür";
        }
    }

    /// <summary>Rebuilds the example chips: only texts the planner understands for the current project and selection (a curved piece is not offered a resize).</summary>
    internal void RefreshChips(bool offerUndo=false)
    {
        chips.Children.Clear();
        var context=host.Context();
        var shown=0;var limit=offerUndo ? 2 : 3;
        if(offerUndo)
        {
            var undo=new Button { Content="Geri al",MinHeight=36,FontSize=14,Padding=new Thickness(12,4,12,4),Margin=new Thickness(0,0,8,6) };
            AutomationProperties.SetName(undo,"Yapılan son işlemi geri al");
            undo.Click+=(_,_)=>{ ShowInfo(host.HistoryStep(false));RefreshChips();FocusBox(); };
            chips.Children.Add(undo);
        }
        else chips.Children.Add(new TextBlock { Text="Deneyin:",FontSize=14,Foreground=StudioTheme.Muted,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,8,6) });
        foreach(var text in SuggestedTexts(context))
        {
            if(shown>=limit+(context.HasUnsavedWork ? 1 : 0))break;
            if(host.Preview(text,true).Kind!=CommandPreviewKind.Plan)continue;
            chips.Children.Add(MakeChip(text,text=="projeyi kaydet" && context.HasUnsavedWork));
            shown++;
        }
        chips.Children.Add(examplesToggle);
    }
}
