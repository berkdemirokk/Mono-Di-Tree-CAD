using System.IO;
using TekstilCAD.FileFormats;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TekstilCAD.Core;
using TekstilCAD.Core.Commands;
using static TekstilCAD.Desktop.DesktopVerification;

namespace TekstilCAD.Desktop;

// PR 4: the command box. The view is built in a detached container and rendered from there; the Popup is never opened.
internal static partial class CommandVerification
{
    private sealed class RecordingHost : ICommandBoxHost
    {
        internal int Opens;
        public bool IsOpen { get; private set; }
        public void Open(CommandBarView view) { IsOpen=true;Opens++; }
        public void Close() { IsOpen=false; }
    }

    private static CommandBarView NewBox(MainWindow window)
    {
        var view=new CommandBarView(window.CreateCommandBarHost()) { AutoPreview=false };
        var holder=new Border { Width=880,Child=view,Background=StudioTheme.Background };
        holder.Measure(new Size(880,2000));holder.Arrange(new Rect(0,0,880,holder.DesiredSize.Height));holder.UpdateLayout();
        return view;
    }
    private static void Relayout(CommandBarView view,double width=880)
    {
        var holder=(FrameworkElement)view.Parent;holder.Width=width;
        holder.Measure(new Size(width,3000));holder.Arrange(new Rect(0,0,width,holder.DesiredSize.Height));holder.UpdateLayout();
    }
    private static void RenderView(CommandBarView view,string? reportPath,string name,double width=880)
    {
        Relayout(view,width);
        if(reportPath is null)return;
        var holder=(FrameworkElement)view.Parent;
        var w=(int)Math.Ceiling(holder.ActualWidth);var h=(int)Math.Ceiling(holder.ActualHeight)+8;
        var bitmap=new RenderTargetBitmap(w,h,96,96,PixelFormats.Pbgra32);
        var backing=new DrawingVisual();
        using(var dc=backing.RenderOpen())dc.DrawRectangle(StudioTheme.Background,null,new Rect(0,0,w,h));
        bitmap.Render(backing);bitmap.Render(holder);
        var path=Path.ChangeExtension(reportPath,$".command-box-{name}.png");
        ProjectStorage.EnsureWritableDestination(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream=new FileStream(path,FileMode.Create,FileAccess.Write);encoder.Save(stream);
    }
    private static bool Type(CommandBarView view,string text)
    {
        view.SetText(text);return true;
    }
    private static bool Enter(CommandBarView view)=>view.HandleKey(Key.Enter,ModifierKeys.None);

    // Everything the classic layout shows or measures, in tree order.
    private static string Fingerprint(MainWindow window)
    {
        var sb=new StringBuilder();
        void Walk(DependencyObject node,int depth)
        {
            if(node is FrameworkElement e)
            {
                sb.Append(depth).Append(':').Append(e.GetType().Name).Append('|').Append(System.Windows.Automation.AutomationProperties.GetName(e))
                  .Append('|').Append(e.Visibility).Append('|').Append(e.Margin).Append('|').Append(e.Width).Append('x').Append(e.Height)
                  .Append('|').Append(e.MinWidth).Append('x').Append(e.MinHeight);
                if(e is Control c)sb.Append('|').Append(c.FontSize);
                if(e is TextBlock t)sb.Append('|').Append(t.FontSize);
                if(e is Grid g)sb.Append('|').Append(string.Join(",",g.ColumnDefinitions.Select(col=>col.Width.ToString())));
                sb.AppendLine();
            }
            for(var i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)Walk(VisualTreeHelper.GetChild(node,i),depth+1);
        }
        Walk((DependencyObject)window.Content,0);
        return sb.ToString();
    }
    private static Dictionary<string,object?> ElementFields(MainWindow window)
        =>typeof(MainWindow).GetFields(BindingFlags.NonPublic|BindingFlags.Instance)
            .Where(f=>typeof(UIElement).IsAssignableFrom(f.FieldType) && !f.Name.StartsWith("commandBox",StringComparison.Ordinal))
            .ToDictionary(f=>f.Name,f=>f.GetValue(window));

    private static void VerifyCommandBox(List<string> checks,string? reportPath)
    {
        windowsStable=true;
        // 1 + 2: the classic tree is untouched by creating, opening and closing the host.
        WithFreshWindow(window=>
        {
            var host=new RecordingHost();window.CommandBoxHostForVerification=()=>host;
            Layout(window);
            var before=Fingerprint(window);var fields=ElementFields(window);
            var opened=window.HandleCommandBoxShortcut(ModifierKeys.Control,Key.K);
            Layout(window);var whileOpen=Fingerprint(window);
            window.CloseCommandBox();Layout(window);
            var after=Fingerprint(window);var fieldsAfter=ElementFields(window);
            Check(opened && host.Opens==1 && before==whileOpen && before==after && fields.Count==fieldsAfter.Count
                && fields.All(pair=>ReferenceEquals(pair.Value,fieldsAfter[pair.Key])) && before.Length>2000,
                "Classic window tree is identical before and after the command box host is created and closed",checks);
            Check(window.CommandBoxView is not null && ((FrameworkElement)window.Content) is var root && !Descendants<CommandBarView>(root).Any(),
                "The command box is not part of the classic window's visual tree",checks);
        });
        WithFreshWindow(window=>
        {
            window.CommandBoxHostForVerification=()=>new RecordingHost();
            window.OpenCommandBox();window.CloseCommandBox();
            var floors=new[] { (1024,640,411.0),(1440,900,671.0) };
            var ok=true;var detail=new StringBuilder();
            foreach(var (width,height,floor) in floors)
            {
                Render(window,reportPath is null ? Path.Combine(Path.GetTempPath(),"command-box-floor.png") : Path.ChangeExtension(reportPath,$".command-box-classic-{width}x{height}.png"),width,height);
                var canvas=Field<CadCanvas>(window,"canvas");
                ok&=canvas.ActualHeight>=floor;detail.Append($" {width}x{height}:{canvas.ActualHeight:0}");
            }
            Check(ok,"Classic layout keeps its canvas floors with the command box host present (clean state ≥ 411 and ≥ 671 px,"+detail+")",checks);
        });
        // 3, 4, 5, 6: preview, apply, changed text, unknown text.
        Scenario(window=>
        {
            window.LoadDocument(new ProjectDocument());
            var session=SessionOf(window);var view=NewBox(window);
            Type(view,"120 x 80 dikdörtgen");
            Check(view.State==CommandBarState.Idle && window.Document.Pieces.Count==0,"Typing alone shows no preview and changes nothing",checks);
            Enter(view);
            RenderView(view,reportPath,"preview");
            Check(view.State==CommandBarState.Preview && view.RowText.Contains("120") && view.RowText.Contains("80") && view.ApplyButton.Visibility==Visibility.Visible
                && view.CancelButton.Visibility==Visibility.Visible && window.Document.Pieces.Count==0 && session.UndoCount==0,
                "Command box shows a preview for 120 x 80 dikdörtgen and leaves the document unchanged",checks);
            // (c) no unit written: the preview says so and offers a centimetre reading, which only fills the box.
            Check(view.RowText.Contains("Birim yazmadınız; milimetre sayıldı.") && view.AlternativeButtons.Any(b=>b.Tag is string tag && tag.Contains("cm")),
                "A text without a unit says millimetres were assumed and offers a centimetre chip",checks);
            var cm=view.AlternativeButtons.First(b=>b.Tag is string tag && tag.Contains("cm"));
            cm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(view.Text.Contains("cm") && window.Document.Pieces.Count==0 && session.UndoCount==0 && view.State==CommandBarState.Preview && !view.RowText.Contains("Birim yazmadınız"),
                "The centimetre chip fills the box and previews without applying",checks);
            Enter(view);
            Check(view.State==CommandBarState.Result && window.Document.Pieces.Count==1 && session.UndoCount==1 && window.Selected?.Id==window.Document.Pieces[0].Id
                && view.RowText.StartsWith("Yapıldı") && view.Text.Length==0 && view.Box.Text.Length==0,
                "Enter applies the shown preview once as one undo step and selects the new piece",checks);
            Enter(view);
            Check(window.Document.Pieces.Count==1 && session.UndoCount==1,"Enter in the emptied box applies nothing again",checks);
            // 5
            Type(view,"150 çizgi");Enter(view);
            var shownFirst=view.State==CommandBarState.Preview;
            var undo=session.UndoCount;
            Type(view,"80 dikey çizgi");
            var droppedOnTyping=view.State==CommandBarState.Idle && view.ShownPlan is null;
            Enter(view);
            Check(shownFirst && droppedOnTyping && view.State==CommandBarState.Preview && view.RowText.Contains("80") && session.UndoCount==undo && window.Document.Pieces.Count==1,
                "Enter after the text changed shows the new preview and applies nothing",checks);
            // 6
            Type(view,"zzz qwerty");Enter(view);
            Check(view.State==CommandBarState.Refusal && view.RowText.Length>0 && (view.RowText.Contains("Örnek") || view.RowText.Contains("yazabilirsiniz") || view.AlternativeButtons.Any())
                && window.Document.Pieces.Count==1 && session.UndoCount==undo,
                "Unknown text is refused with an example and changes nothing",checks);
            // (a) an unfinished text shows nothing while typing; the refusal comes with Enter.
            Type(view,"120 x");view.RunAutomaticPreview();
            var quiet=view.State==CommandBarState.Idle;
            Enter(view);
            Check(quiet && view.State is CommandBarState.Refusal or CommandBarState.Question && window.Document.Pieces.Count==1,
                "An unfinished text shows no refusal while typing, only when Enter is pressed",checks);
            Type(view,"120 x 80 cm dikdörtgen");view.RunAutomaticPreview();
            Check(view.State==CommandBarState.Preview && window.Document.Pieces.Count==1,"A clearly understood text previews by itself after a pause without applying",checks);
        });
        // 7: dangerous command.
        Scenario(window=>
        {
            var piece=MarkedPiece();window.LoadDocument(new ProjectDocument { Pieces=[piece],GradingRules=[RuleFor(piece)] });window.Select(piece.Id);
            var session=SessionOf(window);var before=window.Document.Clone();var view=NewBox(window);
            Type(view,"sil");Enter(view);
            RenderView(view,reportPath,"delete");
            var shown=view.State==CommandBarState.DangerPreview && view.DangerButton.Visibility==Visibility.Visible && view.ApplyButton.Visibility!=Visibility.Visible;
            Enter(view);
            var focusOnCancel=ReferenceEquals(view.LastFocusRequest,view.CancelButton);
            Enter(view);
            Check(shown && focusOnCancel && DocumentSession.ContentEquals(before,window.Document) && session.UndoCount==0 && window.Document.Pieces.Count==1,
                "Delete preview moves focus to Vazgeç and Enter does not delete",checks);
            view.DangerButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(window.Document.Pieces.Count==0 && session.UndoCount==1 && session.Undo() && DocumentSession.ContentEquals(before,window.Document),
                "The red confirm button deletes once and undo brings the piece back",checks);
            Type(view,"sil");window.Select(piece.Id);
        });
        // 8: Ctrl+K and Esc reach the window.
        WithFreshWindow(window=>
        {
            var host=new RecordingHost();window.CommandBoxHostForVerification=()=>host;
            var fromCanvas=window.HandleCommandBoxShortcut(ModifierKeys.Control,Key.K);
            var opens=host.Opens;window.CloseCommandBox();
            var fromTextBox=window.HandleCommandBoxShortcut(ModifierKeys.Control,Key.K);
            var refusedPlain=!window.HandleCommandBoxShortcut(ModifierKeys.None,Key.K) && !window.HandleCommandBoxShortcut(ModifierKeys.Shift,Key.K);
            // Esc through the window's own key handler closes an open box and is marked handled.
            var args=new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(window)!,0,Key.Escape) { RoutedEvent=Keyboard.KeyDownEvent };
            typeof(MainWindow).GetMethod("OnKeyDown",Private)!.Invoke(window,[window,args]);
            Check(fromCanvas && opens==1 && fromTextBox && host.Opens==2 && refusedPlain && args.Handled && !host.IsOpen,
                "Ctrl+K reaches the command box from the canvas and from a text box, and Esc closes it",checks);
        });
        // 9: Ctrl+Z only undoes the project when the box is empty.
        Scenario(window=>
        {
            var piece=MarkedPiece();window.LoadDocument(new ProjectDocument { Pieces=[piece] });window.Select(piece.Id);
            var session=SessionOf(window);var original=window.Document.Clone();var view=NewBox(window);
            Check(window.ApplyCommand(PlanOf(window,"aynala")).Result==CommandResult.Applied && session.UndoCount==1,"Setup: one command applied before Ctrl+Z",checks);
            Type(view,"abc");
            var textKey=view.HandleKey(Key.Z,ModifierKeys.Control);
            Check(!textKey && session.UndoCount==1 && !DocumentSession.ContentEquals(original,window.Document),
                "Ctrl+Z with text in the command box is left to the text box and does not undo the project",checks);
            view.SetText("");
            var emptyKey=view.HandleKey(Key.Z,ModifierKeys.Control);
            Check(emptyKey && session.UndoCount==0 && DocumentSession.ContentEquals(original,window.Document),
                "Ctrl+Z in the command box undoes the project only when the box is empty",checks);
            var redone=view.HandleKey(Key.Y,ModifierKeys.Control);
            Check(redone && session.UndoCount==1 && view.RowText.Length>0,"Ctrl+Y in the emptied box redoes the project step and says so",checks);
        });
        // 10: typing never reaches the canvas shortcuts.
        Scenario(window=>
        {
            var piece=MarkedPiece();window.LoadDocument(new ProjectDocument { Pieces=[piece] });window.Select(piece.Id);
            var view=NewBox(window);var session=SessionOf(window);var tool=window.ActiveTool;
            var keys=new[] { Key.V,Key.L,Key.P,Key.R,Key.B,Key.A,Key.F,Key.G,Key.Delete,Key.Back };
            Check(keys.All(key=>!view.HandleKey(key,ModifierKeys.None) && !window.HandleCanvasShortcut(key,false)) && window.ActiveTool==tool
                && window.Document.Pieces.Count==1 && session.UndoCount==0,
                "Typing in the command box does not trigger canvas shortcuts",checks);
        });
        // 11: history.
        Scenario(window=>
        {
            window.LoadDocument(new ProjectDocument());var view=NewBox(window);
            Type(view,"aynala");Enter(view);view.HandleKey(Key.Escape,ModifierKeys.None);
            Type(view,"90 sağa döndür");Enter(view);view.HandleKey(Key.Escape,ModifierKeys.None);
            view.SetText("");
            var up1=view.HandleKey(Key.Up,ModifierKeys.None);var first=view.Text;
            view.HandleKey(Key.Up,ModifierKeys.None);var second=view.Text;
            view.HandleKey(Key.Down,ModifierKeys.None);var back=view.Text;
            view.HandleKey(Key.Down,ModifierKeys.None);var draft=view.Text;
            Check(up1 && first=="90 sağa döndür" && second=="aynala" && back=="90 sağa döndür" && draft=="",
                "Up and Down recall the last typed commands",checks);
            for(var i=0;i<25;i++){ Type(view,$"{i+1} sağa taşı");Enter(view);view.HandleKey(Key.Escape,ModifierKeys.None); }
            Check(view.History.Count==CommandBarView.HistoryLimit && view.History[^1]=="25 sağa taşı" && !view.History.Contains("aynala"),
                "The command history keeps the last 20 commands in memory only",checks);
        });
        // 12 + chips + (b) (e) (f) (g): names, focus, sizes, chips per state.
        Scenario(window=>
        {
            var piece=MarkedPiece();window.LoadDocument(new ProjectDocument { Pieces=[piece] });window.Select(piece.Id);
            var view=NewBox(window);var problems=new List<string>();
            void Audit(string state)
            {
                Relayout(view);
                foreach(var button in view.AllButtons.Where(b=>b.IsVisible))
                {
                    var name=System.Windows.Automation.AutomationProperties.GetName(button);
                    if(string.IsNullOrWhiteSpace(name))problems.Add($"{state}: unnamed button {button.Content}");
                    if(!button.Focusable || !button.IsEnabled)problems.Add($"{state}: button {name} is not focusable or enabled");
                    if(button.FontSize<12)problems.Add($"{state}: {name} font {button.FontSize}");
                }
                foreach(var text in Descendants<TextBlock>(view).Where(t=>t.IsVisible && t.Text.Length>0))
                    if(text.FontSize<12)problems.Add($"{state}: text '{text.Text}' font {text.FontSize}");
                if(view.Box.FontSize<12)problems.Add($"{state}: box font");
            }
            Audit("idle");
            Type(view,"25 sağa taşı");Enter(view);Audit("preview");
            Check(view.RowTextBlock.FontSize>=14,"The preview sentence is at least 14 px",checks);
            Enter(view);Audit("result");
            Type(view,"zzz");Enter(view);Audit("refusal");
            view.SetText("");view.ShowExamples(true);Audit("examples");RenderView(view,reportPath,"examples");view.ShowExamples(false);
            Type(view,"sil");Enter(view);Audit("danger");
            var boxName=System.Windows.Automation.AutomationProperties.GetName(view.Box);var help=System.Windows.Automation.AutomationProperties.GetHelpText(view.Box);
            Check(problems.Count==0 && boxName=="Komut kutusu" && help.Contains("Enter ile uygulanır") && view.Box.Focusable
                && System.Windows.Automation.AutomationProperties.GetLiveSetting(view.RowTextBlock)==System.Windows.Automation.AutomationLiveSetting.Polite
                && !Descendants<TextBlock>(view).Any(t=>!ReferenceEquals(t,view.RowTextBlock) && System.Windows.Automation.AutomationProperties.GetLiveSetting(t)!=System.Windows.Automation.AutomationLiveSetting.Off),
                "Command box exposes accessible names and is focusable for every control; no text is below 12 px and only the row is live"+(problems.Count>0 ? ": "+string.Join("; ",problems.Take(4)) : ""),checks);
        });
        // chips: offered only for what the selected piece can do; never applied by a click.
        Scenario(window=>
        {
            var plain=new PatternPiece { Name="Düz",Points=[new(0,0),new(100,0),new(100,50),new(0,50)] };
            var curved=new PatternPiece { Name="Eğri",Points=[new(0,0),new(100,0),new(100,50),new(0,50)],Curves=
                [new CurveSegment.Line(new(0,0),new(100,0)),new CurveSegment.Line(new(100,0),new(100,50)),new CurveSegment.Line(new(100,50),new(0,50)),new CurveSegment.Line(new(0,50),new(0,0))] };
            window.LoadDocument(new ProjectDocument { Pieces=[plain] });window.Select(plain.Id);
            var view=NewBox(window);view.RefreshChips();
            var plainChips=view.ChipButtons.Select(b=>(string)b.Tag!).ToList();
            var session=SessionOf(window);var before=window.Document.Clone();
            view.ChipButtons.First().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(plainChips.Contains("genişliği 95 yap") && view.State==CommandBarState.Preview && DocumentSession.ContentEquals(before,window.Document) && session.UndoCount==0,
                "A chip fills the box and previews but never applies",checks);
            window.LoadDocument(new ProjectDocument { Pieces=[curved] });window.Select(curved.Id);
            var curvedView=NewBox(window);curvedView.RefreshChips();
            var curvedChips=curvedView.ChipButtons.Select(b=>(string)b.Tag!).ToList();
            Check(!curvedChips.Contains("genişliği 95 yap") && curvedChips.Count>0,"No resize chip is offered for a curved piece",checks);
            window.LoadDocument(new ProjectDocument());
            var emptyView=NewBox(window);emptyView.RefreshChips();
            Check(emptyView.ChipButtons.Select(b=>(string)b.Tag!).First()=="12 x 8 cm dikdörtgen","The first chip of an empty project is the centimetre example",checks);
            Check(CommandBarView.Placeholder.StartsWith("Ölçüyü yazın") && CommandBarView.Placeholder.Contains("12 x 8 cm dikdörtgen"),"The placeholder gives a centimetre example",checks);
            var parser=new ParserContext(["Parça 1"]);
            var unparsed=CommandBarView.AllExamples.Where(example=>{ var parsed=CommandParser.Parse(example,parser);return !(parsed.IsCommand || parsed.Kind==ParseKind.Question); }).ToList();
            Check(unparsed.Count==0,"Every example in the box is understood by the parser"+(unparsed.Count>0 ? ": "+string.Join(", ",unparsed) : ""),checks);
        });
        // 13: the result keeps the caveat whole until the next typing.
        Scenario(window=>
        {
            var piece=MarkedPiece();window.LoadDocument(new ProjectDocument { Pieces=[piece],GradingRules=[RuleFor(piece)] });window.Select(piece.Id);
            var view=NewBox(window);
            Type(view,"25 sağa taşı");Enter(view);Enter(view);
            RenderView(view,reportPath,"result",420);
            var caveat=view.CaveatText;
            Check(view.State==CommandBarState.Result && caveat.Contains("İplik yönü") && view.CaveatTextBlock.Visibility==Visibility.Visible
                && view.CaveatTextBlock.TextWrapping==TextWrapping.Wrap && view.CaveatTextBlock.TextTrimming==TextTrimming.None && double.IsInfinity(view.CaveatTextBlock.MaxHeight)
                && view.CaveatTextBlock.ActualHeight>=view.CaveatTextBlock.DesiredSize.Height-.5
                && System.Windows.Automation.AutomationProperties.GetName(view.RowTextBlock).Contains(caveat.Split(Environment.NewLine)[0]),
                "Result row keeps the caveat of the applied command, whole, until the next typing",checks);
            Type(view,"a");
            Check(view.State==CommandBarState.Idle && view.CaveatText.Length==0,"Typing after a result drops the result row",checks);
        });
        Check(windowsStable,"Command box checks open no window, no popup and no message box",checks);
    }
}
