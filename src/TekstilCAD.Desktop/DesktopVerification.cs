using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Automation.Peers;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;
using TekstilCAD.MachineGateway;

namespace TekstilCAD.Desktop;

/// <summary>Explicit, noninteractive WPF construction/layout verification; not an interactive UI test.</summary>
internal static class DesktopVerification
{
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string newFileName,string existingFileName,IntPtr securityAttributes);
    private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
    private static void VerifyOpenPathClosure(MainWindow studio,List<string> checks)
    {
        var a=new PatternPiece { Name="Açık",Closed=false,Points=[new(0,0),new(10,0),new(10,10),new(0,10)] };
        var rule=new GradingRuleSet { SourcePieceId=a.Id,SourceClosed=false,SourcePoints=[..a.Points],Deltas=[new(0,0),new(0,0),new(0,0),new(0,0)] };
        studio.LoadDocument(new ProjectDocument { Name="Kapatma doğrulaması",Pieces=[a],GradingRules=[rule] });
        var session=(DocumentSession)typeof(MainWindow).GetField("session",PrivateInstance)!.GetValue(studio)!;
        var initial=studio.Document.Clone();var window=new OpenPathClosureWindow(studio,studio);
        try
        {
            void Preview() { var task=window.CalculateForVerification();PumpUntil(()=>task.IsCompleted);task.GetAwaiter().GetResult(); }
            window.ToleranceForVerification.Text="10";Preview();
            Check(window.ApplyButtonForVerification.IsEnabled && window.MessageForVerification.Contains("Kapanış kenarı") &&
                DocumentSession.ContentEquals(initial,studio.Document) && session.UndoCount==0,
                "Open path closure preview leaves the project unchanged and reports the closing edge",checks);
            window.ApplyForVerification();
            Check(studio.Document.Pieces.Count==2 && studio.Document.Pieces[1].Closed &&
                studio.Document.Pieces[1].Id!=a.Id && studio.Document.Pieces[1].Points.SequenceEqual(a.Points) &&
                JsonSerializer.Serialize(studio.Document.Pieces[0])==JsonSerializer.Serialize(initial.Pieces[0]) &&
                JsonSerializer.Serialize(studio.Document.GradingRules)==JsonSerializer.Serialize(initial.GradingRules) && session.UndoCount==1,
                "Closure adds one closed piece while preserving the source and its grading rule",checks);
            Invoke(studio,"Undo");
            Check(DocumentSession.ContentEquals(initial,studio.Document),"Closure undo restores the original project",checks);
            Preview();window.ToleranceForVerification.Text="9";
            var before=studio.Document.Clone();var undo=session.UndoCount;
            Check(!window.ApplyButtonForVerification.IsEnabled,"Changing closure tolerance invalidates preview",checks);
            window.ApplyForVerification();Preview();
            Check(!window.ApplyButtonForVerification.IsEnabled && DocumentSession.ContentEquals(before,studio.Document) && session.UndoCount==undo,
                "Stale or insufficient closure tolerance cannot change the project",checks);
            window.ToleranceForVerification.Text="10";Preview();
            studio.Commit("Kaynak değişikliği",()=>studio.Document.Pieces.Single(p=>p.Id==a.Id).Points[1]=new Point2(11,0));
            before=studio.Document.Clone();undo=session.UndoCount;window.ApplyForVerification();
            Check(!window.ApplyButtonForVerification.IsEnabled && DocumentSession.ContentEquals(before,studio.Document) && session.UndoCount==undo && window.MessageForVerification.Contains("değişti"),
                "Closure rejects a stale source without a history entry",checks);
        }
        finally { window.Close();studio.LoadDocument(new ProjectDocument { Name="Doğrulama" }); }
    }
    private static void VerifyOpenPathJoin(MainWindow studio,List<string> checks)
    {
        var a=new PatternPiece { Name="Ön",Closed=false,Points=[new(0,0),new(10,0)] };
        var b=new PatternPiece { Name="Arka",Closed=false,Points=[new(10.1,0),new(20,0)] };
        var other=new PatternPiece { Name="Bilgili",Points=[new(0,10),new(10,10),new(10,20)],Metadata=new PieceMetadata { Size="M" } };
        var rule=new GradingRuleSet { SourcePieceId=a.Id,SourceClosed=false,SourcePoints=[..a.Points],Deltas=[new(0,0),new(1,0)] };
        studio.LoadDocument(new ProjectDocument { Name="Birleştirme doğrulaması",Pieces=[a,b,other],GradingRules=[rule] });
        var session=(DocumentSession)typeof(MainWindow).GetField("session",PrivateInstance)!.GetValue(studio)!;
        var before=studio.Document.Clone();
        var window=new OpenPathJoinWindow(studio,studio);
        try
        {
            void Preview()
            {
                var task=window.CalculateForVerification(); PumpUntil(()=>task.IsCompleted); task.GetAwaiter().GetResult();
            }
            window.ToleranceForVerification.Text="0,2";
            Preview();
            Check(window.ApplyButtonForVerification.IsEnabled && window.MessageForVerification.Contains("Kırmızı çizgi") &&
                DocumentSession.ContentEquals(before,studio.Document) && session.UndoCount==0,
                "Open path preview describes the bridge without changing the project",checks);
            window.ApplyForVerification();
            Check(studio.Document.Pieces.Count==4 && studio.Document.Pieces[3].Points.SequenceEqual(new[] { new Point2(0,0),new(10,0),new(10.1,0),new(20,0) }) &&
                studio.Document.Pieces.Take(3).Select(p=>p.Id).SequenceEqual(before.Pieces.Select(p=>p.Id)) &&
                JsonSerializer.Serialize(studio.Document.Pieces.Take(3))==JsonSerializer.Serialize(before.Pieces) &&
                JsonSerializer.Serialize(studio.Document.GradingRules)==JsonSerializer.Serialize(before.GradingRules) && session.UndoCount==1,
                "Open path apply adds one result and preserves source geometry, metadata and grading rules",checks);
            Invoke(studio,"Undo");
            Check(DocumentSession.ContentEquals(before,studio.Document),"Undo removes the joined result and restores the source project",checks);

            Preview();
            Check(window.ApplyButtonForVerification.IsEnabled,"Open path preview can be recalculated after undo",checks);
            window.ToleranceForVerification.Text="0,05";
            var beforeTolerance=studio.Document.Clone(); var undoBefore=session.UndoCount;
            Check(!window.ApplyButtonForVerification.IsEnabled,"Changing tolerance invalidates the preview",checks);
            window.ApplyForVerification();
            Check(DocumentSession.ContentEquals(beforeTolerance,studio.Document) && session.UndoCount==undoBefore,
                "A stale tolerance cannot apply a previous proposal",checks);
            Preview();
            Check(!window.ApplyButtonForVerification.IsEnabled && DocumentSession.ContentEquals(beforeTolerance,studio.Document),
                "A tolerance smaller than the gap cannot produce a proposal",checks);

            window.ToleranceForVerification.Text="0,2"; Preview();
            studio.Commit("Kaynak değişikliği",()=>studio.Document.Pieces.Single(p=>p.Id==a.Id).Points[0]=new Point2(-1,0));
            var changed=studio.Document.Clone(); undoBefore=session.UndoCount;
            window.ApplyForVerification();
            Check(!window.ApplyButtonForVerification.IsEnabled && DocumentSession.ContentEquals(changed,studio.Document) && session.UndoCount==undoBefore &&
                window.MessageForVerification.Contains("değişti"),"A changed source rejects a stale preview without another history entry",checks);
        }
        finally { window.Close(); studio.LoadDocument(new ProjectDocument { Name="Doğrulama" }); }
    }
    private static void VerifyHistoryCapacityNotice(MainWindow studio,string reportPath,List<string> checks)
    {
        var session=(DocumentSession)typeof(MainWindow).GetField("session",PrivateInstance)!.GetValue(studio)!;
        var notice=(TextBlock)typeof(MainWindow).GetField("historyCapacityNotice",PrivateInstance)!.GetValue(studio)!;
        var undo=typeof(MainWindow).GetMethod("Undo",PrivateInstance)!;
        studio.LoadDocument(new ProjectDocument { Name="Geçmiş sınırı denemesi" });
        try
        {
            Check(notice.Visibility==Visibility.Collapsed && session.CapacityDiscardedSnapshots>=0,
                "New document has no history-capacity warning",checks);
            studio.Commit("No-op",()=>{});
            Check(notice.Visibility==Visibility.Collapsed,"A no-op does not show a capacity warning",checks);
            studio.Commit("First edit",()=>studio.Document.Name="A");
            undo.Invoke(studio,null);
            Check(session.RedoCount==1,"Undo creates an ordinary redo branch",checks);
            studio.Commit("Branch edit",()=>studio.Document.Name="B");
            Check(session.RedoCount==0 && notice.Visibility==Visibility.Collapsed,
                "Clearing redo for a new edit does not claim history-capacity eviction",checks);
            for(var i=0;i<51;i++)
            {
                var name="Kayıt "+i;
                studio.Commit("History capacity edit",()=>studio.Document.Name=name);
            }
            var root=(FrameworkElement)studio.Content;
            Render(studio,Path.ChangeExtension(reportPath,".history-capacity-compact.png"),1024,640);
            Check(notice.Visibility==Visibility.Visible && notice.Text.Contains("kapasitesi aşıldı",StringComparison.OrdinalIgnoreCase)
                && AutomationProperties.GetName(notice)==notice.Text
                && AutomationProperties.GetLiveSetting(notice)==AutomationLiveSetting.Polite
                && notice.Focusable && KeyboardNavigation.GetIsTabStop(notice)
                && FullyVisible(notice,root)
                && notice.ActualHeight+notice.Margin.Top+notice.Margin.Bottom>=notice.DesiredSize.Height-.5,
                "Capacity eviction produces a complete accessible warning at compact size",checks);
            studio.LoadDocument(new ProjectDocument { Name="Yeni belge" });
            Check(notice.Visibility==Visibility.Collapsed && notice.Text.Length==0,
                "Loading a different document clears the previous document's warning",checks);
        }
        finally { studio.LoadDocument(new ProjectDocument { Name="Doğrulama" }); }
    }
    private static void VerifySourceHistoryNotice(MainWindow studio,string work,string reportPath,List<string> checks)
    {
        const string text="Kaynak ve önceki aktarım uyarıları projede saklanmaz. Üretimden önce ölçüleri ve yerleşimi kaynakla karşılaştırın.";
        var root=(FrameworkElement)studio.Content;
        var notice=Descendants<TextBlock>(root).Single(t=>t.Text==text);
        var export=(Button)typeof(MainWindow).GetField("exportCommand",PrivateInstance)!.GetValue(studio)!;
        var machine=(Button)typeof(MainWindow).GetField("machineQueueCommand",PrivateInstance)!.GetValue(studio)!;
        Render(studio,Path.ChangeExtension(reportPath,".source-history-compact.png"),1024,640);
        Check(notice.Focusable && KeyboardNavigation.GetIsTabStop(notice)
            && AutomationProperties.GetName(notice)==text
            && AutomationProperties.GetHelpText(notice).Contains("üretim onayı değildir",StringComparison.Ordinal)
            && notice.TextWrapping==TextWrapping.Wrap && FullyVisible(notice,root)
            && notice.ActualHeight+notice.Margin.Top+notice.Margin.Bottom>=notice.DesiredSize.Height-.5,
            "Source history notice has complete text, accessibility metadata and compact layout",checks);
        Check((export.ToolTip as string)?.Contains("Kaynak geçmişi projede saklanmaz",StringComparison.Ordinal)==true
            && (machine.ToolTip as string)?.Contains("Kaynak geçmişi projede saklanmaz",StringComparison.Ordinal)==true,
            "Export and simulator commands describe the missing source history",checks);
        // Exercise real new/open/save transitions without a dialog; input is an
        // independent verification project and the caller restores its sample.
        Invoke(studio,"NewDocument");
        Check(ReferenceEquals(notice,Descendants<TextBlock>(root).Single(t=>t.Text==text)) && FullyVisible(notice,root),
            "New project retains the source history notice",checks);
        var path=Path.Combine(work,"source-history-open.tcad");
        ProjectStorage.Save(new ProjectDocument { Name="Kaynak geçmişi denemesi" },path);
        studio.OpenProjectFile(path);
        Check(studio.Document.Name=="Kaynak geçmişi denemesi" && ReferenceEquals(notice,Descendants<TextBlock>(root).Single(t=>t.Text==text))
            && FullyVisible(notice,root),"Opening a project retains the source history notice",checks);
        Invoke(studio,"SaveTo",path);
        Check(ReferenceEquals(notice,Descendants<TextBlock>(root).Single(t=>t.Text==text)) && FullyVisible(notice,root),
            "Saving a project retains the source history notice",checks);
    }
    private static void VerifyLazyMachineQueue(MainWindow studio,string work,List<string> checks)
    {
        var queueField=typeof(MainWindow).GetField("machineQueue",PrivateInstance)!;
        var closedField=typeof(MainWindow).GetField("windowClosed",PrivateInstance)!;
        var button=(Button)typeof(MainWindow).GetField("machineQueueCommand",PrivateInstance)!.GetValue(studio)!;
        var message=(TextBox)typeof(MainWindow).GetField("machineQueueMessage",PrivateInstance)!.GetValue(studio)!;
        var status=(TextBlock)typeof(MainWindow).GetField("status",PrivateInstance)!.GetValue(studio)!;
        var originalFactory=studio.MachineQueueFactory;
        Check(queueField.GetValue(studio) is null && !studio.MachineQueueLoading && button.IsEnabled,
            "Machine history does not load during main window construction",checks);
        var count=0;
        studio.MachineQueueFactory=()=> { Interlocked.Increment(ref count); throw new IOException("deneme hatası"); };
        var failed=studio.BeginMachineQueueLoadAsync();PumpUntil(()=>failed.IsCompleted);failed.GetAwaiter().GetResult();
        Check(count==1 && queueField.GetValue(studio) is null && !studio.MachineQueueLoading && button.IsEnabled && status.Text.Contains("yüklenemedi")
            && Equals(button.Content,"Simülatörü yeniden dene") && message.Visibility==Visibility.Visible && message.IsReadOnly
            && message.Text.Contains("deneme hatası") && !string.IsNullOrWhiteSpace(AutomationProperties.GetName(message)),
            "Machine history failure leaves the window usable and retry enabled",checks);
        studio.SetStatus("Farklı bir işlem tamamlandı");
        Check(message.Text.Contains("deneme hatası") && message.Visibility==Visibility.Visible,
            "Machine history error remains available after an unrelated status update",checks);
        using var reached=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
        studio.MachineQueueFactory=()=> { Interlocked.Increment(ref count);reached.Set();release.Wait();return new SimulationQueue(Path.Combine(work,"lazy-machine-history")); };
        var pending=studio.BeginMachineQueueLoadAsync();
        try
        {
            PumpUntil(()=>reached.IsSet);
            var dispatched=false;studio.Dispatcher.BeginInvoke(new Action(()=>dispatched=true));PumpUntil(()=>dispatched);
            var duplicate=studio.BeginMachineQueueLoadAsync();PumpUntil(()=>duplicate.IsCompleted);
            Check(studio.MachineQueueLoading && !button.IsEnabled && Equals(button.Content,"Simülatör yükleniyor…") && message.Text.Contains("yükleniyor") && count==2 && queueField.GetValue(studio) is null && dispatched,
                "Machine history retry runs once on a worker while the UI dispatcher remains responsive",checks);
            release.Set();PumpUntil(()=>pending.IsCompleted);pending.GetAwaiter().GetResult();
            Check(queueField.GetValue(studio) is SimulationQueue && !studio.MachineQueueLoading && button.IsEnabled && status.Text.Contains("hazır")
                && Equals(button.Content,"Simülatörü aç") && message.Text.Contains("hazır"),
                "Successful retry publishes the queue and enables opening the simulator",checks);
        }
        finally { release.Set();PumpUntil(()=>pending.IsCompleted); }
        // Simulate the already-closed state without showing or interacting with a window.
        queueField.SetValue(studio,null);
        using var reachedClosed=new ManualResetEventSlim();using var releaseClosed=new ManualResetEventSlim();
        studio.MachineQueueFactory=()=> { reachedClosed.Set();releaseClosed.Wait();return new SimulationQueue(Path.Combine(work,"lazy-machine-closed")); };
        var late=studio.BeginMachineQueueLoadAsync();
        try
        {
            PumpUntil(()=>reachedClosed.IsSet);closedField.SetValue(studio,true);
            var before=status.Text;releaseClosed.Set();PumpUntil(()=>late.IsCompleted);late.GetAwaiter().GetResult();
            Check(queueField.GetValue(studio) is null && status.Text==before && !studio.MachineQueueLoading,
                "Machine history result is discarded after the window is closed",checks);
        }
        finally { releaseClosed.Set();PumpUntil(()=>late.IsCompleted);closedField.SetValue(studio,false);button.IsEnabled=true;studio.MachineQueueFactory=originalFactory; }
    }
    private static void VerifyMainPieceIdentification(MainWindow studio,List<string> checks)
    {
        var first=new PatternPiece { Name="Ön\nparça",Points=[new(0,0),new(10,0),new(10,10)] };
        var second=new PatternPiece { Name=first.Name,Points=[new(20,0),new(30,0),new(30,10)] };
        studio.LoadDocument(new ProjectDocument { Name="Aynı adlı parçalar",Pieces=[first,second] });
        var list=(ListBox)typeof(MainWindow).GetField("pieces",PrivateInstance)!.GetValue(studio)!;
        var identity=(TextBox)typeof(MainWindow).GetField("selectedPieceIdentity",PrivateInstance)!.GetValue(studio)!;
        var before=JsonSerializer.Serialize(studio.Document);
        Check(AutomationProperties.GetName(list)=="Proje parçaları" && !string.IsNullOrWhiteSpace(AutomationProperties.GetHelpText(list)) && list.ItemTemplate is not null,
            "Main piece list exposes its purpose and an identifying item template",checks);
        list.SelectedItem=studio.Document.Pieces.Single(p=>p.Id==first.Id);
        Check(studio.SelectedId==first.Id && identity.IsReadOnly && identity.Text.Contains(first.Id.ToString("D")) && identity.Text.Contains("Ön\\u000Aparça"),
            $"First duplicate piece has copyable escaped name and full identity (selected={studio.SelectedId}, expected={first.Id}, readonly={identity.IsReadOnly}, text={identity.Text})",checks);
        list.SelectedItem=studio.Document.Pieces.Single(p=>p.Id==second.Id);
        Check(studio.SelectedId==second.Id && identity.Text.Contains(second.Id.ToString("D")) && !identity.Text.Contains(first.Id.ToString("D")) && before==JsonSerializer.Serialize(studio.Document),
            "Changing duplicate selection shows the correct identity without modifying the project",checks);
        var view=list.Items;
        var resets=0;
        System.Collections.Specialized.NotifyCollectionChangedEventHandler changed=(_,e)=> { if(e.Action==System.Collections.Specialized.NotifyCollectionChangedAction.Reset) resets++; };
        ((System.Collections.Specialized.INotifyCollectionChanged)view).CollectionChanged+=changed;
        var source=list.ItemsSource;
        list.SelectedItem=studio.Document.Pieces.Single(p=>p.Id==first.Id);
        list.SelectedItem=studio.Document.Pieces.Single(p=>p.Id==second.Id);
        Check(ReferenceEquals(source,list.ItemsSource) && resets==0 && studio.SelectedId==second.Id,
            "Selection changes keep the same list view without collection resets",checks);
        var refresh=typeof(MainWindow).GetMethod("Refresh",PrivateInstance)!;
        var added=new PatternPiece { Name="Ek",Points=[new(40,0),new(50,0),new(50,10)] };
        studio.Document.Pieces.Add(added);
        refresh.Invoke(studio,null);
        Check(ReferenceEquals(source,list.ItemsSource) && list.Items.Count==3 && list.Items.Contains(added) && studio.SelectedId==second.Id,
            $"In-place addition is reflected and preserves selection (sameSource={ReferenceEquals(source,list.ItemsSource)}, count={list.Items.Count}, contains={list.Items.Contains(added)}, selected={studio.SelectedId==second.Id})",checks);
        studio.Document.Pieces.Remove(added);
        var replacement=new PatternPiece { Name="Yerine",Points=[new(60,0),new(70,0),new(70,10)] };
        studio.Document.Pieces[0]=replacement;
        refresh.Invoke(studio,null);
        Check(list.Items.Count==2 && list.Items.Contains(replacement) && studio.SelectedId==second.Id,
            "In-place removal and replacement update list while retaining selection",checks);
        var live=studio.Document.Pieces[1]; live.Name="Yeni ad";
        refresh.Invoke(studio,null);
        Check(identity.Text.Contains("Yeni ad") && studio.SelectedId==second.Id,
            "In-place rename refreshes the selected identity",checks);
        ((System.Collections.Specialized.INotifyCollectionChanged)view).CollectionChanged-=changed;
        studio.Commit("Deneme",()=>studio.Document.Pieces.Add(new PatternPiece { Name="İşlem",Points=[new(80,0),new(90,0),new(90,10)] }));
        Check(ReferenceEquals(list.ItemsSource,studio.Document.Pieces) && !ReferenceEquals(source,list.ItemsSource) && studio.SelectedId==second.Id && list.Items.Count==3,
            "Transaction replacement rebinds the list and preserves selected identity",checks);
        Check(VirtualizingPanel.GetIsVirtualizing(list) && VirtualizingPanel.GetVirtualizationMode(list)==VirtualizationMode.Recycling && ScrollViewer.GetCanContentScroll(list),
            "Piece list requests recycling virtualization with logical scrolling",checks);
    }
    public static int Run(string reportPath)
    {
        MainWindow? studio = null;
        var checks = new List<string>();
        string? failure = null, imagePath = null;
        try
        {
            reportPath = AllowedReportPath(reportPath);
            var work = Path.Combine(RepositoryRoot(reportPath), "work", "desktop-verification", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            AppPaths.UseVerificationDirectory(Path.Combine(work, "application-data"));
            studio = new MainWindow();
            studio.ConfirmHpglReviewForVerification=(_,_)=>true;
            StopTimer(studio);
            checks.Add("MainWindow constructed without Show");
            _ = new WindowInteropHelper(studio).EnsureHandle();
            VerifyQuantityChoiceIdentity(studio,reportPath,checks);
            var sample = new ProjectDocument { Name = "Arka plan doğrulaması", Pieces = [new PatternPiece { Name = "100 mm kare", Points = [new(0,0),new(100,0),new(100,100),new(0,100)] }] };
            studio.LoadDocument(sample);
            Check(studio.Document.Pieces.Count == 1, "LoadDocument", checks);

            VerifySourceHistoryNotice(studio,work,reportPath,checks);
            VerifyHistoryCapacityNotice(studio,reportPath,checks);
            studio.LoadDocument(sample);
            VerifyMainStatusAccessibility(studio,checks);
            VerifyRecoverySelectionDetails(studio,checks);
            VerifyMainPieceIdentification(studio,checks);
            studio.LoadDocument(sample);
            VerifyLazyMachineQueue(studio,work,checks);
            VerifyAsyncProjectOpen(studio,work,checks);
            studio.LoadDocument(sample);
            var squareMetrics=MainWindow.FormatPieceMetrics(sample.Pieces[0]);
            Check(squareMetrics.Contains("400 mm") && squareMetrics.Contains("100 cm²"),"Valid contour length and area displayed",checks);
            var duplicateClosedMetrics=MainWindow.FormatPieceMetrics(new PatternPiece { Points=[new(0,0),new(100,0),new(100,100),new(0,100),new(0,0)] });
            Check(duplicateClosedMetrics.Contains("Çevre: 400 mm") && duplicateClosedMetrics.Contains("Alan: 100 cm²"),"Explicit closing point does not double-count contour length or area",checks);
            var smallAreaMetrics=MainWindow.FormatPieceMetrics(new PatternPiece { Points=[new(0,0),new(.001,0),new(.001,.001),new(0,.001)] });
            var expectedSmallArea=(.00000001).ToString("G6",System.Globalization.CultureInfo.CurrentCulture);
            Check(smallAreaMetrics.Contains("Çevre: "+(.004).ToString("G6",System.Globalization.CultureInfo.CurrentCulture)+" mm") && smallAreaMetrics.Contains("Alan: "+expectedSmallArea+" cm²"),"Small positive area is not displayed as zero",checks);
            var shortLineMetrics=MainWindow.FormatPieceMetrics(new PatternPiece { Closed=false, Points=[new(0,0),new(.001,0)] });
            var expectedShortLength=(.001).ToString("G6",System.Globalization.CultureInfo.CurrentCulture);
            Check(shortLineMetrics.Contains("Çizgi uzunluğu: "+expectedShortLength+" mm"),"Small positive line length is not displayed as zero",checks);
            var openMetrics=MainWindow.FormatPieceMetrics(new PatternPiece { Closed=false, Points=[new(0,0),new(3,4)] });
            Check(openMetrics.Contains("5 mm") && openMetrics.Contains("açık çizgide hesaplanmaz"),"Open path length displayed without false area",checks);
            var bowMetrics=MainWindow.FormatPieceMetrics(new PatternPiece { Points=[new(0,0),new(10,10),new(0,10),new(10,0)] });
            Check(bowMetrics.Contains("geçersiz konturda hesaplanamıyor"),"Self intersecting contour area withheld",checks);
            var denseMetrics=MainWindow.FormatPieceMetrics(new PatternPiece { Points=Enumerable.Range(0,513).Select(i=>new Point2(i, i%2)).ToList() });
            Check(denseMetrics.Contains("doğrulama sınırını aşıyor"),"Expensive UI area validation bounded",checks);
            VerifyAsyncArea(studio, checks);
            VerifySelectedPieceLimits(studio, reportPath, checks);
            VerifyPropertyDrafts(studio,work,checks);
            GroupMoveVerification.Run(studio,checks);
            VerifyDraftRemovalConfirmationRace(studio,checks);
            VerifyFitSelectedPiece(studio,checks);
            VerifyDraftTranslationPrecision(studio,work,checks);
            VerifyDxfEncodingChoice(studio,work,reportPath,checks);
            VerifyDxfBulgeOptions(studio,reportPath,checks);
            VerifyDxfStrictOptions(studio,work,reportPath,checks);
            VerifyDxfPlacementRetry(studio,work,reportPath,checks);
            VerifyDxfDecodedNames(studio,reportPath,checks);
            VerifyDxfPiecePreview(studio,work,reportPath,checks);
            VerifySelectedDraftConflict(studio,checks);
            VerifySaveAsVersionProtection(studio,work,checks);
            VerifyProjectSaveConflictPresentation(studio,work,checks);
            VerifyDiscardPromptLongName(studio,reportPath,checks);
            VerifyOrphanDraftReview(studio,work,checks);
            VerifyFailedDraftSaves(studio,work,checks);
            VerifyRecoveryClearDuringWrite(studio,work,checks);
            VerifyRecoveryClearFailure(studio,work,checks);
            VerifyRecoveryCloseDisposeFailure(work,checks);
            VerifyObsoleteRecoveryCleared(studio,work,checks);
            VerifyDraftRecovery(studio,work,checks);
            VerifyPendingDrawingRecovery(studio,work,checks);
            VerifyRecoveryWarningPresentation(studio,reportPath,checks);
            VerifyBackgroundRecoveryScan(studio,work,reportPath,checks);
            VerifyRecoveryPaging(studio,work,checks);
            VerifyPendingDrawingFailurePreservesPoints(studio,checks);
            VerifyToolGuidance(studio,checks);
            VerifyBackspaceDrawing(studio,work,checks);
            VerifyCoordinateDrawing(studio,checks);
            VerifyOpenPathJoin(studio,checks);
            VerifyOpenPathClosure(studio,checks);
            VerifyDuplicatePiece(studio,checks);
            VerifyDuplicateOptions(studio,checks);
            VerifyDuplicateGrading(studio,checks);
            VerifyDeleteShortcutSafety(studio,checks);
            VerifyGradeResetConfirmation(studio,checks);
            VerifyGradingGeometryNotice(studio,reportPath,checks);
            VerifyOffsetSourceHint(studio,checks);
            VerifyOffsetSourceContext(studio,checks);
            VerifyOffsetUnverifiedDetailGuidance(studio,checks);
            VerifyGradeInputFeedback(studio,checks);
            VerifyGradeApplyConflicts(studio,checks);
            VerifyLayeredPointerSelection(studio,checks);
            VerifyPieceSearch(studio,checks);
            VerifyMachineChoiceIdentity(studio,work,reportPath,checks);
            VerifyNestingCancellation(studio,reportPath,checks);
            VerifyBasicNestingAsync(studio,reportPath,checks);
            VerifyNumericVertexMove(studio,checks);
            VerifyGrainlineEditing(studio,checks);
            VerifyGrainlineEditorSafety(studio,work,checks);
            VerifyNumericVertexInsertDelete(studio,checks);
            VerifyEdgeMidpoint(studio,checks);
            VerifyEdgeDistance(studio,checks);
            VerifyEdgePercentage(studio,checks);
            VerifyNotchEdit(studio,reportPath,checks);

            VerifyReverseOpenPath(studio,checks);
            VerifyReverseClosedContour(studio,checks);
            VerifyReverseOpenPathGradingReview(studio,checks);
            VerifyNestingErrorIdentity(checks);
            VerifyNestingErrorNavigation(studio,reportPath,checks);
            VerifyNestingErrorNavigationLarge(studio,reportPath,checks);
            VerifyPointDistance(studio,reportPath,checks);
            VerifySeamLengthComparison(studio,checks);
            VerifyPointPicker(studio,reportPath,checks);
            VerifyBulkGrading(studio,reportPath,checks);
            VerifyBulkPreviewDirection(checks);
            VerifyBulkPreviewGrainline(checks);
            VerifyBulkPreviewFit(checks);
            VerifyIndependentRecoveryWarnings(studio,reportPath,checks);
            VerifySingleGradePostCommit(studio,checks);
            VerifyToolCommandPostCommit(studio,checks);
            VerifyOffsetPostCommit(studio,checks);
            VerifyOpenAndDraftPostCommit(studio,work,checks);
            VerifySaveAndCanvasPostCommit(studio,work,checks);
            VerifyHistoryAndRecoveryPostCommit(studio,work,checks);
            VerifyMainMutationPostCommit(studio,checks);
            VerifyRecoveryRestoreDuringWrite(studio,work,checks);
            VerifyDraftErrorIdentity(studio,checks);
            VerifyBatchTransforms(studio,checks);
            VerifyMetadataDialogStaleProject(studio,checks);
            VerifyMetadataDialogPieceIdentity(studio,checks);
            VerifyActiveDragGuards(studio,work,checks);
            VerifyPolylineMouseEntryLimits(studio,checks);
            VerifyLargePendingPolyline(studio,checks);
            VerifyPendingDrawingOutputGuards(studio,checks);
            VerifyPendingExportPreflightGuards(studio,work,checks);
            VerifyFractionalSelectionWithSnap(studio,checks);
            VerifyNearestVertexSelection(studio,checks);
            VerifyClosingEdgeDragSafety(studio,checks);
            VerifyOpenVertexDrag(studio,checks);
            VerifyExplicitClosureVertexDrag(studio,checks);
            VerifyLargeVertexValidation(studio,checks);
            VerifyAsyncExport(studio,work,checks);
            VerifyDxfGrainlineReviewExport(studio,work,checks);
            VerifyDxfGrainlineExportReviewWindow(studio,reportPath,checks);
            VerifyHpglExportCancellation(studio,work,checks);
            VerifyHpglPreparationFlow(studio,work,checks);
            VerifyMachineHpglCancellation(studio,work,checks);
            VerifyMachineHpglReview(studio,work,checks);
            VerifyMachineConfirmationFailure(studio,work,checks);
            VerifyMachineDxfSourceConfirmation(studio,work,checks);
            VerifyMachineOpenApprovalState(studio,work,checks);
            VerifyAsyncExportPendingPreviews(studio,work,checks);
            VerifyOptionalGradingMetadata(studio,checks);
            VerifyGradePointPicker(studio,reportPath,checks);
            VerifyAsyncGrading(studio,checks);
            VerifyGradingCancellation(studio,checks);
            VerifyGradingSourceIdentification(studio,checks);
            studio.LoadDocument(sample);
            Check(!(typeof(MainWindow).GetField("undoCommand", PrivateInstance)?.GetValue(studio) as Button)!.IsEnabled,
                "Undo disabled for fresh document", checks);
            studio.AddPiece([new(150,0),new(200,0)], false);
            Check(studio.Document.Pieces.Count == 2, "AddPiece", checks);
            Check((typeof(MainWindow).GetField("undoCommand", PrivateInstance)?.GetValue(studio) as Button)!.IsEnabled,
                "Undo enabled after edit", checks);
            Invoke(studio, "Undo");
            Check(studio.Document.Pieces.Count == 1, "Undo", checks);
            Invoke(studio, "Redo");
            Check(studio.Document.Pieces.Count == 2, "Redo", checks);
            var before = JsonSerializer.Serialize(studio.Document);
            try
            {
                studio.Commit("Invalid geometry probe", () => studio.Document.Pieces[0].Points[0] = new(double.NaN, 0));
                throw new InvalidOperationException("Invalid commit unexpectedly succeeded.");
            }
            catch (InvalidDataException) { }
            Check(JsonSerializer.Serialize(studio.Document) == before, "Invalid commit rolled back", checks);
            var canvas = typeof(MainWindow).GetField("canvas", PrivateInstance)!.GetValue(studio)!;
            typeof(CadCanvas).GetMethod("Safe", PrivateInstance)!.Invoke(canvas,
                [new Action(() => throw new InvalidDataException("Verification resource limit")),false,null]);
            Check(JsonSerializer.Serialize(studio.Document) == before, "Canvas validation failure handled without mutation", checks);
            var projectPath = Path.Combine(work, "native-roundtrip.tcad");
            Invoke(studio, "SaveTo", projectPath);
            var loaded = ProjectStorage.Load(projectPath);
            Check(JsonSerializer.Serialize(loaded) == before, "Desktop SaveTo and native load round trip", checks);
            studio.LoadDocument(loaded);
            Check(typeof(MainWindow).GetField("filePath", PrivateInstance)!.GetValue(studio) is null,
                "Replacing document clears previous save destination", checks);
            studio.Select(loaded.Pieces[0].Id);

            // Hostile but valid project names must not grow the header or
            // change the stored name. The complete escaped name remains
            // available to mouse and assistive-technology users.
            var beforeProjectTitle=studio.Document.Clone();
            var unusualProjectName="Model\nKesim\u202E"+new string('A',300)+" 🧵";
            studio.Commit("Proje adı görünümü",()=>studio.Document.Name=unusualProjectName);
            Render(studio,Path.ChangeExtension(reportPath,".project-name.png"),1024,640);
            var displayedTitle=(TextBlock)typeof(MainWindow).GetField("projectTitle",PrivateInstance)!.GetValue(studio)!;
            var completeTitle=displayedTitle.ToolTip as string;
            var titleSavedPath=Path.Combine(work,"project-name-unicode-check.tcad");
            ProjectStorage.Save(studio.Document,titleSavedPath);
            Check(displayedTitle.TextWrapping==TextWrapping.NoWrap && displayedTitle.TextTrimming==TextTrimming.CharacterEllipsis
                && displayedTitle.ActualHeight<=24 && !displayedTitle.Text.Contains('\n')
                && displayedTitle.Text.Contains("\\u000A",StringComparison.Ordinal) && displayedTitle.Text.Contains("\\u202E",StringComparison.Ordinal)
                && completeTitle==displayedTitle.Text && AutomationProperties.GetName(displayedTitle)=="Proje adı: "+completeTitle
                && !studio.Title.Contains('\n') && !studio.Title.Contains('\u202E') && studio.Title.Length<220
                && studio.Document.Name==unusualProjectName && ProjectStorage.Load(titleSavedPath).Name==unusualProjectName,
                "Long multiline and bidirectional project name stays bounded, accessible and unchanged on disk",checks);
            Invoke(studio,"Undo");
            Check(DocumentSession.ContentEquals(beforeProjectTitle,studio.Document),"Project name verification restores the original document",checks);

            var safeNameMethod=typeof(MainWindow).GetMethod("SafeFileName",BindingFlags.NonPublic|BindingFlags.Static)!;
            string Suggested(string input)=>(string)safeNameMethod.Invoke(null,[input])!;
            var suggestionDirectory=Path.Combine(work,"safe-suggestions-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(suggestionDirectory);
            foreach(var deviceName in new[] { "CON", "NUL", "COM1", "lpt9.txt" })
            {
                var suggestion=Suggested(deviceName);
                var suggestedPath=Path.Combine(suggestionDirectory,suggestion+".tcad");
                File.WriteAllText(suggestedPath,"test");
                Check(suggestion.StartsWith('_') && !suggestion.EndsWith(' ') && !suggestion.EndsWith('.')
                    && File.ReadAllText(suggestedPath)=="test",
                    "Windows reserved device names receive usable save suggestions",checks);
            }
            var trimmedName=Suggested("Kalıp  .  ");
            var unicodeName=Suggested(new string('A',239)+"🧵"+new string('B',1024));
            Check(trimmedName=="Kalıp" && unicodeName.Length<=240 && !char.IsHighSurrogate(unicodeName[^1])
                && !unicodeName.Any(c=>Path.GetInvalidFileNameChars().Contains(c)) && unicodeName.Length+".tcad".Length<=255,
                "Save suggestions trim Windows-invalid endings and preserve complete Unicode within component limits",checks);

            // Exercise the actual export preflight without opening a save dialog.
            Invoke(studio,"SaveTo",projectPath);
            var savedBytes=File.ReadAllBytes(projectPath);
            try { Invoke(studio,"EnsureExportDoesNotReplaceOpenProject",projectPath); throw new Exception("Active project was accepted as an export target."); }
            catch(InvalidOperationException) { }
            Check(File.ReadAllBytes(projectPath).SequenceEqual(savedBytes) && DocumentSession.ContentEquals(ProjectStorage.Load(projectPath),studio.Document),
                "Export preflight rejects active project and preserves its bytes",checks);
            // Remove the currently open path temporarily while keeping the same
            // file identity for the following save/conflict checks. An export
            // must rescue the in-memory work, yet the original path remains
            // protected even when it no longer exists.
            var displacedProject=Path.Combine(work,"native-roundtrip-displaced-"+Guid.NewGuid().ToString("N")+".tcad");
            var rescuedExport=Path.Combine(work,"missing-project-export-"+Guid.NewGuid().ToString("N")+".dxf");
            var existingExport=Path.Combine(work,"missing-project-existing-"+Guid.NewGuid().ToString("N")+".dxf");
            var existingBytes=System.Text.Encoding.UTF8.GetBytes("mevcut dışa aktarım");
            File.WriteAllBytes(existingExport,existingBytes);
            var beforeRescue=studio.Document.Clone();
            var previousContext=SynchronizationContext.Current;
            File.Move(projectPath,displacedProject);
            try
            {
                try { Invoke(studio,"EnsureExportDoesNotReplaceOpenProject",projectPath);throw new Exception("Missing active project path was accepted as an export target."); }
                catch(InvalidOperationException) { }
                bool existingBlocked=false;
                try { Invoke(studio,"EnsureExportDoesNotReplaceOpenProject",existingExport); }
                catch(InvalidOperationException) { existingBlocked=true; }
                catch(IOException) { existingBlocked=true; }
                Check(existingBlocked && File.ReadAllBytes(existingExport).SequenceEqual(existingBytes),
                    "Existing export target is preserved when missing open project identity cannot be checked",checks);
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(studio.Dispatcher));
                var rescue=studio.StartExportAsync(beforeRescue.Clone(),rescuedExport,1,ProjectStorage.CaptureVersion(rescuedExport));
                PumpUntil(()=>rescue.IsCompleted);rescue.GetAwaiter().GetResult();
                Check(studio.ExportError is null && File.Exists(rescuedExport) && !File.Exists(projectPath)
                    && DocumentSession.ContentEquals(beforeRescue,studio.Document)
                    && DxfFormat.Import(rescuedExport).Project.Pieces.Count==beforeRescue.Pieces.Count,
                    "Missing open project can be exported to a different file while its own path stays protected",checks);
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previousContext);
                File.Move(displacedProject,projectPath);
            }
            // Access errors are distinct from a missing source: a locked
            // project must not be treated as safe merely because identity
            // verification could not open it.
            using(var lockedProject=new FileStream(projectPath,FileMode.Open,FileAccess.ReadWrite,FileShare.None))
            {
                bool blocked=false;
                try { Invoke(studio,"EnsureExportDoesNotReplaceOpenProject",rescuedExport); }
                catch(IOException) { blocked=true; }
                catch(UnauthorizedAccessException) { blocked=true; }
                Check(blocked,"Unreadable open project blocks export identity verification",checks);
            }
            var exportSnapshot=studio.Document.Clone();
            studio.Commit("Snapshot probe",()=>studio.Document.Pieces[0].Name="Snapshot değişikliği");
            Check(!DocumentSession.ContentEquals(exportSnapshot,studio.Document) && exportSnapshot.Pieces[0].Name!="Snapshot değişikliği",
                "Export snapshot detects later document change without mutation",checks);
            Invoke(studio,"Undo");

            studio.Commit("Precision probe", () => studio.Document.Pieces[0].Points = studio.Document.Pieces[0].Points.Select(p => new Point2(p.X+0.123456789,p.Y+0.987654321)).ToList());
            Invoke(studio,"SaveTo",projectPath);
            var precisePoints=studio.Selected!.Points.ToArray();
            ((TextBox)typeof(MainWindow).GetField("name",PrivateInstance)!.GetValue(studio)!).Text="Hassas kalıp";
            Invoke(studio,"ApplyProperties");
            Check(studio.Selected!.Points.SequenceEqual(precisePoints),"Renaming preserves full precision coordinates",checks);
            Invoke(studio,"Undo");
            Check(!(bool)typeof(MainWindow).GetField("dirty",PrivateInstance)!.GetValue(studio)!,"Undo to saved content is clean",checks);
            Invoke(studio,"ApplyProperties");
            Check(((Button)typeof(MainWindow).GetField("redoCommand",PrivateInstance)!.GetValue(studio)!).IsEnabled,"No-op properties preserve redo",checks);
            Invoke(studio,"Redo");
            Check(studio.Selected!.Name=="Hassas kalıp","Redo restores named edit and selection",checks);
            Invoke(studio,"SaveTo",projectPath);
            studio.Commit("Local unsaved edit",()=>studio.Document.Name="Yerel değişiklik");
            var external=ProjectStorage.Load(projectPath);external.Name="Başka oturum";ProjectStorage.Save(external,projectPath);
            try { Invoke(studio,"SaveTo",projectPath);throw new InvalidOperationException("Stale desktop save succeeded"); }
            catch(ProjectConflictException) { }
            Check(ProjectStorage.Load(projectPath).Name=="Başka oturum" && studio.Document.Name=="Yerel değişiklik" && (bool)typeof(MainWindow).GetField("dirty",PrivateInstance)!.GetValue(studio)!,"Desktop conflict preserves external file and local edits",checks);
            VerifyKeyboardCommands(studio,(CadCanvas)canvas,checks);
            // Exercise the same parse/validation stage as ImportDxf without opening modal dialogs.
            var invalidDxf=Path.Combine(work,"invalid-import.dxf");
            File.WriteAllText(invalidDxf,"0\nSECTION\n2\nENTITIES\n0\nLINE\n10\n0\n20\n0\n11\n10\n0\nENDSEC\n0\nEOF\n");
            var preImport=JsonSerializer.Serialize(studio.Document);var preImportSelection=studio.SelectedId;
            var preImportDirty=(bool)typeof(MainWindow).GetField("dirty",PrivateInstance)!.GetValue(studio)!;
            var documentSession=(DocumentSession)typeof(MainWindow).GetField("session",PrivateInstance)!.GetValue(studio)!;
            var undoBeforeImport=documentSession.UndoCount;var redoBeforeImport=documentSession.RedoCount;
            try { Invoke(studio,"PrepareDxfImport",invalidDxf,1.0); throw new InvalidOperationException("Malformed DXF was accepted"); }
            catch(InvalidDataException) { }
            Check(JsonSerializer.Serialize(studio.Document)==preImport && studio.SelectedId==preImportSelection
                && (bool)typeof(MainWindow).GetField("dirty",PrivateInstance)!.GetValue(studio)! == preImportDirty
                && documentSession.UndoCount==undoBeforeImport && documentSession.RedoCount==redoBeforeImport,
                "Failed DXF preparation preserves document, selection, dirty state and history",checks);

            var content = (FrameworkElement)studio.Content;
            content.Measure(new Size(1440, 900)); content.Arrange(new Rect(0, 0, 1440, 900)); content.UpdateLayout();
            studio.FitView();
            content.UpdateLayout();
            VerifyViewNavigation(studio,(CadCanvas)canvas,checks);
            imagePath = Path.ChangeExtension(reportPath, ".png");
            Render(studio, imagePath, 1440, 900);
            Render(studio, Path.ChangeExtension(reportPath, ".compact.png"), 1024, 640);
            var brand = Descendants<TextBlock>(content).Single(t=>t.Text=="Mono Di Tree CAD");
            Check(studio.Title.EndsWith(AppPaths.IsIsolatedPreview ? " — Mono Di Tree CAD · Geliştirme Önizlemesi" : " — Mono Di Tree CAD · 0.4 Önizleme",StringComparison.Ordinal) && FullyVisibleInCanvas(brand,content,1024,640)
                && brand.ActualWidth >= brand.DesiredSize.Width - .5 && brand.ActualHeight >= brand.DesiredSize.Height - .5,
                "Main window displays the official product name without clipping", checks);
            checks.Add("Offscreen layout and render at 1440x900 and 1024x640");

            // A hidden HWND allows Owner assignment; neither window is shown or activated.
            _ = new WindowInteropHelper(studio).EnsureHandle();
            var beforeImportReview=JsonSerializer.Serialize(studio.Document);
            var importReview=(Window)typeof(MainWindow).GetMethod("BuildImportPreview",PrivateInstance)!.Invoke(studio,[sample.Clone(),new List<string>{"Desteklenmeyen öğe örneği: aktarım notunu onaydan önce inceleyin."},1.0,"kalip.dxf",null])!;
            Render(importReview,Path.ChangeExtension(reportPath,".import-review.png"),650,460);
            Check(JsonSerializer.Serialize(studio.Document)==beforeImportReview && Descendants<TextBlock>((DependencyObject)importReview.Content).Any(t=>t.Text.Contains("100 × 100 mm")) && Descendants<TextBlock>((DependencyObject)importReview.Content).Any(t=>t.Text.Contains("Desteklenmeyen öğe örneği")),"Import review exposes dimensions and loss notes before replacing the document",checks);
            // These warnings must remain visible without scrolling even when many
            // other DXF risks force the compact risk layout. A BLOCK-only file
            // must not be described as containing an INSERT.
            foreach(var scenario in new[] {
                (Label:"block-only", Warnings:new[] { "BLOCK tanım geometrisi doğrudan alındı; yerleşim uygulanmaz." }, Prefix:"Blok tanımları doğrudan alındı;"),
                (Label:"insert-only", Warnings:new[] { "Atlanan desteklenmeyen DXF verileri: ARC, INSERT" }, Prefix:"Blok yerleşimleri (INSERT) aktarılmadı;"),
                (Label:"block-and-insert", Warnings:new[] { "BLOCK tanım geometrisi doğrudan alındı; yerleşim uygulanmaz.", "Atlanan desteklenmeyen DXF verileri: ARC, INSERT", "DXF katman görünürlüğü bilinmiyor: katman tablosu eksik.", "DXF renk bilgileri projede korunmaz." }, Prefix:"Blok yerleşimleri (INSERT) uygulanmadı;")
            })
            {
                var blockReview=(Window)typeof(MainWindow).GetMethod("BuildImportPreview",PrivateInstance)!.Invoke(studio,[sample.Clone(),scenario.Warnings,1.0,scenario.Label+".dxf",null])!;
                Render(blockReview,Path.ChangeExtension(reportPath,".import-"+scenario.Label+".png"),500,380);
                var blockRoot=(FrameworkElement)blockReview.Content;
                var blockNotice=Descendants<TextBlock>(blockRoot).Single(t=>t.Text.StartsWith(scenario.Prefix,StringComparison.Ordinal));
                var blockScroller=Descendants<ScrollViewer>(blockRoot).Single(s=>AutomationProperties.GetName(s)=="DXF aktarım uyarıları");
                var blockCancel=Descendants<Button>(blockRoot).Single(b=>Equals(b.Content,"Vazgeç"));
                var blockAccept=Descendants<Button>(blockRoot).Single(b=>Equals(b.Content,"İçe aktar ve projeyi değiştir"));
                Check(!blockScroller.IsAncestorOf(blockNotice) && FullyVisible(blockNotice,blockRoot)
                    && AutomationProperties.GetName(blockNotice)==blockNotice.Text
                    && FullyVisible(blockCancel,blockRoot) && blockCancel.IsDefault && blockCancel.IsCancel && !blockAccept.IsDefault
                    && JsonSerializer.Serialize(studio.Document)==beforeImportReview,
                    $"DXF {scenario.Label} keeps its specific warning visible outside the scroller and defaults to cancel",checks);
                Render(blockReview,Path.ChangeExtension(reportPath,".import-"+scenario.Label+"-min.png"),484,341);
                Check(FullyVisibleInCanvas(blockNotice,blockRoot,484,341) && FullyVisibleInCanvas(blockCancel,blockRoot,484,341),
                    $"DXF {scenario.Label} warning and cancel remain visible in the smallest client viewport",checks);
                blockReview.Close();
            }
            // Both the standard and controlled parser report color loss using
            // different warning text. The safety summary must cover both,
            // including the compact layout where the other risks scroll.
            foreach(var colorWarning in new[] { "DXF renk bilgileri projede korunmaz; anlamı doğrulayın.", "DXF renkleri projede korunmaz; katman anlamını doğrulayın." })
            foreach(var compact in new[] { false,true })
            {
                var warnings=new List<string> { colorWarning };
                if(compact)
                {
                    warnings.Add("DXF katman görünürlüğü bilinmiyor: katman tablosu eksik.");
                    warnings.Add("DXF çizgi tipi mirası doğrulanamadı.");
                    warnings.Add("DXF plotlanmayan katman bilgisi aktarılmaz.");
                }
                var colorReview=(Window)typeof(MainWindow).GetMethod("BuildImportPreview",PrivateInstance)!.Invoke(studio,
                    [sample.Clone(),warnings,1.0,"renk.dxf",new DxfUnitInfo(4,"milimetre",1,true)])!;
                Render(colorReview,Path.ChangeExtension(reportPath,$".color-{(compact?"compact":"normal")}-{(colorWarning.StartsWith("DXF renk bilgileri",StringComparison.Ordinal)?"legacy":"strict")}.png"),500,380);
                var colorRoot=(FrameworkElement)colorReview.Content;
                var riskScroll=Descendants<ScrollViewer>(colorRoot).FirstOrDefault(s=>AutomationProperties.GetName(s)=="DXF aktarım uyarıları");
                var visibleSummary=Descendants<TextBlock>(colorRoot).Any(t=>t.Text.Contains("Renkler",StringComparison.OrdinalIgnoreCase)
                    && t.Text.Contains("korunmaz",StringComparison.OrdinalIgnoreCase)
                    && (riskScroll is null || !riskScroll.IsAncestorOf(t)) && FullyVisible(t,colorRoot));
                Check(visibleSummary,"DXF color loss stays visible outside the risk scroller in both parser modes",checks);
                colorReview.Close();
            }
            // Use the actual parser output: supported LINE and unsupported ARC must not be
            // presented as a complete import, and reviewing it must not alter the active work.
            var mixedDxf=Path.Combine(work,"mixed-curve-import.dxf");
            File.WriteAllText(mixedDxf,"0\nSECTION\n2\nENTITIES\n0\nLINE\n10\n0\n20\n0\n11\n100\n21\n0\n0\nARC\n10\n50\n20\n50\n40\n20\n50\n0\n51\n90\n0\nENDSEC\n0\nEOF\n");
            var mixed=(DxfImportResult)typeof(MainWindow).GetMethod("PrepareDxfImport",PrivateInstance)!.Invoke(studio,[mixedDxf,1.0])!;
            var fittedDxf=Path.Combine(work,"synthetic-fitted-review.dxf");
            File.WriteAllText(fittedDxf,"0\nSECTION\n2\nENTITIES\n0\nLINE\n10\n0\n20\n0\n11\n10\n21\n0\n0\nPOLYLINE\n70\n1\n0\nVERTEX\n10\n0\n20\n0\n70\n8\n0\nVERTEX\n10\n10\n20\n0\n0\nVERTEX\n10\n0\n20\n10\n0\nSEQEND\n0\nENDSEC\n0\nEOF\n");
            var fitted=(DxfImportResult)typeof(MainWindow).GetMethod("PrepareDxfImport",PrivateInstance)!.Invoke(studio,[fittedDxf,1.0])!;
            var fittedReview=(Window)typeof(MainWindow).GetMethod("BuildImportPreview",PrivateInstance)!.Invoke(studio,[fitted.Project,fitted.Warnings,1.0,"synthetic.dxf",fitted.Units])!;
            Render(fittedReview,Path.ChangeExtension(reportPath,".import-fitted-review.png"),650,460);
            var fittedRoot=(FrameworkElement)fittedReview.Content;
            var fittedNotice=Descendants<TextBlock>(fittedRoot).Single(t=>t.Text.StartsWith("Atlanan çoklu çizgi: 1.",StringComparison.Ordinal));
            Check(fitted.Project.Pieces.Count==1 && fittedNotice.Text.Contains("üretimde kullanmayın") && FullyVisible(fittedNotice,fittedRoot),
                "Skipped fitted POLYLINE count and production warning are visible in preview",checks);
            fittedReview.Close();
            var mixedReview=(Window)typeof(MainWindow).GetMethod("BuildImportPreview",PrivateInstance)!.Invoke(studio,[mixed.Project,mixed.Warnings,1.0,"mixed-curve-import.dxf",mixed.Units])!;
            Render(mixedReview,Path.ChangeExtension(reportPath,".import-curve-review.png"),650,460);
            var mixedRoot=(FrameworkElement)mixedReview.Content;
            var curveNotice=Descendants<TextBlock>(mixedRoot).FirstOrDefault(t=>t.Text.StartsWith("Dikkat:") && t.Text.Contains("ARC") && t.Text.Contains("aktarılmıyor"));
            Check(mixed.Project.Pieces.Count==1 && curveNotice is not null && WarningReachable(curveNotice,mixedRoot)
                && Descendants<TextBlock>(mixedRoot).Any(t=>t.Text.Contains("mevcut projenin yerini alacak"))
                && JsonSerializer.Serialize(studio.Document)==beforeImportReview && studio.SelectedId==preImportSelection
                && (bool)typeof(MainWindow).GetField("dirty",PrivateInstance)!.GetValue(studio)! == preImportDirty
                && documentSession.UndoCount==undoBeforeImport && documentSession.RedoCount==redoBeforeImport,
                "Mixed DXF review visibly reports skipped ARC before replacing unchanged document",checks);
            var visibilityWarnings=new List<string> { "Atlanan desteklenmeyen DXF verileri: ARC, görünmez DXF nesnesi (kontur atlandı), kapalı veya donmuş DXF katmanı (kontur atlandı), kağıt alanı DXF nesnesi (kontur atlandı), model dışı veya belirsiz yerleşim DXF nesnesi (kontur atlandı)" };
            var visibilityReview=(Window)typeof(MainWindow).GetMethod("BuildImportPreview",PrivateInstance)!.Invoke(studio,[mixed.Project,visibilityWarnings,1.0,"visibility.dxf",mixed.Units])!;
            Render(visibilityReview,Path.ChangeExtension(reportPath,".import-visibility-compact.png"),500,380);
            var visibilityRoot=(FrameworkElement)visibilityReview.Content;
            var visibilityNotice=Descendants<TextBlock>(visibilityRoot).Single(t=>t.Text.StartsWith("Dikkat:") && t.Text.Contains("görünmez nesneler"));
            var visibilityCancel=Descendants<Button>(visibilityRoot).Single(b=>Equals(b.Content,"Vazgeç"));
            var visibilityAccept=Descendants<Button>(visibilityRoot).Single(b=>Equals(b.Content,"İçe aktar ve projeyi değiştir"));
            Check(visibilityNotice.Text.Contains("kâğıt alanı") && visibilityNotice.Text.Contains("kapalı veya donmuş katmanlardaki nesneler") && visibilityNotice.Text.Contains("model dışı veya belirsiz yerleşimler")
                && WarningReachable(visibilityNotice,visibilityRoot) && FullyVisible(visibilityCancel,visibilityRoot)
                && visibilityCancel.IsDefault && !visibilityAccept.IsDefault && JsonSerializer.Serialize(studio.Document)==beforeImportReview,
                "Mixed DXF exclusions visibly name hidden and non-model categories and default to cancel",checks);
            visibilityReview.Close();
            var longImportNotes=new List<string> { "Atlanan desteklenmeyen DXF verileri: ARC" };
            longImportNotes.AddRange(Enumerable.Range(0,30).Select(i=>$"Aktarım ayrıntısı {i}: Katman ve kapsam kontrol edilmelidir."));
            longImportNotes.Add("SON AKTARIM NOTU");
            var notesReview=(Window)typeof(MainWindow).GetMethod("BuildImportPreview",PrivateInstance)!.Invoke(studio,[mixed.Project,longImportNotes,1.0,new string('A',230)+".dxf",new DxfUnitInfo(4,"mm",1,true)])!;
            var notesRoot=(FrameworkElement)notesReview.Content;
            foreach(var (width,height) in new[] { (500,380),(484,341) })
            {
                Render(notesReview,Path.ChangeExtension(reportPath,$".dxf-notes-{width}x{height}.png"),width,height);
                var viewer=Descendants<ScrollViewer>(notesRoot).Single(v=>AutomationProperties.GetName(v)=="DXF aktarım bilgileri ve notları");
                var last=Descendants<TextBlock>(viewer).Single(t=>t.Text.Contains("SON AKTARIM NOTU"));
                var notesCancel=Descendants<Button>(notesRoot).Single(b=>Equals(b.Content,"Vazgeç"));
                var notesAccept=Descendants<Button>(notesRoot).Single(b=>Equals(b.Content,"İçe aktar ve projeyi değiştir"));
                Check(viewer.Focusable && KeyboardNavigation.GetIsTabStop(viewer) && !string.IsNullOrWhiteSpace(AutomationProperties.GetHelpText(viewer))
                    && viewer.ScrollableHeight>0 && FullyVisibleInCanvas(notesCancel,notesRoot,width,height) && FullyVisibleInCanvas(notesAccept,notesRoot,width,height),
                    $"DXF import notes are keyboard-focusable and actions fit client viewport {width}x{height}",checks);
                viewer.ScrollToEnd();notesRoot.UpdateLayout();
                Check(Math.Abs(viewer.VerticalOffset-viewer.ScrollableHeight)<1 && FullyVisibleInCanvas(last,notesRoot,width,height),
                    $"Long DXF import notes can reach their last item at client viewport {width}x{height}",checks);
            }
            Check(JsonSerializer.Serialize(studio.Document)==beforeImportReview && !notesReview.IsVisible,
                "Long DXF review keeps the source unchanged and remains hidden",checks);
            notesReview.Close();
            foreach(var (warning,expectedText) in new[] {
                ("DXF katman görünürlüğü bilinmiyor: katman tablosu veya gerekli durum bilgisi eksik. Görünürlük ve viewport ayarlarını doğrulayın.","Katman görünürlüğü bilinmiyor"),
                ("DXF plotlanmayan katman bilgisi aktarılmaz; yazdırma/kesim kapsamını doğrulayın.","Plotlanmayan katman bilgisi aktarılmıyor") })
            {
                var layerReview=(Window)typeof(MainWindow).GetMethod("BuildImportPreview",PrivateInstance)!.Invoke(studio,[mixed.Project,new List<string> { warning },1.0,"layer.dxf",new DxfUnitInfo(4,"mm",1,true)])!;
                Render(layerReview,Path.ChangeExtension(reportPath,expectedText.StartsWith("Katman")?".import-layer-unknown.png":".import-layer-plot.png"),500,380);
                var layerRoot=(FrameworkElement)layerReview.Content;
                var layerNotice=Descendants<TextBlock>(layerRoot).Single(t=>t.Text.Contains(expectedText));
                var layerCancel=Descendants<Button>(layerRoot).Single(b=>Equals(b.Content,"Vazgeç"));
                var layerAccept=Descendants<Button>(layerRoot).Single(b=>Equals(b.Content,"İçe aktar ve projeyi değiştir"));
                Check(WarningReachable(layerNotice,layerRoot) && FullyVisible(layerCancel,layerRoot) && layerCancel.IsDefault && !layerAccept.IsDefault
                    && JsonSerializer.Serialize(studio.Document)==beforeImportReview,
                    $"DXF {expectedText} remains visible and defaults to cancel independently of unit warnings",checks);
                layerReview.Close();
            }
            // A declared inch unit and a millimetre choice must be visible before replacement;
            // the user retains control of the selected factor.
            var inchDxf=Path.Combine(work,"inch-units-import.dxf");
            File.WriteAllText(inchDxf,"0\nSECTION\n2\nHEADER\n9\n$INSUNITS\n70\n1\n0\nENDSEC\n0\nSECTION\n2\nENTITIES\n0\nLINE\n10\n0\n20\n0\n11\n100\n21\n0\n0\nENDSEC\n0\nEOF\n");
            var inch=(DxfImportResult)typeof(MainWindow).GetMethod("PrepareDxfImport",PrivateInstance)!.Invoke(studio,[inchDxf,1.0])!;
            var inchReview=(Window)typeof(MainWindow).GetMethod("BuildImportPreview",PrivateInstance)!.Invoke(studio,[inch.Project,inch.Warnings,1.0,"inch.dxf",inch.Units])!;
            Render(inchReview,Path.ChangeExtension(reportPath,".import-unit-mismatch.png"),650,460);
            var inchRoot=(FrameworkElement)inchReview.Content;
            var mismatch=Descendants<TextBlock>(inchRoot).FirstOrDefault(t=>t.Text.Contains("Birim uyuşmazlığı"));
            var cancelButton=Descendants<Button>(inchRoot).Single(b=>Equals(b.Content,"Vazgeç"));
            Check(mismatch is not null && WarningReachable(mismatch,inchRoot) && cancelButton.IsDefault
                && inch.Project.Pieces[0].Points.Any(p=>p.X==100) && JsonSerializer.Serialize(studio.Document)==beforeImportReview,
                "DXF declared inches and selected millimetres show visible mismatch without changing scale or document",checks);
            // Simultaneous unit mismatch and omitted geometry must both be reachable at
            // minimum dimensions, with cancellation as default.
            var combinedDxf=Path.Combine(work,"combined-units-loss.dxf");
            File.WriteAllText(combinedDxf,"0\nSECTION\n2\nHEADER\n9\n$INSUNITS\n70\n1\n0\nENDSEC\n0\nSECTION\n2\nENTITIES\n0\nLINE\n10\n0\n20\n0\n11\n100\n21\n0\n0\nARC\n10\n50\n20\n50\n40\n20\n50\n0\n51\n90\n0\nENDSEC\n0\nEOF\n");
            var combined=(DxfImportResult)typeof(MainWindow).GetMethod("PrepareDxfImport",PrivateInstance)!.Invoke(studio,[combinedDxf,1.0])!;
            var combinedReview=(Window)typeof(MainWindow).GetMethod("BuildImportPreview",PrivateInstance)!.Invoke(studio,[combined.Project,combined.Warnings,1.0,"combined.dxf",combined.Units])!;
            Render(combinedReview,Path.ChangeExtension(reportPath,".import-combined-compact.png"),500,380);
            var combinedRoot=(FrameworkElement)combinedReview.Content;
            var combinedLoss=Descendants<TextBlock>(combinedRoot).Single(t=>t.Text.StartsWith("Dikkat: Bazı DXF verileri"));
            var combinedMismatch=Descendants<TextBlock>(combinedRoot).Single(t=>t.Text.StartsWith("Birim uyuşmazlığı:"));
            var combinedCancel=Descendants<Button>(combinedRoot).Single(b=>Equals(b.Content,"Vazgeç"));
            var combinedAccept=Descendants<Button>(combinedRoot).Single(b=>Equals(b.Content,"İçe aktar ve projeyi değiştir"));
            Check(WarningReachable(combinedLoss,combinedRoot) && FullyVisible(combinedMismatch,combinedRoot)
                && Descendants<TextBlock>(combinedRoot).Any(t=>t.Text.Contains("bilinen bir ölçüyle karşılaştırın"))
                && !Descendants<ScrollViewer>(combinedRoot).Any(s=>s.IsAncestorOf(combinedMismatch))
                && FullyVisible(combinedCancel,combinedRoot) && FullyVisible(combinedAccept,combinedRoot)
                && combinedCancel.IsDefault && !combinedAccept.IsDefault
                && JsonSerializer.Serialize(studio.Document)==beforeImportReview && studio.SelectedId==preImportSelection
                && (bool)typeof(MainWindow).GetField("dirty",PrivateInstance)!.GetValue(studio)! == preImportDirty
                && documentSession.UndoCount==undoBeforeImport && documentSession.RedoCount==redoBeforeImport,
                "Compact DXF review keeps unit mismatch and skipped geometry reachable with cancel default and unchanged document",checks);
            // A long file name must not obscure the fixed replacement consequence or the
            // first omitted entity; the dimensions must be labelled as bounds, not area.
            var longName=string.Join("_",Enumerable.Repeat("uretim_kalibi",8))+".dxf";
            var longReview=(Window)typeof(MainWindow).GetMethod("BuildImportPreview",PrivateInstance)!.Invoke(studio,[combined.Project,combined.Warnings,1.0,longName,combined.Units])!;
            Render(longReview,Path.ChangeExtension(reportPath,".import-long-name-compact.png"),500,380);
            var longRoot=(FrameworkElement)longReview.Content;
            var replacement=Descendants<TextBlock>(longRoot).Single(t=>t.Text.StartsWith("İçe aktarma mevcut projenin yerini alacak;"));
            var omitted=Descendants<TextBlock>(longRoot).Single(t=>t.Text.StartsWith("Dikkat: Bazı DXF verileri"));
            var dimensions=Descendants<TextBlock>(longRoot).Single(t=>t.Text.Contains("Toplam genişlik × yükseklik:"));
            Check(FullyVisible(replacement,longRoot) && WarningReachable(omitted,longRoot) && omitted.Text.Contains("ARC")
                && dimensions.Text.Contains("100 × 0 mm") && !dimensions.Text.Contains("Toplam alan")
                && Descendants<Button>(longRoot).Single(b=>Equals(b.Content,"Vazgeç")).IsDefault
                && JsonSerializer.Serialize(studio.Document)==beforeImportReview && studio.SelectedId==preImportSelection,
                "Compact long-name DXF review retains fixed replacement and omitted-ARC notice and labels bounds correctly",checks);
            // A missing units header cannot silently imply millimetres or default to acceptance.
            var unknownReview=(Window)typeof(MainWindow).GetMethod("BuildImportPreview",PrivateInstance)!.Invoke(studio,[mixed.Project,mixed.Warnings,1.0,"units-unknown.dxf",mixed.Units])!;
            Render(unknownReview,Path.ChangeExtension(reportPath,".import-unknown-compact.png"),500,380);
            var unknownRoot=(FrameworkElement)unknownReview.Content;
            var unknownNotice=Descendants<TextBlock>(unknownRoot).Single(t=>t.Text.StartsWith("Birim doğrulanamadı:"));
            var unknownCancel=Descendants<Button>(unknownRoot).Single(b=>Equals(b.Content,"Vazgeç"));
            var unknownAccept=Descendants<Button>(unknownRoot).Single(b=>Equals(b.Content,"İçe aktar ve projeyi değiştir"));
            Check(FullyVisible(unknownNotice,unknownRoot) && Descendants<TextBlock>(unknownRoot).Any(t=>t.Text.Contains("bilinen bir ölçüyle karşılaştırın"))
                && !Descendants<ScrollViewer>(unknownRoot).Any(s=>s.IsAncestorOf(unknownNotice)) && FullyVisible(unknownCancel,unknownRoot)
                && unknownCancel.IsDefault && !unknownAccept.IsDefault && JsonSerializer.Serialize(studio.Document)==beforeImportReview,
                "Compact DXF review visibly flags unknown units and defaults to cancellation without changing document",checks);
            var lineTypeReview=(Window)typeof(MainWindow).GetMethod("BuildImportPreview",PrivateInstance)!.Invoke(studio,[mixed.Project,
                new[] { "DXF çizgi tipi mirası doğrulanamadı (katman tipi eksik veya BYBLOCK); bu geometri düz çizgi olarak gösterilir." },1.0,"linetype.dxf",mixed.Units])!;
            Render(lineTypeReview,Path.ChangeExtension(reportPath,".import-linetype-compact.png"),500,380);
            var lineTypeRoot=(FrameworkElement)lineTypeReview.Content;
            var lineTypeNotice=Descendants<TextBlock>(lineTypeRoot).Single(t=>t.Text.Contains("Çizgi tipi doğrulanamadı"));
            var lineTypeCancel=Descendants<Button>(lineTypeRoot).Single(b=>Equals(b.Content,"Vazgeç"));
            Check(WarningReachable(lineTypeNotice,lineTypeRoot) && FullyVisible(lineTypeCancel,lineTypeRoot) && lineTypeCancel.IsDefault,
                "Compact DXF review prominently warns when inherited line type is unknown",checks);
            // A branched LINE junction cannot be chained deterministically. The importer
            // must disclose its conservative separate-line result before replacement.
            var junctionDxf=Path.Combine(work,"ambiguous-line-import.dxf");
            File.WriteAllText(junctionDxf,"0\nSECTION\n2\nHEADER\n9\n$INSUNITS\n70\n4\n0\nENDSEC\n0\nSECTION\n2\nENTITIES\n0\nLINE\n8\n0\n10\n0\n20\n0\n11\n10\n21\n0\n0\nLINE\n8\n0\n10\n10\n20\n0\n11\n20\n21\n0\n0\nLINE\n8\n0\n10\n10\n20\n0\n11\n10\n21\n10\n0\nENDSEC\n0\nEOF\n");
            var junction=(DxfImportResult)typeof(MainWindow).GetMethod("PrepareDxfImport",PrivateInstance)!.Invoke(studio,[junctionDxf,1.0])!;
            var junctionReview=(Window)typeof(MainWindow).GetMethod("BuildImportPreview",PrivateInstance)!.Invoke(studio,[junction.Project,junction.Warnings,1.0,"ambiguous-line-import.dxf",junction.Units])!;
            Render(junctionReview,Path.ChangeExtension(reportPath,".import-junction-compact.png"),500,380);
            var junctionRoot=(FrameworkElement)junctionReview.Content;
            var junctionNotice=Descendants<TextBlock>(junctionRoot).Single(t=>t.Text.StartsWith("Belirsiz LINE bağlantıları birleştirilmedi",StringComparison.Ordinal));
            var junctionCancel=Descendants<Button>(junctionRoot).Single(b=>Equals(b.Content,"Vazgeç"));
            var junctionAccept=Descendants<Button>(junctionRoot).Single(b=>Equals(b.Content,"İçe aktar ve projeyi değiştir"));
            Check(junction.Project.Pieces.Count==3 && junction.Project.Pieces.All(p=>!p.Closed && p.Points.Count==2)
                && junction.Warnings.Any(w=>w.StartsWith("Belirsiz LINE bağlantıları:",StringComparison.Ordinal))
                && junctionNotice.Text.Contains("ayrı parçalar") && WarningReachable(junctionNotice,junctionRoot)
                && FullyVisible(junctionCancel,junctionRoot) && junctionCancel.IsDefault && !junctionAccept.IsDefault
                && JsonSerializer.Serialize(studio.Document)==beforeImportReview,
                "Ambiguous LINE review keeps every segment separate and defaults to cancellation at compact size",checks);
            var combinedJunctionWarnings=junction.Warnings.Concat(["Atlanan desteklenmeyen DXF verileri: ARC"]).ToList();
            var combinedJunctionReview=(Window)typeof(MainWindow).GetMethod("BuildImportPreview",PrivateInstance)!.Invoke(studio,[junction.Project,combinedJunctionWarnings,1.0,"ambiguous-and-unknown.dxf",null])!;
            Render(combinedJunctionReview,Path.ChangeExtension(reportPath,".import-junction-combined.png"),500,380);
            var combinedJunctionRoot=(FrameworkElement)combinedJunctionReview.Content;
            var combinedJunctionNotice=Descendants<TextBlock>(combinedJunctionRoot).Single(t=>t.Text.StartsWith("Belirsiz LINE bağlantıları birleştirilmedi",StringComparison.Ordinal));
            var combinedJunctionCancel=Descendants<Button>(combinedJunctionRoot).Single(b=>Equals(b.Content,"Vazgeç"));
            Check(WarningReachable(combinedJunctionNotice,combinedJunctionRoot) && FullyVisible(combinedJunctionCancel,combinedJunctionRoot)
                && combinedJunctionCancel.IsDefault && JsonSerializer.Serialize(studio.Document)==beforeImportReview,
                "Combined ambiguous LINE, skipped entity and unknown unit review retains visible cancellation at compact size",checks);
            var riskScroller=Descendants<ScrollViewer>(combinedJunctionRoot).Single(s=>AutomationProperties.GetName(s)=="DXF aktarım uyarıları");
            var riskSummary=Descendants<TextBlock>(combinedJunctionRoot).Single(t=>t.Text.StartsWith("Aktarım uyarıları var;",StringComparison.Ordinal) || t.Text.StartsWith("Renkler korunmaz; diğer aktarım uyarılarını",StringComparison.Ordinal));
            Check(FullyVisible(riskSummary,combinedJunctionRoot) && FullyVisible(riskScroller,combinedJunctionRoot)
                && riskScroller.Focusable && KeyboardNavigation.GetIsTabStop(riskScroller) && riskScroller.ScrollableHeight>0,
                "Multiple DXF risks retain a visible summary and keyboard-scrollable details",checks);
            var combinedTabs=Descendants<TabControl>(combinedJunctionRoot).Single();combinedTabs.SelectedIndex=1;combinedJunctionRoot.UpdateLayout();
            var combinedList=Descendants<ListBox>(combinedJunctionRoot).Single(l=>AutomationProperties.GetName(l)=="İçe alınacak tüm parça adları");
            combinedList.SelectedIndex=0;
            Descendants<Button>(combinedJunctionRoot).Single(b=>Equals(b.Content,"Parçayı gör")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Render(combinedJunctionReview,Path.ChangeExtension(reportPath,".import-multiple-risks-piece.png"),500,380);
            var combinedPiece=Descendants<DxfPiecePreview>(combinedJunctionRoot).Single();
            Check(combinedPiece.DisplayedPointCount>0 && combinedPiece.ActualHeight>=70 && FullyVisible(combinedPiece,combinedJunctionRoot)
                && FullyVisible(combinedJunctionCancel,combinedJunctionRoot),"Multiple DXF risks leave usable space to inspect selected geometry",checks);
            Render(combinedJunctionReview,Path.ChangeExtension(reportPath,".import-multiple-risks-piece-min.png"),484,341);
            var combinedDetails=Descendants<TextBox>(combinedJunctionRoot).Single(t=>AutomationProperties.GetName(t)=="İçe alınacak parça geometrisi ve ölçüleri");
            var firstCharacter=combinedDetails.GetRectFromCharacterIndex(0);
            Check(FullyVisibleInCanvas(combinedJunctionCancel,combinedJunctionRoot,484,341) && FullyVisibleInCanvas(riskSummary,combinedJunctionRoot,484,341)
                && riskScroller.Focusable && combinedPiece.ActualHeight>=40 && FullyVisibleInCanvas(combinedPiece,combinedJunctionRoot,484,341)
                && !firstCharacter.IsEmpty && firstCharacter.Top>=0 && firstCharacter.Bottom<=combinedDetails.ActualHeight,
                "Multiple DXF risks keep cancellation, summary and geometry reachable in the smallest client viewport",checks);
            var hiddenAndArc=(Window)typeof(MainWindow).GetMethod("BuildImportPreview",PrivateInstance)!.Invoke(studio,[junction.Project,
                new[] { "Atlanan desteklenmeyen DXF verileri: ARC, negatif renk kodlu kapalı DXF nesnesi" },1.0,"mixed.dxf",junction.Units])!;
            Render(hiddenAndArc,Path.ChangeExtension(reportPath,".import-hidden-and-arc.png"),500,380);
            var hiddenRoot=(FrameworkElement)hiddenAndArc.Content;
            var hiddenNotice=Descendants<TextBlock>(hiddenRoot).Single(t=>t.Text.Contains("negatif renk kodlu kapalı nesneler"));
            Check(WarningReachable(hiddenNotice,hiddenRoot) && hiddenNotice.Text.Contains("Diğer ayrıntılar aktarım notlarında"),
                "DXF review identifies hidden negative-color entities even when ARC is also skipped",checks);
            var textiles = new TextileToolsWindow(studio);
            var verificationQueue = new SimulationQueue(Path.Combine(work, "spool"));
            var machine = new MachineCenterWindow(studio, verificationQueue);
            foreach (var window in new Window[] { textiles, machine })
            {
                var root = (FrameworkElement)window.Content;
                root.Measure(new Size(window.Width, window.Height));
                root.Arrange(new Rect(0, 0, window.Width, window.Height));
                root.UpdateLayout();
                Check(root.DesiredSize.Width > 0, window.GetType().Name + " construction and layout", checks);
                Render(window, Path.ChangeExtension(reportPath, "." + window.GetType().Name + ".png"), (int)window.Width, (int)window.Height);
                Render(window, Path.ChangeExtension(reportPath, "." + window.GetType().Name + ".compact.png"), 1024,640);
            }
            var gradeTable = (DataGrid)typeof(TextileToolsWindow).GetField("grades", PrivateInstance)!.GetValue(textiles)!;
            Check(gradeTable.Items.Count == 4 && gradeTable.Columns.Count == 5 && gradeTable.Columns.All(c => c.ActualWidth > 0),
                "Grade table has visible sized columns and source rows", checks);
            VerifyCompactGradingViewport(gradeTable,(FrameworkElement)textiles.Content,checks);
            var gradeButton = Descendants<Button>((DependencyObject)textiles.Content).Single(b => Equals(b.Content, "Yeni bedeni projeye ekle"));
            var basePiece=studio.Selected!.Clone();
            foreach(var row in gradeTable.Items.Cast<TextileToolsWindow.GradeRow>()) { row.Dx="1";row.Dy="2"; }
            var applyGradeButton=Descendants<Button>((DependencyObject)textiles.Content).Single(b=>Equals(b.Content,"Artışları projeye uygula"));
            Check(FullyVisible(applyGradeButton,(FrameworkElement)textiles.Content) && FullyVisible(gradeButton,(FrameworkElement)textiles.Content),
                "Compact grading apply and create buttons are fully visible",checks);
            applyGradeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(studio.Document.GradingRules.Count==1 && studio.Document.GradingRules[0].SourcePieceId==basePiece.Id
                && studio.Document.GradingRules[0].Deltas.All(p=>p==new Point2(1,2)),"Applying grading table stores source and increments",checks);
            Invoke(studio,"Undo");
            Check(studio.Document.GradingRules.Count==0,"Undo removes applied grading rules",checks);
            Invoke(studio,"Redo");
            Check(studio.Document.GradingRules.Count==1 && studio.Document.GradingRules[0].Deltas.All(p=>p==new Point2(1,2)),"Redo restores applied grading rules",checks);
            // Undo/redo replaces the owned document. Rebind the hidden workshop as
            // opening it again would; its old ComboBox entries are detached snapshots.
            typeof(TextileToolsWindow).GetMethod("RefreshSources",PrivateInstance)!.Invoke(textiles,[basePiece.Id,true]);
            ClickAndWaitForGrade(textiles,gradeButton);
            Check(studio.Document.Pieces.Count == 3 && studio.Selected?.Name.EndsWith("L") == true,
                "Grading command adds and selects a generated piece", checks);
            var gradeResult=(TextBlock)typeof(TextileToolsWindow).GetField("result",PrivateInstance)!.GetValue(textiles)!;
            Check(studio.Selected!.Metadata is null && gradeResult.Text.Contains("rol, beden, malzeme ve adet bilgileri aktarılmadı")
                && gradeResult.Text.Contains("Adetli pastalda") && gradeResult.Text.Contains("Parça bilgileri")
                && AutomationProperties.GetHelpText(gradeButton).Contains("otomatik aktarılmaz"),
                "Generated size clearly discloses metadata is not copied and quantity setup is required",checks);
            var sizeBox=Descendants<TextBox>((DependencyObject)textiles.Content).Single(b=>System.Windows.Automation.AutomationProperties.GetName(b)=="Yeni beden adı");
            var stepBox=Descendants<TextBox>((DependencyObject)textiles.Content).Single(b=>System.Windows.Automation.AutomationProperties.GetName(b)=="Temel bedenden adım sayısı");
            sizeBox.Text="XL";stepBox.Text="2";ClickAndWaitForGrade(textiles,gradeButton);
            Check(studio.Selected!.Points.Select((p,i)=>p==new Point2(basePiece.Points[i].X+2,basePiece.Points[i].Y+4)).All(v=>v),"Sequential sizes retain base and grading increments",checks);
            var afterSizes=studio.Document.Pieces.Count;ClickAndWaitForGrade(textiles,gradeButton);
            Check(studio.Document.Pieces.Count==afterSizes,"Duplicate size name does not create extra geometry",checks);
            sizeBox.Text="Sıfır";stepBox.Text="0";ClickAndWaitForGrade(textiles,gradeButton);
            Check(studio.Document.Pieces.Count==afterSizes+1 && studio.Selected!.Metadata is null
                && gradeResult.Text.Contains("geometri temel parçayla aynı")
                && gradeResult.Text.Contains("rol, beden, malzeme ve adet bilgileri aktarılmadı") && gradeResult.Text.Contains("Adetli pastalda"),
                "Unchanged generated size warns about identical geometry and missing quantity metadata",checks);
            Invoke(studio,"Undo");
            Check(studio.Document.Pieces.Count==afterSizes,"Undo removes zero-step generated size",checks);
            studio.Select(basePiece.Id);
            machine=new MachineCenterWindow(studio,verificationQueue);
            var machineSelection=machine.SelectedPieceIds.ToArray();
            Check(machineSelection.Length==1 && machineSelection[0]==basePiece.Id,"Machine selection explicitly retains the chosen source piece",checks);
            var beforePreparation=JsonSerializer.Serialize(studio.Document);
            ((TextBox)typeof(MachineCenterWindow).GetField("scale",PrivateInstance)!.GetValue(machine)!).Text="2";
            var machinePrepare=typeof(MachineCenterWindow).GetMethod("Prepare",PrivateInstance)!;
            var preparingField=typeof(MachineCenterWindow).GetField("preparing",PrivateInstance)!;
            var prepareButton=(Button)typeof(MachineCenterWindow).GetField("prepare",PrivateInstance)!.GetValue(machine)!;
            VerifyMachineCompactFooter(machine,reportPath,"ready",checks);
            var previousMachineContext=SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(studio.Dispatcher));
            var prepareTask=(Task)machinePrepare.Invoke(machine,null)!;
            Check((bool)preparingField.GetValue(machine)! && !prepareButton.IsEnabled,
                "Machine preparation runs asynchronously with duplicate requests disabled",checks);
            var duplicate=(Task)machinePrepare.Invoke(machine,null)!;
            Check(duplicate.IsCompleted,"Machine ignores a second preparation while busy",checks);
            PumpUntil(()=>prepareTask.IsCompleted);
            prepareTask.GetAwaiter().GetResult();
            Check(!(bool)preparingField.GetValue(machine)!,"Machine preparation restores controls after completion",checks);
            Check(verificationQueue.Jobs.Count == 1 && verificationQueue.Jobs[0].Status == JobStatus.Prepared,
                "Machine UI command creates a local exchange job", checks);
            Check(!verificationQueue.Jobs[0].SourceSnapshotUnconfirmed && !new SimulationQueue(Path.Combine(work,"spool")).Jobs[0].SourceSnapshotUnconfirmed,
                "Unchanged source confirmation persists in the local job manifest",checks);
            Check(verificationQueue.Jobs[0].PieceCount==1 && JsonSerializer.Serialize(studio.Document)==beforePreparation,"Machine prepares only selected pieces without mutating the source",checks);
            var geometryPreview=(OutputPreview)typeof(MachineCenterWindow).GetField("geometryPreview",PrivateInstance)!.GetValue(machine)!;
            var expectedBounds=TekstilCAD.Core.Geometry.Bounds(basePiece.Points);
            var machinePreviewValid=geometryPreview.HasGeometry && geometryPreview.PathCount==1 && geometryPreview.PreviewBounds is { } actualBounds
                && Math.Abs(actualBounds.Width-expectedBounds.Width*2)<0.000001 && Math.Abs(actualBounds.Height-expectedBounds.Height*2)<0.000001;
            var machinePreviewCheck="Machine preview uses the prepared file and its actual scaled millimetre bounds";
            if(!machinePreviewValid)
            {
                var previewSummary=(TextBlock)typeof(MachineCenterWindow).GetField("previewSummary",PrivateInstance)!.GetValue(machine)!;
                machinePreviewCheck+=$" (previewSummary: {previewSummary.Text}; HasGeometry: {geometryPreview.HasGeometry}; PathCount: {geometryPreview.PathCount})";
            }
            Check(machinePreviewValid,machinePreviewCheck,checks);
            ((Button)typeof(MachineCenterWindow).GetField("simulate",PrivateInstance)!.GetValue(machine)!)
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(verificationQueue.Jobs[0].Status == JobStatus.Simulated, "Machine UI verifies checksum without hardware", checks);
            Render(machine,Path.ChangeExtension(reportPath,".MachineCenterPrepared.png"),(int)machine.Width,(int)machine.Height);
            // Exercise failure and an edit during preparation in a separate, local-only queue.
            var asyncQueue=new SimulationQueue(Path.Combine(work,"async-machine-spool"));
            var asyncMachine=new MachineCenterWindow(studio,asyncQueue);
            var asyncScale=(TextBox)typeof(MachineCenterWindow).GetField("scale",PrivateInstance)!.GetValue(asyncMachine)!;
            var asyncResult=(TextBlock)typeof(MachineCenterWindow).GetField("result",PrivateInstance)!.GetValue(asyncMachine)!;
            var asyncPrepareButton=(Button)typeof(MachineCenterWindow).GetField("prepare",PrivateInstance)!.GetValue(asyncMachine)!;
            var beforeFailure=JsonSerializer.Serialize(studio.Document);
            asyncScale.Text="0";
            VerifyMachineCompactFooter(asyncMachine,reportPath,"invalid-scale",checks);
            var failedPrepare=(Task)machinePrepare.Invoke(asyncMachine,null)!;
            PumpUntil(()=>failedPrepare.IsCompleted);failedPrepare.GetAwaiter().GetResult();
            Check(asyncResult.Text.StartsWith("İşlem başarısız:") && !(bool)preparingField.GetValue(asyncMachine)!
                && asyncScale.IsEnabled && !asyncPrepareButton.IsEnabled && asyncQueue.Jobs.Count==0
                && JsonSerializer.Serialize(studio.Document)==beforeFailure,
                "Invalid asynchronous machine preparation reports failure, restores controls and leaves source and queue untouched",checks);
            asyncScale.Text="1";
            Check(asyncPrepareButton.IsEnabled,"Machine preparation can be retried after correcting invalid input",checks);
            var originalProjectName=studio.Document.Name;
            var stalePrepare=(Task)machinePrepare.Invoke(asyncMachine,null)!;
            Check((bool)preparingField.GetValue(asyncMachine)! && !asyncScale.IsEnabled && !asyncPrepareButton.IsEnabled,
                "Machine preparation disables profile editing while work is in progress",checks);
            studio.Commit("Preparation source change",()=>studio.Document.Name=originalProjectName+" değişti");
            PumpUntil(()=>stalePrepare.IsCompleted);stalePrepare.GetAwaiter().GetResult();
            Check(asyncQueue.Jobs.Count==1 && asyncQueue.Jobs[0].ProjectName==originalProjectName
                && asyncQueue.Jobs[0].Status==JobStatus.Prepared && asyncResult.Text.Contains("önceki seçim ve proje kopyasından")
                && asyncResult.Text.Contains("Kaynak doğrulanmadı") && asyncResult.Text.Contains("yeniden hazırlayın")
                && asyncQueue.Jobs[0].SourceSnapshotUnconfirmed
                && !(bool)preparingField.GetValue(asyncMachine)! && asyncScale.IsEnabled && !asyncPrepareButton.IsEnabled,
                "Machine preparation warns about a changed source and keeps the prepared snapshot",checks);
            var reopenedAsyncQueue=new SimulationQueue(Path.Combine(work,"async-machine-spool"));
            Check(reopenedAsyncQueue.Jobs.Count==1 && reopenedAsyncQueue.Jobs[0].SourceSnapshotUnconfirmed,
                "Unconfirmed source warning survives local queue reload",checks);
            var rejectedStale=(Task)machinePrepare.Invoke(asyncMachine,null)!;
            PumpUntil(()=>rejectedStale.IsCompleted);rejectedStale.GetAwaiter().GetResult();
            Check(asyncQueue.Jobs.Count==1 && asyncResult.Text.Contains("Kaynak proje değişti") && !asyncPrepareButton.IsEnabled,
                "A stale machine selection cannot prepare another job",checks);
            VerifyMachineCompactFooter(asyncMachine,reportPath,"stale-source",checks);
            typeof(MachineCenterWindow).GetMethod("Simulate",PrivateInstance)!.Invoke(asyncMachine,null);
            Check(asyncQueue.Jobs[0].Status==JobStatus.Simulated && asyncQueue.Jobs[0].SourceSnapshotUnconfirmed
                && asyncResult.Text.Contains("bütünlüğü doğrulandı") && asyncResult.Text.Contains("önceki proje kopyasından")
                && asyncResult.Text.Contains("yeniden hazırlayın") && asyncResult.Foreground==StudioTheme.Warning
                && new SimulationQueue(Path.Combine(work,"async-machine-spool")).Jobs[0].SourceSnapshotUnconfirmed,
                "Checksum verification keeps the unconfirmed source warning in the visible result and persisted job",checks);
            VerifyMachineCompactFooter(asyncMachine,reportPath,"stale-verified",checks);
            Invoke(studio,"Undo");
            Check(!asyncMachine.IsVisible && asyncQueue.Jobs.All(j=>j.OutputPath.StartsWith(work,StringComparison.OrdinalIgnoreCase)),
                "Asynchronous machine verification stays hidden and uses isolated local output",checks);
            SynchronizationContext.SetSynchronizationContext(previousMachineContext);
            VerifyHpglPreview(work,checks);
            var gradingPath=Path.Combine(work,"grading-roundtrip.tcad");
            Invoke(studio,"SaveTo",gradingPath);
            var reopenedProject=ProjectStorage.Load(gradingPath);
            Check(reopenedProject.GradingRules.Count==1 && reopenedProject.GradingRules[0].SourcePieceId==basePiece.Id
                && reopenedProject.GradingRules[0].Deltas.All(p=>p==new Point2(1,2)),"Desktop save and reopen preserve applied grading rules",checks);
            studio.LoadDocument(reopenedProject);studio.Select(basePiece.Id);
            var reopenedTextiles=new TextileToolsWindow(studio);
            var reopenedGrid=(DataGrid)typeof(TextileToolsWindow).GetField("grades",PrivateInstance)!.GetValue(reopenedTextiles)!;
            Check(reopenedGrid.Items.Cast<TextileToolsWindow.GradeRow>().Count()==basePiece.Points.Count
                && reopenedGrid.Items.Cast<TextileToolsWindow.GradeRow>().All(row=>row.Dx=="1" && row.Dy=="2"),
                "Reopened grading table displays saved increments",checks);
            VerifyMetadataWorkflow(studio,work,checks);
            VerifyOutputAndNestingDisclosures(studio,checks);
            VerifyDxfLayerNotice(studio,checks);
            VerifyDxfLayerReview(studio,reportPath,checks);
            VerifyHpglReviewWindow(studio,reportPath,checks);
            VerifyAsyncDxfImport(studio,work,checks);
            VerifyAsyncDxfBulgeImport(studio,work,reportPath,checks);
            VerifyDxfGrainlineReview(studio,work,checks);
            VerifyDxfGrainlineReviewControls(studio,reportPath,checks);
            VerifyDxfImportCancellation(studio,work,checks);
            VerifyDxfSourceCheckWorkflow(studio,work,checks);
            VerifyDxfReviewRetry(studio,work,checks);
            VerifyBasicNestingLengthPrecision(studio,checks);
            VerifyNestingAndGrading(studio,checks);
            VerifyConcaveAllowance(studio,checks);
            VerifyProductionPreview(studio,reportPath,work,checks);
            VerifyNestingHistory(studio,work,checks);
            Check(!studio.IsVisible && !textiles.IsVisible && !machine.IsVisible && !reopenedTextiles.IsVisible, "No window shown", checks);
            Check(AppPaths.PrepareProjectsDirectory().StartsWith(work, StringComparison.OrdinalIgnoreCase),
                "Runtime data directory isolated from installation and verification stays local", checks);
            var showcase = (ProjectDocument)typeof(MainWindow).GetMethod("Sample", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null)!;
            studio.LoadDocument(showcase); studio.Select(showcase.Pieces[0].Id);
            Render(studio, Path.ChangeExtension(reportPath, ".showcase.png"), 1440, 900);
        }
        catch (Exception ex) { failure = ex.ToString(); }
        finally
        {
            if (studio is not null)
            {
                StopTimer(studio);
                // A failed check can leave an incomplete drawing. Clear only the
                // verification canvas before shutdown so failure reporting cannot
                // open a discard dialog and strand the hidden verifier.
                try { (typeof(MainWindow).GetField("canvas",PrivateInstance)?.GetValue(studio) as CadCanvas)?.CancelDrawing(); }
                catch(Exception cleanupError) when(cleanupError is not OutOfMemoryException and not StackOverflowException and not AccessViolationException)
                { failure ??= "Verification cleanup failed: "+cleanupError; }
                // Shutdown must not prompt about verification-only edits.
                typeof(MainWindow).GetField("dirty", PrivateInstance)?.SetValue(studio, false);
                // A never-shown window may not raise Closing. Release its verification-only recovery lease explicitly.
                WaitRecovery(studio.SetRecoveryForVerificationAsync(null,null));
            }
        }
        try
        {
            reportPath = AllowedReportPath(reportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
            File.WriteAllText(reportPath, JsonSerializer.Serialize(new { status = failure is null ? "PASS" : "FAIL", checks, failure, imagePath,
                scope = "Background construction/layout and command checks only. No interactive input, dialogs, physical machine, or full workflow certification." }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { return 2; }
        return failure is null ? 0 : 1;
    }
    private static string AllowedReportPath(string path)
    {
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("Absolute report path required.");
        ProjectStorage.EnsureWritableDestination(path);
        var full = Path.GetFullPath(path);
        var workRoot=Path.Combine(RepositoryRoot(full),"work")+Path.DirectorySeparatorChar;
        if (!full.StartsWith(workRoot, StringComparison.OrdinalIgnoreCase)
            && !full.StartsWith(@"C:\Users\pc\Documents\Codex\2026-10-31\a-2\outputs\", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Verification reports must be in TekstilCAD work or the authorized outputs folder.");
        if (!string.Equals(Path.GetExtension(full), ".json", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("JSON report extension required.");
        return full;
    }
    private static string RepositoryRoot(string reportPath)
    {
        foreach(var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory, Path.GetDirectoryName(reportPath)! })
        {
            for(DirectoryInfo? directory=new DirectoryInfo(start);directory is not null;directory=directory.Parent)
                if(File.Exists(Path.Combine(directory.FullName,"AGENTS.md")) && File.Exists(Path.Combine(directory.FullName,"TekstilCAD.slnx")))return directory.FullName;
        }
        throw new InvalidOperationException("Verification requires the TekstilCAD checkout so its data remains inside the authorized work directory.");
    }
    private static void VerifyKeyboardCommands(MainWindow studio,CadCanvas canvas,List<string> checks)
    {
        var session=(DocumentSession)typeof(MainWindow).GetField("session",PrivateInstance)!.GetValue(studio)!;
        var move=typeof(CadCanvas).GetMethod("MoveSelectionByKeyboard",PrivateInstance)!;
        var selected=studio.Selected!.Id;
        foreach(var (key,modifiers,dx,dy,label) in new[]
        {
            (Key.Right,ModifierKeys.None,1.0,0.0,"1 mm"),
            (Key.Up,ModifierKeys.Shift,0.0,-10.0,"Shift 10 mm"),
            (Key.Left,ModifierKeys.Control,-0.1,0.0,"Ctrl 0.1 mm")
        })
        {
            var points=studio.Selected!.Points.ToArray();var undoCount=session.UndoCount;
            move.Invoke(canvas,[key,modifiers]);
            Check(studio.Selected!.Points.SequenceEqual(points.Select(p=>new Point2(p.X+dx,p.Y+dy))) && session.UndoCount==undoCount+1,"Canvas keyboard command "+label+" records one edit",checks);
            Invoke(studio,"Undo");
            Check(studio.SelectedId==selected && studio.Selected!.Points.SequenceEqual(points),"Canvas keyboard undo restores geometry and selection "+label,checks);
        }
        var unchanged=JsonSerializer.Serialize(studio.Document);var historyCount=session.UndoCount;
        move.Invoke(canvas,[Key.Right,ModifierKeys.Control|ModifierKeys.Shift]);
        move.Invoke(canvas,[Key.Right,ModifierKeys.Alt]);
        studio.Select(null);move.Invoke(canvas,[Key.Right,ModifierKeys.None]);studio.Select(selected);
        var drawing=(List<Point2>)typeof(CadCanvas).GetField("drawing",PrivateInstance)!.GetValue(canvas)!;
        drawing.Add(new(0,0));move.Invoke(canvas,[Key.Right,ModifierKeys.None]);canvas.CancelDrawing();
        Check(JsonSerializer.Serialize(studio.Document)==unchanged && session.UndoCount==historyCount,"Canvas ignores unsupported modifiers, missing selection and active preview",checks);
        var redoCount=session.RedoCount;
        var status=(TextBlock)typeof(MainWindow).GetField("status",PrivateInstance)!.GetValue(studio)!;
        foreach(var fieldName in new[] { "largeVertexCancellation","largeCompletionCancellation" })
        {
            var field=typeof(CadCanvas).GetField(fieldName,PrivateInstance)!;using var cts=new CancellationTokenSource();field.SetValue(canvas,cts);
            try
            {
                move.Invoke(canvas,[Key.Right,ModifierKeys.None]);
                Check(JsonSerializer.Serialize(studio.Document)==unchanged && session.UndoCount==historyCount && session.RedoCount==redoCount
                    && studio.SelectedId==selected && status.Text.Contains("Doğrulama sürüyor"),
                    "Keyboard movement waits for "+fieldName+" without modifying the project",checks);
            }
            finally { field.SetValue(canvas,null); }
        }
        var peer=UIElementAutomationPeer.CreatePeerForElement(canvas);
        Check(peer is not null && peer.GetName()=="Kalıp çalışma alanı" && !string.IsNullOrWhiteSpace(peer.GetHelpText()),"Canvas exposes accessible name and keyboard guidance",checks);
        var source=studio.Selected!;
        var original=studio.Document.Clone();
        // The earlier precision probe intentionally used decimal fractions. A
        // one-millimetre move of those coordinates need not have exactly the
        // same binary displacement at every vertex, so use an exact fixture for
        // the successful rebase; the separate proof test covers rounded moves.
        studio.Commit("Tam taşıma testi",()=> {
            var target=studio.Document.Pieces.Single(p=>p.Id==source.Id);
            target.Closed=true;target.Points=[new(0,0),new(10,0),new(10,10),new(0,10)];
            target.Grainline=new Grainline { Start=new(1,1),End=new(1,5),NeedsReview=true };
        });
        source=studio.Selected!;
        var beforeRule=studio.Document.Clone();
        studio.Commit("Serileme testi",()=> {
            studio.Document.GradingRules.RemoveAll(r=>r.SourcePieceId==source.Id);
            studio.Document.GradingRules.Add(new GradingRuleSet { SourcePieceId=source.Id,SourceClosed=source.Closed,
                SourcePoints=[..source.Points],Deltas=source.Points.Select(_=>new Point2(1,2)).ToList() });
        });
        var beforeMove=studio.Document.Clone();
        var tools=new TextileToolsWindow(studio);
        var table=(DataGrid)typeof(TextileToolsWindow).GetField("grades",PrivateInstance)!.GetValue(tools)!;
        var draftRow=table.Items.Cast<TextileToolsWindow.GradeRow>().First();draftRow.Dx="7";
        move.Invoke(canvas,[Key.Right,ModifierKeys.None]);
        var movedRule=studio.Document.GradingRules.Single(r=>r.SourcePieceId==source.Id);
        Check(movedRule.SourcePoints.SequenceEqual(studio.Selected!.Points) && movedRule.Deltas.SequenceEqual(beforeMove.GradingRules.Single(r=>r.SourcePieceId==source.Id).Deltas),
            "Keyboard movement carries matching grading increments with the source",checks);
        studio.Commit("İplik yönü değişti",()=>studio.Document.Pieces.Single(p=>p.Id==source.Id).Grainline!.End=new Point2(3,5));
        var badGrainRejected=false;
        try { typeof(TextileToolsWindow).GetMethod("RebaseMovedGradeDraft",PrivateInstance)!.Invoke(tools,null); }
        catch(System.Reflection.TargetInvocationException ex) when(ex.InnerException is InvalidOperationException) { badGrainRejected=true; }
        Check(badGrainRejected && table.Items.Cast<TextileToolsWindow.GradeRow>().First().Dx=="7",
            "Draft rebase rejects independently edited grainline without losing entered values",checks);
        Invoke(studio,"Undo");
        studio.Commit("Parça bilgisi değişti",()=>studio.Document.Pieces.Single(p=>p.Id==source.Id).Name+=" değişti");
        var rejected=false;
        try { typeof(TextileToolsWindow).GetMethod("RebaseMovedGradeDraft",PrivateInstance)!.Invoke(tools,null); }
        catch(System.Reflection.TargetInvocationException ex) when(ex.InnerException is InvalidOperationException) { rejected=true; }
        Check(rejected && table.Items.Cast<TextileToolsWindow.GradeRow>().First().Dx=="7",
            "Draft rebase rejects other source changes without losing entered values",checks);
        Invoke(studio,"Undo");
        typeof(TextileToolsWindow).GetMethod("RebaseMovedGradeDraft",PrivateInstance)!.Invoke(tools,null);
        Check(table.Items.Cast<TextileToolsWindow.GradeRow>().First().Dx=="7" &&
            (bool)typeof(TextileToolsWindow).GetMethod("SavedRuleMatchesDraft",PrivateInstance)!.Invoke(tools,[source.Id,typeof(TextileToolsWindow).GetField("activeDraft",PrivateInstance)!.GetValue(tools)])!,
            "Explicit draft rebase keeps entered values and matches the moved rule and grainline",checks);
        tools.Close();
        Invoke(studio,"Undo");
        Check(DocumentSession.ContentEquals(beforeMove,studio.Document),"Keyboard move undo restores geometry and grading rule together",checks);
        var dragSource=studio.Selected!;
        typeof(CadCanvas).GetField("dragId",PrivateInstance)!.SetValue(canvas,dragSource.Id);
        typeof(CadCanvas).GetField("dragVertex",PrivateInstance)!.SetValue(canvas,-1);
        typeof(CadCanvas).GetField("dragOriginal",PrivateInstance)!.SetValue(canvas,dragSource.Points.ToList());
        typeof(CadCanvas).GetField("dragOriginalGrainline",PrivateInstance)!.SetValue(canvas,dragSource.Grainline?.Clone());
        typeof(CadCanvas).GetField("dragTranslation",PrivateInstance)!.SetValue(canvas,new Point2(10,0));
        typeof(CadCanvas).GetField("dragPreview",PrivateInstance)!.SetValue(canvas,TekstilCAD.Core.Geometry.Translate(dragSource.Points,10,0));
        typeof(CadCanvas).GetMethod("Up",PrivateInstance)!.Invoke(canvas,[new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.Left)]);
        var draggedRule=studio.Document.GradingRules.Single(r=>r.SourcePieceId==source.Id);
        Check(draggedRule.SourcePoints.SequenceEqual(studio.Selected!.Points) && draggedRule.Deltas.SequenceEqual(beforeMove.GradingRules.Single(r=>r.SourcePieceId==source.Id).Deltas),
            "Mouse movement carries matching grading increments with the source",checks);
        Invoke(studio,"Undo");
        Check(DocumentSession.ContentEquals(beforeMove,studio.Document),"Mouse move undo restores geometry and grading rule together",checks);
        studio.Commit("Eski serileme testi",()=>studio.Document.GradingRules.Single(r=>r.SourcePieceId==source.Id).SourcePoints[0]=new Point2(-1,0));
        var keyboardStaleRule=studio.Document.GradingRules.Single(r=>r.SourcePieceId==source.Id).Clone();
        move.Invoke(canvas,[Key.Right,ModifierKeys.None]);
        var warning=(TextBlock)typeof(MainWindow).GetField("canvasError",PrivateInstance)!.GetValue(studio)!;
        Check(warning.Text.Contains("serileme kuralı") && studio.Document.GradingRules.Single(r=>r.SourcePieceId==source.Id).SourcePoints.SequenceEqual(keyboardStaleRule.SourcePoints),
            "Keyboard move warns when the existing grading rule cannot follow the contour",checks);
        Invoke(studio,"Undo");Invoke(studio,"Undo");studio.ClearCanvasError();
        Check(DocumentSession.ContentEquals(beforeMove,studio.Document),"Undo restores the document after a stale-rule warning",checks);
        var positionX=(TextBox)typeof(MainWindow).GetField("x",PrivateInstance)!.GetValue(studio)!;
        positionX.Text="10";
        Invoke(studio,"ApplyProperties");
        var propertyRule=studio.Document.GradingRules.Single(r=>r.SourcePieceId==source.Id);
        Check(propertyRule.SourcePoints.SequenceEqual(studio.Selected!.Points) && propertyRule.Deltas.SequenceEqual(beforeMove.GradingRules.Single(r=>r.SourcePieceId==source.Id).Deltas),
            "Coordinate property movement carries matching grading increments with the source",checks);
        Check(!status.Text.Contains("eski serileme kuralı",StringComparison.Ordinal),"Matching grading rule does not produce a stale-rule warning",checks);
        Invoke(studio,"Undo");
        Check(DocumentSession.ContentEquals(beforeMove,studio.Document),"Coordinate property move undo restores geometry and grading rule together",checks);
        studio.Commit("Eski kural testi",()=>studio.Document.GradingRules.Single(r=>r.SourcePieceId==source.Id).SourcePoints[0]=new Point2(-1,0));
        var staleRule=studio.Document.GradingRules.Single(r=>r.SourcePieceId==source.Id).Clone();
        var beforeStaleMove=studio.Document.Clone();
        positionX.Text="10";Invoke(studio,"ApplyProperties");
        Check(studio.Document.GradingRules.Single(r=>r.SourcePieceId==source.Id).SourcePoints.SequenceEqual(staleRule.SourcePoints) &&
            status.Text.Contains("özellikleri uygulandı",StringComparison.Ordinal) && status.Text.Contains("eski serileme kuralı",StringComparison.Ordinal) &&
            status.Text.Contains(source.Id.ToString("D"),StringComparison.Ordinal),
            "Coordinate property movement succeeds while identifying the preserved stale grading rule",checks);
        Invoke(studio,"Undo");
        Check(DocumentSession.ContentEquals(beforeStaleMove,studio.Document),"Undo restores coordinate move with stale rule",checks);
        Invoke(studio,"Undo");
        Check(DocumentSession.ContentEquals(beforeMove,studio.Document),"Stale grading test restores the matching rule",checks);
        Func<IReadOnlyList<Point2>,Point2,List<Point2>> rotate=(points,center)=>TekstilCAD.Core.Geometry.Rotate(points,90,center);
        typeof(MainWindow).GetMethod("Transform",PrivateInstance)!.Invoke(studio,[rotate]);
        var rotationWarning=(TextBlock)typeof(MainWindow).GetField("canvasError",PrivateInstance)!.GetValue(studio)!;
        Check(rotationWarning.Text.Contains("serileme kuralı") && studio.Document.GradingRules.Single(r=>r.SourcePieceId==source.Id).SourcePoints.SequenceEqual(beforeMove.GradingRules.Single(r=>r.SourcePieceId==source.Id).SourcePoints),
            "Rotation warns when grading remains attached to the previous contour",checks);
        Invoke(studio,"Undo");studio.ClearCanvasError();
        Check(DocumentSession.ContentEquals(beforeMove,studio.Document),"Undo restores contour and grading after rotation warning",checks);
        Invoke(studio,"Undo");
        Check(DocumentSession.ContentEquals(beforeRule,studio.Document),"Keyboard grading verification removes its test rule",checks);
        Invoke(studio,"Undo");
        Check(DocumentSession.ContentEquals(original,studio.Document),"Keyboard grading verification restores the original document",checks);
    }
    private static void VerifyViewNavigation(MainWindow studio,CadCanvas canvas,List<string> checks)
    {
        var session=(DocumentSession)typeof(MainWindow).GetField("session",PrivateInstance)!.GetValue(studio)!;
        var scaleField=typeof(CadCanvas).GetField("scale",PrivateInstance)!;
        var offsetField=typeof(CadCanvas).GetField("offset",PrivateInstance)!;
        var dragField=typeof(CadCanvas).GetField("dragId",PrivateInstance)!;
        var panField=typeof(CadCanvas).GetField("panStart",PrivateInstance)!;
        var oldScale=scaleField.GetValue(canvas);var oldOffset=offsetField.GetValue(canvas);
        var oldDrag=dragField.GetValue(canvas);var oldPan=panField.GetValue(canvas);
        var document=JsonSerializer.Serialize(studio.Document);var selected=studio.SelectedId;
        var undo=session.UndoCount;var redo=session.RedoCount;
        var dirty=(bool)typeof(MainWindow).GetField("dirty",PrivateInstance)!.GetValue(studio)!;
        try
        {
            Check(canvas.ActualWidth>1 && canvas.ActualHeight>1,"View navigation verification has a measured canvas",checks);
            scaleField.SetValue(canvas,1.0);offsetField.SetValue(canvas,new Vector(80,65));
            var center=new Point(canvas.ActualWidth/2,canvas.ActualHeight/2);
            var originalWorld=new Point(center.X-80,center.Y-65);
            canvas.ZoomView(true);
            var zoom=(double)scaleField.GetValue(canvas)!;var offset=(Vector)offsetField.GetValue(canvas)!;
            Check(Math.Abs(zoom-1.15)<1e-12 && Math.Abs((center.X-offset.X)/zoom-originalWorld.X)<1e-8
                && Math.Abs((center.Y-offset.Y)/zoom-originalWorld.Y)<1e-8,
                "View zoom keeps the world point at the canvas center",checks);
            canvas.ZoomView(false);
            Check(Math.Abs((double)scaleField.GetValue(canvas)!-1)<1e-12 && ((Vector)offsetField.GetValue(canvas)!-new Vector(80,65)).Length<1e-8,
                "Opposite view zoom operations restore the original transform",checks);
            var pointer=new Point(120,100);
            canvas.ZoomAt(pointer,120);
            zoom=(double)scaleField.GetValue(canvas)!;offset=(Vector)offsetField.GetValue(canvas)!;
            Check(Math.Abs(zoom-1.15)<1e-12 && Math.Abs((pointer.X-offset.X)/zoom-40)<1e-8 && Math.Abs((pointer.Y-offset.Y)/zoom-35)<1e-8,
                "Mouse wheel zoom keeps the world point beneath the pointer",checks);
            canvas.ZoomAt(pointer,-120);
            scaleField.SetValue(canvas,30.0);canvas.ZoomView(true);
            Check((double)scaleField.GetValue(canvas)! == 30.0,"View zoom respects maximum scale",checks);
            scaleField.SetValue(canvas,.02);canvas.ZoomView(false);
            Check((double)scaleField.GetValue(canvas)! == .02,"View zoom respects minimum scale",checks);
            scaleField.SetValue(canvas,1.0);offsetField.SetValue(canvas,new Vector(80,65));
            canvas.PanView(80,-80);
            Check((Vector)offsetField.GetValue(canvas)! == new Vector(160,-15),"View pan changes only the display offset in the requested direction",checks);

            var compact=new CadCanvas(studio);
            compact.Measure(new Size(80,80));compact.Arrange(new Rect(0,0,80,80));compact.UpdateLayout();
            compact.Fit();
            var compactScale=(double)scaleField.GetValue(compact)!;var compactOffset=(Vector)offsetField.GetValue(compact)!;
            var allPoints=studio.Document.Pieces.SelectMany(piece=>piece.Points).ToArray();
            Check(allPoints.Length>0 && compactScale>.02 && allPoints.All(point=>
                point.X*compactScale+compactOffset.X>=0 && point.X*compactScale+compactOffset.X<=80 &&
                point.Y*compactScale+compactOffset.Y>=0 && point.Y*compactScale+compactOffset.Y<=80),
                "Fit all frames every point on a narrow canvas",checks);

            foreach(var field in new[] { dragField,panField })
            {
                if(field==dragField)field.SetValue(canvas,Guid.NewGuid());else field.SetValue(canvas,new Point(1,1));
                try
                {
                    var beforeScale=(double)scaleField.GetValue(canvas)!;var beforeOffset=(Vector)offsetField.GetValue(canvas)!;
                    canvas.ZoomView(true);canvas.ZoomAt(new Point(10,10),120);canvas.PanView(80,0);canvas.Fit();
                    Check((double)scaleField.GetValue(canvas)! == beforeScale && (Vector)offsetField.GetValue(canvas)! == beforeOffset,
                        "View navigation and fit all wait for active "+field.Name,checks);
                }
                finally { field.SetValue(canvas,null); }
            }
            var buttons=Descendants<Button>((DependencyObject)studio.Content);
            foreach(var name in new[] { "Görünümü yakınlaştır","Görünümü uzaklaştır","Görünümü sola kaydır","Görünümü sağa kaydır","Görünümü yukarı kaydır","Görünümü aşağı kaydır" })
            {
                var button=buttons.Single(b=>AutomationProperties.GetName(b)==name);
                Check(button.Focusable && KeyboardNavigation.GetIsTabStop(button) && !string.IsNullOrWhiteSpace(button.ToolTip?.ToString()),
                    "View navigation button is named and keyboard reachable: "+name,checks);
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            Check(JsonSerializer.Serialize(studio.Document)==document && studio.SelectedId==selected && session.UndoCount==undo && session.RedoCount==redo
                && (bool)typeof(MainWindow).GetField("dirty",PrivateInstance)!.GetValue(studio)! == dirty,
                "View navigation commands and buttons preserve project, selection, dirty state and history",checks);
        }
        finally { scaleField.SetValue(canvas,oldScale);offsetField.SetValue(canvas,oldOffset);dragField.SetValue(canvas,oldDrag);panField.SetValue(canvas,oldPan);canvas.InvalidateVisual(); }
    }
    private static void VerifyLargeVertexValidation(MainWindow studio,List<string> checks)
    {
        var canvas=(CadCanvas)typeof(MainWindow).GetField("canvas",PrivateInstance)!.GetValue(studio)!;
        var session=(DocumentSession)typeof(MainWindow).GetField("session",PrivateInstance)!.GetValue(studio)!;
        var original=Enumerable.Range(0,4097).Select(i=>
        {
            var angle=Math.Tau*i/4097;
            return new Point2(1000*Math.Cos(angle),1000*Math.Sin(angle));
        }).ToArray();
        var valid=original.ToArray();valid[0]=new Point2(1000.01,0);
        foreach(var closed in new[] { false,true })
        {
            var piece=new PatternPiece { Name="Büyük parça",Closed=closed,Points=original.ToList() };
            studio.LoadDocument(new ProjectDocument { Name="Büyük nokta düzenleme",Pieces=[piece] });
            studio.SetTool("Seç");studio.Select(piece.Id);
            var before=studio.Document.Clone();
            canvas.StartLargeVertexValidation(piece.Id,closed,original,valid);
            var task=canvas.LargeVertexValidationTask ?? throw new InvalidOperationException("Large vertex worker did not start.");
            canvas.StartLargeVertexValidation(piece.Id,closed,original,valid);
            Check(canvas.LargeVertexValidationRunning && ReferenceEquals(task,canvas.LargeVertexValidationTask)
                && DocumentSession.ContentEquals(before,studio.Document),
                $"Large {(closed?"closed":"open")} vertex validation rejects duplicates without early commit",checks);
            PumpUntil(()=>task.IsCompleted);task.GetAwaiter().GetResult();
            Check(!canvas.LargeVertexValidationRunning && studio.Selected!.Points.SequenceEqual(valid) && session.UndoCount==1,
                $"Large {(closed?"closed":"open")} vertex change commits once",checks);
            Check(session.Undo() && DocumentSession.ContentEquals(before,studio.Document),
                $"Large {(closed?"closed":"open")} vertex change is undoable",checks);

            var invalid=original.ToArray();invalid[1]=invalid[0];
            canvas.StartLargeVertexValidation(piece.Id,closed,original,invalid);
            var failed=canvas.LargeVertexValidationTask!;PumpUntil(()=>failed.IsCompleted);failed.GetAwaiter().GetResult();
            Check(DocumentSession.ContentEquals(before,studio.Document) && session.UndoCount==0 && studio.SelectedId==piece.Id,
                $"Invalid large {(closed?"closed":"open")} vertex change preserves document and history",checks);
        }
        var a=new PatternPiece { Name="A",Closed=false,Points=original.ToList() };
        var b=new PatternPiece { Name="B",Closed=false,Points=[new(0,0),new(10,0)] };
        studio.LoadDocument(new ProjectDocument { Pieces=[a,b] });studio.Select(a.Id);
        var unchanged=studio.Document.Clone();
        canvas.StartLargeVertexValidation(a.Id,false,original,valid);
        var selectionTask=canvas.LargeVertexValidationTask!;
        var undoBefore=session.UndoCount;var redoBefore=session.RedoCount;
        studio.Select(b.Id);studio.SetTool("Çoklu çizgi");Invoke(studio,"Undo");Invoke(studio,"Redo");
        var canDiscard=(bool)typeof(MainWindow).GetMethod("CanDiscard",PrivateInstance,null,Type.EmptyTypes,null)!.Invoke(studio,null)!;
        var status=(TextBlock)typeof(MainWindow).GetField("status",PrivateInstance)!.GetValue(studio)!;
        Check(!canDiscard && studio.SelectedId==a.Id && studio.ActiveTool=="Seç" && session.UndoCount==undoBefore
            && session.RedoCount==redoBefore && DocumentSession.ContentEquals(unchanged,studio.Document)
            && status.Text.Contains("Esc") && status.Text.Contains("kurtarma"),
            "Running large vertex validation blocks selection, tool, history and discard with an actionable status",checks);
        var busyStatus=status.Text;
        canvas.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice,0,null,canvas) { RoutedEvent=Keyboard.GotKeyboardFocusEvent });
        Check(status.Text==busyStatus,"Canvas focus does not erase running validation guidance",checks);
        // Escape calls CancelDrawing; invoke the same cancellation command without focusing a visible window.
        canvas.CancelDrawing();PumpUntil(()=>selectionTask.IsCompleted);selectionTask.GetAwaiter().GetResult();
        Check(DocumentSession.ContentEquals(unchanged,studio.Document) && studio.SelectedId==a.Id && !canvas.LargeVertexValidationRunning,
            "Escape cancellation command leaves large vertex document unchanged",checks);
        studio.Select(b.Id);
        Check(studio.SelectedId==b.Id,"Selection works after large vertex cancellation completes",checks);

        studio.Select(a.Id);canvas.StartLargeVertexValidation(a.Id,false,original,valid);
        var documentTask=canvas.LargeVertexValidationTask!;
        studio.AddPiece([new Point2(20,0),new Point2(30,0)],false);
        var changed=studio.Document.Clone();
        PumpUntil(()=>documentTask.IsCompleted);documentTask.GetAwaiter().GetResult();
        Check(DocumentSession.ContentEquals(changed,studio.Document) && studio.Document.Pieces.First(p=>p.Id==a.Id).Points.SequenceEqual(original),
            "Changing document discards stale large vertex result",checks);

        studio.Select(a.Id);canvas.StartLargeVertexValidation(a.Id,false,original,valid);
        var cancelled=canvas.LargeVertexValidationTask!;canvas.CancelLargeVertexValidation();
        PumpUntil(()=>cancelled.IsCompleted);cancelled.GetAwaiter().GetResult();
        Check(DocumentSession.ContentEquals(changed,studio.Document) && !canvas.LargeVertexValidationRunning,
            "Cancelled large vertex validation preserves document",checks);
    }
    private static void VerifyFractionalSelectionWithSnap(MainWindow studio,List<string> checks)
    {
        var canvas=(CadCanvas)typeof(MainWindow).GetField("canvas",PrivateInstance)!.GetValue(studio)!;
        var snap=(CheckBox)typeof(MainWindow).GetField("snap",PrivateInstance)!.GetValue(studio)!;
        var scaleField=typeof(CadCanvas).GetField("scale",PrivateInstance)!;
        var offsetField=typeof(CadCanvas).GetField("offset",PrivateInstance)!;
        var vertexField=typeof(CadCanvas).GetField("dragVertex",PrivateInstance)!;
        var originField=typeof(CadCanvas).GetField("dragOrigin",PrivateInstance)!;
        var down=typeof(CadCanvas).GetMethod("Down",PrivateInstance)!;
        var up=typeof(CadCanvas).GetMethod("Up",PrivateInstance)!;
        var click=new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.Left);
        var previousSnap=snap.IsChecked;var previousScale=scaleField.GetValue(canvas);var previousOffset=offsetField.GetValue(canvas);
        try
        {
            foreach(var snapping in new[] { true,false })
            {
                var piece=new PatternPiece { Name="Kesirli seçim",Points=[new(.25,.25),new(10.25,.25),new(10.25,10.25),new(.25,10.25)] };
                studio.LoadDocument(new ProjectDocument { Pieces=[piece] });studio.SetTool("Seç");studio.Select(piece.Id);snap.IsChecked=snapping;
                scaleField.SetValue(canvas,30d);
                var pos=click.GetPosition(canvas);offsetField.SetValue(canvas,new Vector(pos.X-7.5,pos.Y-7.5));
                var before=studio.Document.Clone();var session=(DocumentSession)typeof(MainWindow).GetField("session",PrivateInstance)!.GetValue(studio)!;var undo=session.UndoCount;
                down.Invoke(canvas,[canvas,click]);
                var origin=(Point2)originField.GetValue(canvas)!;
                Check((int)vertexField.GetValue(canvas)! == 0 && (snapping ? origin==new Point2(0,0) : TekstilCAD.Core.Geometry.Distance(origin,new Point2(.25,.25))<1e-9),
                    $"Fractional vertex selection uses visible pointer with snap {snapping} and editing origin follows grid",checks);
                up.Invoke(canvas,[click]);
                Check(DocumentSession.ContentEquals(before,studio.Document) && session.UndoCount==undo,
                    $"Fractional vertex click without movement preserves geometry and undo with snap {snapping}",checks);

                // Exercise the same drag-preview path used by mouse movement, without
                // moving the user's physical pointer or changing its button state.
                down.Invoke(canvas,[canvas,click]);
                var thresholdX=SystemParameters.MinimumHorizontalDragDistance;
                var thresholdY=SystemParameters.MinimumVerticalDragDistance;
                canvas.UpdateDragPreview(new Point(pos.X+thresholdX/2,pos.Y+thresholdY/2));
                up.Invoke(canvas,[click]);
                Check(DocumentSession.ContentEquals(before,studio.Document) && session.UndoCount==undo,
                    $"Subthreshold fractional vertex jitter is a no-op with snap {snapping}",checks);

                down.Invoke(canvas,[canvas,click]);
                var moved=new Point(pos.X+Math.Max(thresholdX+1,30),pos.Y);
                canvas.UpdateDragPreview(moved);
                var preview=(List<Point2>?)typeof(CadCanvas).GetField("dragPreview",PrivateInstance)!.GetValue(canvas);
                Check(preview is not null && !preview.SequenceEqual(before.Pieces[0].Points),
                    $"Beyond-threshold fractional vertex produces a preview with snap {snapping}",checks);
                canvas.UpdateDragPreview(pos);
                up.Invoke(canvas,[click]);
                Check(DocumentSession.ContentEquals(before,studio.Document) && session.UndoCount==undo,
                    $"Returning to drag origin preserves geometry and undo with snap {snapping}",checks);

                down.Invoke(canvas,[canvas,click]);
                canvas.UpdateDragPreview(moved);
                preview=(List<Point2>?)typeof(CadCanvas).GetField("dragPreview",PrivateInstance)!.GetValue(canvas);
                var proposed=preview!.ToArray();
                up.Invoke(canvas,[click]);
                Check(studio.Document.Pieces[0].Points.SequenceEqual(proposed) && session.UndoCount==undo+1 &&
                      (snapping ? proposed[0].X==Math.Round(proposed[0].X) && proposed[0].Y==Math.Round(proposed[0].Y) :
                       TekstilCAD.Core.Geometry.Distance(proposed[0],before.Pieces[0].Points[0])>0),
                    $"Beyond-threshold drag commits one snapped or free edit with snap {snapping}",checks);
                Invoke(studio,"Undo");
                Check(DocumentSession.ContentEquals(before,studio.Document),
                    $"Undo restores fractional vertex after drag with snap {snapping}",checks);

                studio.Select(null);pos=click.GetPosition(canvas);offsetField.SetValue(canvas,new Vector(pos.X-157.5,pos.Y-7.5));
                down.Invoke(canvas,[canvas,click]);
                origin=(Point2)originField.GetValue(canvas)!;
                Check(studio.SelectedId==piece.Id && (int)vertexField.GetValue(canvas)! == -1 &&
                    (snapping ? origin==new Point2(5,0) : TekstilCAD.Core.Geometry.Distance(origin,new Point2(5.25,.25))<1e-9),
                    $"Fractional piece edge selection uses visible pointer with snap {snapping} and snapped drag origin",checks);
                up.Invoke(canvas,[click]);
                Check(DocumentSession.ContentEquals(before,studio.Document) && session.UndoCount==undo,
                    $"Fractional piece click without movement preserves geometry and undo with snap {snapping}",checks);
            }
        }
        finally { snap.IsChecked=previousSnap;scaleField.SetValue(canvas,previousScale);offsetField.SetValue(canvas,previousOffset); }
    }
    private static void VerifyClosingEdgeDragSafety(MainWindow studio,List<string> checks)
    {
        var canvas=(CadCanvas)typeof(MainWindow).GetField("canvas",PrivateInstance)!.GetValue(studio)!;
        var session=(DocumentSession)typeof(MainWindow).GetField("session",PrivateInstance)!.GetValue(studio)!;
        var snap=(CheckBox)typeof(MainWindow).GetField("snap",PrivateInstance)!.GetValue(studio)!;
        var scale=typeof(CadCanvas).GetField("scale",PrivateInstance)!;var offset=typeof(CadCanvas).GetField("offset",PrivateInstance)!;
        var previousScale=scale.GetValue(canvas);var previousOffset=offset.GetValue(canvas);var previousSnap=snap.IsChecked;
        var down=typeof(CadCanvas).GetMethod("Down",PrivateInstance)!;
        var up=typeof(CadCanvas).GetMethod("Up",PrivateInstance)!.CreateDelegate<Action<MouseButtonEventArgs>>(canvas);
        var safe=typeof(CadCanvas).GetMethod("Safe",PrivateInstance)!;
        var error=(TextBlock)typeof(MainWindow).GetField("canvasError",PrivateInstance)!.GetValue(studio)!;
        try
        {
            foreach(var index in new[] { 0,3 })
            {
                var p=new PatternPiece { Points=[new(0,0),new(100,0),new(100,100),new(0,100)] };
                studio.LoadDocument(new ProjectDocument { Pieces=[p] });studio.SetTool("Seç");studio.Select(p.Id);snap.IsChecked=false;
                studio.Commit("Geçmiş hazırlığı",()=>studio.Document.Name="Düzenlendi");
                Check(session.Undo(),"Closing edge test has redo history",checks);studio.Select(p.Id);
                var before=studio.Document.Clone();var undo=session.UndoCount;var redo=session.RedoCount;
                var click=new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.Left);var pointer=click.GetPosition(canvas);
                scale.SetValue(canvas,1.0);var selected=p.Points[index];offset.SetValue(canvas,new Vector(pointer.X-selected.X,pointer.Y-selected.Y));
                down.Invoke(canvas,[canvas,click]);Check(canvas.HasActiveDragPreview,"Closing endpoint drag starts",checks);
                canvas.UpdateDragPreview(new Point(pointer.X,pointer.Y+(index==0?100:-100)));
                safe.Invoke(canvas,[new Action(()=>up(click)),false,null]);
                Check(!canvas.HasActiveDragPreview && error.Text.Contains("kapanış kenarını") && DocumentSession.ContentEquals(before,studio.Document) &&
                    session.UndoCount==undo && session.RedoCount==redo,
                    $"Closing endpoint {index} collapse is rejected without changing document or history",checks);
            }
            var large=new PatternPiece { Points=Enumerable.Range(0,4097).Select(i=> { var angle=Math.Tau*i/4097;return new Point2(100*Math.Cos(angle),100*Math.Sin(angle)); }).ToList() };
            studio.LoadDocument(new ProjectDocument { Pieces=[large] });studio.SetTool("Seç");studio.Select(large.Id);
            var original=studio.Document.Clone();var proposed=large.Points.ToArray();proposed[^1]=proposed[0];
            try { canvas.StartLargeVertexValidation(large.Id,true,large.Points,proposed);throw new InvalidOperationException("Collapsed large contour was accepted."); }
            catch(ArgumentException ex) when(ex.Message.Contains("kapanış kenarını")) { }
            Check(!canvas.LargeVertexValidationRunning && DocumentSession.ContentEquals(original,studio.Document) && session.UndoCount==0,
                "Large closing edge collapse is rejected before starting a worker",checks);
        }
        finally { canvas.CancelDrawing();studio.ClearCanvasError();snap.IsChecked=previousSnap;scale.SetValue(canvas,previousScale);offset.SetValue(canvas,previousOffset); }
    }

    private static void VerifyNearestVertexSelection(MainWindow studio,List<string> checks)
    {
        var canvas=(CadCanvas)typeof(MainWindow).GetField("canvas",PrivateInstance)!.GetValue(studio)!;
        var snap=(CheckBox)typeof(MainWindow).GetField("snap",PrivateInstance)!.GetValue(studio)!;
        var scaleField=typeof(CadCanvas).GetField("scale",PrivateInstance)!;
        var offsetField=typeof(CadCanvas).GetField("offset",PrivateInstance)!;
        var vertexField=typeof(CadCanvas).GetField("dragVertex",PrivateInstance)!;
        var error=(TextBlock)typeof(MainWindow).GetField("canvasError",PrivateInstance)!.GetValue(studio)!;
        var session=(DocumentSession)typeof(MainWindow).GetField("session",PrivateInstance)!.GetValue(studio)!;
        var down=typeof(CadCanvas).GetMethod("Down",PrivateInstance)!;
        var up=typeof(CadCanvas).GetMethod("Up",PrivateInstance)!;
        var click=new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.Left);
        var oldSnap=snap.IsChecked;var oldScale=scaleField.GetValue(canvas);var oldOffset=offsetField.GetValue(canvas);
        void PointAt(double x,double y,double scale)
        {
            var p=click.GetPosition(canvas);scaleField.SetValue(canvas,scale);
            offsetField.SetValue(canvas,new Vector(p.X-x*scale,p.Y-y*scale));
        }
        try
        {
            var piece=new PatternPiece { Name="Yakın noktalar",Closed=false,Points=[new(0,0),new(5,0),new(50,0)] };
            studio.LoadDocument(new ProjectDocument { Pieces=[piece] });studio.SetTool("Seç");studio.Select(piece.Id);snap.IsChecked=false;
            var before=studio.Document.Clone();var undo=session.UndoCount;
            PointAt(5,0,1);down.Invoke(canvas,[canvas,click]);
            Check((int)vertexField.GetValue(canvas)! == 1,"Pointer selects nearest vertex instead of earlier nearby vertex",checks);
            var pointer=click.GetPosition(canvas);canvas.UpdateDragPreview(new Point(pointer.X,pointer.Y+10));up.Invoke(canvas,[click]);
            Check(studio.Document.Pieces[0].Points[0]==new Point2(0,0) && studio.Document.Pieces[0].Points[1]==new Point2(5,10)
                && session.UndoCount==undo+1,"Nearest vertex drag changes only intended vertex",checks);
            Invoke(studio,"Undo");Check(DocumentSession.ContentEquals(before,studio.Document),"Undo restores nearest vertex drag",checks);
            PointAt(2.5,0,1);down.Invoke(canvas,[canvas,click]);
            Check(!canvas.HasActiveDragPreview && DocumentSession.ContentEquals(before,studio.Document) && error.Text.Contains("aynı uzaklıkta"),
                "Equidistant vertices are not dragged and ambiguity is reported",checks);
            Check(AutomationProperties.GetLiveSetting(error)==AutomationLiveSetting.Polite && AutomationProperties.GetName(error).Contains("aynı uzaklıkta"),
                "Canvas selection warning exposes its message to accessibility APIs",checks);

            var duplicates=new PatternPiece { Closed=false,Points=[new(0,0),new(0,0),new(20,0)] };
            studio.LoadDocument(new ProjectDocument { Pieces=[duplicates] });studio.Select(duplicates.Id);PointAt(0,0,1);down.Invoke(canvas,[canvas,click]);
            Check(!canvas.HasActiveDragPreview && error.Text.Contains("aynı uzaklıkta"),"Overlapping distinct vertices are not selected arbitrarily",checks);

            var closed=new PatternPiece { Closed=true,Points=[new(0,0),new(10,0),new(10,10),new(0,10),new(0,0)] };
            studio.LoadDocument(new ProjectDocument { Pieces=[closed] });studio.Select(closed.Id);PointAt(0,0,30);down.Invoke(canvas,[canvas,click]);
            Check(canvas.HasActiveDragPreview && (int)vertexField.GetValue(canvas)! == 0,"Explicit closing duplicate is treated as the same logical vertex",checks);
            before=studio.Document.Clone();pointer=click.GetPosition(canvas);canvas.UpdateDragPreview(new Point(pointer.X+30,pointer.Y));up.Invoke(canvas,[click]);
            Check(studio.Document.Pieces[0].Points[0]==new Point2(1,0) && studio.Document.Pieces[0].Points[^1]==new Point2(1,0),
                "Dragging explicit closing vertex keeps both stored endpoints together",checks);
            Invoke(studio,"Undo");Check(DocumentSession.ContentEquals(before,studio.Document),"Undo restores explicit closing vertex",checks);
        }
        finally { canvas.CancelDrawing();studio.ClearCanvasError();snap.IsChecked=oldSnap;scaleField.SetValue(canvas,oldScale);offsetField.SetValue(canvas,oldOffset); }
    }
    private static void VerifyOpenVertexDrag(MainWindow studio,List<string> checks)
    {
        var piece=new PatternPiece { Name="Açık çizgi",Closed=false,Points=[new(0,0),new(10,0),new(10,10),new(20,10)] };
        studio.LoadDocument(new ProjectDocument { Name="Açık çizgi sınaması",Pieces=[piece] });
        studio.Select(piece.Id);
        var canvas=(CadCanvas)typeof(MainWindow).GetField("canvas",PrivateInstance)!.GetValue(studio)!;
        var session=(DocumentSession)typeof(MainWindow).GetField("session",PrivateInstance)!.GetValue(studio)!;
        var before=studio.Document.Clone();var selected=studio.SelectedId;var undo=session.UndoCount;var redo=session.RedoCount;
        var up=typeof(CadCanvas).GetMethod("Up",PrivateInstance)!;
        void Prepare(IReadOnlyList<Point2> result,int index)
        {
            typeof(CadCanvas).GetField("dragId",PrivateInstance)!.SetValue(canvas,piece.Id);
            typeof(CadCanvas).GetField("dragVertex",PrivateInstance)!.SetValue(canvas,index);
            typeof(CadCanvas).GetField("dragOriginal",PrivateInstance)!.SetValue(canvas,before.Pieces[0].Points.ToList());
            typeof(CadCanvas).GetField("dragPreview",PrivateInstance)!.SetValue(canvas,result.ToList());
        }
        void Drag() => up.Invoke(canvas,[new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.Left)]);
        foreach(var invalid in new[]
        {
            new[] {new Point2(0,0),new Point2(0,0),new Point2(10,10),new Point2(20,10)},
            new[] {new Point2(0,0),new Point2(10,0),new Point2(10,10),new Point2(5,-5)}
        })
        {
            Prepare(invalid,1);
            try { Drag(); throw new InvalidOperationException("Invalid open path drag unexpectedly succeeded."); }
            catch(TargetInvocationException ex) when(ex.InnerException is ArgumentException or InvalidDataException) { }
            Check(DocumentSession.ContentEquals(before,studio.Document) && studio.SelectedId==selected && session.UndoCount==undo && session.RedoCount==redo,
                "Invalid open-path vertex drag preserves document, selection and history",checks);
        }
        var valid=new[] {new Point2(0,0),new Point2(10,0),new Point2(10,10),new Point2(20,20)};
        Prepare(valid,3);Drag();
        Check(studio.SelectedId==selected && studio.Selected!.Points.SequenceEqual(valid) && session.UndoCount==undo+1,
            "Valid open-path vertex drag commits one undoable edit",checks);
        Invoke(studio,"Undo");
        Check(DocumentSession.ContentEquals(before,studio.Document) && studio.SelectedId==selected,
            "Undo restores valid open-path vertex drag",checks);
    }
    private static void VerifyExplicitClosureVertexDrag(MainWindow studio,List<string> checks)
    {
        Point2[] original=[new(0,0),new(10,0),new(10,10),new(0,10),new(0,0)];
        var moved=new Point2(-2,-3);
        foreach(var index in new[] {0,original.Length-1})
        {
            var piece=new PatternPiece { Name="Kapalı kontur",Closed=true,Points=original.ToList() };
            studio.LoadDocument(new ProjectDocument { Name="Kapanış noktası",Pieces=[piece] });
            studio.Select(piece.Id);
            var before=studio.Document.Clone();
            var selected=studio.SelectedId;
            var session=(DocumentSession)typeof(MainWindow).GetField("session",PrivateInstance)!.GetValue(studio)!;
            var undo=session.UndoCount;var redo=session.RedoCount;
            var preview=CadCanvas.PreviewVertexDrag(original,index,moved,true);
            Check(preview[0]==moved && preview[^1]==moved && preview.Skip(1).Take(original.Length-2).SequenceEqual(original.Skip(1).Take(original.Length-2))
                && original[0]==new Point2(0,0) && original[^1]==original[0],
                $"Explicit closing point drag {index} moves both preview endpoints without altering source or other vertices",checks);
            var canvas=(CadCanvas)typeof(MainWindow).GetField("canvas",PrivateInstance)!.GetValue(studio)!;
            typeof(CadCanvas).GetField("dragId",PrivateInstance)!.SetValue(canvas,piece.Id);
            typeof(CadCanvas).GetField("dragVertex",PrivateInstance)!.SetValue(canvas,index);
            typeof(CadCanvas).GetField("dragOriginal",PrivateInstance)!.SetValue(canvas,original.ToList());
            typeof(CadCanvas).GetField("dragPreview",PrivateInstance)!.SetValue(canvas,preview);
            typeof(CadCanvas).GetMethod("Up",PrivateInstance)!.Invoke(canvas,[new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.Left)]);
            Check(studio.SelectedId==selected && studio.Selected!.Points.SequenceEqual(preview) && session.UndoCount==undo+1 && session.RedoCount==redo,
                $"Explicit closing point drag {index} commits one edit and preserves selection",checks);
            Invoke(studio,"Undo");
            Check(DocumentSession.ContentEquals(before,studio.Document) && studio.SelectedId==selected,
                $"Undo restores explicit closing point drag {index}",checks);
        }
        var interior=CadCanvas.PreviewVertexDrag(original,2,new Point2(12,11),true);
        var open=CadCanvas.PreviewVertexDrag(original,0,moved,false);
        Check(interior[0]==original[0] && interior[^1]==original[^1] && interior[2]==new Point2(12,11)
            && open[0]==moved && open[^1]==original[^1],
            "Ordinary vertex and open-path endpoints remain independent",checks);
    }
    private static void Check(bool condition, string name, List<string> checks)
    { if (!condition) throw new InvalidOperationException("Verification failed: " + name); checks.Add(name); }
    private static void VerifyHpglPreview(string work,List<string> checks)
    {
        var queue=new SimulationQueue(Path.Combine(work,"hpgl-preview-"+Guid.NewGuid().ToString("N")));
        var piece=new PatternPiece { Name="Kapalı örnek",Closed=true,Points=[new(-10,-5),new(30,-5),new(30,15),new(-10,15)] };
        var job=queue.Prepare(new ProjectDocument { Name="HPGL önizleme",Pieces=[piece] },new MachineProfile("Dosya simülatörü",40,2),JobFormat.Hpgl);
        var preview=OutputPreview.Read(job);
        Check(preview.Paths.Count==1 && preview.Paths[0].Points.Count==5
            && preview.Paths[0].Points[0]==preview.Paths[0].Points[^1]
            && Math.Abs(preview.Bounds.MinX+20)<1e-9 && Math.Abs(preview.Bounds.MinY+10)<1e-9
            && Math.Abs(preview.Bounds.Width-80)<1e-9 && Math.Abs(preview.Bounds.Height-40)<1e-9,
            "HPGL preview reads closed contour and applies scale exactly once",checks);
        var original=File.ReadAllText(job.OutputPath);
        File.AppendAllText(job.OutputPath,"ZZ;");
        var rejectedHash=false;
        try { OutputPreview.Read(job); } catch(InvalidDataException) { rejectedHash=true; }
        Check(rejectedHash,"HPGL preview rejects altered output checksum",checks);
        var malformed=original.Replace("SP0;","ZZ;");
        Check(malformed!=original,"HPGL fixture contains end command",checks);
        File.WriteAllText(job.OutputPath,malformed);
        var hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(job.OutputPath)));
        var rejectedSyntax=false;
        try { OutputPreview.Read(job with { Sha256=hash }); } catch(InvalidDataException) { rejectedSyntax=true; }
        Check(rejectedSyntax,"HPGL preview rejects unsupported commands even with matching checksum",checks);
    }
    private static void VerifyMetadataWorkflow(MainWindow studio,string work,List<string> checks)
    {
        var piece=new PatternPiece { Name="Meta kalıp", Points=[new(0,0),new(100,0),new(100,100),new(0,100)] };
        studio.LoadDocument(new ProjectDocument { Name="Meta deneme",Pieces=[piece] });
        studio.Select(piece.Id);
        studio.Commit("Parça bilgileri doğrulaması",()=>studio.Selected!.Metadata=new PieceMetadata { Role="Kesim",Size="M",Material="Pamuk",Quantity=3 });
        Check(studio.Selected!.Metadata is { Role:"Kesim",Size:"M",Material:"Pamuk",Quantity:3 },"Piece metadata edit is reflected in document",checks);
        Invoke(studio,"Undo");
        Check(studio.Selected!.Metadata is null,"Undo removes piece metadata edit",checks);
        Invoke(studio,"Redo");
        Check(studio.Selected!.Metadata is { Role:"Kesim",Size:"M",Material:"Pamuk",Quantity:3 },"Redo restores piece metadata edit",checks);
        var window=new TextileToolsWindow(studio);
        var edited=TekstilCAD.Patterns.PatternTools.InsertPoint(studio.Selected!,0,new Point2(50,0));
        typeof(TextileToolsWindow).GetMethod("ReplacePiece",PrivateInstance)!.Invoke(window,[edited,false,null,false]);
        Check(studio.Selected!.Points.Count==5 && studio.Selected.Metadata is { Role:"Kesim",Size:"M",Material:"Pamuk",Quantity:3 },"Contour edit preserves piece metadata",checks);
        Invoke(studio,"Undo");
        Check(studio.Selected!.Points.Count==4 && studio.Selected.Metadata is { Quantity:3 },"Undo contour edit preserves metadata",checks);
        Invoke(studio,"Redo");
        Check(studio.Selected!.Points.Count==5 && studio.Selected.Metadata is { Quantity:3 },"Redo contour edit preserves metadata",checks);
        var path=Path.Combine(work,"metadata-roundtrip.tcad");
        Invoke(studio,"SaveTo",path);
        using(var json=JsonDocument.Parse(File.ReadAllText(path)))
            Check(json.RootElement.ValueKind==JsonValueKind.Array && json.RootElement[0].GetInt32()==2,"Metadata save uses version 2 envelope",checks);
        var loaded=ProjectStorage.Load(path);
        studio.LoadDocument(loaded);studio.Select(piece.Id);
        Check(studio.Selected!.Points.Count==5 && studio.Selected.Metadata is { Role:"Kesim",Size:"M",Material:"Pamuk",Quantity:3 },"Desktop save and reload preserve metadata and contour",checks);
        Check(!window.IsVisible,"Metadata workflow did not show a window",checks);
    }
    private static void VerifyAsyncProjectOpen(MainWindow studio,string work,List<string> checks)
    {
        var previousContext=SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(studio.Dispatcher));
        var path=Path.Combine(work,"async-open.tcad");
        ProjectStorage.Save(new ProjectDocument { Name="Opened" },path);
        using var reached=new ManualResetEventSlim();
        using var release=new ManualResetEventSlim();
        Task? pending=null;
        try
        {
            studio.LoadDocument(new ProjectDocument { Name="Original" });
            studio.BeforeProjectReadForVerification=()=> { reached.Set();if(!release.Wait(TimeSpan.FromSeconds(30)))throw new TimeoutException(); };
            pending=studio.OpenProjectFileAsync(path);
            PumpUntil(()=>reached.IsSet);
            var dispatched=false;studio.Dispatcher.BeginInvoke(new Action(()=>dispatched=true));PumpUntil(()=>dispatched);
            studio.Commit("Edit during read",()=>studio.Document.Name="Edited");
            var duplicate=studio.OpenProjectFileAsync(path);PumpUntil(()=>duplicate.IsCompleted);
            studio.ConfirmOpenDiscardForVerification=()=> (false,false);
            release.Set();PumpUntil(()=>pending.IsCompleted);pending.GetAwaiter().GetResult();
            Check(studio.Document.Name=="Edited","Background project read leaves dispatcher responsive and declining discard preserves edits",checks);

            studio.BeforeProjectReadForVerification=null;
            studio.ConfirmOpenDiscardForVerification=()=> { studio.Commit("Edit during approval",()=>studio.Document.Name="Changed during approval");return (true,false); };
            pending=studio.OpenProjectFileAsync(path);PumpUntil(()=>pending.IsCompleted);pending.GetAwaiter().GetResult();
            Check(studio.Document.Name=="Changed during approval","Changed work invalidates project-open approval",checks);

            var canvas=(CadCanvas)typeof(MainWindow).GetField("canvas",PrivateInstance)!.GetValue(studio)!;
            studio.SetTool("Çizgi");canvas.RestorePendingDrawing(new RecoveryPendingDrawing("Çizgi",[new Point2(1,2)]));
            studio.ConfirmOpenDrawingForVerification=()=> { canvas.RestorePendingDrawing(new RecoveryPendingDrawing("Çizgi",[new Point2(3,4)]));return true; };
            pending=studio.OpenProjectFileAsync(path);PumpUntil(()=>pending.IsCompleted);pending.GetAwaiter().GetResult();
            Check(studio.Document.Name=="Changed during approval" && canvas.CapturePendingDrawing()!.Points.SequenceEqual([new Point2(3,4)]),
                "Changing the pending drawing during its confirmation preserves the new drawing",checks);
            canvas.CancelDrawing();studio.ConfirmOpenDrawingForVerification=null;

            studio.ConfirmOpenDiscardForVerification=()=> (true,true);
            pending=studio.OpenProjectFileAsync(path);PumpUntil(()=>pending.IsCompleted);pending.GetAwaiter().GetResult();
            Check(studio.Document.Name=="Changed during approval","Saving in discard prompt does not apply stale open approval",checks);

            reached.Reset();release.Reset();
            studio.BeforeProjectReadForVerification=()=> { reached.Set();if(!release.Wait(TimeSpan.FromSeconds(30)))throw new TimeoutException(); };
            pending=studio.OpenProjectFileAsync(path);PumpUntil(()=>reached.IsSet);
            studio.CancelProjectOpen();release.Set();PumpUntil(()=>pending.IsCompleted);pending.GetAwaiter().GetResult();
            Check(studio.Document.Name=="Changed during approval","Cancelled read cannot replace current work",checks);

            reached.Reset();release.Reset();
            pending=studio.OpenProjectFileAsync(path);PumpUntil(()=>reached.IsSet);
            var closed=typeof(MainWindow).GetField("windowClosed",PrivateInstance)!;
            closed.SetValue(studio,true);release.Set();PumpUntil(()=>pending.IsCompleted);pending.GetAwaiter().GetResult();closed.SetValue(studio,false);
            Check(studio.Document.Name=="Changed during approval","Read completed after window closure cannot replace work",checks);

            studio.BeforeProjectReadForVerification=null;
            studio.ConfirmOpenDiscardForVerification=()=> { ProjectStorage.Save(new ProjectDocument { Name="Changed on disk" },path);return (true,false); };
            pending=studio.OpenProjectFileAsync(path);PumpUntil(()=>pending.IsCompleted);pending.GetAwaiter().GetResult();
            Check(studio.Document.Name=="Changed during approval" && ProjectStorage.Load(path).Name=="Changed on disk",
                "Source changed during approval prevents opening the stale snapshot and preserves work",checks);
            ProjectStorage.Save(new ProjectDocument { Name="Opened" },path);
            studio.ConfirmOpenDiscardForVerification=()=> (true,false);
            pending=studio.OpenProjectFileAsync(path);PumpUntil(()=>pending.IsCompleted);pending.GetAwaiter().GetResult();
            Check(studio.Document.Name=="Opened","Approved background project read installs loaded project",checks);
            ProjectStorage.Save(new ProjectDocument { Name="External change" },path);
            Exception? conflict=null;
            try { Invoke(studio,"SaveTo",path); } catch(Exception ex) { conflict=ex is TargetInvocationException { InnerException: { } inner } ? inner : ex; }
            Check(conflict is ProjectConflictException && ProjectStorage.Load(path).Name=="External change",
                "Saving an opened snapshot detects a later source change",checks);
            Exception? failure=null;studio.ProjectOpenErrorForVerification=ex=>failure=ex;
            pending=studio.OpenProjectFileAsync(Path.Combine(work,"missing-open.tcad"));PumpUntil(()=>pending.IsCompleted);pending.GetAwaiter().GetResult();
            Check(failure is not null && studio.Document.Name=="Opened","Failed project read preserves current document",checks);
        }
        finally
        {
            release.Set();if(pending is not null)PumpUntil(()=>pending.IsCompleted);
            studio.BeforeProjectReadForVerification=null;studio.ConfirmOpenDiscardForVerification=null;
            studio.ConfirmOpenDrawingForVerification=null;studio.ProjectOpenErrorForVerification=null;
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }
    private static void VerifyDxfGrainlineReviewControls(MainWindow studio,string reportPath,List<string> checks)
    {
        var contour=new PatternPiece { Name="Ön beden",Closed=true,Points=[new(0,0),new(20,0),new(0,20)] };
        var otherContour=new PatternPiece { Name="Arka beden",Closed=true,Points=[new(30,0),new(50,0),new(30,20)] };
        var marker=new PatternPiece { Name="Yön çizgisi",Closed=false,Points=[new(2,3),new(2,8)] };
        var otherMarker=new PatternPiece { Name="Diğer çizgi",Closed=false,Points=[new(32,3),new(32,8)] };
        var invalidMarker=new PatternPiece { Name="Çok noktalı",Closed=false,Points=[new(0,0),new(1,1),new(2,2)] };
        var project=new ProjectDocument { Pieces=[contour,otherContour,marker,otherMarker,invalidMarker] };
        var before=project.Clone();var import=new DxfImportResult(project,[]) { SourceSha256=new string('A',64),SourceByteLength=123 };
        var window=new DxfGrainlineReviewWindow(studio,import);
        var root=(FrameworkElement)window.Content;
        var contours=(ComboBox)typeof(DxfGrainlineReviewWindow).GetField("contours",PrivateInstance)!.GetValue(window)!;
        var markers=(ComboBox)typeof(DxfGrainlineReviewWindow).GetField("markers",PrivateInstance)!.GetValue(window)!;
        var list=(ListBox)typeof(DxfGrainlineReviewWindow).GetField("mappingList",PrivateInstance)!.GetValue(window)!;
        var status=(TextBlock)typeof(DxfGrainlineReviewWindow).GetField("status",PrivateInstance)!.GetValue(window)!;
        var apply=(Button)typeof(DxfGrainlineReviewWindow).GetField("apply",PrivateInstance)!.GetValue(window)!;
        var contourPreview=(DxfPiecePreview)typeof(DxfGrainlineReviewWindow).GetField("contourPreview",PrivateInstance)!.GetValue(window)!;
        var markerPreview=(DxfPiecePreview)typeof(DxfGrainlineReviewWindow).GetField("markerPreview",PrivateInstance)!.GetValue(window)!;
        var overlay=(DxfGrainlineOverlayPreview)typeof(DxfGrainlineReviewWindow).GetField("overlay",PrivateInstance)!.GetValue(window)!;
        var markerDetails=(TextBox)typeof(DxfGrainlineReviewWindow).GetField("markerDetails",PrivateInstance)!.GetValue(window)!;
        try
        {
            Render(window,Path.ChangeExtension(reportPath,".dxf-grain-review.png"),950,800);
            var add=Descendants<Button>(root).Single(button=>Equals(button.Content,"Seçilen ikiliyi ekle"));
            var remove=Descendants<Button>(root).Single(button=>Equals(button.Content,"Seçili eşlemeyi kaldır"));
            Check(contours.Items.Count==2 && markers.Items.Count==2 && contours.SelectedIndex==-1 && markers.SelectedIndex==-1
                && !apply.IsEnabled && AutomationProperties.GetName(contours)=="Kapalı hedef kontur"
                && AutomationProperties.GetName(markers)=="İki uçlu açık çizgi" && AutomationProperties.GetLiveSetting(status)==AutomationLiveSetting.Polite,
                "Grain review shows only eligible candidates with no automatic association",checks);
            Check(overlay.ContourId is null && overlay.MarkerId is null && !string.IsNullOrWhiteSpace(AutomationProperties.GetName(overlay)),
                "Combined preview starts empty and has an accessible name",checks);
            contours.SelectedIndex=0;
            Check(overlay.ContourId is null && overlay.MarkerId is null && AutomationProperties.GetName(overlay).Contains("seçin"),"One selection alone does not display a partial pair",checks);
            var source=Descendants<TextBox>(root).Single(box=>AutomationProperties.GetName(box)=="Aktarım kaynağı kimliği");
            Check(source.Text.Contains(new string('A',64)) && source.Text.Contains("123 bayt")
                && Descendants<TextBlock>(root).Any(text=>text.Text.Contains("otomatik varsayılmaz") && text.Text.Contains("kontrol bekleyen")),
                "Grain review explains source identity and explicitly warns about consumed markers",checks);
            contours.SelectedIndex=0;markers.SelectedIndex=0;
            Check(overlay.ContourId==contour.Id && overlay.MarkerId==marker.Id &&
                AutomationProperties.GetName(overlay).Contains(contour.Id.ToString("D")) &&
                AutomationProperties.GetName(overlay).Contains(marker.Id.ToString("D")) &&
                AutomationProperties.GetName(overlay).Contains("Y 3") && AutomationProperties.GetName(overlay).Contains("Y 8"),
                "Combined preview exposes both selected identities and marker endpoints",checks);
            markers.SelectedIndex=1;
            Check(overlay.ContourId==contour.Id && overlay.MarkerId==otherMarker.Id && AutomationProperties.GetName(overlay).Contains(otherMarker.Id.ToString("D")),
                "Changing marker refreshes combined preview identity and accessible description",checks);
            contours.SelectedIndex=1;
            Check(overlay.ContourId==otherContour.Id && overlay.MarkerId==otherMarker.Id && AutomationProperties.GetName(overlay).Contains(otherContour.Id.ToString("D")),
                "Changing contour refreshes combined preview identity and accessible description",checks);
            markers.SelectedIndex=-1;
            Check(overlay.ContourId is null && overlay.MarkerId is null,"Clearing either selection removes the entire combined preview",checks);
            contours.SelectedIndex=0;markers.SelectedIndex=0;
            var sample=new DxfGrainlineOverlayPreview();sample.ShowPair(contour,marker);
            sample.Measure(new Size(220,220));sample.Arrange(new Rect(0,0,220,220));sample.UpdateLayout();
            var bitmap=new RenderTargetBitmap(220,220,96,96,PixelFormats.Pbgra32);bitmap.Render(sample);
            var pixels=new byte[220*220*4];bitmap.CopyPixels(pixels,220*4,0);
            bool CrimsonAt(int x,int y) { var i=(y*220+x)*4;return pixels[i+2]>140 && pixels[i+2]>pixels[i+1]*1.5 && pixels[i+2]>pixels[i]*1.5; }
            Check(CrimsonAt(30,65) && !CrimsonAt(110,65),
                "Combined preview places marker at its world-coordinate position relative to the contour",checks);
            Check(contourPreview.DisplayedPieceId==contour.Id && markerPreview.DisplayedPieceId==marker.Id
                && markerDetails.Text.Contains(marker.Id.ToString("D")) && markerDetails.Text.Contains("Y 3") && markerDetails.Text.Contains("Y 8")
                && AutomationProperties.GetName(contourPreview).Contains(contour.Id.ToString("D"))
                && AutomationProperties.GetName(markerPreview).Contains(marker.Id.ToString("D")),
                "Grain review displays selected contour and marker identity, endpoints and accessible previews",checks);
            add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(list.Items.Count==1 && apply.IsEnabled && status.Text.Contains("1 eşleme"),"Explicit grain pair can be added",checks);
            add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(list.Items.Count==1 && status.Text.Contains("yalnız bir"),"Duplicate grain pair is rejected",checks);
            contours.SelectedIndex=1;add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(list.Items.Count==1 && status.Text.Contains("yalnız bir"),"A marker cannot be used for two contours",checks);
            contours.SelectedIndex=0;markers.SelectedIndex=1;add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(list.Items.Count==1 && status.Text.Contains("yalnız bir"),"A contour cannot use two markers",checks);
            list.SelectedIndex=0;remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(list.Items.Count==0 && !apply.IsEnabled && window.Selections.Count==0 && DocumentSession.ContentEquals(before,project),
                "Removing the pair leaves the source and unapplied result untouched",checks);
            var skip=Descendants<Button>(root).Single(button=>Equals(button.Content,"Eşleme yapmadan devam"));
            // DialogResult cannot be set on an unshown, non-modal WPF window;
            // invoking the real handler still exercises its empty-selection branch.
            bool nonModal=false;
            try { skip.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
            catch(InvalidOperationException) { nonModal=true; }
            Check(nonModal && window.Selections.Count==0 && DocumentSession.ContentEquals(before,project),
                "Headless empty-continue handler clears selections without changing the import",checks);
        }
        finally { window.Close(); }
    }

    private static void VerifyDxfGrainlineReview(MainWindow studio,string work,List<string> checks)
    {
        var previous=SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(studio.Dispatcher));
        var path=Path.Combine(work,"grain-review.dxf");
        var source=System.Text.Encoding.UTF8.GetBytes("0\nSECTION\n2\nENTITIES\n"+
            "0\nLWPOLYLINE\n8\nCUT\n90\n3\n70\n1\n10\n0\n20\n0\n10\n20\n20\n0\n10\n0\n20\n20\n"+
            "0\nLINE\n8\nGRAIN\n10\n2\n20\n2\n11\n2\n21\n8\n0\nENDSEC\n0\nEOF\n");
        var review=(Button)typeof(MainWindow).GetField("dxfReview",PrivateInstance)!.GetValue(studio)!;
        DxfImportResult Prepare()
        {
            File.WriteAllBytes(path,source);
            studio.LoadDocument(new ProjectDocument { Name="Mevcut çalışma",Pieces=[new PatternPiece { Name="Eski",Closed=false,Points=[new(0,0),new(1,0)] }] });
            var task=studio.StartDxfPreparationAsync(path,1);PumpUntil(()=>task.IsCompleted);
            return task.GetAwaiter().GetResult() ?? throw new InvalidOperationException("DXF grain review fixture failed.");
        }
        void Run() { var task=studio.ReviewPreparedDxfAsync();PumpUntil(()=>task.IsCompleted);task.GetAwaiter().GetResult(); }
        DxfGrainlineSelection Selection(DxfImportResult input) => new(input.Project.Pieces.Single(p=>p.Closed).Id,input.Project.Pieces.Single(p=>!p.Closed).Id);
        try
        {
            studio.ConfirmDxfReviewForVerification=_=>true;
            studio.DxfDiscardDecisionForVerification=()=>(true,false);
            var imported=Prepare();var originalImport=imported.Project.Clone();var prior=studio.Document.Clone();
            studio.ConfirmDxfGrainlineReviewForVerification=_=>null;Run();
            Check(DocumentSession.ContentEquals(prior,studio.Document) && review.Visibility==Visibility.Visible && File.ReadAllBytes(path).SequenceEqual(source),
                "Cancelling grain review preserves workspace and source and offers retry",checks);
            studio.ConfirmDxfGrainlineReviewForVerification=i=>[Selection(i)];Run();
            var contour=studio.Document.Pieces.Single();
            Check(contour.Grainline is { NeedsReview:true } && contour.Grainline.Start==new Point2(2,2) && contour.Grainline.End==new Point2(2,8) &&
                DocumentSession.ContentEquals(imported.Project,originalImport) && File.ReadAllBytes(path).SequenceEqual(source) &&
                studio.Document.ImportProvenance?.SourceSha256==Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(source)),
                "Explicit grain selection consumes only copied marker and records review and source identity",checks);

            imported=Prepare();studio.ConfirmDxfGrainlineReviewForVerification=_=>Array.Empty<DxfGrainlineSelection>();Run();
            Check(studio.Document.Pieces.Count==2 && studio.Document.Pieces.All(p=>p.Grainline is null),
                "Continuing without grain mapping preserves imported contours and marker",checks);

            foreach(var duplicate in new[] { false,true })
            {
                imported=Prepare();prior=studio.Document.Clone();
                studio.ConfirmDxfGrainlineReviewForVerification=i=>duplicate ? [Selection(i),Selection(i)] : [new DxfGrainlineSelection(i.Project.Pieces.Single(p=>p.Closed).Id,Guid.NewGuid())];
                Run();
                Check(DocumentSession.ContentEquals(prior,studio.Document) && review.Visibility==Visibility.Visible && File.ReadAllBytes(path).SequenceEqual(source),
                    duplicate ? "Duplicate grain marker is rejected with retry and no workspace change" : "Unknown grain marker is rejected with retry and no workspace change",checks);
            }
            studio.ConfirmDxfGrainlineReviewForVerification=i=>[Selection(i)];Run();
            Check(studio.Document.Pieces.Count==1 && studio.Document.Pieces[0].Grainline is { NeedsReview:true },
                "Retry after invalid grain choice applies an explicit valid mapping",checks);

            imported=Prepare();prior=studio.Document.Clone();
            studio.ConfirmDxfGrainlineReviewForVerification=_=>throw new InvalidOperationException("injected grain review failure");Run();
            Check(DocumentSession.ContentEquals(prior,studio.Document) && review.Visibility==Visibility.Visible,
                "Grain review exception retains workspace and allows retry",checks);
            studio.ConfirmDxfGrainlineReviewForVerification=i=> { studio.Commit("Change during grain review",()=>studio.Document.Name="Yeni çalışma");return [Selection(i)]; };Run();
            Check(studio.Document.Name=="Yeni çalışma" && File.ReadAllBytes(path).SequenceEqual(source),
                "Workspace edit during grain review invalidates selection without reverting edit",checks);

            imported=Prepare();prior=studio.Document.Clone();
            studio.ConfirmDxfGrainlineReviewForVerification=i=> { File.AppendAllText(path," ");return [Selection(i)]; };Run();
            Check(DocumentSession.ContentEquals(prior,studio.Document) && !File.ReadAllBytes(path).SequenceEqual(source),
                "Changed DXF source after grain selection is not applied or overwritten",checks);

            foreach(var mutateInPreview in new[] { true,false })
            {
                imported=Prepare();prior=studio.Document.Clone();var changed=0;
                studio.ConfirmDxfReviewForVerification=i=>
                {
                    if(mutateInPreview) { i.Project.Pieces[0].Name="Değişmiş hazırlık";changed++; }
                    return true;
                };
                studio.ConfirmDxfGrainlineReviewForVerification=i=>
                {
                    if(!mutateInPreview) { i.Project.Pieces[0].Name="Değişmiş hazırlık";changed++; }
                    return Array.Empty<DxfGrainlineSelection>();
                };
                Run();
                Check(changed==1 && DocumentSession.ContentEquals(prior,studio.Document) && File.ReadAllBytes(path).SequenceEqual(source),
                    mutateInPreview ? "Changing prepared import during first preview rejects application and preserves source and workspace"
                                    : "Changing prepared import while continuing without a mapping rejects application and preserves source and workspace",checks);
                var retryInvoked=false;
                studio.ConfirmDxfReviewForVerification=_=> { retryInvoked=true;return false; };
                Run();
                Check(changed==1 && !retryInvoked && DocumentSession.ContentEquals(prior,studio.Document) && File.ReadAllBytes(path).SequenceEqual(source) && review.Visibility!=Visibility.Visible,
                    "Retry cannot re-baseline a mutated prepared import",checks);
            }
            foreach(var throwAfterMutation in new[] { false,true })
            {
                imported=Prepare();prior=studio.Document.Clone();var callbackCount=0;
                studio.ConfirmDxfReviewForVerification=_=>true;
                studio.ConfirmDxfGrainlineReviewForVerification=i=>
                {
                    callbackCount++;i.Project.Pieces[0].Name="Değişmiş inceleme";
                    if(throwAfterMutation)throw new InvalidOperationException("injected after mutation");
                    return null;
                };
                Run();
                Check(callbackCount==1 && DocumentSession.ContentEquals(prior,studio.Document) && File.ReadAllBytes(path).SequenceEqual(source) && review.Visibility!=Visibility.Visible,
                    throwAfterMutation ? "Mutating prepared import then throwing closes retry and preserves source and workspace"
                                       : "Mutating prepared import then cancelling closes retry and preserves source and workspace",checks);
                var previewRetried=false;
                studio.ConfirmDxfReviewForVerification=_=> { previewRetried=true;return true; };
                Run();
                Check(!previewRetried && callbackCount==1 && DocumentSession.ContentEquals(prior,studio.Document) && File.ReadAllBytes(path).SequenceEqual(source) && review.Visibility!=Visibility.Visible,
                    "Mutated cancelled or failed grain review cannot be re-baselined on retry",checks);
            }
        }
        finally
        {
            studio.ConfirmDxfReviewForVerification=null;studio.ConfirmDxfGrainlineReviewForVerification=null;
            studio.DxfDiscardDecisionForVerification=null;SynchronizationContext.SetSynchronizationContext(previous);
        }
    }
    private static void VerifyAsyncDxfBulgeImport(MainWindow studio,string work,string reportPath,List<string> checks)
    {
        var previous=SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(studio.Dispatcher));
        var path=Path.Combine(work,"async-bulge.dxf");
        var source=System.Text.Encoding.UTF8.GetBytes("0\nSECTION\n2\nENTITIES\n"+
            "0\nLWPOLYLINE\n8\nPOS\n90\n2\n70\n0\n10\n0\n20\n0\n42\n1\n10\n10\n20\n0\n"+
            "0\nLWPOLYLINE\n8\nNEG\n90\n2\n70\n0\n10\n0\n20\n20\n42\n-1\n10\n10\n20\n20\n"+
            "0\nLINE\n8\nSTRAIGHT\n10\n40\n20\n0\n11\n50\n21\n0\n0\nENDSEC\n0\nEOF\n");
        File.WriteAllBytes(path,source);
        studio.LoadDocument(new ProjectDocument { Name="Özgün",Pieces=[new PatternPiece { Closed=false,Points=[new(0,0),new(1,0)] }] });
        var original=studio.Document.Clone();
        try
        {
            var task=studio.StartDxfPreparationWithBulgeAsync(path,1,DxfTextEncoding.Utf8,0.1);
            PumpUntil(()=>task.IsCompleted);var imported=task.GetAwaiter().GetResult() ?? throw new InvalidOperationException("Bulge fixture was not prepared.");
            Check(imported.Project.Pieces.Count==3 && imported.Project.Pieces.Single(p=>p.Name=="POS").Points.Count>2 &&
                imported.Project.Pieces.Single(p=>p.Name=="NEG").Points.Count>2 && imported.Project.Pieces.Single(p=>p.Name=="POS").Points.Any(p=>p.Y<0) &&
                imported.Project.Pieces.Single(p=>p.Name=="NEG").Points.Any(p=>p.Y>20) &&
                imported.ImportParameters?.BulgeToleranceMm==0.1 && imported.SourceSha256==Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(source)) &&
                File.ReadAllBytes(path).SequenceEqual(source) && DocumentSession.ContentEquals(original,studio.Document),
                "Opt-in bulge prepares both directed arcs with captured source hash and tolerance without replacing workspace",checks);
            var preview=(Window)typeof(MainWindow).GetMethod("BuildImportPreviewCapturedWithState",PrivateInstance)!.Invoke(studio,
                [imported.Project,imported.Warnings,1.0,"async-bulge.dxf",imported.Units,imported.SourceSha256,imported.SourceByteLength,(Action)(()=>{}),imported.SkippedRelevantInsertEntities])!;
            try
            {
                Render(preview,Path.ChangeExtension(reportPath,".dxf-bulge-import-preview.png"),800,650);
                Check(Descendants<TextBlock>((FrameworkElement)preview.Content).Any(t=>t.Text.Contains("Bulge") && t.Text.Contains("garanti edilmez")),
                    "Bulge preview displays the approximation and non-guarantee warning",checks);
            }
            finally { preview.Close(); }
            var legacy=studio.StartDxfPreparationAsync(path,1,DxfTextEncoding.Utf8);PumpUntil(()=>legacy.IsCompleted);
            var ordinary=legacy.GetAwaiter().GetResult() ?? throw new InvalidOperationException("Legacy fixture was not prepared.");
            Check(ordinary.ImportParameters?.BulgeToleranceMm is null && ordinary.Project.Pieces.Count==1 && ordinary.Project.Pieces[0].Name=="STRAIGHT" && ordinary.Warnings.Any(w=>w.Contains("bulge",StringComparison.OrdinalIgnoreCase)),
                "Default DXF mode continues to skip bulge contours and disclose the omission",checks);
            var bad=Path.Combine(work,"async-invalid-bulge.dxf");File.WriteAllText(bad,System.Text.Encoding.UTF8.GetString(source).Replace("42\n1\n","42\ninvalid\n"));
            var failed=studio.StartDxfPreparationWithBulgeAsync(bad,1,DxfTextEncoding.Utf8,0.1);PumpUntil(()=>failed.IsCompleted);
            var rejected=false;try { failed.GetAwaiter().GetResult(); } catch(InvalidDataException) { rejected=true; }
            Check(rejected && DocumentSession.ContentEquals(original,studio.Document),"Invalid bulge input preserves previous workspace",checks);
            using var reached=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
            studio.BeforeDxfPreparationForVerification=()=> { reached.Set();if(!release.Wait(TimeSpan.FromSeconds(30)))throw new TimeoutException(); };
            var pending=studio.StartDxfPreparationWithBulgeAsync(path,1,DxfTextEncoding.Utf8,0.1);
            try
            {
                PumpUntil(()=>reached.IsSet);Invoke(studio,"RequestDxfCancellation");release.Set();PumpUntil(()=>pending.IsCompleted);
                Check(pending.GetAwaiter().GetResult() is null && DocumentSession.ContentEquals(original,studio.Document) && File.ReadAllBytes(path).SequenceEqual(source),
                    "Cancelled bulge preparation preserves workspace and source",checks);
            }
            finally { release.Set();PumpUntil(()=>pending.IsCompleted); }
            reached.Reset();release.Reset();
            pending=studio.StartDxfPreparationWithBulgeAsync(path,1,DxfTextEncoding.Utf8,0.1);
            try
            {
                PumpUntil(()=>reached.IsSet);studio.Commit("Edit during bulge preparation",()=>studio.Document.Name="Yeni çalışma");
                release.Set();PumpUntil(()=>pending.IsCompleted);
                Check(pending.GetAwaiter().GetResult() is null && studio.Document.Name=="Yeni çalışma" && File.ReadAllBytes(path).SequenceEqual(source),
                    "Workspace changes invalidate bulge preparation without reverting edits or source",checks);
            }
            finally { release.Set();PumpUntil(()=>pending.IsCompleted); }
        }
        finally { studio.BeforeDxfPreparationForVerification=null;SynchronizationContext.SetSynchronizationContext(previous); }
    }
    private static void VerifyAsyncDxfImport(MainWindow studio,string work,List<string> checks)
    {
        var previousContext=SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(studio.Dispatcher));
        var path=Path.Combine(work,"async-import.dxf");
        var invalid=Path.Combine(work,"async-invalid.dxf");
        File.WriteAllText(path,"0\nSECTION\n2\nENTITIES\n0\nLINE\n10\n0\n20\n0\n11\n100\n21\n0\n0\nARC\n10\n50\n20\n50\n40\n20\n50\n0\n51\n90\n0\nENDSEC\n0\nEOF\n");
        File.WriteAllText(invalid,"0\nSECTION\n2\nENTITIES\n0\nLINE\n10\n0\n20\n0\n11\n10\n0\nENDSEC\n0\nEOF\n");
        try
        {
            studio.LoadDocument(new ProjectDocument { Pieces=[new PatternPiece { Name="Existing",Points=[new(0,0),new(10,0),new(10,10)] }] });
            var original=JsonSerializer.Serialize(studio.Document);
            using var reached=new ManualResetEventSlim();
            using var release=new ManualResetEventSlim();
            studio.BeforeDxfPreparationForVerification=()=> { reached.Set(); if(!release.Wait(TimeSpan.FromSeconds(30)))throw new TimeoutException("DXF preparation boundary timed out."); };
            var task=studio.StartDxfPreparationAsync(path,1);
            try
            {
                PumpUntil(()=>reached.IsSet);
                var dispatched=false;studio.Dispatcher.BeginInvoke(new Action(()=>dispatched=true));PumpUntil(()=>dispatched);
                Check(studio.DxfImportRunning && !studio.IsVisible,"DXF preparation leaves the dispatcher responsive without showing a window",checks);
                var duplicate=studio.StartDxfPreparationAsync(path,1);
                PumpUntil(()=>duplicate.IsCompleted);
                try { duplicate.GetAwaiter().GetResult();throw new InvalidOperationException("Concurrent DXF preparation accepted"); }
                catch(InvalidOperationException ex) when(ex.Message.Contains("DXF okuma sürüyor")) { }
                release.Set();PumpUntil(()=>task.IsCompleted);
                var result=task.GetAwaiter().GetResult();
                Check(result is not null && studio.IsDxfPreparationCurrent(result) && result.Project.Pieces.Count==1 && result.Warnings.Count>0 && JsonSerializer.Serialize(studio.Document)==original,
                    "Background DXF retains parser warnings and does not replace the live document",checks);
                var savedPath=Path.Combine(work,"async-dxf-save.tcad");
                studio.SaveAsTo(savedPath,studio.CaptureSaveAsDestination(savedPath));
                Check(!studio.IsDxfPreparationCurrent(result!) && studio.IsDxfPreparationCurrentAfterSave(result!),
                    "A successful save permits the unchanged prepared DXF to continue",checks);
                studio.Commit("Invalidate prepared DXF",()=>studio.Document.Pieces[0].Name="Changed");
                Check(!studio.IsDxfPreparationCurrent(result!) && !studio.IsDxfPreparationCurrentAfterSave(result!),
                    "Editing after DXF preparation invalidates both ordinary and saved-state review",checks);
            }
            finally { release.Set();PumpUntil(()=>task.IsCompleted);studio.BeforeDxfPreparationForVerification=null; }

            reached.Reset();release.Reset();
            studio.BeforeDxfPreparationForVerification=()=> { reached.Set();if(!release.Wait(TimeSpan.FromSeconds(30)))throw new TimeoutException("DXF stale boundary timed out."); };
            var stale=studio.StartDxfPreparationAsync(path,1);
            try
            {
                PumpUntil(()=>reached.IsSet);
                studio.Commit("Edit during DXF preparation",()=>studio.Document.Pieces[0].Name="Live edit");
                release.Set();PumpUntil(()=>stale.IsCompleted);
                Check(stale.GetAwaiter().GetResult() is null && studio.Document.Pieces[0].Name=="Live edit",
                    "A document edit during DXF preparation rejects the stale result and preserves the edit",checks);
            }
            finally { release.Set();PumpUntil(()=>stale.IsCompleted);studio.BeforeDxfPreparationForVerification=null; }

            var beforeFailure=JsonSerializer.Serialize(studio.Document);
            var failed=studio.StartDxfPreparationAsync(invalid,1);PumpUntil(()=>failed.IsCompleted);
            try { failed.GetAwaiter().GetResult();throw new InvalidOperationException("Malformed asynchronous DXF was accepted"); }
            catch(InvalidDataException) { }
            Check(!studio.DxfImportRunning && JsonSerializer.Serialize(studio.Document)==beforeFailure,
                "Failed background DXF parsing restores idle state and preserves the document",checks);
        }
        finally { studio.BeforeDxfPreparationForVerification=null;SynchronizationContext.SetSynchronizationContext(previousContext); }
    }
    private static void VerifyDxfReviewRetry(MainWindow studio,string work,List<string> checks)
    {
        var previousContext=SynchronizationContext.Current;SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(studio.Dispatcher));
        var path=Path.Combine(work,"dxf-review-retry.dxf");var bytes=System.Text.Encoding.UTF8.GetBytes("0\nSECTION\n2\nENTITIES\n0\nLINE\n10\n0\n20\n0\n11\n20\n21\n0\n0\nENDSEC\n0\nEOF\n");
        var review=(Button)typeof(MainWindow).GetField("dxfReview",PrivateInstance)!.GetValue(studio)!;
        var notice=(TextBlock)typeof(MainWindow).GetField("dxfNotice",PrivateInstance)!.GetValue(studio)!;
        DxfImportResult Prepare(bool dirty=false)
        {
            File.WriteAllBytes(path,bytes);
            var existingPiece=new PatternPiece { Name="Kaynak",Points=[new(0,0),new(10,0),new(0,10)] };
            studio.LoadDocument(new ProjectDocument { Name="Özgün çalışma",Pieces=[existingPiece],
                ImportProvenance=new DxfImportProvenance { SourceFileName="onceki.dxf",SourceSha256=new string('A',64),SourceByteLength=1,
                    MillimetersPerUnit=1,Warnings=["Önceki uyarı"],PieceIds=[existingPiece.Id] } });
            studio.Select(studio.Document.Pieces[0].Id);
            if(dirty)studio.Commit("Kaydedilecek değişiklik",()=>studio.Document.Name="Kaydedilecek çalışma");
            var task=studio.StartDxfPreparationAsync(path,1);PumpUntil(()=>task.IsCompleted);
            return task.GetAwaiter().GetResult() ?? throw new InvalidOperationException("DXF retry fixture preparation failed.");
        }
        void Run() { var task=studio.ReviewPreparedDxfAsync();PumpUntil(()=>task.IsCompleted);task.GetAwaiter().GetResult(); }
        ProjectDocument Expected(DxfImportResult input)
        {
            var doc=input.Project.Clone();doc.ImportProvenance=new DxfImportProvenance {
                SourceFileName=Path.GetFileName(path),SourceSha256=input.SourceSha256!,SourceByteLength=input.SourceByteLength!.Value,
                MillimetersPerUnit=1,Warnings=[..input.Warnings],PieceIds=doc.Pieces.Select(p=>p.Id).ToList() };
            return doc;
        }
        try
        {
            var incoming=Prepare();var original=studio.Document.Clone();var selected=studio.SelectedId;
            studio.ConfirmDxfReviewForVerification=_=>false;studio.DxfDiscardDecisionForVerification=()=>throw new InvalidOperationException("Discard should not be requested.");
            Run();
            Check(review.Visibility==Visibility.Visible && notice.Text.Contains("yeniden inceleyebilirsiniz") && DocumentSession.ContentEquals(original,studio.Document) && studio.SelectedId==selected,
                "Declining DXF preview preserves source workspace and offers review retry",checks);
            studio.ConfirmDxfReviewForVerification=_=>true;studio.DxfDiscardDecisionForVerification=()=>(false,false);Run();
            Check(review.Visibility==Visibility.Visible && notice.Text.Contains("yeniden inceleyebilirsiniz") && DocumentSession.ContentEquals(original,studio.Document),
                "Declining workspace replacement keeps the same DXF review retry",checks);
            studio.DxfDiscardDecisionForVerification=()=>(true,false);Run();
            Check(DocumentSession.ContentEquals(Expected(incoming),studio.Document) && File.ReadAllBytes(path).SequenceEqual(bytes) && review.Visibility==Visibility.Collapsed,
                "Retrying an unchanged DXF review applies the captured project without changing its source",checks);
            var savedImport=Path.Combine(work,"dxf-provenance-"+Guid.NewGuid().ToString("N")+".tcad");
            Invoke(studio,"SaveTo",savedImport);
            Check(DocumentSession.ContentEquals(Expected(incoming),ProjectStorage.Load(savedImport)) &&
                studio.Document.ImportProvenance!.SourceSha256==Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)) &&
                studio.Document.ImportProvenance.SourceByteLength==bytes.Length,
                "Imported DXF source identity and warnings survive project save and reopen",checks);
            var savedWarningCount=studio.Document.ImportProvenance.Warnings.Count;
            incoming.Warnings.Add("sonradan değişen kaynak listesi");
            Check(studio.Document.ImportProvenance.Warnings.Count==savedWarningCount,
                "Imported DXF provenance owns an independent warning list",checks);

            incoming=Prepare();original=studio.Document.Clone();selected=studio.SelectedId;
            studio.ConfirmDxfReviewForVerification=_=>true;studio.DxfDiscardDecisionForVerification=()=>(true,false);
            using(var reached=new ManualResetEventSlim())using(var release=new ManualResetEventSlim())
            {
                studio.BeforeDxfSourceCheckForVerification=()=> { reached.Set();release.Wait(); };
                var pending=studio.ReviewPreparedDxfAsync();
                try
                {
                    PumpUntil(()=>reached.IsSet);var duplicate=studio.ReviewPreparedDxfAsync();PumpUntil(()=>duplicate.IsCompleted);
                    Check(!pending.IsCompleted && studio.DxfImportRunning,"A second DXF review cannot start during source verification",checks);
                    studio.RequestDxfCancellation();release.Set();PumpUntil(()=>pending.IsCompleted);pending.GetAwaiter().GetResult();
                    Check(review.Visibility==Visibility.Visible && notice.Text.Contains("iptal edildi") && DocumentSession.ContentEquals(original,studio.Document) && studio.SelectedId==selected,
                        "Canceling DXF source verification offers a safe review retry",checks);
                }
                finally { release.Set();PumpUntil(()=>pending.IsCompleted);studio.BeforeDxfSourceCheckForVerification=null; }
            }
            Run();Check(DocumentSession.ContentEquals(Expected(incoming),studio.Document),"Retry after canceled DXF source check can complete",checks);

            incoming=Prepare();original=studio.Document.Clone();
            studio.ConfirmDxfReviewForVerification=_=> { File.WriteAllText(path,"başka sürüm");return true; };studio.DxfDiscardDecisionForVerification=()=>(true,false);Run();
            Check(review.Visibility==Visibility.Collapsed && notice.Text.Contains("farklı") && DocumentSession.ContentEquals(original,studio.Document) && File.ReadAllText(path)=="başka sürüm",
                "Changed source disables DXF review retry and preserves both workspace and external file",checks);

            incoming=Prepare();studio.ConfirmDxfReviewForVerification=_=> { studio.Commit("Önizlemede düzenleme",()=>studio.Document.Name="Değişti");return false; };Run();
            Check(review.Visibility==Visibility.Collapsed && notice.Text.Contains("değişti") && studio.Document.Name=="Değişti" &&
                studio.Document.ImportProvenance is { SourceFileName:"onceki.dxf" } staleHistory && staleHistory.Warnings.SequenceEqual(["Önceki uyarı"]),
                "Stale workspace does not offer retry after declining DXF preview",checks);

            incoming=Prepare(true);var previousSaved=Path.Combine(work,"dxf-post-load-"+Guid.NewGuid().ToString("N")+".tcad");
            studio.ConfirmDxfReviewForVerification=_=>true;
            studio.DxfDiscardDecisionForVerification=()=> { Invoke(studio,"SaveTo",previousSaved);return (true,true); };
            studio.AfterDxfLoadBeforeRefreshForVerification=()=>throw new InvalidOperationException("injected refresh failure");
            try { Run(); } finally { studio.AfterDxfLoadBeforeRefreshForVerification=null; }
            var dxfSession=(DocumentSession)typeof(MainWindow).GetField("session",PrivateInstance)!.GetValue(studio)!;
            var activePath=typeof(MainWindow).GetField("filePath",PrivateInstance)!.GetValue(studio);
            var activeVersion=typeof(MainWindow).GetField("fileVersion",PrivateInstance)!.GetValue(studio);
            var prepared=typeof(MainWindow).GetField("preparedDxf",PrivateInstance)!.GetValue(studio);
            Check(DocumentSession.ContentEquals(Expected(incoming),studio.Document) && dxfSession.IsDirty && dxfSession.UndoCount==0 && activePath is null && activeVersion is null && prepared is null && review.Visibility==Visibility.Collapsed && notice.Text.Contains("projeye yüklendi") && notice.Text.Contains("görünüm yenilenemedi") && ProjectStorage.Load(previousSaved).Name=="Kaydedilecek çalışma" && File.ReadAllBytes(path).SequenceEqual(bytes),"DXF post-load refresh failure reports imported unsaved document and preserves previous saved file",checks);
            WaitRecovery(studio.TriggerRecoveryForVerificationAsync());
            var activeRecovery=(RecoveryStore)typeof(MainWindow).GetField("recoveryStore",PrivateInstance)!.GetValue(studio)!;
            Check(File.Exists(activeRecovery.DraftRecoveryPath) && DocumentSession.ContentEquals(RecoveryEnvelope.Deserialize(File.ReadAllBytes(activeRecovery.DraftRecoveryPath)).Document,Expected(incoming)),"DXF post-load refresh failure retains imported content in recovery snapshot",checks);
            incoming=Prepare();studio.ConfirmDxfReviewForVerification=_=>true;studio.DxfDiscardDecisionForVerification=()=>(true,false);
            studio.AfterDxfLoadBeforeRefreshForVerification=()=>studio.DuringRefreshForVerification=()=>throw new InvalidOperationException("injected during refresh");
            try { Run(); } finally { studio.AfterDxfLoadBeforeRefreshForVerification=null;studio.DuringRefreshForVerification=null; }
            var syncing=(bool)typeof(MainWindow).GetField("syncing",PrivateInstance)!.GetValue(studio)!;
            Check(!syncing && DocumentSession.ContentEquals(Expected(incoming),studio.Document) && notice.Text.Contains("projeye yüklendi"),"DXF refresh failure restores UI synchronization flag and retains imported content",checks);
            studio.Select(studio.Document.Pieces[0].Id);
            var nameField=(TextBox)typeof(MainWindow).GetField("name",PrivateInstance)!.GetValue(studio)!;
            nameField.Text="Daha sonra düzenlenen parça";
            var drafts=(System.Collections.IDictionary)typeof(MainWindow).GetField("propertyDrafts",PrivateInstance)!.GetValue(studio)!;
            Check(drafts.Contains(studio.Document.Pieces[0].Id),"Property edits still create a draft after an interrupted refresh",checks);
            var unicode="Ön"+char.ConvertFromUtf32(0xE0001)+"😀";
            var safe=(string)typeof(MainWindow).GetMethod("SafePieceName",PrivateInstance|BindingFlags.Static)!.Invoke(null,[unicode])!;
            var needs=(bool)typeof(MainWindow).GetMethod("PieceNameNeedsEscaping",PrivateInstance|BindingFlags.Static)!.Invoke(null,[unicode])!;
            Check(needs && safe=="Ön\\uDB40\\uDC01😀","DXF preview escapes invisible supplementary format characters without changing emoji",checks);
            var safeLayer=(string)typeof(DxfLayerReviewWindow).GetMethod("SafeText",PrivateInstance|BindingFlags.Static)!.Invoke(null,[unicode])!;
            Check(safeLayer=="Ön\\uDB40\\uDC01😀" && unicode.Contains(char.ConvertFromUtf32(0xE0001)),"DXF layer review safely renders supplementary format controls and preserves source",checks);
            incoming=Prepare(true);var savedPath=Path.Combine(work,"dxf-retry-saved.tcad");
            studio.ConfirmDxfReviewForVerification=_=>true;studio.DxfDiscardDecisionForVerification=()=> { Invoke(studio,"SaveTo",savedPath);return (true,true); };
            using(var reached=new ManualResetEventSlim())using(var release=new ManualResetEventSlim())
            {
                studio.BeforeDxfSourceCheckForVerification=()=> { reached.Set();release.Wait(); };var pending=studio.ReviewPreparedDxfAsync();
                try
                {
                    PumpUntil(()=>reached.IsSet);studio.RequestDxfCancellation();release.Set();PumpUntil(()=>pending.IsCompleted);pending.GetAwaiter().GetResult();
                    Check(review.Visibility==Visibility.Visible && File.Exists(savedPath) && ProjectStorage.Load(savedPath).Name=="Kaydedilecek çalışma",
                        "DXF source cancellation after save retains saved workspace and offers retry",checks);
                }
                finally { release.Set();PumpUntil(()=>pending.IsCompleted);studio.BeforeDxfSourceCheckForVerification=null; }
            }
            studio.DxfDiscardDecisionForVerification=()=>(true,false);Run();
            Check(DocumentSession.ContentEquals(Expected(incoming),studio.Document) && ProjectStorage.Load(savedPath).Name=="Kaydedilecek çalışma",
                "Retry after saved workspace rechecks and applies DXF without changing saved project",checks);
        }
        finally
        {
            studio.ConfirmDxfReviewForVerification=null;studio.DxfDiscardDecisionForVerification=null;studio.BeforeDxfSourceCheckForVerification=null;
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }
    private static void VerifyDxfSourceCheckWorkflow(MainWindow studio,string work,List<string> checks)
    {
        var previousContext=SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(studio.Dispatcher));
        var path=Path.Combine(work,"source-check-workflow.dxf");
        var bytes=System.Text.Encoding.UTF8.GetBytes("0\nSECTION\n2\nENTITIES\n0\nLINE\n8\n0\n10\n0\n20\n0\n11\n10\n21\n0\n0\nENDSEC\n0\nEOF\n");
        var notice=(TextBlock)typeof(MainWindow).GetField("dxfNotice",PrivateInstance)!.GetValue(studio)!;
        var session=(DocumentSession)typeof(MainWindow).GetField("session",PrivateInstance)!.GetValue(studio)!;
        DxfImportResult Prepare()
        {
            File.WriteAllBytes(path,bytes);
            studio.LoadDocument(new ProjectDocument { Name="Korunan proje",Pieces=[new PatternPiece { Points=[new(0,0),new(10,0),new(0,10)] }] });
            studio.Select(studio.Document.Pieces[0].Id);
            var task=studio.StartDxfPreparationAsync(path,1);PumpUntil(()=>task.IsCompleted);
            return task.GetAwaiter().GetResult() ?? throw new InvalidOperationException("DXF preparation unexpectedly canceled.");
        }
        bool Verify(DxfImportResult result,bool saved=false)
        {
            var task=studio.VerifyDxfSourceBeforeApplyAsync(result,saved);PumpUntil(()=>task.IsCompleted);return task.GetAwaiter().GetResult();
        }
        try
        {
            var incoming=Prepare();var document=studio.Document.Clone();var selected=studio.SelectedId;var undo=session.UndoCount;var redo=session.RedoCount;
            Check(Verify(incoming) && DocumentSession.ContentEquals(document,studio.Document) && File.ReadAllBytes(path).SequenceEqual(bytes),
                "Unchanged DXF source matches the captured bytes without replacing the document",checks);

            incoming=Prepare();document=studio.Document.Clone();File.WriteAllText(path,"changed");
            Check(!Verify(incoming) && notice.Text.Contains("farklı") && DocumentSession.ContentEquals(document,studio.Document)
                && File.ReadAllText(path)=="changed","Changed DXF source rejects apply and preserves document and external bytes",checks);

            incoming=Prepare();document=studio.Document.Clone();File.Delete(path);
            Check(!Verify(incoming) && notice.Text.Contains("bulunamadı") && DocumentSession.ContentEquals(document,studio.Document),
                "Missing DXF source rejects apply and preserves the document",checks);

            incoming=Prepare();document=studio.Document.Clone();
            using(var locked=new FileStream(path,FileMode.Open,FileAccess.ReadWrite,FileShare.None))
                Check(!Verify(incoming) && notice.Text.Contains("okunamadı") && DocumentSession.ContentEquals(document,studio.Document),
                    "Locked DXF source rejects apply and preserves the document",checks);
            Check(File.ReadAllBytes(path).SequenceEqual(bytes),"Failed source check does not change DXF bytes",checks);

            incoming=Prepare();document=studio.Document.Clone();selected=studio.SelectedId;undo=session.UndoCount;redo=session.RedoCount;
            using(var reached=new ManualResetEventSlim())using(var release=new ManualResetEventSlim())
            {
                studio.BeforeDxfSourceCheckForVerification=()=> { reached.Set();if(!release.Wait(TimeSpan.FromSeconds(30)))throw new TimeoutException("DXF source check boundary timed out."); };
                var pending=studio.VerifyDxfSourceBeforeApplyAsync(incoming,false);
                try
                {
                    PumpUntil(()=>reached.IsSet);
                    var duplicate=studio.VerifyDxfSourceBeforeApplyAsync(incoming,false);PumpUntil(()=>duplicate.IsCompleted);
                    Check(studio.DxfImportRunning && !duplicate.GetAwaiter().GetResult(),"Concurrent DXF source check is rejected",checks);
                    studio.RequestDxfCancellation();release.Set();PumpUntil(()=>pending.IsCompleted);
                    Check(!pending.GetAwaiter().GetResult() && notice.Text.Contains("iptal") && !studio.DxfImportRunning
                        && DocumentSession.ContentEquals(document,studio.Document) && selected==studio.SelectedId
                        && undo==session.UndoCount && redo==session.RedoCount && File.ReadAllBytes(path).SequenceEqual(bytes),
                        "Canceling DXF source check preserves source, document, selection and history",checks);
                }
                finally { release.Set();PumpUntil(()=>pending.IsCompleted);studio.BeforeDxfSourceCheckForVerification=null; }
            }

            incoming=Prepare();
            using(var reached=new ManualResetEventSlim())using(var release=new ManualResetEventSlim())
            {
                studio.AfterDxfSourceCheckForVerification=()=> { reached.Set();if(!release.Wait(TimeSpan.FromSeconds(30)))throw new TimeoutException("DXF source check boundary timed out."); };
                var pending=studio.VerifyDxfSourceBeforeApplyAsync(incoming,false);
                try
                {
                    PumpUntil(()=>reached.IsSet);studio.Commit("DXF check stale",()=>studio.Document.Name="Kontrol sırasında değişti");
                    release.Set();PumpUntil(()=>pending.IsCompleted);
                    Check(!pending.GetAwaiter().GetResult() && studio.Document.Name=="Kontrol sırasında değişti" && notice.Text.Contains("değişti"),
                        "Document edit after hashing rejects stale DXF before apply",checks);
                }
                finally { release.Set();PumpUntil(()=>pending.IsCompleted);studio.AfterDxfSourceCheckForVerification=null; }
            }

            incoming=Prepare();studio.Commit("Before DXF save",()=>studio.Document.Name="Kaydedilecek çalışma");
            // Prepare after the edit, then simulate the user's successful Save choice.
            var task=studio.StartDxfPreparationAsync(path,1);PumpUntil(()=>task.IsCompleted);incoming=task.GetAwaiter().GetResult()!;
            var savedPath=Path.Combine(work,"source-check-save.tcad");Invoke(studio,"SaveTo",savedPath);
            Check(studio.IsDxfPreparationCurrentAfterSave(incoming) && Verify(incoming,true)
                && File.Exists(savedPath) && studio.Document.Name=="Kaydedilecek çalışma",
                "A user-saved unchanged workspace remains eligible for source verification",checks);
        }
        finally
        {
            studio.BeforeDxfSourceCheckForVerification=null;studio.AfterDxfSourceCheckForVerification=null;
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }
    private static void VerifyDxfImportCancellation(MainWindow studio,string work,List<string> checks)
    {
        var previousContext=SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(studio.Dispatcher));
        var path=Path.Combine(work,"cancel-import.dxf");
        File.WriteAllText(path,"0\nSECTION\n2\nENTITIES\n0\nLINE\n10\n0\n20\n0\n11\n100\n21\n0\n0\nENDSEC\n0\nEOF\n");
        var sourceBytes=File.ReadAllBytes(path);
        try
        {
            var piece=new PatternPiece { Name="Kaynak",Points=[new(0,0),new(10,0),new(10,10)] };
            studio.LoadDocument(new ProjectDocument { Pieces=[piece] });studio.Select(piece.Id);
            var name=(TextBox)typeof(MainWindow).GetField("name",PrivateInstance)!.GetValue(studio)!;
            name.Text="Korunacak taslak";
            var session=(DocumentSession)typeof(MainWindow).GetField("session",PrivateInstance)!.GetValue(studio)!;
            var document=JsonSerializer.Serialize(studio.Document);var selected=studio.SelectedId;
            var undo=session.UndoCount;var redo=session.RedoCount;
            var dirty=(bool)typeof(MainWindow).GetField("dirty",PrivateInstance)!.GetValue(studio)!;
            var cancel=(Button)typeof(MainWindow).GetField("dxfCancel",PrivateInstance)!.GetValue(studio)!;
            var notice=(TextBlock)typeof(MainWindow).GetField("dxfNotice",PrivateInstance)!.GetValue(studio)!;
            var review=(Button)typeof(MainWindow).GetField("dxfReview",PrivateInstance)!.GetValue(studio)!;
            foreach(var late in new[] { false,true })
            {
                using var reached=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
                Action pause=()=> { reached.Set();if(!release.Wait(TimeSpan.FromSeconds(30)))throw new TimeoutException("DXF cancellation boundary timed out."); };
                if(late)studio.AfterDxfPreparationForVerification=pause;else studio.BeforeDxfPreparationForVerification=pause;
                cancel.Content="DXF kaynak kontrolünü iptal et";AutomationProperties.SetName(cancel,"DXF kaynak kontrolünü iptal et");
                var task=studio.StartDxfPreparationAsync(path,1);
                try
                {
                    PumpUntil(()=>reached.IsSet);
                    Render(studio,Path.Combine(work,$"dxf-cancel-{late}-1024x640.png"),1024,640);
                    var root=(FrameworkElement)studio.Content;
                    Check(cancel.Visibility==Visibility.Visible && cancel.IsEnabled && FullyVisible(cancel,root),
                        $"DXF cancellation action is available at compact size ({late})",checks);
                    Check(Equals(cancel.Content,"DXF okumayı iptal et") && AutomationProperties.GetName(cancel)=="DXF okumayı iptal et",
                        $"Standard DXF preparation resets visible and accessible cancellation labels ({late})",checks);
                    var duplicate=studio.StartDxfPreparationAsync(path,1);PumpUntil(()=>duplicate.IsCompleted);
                    try { duplicate.GetAwaiter().GetResult();throw new InvalidOperationException("Concurrent DXF preparation accepted"); }
                    catch(InvalidOperationException ex) when(ex.Message.Contains("DXF okuma sürüyor")) { }
                    var canDiscard=(bool)typeof(MainWindow).GetMethod("CanDiscard",PrivateInstance,null,Type.EmptyTypes,null)!.Invoke(studio,null)!;
                    Check(!canDiscard && studio.DxfImportRunning,"DXF preparation blocks closing and duplicate preparation",checks);
                    cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Check(studio.DxfImportRunning && !cancel.IsEnabled && notice.Text.Contains("istendi"),
                        $"DXF cancellation request waits for worker ({late})",checks);
                    release.Set();PumpUntil(()=>task.IsCompleted);
                    Check(task.GetAwaiter().GetResult() is null && !studio.DxfImportRunning && cancel.Visibility==Visibility.Collapsed
                        && review.Visibility==Visibility.Collapsed && notice.Text.Contains("iptal edildi")
                        && JsonSerializer.Serialize(studio.Document)==document && studio.SelectedId==selected
                        && session.UndoCount==undo && session.RedoCount==redo && name.Text=="Korunacak taslak"
                        && (bool)typeof(MainWindow).GetField("dirty",PrivateInstance)!.GetValue(studio)! == dirty
                        && File.ReadAllBytes(path).SequenceEqual(sourceBytes),
                        $"DXF cancellation preserves source bytes and live work ({late})",checks);
                }
                finally
                {
                    release.Set();PumpUntil(()=>task.IsCompleted);
                    studio.BeforeDxfPreparationForVerification=null;studio.AfterDxfPreparationForVerification=null;
                }
            }
        }
        finally
        {
            studio.BeforeDxfPreparationForVerification=null;studio.AfterDxfPreparationForVerification=null;
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }
    private static void VerifyHpglReviewWindow(MainWindow studio,string reportPath,List<string> checks)
    {
        PatternPiece Piece(double x,bool changed)=>new() { Name="Ön\nparça",Points=changed ? [new(x,0),new(x+10,0),new(x+10,10),new(x+6,10),new(x+5,9.99),new(x+4,10),new(x,10)] : [new(x,0),new(x+10,0),new(x+10,10),new(x,10)] };
        var document=new ProjectDocument { Pieces=[Piece(0,true),Piece(20,false)] };var before=JsonSerializer.Serialize(document);
        var prepared=HpglFormat.Prepare(document,40);var review=new HpglReviewWindow(studio,prepared,document.Pieces);
        Render(review,Path.ChangeExtension(reportPath,".hpgl-review-compact.png"),580,420);
        var root=(FrameworkElement)review.Content;var rows=review.ReportsGrid.Items.Cast<object>().ToArray();
        Check(rows.Length==2 && review.ReportsGrid.IsReadOnly && prepared.TurnReviews[0].FlattenedTurns>0 && prepared.TurnReviews[1].ChangedTurns==0
            && AutomationProperties.GetName(review.ReportsGrid)=="HPGL parça dönüş raporları",
            "HPGL review displays every piece and reports detected flattened turns",checks);
        Check(review.CancelButton.IsDefault && review.CancelButton.IsCancel && !review.ExportButton.IsDefault && Equals(review.ExportButton.Content,"HPGL dosyasını oluştur")
            && FullyVisible(review.CancelButton,root) && FullyVisible(review.ExportButton,root) && review.ReportsGrid.ActualHeight>0,
            "Compact HPGL review uses cancel as default and exposes both actions and grid",checks);
        review.ReportsGrid.SelectedItem=rows[0];var first=review.SelectedDetails.Text;
        Check(review.SelectedDetails.IsReadOnly && review.SelectedDetails.Focusable && first.Contains("Ön\\u000Aparça")
            && first.Contains(document.Pieces[0].Id.ToString("D")) && first.Contains("Kaynak nokta 5") && first.Contains("düzleşen"),
            "HPGL review exposes escaped name, full identity and one-based source point",checks);
        review.ReportsGrid.SelectedItem=rows[1];var second=review.SelectedDetails.Text;
        Check(second.Contains(document.Pieces[1].Id.ToString("D")) && !second.Contains(document.Pieces[0].Id.ToString("D")),
            "Same-named pieces have distinguishable HPGL review details",checks);
        review.ChangedOnlyFilter.IsChecked=true;
        Check(review.ReportsGrid.Items.Count==1 && review.ReportsGrid.SelectedItem is null && !review.SelectedDetails.Text.Contains(document.Pieces[1].Id.ToString("D")),
            "Changed-only HPGL filter removes unchanged selected row and clears its details",checks);
        review.ReportsGrid.SelectedItem=rows[0];review.ChangedOnlyFilter.IsChecked=false;
        Check(review.ReportsGrid.Items.Count==2 && ReferenceEquals(review.ReportsGrid.SelectedItem,rows[0]) && review.SelectedDetails.Text.Contains(document.Pieces[0].Id.ToString("D")),
            "Removing HPGL filter restores every row while retaining the valid selection",checks);
        review.ChangedOnlyFilter.IsChecked=true;
        Check(review.ReportsGrid.Items.Count==1 && ReferenceEquals(review.ReportsGrid.SelectedItem,rows[0]),
            "Changed-only HPGL filter retains an eligible selection",checks);
        review.ChangedOnlyFilter.IsChecked=false;
        var previousContext=SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(studio.Dispatcher));
        try
        {
            using var reached=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
            review.BeforePieceReviewForVerification=()=> { reached.Set();release.Wait(); };
            review.MoreSamplesButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var task=review.PieceReviewTaskForVerification!;
            try
            {
                PumpUntil(()=>reached.IsSet);review.ReportsGrid.SelectedItem=rows[1];
                var otherDetails=review.SelectedDetails.Text;var otherStatus=review.PageStatus.Text;review.CancelReviewButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(review.SelectedDetails.Text==otherDetails && review.PageStatus.Text==otherStatus
                    && typeof(HpglGeometryComparison).GetField("geometry",PrivateInstance)!.GetValue(review.GeometryComparison) is null
                    && review.SampleSelector.SelectedIndex==-1 && !review.SampleSelector.IsEnabled,
                    "Stale HPGL cancellation does not overwrite the newly selected piece or show old geometry",checks);
                review.ReportsGrid.SelectedItem=rows[0];var reselectedDetails=review.SelectedDetails.Text;
                release.Set();PumpUntil(()=>task.IsCompleted);task.GetAwaiter().GetResult();
                Check(review.SelectedDetails.Text==reselectedDetails && review.SelectedDetails.Text.Contains(document.Pieces[0].Id.ToString("D")) && review.PageStatus.Text.Contains("başlatın"),
                    "Late HPGL detail result is discarded after selection changes away and back",checks);
            }
            finally { release.Set();PumpUntil(()=>task.IsCompleted);review.BeforePieceReviewForVerification=null; }
            review.ReportsGrid.SelectedItem=rows[0];review.MoreSamplesButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var refreshed=review.PieceReviewTaskForVerification!;PumpUntil(()=>refreshed.IsCompleted);refreshed.GetAwaiter().GetResult();
            Check(review.SelectedDetails.Text.Contains("Kaynak nokta 5") && review.PageStatus.Text.Contains("Örnekler: 1–") && review.PageStatus.Text.Contains("Son sayfa"),
                "Selected HPGL piece can be reviewed again from the captured output snapshot",checks);
            var displayedGeometry=(HpglPieceGeometryPreview?)typeof(HpglGeometryComparison).GetField("geometry",PrivateInstance)!.GetValue(review.GeometryComparison);
            var highlighted=(int[])typeof(HpglGeometryComparison).GetField("examples",PrivateInstance)!.GetValue(review.GeometryComparison)!;
            Check(displayedGeometry is not null && displayedGeometry.PieceId==document.Pieces[0].Id && displayedGeometry.UnitsPerMillimeter==40
                && displayedGeometry.Points[4].SourceX==5 && displayedGeometry.Points[4].SourceY==9.99
                && displayedGeometry.Points[4].PlotX==200 && displayedGeometry.Points[4].PlotY==400 && highlighted.Contains(4)
                && review.GeometryText.Text.Contains("5: kaynak (5, 9.99) mm; HPGL (5, 10) mm"),
                "V notch comparison presents the captured source and actual flattened HPGL coordinate",checks);
            Descendants<TabControl>(root).Single().SelectedIndex=1;
            Render(review,Path.ChangeExtension(reportPath,".hpgl-comparison-compact.png"),580,420);
            Check(review.GeometryComparison.ActualWidth>=150 && review.GeometryComparison.ActualHeight>=70 && FullyVisible(review.GeometryComparison,root)
                && FullyVisible(review.CancelButton,root) && FullyVisible(review.ExportButton,root) && review.GeometryText.IsReadOnly
                && AutomationProperties.GetName(review.GeometryComparison)=="Kaynak ve HPGL geometrisi karşılaştırması"
                && AutomationProperties.GetHelpText(review.GeometryComparison).Contains("fiziksel ölçüm değildir")
                && AutomationProperties.GetName(review.GeometryText)=="Kaynak ve HPGL örnek koordinatları",
                "Compact HPGL overlay and numeric text expose accessible descriptions",checks);
            var samplePage=prepared.ReviewPiecePage(document.Pieces[0].Id);
            var notchChoice=samplePage.Report.Samples.Select((sample,index)=>(sample,index)).Single(x=>x.sample.SourceVertexIndex==4).index;
            review.SampleSelector.SelectedIndex=notchChoice;
            Render(review,Path.ChangeExtension(reportPath,".hpgl-local-notch-compact.png"),580,420);
            var local=review.GeometryComparison;var k=local.LastLocalScaleForVerification;
            var sourceLocal=local.LastLocalSourceForVerification;var plotLocal=local.LastLocalPlotForVerification;
            Check(k>0 && sourceLocal.Length==3 && plotLocal.Length==3
                && Math.Abs(Math.Abs(sourceLocal[0].X-sourceLocal[1].X)-k)<1e-7
                && Math.Abs(Math.Abs(sourceLocal[1].Y-sourceLocal[0].Y)-.01*k)<1e-7
                && Math.Abs(Math.Abs(sourceLocal[1].Y-plotLocal[1].Y)-.01*k)<1e-7,
                "Selected local V notch shows the real 0.01 mm difference with one equal axis scale",checks);
            Check(FullyVisible(review.SampleSelector,root) && FullyVisible(review.ShowWholeButton,root)
                && AutomationProperties.GetName(review.SampleSelector)=="Yakından incelenecek dönüş örneği"
                && review.GeometryText.Text.Contains("Örnek kaynak nokta 5:"),
                "Compact local HPGL sample selector is visible and describes the selected point",checks);
            var initialScale=k;
            Check(local.ZoomFactorForVerification==1 && review.ZoomInButton.IsEnabled && !review.ZoomOutButton.IsEnabled
                && FullyVisible(review.ZoomInButton,root) && FullyVisible(review.ZoomOutButton,root) && FullyVisible(review.ResetZoomButton,root)
                && AutomationProperties.GetName(review.ZoomInButton)=="Seçili nokta çevresini yakınlaştır"
                && Math.Abs(sourceLocal[1].X-local.ActualWidth/2)<1e-7 && Math.Abs(sourceLocal[1].Y-local.ActualHeight/2)<1e-7,
                "Compact local zoom is accessible and centers the selected source vertex",checks);
            review.ZoomInButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Render(review,Path.ChangeExtension(reportPath,".hpgl-local-zoom-2.png"),580,420);
            sourceLocal=local.LastLocalSourceForVerification;plotLocal=local.LastLocalPlotForVerification;k=local.LastLocalScaleForVerification;
            Check(local.ZoomFactorForVerification==2 && Math.Abs(k-2*initialScale)<1e-7
                && Math.Abs(sourceLocal[1].X-local.ActualWidth/2)<1e-7 && Math.Abs(sourceLocal[1].Y-local.ActualHeight/2)<1e-7
                && Math.Abs(Math.Abs(sourceLocal[1].Y-plotLocal[1].Y)-.01*k)<1e-7,
                "Zoom doubles one common scale while retaining the true notch difference and focus",checks);
            review.ZoomInButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));review.ZoomInButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            review.ZoomInButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(local.ZoomFactorForVerification==8 && !review.ZoomInButton.IsEnabled,"Local zoom is clamped at 8x",checks);
            review.ResetZoomButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(local.ZoomFactorForVerification==1 && !review.ZoomOutButton.IsEnabled && !review.ResetZoomButton.IsEnabled,"Local zoom reset restores 1x",checks);
            review.ShowWholeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(review.SampleSelector.SelectedIndex==-1 && local.LastLocalSourceForVerification.Length==0 && local.ZoomFactorForVerification==1,
                "Returning to the whole piece clears local sample coordinates",checks);
            Descendants<TabControl>(root).Single().SelectedIndex=0;
            Render(review,Path.ChangeExtension(reportPath,".hpgl-pieces-compact.png"),580,420);
            Check(review.ReportsGrid.ActualHeight>=60 && FullyVisible(review.CancelButton,root) && FullyVisible(review.ExportButton,root)
                && review.ReportsGrid.SelectedItem is not null,
                "Compact HPGL piece tab retains usable table height, selection and actions",checks);
            var scopeViewer=Descendants<ScrollViewer>(root).Single(v=>AutomationProperties.GetName(v)=="HPGL incelemesinin kapsamı ve sınırlamaları");
            foreach(var (width,height) in new[] { (580,420),(720,540) })
            foreach(var tab in new[] { 0,1 })
            {
                Descendants<TabControl>(root).Single().SelectedIndex=tab;
                Render(review,Path.ChangeExtension(reportPath,$".hpgl-actions-{width}x{height}-tab{tab}.png"),width,height);
                Check(FullyVisible(review.CancelButton,root) && FullyVisible(review.ExportButton,root) && FullyVisible(scopeViewer,root)
                    && scopeViewer.Focusable && KeyboardNavigation.GetIsTabStop(scopeViewer) && scopeViewer.ScrollableHeight>0,
                    $"HPGL review actions and scrollable scope remain available at {width}x{height}, tab {tab}",checks);
            }
            // A 580x420 outer window leaves less client space after its frame/title bar.
            // Check the actual rendered canvas as well as the root's own layout bounds.
            foreach(var tab in new[] { 0,1,2 })
            {
                Descendants<TabControl>(root).Single().SelectedIndex=tab;
                Render(review,Path.ChangeExtension(reportPath,$".hpgl-client-564x381-tab{tab}.png"),564,381);
                Check(FullyVisibleInCanvas(review.CancelButton,root,564,381) && FullyVisibleInCanvas(review.ExportButton,root,564,381)
                    && FullyVisibleInCanvas(review.PageStatus,root,564,381)
                    && (tab==0 ? review.ReportsGrid.ActualHeight>=60 && FullyVisibleInCanvas(review.ReportsGrid,root,564,381)
                        : tab==1 ? review.GeometryComparison.ActualHeight>=70 && FullyVisibleInCanvas(review.GeometryComparison,root,564,381)
                            : FullyVisibleInCanvas(review.GeometryText,root,564,381)),
                    $"HPGL review controls remain within the smaller rendered client viewport, tab {tab}",checks);
            }
            using var lateReached=new ManualResetEventSlim();using var lateRelease=new ManualResetEventSlim();
            review.AfterPieceReviewForVerification=()=> { lateReached.Set();lateRelease.Wait(); };
            review.MoreSamplesButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var cancelling=review.PieceReviewTaskForVerification!;
            try
            {
                PumpUntil(()=>lateReached.IsSet);review.CancelReviewButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(!cancelling.IsCompleted && review.PageStatus.Text.Contains("İptal istendi"),"HPGL detail cancellation waits for the running worker",checks);
                lateRelease.Set();PumpUntil(()=>cancelling.IsCompleted);cancelling.GetAwaiter().GetResult();
                Check(review.PageStatus.Text.Contains("iptal edildi") && review.MoreSamplesButton.IsEnabled,
                    "Late cancelled HPGL detail is discarded and can be requested again",checks);
            }
            finally { lateRelease.Set();PumpUntil(()=>cancelling.IsCompleted);review.AfterPieceReviewForVerification=null; }
        }
        finally { SynchronizationContext.SetSynchronizationContext(previousContext); }
        var description=Descendants<TextBlock>(root).Single(t=>t.Text.Contains("Bu inceleme yalnız kaynak noktalarındaki"));
        Check(description.Text.Contains("yollar arası") && description.Text.Contains("sıfır bulgu") && description.Text.Contains("Bu rapor HPGL dosyasına eklenmez") && description.Text.Contains("Fiziksel makine")
            && JsonSerializer.Serialize(document)==before && !review.IsVisible,
            "HPGL review states limited scope and remains hidden without modifying source",checks);
        review.Close();
        var unchanged=new ProjectDocument { Pieces=[Piece(0,false)] };var emptyReview=new HpglReviewWindow(studio,HpglFormat.Prepare(unchanged,40),unchanged.Pieces);
        emptyReview.ChangedOnlyFilter.IsChecked=true;
        var emptyStatus=Descendants<TextBlock>((FrameworkElement)emptyReview.Content).Single(t=>AutomationProperties.GetName(t)=="HPGL rapor filtresi durumu");
        Check(emptyReview.ReportsGrid.Items.Count==0 && emptyStatus.Text.Contains("Değişen parça bulunmadı"),
            "Changed-only HPGL filter explains its empty state",checks);
        emptyReview.ChangedOnlyFilter.IsChecked=false;
        Check(emptyReview.ReportsGrid.Items.Count==1,"Clearing an empty HPGL filter restores the original rows",checks);
        var oldEmptyContext=SynchronizationContext.Current;SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(studio.Dispatcher));
        try
        {
            emptyReview.ReportsGrid.SelectedIndex=0;emptyReview.MoreSamplesButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var emptyTask=emptyReview.PieceReviewTaskForVerification!;PumpUntil(()=>emptyTask.IsCompleted);emptyTask.GetAwaiter().GetResult();
            Check(emptyReview.PageStatus.Text.Contains("Toplam değişen dönüş: 0") && !emptyReview.NextPageButton.IsEnabled && !emptyReview.PreviousPageButton.IsEnabled,
                "HPGL detail pages explain when a piece has no changed turns",checks);
            var exact=HpglFormat.Prepare(unchanged,40).GetPieceGeometry(unchanged.Pieces[0].Id);
            emptyReview.GeometryComparison.SetData(exact,[1]);emptyReview.GeometryComparison.ShowLocal(1);
            Descendants<TabControl>((FrameworkElement)emptyReview.Content).Single().SelectedIndex=1;
            Render(emptyReview,Path.ChangeExtension(reportPath,".hpgl-local-zero-difference.png"),580,420);
            var local=emptyReview.GeometryComparison;
            Check(double.IsFinite(local.LastLocalScaleForVerification) && local.LastLocalScaleForVerification>0
                && local.LastLocalSourceForVerification.SequenceEqual(local.LastLocalPlotForVerification),
                "Exact-grid local geometry renders finite overlapping source and HPGL points",checks);
        }
        finally { SynchronizationContext.SetSynchronizationContext(oldEmptyContext); }
        emptyReview.Close();
        var zigzag=new List<Point2> { new(0,0),new(60,0),new(60,10) };
        for(var x=59;x>=1;x--)zigzag.Add(new Point2(x,x%2==0?10:9.99));
        zigzag.Add(new Point2(0,10));
        var pagedDocument=new ProjectDocument { Pieces=[new PatternPiece { Name="Çok değişen",Points=zigzag }] };
        var pagedPrepared=HpglFormat.Prepare(pagedDocument,40);var pagedReview=new HpglReviewWindow(studio,pagedPrepared,pagedDocument.Pieces);
        var oldPagedContext=SynchronizationContext.Current;SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(studio.Dispatcher));
        try
        {
            pagedReview.ReportsGrid.SelectedIndex=0;pagedReview.MoreSamplesButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var pageTask=pagedReview.PieceReviewTaskForVerification!;PumpUntil(()=>pageTask.IsCompleted);pageTask.GetAwaiter().GetResult();
            Check(pagedPrepared.TurnReviews[0].ChangedTurns>40 && pagedReview.PageStatus.Text.Contains("Örnekler: 1–20 /")
                && pagedReview.NextPageButton.IsEnabled && !pagedReview.PreviousPageButton.IsEnabled,
                "HPGL detailed review displays the first bounded page and total",checks);
            pagedReview.SampleSelector.SelectedIndex=0;
            pagedReview.ZoomInButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            pagedReview.NextPageButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));pageTask=pagedReview.PieceReviewTaskForVerification!;
            Check(pagedReview.SampleSelector.SelectedIndex==-1 && !pagedReview.SampleSelector.IsEnabled && pagedReview.GeometryComparison.ZoomFactorForVerification==1,
                "Changing HPGL sample page clears the old local selection while loading",checks);
            PumpUntil(()=>pageTask.IsCompleted);pageTask.GetAwaiter().GetResult();
            Check(pagedReview.PageStatus.Text.Contains("Örnekler: 21–40 /") && pagedReview.PreviousPageButton.IsEnabled,
                "HPGL detailed review advances to the next bounded page",checks);
            pagedReview.PreviousPageButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));pageTask=pagedReview.PieceReviewTaskForVerification!;
            PumpUntil(()=>pageTask.IsCompleted);pageTask.GetAwaiter().GetResult();
            Check(pagedReview.PageStatus.Text.Contains("Örnekler: 1–20 /") && !pagedReview.PreviousPageButton.IsEnabled,
                "HPGL detailed review returns to the first page",checks);
            Render(pagedReview,Path.ChangeExtension(reportPath,".hpgl-review-paged-compact.png"),580,420);
            Check(FullyVisible(pagedReview.PageStatus,(FrameworkElement)pagedReview.Content) && pagedReview.PageStatus.Text.Contains("1–20")
                && AutomationProperties.GetName(pagedReview.PageStatus)=="HPGL örnek sayfası durumu"
                && AutomationProperties.GetLiveSetting(pagedReview.PageStatus)==AutomationLiveSetting.Polite,
                "Compact HPGL page status is visible and exposed as an accessible live region",checks);
        }
        finally { SynchronizationContext.SetSynchronizationContext(oldPagedContext);pagedReview.Close(); }
        var scaled=document.Clone();foreach(var piece in scaled.Pieces)piece.Points=piece.Points.Select(p=>p*2).ToList();
        var simulated=HpglFormat.Prepare(scaled,20);
        var queue=new SimulationQueue(Path.Combine(Path.GetDirectoryName(reportPath)!,"hpgl-review-simulator-"+Guid.NewGuid().ToString("N")));
        var machine=new MachineCenterWindow(studio,queue);
        new WindowInteropHelper(machine).EnsureHandle();
        var simulationReview=new HpglReviewWindow(machine,simulated,document.Pieces,2);
        Render(simulationReview,Path.ChangeExtension(reportPath,".hpgl-simulator-review.png"),580,420);
        var simulationRoot=(FrameworkElement)simulationReview.Content;
        var simulationDescription=Descendants<TextBlock>(simulationRoot).Single(t=>t.Text.Contains("Bu inceleme yalnız kaynak noktalarındaki"));
        Check(ReferenceEquals(simulationReview.Owner,machine) && Equals(simulationReview.ExportButton.Content,"Simülasyon dosyasını oluştur")
            && simulationDescription.Text.Contains("2 seçili parça") && simulationDescription.Text.Contains("2 ölçek çarpanı")
            && simulationDescription.Text.Contains("Fiziksel cihaza bağlantı kurulmaz") && simulationReview.ReportsGrid.Items.Count==2
            && FullyVisible(simulationReview.CancelButton,simulationRoot) && FullyVisible(simulationReview.ExportButton,simulationRoot)
            && !simulationReview.IsVisible && JsonSerializer.Serialize(document)==before,
            "Machine HPGL review identifies selected subset and scale without visible windows or source mutation",checks);
        var simulationContext=SynchronizationContext.Current;SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(studio.Dispatcher));
        try
        {
            simulationReview.ReportsGrid.SelectedIndex=0;simulationReview.MoreSamplesButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var simulationTask=simulationReview.PieceReviewTaskForVerification!;PumpUntil(()=>simulationTask.IsCompleted);simulationTask.GetAwaiter().GetResult();
            var geometry=(HpglPieceGeometryPreview?)typeof(HpglGeometryComparison).GetField("geometry",PrivateInstance)!.GetValue(simulationReview.GeometryComparison);
            Check(geometry is not null && geometry.PieceId==document.Pieces[0].Id && geometry.UnitsPerMillimeter==20
                && geometry.Points[4].SourceX==10 && geometry.Points[4].SourceY==19.98
                && geometry.Points[4].PlotX==200 && geometry.Points[4].PlotY==400
                && simulationReview.GeometryText.Text.Contains("5: kaynak (10, 19.98) mm; HPGL (10, 20) mm"),
                "Simulator comparison displays scaled source and actual quantized HPGL millimetres",checks);
            Descendants<TabControl>(simulationRoot).Single().SelectedIndex=1;
            var simulatedPage=simulated.ReviewPiecePage(document.Pieces[0].Id);
            simulationReview.SampleSelector.SelectedIndex=simulatedPage.Report.Samples.Select((sample,index)=>(sample,index)).Single(x=>x.sample.SourceVertexIndex==4).index;
            Render(simulationReview,Path.ChangeExtension(reportPath,".hpgl-local-simulation-compact.png"),580,420);
            var local=simulationReview.GeometryComparison;var k=local.LastLocalScaleForVerification;
            Check(k>0 && local.LastLocalSourceForVerification.Length==3 && local.LastLocalPlotForVerification.Length==3
                && Math.Abs(Math.Abs(local.LastLocalSourceForVerification[1].Y-local.LastLocalPlotForVerification[1].Y)-.02*k)<1e-7,
                "Scaled simulator local view shows the real 0.02 mm notch difference",checks);
        }
        finally { SynchronizationContext.SetSynchronizationContext(simulationContext); }
        simulationReview.Close();machine.Close();
        var duplicateCases=new[]
        {
            new PatternPiece { Name="Açık tekrar",Closed=false,Points=[new(0,0),new(1,.01),new(1,.01),new(2,0)] },
            new PatternPiece { Name="Kapalı tekrar",Points=[new(0,0),new(10,0),new(10,10),new(6,10),new(5,9.99),new(5,9.99),new(4,10),new(0,10),new(0,0)] }
        };
        var duplicateContext=SynchronizationContext.Current;SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(studio.Dispatcher));
        try
        {
            foreach(var piece in duplicateCases)
            {
                var duplicateDocument=new ProjectDocument { Pieces=[piece] };
                var duplicatePrepared=HpglFormat.Prepare(duplicateDocument,40);
                var duplicateReview=new HpglReviewWindow(studio,duplicatePrepared,duplicateDocument.Pieces);
                try
                {
                    duplicateReview.ReportsGrid.SelectedIndex=0;duplicateReview.MoreSamplesButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    var duplicateTask=duplicateReview.PieceReviewTaskForVerification!;PumpUntil(()=>duplicateTask.IsCompleted);duplicateTask.GetAwaiter().GetResult();
                    var text=duplicateReview.GeometryText.Text;
                    Check(piece.Closed
                        ? text.Contains("Örnek kaynak nokta 5:") && text.Contains("Önceki dönüş noktası 4:") && text.Contains("Sonraki dönüş noktası 7:")
                        : text.Contains("Örnek kaynak nokta 2:") && text.Contains("Önceki dönüş noktası 1:") && text.Contains("Sonraki dönüş noktası 4:"),
                        piece.Closed ? "Closed explicit closure skips duplicate points when describing actual HPGL turn neighbors" : "Open HPGL comparison skips duplicate points when describing actual turn neighbors",checks);
                    var wanted=piece.Closed ? 4 : 1;
                    var page=duplicatePrepared.ReviewPiecePage(piece.Id);
                    duplicateReview.SampleSelector.SelectedIndex=page.Report.Samples.Select((sample,index)=>(sample,index)).Single(x=>x.sample.SourceVertexIndex==wanted).index;
                    Descendants<TabControl>((FrameworkElement)duplicateReview.Content).Single().SelectedIndex=1;
                    Render(duplicateReview,Path.ChangeExtension(reportPath,piece.Closed?".hpgl-local-duplicate-closed.png":".hpgl-local-duplicate-open.png"),580,420);
                    var local=duplicateReview.GeometryComparison;var k=local.LastLocalScaleForVerification;var src=local.LastLocalSourceForVerification;
                    Check(k>0 && src.Length==3 && Math.Abs(Math.Abs(src[0].X-src[1].X)-k)<1e-7
                        && Math.Abs(Math.Abs(src[2].X-src[1].X)-k)<1e-7,
                        piece.Closed ? "Closed local view skips duplicate turn and explicit closure" : "Open local view skips duplicate turn",checks);
                }
                finally { duplicateReview.Close(); }
            }
        }
        finally { SynchronizationContext.SetSynchronizationContext(duplicateContext); }
        var largePiece=Piece(50_000_000,true);
        largePiece.Points=largePiece.Points.Select(p=>new Point2(p.X,p.Y-50_000_000)).ToList();
        var largeDocument=new ProjectDocument { Pieces=[largePiece] };var largePrepared=HpglFormat.Prepare(largeDocument,40);
        var largeReview=new HpglReviewWindow(studio,largePrepared,largeDocument.Pieces);
        var largeContext=SynchronizationContext.Current;SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(studio.Dispatcher));
        try
        {
            largeReview.ReportsGrid.SelectedIndex=0;largeReview.MoreSamplesButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var task=largeReview.PieceReviewTaskForVerification!;PumpUntil(()=>task.IsCompleted);task.GetAwaiter().GetResult();
            var page=largePrepared.ReviewPiecePage(largePiece.Id);
            largeReview.SampleSelector.SelectedIndex=page.Report.Samples.Select((sample,index)=>(sample,index)).Single(x=>x.sample.SourceVertexIndex==4).index;
            Descendants<TabControl>((FrameworkElement)largeReview.Content).Single().SelectedIndex=1;
            largeReview.ZoomInButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Render(largeReview,Path.ChangeExtension(reportPath,".hpgl-local-large-origin.png"),580,420);
            var local=largeReview.GeometryComparison;var scale=local.LastLocalScaleForVerification;
            var source=local.LastLocalSourceForVerification;var output=local.LastLocalPlotForVerification;
            var point=largePrepared.GetPieceGeometry(largePiece.Id).Points[4];
            var actualDifference=Math.Abs(point.SourceY-point.PlotY/40d);
            Check(scale>0 && double.IsFinite(scale) && source.Length==3 && output.Length==3 && actualDifference>0
                && source.All(p=>double.IsFinite(p.X) && double.IsFinite(p.Y))
                && Math.Abs(source[1].X-local.ActualWidth/2)<1e-7 && Math.Abs(source[1].Y-local.ActualHeight/2)<1e-7
                && Math.Abs(Math.Abs(source[1].Y-output[1].Y)-actualDifference*scale)<1e-6,
                "Large translated coordinates keep the true bounded local zoom difference",checks);
        }
        finally { largeReview.Close();SynchronizationContext.SetSynchronizationContext(largeContext); }
    }
    private static void VerifyDxfLayerNotice(MainWindow studio,List<string> checks)
    {
        static PatternPiece Piece(string name) => new() { Name=name, Points=[new(0,0),new(10,0),new(10,10)] };
        studio.LoadDocument(new ProjectDocument { Pieces=[Piece("Ön"),Piece("Arka")] });
        Check(studio.DxfLayerRenameNotice() is null,"DXF unchanged layer names do not produce a rename notice",checks);
        studio.LoadDocument(new ProjectDocument { Pieces=[Piece("a/b"),Piece("a?b"),Piece("  Yaka  ")] });
        var before=JsonSerializer.Serialize(studio.Document);
        var plan=DxfFormat.PlanLayers(studio.Document);
        var notice=studio.DxfLayerRenameNotice();
        Check(notice is not null && plan.Count==3 && plan.All(p=>p.Changed)
            && plan.All(p=>notice.Contains(p.OriginalName+" → "+p.LayerName,StringComparison.Ordinal))
            && plan.Select(p=>p.LayerName).Distinct(StringComparer.OrdinalIgnoreCase).Count()==3
            && notice.Contains("3 parça adı değişecek",StringComparison.Ordinal),
            "DXF rename notice matches exporter mapping for sanitized, colliding and trimmed names",checks);
        Check(JsonSerializer.Serialize(studio.Document)==before,"DXF rename notice does not mutate the project",checks);
    }
    private static void VerifyDxfLayerReview(MainWindow studio,string reportPath,List<string> checks)
    {
        var longPrefix=new string('A',100);
        var pieces=Enumerable.Range(0,8).Select(i=>new PatternPiece { Name=i==0 ? "Ön\nparça" : i is 1 or 2 ? longPrefix+i : $"Parça/{i}", Points=[new(0,0),new(10,0),new(10,10)] }).ToList();
        pieces[3].Name=pieces[4].Name="Aynı ad";
        studio.LoadDocument(new ProjectDocument { Pieces=pieces });
        var before=JsonSerializer.Serialize(studio.Document);
        var plan=DxfFormat.PlanLayers(studio.Document);
        var review=new DxfLayerReviewWindow(studio,plan);
        Render(review,Path.ChangeExtension(reportPath,".DxfLayerReview.compact.png"),500,380);
        var root=(FrameworkElement)review.Content;
        var rows=review.MappingGrid.Items.Cast<object>().ToArray();
        Check(rows.Length==plan.Count && rows.Length>5 && review.MappingGrid.IsReadOnly,
            "DXF layer review exposes every mapping in a read-only grid",checks);
        Check(rows.Select((row,i)=>(string?)row.GetType().GetProperty("Layer")?.GetValue(row)==plan[i].LayerName).All(x=>x)
            && ((string?)rows[0].GetType().GetProperty("Original")?.GetValue(rows[0]))?.Contains("\\u000A",StringComparison.Ordinal)==true,
            "DXF layer review matches exporter plan and escapes control characters",checks);
        Check(review.CancelButton.IsDefault && review.CancelButton.IsCancel && !review.ExportButton.IsDefault
            && FullyVisible(review.CancelButton,root) && FullyVisible(review.ExportButton,root),
            "Compact DXF layer review keeps cancel as default and both actions visible",checks);
        var original=Descendants<TextBox>(root).Single(t=>AutomationProperties.GetName(t)=="Seçili parçanın tam adı");
        var layer=Descendants<TextBox>(root).Single(t=>AutomationProperties.GetName(t)=="Seçili DXF katmanının tam adı");
        var identity=Descendants<TextBox>(root).Single(t=>AutomationProperties.GetName(t)=="Seçili parçanın kimliği");
        Check(original.IsReadOnly && layer.IsReadOnly && original.Focusable && layer.Focusable
            && original.Text=="" && layer.Text=="", "DXF layer details start empty, focusable and read-only",checks);
        review.MappingGrid.SelectedItem=rows[1];
        Check(original.Text==pieces[1].Name && layer.Text==plan[1].LayerName,
            "DXF layer details show complete first similarly prefixed name and exact layer",checks);
        review.MappingGrid.SelectedItem=rows[2];
        Check(original.Text==pieces[2].Name && layer.Text==plan[2].LayerName && original.Text!=pieces[1].Name,
            "DXF layer details update for another similarly prefixed name",checks);
        review.MappingGrid.SelectedItem=rows[0];
        Check(original.Text=="Ön\\u000Aparça" && layer.Text==plan[0].LayerName,
            "DXF layer details expose escaped control characters",checks);
        review.MappingGrid.SelectedItem=rows[3];var firstId=identity.Text;
        review.MappingGrid.SelectedItem=rows[4];
        Check(identity.IsReadOnly && firstId==pieces[3].Id.ToString("D") && identity.Text==pieces[4].Id.ToString("D") && firstId!=identity.Text,"DXF layer review distinguishes same-name pieces by full identity",checks);
        review.MappingGrid.SelectedItem=null;
        Check(original.Text=="" && layer.Text=="" && identity.Text=="", "DXF layer details clear after selection is removed",checks);
        foreach(var (width,height) in new[] { (500,380),(720,540) })
        {
            Render(review,Path.ChangeExtension(reportPath,$".DxfLayerReview.details-{width}x{height}.png"),width,height);
            Check(FullyVisible(original,root) && FullyVisible(layer,root) && FullyVisible(identity,root) && FullyVisible(review.CancelButton,root)
                && FullyVisible(review.ExportButton,root) && review.MappingGrid.ActualHeight>0,
                $"DXF layer details, grid and actions remain visible at {width}x{height}",checks);
        }
        Check(JsonSerializer.Serialize(studio.Document)==before && !review.IsVisible,
            "Review construction and rendering leave source unchanged without showing a window",checks);
    }
    private static void VerifyOutputAndNestingDisclosures(MainWindow studio,List<string> checks)
    {
        var piece=new PatternPiece { Name="Kapsam kontrolü",Points=[new(0,0),new(100,0),new(100,100),new(0,100)] };
        var project=new ProjectDocument { Pieces=[piece] };
        studio.LoadDocument(project);
        Check(studio.OutputLossNotice() is null,"Output without metadata or grading has no loss notice",checks);
        studio.Commit("Metadata",()=>studio.Document.Pieces[0].Metadata=new PieceMetadata { Role="Kesim",Quantity=2 });
        var metadataNotice=studio.OutputLossNotice();
        Check(metadataNotice is not null && metadataNotice.Contains("rolü") && metadataNotice.Contains("adet") && !metadataNotice.Contains("Serileme"),
            "Metadata-only output notice explains omitted role and quantity",checks);
        studio.Commit("Grading",()=>studio.Document.GradingRules.Add(new GradingRuleSet {
            SourcePieceId=piece.Id, SourceClosed=true, SourcePoints=[..piece.Points], Deltas=piece.Points.Select(_=>new Point2(1,1)).ToList()
        }));
        var combinedNotice=studio.OutputLossNotice();
        Check(combinedNotice is not null && combinedNotice.Contains("adet") && combinedNotice.Contains("Serileme kuralları"),
            "Combined output notice explains both metadata and grading loss",checks);
        studio.Commit("Remove metadata",()=>studio.Document.Pieces[0].Metadata=null);
        var gradingNotice=studio.OutputLossNotice();
        Check(gradingNotice is not null && gradingNotice.Contains("Serileme kuralları") && !gradingNotice.Contains("Parça rolü"),
            "Grading-only output notice explains grading loss",checks);

        studio.Select(piece.Id);
        var tools=new TextileToolsWindow(studio);var root=(FrameworkElement)tools.Content;
        root.Measure(new Size(1024,640));root.Arrange(new Rect(0,0,1024,640));root.UpdateLayout();
        Descendants<TabControl>(root).Single().SelectedIndex=2;root.UpdateLayout();
        Check(Descendants<TextBlock>(root).Any(t=>t.Text.Contains("birer kez") && t.Text.Contains("rol ve adet uygulanmaz")),
            "Nesting screen discloses one copy per piece and excludes stored role and quantity",checks);
        root.Measure(new Size(680,540));root.Arrange(new Rect(0,0,680,540));root.UpdateLayout();
        var basicWarning=Descendants<TextBlock>(root).Single(t=>t.Text.Contains("Kenar payı dört kenarın her birine uygulanır"));
        Check(basicWarning.Text.Contains("Boşluk yalnız parçalar arasındadır") && basicWarning.Text.Contains("sıfır seçilirse pay eklenmez") && FullyVisible(basicWarning,root)
            && basicWarning.ActualHeight+basicWarning.Margin.Top+basicWarning.Margin.Bottom>=basicWarning.DesiredSize.Height-.5,
            "Compact basic nesting shows full fabric edge margin warning before calculation",checks);
    }
    private static void RunBasicNestingButton(MainWindow studio,TextileToolsWindow tools,Button button)
    {
        var old=SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(studio.Dispatcher));
        try
        {
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var task=tools.BasicNestingTaskForVerification ?? throw new InvalidOperationException("Temel pastal görevi başlatılmadı.");
            PumpUntil(()=>task.IsCompleted);task.GetAwaiter().GetResult();
        }
        finally { SynchronizationContext.SetSynchronizationContext(old); }
    }
    private static void VerifyBasicNestingLengthPrecision(MainWindow studio,List<string> checks)
    {
        var originalCulture=System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            foreach(var culture in new[] { "tr-TR", "en-US" })
            {
                System.Globalization.CultureInfo.CurrentCulture=System.Globalization.CultureInfo.GetCultureInfo(culture);
                var piece=new PatternPiece { Name="Kesirli boy",Points=[new(0,0),new(5,0),new(5,10.004),new(0,10.004)] };
                studio.LoadDocument(new ProjectDocument { Pieces=[piece] });studio.Select(piece.Id);
                var tools=new TextileToolsWindow(studio);var root=(FrameworkElement)tools.Content;
                root.Measure(new Size(1024,640));root.Arrange(new Rect(0,0,1024,640));root.UpdateLayout();
                Descendants<TabControl>(root).Single().SelectedIndex=2;root.UpdateLayout();
                var arrange=Descendants<Button>(root).Single(b=>Equals(b.Content,"Tüm proje parçalarını yerleştir"));
                RunBasicNestingButton(studio,tools,arrange);
                var result=(TextBlock)typeof(TextileToolsWindow).GetField("result",PrivateInstance)!.GetValue(tools)!;
                var bounds=TekstilCAD.Core.Geometry.Bounds(studio.Document.Pieces.Single().Points);
                var prefix="Hesaplanan boy ";var start=result.Text.IndexOf(prefix,StringComparison.Ordinal);
                var end=start<0 ? -1 : result.Text.IndexOf(" mm",start+prefix.Length,StringComparison.Ordinal);
                var token=end<0 ? "" : result.Text[(start+prefix.Length)..end];
                Check(MeasurementInput.TryParseMillimeters(token,out var reported) && reported==bounds.MaxY && reported>=10.004,
                    $"Basic nesting reports fractional used length without downward rounding ({culture})",checks);
                tools.Close();
            }
        }
        finally { System.Globalization.CultureInfo.CurrentCulture=originalCulture; }
    }
    private static void VerifyNestingAndGrading(MainWindow studio,List<string> checks)
    {
        var first=new PatternPiece { Name="Ön",Points=[new(300,400),new(400,400),new(400,500),new(300,500)] };
        var second=new PatternPiece { Name="Arka",Points=[new(600,400),new(700,400),new(700,500),new(600,500)] };
        var rule=new GradingRuleSet { SourcePieceId=first.Id,SourceClosed=true,SourcePoints=[..first.Points],Deltas=Enumerable.Repeat(new Point2(1,2),4).ToList() };
        studio.LoadDocument(new ProjectDocument { Name="Pastal sınaması",Pieces=[first,second],GradingRules=[rule] });
        studio.Select(first.Id);
        var tools=new TextileToolsWindow(studio);
        var root=(FrameworkElement)tools.Content;
        root.Measure(new Size(1024,640));root.Arrange(new Rect(0,0,1024,640));root.UpdateLayout();
        var tabs=Descendants<TabControl>(root).Single();tabs.SelectedIndex=2;root.UpdateLayout();
        var before=studio.Document.Clone();
        var margin=Descendants<TextBox>(root).Single(t=>AutomationProperties.GetName(t)=="Her kenarda kumaş payı · mm");
        var arrange=Descendants<Button>(root).Single(b=>Equals(b.Content,"Tüm proje parçalarını yerleştir"));
        margin.Text="geçersiz";
        Check(!arrange.IsEnabled && DocumentSession.ContentEquals(before,studio.Document),"Basic nesting rejects invalid edge margin without changing document",checks);
        margin.Text="10";
        RunBasicNestingButton(studio,tools,arrange);
        var placed=studio.Document.Clone();
        var placedBounds=placed.Pieces.Select(p=>TekstilCAD.Core.Geometry.Bounds(p.Points)).ToArray();
        var result=(TextBlock)typeof(TextileToolsWindow).GetField("result",PrivateInstance)!.GetValue(tools)!;
        Check(placedBounds.Min(b=>b.MinX)==10 && placedBounds.Min(b=>b.MinY)==10 && placedBounds.Max(b=>b.MaxY)==110 && result.Text.Contains("120"),
            "Basic nesting applies 10 mm fabric edge margin and includes end margin in length",checks);
        Check(!DocumentSession.ContentEquals(before,placed) && placed.GradingRules.Count==1
            && placed.GradingRules[0].Deltas.SequenceEqual(rule.Deltas),"Nesting moves geometry while preserving grading increments",checks);
        var grid=(DataGrid)typeof(TextileToolsWindow).GetField("grades",PrivateInstance)!.GetValue(tools)!;
        Check(!grid.IsReadOnly && grid.Items.Cast<TextileToolsWindow.GradeRow>().All(r=>r.Dx=="1" && r.Dy=="2"),
            "Translated source keeps its grading table editable",checks);
        Invoke(studio,"Undo");
        Check(DocumentSession.ContentEquals(studio.Document,before),"Nesting undo restores original geometry and rules",checks);
        Invoke(studio,"Redo");
        Check(DocumentSession.ContentEquals(studio.Document,placed),"Nesting redo restores placement and rules",checks);
        var placedSource=studio.Document.Pieces.Single(p=>p.Id==first.Id);
        studio.Commit("Deform source",()=>placedSource.Points[1]=placedSource.Points[1] with { X=placedSource.Points[1].X+10 });
        typeof(TextileToolsWindow).GetMethod("RefreshSources",PrivateInstance)!.Invoke(tools,[first.Id,true]);
        Check(grid.IsReadOnly,"Non-translation geometry changes make grading table stale",checks);
        // Sub-tolerance deformations must be compared with the original anchor, not repeatedly rebased.
        studio.LoadDocument(before);studio.Select(first.Id);
        var driftTools=new TextileToolsWindow(studio);
        var driftGrid=(DataGrid)typeof(TextileToolsWindow).GetField("grades",PrivateInstance)!.GetValue(driftTools)!;
        var refresh=typeof(TextileToolsWindow).GetMethod("RefreshSources",PrivateInstance)!;
        for(var i=0;i<12;i++)
        {
            var current=studio.Document.Pieces.Single(p=>p.Id==first.Id);
            studio.Commit("Small deformation",()=>current.Points[1]=current.Points[1] with { X=current.Points[1].X+5e-11 });
            refresh.Invoke(driftTools,[first.Id,true]);
        }
        Check(driftGrid.IsReadOnly,"Accumulated small deformations invalidate the original grading anchor",checks);
        var tiny=new[] { new Point2(0,0),new Point2(1e-11,0),new Point2(0,1e-11) };
        var deformed=new[] { tiny[0],new Point2(2e-11,0),tiny[2] };
        Check(!TextileToolsWindow.IsTranslation(tiny,deformed),
            "A small piece whose edge doubles is not treated as a translation",checks);
        var ordinary=new[] { new Point2(0.1,0.2),new Point2(1.1,0.2),new Point2(0.1,1.2) };
        var moved=ordinary.Select(p=>new Point2(p.X+0.3,p.Y+0.4)).ToArray();
        Check(TextileToolsWindow.IsTranslation(ordinary,moved),
            "Fractional translation remains recognised despite floating-point rounding",checks);
        var cancellation=new[] { new Point2(0.4,0),new Point2(0.1,0) };
        var toZero=cancellation.Select(p=>new Point2(p.X-0.1,p.Y)).ToArray();
        Check(TextileToolsWindow.IsTranslation(cancellation,toZero),
            "Fractional translation to zero remains recognised despite cancellation",checks);
        Check(!TextileToolsWindow.IsTranslation([new Point2(double.MaxValue,0)], [new Point2(double.MaxValue,0)]),
            "Out-of-range geometry cannot be classified as a grading translation",checks);
        Check(!TextileToolsWindow.IsTranslation([new Point2(100_000_000,0),new Point2(0,0)],
            [new Point2(100_000_000,0),new Point2(1e-7,0)]),
            "A large stationary anchor cannot hide deformation near zero",checks);
    }
    private static void VerifyAsyncQuantityLayoutSave(MainWindow studio,ProductionNestingWindow window,ProjectDocument layout,int version,string sourcePath,byte[] sourceBytes,string work,List<string> checks)
    {
        var before=studio.Document.Clone();
        var status=(TextBlock)typeof(ProductionNestingWindow).GetField("status",PrivateInstance)!.GetValue(window)!;
        var save=(Button)typeof(ProductionNestingWindow).GetField("saveLayout",PrivateInstance)!.GetValue(window)!;
        var calculate=(Button)typeof(ProductionNestingWindow).GetField("calculate",PrivateInstance)!.GetValue(window)!;
        new WindowInteropHelper(window).EnsureHandle();
        foreach(var scenario in new[] { "success", "conflict", "stale" })
        {
            var path=Path.Combine(work,"async-quantity-"+scenario+".tcad");
            File.WriteAllText(path,"previous destination");
            var expected=window.CaptureLayoutDestination(path,version,layout);
            using var reached=new ManualResetEventSlim();
            using var release=new ManualResetEventSlim();
            window.BeforeLayoutSaveForVerification=()=> { reached.Set(); if(!release.Wait(TimeSpan.FromSeconds(30)))throw new TimeoutException("Layout save boundary timed out."); };
            var task=window.StartSaveLayoutAsync(path,version,layout,expected);
            try
            {
                Check(reached.Wait(TimeSpan.FromSeconds(15)),"Quantity async save reaches controlled worker boundary",checks);
                var pumped=false;studio.Dispatcher.BeginInvoke(new Action(()=>pumped=true));PumpUntil(()=>pumped);
                Check(!save.IsEnabled && !calculate.IsEnabled && !task.IsCompleted && pumped,
                    "Quantity async save keeps dispatcher responsive and disables duplicate actions",checks);
                var duplicate=window.StartSaveLayoutAsync(path,version,layout,expected);
                PumpUntil(()=>duplicate.IsCompleted);
                try { duplicate.GetAwaiter().GetResult();throw new InvalidOperationException("Duplicate save accepted."); }
                catch(InvalidOperationException ex) when(!ex.Message.Contains("accepted")) { }
                if(scenario=="success")
                {
                    var cancelled=false;System.ComponentModel.CancelEventHandler observer=(_,e)=>cancelled=e.Cancel;
                    window.Closing+=observer;window.Close();window.Closing-=observer;
                    Check(cancelled && !(bool)typeof(ProductionNestingWindow).GetField("closed",PrivateInstance)!.GetValue(window)!,
                        "Quantity window blocks closing while save is in flight",checks);
                }
                if(scenario=="conflict")File.WriteAllText(path,"external edit");
                if(scenario=="stale")studio.Document.Pieces[0].Name="Changed while saving";
                release.Set();PumpUntil(()=>task.IsCompleted);
                if(scenario=="conflict")
                {
                    try { task.GetAwaiter().GetResult();throw new InvalidOperationException("Conflict accepted."); }
                    catch(IOException) { }
                    Check(File.ReadAllText(path)=="external edit" && DocumentSession.ContentEquals(before,studio.Document),
                        "Quantity async conflict preserves destination and source",checks);
                }
                else
                {
                    task.GetAwaiter().GetResult();
                    Check(DocumentSession.ContentEquals(ProjectStorage.Load(path),layout) && File.ReadAllBytes(sourcePath).SequenceEqual(sourceBytes)
                        && (scenario!="stale" ? DocumentSession.ContentEquals(before,studio.Document) : status.Text.Contains("önceki önizlemesi")),
                        scenario=="stale" ? "Quantity async save reports earlier snapshot when source changes" : "Quantity async save writes snapshot without changing source",checks);
                }
            }
            finally
            {
                release.Set();PumpUntil(()=>task.IsCompleted);window.BeforeLayoutSaveForVerification=null;
                studio.Document.Pieces[0].Name=before.Pieces[0].Name;
            }
        }
    }
    private static void VerifyNestingHistory(MainWindow studio,string work,List<string> checks)
    {
        var saved=ProjectStorage.Load(Path.Combine(work,"quantity-layout-result.tcad"));
        var withoutHistory=saved.Clone();withoutHistory.NestingSnapshot=null;
        studio.LoadDocument(withoutHistory);
        _=new WindowInteropHelper(studio).EnsureHandle();
        var machineQueue=new SimulationQueue(Path.Combine(work,"v3-machine-disclosure-spool"));
        var ordinaryMachine=new MachineCenterWindow(studio,machineQueue);
        try
        {
            Render(ordinaryMachine,Path.Combine(work,"machine-no-nesting-warning.png"),720,540);
            var plainRoot=(FrameworkElement)ordinaryMachine.Content;
            Check(!Descendants<TextBlock>(plainRoot).Any(t=>AutomationProperties.GetName(t)=="Pastal geçmişi çıktı kapsamı uyarısı"),
                "Machine center has no nesting-loss warning when no nesting history exists",checks);
        }
        finally { ordinaryMachine.Close(); }
        studio.LoadDocument(saved);
        var historyMachine=new MachineCenterWindow(studio,machineQueue);
        try
        {
            Render(historyMachine,Path.Combine(work,"machine-nesting-warning.png"),720,540);
            var machineRoot=(FrameworkElement)historyMachine.Content;
            var warning=Descendants<TextBlock>(machineRoot).Single(t=>AutomationProperties.GetName(t)=="Pastal geçmişi çıktı kapsamı uyarısı");
            var scroll=Descendants<ScrollViewer>(machineRoot).First(s=>s.Content is StackPanel body && Descendants<TextBlock>(body).Contains(warning));
            scroll.ScrollToVerticalOffset(0);machineRoot.UpdateLayout();
            Check(warning.Text.Contains("kumaş ölçüleri") && warning.Text.Contains("kaynak–kopya") && warning.Text.Contains("aktarılmaz") &&
                warning.Text.Contains("her parça bir kez") && warning.Text.Contains("onayı değildir") &&
                warning.ActualHeight+warning.Margin.Top+warning.Margin.Bottom>=warning.DesiredSize.Height-.5 && FullyVisible(warning,machineRoot) &&
                scroll.ComputedVerticalScrollBarVisibility==Visibility.Visible,
                "Compact machine center exposes nesting metadata loss and one-copy warning within scrollable content",checks);
        }
        finally { historyMachine.Close(); }
        var notice=(TextBlock)typeof(MainWindow).GetField("nestingHistoryNotice",PrivateInstance)!.GetValue(studio)!;
        PumpUntil(()=>notice.Text.Contains("parçalarda değişiklik algılanmadı"));
        Check(notice.Visibility==Visibility.Visible && notice.Text.Contains("üretim onayı değildir") &&
            studio.Document.NestingSnapshot!.IsCurrentFor(studio.Document),
            "Reopened v3 layout shows historical match without production approval",checks);
        _=new WindowInteropHelper(studio).EnsureHandle();
        var historyWindow=studio.CreateNestingHistoryWindow();
        try
        {
            Render(historyWindow,Path.Combine(work,"nesting-history-minimum.png"),540,430);
            var root=(FrameworkElement)historyWindow.Content;
            var dimensions=Descendants<TextBox>(root).Single(t=>AutomationProperties.GetName(t)=="Kaydedilmiş geçmiş pastal ölçüleri");
            var detail=Descendants<TextBox>(root).Single(t=>AutomationProperties.GetName(t)=="Seçili geçmiş parça eşlemesi");
            var mappings=Descendants<ListBox>(root).Single(t=>AutomationProperties.GetName(t)=="Geçmişte yerleştirilen parçalar");
            var close=Descendants<Button>(root).Single(b=>Equals(b.Content,"Kapat"));
            var snapshot=studio.Document.NestingSnapshot!;
            Check(dimensions.IsReadOnly && dimensions.Focusable && detail.IsReadOnly && detail.Focusable &&
                dimensions.Text.Contains($"Kumaş eni: {MeasurementInput.Format(snapshot.FabricWidth)} mm") &&
                dimensions.Text.Contains($"Kullanılan boy: {MeasurementInput.Format(snapshot.UsedLength)} mm") &&
                dimensions.Text.Contains($"Parça boşluğu: {MeasurementInput.Format(snapshot.Gap)} mm") &&
                dimensions.Text.Contains($"Her kenar payı: {MeasurementInput.Format(snapshot.EdgeMargin)} mm") &&
                dimensions.Text.Contains(snapshot.Algorithm) && mappings.Items.Count==snapshot.Placements.Count,
                "Nesting history exposes exact saved dimensions and read-only keyboard fields",checks);
            mappings.SelectedIndex=1;
            var selected=snapshot.Placements[1];
            var rowLabel=(string)mappings.Items[1].GetType().GetProperty("Label")!.GetValue(mappings.Items[1])!;
            var currentName=studio.Document.Pieces.Single(piece=>piece.Id==selected.PlacedPieceId).Name;
            Check(rowLabel.Contains("Mevcut parça: "+currentName) && rowLabel.Contains($"Kopya: {selected.CopyNumber}") &&
                rowLabel.Contains(selected.PlacedPieceId.ToString()) && AutomationProperties.GetHelpText(mappings).Contains("doğrulanmamıştır"),
                "Nesting history rows identify current names, copies and IDs without claiming historical source names",checks);
            Check(detail.Text.Contains(selected.SourcePieceId.ToString()) && detail.Text.Contains($"Kopya: {selected.CopyNumber}") &&
                detail.Text.Contains(selected.PlacedPieceId.ToString()) && FullyVisible(mappings,root) && FullyVisible(detail,root) && FullyVisible(close,root),
                "Nesting history exposes selected identity and copy at compact size",checks);
            mappings.SelectedIndex=-1;
            Check(detail.Text=="","Nesting history clears mapping details when selection is cleared",checks);
        var outputNotice=studio.OutputLossNotice();
        Check(outputNotice is not null && outputNotice.Contains("Pastal geçmişi") && outputNotice.Contains("kaynak–kopya") &&
            outputNotice.Contains("aktarılmaz"),"Export discloses loss of historical nesting settings and mapping",checks);
        studio.Commit("Pastal parçasını değiştir",()=>studio.Document.Pieces[0].Name+=" düzenlendi");
        PumpUntil(()=>notice.Text.Contains("parçalar değişmiş"));
        Check(!studio.Document.NestingSnapshot!.IsCurrentFor(studio.Document) && notice.Text.Contains("doğrulamaz"),
            "Editing layout marks historical nesting snapshot stale",checks);
        root.UpdateLayout();
        Check(Descendants<TextBlock>(root).Any(t=>t.Text.Contains("parçalar değişmiş")),
            "Open nesting history displays stale status after layout edit",checks);
        Invoke(studio,"Undo");
        PumpUntil(()=>notice.Text.Contains("parçalarda değişiklik algılanmadı"));
        Check(studio.Document.NestingSnapshot!.IsCurrentFor(studio.Document) && notice.Text.Contains("üretim onayı değildir"),
            "Undo restores the original historical layout match",checks);
        }
        finally { historyWindow.Close(); }
        var many=saved.Clone();
        var history=many.NestingSnapshot!;
        var sourceId=Guid.NewGuid();
        history.Placements=Enumerable.Range(1,10000).Select(n=>new NestingPlacementSnapshot(sourceId,n,Guid.NewGuid())).ToList();
        many.Pieces[0].Name="Mevcut\u202Ead";
        history.Placements[0]=new NestingPlacementSnapshot(sourceId,1,many.Pieces[0].Id);
        history.LayoutFingerprint=new string('0',64); // Historical mapping need not match the edited current pieces.
        studio.LoadDocument(many);
        var manyWindow=studio.CreateNestingHistoryWindow();
        try
        {
            Render(manyWindow,Path.Combine(work,"nesting-history-many.png"),760,620);
            var root=(FrameworkElement)manyWindow.Content;
            var mappings=Descendants<ListBox>(root).Single(t=>AutomationProperties.GetName(t)=="Geçmişte yerleştirilen parçalar");
            var detail=Descendants<TextBox>(root).Single(t=>AutomationProperties.GetName(t)=="Seçili geçmiş parça eşlemesi");
            var presentLabel=(string)mappings.Items[0].GetType().GetProperty("Label")!.GetValue(mappings.Items[0])!;
            var missingLabel=(string)mappings.Items[1].GetType().GetProperty("Label")!.GetValue(mappings.Items[1])!;
            Check(presentLabel.Contains("Mevcut parça: Mevcut\\u202Ead") && !presentLabel.Contains('\u202E') &&
                missingLabel.Contains("Parça projede yok") && missingLabel.Contains("Kopya: 2") &&
                missingLabel.Contains(history.Placements[1].PlacedPieceId.ToString()),
                "Historical rows escape invisible names and identify missing placed pieces",checks);
            var realized=Enumerable.Range(0,mappings.Items.Count).Count(i=>mappings.ItemContainerGenerator.ContainerFromIndex(i) is not null);
            Check(mappings.Items.Count==10000 && realized>0 && realized<100,
                "Nesting history virtualizes 10000 historical mappings",checks);
            mappings.SelectedIndex=9999; mappings.ScrollIntoView(mappings.SelectedItem); root.UpdateLayout();
            Check(detail.Text.Contains("Kopya: 10000") && detail.Text.Contains(history.Placements[^1].PlacedPieceId.ToString()),
                "Nesting history can inspect an offscreen historical mapping",checks);
        }
        finally { manyWindow.Close(); }
    }

    private static void VerifyProductionPreview(MainWindow studio,string reportPath,string work,List<string> checks)
    {
        var old=SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(studio.Dispatcher));
        try
        {
            var piece=new PatternPiece { Name="Ön parça",Points=[new(0,0),new(100,0),new(100,100),new(0,100)], Metadata=new PieceMetadata { Role="Kesim",Quantity=2,Material="Pamuk" } };
            studio.LoadDocument(new ProjectDocument { Name="Pastal testi",Pieces=[piece] });studio.Select(piece.Id);
            var sourcePath=Path.Combine(work,"quantity-layout-source.tcad");
            Invoke(studio,"SaveTo",sourcePath);
            var sourceBytes=File.ReadAllBytes(sourcePath);
            var window=new ProductionNestingWindow(studio,studio);
            Render(window,Path.ChangeExtension(reportPath,".ProductionNestingWindow.minimum.png"),680,520);
            var minimumRoot=(FrameworkElement)window.Content;
            var marginWarning=Descendants<TextBlock>(minimumRoot).Single(t=>t.Text.Contains("Kenar payı dört kenarın her birine uygulanır"));
            var calculateButton=(Button)typeof(ProductionNestingWindow).GetField("calculate",PrivateInstance)!.GetValue(window)!;
            var saveLayoutButton=(Button)typeof(ProductionNestingWindow).GetField("saveLayout",PrivateInstance)!.GetValue(window)!;
            Check(!saveLayoutButton.IsEnabled,"Quantity layout save is disabled before calculation",checks);
            var widthInput=(TextBox)typeof(ProductionNestingWindow).GetField("width",PrivateInstance)!.GetValue(window)!;
            var gapInput=(TextBox)typeof(ProductionNestingWindow).GetField("gap",PrivateInstance)!.GetValue(window)!;
            var marginInput=(TextBox)typeof(ProductionNestingWindow).GetField("edgeMargin",PrivateInstance)!.GetValue(window)!;
            Check(marginWarning.Text.Contains("Boşluk parçalar arasındadır") && marginWarning.Text.Contains("sıfır seçilirse pay eklenmez") && FullyVisible(marginWarning,minimumRoot)
                && marginWarning.ActualHeight+marginWarning.Margin.Top+marginWarning.Margin.Bottom>=marginWarning.DesiredSize.Height-.5 && FullyVisible(calculateButton,minimumRoot)
                && FullyVisible(widthInput,minimumRoot) && FullyVisible(gapInput,minimumRoot) && FullyVisible(marginInput,minimumRoot),
                "Minimum quantity nesting window shows full margin warning, inputs and calculation action",checks);
            Render(window,Path.ChangeExtension(reportPath,".ProductionNestingWindow.compact.png"),1024,640);
            var choices=(ListBox)typeof(ProductionNestingWindow).GetField("choices",PrivateInstance)!.GetValue(window)!;
            var choiceItems=(System.Collections.IList)typeof(ProductionNestingWindow).GetField("choiceItems",PrivateInstance)!.GetValue(window)!;
            var selectedProperty=choiceItems[0]!.GetType().GetProperty("IsSelected")!;
            Check(choiceItems.Count==1 && (bool)selectedProperty.GetValue(choiceItems[0])!,"Quantity preview initially selects source",checks);
            var original=studio.Document.Clone();
            var calculate=typeof(ProductionNestingWindow).GetMethod("Calculate",PrivateInstance)!;
            var task=(Task)calculate.Invoke(window,null)!; PumpUntil(()=>task.IsCompleted);
            var status=(TextBlock)typeof(ProductionNestingWindow).GetField("status",PrivateInstance)!.GetValue(window)!;
            var mapping=(TextBlock)typeof(ProductionNestingWindow).GetField("mapping",PrivateInstance)!.GetValue(window)!;
            var preview=typeof(ProductionNestingWindow).GetField("preview",PrivateInstance)!.GetValue(window)!;
            var highlight=(ComboBox)typeof(ProductionNestingWindow).GetField("highlight",PrivateInstance)!.GetValue(window)!;
            var drawing=(ProjectDocument?)preview.GetType().GetProperty("Drawing")!.GetValue(preview);
            Check(drawing is not null && drawing.Pieces.Count==2 && highlight.Items.Count==2 && highlight.Items[0].ToString()!.Contains("kopya 1") && highlight.Items[1].ToString()!.Contains("kopya 2") && DocumentSession.ContentEquals(original,studio.Document),"Quantity preview maps two copies without mutating source",checks);
            Check(saveLayoutButton.IsEnabled && drawing is not null,"Quantity layout save is enabled only after a successful preview",checks);
            var layoutVersion=(int)typeof(ProductionNestingWindow).GetField("inputsVersion",PrivateInstance)!.GetValue(window)!;
            VerifyAsyncQuantityLayoutSave(studio,window,drawing!,layoutVersion,sourcePath,sourceBytes,work,checks);
            var layoutPath=Path.Combine(work,"quantity-layout-result.tcad");
            var expectedLayout=window.CaptureLayoutDestination(layoutPath,layoutVersion,drawing!);
            window.SaveLayoutTo(layoutPath,layoutVersion,drawing!,expectedLayout);
            var savedLayout=ProjectStorage.Load(layoutPath);
            Check(DocumentSession.ContentEquals(savedLayout,drawing!) && DocumentSession.ContentEquals(original,studio.Document)
                && File.ReadAllBytes(sourcePath).SequenceEqual(sourceBytes),
                "Saving quantity layout preserves source and writes the placed geometry and metadata",checks);
            // The confirmation must describe the actual format selected by storage,
            // including the precedence of DXF history over grainline information.
            foreach (var expectedFormat in new[] { 3, 4, 5 })
            {
                var candidate=savedLayout.Clone();
                if (expectedFormat>=4)
                    candidate.Pieces[0].Grainline=new Grainline { Start=new(10,10),End=new(20,10) };
                if (expectedFormat==5)
                    candidate.ImportProvenance=new DxfImportProvenance { SourceFileName="source.dxf",SourceSha256=new string('A',64),SourceByteLength=1,MillimetersPerUnit=1 };
                var candidatePath=Path.Combine(work,$"quantity-layout-v{expectedFormat}.tcad");
                ProjectStorage.Save(candidate,candidatePath);
                using var candidateJson=JsonDocument.Parse(File.ReadAllBytes(candidatePath));
                var confirmation=ProductionNestingWindow.FormatLayoutSaveConfirmation(candidate);
                Check(candidateJson.RootElement[0].GetInt32()==expectedFormat &&
                    confirmation.Contains($"sürüm {expectedFormat} biçimindedir") &&
                    confirmation.Contains("eski TekstilCAD sürümleri açamaz") &&
                    confirmation.Contains("makine işi değildir"),
                    $"Quantity layout v{expectedFormat} confirmation matches stored format and explains compatibility",checks);
            }
            var pastal=savedLayout.NestingSnapshot;
            using(var savedJson=JsonDocument.Parse(File.ReadAllBytes(layoutPath)))
                Check(savedJson.RootElement.ValueKind==JsonValueKind.Array && savedJson.RootElement[0].GetInt32()==3 &&
                    pastal is not null && pastal.FabricWidth==1500 && pastal.Gap==5 && pastal.EdgeMargin==0 &&
                    pastal.UsedLength==100 && pastal.Algorithm==NestingSnapshot.ShelfAlgorithm && pastal.IsCurrentFor(savedLayout) &&
                    pastal.Placements.Count==2 && pastal.Placements.All(p=>p.SourcePieceId==original.Pieces[0].Id) &&
                    pastal.Placements.Select(p=>p.CopyNumber).Order().SequenceEqual(new[]{1,2}) &&
                    pastal.Placements.Select(p=>p.PlacedPieceId).ToHashSet().SetEquals(savedLayout.Pieces.Select(p=>p.Id)),
                    "Quantity layout v3 preserves dimensions, mapping and current historical fingerprint",checks);
            var longResult=string.Concat(Enumerable.Repeat("Pastal ayrı kaydedildi; kumaş ölçüleri ve kaynak eşleşmesi tarihsel bilgi olarak saklandı. ",8));
            typeof(ProductionNestingWindow).GetMethod("SetStatus",PrivateInstance)!.Invoke(window,[longResult]);
            Render(window,Path.ChangeExtension(reportPath,".ProductionNestingWindow.save.minimum.png"),680,520);
            var statusViewer=Descendants<ScrollViewer>((DependencyObject)window.Content).Single(s=>AutomationProperties.GetName(s)=="Pastal işlem sonucu ayrıntıları");
            Check(FullyVisible(saveLayoutButton,(FrameworkElement)window.Content) && status.Text==longResult && statusViewer.Focusable
                && KeyboardNavigation.GetIsTabStop(statusViewer) && statusViewer.ScrollableHeight>0,
                "Quantity layout save action and scrollable full result remain accessible at minimum size",checks);
            var wrongPath=Path.Combine(work,"quantity-layout-wrong.svg");File.WriteAllText(wrongPath,"unchanged");
            try { window.CaptureLayoutDestination(wrongPath,layoutVersion,drawing!);throw new InvalidOperationException("Wrong extension accepted."); }
            catch(InvalidDataException) { }
            try { window.CaptureLayoutDestination(sourcePath,layoutVersion,drawing!);throw new InvalidOperationException("Source overwrite accepted."); }
            catch(InvalidOperationException ex) when(!ex.Message.Contains("accepted")) { }
            var hardLinkPath=Path.Combine(work,"quantity-layout-source-alias.tcad");
            if(!CreateHardLink(hardLinkPath,sourcePath,IntPtr.Zero)) throw new IOException("Could not create isolated hard-link verification fixture.");
            try { window.CaptureLayoutDestination(hardLinkPath,layoutVersion,drawing!);throw new InvalidOperationException("Source alias accepted."); }
            catch(InvalidOperationException ex) when(!ex.Message.Contains("accepted")) { }
            Check(File.ReadAllText(wrongPath)=="unchanged" && File.ReadAllBytes(sourcePath).SequenceEqual(sourceBytes),
                "Quantity layout rejects wrong extension, active source and its hard link without changing files",checks);
            var conflictPath=Path.Combine(work,"quantity-layout-conflict.tcad");File.WriteAllText(conflictPath,"before");
            var expectedConflict=window.CaptureLayoutDestination(conflictPath,layoutVersion,drawing!);
            File.WriteAllText(conflictPath,"external edit");
            try { window.SaveLayoutTo(conflictPath,layoutVersion,drawing!,expectedConflict);throw new InvalidOperationException("Conflict accepted."); }
            catch(IOException) { }
            Check(File.ReadAllText(conflictPath)=="external edit" && DocumentSession.ContentEquals(original,studio.Document),
                "Quantity layout destination conflict preserves external edit and source",checks);
                        highlight.SelectedIndex=1;
            var highlighted=(Guid?)preview.GetType().GetProperty("HighlightId")!.GetValue(preview);
            var selectedPlacement=(Guid)highlight.SelectedItem.GetType().GetProperty("Id")!.GetValue(highlight.SelectedItem)!;
            Check(highlighted==selectedPlacement && drawing!.Pieces.Any(p=>p.Id==highlighted),"Quantity selection highlights the corresponding placed piece ID",checks);
            var highlightedPiece=drawing!.Pieces.Single(p=>p.Id==selectedPlacement);
            var highlightedBounds=TekstilCAD.Core.Geometry.Bounds(highlightedPiece.Points);
            Check(mapping.Text.Contains("kopya 2") && mapping.Text.Contains("Kumaş solundan") && mapping.Text.Contains("üstünden")
                && mapping.Text.Contains(MeasurementInput.Format(highlightedBounds.MinX)+" mm")
                && mapping.Text.Contains(MeasurementInput.Format(highlightedBounds.MinY)+" mm")
                && mapping.Text.Contains(MeasurementInput.Format(highlightedBounds.Width)+" × "+MeasurementInput.Format(highlightedBounds.Height)+" mm")
                && mapping.Text.Contains("kesik ve kalın"),"Quantity highlight exposes selected copy, location, size and non-color guidance",checks);
            marginInput.Text="10";
            Check(preview.GetType().GetProperty("Drawing")!.GetValue(preview) is null && string.IsNullOrEmpty(mapping.Text),"Changing edge margin invalidates quantity preview",checks);
            try { window.SaveLayoutTo(layoutPath,layoutVersion,drawing!,ProjectStorage.CaptureVersion(layoutPath));throw new InvalidOperationException("Stale layout accepted."); }
            catch(InvalidOperationException ex) when(!ex.Message.Contains("accepted")) { }
            Check(!saveLayoutButton.IsEnabled && DocumentSession.ContentEquals(savedLayout,ProjectStorage.Load(layoutPath)),
                "Changed inputs disable layout save and reject the stale layout",checks);
            var marginTask=(Task)calculate.Invoke(window,null)!;PumpUntil(()=>marginTask.IsCompleted);
            var marginDrawing=(ProjectDocument?)preview.GetType().GetProperty("Drawing")!.GetValue(preview);
            var marginBounds=marginDrawing?.Pieces.Select(p=>TekstilCAD.Core.Geometry.Bounds(p.Points)).ToArray();
            Check(marginBounds is { Length:2 } && marginBounds.Min(b=>b.MinX)==10 && marginBounds.Min(b=>b.MinY)==10 && marginBounds.Max(b=>b.MaxY)==110 && status.Text.Contains("120"),
                "Quantity nesting applies 10 mm on four edges and reports full used length",checks);
            Check(status.Text.Contains("Dört kenarda 10 mm pay") && status.Text.Contains("kesik iç çerçeve")
                && AutomationProperties.GetHelpText((DependencyObject)preview).Contains("kesik iç çerçeve"),
                "Quantity preview describes the exact margin and dashed guide accessibly",checks);
            // Inspect the rendered WPF drawing commands, not just the input field or result model.
            static IEnumerable<GeometryDrawing> GeometryDrawings(Drawing? item)
            {
                if(item is GeometryDrawing geometryDrawing) yield return geometryDrawing;
                if(item is DrawingGroup group)
                    foreach(var child in group.Children)
                        foreach(var found in GeometryDrawings(child)) yield return found;
            }
            static GeometryDrawing[] Guides(FrameworkElement element)
                => GeometryDrawings(VisualTreeHelper.GetDrawing(element))
                    .Where(g=>g.Pen?.Brush is SolidColorBrush brush && brush.Color==Colors.Goldenrod).ToArray();
            var guidePreview=(FrameworkElement)Activator.CreateInstance(preview.GetType(),nonPublic:true)!;
            var setDrawing=guidePreview.GetType().GetMethod("SetDrawing")!;
            var buildGeometry=guidePreview.GetType().GetMethod("BuildGeometry",BindingFlags.Static|BindingFlags.NonPublic)!;
            setDrawing.Invoke(guidePreview,[marginDrawing!,1500d,120d,10d,buildGeometry.Invoke(null,[marginDrawing!])!]);
            guidePreview.Measure(new Size(600,400));guidePreview.Arrange(new Rect(0,0,600,400));guidePreview.UpdateLayout();
            var guideBitmap=new RenderTargetBitmap(600,400,96,96,PixelFormats.Pbgra32);guideBitmap.Render(guidePreview);
            var guides=Guides(guidePreview);
            Check(guides.Length==1 && guides[0].Geometry.Bounds==new Rect(10,10,1480,100)
                && guides[0].Pen!.DashStyle.Dashes.Count>0,
                "Positive margin renders a dashed inset guide in fabric coordinates",checks);
            setDrawing.Invoke(guidePreview,[drawing!,1500d,100d,0d,buildGeometry.Invoke(null,[drawing!])!]);
            guidePreview.InvalidateVisual();guidePreview.UpdateLayout();guideBitmap.Render(guidePreview);
            Check(Guides(guidePreview).Length==0,"Zero margin renders no inset guide",checks);
            marginInput.Text="-1";
            var invalidMargin=(Task)calculate.Invoke(window,null)!;PumpUntil(()=>invalidMargin.IsCompleted);
            Check(preview.GetType().GetProperty("Drawing")!.GetValue(preview) is null && (double)preview.GetType().GetField("edgeMargin",PrivateInstance)!.GetValue(preview)! == 0
                && !string.IsNullOrEmpty(status.Text) && DocumentSession.ContentEquals(original,studio.Document),
                "Negative edge margin cannot create quantity preview or change source",checks);
            marginInput.Text="0";
            // A detached instance avoids the real preview's MinHeight and its parent's layout constraints.
            var tiny=(FrameworkElement)Activator.CreateInstance(preview.GetType(),nonPublic:true)!;
            tiny.GetType().GetMethod("SetDrawing")!.Invoke(tiny,[drawing,1500d,100d,0d,buildGeometry.Invoke(null,[drawing!])!]);
            tiny.Measure(new Size(20,20));tiny.Arrange(new Rect(0,0,20,20));tiny.UpdateLayout();
            var tinyBitmap=new RenderTargetBitmap(20,20,96,96,PixelFormats.Pbgra32);tinyBitmap.Render(tiny);
            Check(tiny.RenderSize.Width<=24 && tiny.RenderSize.Height<=24,"Quantity preview safely renders in a tiny viewport",checks);
            Render(window,Path.ChangeExtension(reportPath,".ProductionNestingWindow.prepared.png"),1024,640);
            var width=(TextBox)typeof(ProductionNestingWindow).GetField("width",PrivateInstance)!.GetValue(window)!;
            width.Text="1600";
            Check(preview.GetType().GetProperty("Drawing")!.GetValue(preview) is null && string.IsNullOrEmpty(mapping.Text),"Quantity preview clears stale geometry on input change",checks);
            var recalculate=(Task)calculate.Invoke(window,null)!;PumpUntil(()=>recalculate.IsCompleted);
            var choice=choiceItems[0]!;
            selectedProperty.SetValue(choice,false);
            Check(preview.GetType().GetProperty("Drawing")!.GetValue(preview) is null && string.IsNullOrEmpty(mapping.Text),"Quantity preview clears stale geometry on selection change",checks);
            selectedProperty.SetValue(choice,true);
            var selectedAgain=(Task)calculate.Invoke(window,null)!;PumpUntil(()=>selectedAgain.IsCompleted);
            var gap=(TextBox)typeof(ProductionNestingWindow).GetField("gap",PrivateInstance)!.GetValue(window)!;
            gap.Text="geçersiz";
            var invalid=(Task)calculate.Invoke(window,null)!;PumpUntil(()=>invalid.IsCompleted);
            Check(preview.GetType().GetProperty("Drawing")!.GetValue(preview) is null && string.IsNullOrEmpty(mapping.Text) && status.Text.Contains("Parça boşluğu"),"Quantity preview invalid input never retains a result",checks);
            Check(AutomationProperties.GetName(width)=="Kumaş eni, milimetre" && AutomationProperties.GetName(gap)=="Parçalar arası boşluk, milimetre" && AutomationProperties.GetName((DependencyObject)preview)=="Adetli pastal geometrisi önizlemesi" && !string.IsNullOrWhiteSpace(AutomationProperties.GetHelpText((DependencyObject)preview)),"Quantity preview fields expose accessible labels and guidance",checks);
            gap.Text="5";
            var stale=(Task)calculate.Invoke(window,null)!;
            var current=studio.Document.Pieces.Single();studio.Commit("Preview stale probe",()=>current.Name="Değişmiş parça");
            PumpUntil(()=>stale.IsCompleted);
            Check(status.Text.Contains("Proje değişti") && preview.GetType().GetProperty("Drawing")!.GetValue(preview) is null,"Quantity preview discards result when project changes",checks);
            var staleBeforeWindow=new ProductionNestingWindow(studio,studio);
            var beforePiece=studio.Document.Pieces.Single();studio.Commit("Preview preflight stale probe",()=>beforePiece.Name="Yeniden değişti");
            var staleBefore=(Task)calculate.Invoke(staleBeforeWindow,null)!;PumpUntil(()=>staleBefore.IsCompleted);
            var staleBeforeStatus=(TextBlock)typeof(ProductionNestingWindow).GetField("status",PrivateInstance)!.GetValue(staleBeforeWindow)!;
            var staleBeforePreview=typeof(ProductionNestingWindow).GetField("preview",PrivateInstance)!.GetValue(staleBeforeWindow)!;
            Check(staleBeforeStatus.Text.Contains("Proje değişti") && staleBeforePreview.GetType().GetProperty("Drawing")!.GetValue(staleBeforePreview) is null,"Quantity preview rejects a project changed before calculation",checks);
            staleBeforeWindow.Close();
            var displayedWindow=new ProductionNestingWindow(studio,studio);
            var displayedTask=(Task)calculate.Invoke(displayedWindow,null)!;PumpUntil(()=>displayedTask.IsCompleted);
            var displayedPreview=typeof(ProductionNestingWindow).GetField("preview",PrivateInstance)!.GetValue(displayedWindow)!;
            var displayedStatus=(TextBlock)typeof(ProductionNestingWindow).GetField("status",PrivateInstance)!.GetValue(displayedWindow)!;
            var displayedMapping=(TextBlock)typeof(ProductionNestingWindow).GetField("mapping",PrivateInstance)!.GetValue(displayedWindow)!;
            Check(displayedPreview.GetType().GetProperty("Drawing")!.GetValue(displayedPreview) is not null,"Quantity preview has a result before project mutation",checks);
            var displayedDrawing=(ProjectDocument)displayedPreview.GetType().GetProperty("Drawing")!.GetValue(displayedPreview)!;
            var displayedVersion=(int)typeof(ProductionNestingWindow).GetField("inputsVersion",PrivateInstance)!.GetValue(displayedWindow)!;
            var stalePath=Path.Combine(work,"quantity-layout-stale.tcad");File.WriteAllText(stalePath,"stale destination");
            var staleTargetVersion=displayedWindow.CaptureLayoutDestination(stalePath,displayedVersion,displayedDrawing);
            // A no-op transaction may replace the document object while leaving content unchanged.
            studio.Commit("Preview no-op probe",()=> { });
            typeof(ProductionNestingWindow).GetMethod("CheckVisibleResultCurrent",PrivateInstance)!.Invoke(displayedWindow,null);
            Check(displayedPreview.GetType().GetProperty("Drawing")!.GetValue(displayedPreview) is not null,"Quantity preview remains visible after no-op transaction",checks);
            var displayedPiece=studio.Document.Pieces.Single();studio.Commit("Preview displayed stale probe",()=>displayedPiece.Name="Son değişiklik");
            try { displayedWindow.SaveLayoutTo(stalePath,displayedVersion,displayedDrawing,staleTargetVersion);throw new InvalidOperationException("Stale source accepted."); }
            catch(InvalidOperationException ex) when(!ex.Message.Contains("accepted")) { }
            Check(File.ReadAllText(stalePath)=="stale destination","Changed source rejects layout save before the watch timer fires",checks);
            typeof(ProductionNestingWindow).GetMethod("CheckVisibleResultCurrent",PrivateInstance)!.Invoke(displayedWindow,null);
            Check(displayedPreview.GetType().GetProperty("Drawing")!.GetValue(displayedPreview) is null && string.IsNullOrEmpty(displayedMapping.Text) && displayedStatus.Text.Contains("Proje değişti"),"Quantity preview clears an already displayed result after project mutation",checks);
            displayedWindow.Close();
            var closingWindow=new ProductionNestingWindow(studio,studio);
            var pending=(Task)calculate.Invoke(closingWindow,null)!;
            closingWindow.Close();PumpUntil(()=>pending.IsCompleted);
            var closedPreview=typeof(ProductionNestingWindow).GetField("preview",PrivateInstance)!.GetValue(closingWindow)!;
            Check(closedPreview.GetType().GetProperty("Drawing")!.GetValue(closedPreview) is null,"Quantity preview discards result after closing",checks);
            var many=Enumerable.Range(0,10000).Select(i=>new PatternPiece { Name=$"Parça {i}",Points=[new(0,0),new(10,0),new(10,10),new(0,10)],Metadata=new PieceMetadata { Role="Kesim",Quantity=1,Material="Pamuk" } }).ToList();
            studio.LoadDocument(new ProjectDocument { Name="Sanal liste kontrolü",Pieces=many });studio.Select(many[0].Id);
            var virtualWindow=new ProductionNestingWindow(studio,studio);
            Render(virtualWindow,Path.ChangeExtension(reportPath,".ProductionNestingWindow.virtual.png"),1024,640);
            var list=(ListBox)typeof(ProductionNestingWindow).GetField("choices",PrivateInstance)!.GetValue(virtualWindow)!;
            var models=(System.Collections.IList)typeof(ProductionNestingWindow).GetField("choiceItems",PrivateInstance)!.GetValue(virtualWindow)!;
            int RealizedCount()=>Enumerable.Range(0,models.Count).Count(i=>list.ItemContainerGenerator.ContainerFromIndex(i) is not null);
            var initialRealized=RealizedCount();
            Check(initialRealized>0 && initialRealized<100,$"Quantity selection realizes a bounded subset of 10000 items (initial {initialRealized})",checks);
            var selected=models[0]!.GetType().GetProperty("IsSelected")!;
            selected.SetValue(models[9999],true);
            var listScroll=Descendants<ScrollViewer>(list).First();
            listScroll.ScrollToEnd();
            ((FrameworkElement)virtualWindow.Content).UpdateLayout();
            list.Dispatcher.Invoke(()=>{},DispatcherPriority.ContextIdle);
            ((FrameworkElement)virtualWindow.Content).UpdateLayout();
            var scrolledRealized=RealizedCount();
            Check(list.ItemContainerGenerator.ContainerFromIndex(9999) is not null && scrolledRealized>0 && scrolledRealized<100,$"Quantity selection realizes offscreen target without materializing the full list (after scroll {scrolledRealized})",checks);
            list.ScrollIntoView(models[0]);((FrameworkElement)virtualWindow.Content).UpdateLayout();
            Check((bool)selected.GetValue(models[0])! && (bool)selected.GetValue(models[9999])!,"Virtualized list retains offscreen selections after scrolling",checks);
            var virtualQuery=(TextBox)typeof(ProductionNestingWindow).GetField("choiceSearch",PrivateInstance)!.GetValue(virtualWindow)!;
            virtualQuery.Text=many[9999].Id.ToString("D");
            Check(list.Items.Count==1 && (bool)selected.GetValue(models[0])! && (bool)selected.GetValue(models[9999])!,
                "Quantity search retains selected sources hidden among 10000 pieces",checks);
            var virtualTask=(Task)calculate.Invoke(virtualWindow,null)!;PumpUntil(()=>virtualTask.IsCompleted);
            var virtualPreview=typeof(ProductionNestingWindow).GetField("preview",PrivateInstance)!.GetValue(virtualWindow)!;
            var virtualDrawing=(ProjectDocument?)virtualPreview.GetType().GetProperty("Drawing")!.GetValue(virtualPreview);
            Check(virtualDrawing?.Pieces.Count==2,"Virtualized list includes offscreen selections in quantity preview",checks);
            virtualWindow.Close();
            var virtualMachineQueue=new SimulationQueue(Path.Combine(work,"virtual-machine-spool"));
            var virtualMachine=new MachineCenterWindow(studio,virtualMachineQueue);
            Render(virtualMachine,Path.ChangeExtension(reportPath,".MachineCenter.virtual.png"),1024,640);
            var machineList=(ListBox)typeof(MachineCenterWindow).GetField("choiceList",PrivateInstance)!.GetValue(virtualMachine)!;
            var machineModels=(System.Collections.IList)typeof(MachineCenterWindow).GetField("partChoices",PrivateInstance)!.GetValue(virtualMachine)!;
            int MachineRealized()=>Enumerable.Range(0,machineModels.Count).Count(i=>machineList.ItemContainerGenerator.ContainerFromIndex(i) is not null);
            var machineInitial=MachineRealized();
            Check(machineInitial>0 && machineInitial<100,$"Machine selection realizes a bounded subset of 10000 items (initial {machineInitial})",checks);
            var machineSelected=machineModels[0]!.GetType().GetProperty("IsSelected")!;
            machineSelected.SetValue(machineModels[9999],true);
            var machineScroll=Descendants<ScrollViewer>(machineList).First();
            machineScroll.ScrollToEnd();((FrameworkElement)virtualMachine.Content).UpdateLayout();
            machineList.Dispatcher.Invoke(()=>{},DispatcherPriority.ContextIdle);
            ((FrameworkElement)virtualMachine.Content).UpdateLayout();
            var machineScrolled=MachineRealized();
            Check(machineList.ItemContainerGenerator.ContainerFromIndex(9999) is not null && machineScrolled>0 && machineScrolled<100,
                $"Machine selection realizes the offscreen target without materializing all items (after scroll {machineScrolled})",checks);
            machineList.ScrollIntoView(machineModels[0]);((FrameworkElement)virtualMachine.Content).UpdateLayout();
            Check(virtualMachine.SelectedPieceIds.Count==2 && virtualMachine.SelectedPieceIds.Contains(many[0].Id)
                && virtualMachine.SelectedPieceIds.Contains(many[9999].Id),"Machine selection preserves offscreen choices",checks);
            var machineQuery=(TextBox)typeof(MachineCenterWindow).GetField("choiceSearch",PrivateInstance)!.GetValue(virtualMachine)!;
            machineQuery.Text=many[9999].Id.ToString("D");
            Check(machineList.Items.Count==1 && virtualMachine.SelectedPieceIds.Count==2,
                "Machine search retains selected sources hidden among 10000 pieces",checks);
            var virtualMachineTask=(Task)typeof(MachineCenterWindow).GetMethod("Prepare",PrivateInstance)!.Invoke(virtualMachine,null)!;
            PumpUntil(()=>virtualMachineTask.IsCompleted);virtualMachineTask.GetAwaiter().GetResult();
            Check(virtualMachineQueue.Jobs.Count==1 && virtualMachineQueue.Jobs[0].PieceCount==2
                && !virtualMachineQueue.Jobs[0].SourceSnapshotUnconfirmed,"Machine output includes selected offscreen piece",checks);
            Check(!virtualMachine.IsVisible,"Virtualized machine verification remains hidden",checks);
            virtualMachine.Close();
        }
        finally { SynchronizationContext.SetSynchronizationContext(old); }
    }
    private static void VerifyConcaveAllowance(MainWindow studio,List<string> checks)
    {
        var oldContext=SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(studio.Dispatcher));
        try
        {
        var source=new PatternPiece { Name="İçbükey örnek", Points=[new(0,0),new(100,0),new(100,40),new(40,40),new(40,100),new(0,100)] };
        studio.LoadDocument(new ProjectDocument { Name="Dikiş payı kontrolü",Pieces=[source] });studio.Select(source.Id);
        var tools=new TextileToolsWindow(studio);var root=(FrameworkElement)tools.Content;
        root.Measure(new Size(1024,640));root.Arrange(new Rect(0,0,1024,640));root.UpdateLayout();
        Descendants<TabControl>(root).Single().SelectedIndex=1;root.UpdateLayout();
        var rangeInput=(TextBox)typeof(TextileToolsWindow).GetField("offsetAllowance",PrivateInstance)!.GetValue(tools)!;
        var allowancePanel=(Panel)VisualTreeHelper.GetParent(rangeInput);
        var allowanceHint=allowancePanel.Children.Cast<UIElement>().SkipWhile(child=>!ReferenceEquals(child,rangeInput)).Skip(1).OfType<TextBlock>().First(t=>AutomationProperties.GetLiveSetting(t)==AutomationLiveSetting.Polite);
        var allowanceButton=allowancePanel.Children.OfType<Button>().Single(b=>Equals(b.Content,"Dikiş payını ayrı parça oluştur"));
        foreach(var value in new[] { "", "abc", "0", "-1", "0,0000001", "100001" })
        {
            rangeInput.Text=value;
            Check(!allowanceButton.IsEnabled && allowanceHint.Visibility==Visibility.Visible && allowanceHint.Text.Contains("0,000001 ile 100000"),
                "Invalid allowance explains its allowed range: "+value,checks);
        }
        rangeInput.Text="10";
        Check(allowanceButton.IsEnabled && allowanceHint.Visibility==Visibility.Collapsed,"Valid allowance clears its input warning",checks);
        var original=studio.Document.Pieces[0].Clone();
        Descendants<Button>(root).Single(b=>Equals(b.Content,"Dikiş payını ayrı parça oluştur"))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        WaitForOffset(tools);
        var generated=studio.Document.Pieces.SingleOrDefault(p=>p.Id!=source.Id);
        Check(generated is not null && generated.Closed && studio.Document.Pieces.Count==2
            && studio.Document.Pieces.Single(p=>p.Id==source.Id).Points.SequenceEqual(original.Points)
            && TekstilCAD.Core.Geometry.Bounds(generated.Points).Width>100 && TekstilCAD.Core.Geometry.Bounds(generated.Points).Height>100,
            "Concave seam allowance UI adds an outward piece without modifying the source",checks);
        var offsetResult=(TextBlock)typeof(TextileToolsWindow).GetField("result",PrivateInstance)!.GetValue(tools)!;
        var savedInput=(TextBox)typeof(TextileToolsWindow).GetField("offsetAllowance",PrivateInstance)!.GetValue(tools)!;
        Check(savedInput.IsEnabled && offsetResult.Text.Contains("İstenen dikiş payı: 10 mm") && offsetResult.Text.Contains("hesaplama parametresi: 10 mm") && offsetResult.Text.Contains("fiziksel ölçüm değildir") && offsetResult.Text.Contains(source.Id.ToString("D"),StringComparison.Ordinal),
            "Successful offset reports the requested amount and restores its input",checks);
        Check(offsetResult.Text.StartsWith("Dikiş payı deneyseldir; üretimden önce oluşan konturu ve ölçüleri kontrol edin.",StringComparison.Ordinal),
            "Successful offset places its experimental warning before the generated name and details",checks);
        Invoke(studio,"Undo");
        Check(studio.Document.Pieces.Count==1 && studio.Document.Pieces[0].Points.SequenceEqual(original.Points),
            "Undo removes concave seam allowance while preserving its source",checks);

        var run=typeof(TextileToolsWindow).GetMethod("CreateOffsetAsync",PrivateInstance)!;
        var first=(Task)run.Invoke(tools,["10"])!;
        var duplicate=(Task)run.Invoke(tools,["10"])!;
        Check(duplicate.IsCompleted,"A second offset request is ignored while busy",checks);
        var current=studio.Document.Pieces.Single(p=>p.Id==source.Id);
        studio.Commit("Change during offset",()=>current.Name="Changed during offset");
        PumpUntil(()=>first.IsCompleted);
        Check(studio.Document.Pieces.Count==1 && studio.Document.Pieces[0].Name=="Changed during offset",
            "Stale asynchronous offset does not change the document",checks);

        // The numeric input is valid, but exceeds the geometry operation's supported allowance.
        // This exercises a failure on the worker thread rather than an input parse failure.
        var failureBefore=studio.Document.Clone();
        var sourceSelector=(ComboBox)typeof(TextileToolsWindow).GetField("source",PrivateInstance)!.GetValue(tools)!;
        var allowanceInput=(TextBox)typeof(TextileToolsWindow).GetField("offsetAllowance",PrivateInstance)!.GetValue(tools)!;
        var status=(TextBlock)typeof(TextileToolsWindow).GetField("result",PrivateInstance)!.GetValue(tools)!;
        var offsetButton=Descendants<Button>(root).Single(b=>Equals(b.Content,"Dikiş payını ayrı parça oluştur"));
        var failure=(Task)run.Invoke(tools,["100001"])!;
        Check(!sourceSelector.IsEnabled && !allowanceInput.IsEnabled && !offsetButton.IsEnabled && status.Text.Contains("100001 mm") && status.Text.Contains("İptal isteyebilirsiniz")
            && status.Text.Contains("işlemin bitmesini bekleyin") && !status.Text.Contains("Çalışma alanını kullanmaya devam"),
            "Offset worker explains cancellation and waiting without implying an available modal owner",checks);
        PumpUntil(()=>failure.IsCompleted);
        Check(DocumentSession.ContentEquals(studio.Document,failureBefore) && sourceSelector.IsEnabled && allowanceInput.IsEnabled && offsetButton.IsEnabled
            && status.Text.StartsWith("Dikiş payı uygulanmadı",StringComparison.Ordinal) && status.Text.Contains("100001 mm") && status.Text.Contains("üretim şartları izin veriyorsa")
            && !status.Text.Contains("daha küçük bir pay",StringComparison.OrdinalIgnoreCase),
            "Failed asynchronous offset preserves document, reports error and restores controls",checks);

        var pointIndex=(TextBox)typeof(TextileToolsWindow).GetField("vertexIndexInput",PrivateInstance)!.GetValue(tools)!;
        var pointX=(TextBox)typeof(TextileToolsWindow).GetField("vertexXInput",PrivateInstance)!.GetValue(tools)!;
        var pointY=(TextBox)typeof(TextileToolsWindow).GetField("vertexYInput",PrivateInstance)!.GetValue(tools)!;
        var loadPoint=Descendants<Button>(root).Single(b=>Equals(b.Content,"Seçili noktanın koordinatlarını yükle"));
        pointIndex.Text="-1";
        Check(!loadPoint.IsEnabled,"Invalid point index disables coordinate loading",checks);
        pointIndex.Text="1";pointX.Text="999";pointY.Text="999";
        var beforeStaleLoading=studio.Document.Clone();
        loadPoint.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(pointX.Text=="999" && pointY.Text=="999" && DocumentSession.ContentEquals(studio.Document,beforeStaleLoading)
            && status.Text.Contains("Kaynak parça değişti; seçimi yenileyin.",StringComparison.Ordinal),
            "Stale point source is rejected without changing inputs or document",checks);
        // Undo replaces the document objects while the hidden workshop is still
        // alive. Refresh its source list just as a user reopening the workshop
        // would; the stale-reference guard must not load an obsolete contour.
        typeof(TextileToolsWindow).GetMethod("RefreshSources",PrivateInstance)!.Invoke(tools,[source.Id,true]);
        pointIndex.Text="1";pointX.Text="999";pointY.Text="999";
        var selectedPoint=((PatternPiece)sourceSelector.SelectedItem).Points[1];
        var beforeLoading=studio.Document.Clone();
        loadPoint.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(pointX.Text==MeasurementInput.Format(selectedPoint.X) && pointY.Text==MeasurementInput.Format(selectedPoint.Y)
            && DocumentSession.ContentEquals(studio.Document,beforeLoading) && status.Text.Contains("Parça değiştirilmedi"),
            "Point coordinate loading after a failed offset updates only the edit inputs",checks);

        // Exercise an intentionally long, actionable error at the minimum supported window size.
        status.Text="Dikiş payı uygulanmadı. "+string.Concat(Enumerable.Repeat("Kaynak konturu kontrol edin ve seçilen üretim payını doğrulatın. ",12))+"Son kontrol: kaynak parça korunmuştur.";
        root.Measure(new Size(680,540));root.Arrange(new Rect(0,0,680,540));root.UpdateLayout();
        var statusViewer=Descendants<ScrollViewer>(root).Single(v=>System.Windows.Automation.AutomationProperties.GetName(v)=="İşlem sonucu ayrıntıları");
        var closeButton=Descendants<Button>(root).Single(b=>Equals(b.Content,"Atölyeyi kapat"));
        Check(statusViewer.Focusable && KeyboardNavigation.GetIsTabStop(statusViewer)
            && System.Windows.Automation.AutomationProperties.GetLiveSetting(status)==System.Windows.Automation.AutomationLiveSetting.Polite
            && status.TextTrimming==TextTrimming.None && statusViewer.ScrollableHeight>0
            && FullyVisible(statusViewer,root) && FullyVisible(closeButton,root),
            "Compact textile result retains accessible scrollable full error and close action",checks);
        statusViewer.ScrollToEnd();root.UpdateLayout();
        Check(statusViewer.VerticalOffset>0 && Math.Abs(statusViewer.VerticalOffset-statusViewer.ScrollableHeight)<1,
            "Long textile result can be scrolled to its end",checks);

        // Keep both sides of history populated so cancellation must preserve redo too.
        studio.Commit("İptal geçmişi denemesi",()=>studio.Document.Name="Geçici ad");
        Invoke(studio,"Undo");
        var cancelSession=(DocumentSession)typeof(MainWindow).GetField("session",PrivateInstance)!.GetValue(studio)!;
        Check(cancelSession.UndoCount>0 && cancelSession.RedoCount>0,"Offset cancellation fixture has undo and redo history",checks);
        foreach(var late in new[] { false,true })
        {
            var closeTools=new TextileToolsWindow(studio);
            using var reached=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
            Action pause=()=> { reached.Set();release.Wait(); };
            if(late)closeTools.AfterOffsetBeforeReturnForVerification=pause;else closeTools.BeforeOffsetForVerification=pause;
            var beforeCancel=studio.Document.Clone();
            var selectedBefore=studio.SelectedId;var undoBefore=cancelSession.UndoCount;var redoBefore=cancelSession.RedoCount;var dirtyBefore=cancelSession.IsDirty;
            var pending=(Task)run.Invoke(closeTools,["10"])!;
            try
            {
                PumpUntil(()=>reached.IsSet);
                var pendingAgain=(Task)run.Invoke(closeTools,["10"])!;
                var cancel=(Button)typeof(TextileToolsWindow).GetField("cancelOffset",PrivateInstance)!.GetValue(closeTools)!;
                var pendingAmount=(TextBox)typeof(TextileToolsWindow).GetField("offsetAllowance",PrivateInstance)!.GetValue(closeTools)!;
                closeTools.Close();
                Check(pendingAgain.IsCompleted && !pending.IsCompleted && cancel.Visibility==Visibility.Visible && cancel.IsEnabled && !pendingAmount.IsEnabled
                    && !(bool)typeof(TextileToolsWindow).GetField("windowClosed",PrivateInstance)!.GetValue(closeTools)!,
                    $"Offset {(late?"late":"early")} work blocks duplicate start and window closure",checks);
                cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(!pending.IsCompleted && !cancel.IsEnabled && DocumentSession.ContentEquals(beforeCancel,studio.Document)
                    && studio.SelectedId==selectedBefore && cancelSession.UndoCount==undoBefore && cancelSession.RedoCount==redoBefore && cancelSession.IsDirty==dirtyBefore,
                    $"Offset {(late?"late":"early")} cancellation waits for worker and preserves source",checks);
                release.Set();PumpUntil(()=>pending.IsCompleted);pending.GetAwaiter().GetResult();
                var final=(TextBlock)typeof(TextileToolsWindow).GetField("result",PrivateInstance)!.GetValue(closeTools)!;
                Check(DocumentSession.ContentEquals(beforeCancel,studio.Document) && studio.SelectedId==selectedBefore && cancelSession.UndoCount==undoBefore
                    && cancelSession.RedoCount==redoBefore && cancelSession.IsDirty==dirtyBefore && pendingAmount.IsEnabled && cancel.Visibility==Visibility.Collapsed && final.Text.Contains("iptal edildi"),
                    $"Offset {(late?"late":"early")} cancellation discards result and restores controls",checks);
                if(late)
                {
                    closeTools.BeforeOffsetForVerification=null;closeTools.AfterOffsetBeforeReturnForVerification=null;
                    var retry=(Task)run.Invoke(closeTools,["10"])!;PumpUntil(()=>retry.IsCompleted);retry.GetAwaiter().GetResult();
                    Check(studio.Document.Pieces.Count==beforeCancel.Pieces.Count+1 && studio.Document.Pieces.Any(p=>p.Id==selectedBefore)
                        && cancelSession.UndoCount==undoBefore+1 && cancelSession.RedoCount==0 && final.Text.Contains("projeye eklendi"),
                        "Offset can be started again after cancellation and commits one undoable result",checks);
                    Invoke(studio,"Undo");
                    Check(DocumentSession.ContentEquals(beforeCancel,studio.Document),"Undo after offset retry restores the source document",checks);
                    // Undo replaces the owned document; reopen-equivalent source rebinding
                    // is required before issuing a second operation in this hidden window.
                    typeof(TextileToolsWindow).GetMethod("RefreshSources",PrivateInstance)!.Invoke(closeTools,[selectedBefore,true]);
                    var rounded=(Task)run.Invoke(closeTools,["1.0000004"])!;PumpUntil(()=>rounded.IsCompleted);rounded.GetAwaiter().GetResult();
                    Check(studio.Document.Pieces.Count==beforeCancel.Pieces.Count+1 && final.Text.Contains("İstenen dikiş payı: 1.0000004 mm")
                        && final.Text.Contains("hesaplama parametresi: 1 mm") && final.Text.Contains("fiziksel ölçüm değildir"),
                        "Offset reports requested and quantized grid amounts without claiming a physical measurement",checks);
                    Invoke(studio,"Undo");
                    Check(DocumentSession.ContentEquals(beforeCancel,studio.Document),"Undo after rounded offset restores the source document",checks);
                }
            }
            finally { release.Set();PumpUntil(()=>pending.IsCompleted);closeTools.Close(); }
        }
        var lobed=new PatternPiece { Name="Yoğun eğri örneği",Points=Enumerable.Range(0,512).Select(i=> {
            var angle=Math.Tau*i/512;var radius=1000+200*Math.Cos(3*angle);
            return new Point2(radius*Math.Cos(angle),radius*Math.Sin(angle)); }).ToList() };
        studio.LoadDocument(new ProjectDocument { Pieces=[lobed] });studio.Select(lobed.Id);
        var uncertainTools=new TextileToolsWindow(studio);var beforeUncertain=studio.Document.Clone();
        var uncertainTask=(Task)run.Invoke(uncertainTools,["1"])!;PumpUntil(()=>uncertainTask.IsCompleted);uncertainTask.GetAwaiter().GetResult();
        var uncertainStatus=(TextBlock)typeof(TextileToolsWindow).GetField("result",PrivateInstance)!.GetValue(uncertainTools)!;
        Check(DocumentSession.ContentEquals(beforeUncertain,studio.Document) && uncertainStatus.Text.Contains("korunduğu doğrulanamadı")
            && !uncertainStatus.Text.Contains("ayrıntısını değiştiriyor") && uncertainStatus.Text.Contains("üretim şartları izin veriyorsa"),
            "Uncertain smooth allowance reports unverified preservation without claiming damage or changing the source",checks);
        uncertainTools.Close();
        }
        finally { SynchronizationContext.SetSynchronizationContext(oldContext); }
    }
    private static void VerifyMainStatusAccessibility(MainWindow studio,List<string> checks)
    {
        var root=(FrameworkElement)studio.Content;
        var viewer=(ScrollViewer)typeof(MainWindow).GetField("statusViewer",PrivateInstance)!.GetValue(studio)!;
        var status=(TextBlock)typeof(MainWindow).GetField("status",PrivateInstance)!.GetValue(studio)!;
        var units=Descendants<TextBlock>(root).Single(t=>t.Text.StartsWith("MILIMETRE",StringComparison.Ordinal));
        var longMessage=string.Concat(Enumerable.Repeat("İşlem sürüyor; çalışmanızı korumak için sonucu bekleyin. ",25))+"Mesaj sonu.";
        studio.SetStatus(longMessage);
        root.Measure(new Size(1024,640));root.Arrange(new Rect(0,0,1024,640));root.UpdateLayout();
        Check(status.Text==longMessage && status.TextTrimming==TextTrimming.None && viewer.Focusable
            && KeyboardNavigation.GetIsTabStop(viewer) && AutomationProperties.GetLiveSetting(status)==AutomationLiveSetting.Polite
            && viewer.ScrollableHeight>0 && FullyVisible(viewer,root) && FullyVisible(units,root),
            "Compact main status exposes full message to keyboard scrolling and keeps units visible",checks);
        viewer.ScrollToEnd();root.UpdateLayout();
        Check(viewer.VerticalOffset>0 && Math.Abs(viewer.VerticalOffset-viewer.ScrollableHeight)<1,
            "Long main status can be scrolled to the end",checks);
        studio.SetStatus(longMessage+" Yeni durum.");root.UpdateLayout();
        Check(viewer.VerticalOffset==0 && status.Text.EndsWith("Yeni durum.",StringComparison.Ordinal),
            "New main status returns the viewer to its beginning",checks);
        studio.SetStatus("Çalışma alanı hazır · Ölçüler milimetre cinsindedir");
        Check(!studio.IsVisible,"Main status verification remains hidden",checks);
    }
    private static void WaitForOffset(TextileToolsWindow tools)
    {
        var busy=typeof(TextileToolsWindow).GetField("offsetBusy",PrivateInstance)!;
        PumpUntil(()=>!(bool)busy.GetValue(tools)!);
    }
    private static void VerifyAsyncArea(MainWindow studio,List<string> checks)
    {
        var dense=new PatternPiece { Name="Yoğun daire", Points=Enumerable.Range(0,513).Select(i=> {
            var angle=2*Math.PI*i/513; return new Point2(100*Math.Cos(angle),100*Math.Sin(angle)); }).ToList() };
        var other=new PatternPiece { Name="Açık çizgi",Closed=false,Points=[new(0,0),new(3,4)] };
        studio.LoadDocument(new ProjectDocument { Pieces=[dense,other] });
        var metrics=(TextBlock)typeof(MainWindow).GetField("metrics",PrivateInstance)!.GetValue(studio)!;
        studio.Select(dense.Id);
        PumpUntil(()=>metrics.Text.Contains("cm²"));
        Check(metrics.Text.Contains("Alan:") && metrics.Text.Contains("cm²"),"Dense valid contour area computed asynchronously",checks);
        studio.Select(other.Id); studio.Select(dense.Id); studio.Select(other.Id);
        var frame=new DispatcherFrame();
        var timer=new DispatcherTimer(DispatcherPriority.Background) { Interval=TimeSpan.FromMilliseconds(200) };
        timer.Tick+=(_,_)=>frame.Continue=false;timer.Start();try { Dispatcher.PushFrame(frame); } finally { timer.Stop(); }
        Check(metrics.Text.Contains("açık çizgide hesaplanmaz") && !metrics.Text.Contains("cm²"),"Stale dense area cannot replace another selection",checks);
        studio.Select(dense.Id);
        studio.Commit("Doğrulama için kontur değişikliği",()=> {
            var current=studio.Document.Pieces.Single(p=>p.Id==dense.Id); current.Points[0]=current.Points[1]; });
        PumpUntil(()=>metrics.Text.Contains("geçersiz konturda hesaplanamıyor"));
        Check(metrics.Text.Contains("geçersiz konturda hesaplanamıyor"),"Stale area discarded after geometry becomes invalid",checks);

        // All selection changes below run on the dispatcher without pumping it. The
        // worker continuation cannot consume the pending slot until we yield, so
        // this verifies coalescing without depending on thread timing.
        var burst=Enumerable.Range(0,3).Select(n=>new PatternPiece { Name=$"Kontur {n}",
            Points=Enumerable.Range(0,513).Select(i=> { var a=2*Math.PI*i/513;var radius=100+n*20;
                return new Point2(radius*Math.Cos(a),radius*Math.Sin(a)); }).ToList() }).ToArray();
        studio.LoadDocument(new ProjectDocument { Pieces=burst.ToList() });
        studio.Select(burst[0].Id);
        var running=typeof(MainWindow).GetField("areaWorkerRunning",PrivateInstance)!;
        var pending=typeof(MainWindow).GetField("pendingArea",PrivateInstance)!;
        Check((bool)running.GetValue(studio)!,"Dense area worker started",checks);
        for(var i=0;i<30;i++) studio.Select(burst[1+i%2].Id);
        studio.Select(burst[2].Id);
        var latest=((Point2[] Points,long Generation))pending.GetValue(studio)!;
        Check((bool)running.GetValue(studio)! && latest.Points.SequenceEqual(burst[2].Points),
            "Rapid selection retains only latest pending area request",checks);
        PumpUntil(()=>!(bool)running.GetValue(studio)!);
        Check(studio.SelectedId==burst[2].Id && metrics.Text.Contains("cm²") && pending.GetValue(studio) is null,
            "Latest selection receives completed area and pending queue drains",checks);

        var boundary=new PatternPiece { Name="4096 köşe",Points=Enumerable.Range(0,4096).Select(i=> {
            var angle=2*Math.PI*i/4096;return new Point2(100*Math.Cos(angle),100*Math.Sin(angle)); }).ToList() };
        boundary.Points.Add(boundary.Points[0]);
        studio.LoadDocument(new ProjectDocument { Pieces=[boundary] });studio.Select(boundary.Id);
        Check(metrics.Text.Contains("hesaplanıyor"),"4096 vertices plus closing point enter area calculation",checks);
        PumpUntil(()=>!(bool)running.GetValue(studio)!);
        Check(metrics.Text.Contains("cm²"),"4096 vertices plus closing point receive area",checks);
    }
    private static void VerifySelectedPieceLimits(MainWindow studio,string reportPath,List<string> checks)
    {
        static PatternPiece Piece(int count) => new() { Name=$"Sınır {count}", Closed=false,
            Points=Enumerable.Range(0,count).Select(i=>new Point2(i,0)).ToList() };
        var p2000=Piece(2000); var p2001=Piece(2001); var p4096=Piece(4096); var p4097=Piece(4097);
        studio.LoadDocument(new ProjectDocument { Name="Parça sınırları", Pieces=[p2000,p2001,p4096,p4097] });
        var warning=(TextBlock)typeof(MainWindow).GetField("pieceLimits",PrivateInstance)!.GetValue(studio)!;
        studio.Select(p2000.Id);
        Check(warning.Visibility==Visibility.Collapsed, "2000-point selected piece has no large-piece notice",checks);
        studio.Select(p2001.Id);
        Check(warning.Visibility==Visibility.Visible && warning.Text.Contains("2000") && warning.Text.Contains("HPGL") && !warning.Text.Contains("4096 noktanın üzerini desteklemez"),
            "2001-point selected piece states nesting and HPGL limits",checks);
        studio.Select(p4096.Id);
        Check(warning.Visibility==Visibility.Visible && warning.Text.Contains("2000") && !warning.Text.Contains("4096 noktanın üzerini desteklemez"),
            "4096-point selected piece does not claim the over-4096 limits",checks);
        studio.Select(p4097.Id);
        var prior=warning.Text;
        Check(warning.Visibility==Visibility.Visible && prior.Contains("4096 noktanın üzerini desteklemez") && prior.Contains("2000") && prior.Contains("HPGL") && warning.Focusable,
            "4097-point selected piece shows persistent and focusable downstream limits",checks);
        Render(studio,Path.ChangeExtension(reportPath,".selected-piece-limits-1024x640.png"),1024,640);
        Check(FullyVisible(warning,(FrameworkElement)studio.Content)
            && warning.ActualHeight+warning.Margin.Top+warning.Margin.Bottom>=warning.DesiredSize.Height-.5
            && AutomationProperties.GetName(warning)=="Seçili parça işlem sınırları",
            "4097-point limit notice is fully visible and has an accessible name at 1024x640",checks);
        studio.SetStatus("Fare konumu değişti");
        Check(warning.Visibility==Visibility.Visible && warning.Text==prior,
            "Pointer status updates do not erase selected-piece limits",checks);
        studio.Select(p2000.Id);
        Check(warning.Visibility==Visibility.Collapsed && warning.Text=="", "Selecting a smaller piece clears its previous limit notice",checks);
    }
    private static void PumpUntil(Func<bool> done)
    {
        var frame=new DispatcherFrame();
        var deadline=DateTime.UtcNow.AddSeconds(15);
        var timer=new DispatcherTimer(DispatcherPriority.Background) { Interval=TimeSpan.FromMilliseconds(10) };
        timer.Tick+=(_,_)=> { if(done() || DateTime.UtcNow>=deadline)frame.Continue=false; };
        timer.Start();
        try { if(!done())Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
        if(!done())throw new TimeoutException("Asynchronous desktop verification timed out.");
    }
    private static void ClickAndWaitForGrade(TextileToolsWindow tools,Button button)
    {
        var previous=SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(tools.Dispatcher));
        try
        {
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var task=tools.GradeTaskForVerification ?? throw new InvalidOperationException("Grading request was not started.");
            PumpUntil(()=>task.IsCompleted);task.GetAwaiter().GetResult();
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }
    private static void VerifyGradingSourceIdentification(MainWindow studio,List<string> checks)
    {
        _=new WindowInteropHelper(studio).EnsureHandle();
        var longName="Aynı\u202E ad "+new string('x',900);
        var safeLongName=longName.Replace("\u202E","\\u202E");
        var first=new PatternPiece { Name=longName,Points=[new(0,0),new(10,0),new(10,10)] };
        var second=new PatternPiece { Name=first.Name,Points=[new(0,0),new(20,0),new(20,15)] };
        studio.LoadDocument(new ProjectDocument { Pieces=[first,second] });studio.Select(first.Id);
        var tools=new TextileToolsWindow(studio);
        try
        {
            var root=(FrameworkElement)tools.Content;
            root.Measure(new Size(680,540));root.Arrange(new Rect(0,0,680,540));root.UpdateLayout();
            var selector=(ComboBox)typeof(TextileToolsWindow).GetField("source",PrivateInstance)!.GetValue(tools)!;
            var detail=(TextBox)typeof(TextileToolsWindow).GetField("sourceIdentity",PrivateInstance)!.GetValue(tools)!;
            var converterType=typeof(TextileToolsWindow).GetNestedType("SourceLabelConverter",BindingFlags.NonPublic)!;
            var converter=(System.Windows.Data.IValueConverter)Activator.CreateInstance(converterType,true)!;
            string Label(PatternPiece piece)=>(string)converter.Convert(piece,typeof(string),null!,System.Globalization.CultureInfo.CurrentCulture);
            var firstLabel=Label(first);var secondLabel=Label(second);
            Check(firstLabel.Contains(first.Id.ToString("D")) && secondLabel.Contains(second.Id.ToString("D")) &&
                firstLabel!=secondLabel && firstLabel.Contains("10 × 10 mm") && secondLabel.Contains("20 × 15 mm") &&
                firstLabel.Contains(safeLongName) && !firstLabel.Contains('\u202E'),
                "Grading source labels distinguish same names by full identity and size, escaping hidden controls",checks);
            Check(detail.IsReadOnly && detail.Focusable && detail.Text.Contains(first.Id.ToString("D")) &&
                detail.Text.Contains(safeLongName) && !detail.Text.Contains('\u202E') && FullyVisible(detail,root),
                "Compact grading selected source exposes copyable safe full name and identity",checks);
            selector.SelectedItem=studio.Document.Pieces.Single(p=>p.Id==second.Id);root.UpdateLayout();
            Check((selector.SelectedItem as PatternPiece)?.Id==second.Id && detail.Text.Contains(second.Id.ToString("D")) &&
                !detail.Text.Contains(first.Id.ToString("D")),
                "Grading source selection switches identity even for duplicate names",checks);
            selector.SelectedIndex=-1;root.UpdateLayout();
            Check(detail.Text=="","Clearing grading source clears selected identity",checks);
        }
        finally { tools.Close(); }

        var many=Enumerable.Range(0,10000).Select(i=>new PatternPiece { Name=$"Kaynak {i}",Closed=false,
            Points=[new(0,0),new(10,0)] }).ToList();
        studio.LoadDocument(new ProjectDocument { Pieces=many });studio.Select(many[0].Id);
        var large=new TextileToolsWindow(studio);
        try
        {
            var root=(FrameworkElement)large.Content;
            root.Measure(new Size(680,540));root.Arrange(new Rect(0,0,680,540));root.UpdateLayout();
            var selector=(ComboBox)typeof(TextileToolsWindow).GetField("source",PrivateInstance)!.GetValue(large)!;
            var detail=(TextBox)typeof(TextileToolsWindow).GetField("sourceIdentity",PrivateInstance)!.GetValue(large)!;
            Check(selector.Items.Count==10000 && selector.ItemsPanel.VisualTree?.Type==typeof(VirtualizingStackPanel) &&
                VirtualizingPanel.GetIsVirtualizing(selector) && ScrollViewer.GetCanContentScroll(selector),
                "Grading 10000-source dropdown configures virtualized scrolling without opening a window",checks);
            selector.SelectedItem=studio.Document.Pieces[^1];root.UpdateLayout();
            Check((selector.SelectedItem as PatternPiece)?.Id==many[^1].Id && detail.Text.Contains(many[^1].Id.ToString("D")) &&
                !large.IsVisible && !studio.IsVisible,
                "Offscreen grading source can be selected by identity while verification remains hidden",checks);
        }
        finally { large.Close(); }
    }
    private static void VerifyGradingCancellation(MainWindow studio,List<string> checks)
    {
        _=new WindowInteropHelper(studio).EnsureHandle();
        var old=SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(studio.Dispatcher));
        try
        {
            foreach(var scenario in new[] { "before-compute", "before-return" })
            {
                var source=new PatternPiece { Name="İptal kaynağı", Points=[new(0,0),new(20,0),new(20,20),new(0,20)],
                    Metadata=new PieceMetadata { Role="Kesim",Size="M",Material="Pamuk",Quantity=7 } };
                studio.LoadDocument(new ProjectDocument { Name="İptal kontrolü",Pieces=[source] });studio.Select(source.Id);
                var tools=new TextileToolsWindow(studio);
                var root=(FrameworkElement)tools.Content;
                root.Measure(new Size(680,540));root.Arrange(new Rect(0,0,680,540));root.UpdateLayout();
                var create=Descendants<Button>(root).Single(b=>Equals(b.Content,"Yeni bedeni projeye ekle"));
                var cancel=(Button)typeof(TextileToolsWindow).GetField("cancelGrade",PrivateInstance)!.GetValue(tools)!;
                var status=(TextBlock)typeof(TextileToolsWindow).GetField("result",PrivateInstance)!.GetValue(tools)!;
                var session=(DocumentSession)typeof(MainWindow).GetField("session",PrivateInstance)!.GetValue(studio)!;
                var before=studio.Document.Clone();var undo=session.UndoCount;var redo=session.RedoCount;var selected=studio.SelectedId;
                Check(cancel.Visibility==Visibility.Collapsed && !tools.GradeRunningForVerification,
                    $"Grading cancellation {scenario} is unavailable before a request",checks);
                using var reached=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
                Action barrier=()=> { reached.Set();if(!release.Wait(TimeSpan.FromSeconds(30)))throw new TimeoutException("Grading cancellation boundary timed out."); };
                if(scenario=="before-compute")tools.BeforeGradeForVerification=barrier;
                else tools.AfterGradeBeforeReturnForVerification=barrier;
                create.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var task=tools.GradeTaskForVerification ?? throw new InvalidOperationException("Grading task unavailable.");
                try
                {
                    PumpUntil(()=>reached.IsSet);
                    root.Measure(new Size(680,540));root.Arrange(new Rect(0,0,680,540));root.UpdateLayout();
                    Check(cancel.Visibility==Visibility.Visible && cancel.IsEnabled && FullyVisible(cancel,root),
                        $"Grading cancellation {scenario} action is visible during the operation",checks);
                    cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Check(tools.GradeRunningForVerification && !cancel.IsEnabled && status.Text.Contains("İptal istendi") &&
                        DocumentSession.ContentEquals(before,studio.Document) && session.UndoCount==undo && session.RedoCount==redo,
                        $"Grading cancellation {scenario} waits for worker without modifying document or history",checks);
                    tools.Close();
                    Check(!(bool)typeof(TextileToolsWindow).GetField("windowClosed",PrivateInstance)!.GetValue(tools)! &&
                        status.Text.Contains("İptal istendi",StringComparison.OrdinalIgnoreCase) && !cancel.IsEnabled,
                        $"Grading cancellation {scenario} blocks close and retains cancellation request status",checks);
                    release.Set();PumpUntil(()=>task.IsCompleted);task.GetAwaiter().GetResult();
                    Check(!tools.GradeRunningForVerification && cancel.Visibility==Visibility.Collapsed && status.Text.Contains("iptal edildi") &&
                        DocumentSession.ContentEquals(before,studio.Document) && studio.SelectedId==selected && session.UndoCount==undo && session.RedoCount==redo &&
                        studio.Document.Pieces.Single().Metadata is {Role:"Kesim",Size:"M",Material:"Pamuk",Quantity:7},
                        $"Grading cancellation {scenario} completes without partial result and preserves metadata",checks);
                }
                finally
                {
                    release.Set();PumpUntil(()=>task.IsCompleted);
                    tools.BeforeGradeForVerification=null;tools.AfterGradeBeforeReturnForVerification=null;
                    tools.Close();
                }
            }
        }
        finally { SynchronizationContext.SetSynchronizationContext(old); }
    }
    private static void VerifyAsyncGrading(MainWindow studio,List<string> checks)
    {
        _=new WindowInteropHelper(studio).EnsureHandle();
        var old=SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(studio.Dispatcher));
        try
        {
            foreach(var scenario in new[] { "success", "document", "inputs", "pending", "close" })
            {
                var source=new PatternPiece { Name="Beden kaynağı", Points=[new(0,0),new(20,0),new(20,20),new(0,20)],
                    Metadata=new PieceMetadata { Role="Kesim",Size="M",Material="Pamuk",Quantity=7 } };
                studio.LoadDocument(new ProjectDocument { Name="Asenkron beden",Pieces=[source] });studio.Select(source.Id);
                var tools=new TextileToolsWindow(studio);
                var root=(FrameworkElement)tools.Content;
                root.Measure(new Size(1024,640));root.Arrange(new Rect(0,0,1024,640));root.UpdateLayout();
                var create=Descendants<Button>(root).Single(b=>Equals(b.Content,"Yeni bedeni projeye ekle"));
                var selector=(ComboBox)typeof(TextileToolsWindow).GetField("source",PrivateInstance)!.GetValue(tools)!;
                var table=(DataGrid)typeof(TextileToolsWindow).GetField("grades",PrivateInstance)!.GetValue(tools)!;
                var status=(TextBlock)typeof(TextileToolsWindow).GetField("result",PrivateInstance)!.GetValue(tools)!;
                var size=Descendants<TextBox>(root).Single(t=>AutomationProperties.GetName(t)=="Yeni beden adı");
                        var options=Descendants<Expander>(root).Single(e=>Equals(e.Header,"Yeni parça bilgileri · isteğe bağlı"));
                options.IsExpanded=true;root.UpdateLayout();
                var copy=Descendants<CheckBox>(root).Single(c=>AutomationProperties.GetName(c)=="Kaynağın rol ve malzemesini kopyala");
                copy.IsChecked=true;
                var before=studio.Document.Clone();
                using var reached=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
                tools.BeforeGradeForVerification=()=> { reached.Set();if(!release.Wait(TimeSpan.FromSeconds(30)))throw new TimeoutException("Grading boundary timed out."); };
                create.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var task=tools.GradeTaskForVerification ?? throw new InvalidOperationException("Grading task unavailable.");
                try
                {
                    PumpUntil(()=>reached.IsSet);
                    var pumped=false;studio.Dispatcher.BeginInvoke(new Action(()=>pumped=true));PumpUntil(()=>pumped);
                    Check(tools.GradeRunningForVerification && !create.IsEnabled && !selector.IsEnabled && !table.IsEnabled && pumped,
                        $"Async grading {scenario} keeps dispatcher responsive and prevents duplicate work",checks);
                    create.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Check(ReferenceEquals(task,tools.GradeTaskForVerification),$"Async grading {scenario} ignores duplicate request",checks);
                    if(scenario=="document") studio.Commit("Kaynak değişti",()=>studio.Document.Pieces[0].Name="Değişmiş kaynak");
                    else if(scenario=="inputs") size.Text="XL";
                    else if(scenario=="pending")
                    {
                        studio.SetTool("Çizgi");
                        var canvas=(CadCanvas)typeof(MainWindow).GetField("canvas",PrivateInstance)!.GetValue(studio)!;
                        canvas.RestorePendingDrawing(new RecoveryPendingDrawing("Çizgi",[new Point2(1,2)]));
                    }
                    else if(scenario=="close")
                    {
                        tools.Close();
                        Check(!(bool)typeof(TextileToolsWindow).GetField("windowClosed",PrivateInstance)!.GetValue(tools)!
                            && status.Text.Contains("bekleyin"),"Closing workshop is blocked during grading",checks);
                    }
                    release.Set();PumpUntil(()=>task.IsCompleted);task.GetAwaiter().GetResult();
                    if(scenario is "success" or "close")
                    {
                        var added=studio.Selected;
                        Check(studio.Document.Pieces.Count==2 && added is not null && added.Metadata is { Role:"Kesim",Material:"Pamuk",Quantity:null }
                            && studio.Document.Pieces.Single(p=>p.Id==source.Id).Metadata!.Quantity==7,
                            "Async grading commits detached result and never copies quantity",checks);
                        Invoke(studio,"Undo");
                        Check(DocumentSession.ContentEquals(before,studio.Document),"One undo removes async grading result",checks);
                        Invoke(studio,"Redo");
                        Check(studio.Document.Pieces.Count==2 && studio.Selected?.Metadata?.Quantity is null,"Redo restores async grading result",checks);
                    }
                    else if(scenario=="document")
                        Check(studio.Document.Pieces.Count==1 && studio.Document.Pieces[0].Name=="Değişmiş kaynak" && status.Text.Contains("değişti"),
                            "Async grading rejects a changed source without adding geometry",checks);
                    else if(scenario is "inputs" or "pending")
                        Check(DocumentSession.ContentEquals(before,studio.Document) && status.Text.Contains("değişti"),
                            $"Async grading rejects changed {scenario} without modifying document",checks);
                    Check(!tools.GradeRunningForVerification,$"Async grading {scenario} releases busy state",checks);
                }
                finally
                {
                    release.Set();PumpUntil(()=>task.IsCompleted);tools.BeforeGradeForVerification=null;
                    if(scenario=="pending") ((CadCanvas)typeof(MainWindow).GetField("canvas",PrivateInstance)!.GetValue(studio)!).CancelDrawing();
                    tools.Close();
                }
            }
        }
        finally { SynchronizationContext.SetSynchronizationContext(old); }
    }
    private static void VerifyCompactGradingViewport(DataGrid grid,FrameworkElement content,List<string> checks)
    {
        // Run after the 1024 x 640 render. Do not ScrollIntoView: this checks what an operator sees on arrival.
        var gridBounds=BoundsInRoot(grid,content);
        var visibleGrid=VisibleBoundsInRoot(grid,content);
        Check(grid.ActualHeight>=150 && visibleGrid.Height>=150,
            $"Compact grading grid has at least 150 visible pixels (actual {grid.ActualHeight:0.#}, visible {visibleGrid.Height:0.#})",checks);
        var headers=Descendants<DataGridColumnHeader>(grid).Where(h=>h.Column is not null && h.Column.Visibility==Visibility.Visible).ToArray();
        Check(headers.Length==grid.Columns.Count && headers.All(h=>FullyVisible(h,content)),
            "Compact grading column headers are inside the clipped content viewport",checks);
        var rows=Enumerable.Range(0,3).Select(i=>grid.ItemContainerGenerator.ContainerFromIndex(i) as DataGridRow).ToArray();
        Check(rows.All(row=>row is not null && RowVisible(row,content,gridBounds.Width)),
            "Compact grading first three rows are fully readable inside the clipped content viewport",checks);
    }
    private static void VerifyOptionalGradingMetadata(MainWindow studio,List<string> checks)
    {
        var source=new PatternPiece { Name="Kaynak",Points=[new(0,0),new(10,0),new(10,10),new(0,10)],Metadata=new PieceMetadata { Role="Kesim",Size="M",Material="Pamuk",Quantity=8 } };
        var missing=new PatternPiece { Name="Bilgisiz kaynak",Points=[new(20,0),new(30,0),new(30,10),new(20,10)] };
        studio.LoadDocument(new ProjectDocument { Pieces=[source,missing] });studio.Select(source.Id);
        _=new WindowInteropHelper(studio).EnsureHandle();
        var tools=new TextileToolsWindow(studio);var root=(FrameworkElement)tools.Content;
        root.Measure(new Size(680,540));root.Arrange(new Rect(0,0,680,540));root.UpdateLayout();
        var gradeHelp=Descendants<Expander>(root).Single(e=>Equals(e.Header,"Artışları mm olarak girin · Projeye uygulayıp kaydedin"));
        var pointPickerButton=Descendants<Button>(root).Single(b=>Equals(b.Content,"Konturdan nokta seç…"));
        gradeHelp.IsExpanded=true;root.Measure(new Size(680,540));root.Arrange(new Rect(0,0,680,540));root.UpdateLayout();
        var helpText=(TextBlock)gradeHelp.Content;
        Check(FullyVisible(gradeHelp,root) && FullyVisible(helpText,root) && FullyVisible(pointPickerButton,root) &&
              pointPickerButton.Focusable && helpText.ActualWidth<=gradeHelp.ActualWidth,
            $"Expanded grading help and point picker remain visible and reachable at minimum window size: help={FullyVisible(gradeHelp,root)} text={FullyVisible(helpText,root)} button={FullyVisible(pointPickerButton,root)} widths={helpText.ActualWidth}/{gradeHelp.ActualWidth} bounds={BoundsInRoot(gradeHelp,root)} textBounds={BoundsInRoot(helpText,root)} buttonBounds={BoundsInRoot(pointPickerButton,root)}",checks);
        gradeHelp.IsExpanded=false;root.UpdateLayout();        var options=Descendants<Expander>(root).Single(e=>Equals(e.Header,"Yeni parça bilgileri · isteğe bağlı"));
        Check(!options.IsExpanded && FullyVisible(options,root),"Grading optional metadata is collapsed and reachable at compact size",checks);
        options.IsExpanded=true;root.UpdateLayout();
        var sizeOption=Descendants<CheckBox>(root).Single(c=>AutomationProperties.GetName(c)=="Yeni beden adını beden bilgisi olarak ekle");
        var copyOption=Descendants<CheckBox>(root).Single(c=>AutomationProperties.GetName(c)=="Kaynağın rol ve malzemesini kopyala");
        Check(sizeOption.IsChecked==false && copyOption.IsChecked==false && sizeOption.Focusable && copyOption.Focusable,
            "Grading metadata options start independently off and keyboard focusable",checks);
        var button=Descendants<Button>(root).Single(b=>Equals(b.Content,"Yeni bedeni projeye ekle"));
        root.Measure(new Size(680,540));root.Arrange(new Rect(0,0,680,540));root.UpdateLayout();
        var lowerScroll=Descendants<ScrollViewer>(root).Single(v=>AutomationProperties.GetName(v)=="Serileme seçenekleri ve işlemleri");
        lowerScroll.ScrollToTop();lowerScroll.UpdateLayout();
        var sizeVisible=FullyVisible(sizeOption,root);var copyVisible=FullyVisible(copyOption,root);
        var optionsVisible=sizeVisible && copyVisible;
        lowerScroll.ScrollToEnd();lowerScroll.UpdateLayout();
        var createVisible=FullyVisible(button,root);
        var rebaseButton=Descendants<Button>(root).Single(b=>Equals(b.Content,"Taşınan kaynakla taslağı eşleştir"));
        var rebaseVisible=FullyVisible(rebaseButton,root);
        Check(optionsVisible && createVisible && rebaseVisible && rebaseButton.Focusable && lowerScroll.Focusable && KeyboardNavigation.GetIsTabStop(lowerScroll),
            $"Expanded grading metadata controls: size={sizeVisible}, copy={copyVisible}, create={createVisible}, rebase={rebaseVisible}, offset={lowerScroll.VerticalOffset}, scrollable={lowerScroll.ScrollableHeight}, bounds={BoundsInRoot(lowerScroll,root)}, button={BoundsInRoot(button,root)}, buttonVisible={VisibleBoundsInRoot(button,root)}, rebaseBounds={BoundsInRoot(rebaseButton,root)}, rebaseVisibleBounds={VisibleBoundsInRoot(rebaseButton,root)}",checks);
        var size=Descendants<TextBox>(root).Single(t=>AutomationProperties.GetName(t)=="Yeni beden adı");
        var result=(TextBlock)typeof(TextileToolsWindow).GetField("result",PrivateInstance)!.GetValue(tools)!;
        var selector=(ComboBox)typeof(TextileToolsWindow).GetField("source",PrivateInstance)!.GetValue(tools)!;
        size.Text="L";sizeOption.IsChecked=true;copyOption.IsChecked=true;
        var original=studio.Document.Pieces.Single(p=>p.Id==source.Id).Clone();
        ClickAndWaitForGrade(tools,button);
        var created=studio.Selected!;
        Check(created.Metadata is { Size:"L",Role:"Kesim",Material:"Pamuk",Quantity:null }
            && studio.Document.Pieces.Single(p=>p.Id==source.Id).Metadata!.Quantity==8
            && studio.Document.Pieces.Single(p=>p.Id==source.Id).Points.SequenceEqual(original.Points)
            && result.Text.Contains("Adet aktarılmadı"),
            "Optional grading metadata copies selected values but never quantity or changes source",checks);
        var createdId=created.Id;Invoke(studio,"Undo");
        Check(studio.Document.Pieces.All(p=>p.Id!=createdId),"One undo removes generated geometry and optional metadata",checks);
        Invoke(studio,"Redo");
        Check(studio.Document.Pieces.Single(p=>p.Id==createdId).Metadata is { Size:"L",Role:"Kesim",Material:"Pamuk",Quantity:null },
            "Redo restores generated geometry and optional metadata without quantity",checks);
        var afterRedo=studio.Document.Clone();
        size.Text="XL";sizeOption.IsChecked=true;copyOption.IsChecked=false;ClickAndWaitForGrade(tools,button);
        Check(DocumentSession.ContentEquals(afterRedo,studio.Document) && result.Text.Contains("Kaynak listesi güncel değil"),
            "Grading rejects stale source entries after external undo and redo",checks);
        typeof(TextileToolsWindow).GetMethod("RefreshSources",PrivateInstance)!.Invoke(tools,[source.Id,true]);
        size.Text="XL";sizeOption.IsChecked=true;copyOption.IsChecked=false;ClickAndWaitForGrade(tools,button);
        Check(studio.Selected!.Metadata is { Size:"XL",Role:null,Material:null,Quantity:null },
            "Grading can add size alone without copying source production fields",checks);
        // Source choices are reset when the user explicitly changes the base piece.
        selector.SelectedItem=selector.Items.Cast<PatternPiece>().Single(p=>p.Id==missing.Id);
        Check(sizeOption.IsChecked==false && copyOption.IsChecked==false,"Changing grading source resets optional metadata choices",checks);
        copyOption.IsChecked=true;size.Text="XXL";ClickAndWaitForGrade(tools,button);
        Check(studio.Selected!.Metadata is null && result.Text.Contains("rol kaynakta yoktu") && result.Text.Contains("malzeme kaynakta yoktu")
            && result.Text.Contains("Adet aktarılmadı"),"Missing source metadata is reported without invented values",checks);
        Check(!tools.IsVisible,"Optional grading metadata verification remains hidden",checks);
    }
    private static void VerifyMachineCompactFooter(MachineCenterWindow window,string reportPath,string state,List<string> checks)
    {
        var root=(FrameworkElement)window.Content;
        var button=(Button)typeof(MachineCenterWindow).GetField("prepare",PrivateInstance)!.GetValue(window)!;
        var hint=(TextBlock)typeof(MachineCenterWindow).GetField("inputHint",PrivateInstance)!.GetValue(window)!;
        var message=(TextBlock)typeof(MachineCenterWindow).GetField("result",PrivateInstance)!.GetValue(window)!;
        foreach(var (width,height) in new[] { (720,540),(1024,640) })
        {
            Render(window,Path.ChangeExtension(reportPath,$".machine-{state}-{width}x{height}.png"),width,height);
            var footerViewer=Descendants<ScrollViewer>(root).Single(v=>AutomationProperties.GetName(v)=="Dosya hazırlama mesajları");
            footerViewer.ScrollToTop();footerViewer.UpdateLayout();
            var headVisible=FullyVisible(message,root);
            var tailVisible=FullyVisible(hint,root);
            if(footerViewer.ScrollableHeight>0)
            {
                footerViewer.ScrollToEnd();footerViewer.UpdateLayout();
                tailVisible=FullyVisible(hint,root);
                footerViewer.ScrollToTop();footerViewer.UpdateLayout();
            }
            // The bounded footer deliberately scrolls when both warnings cannot fit.
            // Require the actual result at the start and the hint at the end, plus
            // a keyboard tab stop and visible action; never require clipped text.
            Check(FullyVisible(button,root) && FullyVisible(footerViewer,root) && footerViewer.Focusable
                && KeyboardNavigation.GetIsTabStop(footerViewer) && headVisible && tailVisible
                && hint.ActualHeight+hint.Margin.Top+hint.Margin.Bottom>=hint.DesiredSize.Height-.5
                && message.ActualHeight+message.Margin.Top+message.Margin.Bottom>=message.DesiredSize.Height-.5,
                $"Machine {state} action and messages are visible or keyboard-reachable at {width}x{height}",checks);
        }
        var originalMessage=message.Text;
        var viewer=Descendants<ScrollViewer>(root).Single(v=>AutomationProperties.GetName(v)=="Dosya hazırlama mesajları");
        try
        {
            message.Text=string.Concat(Enumerable.Repeat("Uzun parça adı ve hata ayrıntısı ",70))+" SON";
            Render(window,Path.ChangeExtension(reportPath,$".machine-{state}-long.png"),720,540);
            Check(viewer.Focusable && KeyboardNavigation.GetIsTabStop(viewer) && viewer.ScrollableHeight>0
                && FullyVisible(viewer,root) && FullyVisible(button,root),
                $"Machine {state} long result remains keyboard-scrollable with action visible",checks);
            viewer.ScrollToEnd();viewer.UpdateLayout();
            Check(viewer.VerticalOffset>=viewer.ScrollableHeight-.5 && message.Text.EndsWith(" SON"),
                $"Machine {state} long result can be scrolled to its end",checks);
        }
        finally
        {
            // This same window is reused in later states. Do not leave the
            // test-created scroll position at the end after restoring its text.
            message.Text=originalMessage;
            viewer.ScrollToTop();
            root.UpdateLayout();
        }
    }
    private static void VerifyDxfGrainlineExportReviewWindow(MainWindow studio,string reportPath,List<string> checks)
    {
        var piece=new PatternPiece { Name="Ön beden",Closed=true,Points=[new(0,0),new(20,0),new(0,20)],
            Metadata=new PieceMetadata { Size="38" },Grainline=new Grainline { Start=new(2,3),End=new(2,8),NeedsReview=true } };
        var document=new ProjectDocument { Name="İnceleme",Pieces=[piece] };
        var before=document.Clone();var plan=DxfGrainlineReviewExport.Prepare(document);var item=plan.Items.Single();
        var window=new DxfGrainlineExportReviewWindow(studio,plan);var root=(FrameworkElement)window.Content;
        try
        {
            Render(window,Path.ChangeExtension(reportPath,".dxf-grain-export-review.png"),980,720);
            var grid=Descendants<DataGrid>(root).Single(g=>AutomationProperties.GetName(g)=="Tüm parça, kontur ve işaret katmanları");
            var details=Descendants<TextBox>(root).Single(t=>AutomationProperties.GetName(t)=="Seçili parçanın tam katman ve işaret ayrıntıları");
            var omitted=Descendants<TextBox>(root).Single(t=>AutomationProperties.GetName(t)=="DXF'ye aktarılmayan bilgiler");
            var warning=Descendants<TextBlock>(root).Single(t=>t.Text.Contains("kesim yolu sayabilir"));
            var accept=Descendants<Button>(root).Single(b=>Equals(b.Content,"İnceleme dosyasını yaz"));
            var cancel=Descendants<Button>(root).Single(b=>Equals(b.Content,"İptal et"));
            Check(grid.Items.Count==1 && grid.SelectedIndex==0 && !string.IsNullOrEmpty(AutomationProperties.GetHelpText(grid)) &&
                details.Text.Contains(piece.Name) && details.Text.Contains(piece.Id.ToString("D")) &&
                details.Text.Contains(item.ContourLayer) && details.Text.Contains(item.MarkerLayer!) &&
                details.Text.Contains("Y 3") && details.Text.Contains("Y 8") && details.Text.Contains("Kontrol bekliyor"),
                "Review window exposes the selected piece, both layer names, endpoints and review state",checks);
            Check(omitted.Text.Contains("Beden") && omitted.Text.Contains("İplik yönü kontrol durumu") && omitted.Text.Contains("TekstilCAD parça kimliği") &&
                warning.Text.Contains("Hedef CAD") && warning.Text.Contains("üretim onayı değildir") && FullyVisible(warning,root),
                "Review window displays omitted data and the target CAD and cutting warning",checks);
            Check(cancel.IsCancel && !accept.IsDefault && !accept.IsCancel && DocumentSession.ContentEquals(before,document),
                "Review window requires explicit confirmation, offers cancellation and leaves source unchanged",checks);
            bool rejectedNonModal=false;
            try { cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); } catch(InvalidOperationException) { rejectedNonModal=true; }
            Check(rejectedNonModal && DocumentSession.ContentEquals(before,document),"Headless cancel does not modify the source",checks);
        }
        finally { window.Close(); }
    }
    private static void VerifyDxfGrainlineReviewExport(MainWindow studio,string work,List<string> checks)
    {
        var oldContext=SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(studio.Dispatcher));
        var piece=new PatternPiece { Name="Ön",Closed=true,Points=[new(0,0),new(20,0),new(0,20)],
            Grainline=new Grainline { Start=new(2,3),End=new(2,8),NeedsReview=true } };
        var original=new ProjectDocument { Name="İnceleme",Pieces=[piece] };
        string Target()=>Path.Combine(work,"grain-review-export-"+Guid.NewGuid().ToString("N")+".dxf");
        void Reset() { studio.LoadDocument(original);studio.Select(piece.Id); }
        void Run(string path,ProjectFileVersion expected) { var task=studio.StartDxfGrainlineReviewExportAsync(path,expected);PumpUntil(()=>task.IsCompleted);task.GetAwaiter().GetResult(); }
        try
        {
            Reset();var before=studio.Document.Clone();var path=Target();var version=ProjectStorage.CaptureVersion(path);var approved=false;
            studio.ConfirmDxfGrainlineExportForVerification=plan=>
            {
                var item=plan.Items.Single();
                approved=item.PieceId==piece.Id && item.MarkerLayer is not null && item.ContourLayer!=item.MarkerLayer && plan.Matches(before);
                return true;
            };
            Run(path,version);
            var exported=DxfFormat.Import(path);
            Check(approved && studio.ExportError is null && File.Exists(path) && DocumentSession.ContentEquals(before,studio.Document) &&
                exported.Project.Pieces.Count==2 && exported.Project.Pieces.Select(p=>p.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count()==2 &&
                exported.Project.Pieces.Any(p=>p.Closed) && exported.Project.Pieces.Any(p=>!p.Closed && p.Points.Count==2),
                "Approved grain review export writes separate contour and marker layers without changing source",checks);

            Reset();before=studio.Document.Clone();path=Target();version=ProjectStorage.CaptureVersion(path);
            studio.ConfirmDxfGrainlineExportForVerification=_=>false;Run(path,version);
            Check(studio.ExportWasCancelled && !File.Exists(path) && DocumentSession.ContentEquals(before,studio.Document),
                "Declining grain review preserves workspace and leaves target absent",checks);

            Reset();path=Target();File.WriteAllText(path,"existing");var existing=File.ReadAllBytes(path);version=ProjectStorage.CaptureVersion(path);
            bool rejected=false;try { studio.StartDxfGrainlineReviewExportAsync(path,version); } catch(InvalidOperationException) { rejected=true; }
            Check(rejected && !studio.ExportRunning && File.ReadAllBytes(path).SequenceEqual(existing),
                "Grain review export refuses a pre-existing destination",checks);

            Reset();before=studio.Document.Clone();path=Target();version=ProjectStorage.CaptureVersion(path);
            studio.ConfirmDxfGrainlineExportForVerification=_=> { File.WriteAllText(path,"other writer");return true; };Run(path,version);
            Check(studio.ExportError is not null && File.ReadAllText(path)=="other writer" && DocumentSession.ContentEquals(before,studio.Document),
                "Grain review export retains destination created after approval capture",checks);

            foreach(var change in new[] { "document","selection","draft" })
            {
                Reset();path=Target();version=ProjectStorage.CaptureVersion(path);
                studio.ConfirmDxfGrainlineExportForVerification=_=>
                {
                    if(change=="document")studio.Commit("Change during grain review",()=>studio.Document.Name="Değişti");
                    else if(change=="selection")studio.Select(null);
                    else ((TextBox)typeof(MainWindow).GetField("name",PrivateInstance)!.GetValue(studio)!).Text="Uygulanmamış taslak";
                    return true;
                };
                Run(path,version);
                Check(studio.ExportError is not null && !File.Exists(path),$"Grain review export rejects {change} changed during approval",checks);
            }
            Reset();path=Target();version=ProjectStorage.CaptureVersion(path);
            using var reached=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
            studio.BeforeGrainlineExportPreparationForVerification=()=> { reached.Set();if(!release.Wait(TimeSpan.FromSeconds(30)))throw new TimeoutException(); };
            var pending=studio.StartDxfGrainlineReviewExportAsync(path,version);
            try
            {
                PumpUntil(()=>reached.IsSet);
                bool blocked=false;try { studio.StartDxfGrainlineReviewExportAsync(Target(),ProjectStorage.CaptureVersion(Target())); } catch(InvalidOperationException) { blocked=true; }
                Invoke(studio,"RequestExportCancellation");release.Set();PumpUntil(()=>pending.IsCompleted);pending.GetAwaiter().GetResult();
                Check(blocked && studio.ExportWasCancelled && !File.Exists(path),"Running grain review export blocks a second export and cancellation leaves no target",checks);
            }
            finally { release.Set();PumpUntil(()=>pending.IsCompleted);studio.BeforeGrainlineExportPreparationForVerification=null; }
        }
        finally
        {
            studio.ConfirmDxfGrainlineExportForVerification=null;studio.BeforeGrainlineExportPreparationForVerification=null;
            SynchronizationContext.SetSynchronizationContext(oldContext);
        }
    }
    private static void VerifyAsyncExport(MainWindow studio,string work,List<string> checks)
    {
        var original=new ProjectDocument { Name="Dışa aktarma testi",Pieces=[new PatternPiece { Name="Özgün parça",Points=[new(0,0),new(100,0),new(100,100),new(0,100)] }] };
        studio.LoadDocument(original);
        var mismatchPath=Path.Combine(work,"export-extension-mismatch.tcad");
        File.WriteAllBytes(mismatchPath,[0,1,2,3,255]);
        var mismatchBytes=File.ReadAllBytes(mismatchPath);
        var mismatchDocument=studio.Document.Clone();
        var mismatchVersion=ProjectStorage.CaptureVersion(mismatchPath);
        var previousTask=studio.ExportTask;
        var command=(Button)typeof(MainWindow).GetField("exportCommand",PrivateInstance)!.GetValue(studio)!;
        var commandWasEnabled=command.IsEnabled;
        var declinedPath=Path.Combine(work,"hpgl-preflight-declined.plt");
        File.WriteAllText(declinedPath,"önceki içerik");var declinedBytes=File.ReadAllBytes(declinedPath);
        var declinedVersion=ProjectStorage.CaptureVersion(declinedPath);string? shown=null;
        var declined=studio.StartExportAfterPreflight(studio.Document.Clone(),declinedPath,3,declinedVersion,message=> { shown=message;return false; });
        Check(declined is null && !studio.ExportRunning && ReferenceEquals(previousTask,studio.ExportTask)
            && File.ReadAllBytes(declinedPath).SequenceEqual(declinedBytes) && DocumentSession.ContentEquals(mismatchDocument,studio.Document)
            && shown is not null && shown.Contains("40 birim/mm") && shown.Contains("0,025 mm") && shown.Contains("tam sayıya yuvarlanır")
            && shown.Contains("kalibrasyon doğrulanmadı") && shown.Contains("Makine simülatöründe") && shown.Contains("fiziksel makine hassasiyeti değildir")
            && shown.Contains("Küçük çentikler") && shown.Contains("düzleşebilir") && shown.Contains("üretimden önce kontrol edin"),
            "Declining HPGL preflight preserves source and target and shows coordinate and machine limitations before writing",checks);
        var approvedPath=Path.Combine(work,"hpgl-preflight-approved.plt");
        var approvedVersion=ProjectStorage.CaptureVersion(approvedPath);var approvedSeen=false;
        var approvedTask=studio.StartExportAfterPreflight(studio.Document.Clone(),approvedPath,3,approvedVersion,_=> { approvedSeen=true;return true; });
        Check(approvedSeen && approvedTask is not null,"Accepting HPGL preflight starts the export",checks);
        PumpUntil(()=>approvedTask!.IsCompleted);approvedTask!.GetAwaiter().GetResult();
        Check(studio.ExportError is null && File.Exists(approvedPath) && DocumentSession.ContentEquals(mismatchDocument,studio.Document),
            "Accepted HPGL preflight exports without changing the source",checks);
        previousTask=studio.ExportTask;
        foreach(var format in new[] { 1,2,3 })
        {
            try { studio.StartExportAsync(studio.Document.Clone(),mismatchPath,format,mismatchVersion);throw new InvalidOperationException("Mismatched export extension was accepted."); }
            catch(InvalidDataException) { }
            Check(!studio.ExportRunning && ReferenceEquals(previousTask,studio.ExportTask) && command.IsEnabled==commandWasEnabled
                && File.ReadAllBytes(mismatchPath).SequenceEqual(mismatchBytes) && DocumentSession.ContentEquals(mismatchDocument,studio.Document),
                $"Export format {format} rejects mismatched extension before starting and preserves destination and document",checks);
        }
        foreach(var (format,extension) in new[] { (1,"dxf"),(2,"svg"),(3,"plt") })
        {
            // A fresh isolated path is absent at version capture; another writer
            // creates it after the user approved the target but before the export.
            var appearedPath=Path.Combine(work,$"export-appeared-{Guid.NewGuid():N}.{extension}");
            var absent=ProjectStorage.CaptureVersion(appearedPath);
            File.WriteAllText(appearedPath,"başka işlem tarafından oluşturuldu");
            var external=File.ReadAllBytes(appearedPath);
            var before=studio.Document.Clone();
            var appeared=studio.StartExportAsync(before.Clone(),appearedPath,format,absent);
            PumpUntil(()=>appeared.IsCompleted);appeared.GetAwaiter().GetResult();
            Check(!studio.ExportRunning && studio.ExportError is not null && File.ReadAllBytes(appearedPath).SequenceEqual(external)
                && DocumentSession.ContentEquals(before,studio.Document),
                $"Export format {format} preserves a destination created after version capture",checks);

            var changedPath=Path.Combine(work,$"export-changed-{Guid.NewGuid():N}.{extension}");
            File.WriteAllText(changedPath,"onaylanan sürüm");
            var approved=ProjectStorage.CaptureVersion(changedPath);
            File.WriteAllText(changedPath,"başka işlem tarafından değiştirilen sürüm");
            external=File.ReadAllBytes(changedPath);
            var changed=studio.StartExportAsync(before.Clone(),changedPath,format,approved);
            PumpUntil(()=>changed.IsCompleted);changed.GetAwaiter().GetResult();
            Check(!studio.ExportRunning && studio.ExportError is not null && File.ReadAllBytes(changedPath).SequenceEqual(external)
                && DocumentSession.ContentEquals(before,studio.Document),
                $"Export format {format} preserves a destination changed after version capture",checks);
        }
        var path=Path.Combine(work,"async-export.svg");
        var expected=ProjectStorage.CaptureVersion(path);
        var snapshot=studio.Document.Clone();
        var task=studio.StartExportAsync(snapshot,path,2,expected);
        Check(studio.ExportRunning && !((Button)typeof(MainWindow).GetField("exportCommand",PrivateInstance)!.GetValue(studio)!).IsEnabled,
            "Async export begins once and disables the export command",checks);
        try { studio.StartExportAsync(snapshot,path,2,expected);throw new InvalidOperationException("Duplicate export was accepted"); }
        catch(InvalidOperationException ex) when(ex.Message.Contains("Dışa aktarım sürüyor")) { }
        var canDiscard=(bool)typeof(MainWindow).GetMethod("CanDiscard",PrivateInstance,null,Type.EmptyTypes,null)!.Invoke(studio,null)!;
        Check(!canDiscard,"Running export blocks closing or replacing the project",checks);
        snapshot.Pieces[0].Name="Çağıran kopyayı değiştirdi";
        studio.Commit("Dışa aktarım sırasında düzenleme",()=>studio.Document.Pieces[0].Name="Yeni parça adı");
        PumpUntil(()=>task.IsCompleted);task.GetAwaiter().GetResult();
        var notice=(TextBlock)typeof(MainWindow).GetField("exportNotice",PrivateInstance)!.GetValue(studio)!;
        var exported=File.ReadAllText(path);
        Check(!studio.ExportRunning && studio.ExportError is null && exported.Contains("Özgün parça") && !exported.Contains("Yeni parça adı") && !exported.Contains("Çağıran kopyayı değiştirdi")
            && studio.Document.Pieces[0].Name=="Yeni parça adı" && notice.Text.Contains("önceki"),
            "Async export retains its independent snapshot and reports later project edits",checks);

        var conflictPath=Path.Combine(work,"async-export-conflict.svg");
        File.WriteAllText(conflictPath,"ilk sürüm");
        var conflictVersion=ProjectStorage.CaptureVersion(conflictPath);
        File.WriteAllText(conflictPath,"başka oturumun sürümü");
        var prior=File.ReadAllBytes(conflictPath);var document=studio.Document.Clone();
        var conflict=studio.StartExportAsync(studio.Document.Clone(),conflictPath,2,conflictVersion);
        PumpUntil(()=>conflict.IsCompleted);conflict.GetAwaiter().GetResult();
        Check(!studio.ExportRunning && studio.ExportError is not null &&
            studio.ExportError.Contains("başka bir işlem",StringComparison.Ordinal) &&
            studio.ExportError.Contains("Aktarım yapılmadı",StringComparison.Ordinal) &&
            studio.ExportError.Contains("açık projeniz korunuyor",StringComparison.Ordinal) &&
            studio.ExportError.Contains("farklı bir dosya adı",StringComparison.Ordinal) &&
            notice.Text==studio.ExportError && File.ReadAllBytes(conflictPath).SequenceEqual(prior)
            && DocumentSession.ContentEquals(document,studio.Document),
            "Async export conflict explains recovery and preserves the changed destination and open document",checks);

        var lockedPath=Path.Combine(work,"async-export-locked.svg");
        File.WriteAllText(lockedPath,"kilitli önceki dosya");
        var lockedVersion=ProjectStorage.CaptureVersion(lockedPath);var oldBytes=File.ReadAllBytes(lockedPath);
        using(var held=new FileStream(lockedPath,FileMode.Open,FileAccess.Read,FileShare.Read))
        {
            var locked=studio.StartExportAsync(studio.Document.Clone(),lockedPath,2,lockedVersion);
            PumpUntil(()=>locked.IsCompleted);locked.GetAwaiter().GetResult();
            Check(studio.ExportError is not null && File.ReadAllBytes(lockedPath).SequenceEqual(oldBytes)
                && DocumentSession.ContentEquals(document,studio.Document),
                "Async export write failure preserves the previous destination and open document",checks);
        }
    }
    private static void VerifyMachineOpenApprovalState(MainWindow studio,string work,List<string> checks)
    {
        var canvas=(CadCanvas)typeof(MainWindow).GetField("canvas",PrivateInstance)!.GetValue(studio)!;
        var session=(DocumentSession)typeof(MainWindow).GetField("session",PrivateInstance)!.GetValue(studio)!;
        var queueField=typeof(MainWindow).GetField("machineQueue",PrivateInstance)!;
        var originalQueue=queueField.GetValue(studio);
        var invoke=typeof(MainWindow).GetMethod("OpenMachineCenter",PrivateInstance)!;
        var name=(TextBox)typeof(MainWindow).GetField("name",PrivateInstance)!.GetValue(studio)!;
        var status=(TextBlock)typeof(MainWindow).GetField("status",PrivateInstance)!.GetValue(studio)!;
        try
        {
            foreach(var scenario in new[] { "accept", "reject", "drawing", "draft", "selection", "document", "reload" })
            {
                canvas.CancelDrawing();studio.SetTool("Seç");
                var first=new PatternPiece { Name="Ön",Points=[new(0,0),new(10,0),new(0,10)],Metadata=new PieceMetadata { Role="Kesim",Quantity=1 } };
                var second=new PatternPiece { Name="Arka",Points=[new(20,0),new(30,0),new(20,10)] };
                studio.LoadDocument(new ProjectDocument { Name="Onay testi",Pieces=[first,second] });studio.Select(first.Id);
                var queue=new SimulationQueue(Path.Combine(work,"machine-open-approval-"+scenario+"-"+Guid.NewGuid().ToString("N")));
                queueField.SetValue(studio,queue);
                var opened=0;var notices=0;ProjectDocument? expected=null;var undo=-1;var redo=-1;
                studio.OpenMachineCenterForVerification=value=> { Check(ReferenceEquals(queue,value),"Machine open uses the approved queue",checks);opened++; };
                studio.ConfirmMachineLossForVerification=message=>
                {
                    notices++;
                    if(scenario=="drawing") { studio.SetTool("Çizgi");canvas.RestorePendingDrawing(new RecoveryPendingDrawing("Çizgi",[new Point2(1,2)])); }
                    else if(scenario=="draft")name.Text="Uygulanmamış ad";
                    else if(scenario=="selection")studio.Select(second.Id);
                    else if(scenario=="document")studio.Commit("Onay sırasında düzenleme",()=>studio.Document.Name="Değişen proje");
                    else if(scenario=="reload")studio.LoadDocument(new ProjectDocument { Name="Yeniden açılan proje",Pieces=[first.Clone()] });
                    expected=studio.Document.Clone();undo=session.UndoCount;redo=session.RedoCount;
                    return scenario!="reject";
                };
                invoke.Invoke(studio,null);
                var allowed=scenario=="accept";
                Check(notices==1 && opened==(allowed?1:0) && expected is not null && DocumentSession.ContentEquals(expected,studio.Document)
                    && session.UndoCount==undo && session.RedoCount==redo && queue.Jobs.Count==0
                    && (allowed ? queueField.GetValue(studio) is null : ReferenceEquals(queueField.GetValue(studio),queue)),
                    $"Machine loss approval {scenario} respects the captured document and preserves work",checks);
                if(scenario is not ("accept" or "reject"))
                    Check(status.Text.Contains("simülatörü açılmadı",StringComparison.OrdinalIgnoreCase),$"Stale machine approval {scenario} explains why it was not opened",checks);
                if(scenario=="drawing")Check(canvas.HasPendingDrawing,"Machine approval preserves a newly pending drawing",checks);
                if(scenario=="draft")Check(studio.HasPendingCanvasOrPropertyWork && name.Text=="Uygulanmamış ad","Machine approval preserves a newly edited property draft",checks);
            }
        }
        finally
        {
            studio.ConfirmMachineLossForVerification=null;studio.OpenMachineCenterForVerification=null;
            canvas.CancelDrawing();studio.LoadDocument(new ProjectDocument { Name="Makine onayı doğrulandı" });studio.SetTool("Seç");
            queueField.SetValue(studio,originalQueue);
        }
    }

    private static void VerifyMachineDxfSourceConfirmation(MainWindow studio,string work,List<string> checks)
    {
        var canvas=(CadCanvas)typeof(MainWindow).GetField("canvas",PrivateInstance)!.GetValue(studio)!;
        var oldContext=SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(studio.Dispatcher));
        try
        {
            foreach(var scenario in new[] { "unchanged", "draft", "drawing", "selection" })
            {
                canvas.CancelDrawing();studio.SetTool("Seç");
                var piece=new PatternPiece { Name="DXF kaynak",Closed=false,Points=[new(0,0),new(10,0)] };
                studio.LoadDocument(new ProjectDocument { Pieces=[piece] });studio.Select(piece.Id);
                var original=studio.Document.Clone();
                var spool=Path.Combine(work,"machine-dxf-source-"+scenario+"-"+Guid.NewGuid().ToString("N"));
                var queue=new SimulationQueue(spool);var machine=new MachineCenterWindow(studio,queue);
                using var reached=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
                machine.BeforePrepareForVerification=()=> { reached.Set();if(!release.Wait(TimeSpan.FromSeconds(30)))throw new TimeoutException("DXF preparation boundary not released."); };
                var task=(Task)typeof(MachineCenterWindow).GetMethod("Prepare",PrivateInstance)!.Invoke(machine,null)!;
                try
                {
                    PumpUntil(()=>reached.IsSet);
                    if(scenario=="draft")((TextBox)typeof(MainWindow).GetField("name",PrivateInstance)!.GetValue(studio)!).Text="Bekleyen yeni ad";
                    else if(scenario=="drawing") { studio.SetTool("Çizgi");canvas.RestorePendingDrawing(new RecoveryPendingDrawing("Çizgi",[new Point2(3,4)])); }
                    else if(scenario=="selection")studio.Select(null);
                    release.Set();PumpUntil(()=>task.IsCompleted);task.GetAwaiter().GetResult();
                    var job=queue.Jobs.Single();var result=(TextBlock)typeof(MachineCenterWindow).GetField("result",PrivateInstance)!.GetValue(machine)!;
                    var expectedUnconfirmed=scenario!="unchanged";
                    Check(job.SourceSnapshotUnconfirmed==expectedUnconfirmed && new SimulationQueue(spool).Jobs.Single().SourceSnapshotUnconfirmed==expectedUnconfirmed
                        && DocumentSession.ContentEquals(original,studio.Document)
                        && (!expectedUnconfirmed || result.Foreground==StudioTheme.Warning && result.Text.Contains("yeniden hazırlayın",StringComparison.OrdinalIgnoreCase)),
                        $"DXF preparation confirms source only when {scenario} state remains current",checks);
                    if(scenario=="drawing")Check(canvas.HasPendingDrawing,"DXF preparation preserves a later pending drawing",checks);
                    if(scenario=="draft")Check(studio.HasPendingCanvasOrPropertyWork,"DXF preparation preserves a later property draft",checks);
                }
                finally { release.Set();PumpUntil(()=>task.IsCompleted);canvas.CancelDrawing(); }
            }
        }
        finally { canvas.CancelDrawing();studio.SetTool("Seç");SynchronizationContext.SetSynchronizationContext(oldContext); }
    }
    private static void VerifyMachineConfirmationFailure(MainWindow studio,string work,List<string> checks)
    {
        var source=new ProjectDocument { Name="Kuyruk onay hatası",Pieces=[new PatternPiece { Name="Parça",Closed=false,Points=[new(0,0),new(10,0)] }] };
        studio.LoadDocument(source);studio.Select(studio.Document.Pieces[0].Id);
        var before=studio.Document.Clone();var spool=Path.Combine(work,"machine-confirm-conflict");
        var queue=new SimulationQueue(spool);var machine=new MachineCenterWindow(studio,queue);
        byte[]? alteredManifest=null;byte[]? outputBytes=null;
        machine.AfterPrepareForVerification=()=>
        {
            var prepared=queue.Jobs.Single();outputBytes=File.ReadAllBytes(prepared.OutputPath);
            File.AppendAllText(prepared.ManifestPath," ");
            alteredManifest=File.ReadAllBytes(prepared.ManifestPath);
        };
        var previousContext=SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(studio.Dispatcher));
        try
        {
            var task=(Task)typeof(MachineCenterWindow).GetMethod("Prepare",PrivateInstance)!.Invoke(machine,null)!;
            PumpUntil(()=>task.IsCompleted);task.GetAwaiter().GetResult();
            var job=queue.Jobs.Single();
            var grid=(DataGrid)typeof(MachineCenterWindow).GetField("jobs",PrivateInstance)!.GetValue(machine)!;
            var result=(TextBlock)typeof(MachineCenterWindow).GetField("result",PrivateInstance)!.GetValue(machine)!;
            Check(job.SourceSnapshotUnconfirmed && grid.Items.Count==1 && grid.SelectedItem is SimulationJob selected && selected.Id==job.Id
                && result.Foreground==StudioTheme.Warning && result.Text.Contains("İş listede görünüyor") && result.Text.Contains("diskteki durumu doğrulanamadı") && result.Text.Contains("Kaynak doğrulanmadı")
                && result.Text.Contains("yeniden hazırlayın"),
                "Manifest confirmation conflict shows the committed unconfirmed job and warning",checks);
            Check(alteredManifest is not null && File.ReadAllBytes(job.ManifestPath).SequenceEqual(alteredManifest)
                && outputBytes is not null && File.ReadAllBytes(job.OutputPath).SequenceEqual(outputBytes)
                && DocumentSession.ContentEquals(before,studio.Document) && !machine.IsVisible,
                "Confirmation failure does not retry or change the manifest, output or source",checks);
            Check(new SimulationQueue(spool).Jobs.Single().SourceSnapshotUnconfirmed,
                "Unconfirmed source survives reopening after the manifest conflict",checks);
            var missingQueue=new SimulationQueue(Path.Combine(work,"machine-missing-output"));
            var missingMachine=new MachineCenterWindow(studio,missingQueue);
            missingMachine.AfterPrepareForVerification=()=>File.Delete(missingQueue.Jobs.Single().OutputPath);
            var missingTask=(Task)typeof(MachineCenterWindow).GetMethod("Prepare",PrivateInstance)!.Invoke(missingMachine,null)!;
            PumpUntil(()=>missingTask.IsCompleted);missingTask.GetAwaiter().GetResult();
            var missingJob=missingQueue.Jobs.Single();
            var missingResult=(TextBlock)typeof(MachineCenterWindow).GetField("result",PrivateInstance)!.GetValue(missingMachine)!;
            var missingGrid=(DataGrid)typeof(MachineCenterWindow).GetField("jobs",PrivateInstance)!.GetValue(missingMachine)!;
            Check(!File.Exists(missingJob.OutputPath) && missingGrid.Items.Count==1 && missingResult.Foreground==StudioTheme.Warning
                && missingResult.Text.Contains("İş listede görünüyor") && missingResult.Text.Contains("çıktı önizlemesi okunamadı")
                && missingResult.Text.Contains("Fiziksel makine testi yapılmadı") && DocumentSession.ContentEquals(before,studio.Document),
                "Missing prepared output remains a warning instead of a successful preview",checks);
            typeof(MachineCenterWindow).GetMethod("Simulate",PrivateInstance)!.Invoke(missingMachine,null);
            Check(missingQueue.Jobs.Single().Status==JobStatus.Failed && missingResult.Foreground==StudioTheme.Danger
                && missingResult.Text.Contains("Dosya kontrolü başarısız") && missingResult.Text.Contains("Çıktı önizlemesi okunamadı")
                && missingResult.Text.Contains("Fiziksel makine testi yapılmadı") && DocumentSession.ContentEquals(before,studio.Document),
                "Checking a missing output reports both failed integrity and unreadable preview",checks);
        }
        finally { SynchronizationContext.SetSynchronizationContext(previousContext); }
    }
    private static void VerifyMachineHpglReview(MainWindow studio,string work,List<string> checks)
    {
        var source=new ProjectDocument { Name="Simülatör incelemesi",Pieces=[new PatternPiece { Name="Çizgi",Closed=false,Points=[new(0,0),new(10,0)] }] };
        var oldContext=SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(studio.Dispatcher));
        try
        {
            (MachineCenterWindow Window,SimulationQueue Queue,string Spool) Fixture(string label)
            {
                studio.LoadDocument(source);studio.Select(studio.Document.Pieces[0].Id);
                var spool=Path.Combine(work,"machine-review-"+label);var queue=new SimulationQueue(spool);
                var window=new MachineCenterWindow(studio,queue);
                ((ComboBox)typeof(MachineCenterWindow).GetField("format",PrivateInstance)!.GetValue(window)!).SelectedIndex=1;
                ((TextBox)typeof(MachineCenterWindow).GetField("scale",PrivateInstance)!.GetValue(window)!).Text="2";
                ((TextBox)typeof(MachineCenterWindow).GetField("units",PrivateInstance)!.GetValue(window)!).Text="20";
                return (window,queue,spool);
            }
            Task Prepare(MachineCenterWindow window)=>(Task)typeof(MachineCenterWindow).GetMethod("Prepare",PrivateInstance)!.Invoke(window,null)!;
            var (rejected,empty,rejectedSpool)=Fixture("rejected");string? shown=null;
            rejected.ConfirmHpglReviewForVerification=(prepared,pieces)=> { shown=prepared.Text;return false; };
            var rejectedTask=Prepare(rejected);PumpUntil(()=>rejectedTask.IsCompleted);rejectedTask.GetAwaiter().GetResult();
            Check(shown is not null && empty.Jobs.Count==0 && !Directory.Exists(rejectedSpool) && DocumentSession.ContentEquals(source,studio.Document),
                "Declining machine HPGL review creates no spool or job and preserves the source",checks);
            rejected.Close();
            var (accepted,queue,_)=Fixture("accepted");string? approved=null;
            accepted.ConfirmHpglReviewForVerification=(prepared,pieces)=> { approved=prepared.Text;return pieces.Count==1; };
            var acceptedTask=Prepare(accepted);PumpUntil(()=>acceptedTask.IsCompleted);acceptedTask.GetAwaiter().GetResult();
            Check(queue.Jobs.Count==1 && approved is not null && File.ReadAllText(queue.Jobs[0].OutputPath)==approved
                && approved.Contains("400,0") && queue.Jobs[0].Profile.Scale==2 && queue.Jobs[0].Profile.UnitsPerMillimeter==20,
                "Machine writes exactly the reviewed HPGL with captured scale and units",checks);
            accepted.Close();
            var (changed,changedQueue,changedSpool)=Fixture("profile-changed");
            changed.ConfirmHpglReviewForVerification=(_,_)=> { ((TextBox)typeof(MachineCenterWindow).GetField("units",PrivateInstance)!.GetValue(changed)!).Text="21";return true; };
            var changedTask=Prepare(changed);PumpUntil(()=>changedTask.IsCompleted);changedTask.GetAwaiter().GetResult();
            Check(changedQueue.Jobs.Count==0 && !Directory.Exists(changedSpool),"Profile change invalidates machine HPGL review before file creation",checks);
            changed.Close();
            var (stale,staleQueue,staleSpool)=Fixture("project-changed");
            stale.ConfirmHpglReviewForVerification=(_,_)=> { studio.Commit("İnceleme sırasında değişiklik",()=>studio.Document.Name="Değişti");return true; };
            var staleTask=Prepare(stale);PumpUntil(()=>staleTask.IsCompleted);staleTask.GetAwaiter().GetResult();
            Check(staleQueue.Jobs.Count==0 && !Directory.Exists(staleSpool) && studio.Document.Name=="Değişti",
                "Project change invalidates machine HPGL review before file creation",checks);
            stale.Close();
            var (cancelled,cancelQueue,cancelSpool)=Fixture("cancel-after-review-preparation");
            using var reached=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
            cancelled.AfterHpglReviewPreparationForVerification=()=> { reached.Set();release.Wait(); };
            var reviewCalled=false;cancelled.ConfirmHpglReviewForVerification=(_,_)=> { reviewCalled=true;return true; };
            var cancelTask=Prepare(cancelled);
            try
            {
                PumpUntil(()=>reached.IsSet);
                var button=(Button)typeof(MachineCenterWindow).GetField("cancelPrepare",PrivateInstance)!.GetValue(cancelled)!;
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(!cancelTask.IsCompleted && !Directory.Exists(cancelSpool),"Machine HPGL cancellation waits for preparation before writing",checks);
                release.Set();PumpUntil(()=>cancelTask.IsCompleted);cancelTask.GetAwaiter().GetResult();
                Check(!reviewCalled && cancelQueue.Jobs.Count==0 && !Directory.Exists(cancelSpool) && DocumentSession.ContentEquals(source,studio.Document),
                    "Late machine HPGL preparation cancellation discards the report without a job",checks);
            }
            finally { release.Set();PumpUntil(()=>cancelTask.IsCompleted);cancelled.Close(); }
        }
        finally { SynchronizationContext.SetSynchronizationContext(oldContext); }
    }
    private static void VerifyMachineHpglCancellation(MainWindow studio,string work,List<string> checks)
    {
        new WindowInteropHelper(studio).EnsureHandle();
        var source=new ProjectDocument { Name="Makine iptal sınaması",Pieces=[new PatternPiece { Name="Çizgi",Closed=false,Points=[new(0,0),new(10,0)] }] };
        studio.LoadDocument(source);
        studio.Select(studio.Document.Pieces[0].Id);
        var before=studio.Document.Clone();
        var previousContext=SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(studio.Dispatcher));
        try
        {
            foreach(var afterCommit in new[] { false,true })
            {
                var spool=Path.Combine(work,afterCommit?"machine-cancel-after-commit":"machine-cancel-before-commit");
                var queue=new SimulationQueue(spool);
                var machine=new MachineCenterWindow(studio,queue);
                machine.ConfirmHpglReviewForVerification=(_,_)=>true;
                Check(machine.SelectedPieceIds.SequenceEqual(new[] { studio.Document.Pieces[0].Id }),"Machine HPGL fixture selects its source piece",checks);
                ((ComboBox)typeof(MachineCenterWindow).GetField("format",PrivateInstance)!.GetValue(machine)!).SelectedIndex=1;
                var cancel=(Button)typeof(MachineCenterWindow).GetField("cancelPrepare",PrivateInstance)!.GetValue(machine)!;
                var result=(TextBlock)typeof(MachineCenterWindow).GetField("result",PrivateInstance)!.GetValue(machine)!;
                using var reached=new ManualResetEventSlim();
                using var release=new ManualResetEventSlim();
                Action pause=()=> { reached.Set();if(!release.Wait(TimeSpan.FromSeconds(30)))throw new TimeoutException("Machine HPGL verification boundary was not released."); };
                if(afterCommit)machine.AfterPrepareForVerification=pause;
                else machine.BeforePrepareForVerification=pause;
                var task=(Task)typeof(MachineCenterWindow).GetMethod("Prepare",PrivateInstance)!.Invoke(machine,null)!;
                try
                {
                    PumpUntil(()=>reached.IsSet);
                    Check(cancel.Visibility==Visibility.Visible && (afterCommit ? !machine.IsEnabled : cancel.IsEnabled),
                        "HPGL machine preparation exposes cancellation before commit and locks controls during commit",checks);
                    byte[]? output=null;
                    if(afterCommit)
                    {
                        Check(queue.Jobs.Count==1,"Machine HPGL job is committed before late cancellation",checks);
                        output=File.ReadAllBytes(queue.Jobs[0].OutputPath);
                    }
                    cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Check(!cancel.IsEnabled && result.Text.Contains("İptal istendi"),"Machine cancellation reports a request while awaiting its outcome",checks);
                    release.Set();
                    PumpUntil(()=>task.IsCompleted);task.GetAwaiter().GetResult();
                    Check(cancel.Visibility==Visibility.Collapsed && DocumentSession.ContentEquals(before,studio.Document),
                        "Machine HPGL cancellation restores controls and preserves the source",checks);
                    if(afterCommit)
                    {
                        var job=queue.Jobs.Single();
                        Check(job.Status==JobStatus.Prepared && result.Text.Contains("iş kuyrukta korunuyor")
                            && File.ReadAllBytes(job.OutputPath).SequenceEqual(output!)
                            && File.Exists(job.ManifestPath) && new SimulationQueue(spool).Jobs.Single().Id==job.Id,
                            "Late machine cancellation retains the committed output and persisted job",checks);
                    }
                    else
                        Check(queue.Jobs.Count==0 && new SimulationQueue(spool).Jobs.Count==0 && result.Text.Contains("hazırlama iptal edildi"),
                            "Early machine cancellation reports cancellation without a queued job",checks);
                }
                finally { release.Set();PumpUntil(()=>task.IsCompleted); }
            }
        }
        finally { SynchronizationContext.SetSynchronizationContext(previousContext); }
    }
    private static void VerifyHpglPreparationFlow(MainWindow studio,string work,List<string> checks)
    {
        var source=new ProjectDocument { Name="HPGL inceleme",Pieces=[new PatternPiece { Name="Çentik",Points=[new(0,0),new(10,0),new(10,10),new(6,10),new(5,9.99),new(4,10),new(0,10)] }] };
        var oldContext=SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(studio.Dispatcher));
        try
        {
            string Target(string label) { var path=Path.Combine(work,"hpgl-review-"+label+".plt");File.WriteAllText(path,"eski çıktı");return path; }
            studio.LoadDocument(source);var rejectedPath=Target("rejected");var rejectedBytes=File.ReadAllBytes(rejectedPath);
            var original=studio.Document.Clone();var reviewed=false;
            studio.ConfirmHpglReviewForVerification=(prepared,pieces)=> { reviewed=prepared.TurnReviews.Count==pieces.Count && prepared.TurnReviews[0].FlattenedTurns>0;return false; };
            var rejected=studio.StartExportAsync(original.Clone(),rejectedPath,3,ProjectStorage.CaptureVersion(rejectedPath));
            PumpUntil(()=>rejected.IsCompleted);rejected.GetAwaiter().GetResult();
            Check(reviewed && studio.ExportWasCancelled && studio.ExportError is null && File.ReadAllBytes(rejectedPath).SequenceEqual(rejectedBytes)
                && DocumentSession.ContentEquals(original,studio.Document),"Declining prepared HPGL report leaves source and target unchanged",checks);
            studio.LoadDocument(source);var acceptedPath=Target("accepted");string? approvedText=null;
            studio.ConfirmHpglReviewForVerification=(prepared,_)=> { approvedText=prepared.Text;return true; };
            var accepted=studio.StartExportAsync(studio.Document.Clone(),acceptedPath,3,ProjectStorage.CaptureVersion(acceptedPath));
            PumpUntil(()=>accepted.IsCompleted);accepted.GetAwaiter().GetResult();
            Check(studio.ExportError is null && !studio.ExportWasCancelled && approvedText is not null && File.ReadAllText(acceptedPath)==approvedText,
                "Approved prepared HPGL bytes match the reviewed output",checks);
            studio.LoadDocument(source);var conflictPath=Target("conflict");var expected=ProjectStorage.CaptureVersion(conflictPath);
            studio.ConfirmHpglReviewForVerification=(_,_)=> { File.WriteAllText(conflictPath,"dış değişiklik");return true; };
            var conflict=studio.StartExportAsync(studio.Document.Clone(),conflictPath,3,expected);
            PumpUntil(()=>conflict.IsCompleted);conflict.GetAwaiter().GetResult();
            Check(studio.ExportError is not null && File.ReadAllText(conflictPath)=="dış değişiklik",
                "HPGL report approval does not overwrite a target changed during review",checks);
            studio.LoadDocument(source);var stalePath=Target("stale");var staleBytes=File.ReadAllBytes(stalePath);
            studio.ConfirmHpglReviewForVerification=(_,_)=> { studio.Commit("İnceleme sırasında değişiklik",()=>studio.Document.Name="Değişti");return true; };
            var stale=studio.StartExportAsync(studio.Document.Clone(),stalePath,3,ProjectStorage.CaptureVersion(stalePath));
            PumpUntil(()=>stale.IsCompleted);stale.GetAwaiter().GetResult();
            Check(studio.ExportError is not null && File.ReadAllBytes(stalePath).SequenceEqual(staleBytes) && studio.Document.Name=="Değişti",
                "Changed project invalidates HPGL report and preserves the target",checks);
            foreach(var late in new[] { false,true })
            {
                studio.LoadDocument(source);var path=Target(late?"cancel-late":"cancel-early");var bytes=File.ReadAllBytes(path);
                using var reached=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
                Action pause=()=> { reached.Set();release.Wait(); };
                if(late)studio.AfterHpglPreparationForVerification=pause;else studio.BeforeHpglPreparationForVerification=pause;
                var reviewCalled=false;studio.ConfirmHpglReviewForVerification=(_,_)=> { reviewCalled=true;return true; };
                var pending=studio.StartExportAsync(studio.Document.Clone(),path,3,ProjectStorage.CaptureVersion(path));
                try
                {
                    PumpUntil(()=>reached.IsSet);Invoke(studio,"RequestExportCancellation");
                    Check(!pending.IsCompleted && File.ReadAllBytes(path).SequenceEqual(bytes),$"HPGL {(late?"late":"early")} cancellation waits before changing the target",checks);
                    release.Set();PumpUntil(()=>pending.IsCompleted);pending.GetAwaiter().GetResult();
                    Check(studio.ExportWasCancelled && !reviewCalled && File.ReadAllBytes(path).SequenceEqual(bytes) && DocumentSession.ContentEquals(source,studio.Document),
                        $"HPGL {(late?"late":"early")} cancellation discards preparation without review or write",checks);
                }
                finally { release.Set();PumpUntil(()=>pending.IsCompleted);studio.BeforeHpglPreparationForVerification=null;studio.AfterHpglPreparationForVerification=null; }
            }
        }
        finally
        {
            studio.BeforeHpglPreparationForVerification=null;studio.AfterHpglPreparationForVerification=null;
            studio.ConfirmHpglReviewForVerification=(_,_)=>true;SynchronizationContext.SetSynchronizationContext(oldContext);
        }
    }
    private static void VerifyHpglExportCancellation(MainWindow studio,string work,List<string> checks)
    {
        var path=Path.Combine(work,"async-hpgl-cancel.plt");
        File.WriteAllText(path,"önceki çıktı");
        var previous=File.ReadAllBytes(path);
        var source=new ProjectDocument { Name="İptal sınaması", Pieces=[new PatternPiece { Name="Yoğun", Closed=true,
            Points=Enumerable.Range(0,10000).Select(i=> { var a=2*Math.PI*i/10000;return new Point2(1_000_000*Math.Cos(a),1_000_000*Math.Sin(a)); }).ToList() }] };
        studio.LoadDocument(source); var before=studio.Document.Clone();
        var cancel=(Button)typeof(MainWindow).GetField("exportCancel",PrivateInstance)!.GetValue(studio)!;
        var notice=(TextBlock)typeof(MainWindow).GetField("exportNotice",PrivateInstance)!.GetValue(studio)!;
        var task=studio.StartExportAsync(before,path,3,ProjectStorage.CaptureVersion(path));
        Check(studio.ExportRunning && cancel.Visibility==Visibility.Visible && cancel.IsEnabled,
            "Running HPGL export exposes the cancellation action",checks);
        Invoke(studio,"RequestExportCancellation");
        Check(!cancel.IsEnabled && notice.Text.Contains("İptal istendi"),
            "HPGL cancellation request waits for the actual outcome",checks);
        PumpUntil(()=>task.IsCompleted);task.GetAwaiter().GetResult();
        Check(studio.ExportWasCancelled && studio.ExportError is null && File.ReadAllBytes(path).SequenceEqual(previous)
            && DocumentSession.ContentEquals(before,studio.Document) && notice.Text.Contains("iptal edildi") && cancel.Visibility==Visibility.Collapsed,
            "Cancelled HPGL export preserves destination and document",checks);

        var small=new ProjectDocument { Name="Küçük", Pieces=[new PatternPiece { Name="Çizgi",Closed=false,Points=[new(0,0),new(10,0)] }] };
        studio.LoadDocument(small);
        foreach(var (format,extension) in new[] { (1,"dxf"),(2,"svg") })
        {
            var other=Path.Combine(work,$"no-cancel-{format}.{extension}");
            var run=studio.StartExportAsync(studio.Document.Clone(),other,format,ProjectStorage.CaptureVersion(other));
            Check(cancel.Visibility==Visibility.Collapsed,$"Export format {format} does not offer unsupported cancellation",checks);
            PumpUntil(()=>run.IsCompleted);run.GetAwaiter().GetResult();
            Check(studio.ExportError is null && !studio.ExportWasCancelled,$"Export format {format} completes without cancellation",checks);
        }
        var successPath=Path.Combine(work,"async-hpgl-completed.plt");
        var done=studio.StartExportAsync(studio.Document.Clone(),successPath,3,ProjectStorage.CaptureVersion(successPath));
        PumpUntil(()=>done.IsCompleted);done.GetAwaiter().GetResult();
        var finished=File.ReadAllBytes(successPath);
        Invoke(studio,"RequestExportCancellation");
        Check(!studio.ExportWasCancelled && studio.ExportError is null && File.ReadAllBytes(successPath).SequenceEqual(finished)
            && notice.Text.Contains("dışa aktarıldı"),
            "Cancellation requested after HPGL completion preserves successful outcome",checks);
    }
    private static void VerifyAsyncExportPendingPreviews(MainWindow studio,string work,List<string> checks)
    {
        var canvas=(CadCanvas)typeof(MainWindow).GetField("canvas",PrivateInstance)!.GetValue(studio)!;
        var notice=(TextBlock)typeof(MainWindow).GetField("exportNotice",PrivateInstance)!.GetValue(studio)!;
        var name=(TextBox)typeof(MainWindow).GetField("name",PrivateInstance)!.GetValue(studio)!;
        var root=(FrameworkElement)studio.Content;
        foreach(var state in new[] { "property", "drawing", "drag" })
        {
            var piece=new PatternPiece { Name="Kaynak parça",Points=[new(0,0),new(100,0),new(100,100),new(0,100)] };
            studio.LoadDocument(new ProjectDocument { Name="Aktarım önizlemesi",Pieces=[piece] });
            studio.Select(piece.Id);
            if(state=="drawing")studio.SetTool("Çoklu çizgi");
            var before=studio.Document.Clone();
            var path=Path.Combine(work,$"async-pending-{state}.svg");
            var task=studio.StartExportAsync(before.Clone(),path,2,ProjectStorage.CaptureVersion(path));
            if(state=="property")name.Text="Uygulanmamış parça adı";
            else if(state=="drawing")
            {
                var drawing=(List<Point2>)typeof(CadCanvas).GetField("drawing",PrivateInstance)!.GetValue(canvas)!;
                drawing.Add(new Point2(5,5));studio.PendingDrawingChanged();
            }
            else
            {
                typeof(CadCanvas).GetField("dragId",PrivateInstance)!.SetValue(canvas,piece.Id);
                typeof(CadCanvas).GetField("dragOriginal",PrivateInstance)!.SetValue(canvas,piece.Points.ToList());
                typeof(CadCanvas).GetField("dragPreview",PrivateInstance)!.SetValue(canvas,piece.Points.Select(p=>new Point2(p.X+2,p.Y)).ToList());
            }
            PumpUntil(()=>task.IsCompleted);task.GetAwaiter().GetResult();
            Check(studio.ExportError is null && DocumentSession.ContentEquals(before,studio.Document)
                && File.ReadAllText(path).Contains("Kaynak parça") && notice.Visibility==Visibility.Visible
                && Equals(notice.Foreground,StudioTheme.Warning) && notice.Text.Contains("önceki"),
                $"Async export warns about {state} preview without changing its snapshot or document",checks);
            if(state=="property")Check(name.Text=="Uygulanmamış parça adı","Export warning retains the property draft",checks);
            else if(state=="drawing")Check(canvas.HasPendingDrawing,"Export warning retains the unfinished drawing",checks);
            else Check(canvas.HasActiveDragPreview,"Export warning retains the active drag preview",checks);
            Render(studio,Path.Combine(work,$"async-pending-{state}-1024x640.png"),1024,640);
            Check(FullyVisible(notice,root) && notice.ActualHeight+notice.Margin.Top+notice.Margin.Bottom>=notice.DesiredSize.Height-.5
                && notice.Focusable && AutomationProperties.GetName(notice)=="Dışa aktarma durumu",
                $"Async export {state} warning is visible and named at 1024x640",checks);
            canvas.CancelDrawing();
        }
        studio.LoadDocument(new ProjectDocument { Pieces=[new PatternPiece { Name="Başarılı",Points=[new(0,0),new(10,0)] ,Closed=false }] });
        var successPath=Path.Combine(work,"async-visible-success.svg");
        var success=studio.StartExportAsync(studio.Document.Clone(),successPath,2,ProjectStorage.CaptureVersion(successPath));
        PumpUntil(()=>success.IsCompleted);success.GetAwaiter().GetResult();
        Render(studio,Path.Combine(work,"async-success-1024x640.png"),1024,640);
        Check(studio.ExportError is null && notice.Visibility==Visibility.Visible && !notice.Text.Contains("önceki")
            && FullyVisible(notice,root) && File.Exists(successPath),"Async export success notice is visible at 1024x640",checks);

        var conflictPath=Path.Combine(work,"async-visible-error.svg");
        File.WriteAllText(conflictPath,"eski");var version=ProjectStorage.CaptureVersion(conflictPath);
        File.WriteAllText(conflictPath,"dış değişiklik");var original=File.ReadAllBytes(conflictPath);var documentBeforeConflict=studio.Document.Clone();
        var failed=studio.StartExportAsync(studio.Document.Clone(),conflictPath,2,version);
        PumpUntil(()=>failed.IsCompleted);failed.GetAwaiter().GetResult();
        Render(studio,Path.Combine(work,"async-error-1024x640.png"),1024,640);
        Check(studio.ExportError is not null && notice.Visibility==Visibility.Visible && notice.Text==studio.ExportError
            && notice.Text.Contains("Aktarım yapılmadı",StringComparison.Ordinal) && notice.Text.Contains("farklı bir dosya adı",StringComparison.Ordinal)
            && FullyVisible(notice,root) && File.ReadAllBytes(conflictPath).SequenceEqual(original)
            && DocumentSession.ContentEquals(documentBeforeConflict,studio.Document),
            "Async export error notice is visible and preserves the conflicting file at 1024x640",checks);
    }
    private static void VerifyDiscardPromptLongName(MainWindow studio,string reportPath,List<string> checks)
    {
        var longName=string.Concat(Enumerable.Repeat("Ad\n",300));
        studio.LoadDocument(new ProjectDocument { Name=longName });
        var prompt=studio.CreateDiscardPrompt(()=>{});
        try
        {
            Render(prompt,Path.ChangeExtension(reportPath,".long-discard.png"),520,300);
            var root=(FrameworkElement)prompt.Content;
            var notice=Descendants<TextBlock>(root).Single(t=>AutomationProperties.GetName(t).StartsWith("Kaydedilmemiş proje:",StringComparison.Ordinal));
            Check(!notice.Text.Contains('\n') && notice.ToolTip is string full && full.Contains("\\u000A")
                && AutomationProperties.GetName(notice).Contains("\\u000A") && studio.Document.Name==longName
                && Descendants<Button>(root).All(b=>FullyVisible(b,root)),
                "Discard prompt keeps long project name accessible without hiding actions or changing data",checks);
        }
        finally { prompt.Close(); }
    }
    private static void VerifyProjectSaveConflictPresentation(MainWindow studio,string work,List<string> checks)
    {
        var piece=new PatternPiece { Name="Ön",Points=[new(0,0),new(10,0),new(0,10)] };
        studio.LoadDocument(new ProjectDocument { Name="Kayıt çakışması",Pieces=[piece] });
        var path=Path.Combine(work,"project-conflict-"+Guid.NewGuid().ToString("N")+".tcad");
        Invoke(studio,"SaveTo",path);
        studio.Select(piece.Id);
        var name=(TextBox)typeof(MainWindow).GetField("name",PrivateInstance)!.GetValue(studio)!;
        name.Text="Korunan taslak";
        var before=studio.Document.Clone();
        File.WriteAllText(path,"başka uygulamanın içeriği");
        var external=File.ReadAllBytes(path);
        Window? reportedOwner=null; string? message=null;
        MainWindow.ProjectSaveConflictForVerification=(owner,text)=> { reportedOwner=owner; message=text; };
        Window? prompt=null;
        try
        {
            var save=Descendants<Button>((FrameworkElement)studio.Content).Single(b=>Equals(b.Content,"Kaydet"));
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(ReferenceEquals(reportedOwner,studio) && message is not null
                && message.Contains("Bu kayıt yapılmadı",StringComparison.Ordinal)
                && message.Contains("Farklı kaydet",StringComparison.Ordinal)
                && !message.Contains("önceki pencerede Vazgeç",StringComparison.Ordinal)
                && message.Contains("açık çalışmanız korunuyor",StringComparison.Ordinal)
                && File.ReadAllBytes(path).SequenceEqual(external)
                && DocumentSession.ContentEquals(before,studio.Document) && name.Text=="Korunan taslak",
                "Project save conflict gives recovery guidance owned by main window and preserves both versions",checks);
            reportedOwner=null; message=null; var saved=false;
            prompt=studio.CreateDiscardPrompt(()=>saved=true);
            var promptSave=Descendants<Button>((FrameworkElement)prompt.Content).Single(b=>Equals(b.Content,"Kaydet"));
            promptSave.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(ReferenceEquals(reportedOwner,prompt) && message is not null && !saved && prompt.DialogResult is not true
                && message.Contains("önceki pencerede Vazgeç",StringComparison.Ordinal)
                && message.Contains("ana pencerede Farklı kaydet",StringComparison.Ordinal)
                && message.IndexOf("Vazgeç",StringComparison.Ordinal)<message.IndexOf("Farklı kaydet",StringComparison.Ordinal)
                && File.ReadAllBytes(path).SequenceEqual(external)
                && DocumentSession.ContentEquals(before,studio.Document) && name.Text=="Korunan taslak",
                "Discard prompt owns conflict warning and stays open with unsaved work intact",checks);
        }
        finally { MainWindow.ProjectSaveConflictForVerification=null; prompt?.Close(); }
    }
    private static void VerifySaveAsVersionProtection(MainWindow studio,string work,List<string> checks)
    {
        var piece=new PatternPiece { Name="Özgün",Points=[new(0,0),new(10,0),new(10,10)] };
        studio.LoadDocument(new ProjectDocument { Name="Farklı kaydet kontrolü",Pieces=[piece] });
        var source=Path.Combine(work,"save-as-source.tcad");Invoke(studio,"SaveTo",source);
        var sourceBytes=File.ReadAllBytes(source);
        studio.Select(piece.Id);
        var name=(TextBox)typeof(MainWindow).GetField("name",PrivateInstance)!.GetValue(studio)!;
        name.Text="Korunan taslak";
        var before=studio.Document.Clone();
        var target=Path.Combine(work,"save-as-target.tcad");File.WriteAllText(target,"onaylanan sürüm");
        var expected=studio.CaptureSaveAsDestination(target);
        File.WriteAllText(target,"dışarıdan değiştirildi");var external=File.ReadAllBytes(target);
        try { studio.SaveAsTo(target,expected);throw new InvalidOperationException("Changed Save As target was overwritten."); }
        catch(IOException) { }
        Check(File.ReadAllBytes(target).SequenceEqual(external) && File.ReadAllBytes(source).SequenceEqual(sourceBytes)
            && DocumentSession.ContentEquals(before,studio.Document) && name.Text=="Korunan taslak",
            "Save As conflict preserves external target, original project and unapplied draft",checks);
        var appeared=Path.Combine(work,"save-as-appeared.tcad");var missing=studio.CaptureSaveAsDestination(appeared);
        File.WriteAllText(appeared,"yeni dış dosya");var appearedBytes=File.ReadAllBytes(appeared);
        try { studio.SaveAsTo(appeared,missing);throw new InvalidOperationException("Newly appeared Save As target was overwritten."); }
        catch(IOException) { }
        Check(File.ReadAllBytes(appeared).SequenceEqual(appearedBytes) && File.ReadAllBytes(source).SequenceEqual(sourceBytes)
            && DocumentSession.ContentEquals(before,studio.Document) && name.Text=="Korunan taslak",
            "Save As rejects a file created after capture while preserving the draft",checks);
        try { studio.SaveNewCopyTo(appeared);throw new InvalidOperationException("New copy overwrote an existing target."); }
        catch(IOException) { }
        Check(File.ReadAllBytes(appeared).SequenceEqual(appearedBytes) && File.ReadAllBytes(source).SequenceEqual(sourceBytes)
            && DocumentSession.ContentEquals(before,studio.Document) && name.Text=="Korunan taslak",
            "New copy rejects an existing destination and preserves source and draft",checks);
        var same=studio.CaptureSaveAsDestination(source);
        Check(same==ProjectStorage.CaptureVersion(source),"Save As accepts the unchanged currently open path",checks);
        try { studio.CaptureSaveAsDestination(source,true);throw new InvalidOperationException("Compatibility Save As accepted the open path."); }
        catch(InvalidOperationException ex) when(ex.Message.Contains("Mevcut dosyayı korumak")) { }
        try { studio.SaveAsTo(source,same,true);throw new InvalidOperationException("Compatibility Save As committed to the open path."); }
        catch(InvalidOperationException ex) when(ex.Message.Contains("Mevcut dosyayı korumak")) { }
        Check(File.ReadAllBytes(source).SequenceEqual(sourceBytes) && name.Text=="Korunan taslak" && DocumentSession.ContentEquals(before,studio.Document),
            "Compatibility Save As rejects the same path at capture and commit without changing source or draft",checks);
        var link=Path.Combine(work,"save-as-source-link.tcad");
        if(!CreateHardLink(link,source,IntPtr.Zero))throw new IOException("Could not create isolated hard link for verification.");
        try { studio.CaptureSaveAsDestination(link);throw new InvalidOperationException("Source hard link was accepted."); }
        catch(InvalidOperationException ex) when(ex.Message.Contains("aynı dosyadır")) { }
        try { studio.CaptureSaveAsDestination(link,true);throw new InvalidOperationException("Compatibility Save As accepted source hard link."); }
        catch(InvalidOperationException ex) when(ex.Message.Contains("aynı dosyadır")) { }
        Check(File.ReadAllBytes(source).SequenceEqual(sourceBytes) && name.Text=="Korunan taslak",
            "Save As rejects hard link to open source and keeps draft",checks);
        var destination=Path.Combine(work,"save-as-success.tcad");var approved=studio.CaptureSaveAsDestination(destination);
        studio.SaveAsTo(destination,approved);
        Check(ProjectStorage.Load(destination).Pieces.Single().Name=="Korunan taslak" && File.ReadAllBytes(source).SequenceEqual(sourceBytes)
            && studio.Document.Pieces.Single().Name=="Korunan taslak",
            "Save As writes the approved separate target and applies the draft",checks);
        File.WriteAllText(destination,"harici değişiklik");var edited=File.ReadAllBytes(destination);
        try { studio.CaptureSaveAsDestination(destination);throw new InvalidOperationException("Changed open source was accepted."); }
        catch(IOException) { }
        Check(File.ReadAllBytes(destination).SequenceEqual(edited) && studio.Document.Pieces.Single().Name=="Korunan taslak",
            "Save As rejects externally changed currently open path",checks);
        var lastLink=Path.Combine(work,"save-as-last-link.tcad");
        if(!CreateHardLink(lastLink,destination,IntPtr.Zero))throw new IOException("Could not create isolated destination hard link.");
        var linkBytes=File.ReadAllBytes(lastLink);var linkVersion=ProjectStorage.CaptureVersion(lastLink);
        File.Delete(destination);
        var remaining=Path.Combine(work,"save-as-remaining.tcad");File.WriteAllText(remaining,"korunan mevcut hedef");
        var remainingBytes=File.ReadAllBytes(remaining);var remainingVersion=ProjectStorage.CaptureVersion(remaining);var rescueBefore=studio.Document.Clone();
        try { studio.CaptureSaveAsDestination(remaining);throw new Exception("Missing source allowed existing Save As target."); }
        catch(InvalidOperationException) { }
        try { studio.SaveAsTo(remaining,remainingVersion);throw new Exception("Missing source allowed existing Save As target at commit."); }
        catch(InvalidOperationException) { }
        try { studio.CaptureSaveAsDestination(lastLink);throw new Exception("Missing source allowed its remaining hard link."); }
        catch(InvalidOperationException) { }
        try { studio.SaveAsTo(lastLink,linkVersion);throw new Exception("Missing source allowed its remaining hard link at commit."); }
        catch(InvalidOperationException) { }
        Check(File.ReadAllBytes(remaining).SequenceEqual(remainingBytes) && DocumentSession.ContentEquals(rescueBefore,studio.Document),
            "Save As refuses an existing target when source identity cannot be verified",checks);
        Check(File.ReadAllBytes(lastLink).SequenceEqual(linkBytes),"Save As preserves remaining source hard link when original path was removed",checks);
        var rescue=Path.Combine(work,"save-as-rescue.tcad");var rescueVersion=studio.CaptureSaveAsDestination(rescue);
        studio.SaveAsTo(rescue,rescueVersion);
        Check(ProjectStorage.Load(rescue).Pieces.Single().Name=="Korunan taslak" && !File.Exists(destination),
            "Save As rescues open document to a separate path after its source was removed",checks);
        var copy=Path.Combine(work,"new-copy-success.tcad");
        var rescueBytes=File.ReadAllBytes(rescue);
        studio.SaveNewCopyTo(copy);
        Check(ProjectStorage.Load(copy).Pieces.Single().Name=="Korunan taslak" && File.ReadAllBytes(rescue).SequenceEqual(rescueBytes),
            "New copy creates a separate project and preserves the original",checks);
    }
    private static void VerifyDxfPiecePreview(MainWindow studio,string work,string reportPath,List<string> checks)
    {
        var path=Path.Combine(work,"dxf-single-piece-preview.dxf");
        var dxf="0\nSECTION\n2\nHEADER\n9\n$INSUNITS\n70\n5\n0\nENDSEC\n0\nSECTION\n2\nTABLES\n0\nTABLE\n2\nLAYER\n70\n2\n0\nLAYER\n2\nAcik\n70\n0\n62\n7\n0\nLAYER\n2\nKapali\n70\n0\n62\n7\n0\nENDTAB\n0\nENDSEC\n0\nSECTION\n2\nENTITIES\n0\nLINE\n8\nAcik\n10\n1\n20\n2\n11\n3\n21\n2\n0\nLWPOLYLINE\n8\nKapali\n90\n3\n70\n1\n10\n0\n20\n0\n10\n2\n20\n0\n10\n0\n20\n1\n0\nENDSEC\n0\nEOF\n";
        File.WriteAllText(path,dxf);var sourceBytes=File.ReadAllBytes(path);var current=studio.Document.Clone();
        var imported=(DxfImportResult)typeof(MainWindow).GetMethod("PrepareDxfImport",PrivateInstance)!.Invoke(studio,[path,10.0])!;
        var open=imported.Project.Pieces.Single(p=>!p.Closed);var closed=imported.Project.Pieces.Single(p=>p.Closed);
        var window=(Window)typeof(MainWindow).GetMethod("BuildImportPreview",PrivateInstance)!.Invoke(studio,[imported.Project,imported.Warnings,10.0,"preview.dxf",imported.Units])!;
        var root=(FrameworkElement)window.Content;
        try
        {
            Render(window,Path.ChangeExtension(reportPath,".dxf-piece-names.png"),500,380);
            var tabs=Descendants<TabControl>(root).Single();tabs.SelectedIndex=1;root.UpdateLayout();
            var list=Descendants<ListBox>(root).Single(l=>AutomationProperties.GetName(l)=="İçe alınacak tüm parça adları");
            var show=Descendants<Button>(root).Single(b=>Equals(b.Content,"Parçayı gör"));
            Check(!show.IsEnabled,"DXF piece preview requires an explicit selected name",checks);
            list.SelectedItem=open;Check(show.IsEnabled,"DXF piece preview becomes available for the selected piece",checks);
            show.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));root.UpdateLayout();
            var view=Descendants<DxfPiecePreview>(root).Single();
            var status=Descendants<TextBox>(root).Single(t=>AutomationProperties.GetName(t)=="İçe alınacak parça geometrisi ve ölçüleri");
            var summary=Descendants<TextBlock>(root).Single(t=>AutomationProperties.GetName(t)=="Seçili DXF parçasının görünür ölçü özeti");
            Check(view.DisplayedPieceId==open.Id && view.DisplayedPointCount==2 && status.IsReadOnly && status.Focusable
                && status.Text.Contains(open.Name) && status.Text.Contains(open.Id.ToString("D")) && status.Text.Contains("Açık çizgi")
                && status.Text.Contains("Genişlik: 20 mm") && status.Text.Contains("Yükseklik: 0 mm")
                && AutomationProperties.GetName(view).Contains(open.Id.ToString("D")) && AutomationProperties.GetName(view).Contains("Açık · 20 × 0 mm") && AutomationProperties.GetHelpText(view).Contains("fiziksel ölçüm") && summary.Text=="Açık · 20 × 0 mm"
                && AutomationProperties.GetLiveSetting(summary)==AutomationLiveSetting.Polite,
                "DXF selected open piece shows its decoded identity and actual 10x millimetre dimensions",checks);
            foreach(var (width,height) in new[] { (500,380),(484,341) })
            {
                Render(window,Path.ChangeExtension(reportPath,$".dxf-piece-{width}x{height}.png"),width,height);
                var cancel=Descendants<Button>(root).Single(b=>Equals(b.Content,"Vazgeç"));
                var accept=Descendants<Button>(root).Single(b=>Equals(b.Content,"İçe aktar ve projeyi değiştir"));
                Check(view.ActualWidth>=100 && view.ActualHeight>=70 && FullyVisibleInCanvas(view,root,width,height) && FullyVisibleInCanvas(summary,root,width,height)
                    && summary.Text=="Açık · 20 × 0 mm"
                    && FullyVisibleInCanvas(cancel,root,width,height) && FullyVisibleInCanvas(accept,root,width,height) && cancel.IsDefault,
                    $"DXF selected piece and default cancel remain visible in {width}x{height} client viewport",checks);
            }
            tabs.SelectedIndex=1;root.UpdateLayout();list.SelectedItem=closed;
            Check(view.DisplayedPieceId is null && view.DisplayedPointCount==0 && !AutomationProperties.GetName(view).Contains(open.Id.ToString("D")),"Changing DXF selection clears the previous piece preview and accessible identity",checks);
            show.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));root.UpdateLayout();
            Check(view.DisplayedPieceId==closed.Id && status.Text.Contains("Kapalı kontur") && status.Text.Contains("Genişlik: 20 mm")
                && status.Text.Contains("Yükseklik: 10 mm") && status.Text.Contains("min X 0 mm") && status.Text.Contains("max Y 10 mm")
                && !status.Text.Contains("Başlangıç:") && status.Text.Contains(closed.Id.ToString("D")) && summary.Text=="Kapalı · 20 × 10 mm",
                "DXF selected closed piece shows distinct geometry and actual scaled bounds",checks);
            var a=new Point2(99999990.125,-99999990.5);var b=new Point2(99999990.25,-99999990.0);
            var distant=new PatternPiece { Name="Uzak açık çizgi",Closed=false,Points=[a,b] };
            var distantStatus=view.ShowPiece(distant);
            Check(distantStatus.Contains("min X 99999990.125 mm") && distantStatus.Contains("max X 99999990.25 mm")
                && distantStatus.Contains("min Y -99999990.5 mm") && distantStatus.Contains("max Y -99999990 mm")
                && distantStatus.Contains("Başlangıç: X 99999990.125 mm · Y -99999990.5 mm")
                && distantStatus.Contains("Bitiş: X 99999990.25 mm · Y -99999990 mm")
                && distantStatus.Contains("gerçek koordinat yerleşimini göstermez"),
                "DXF piece preview reports precise large-origin bounds and ordered open endpoints",checks);
            var reversed=new PatternPiece { Name="Ters açık çizgi",Closed=false,Points=[b,a] };
            var reverseStatus=view.ShowPiece(reversed);
            Check(reverseStatus.Contains("Başlangıç: X 99999990.25 mm · Y -99999990 mm")
                && reverseStatus.Contains("Bitiş: X 99999990.125 mm · Y -99999990.5 mm") && reversed.Points.SequenceEqual(new[] { b,a }),
                "DXF open endpoint text follows source direction without changing coordinates",checks);
            var tooLarge=new PatternPiece { Closed=false,Points=Enumerable.Range(0,DxfPiecePreview.MaximumPoints+1).Select(i=>new Point2(i,0)).ToList() };
            var error=view.ShowPiece(tooLarge);
            Check(view.DisplayedPieceId is null && view.DisplayedPointCount==0 && error.Contains("sınırı")
                && error.Contains("Kısmi geometri gösterilmedi"),"Oversized DXF piece clears previous preview instead of showing partial geometry",checks);
            Check(File.ReadAllBytes(path).SequenceEqual(sourceBytes) && DocumentSession.ContentEquals(current,studio.Document) && !window.IsVisible,
                "DXF preview is hidden and preserves the source file and current project",checks);
        }
        finally { window.Close(); }
    }
    private static void VerifyDxfDecodedNames(MainWindow studio,string reportPath,List<string> checks)
    {
        var shared="Ön\u202E"+new string('x',900);
        var incoming=new ProjectDocument { Name="İçe alınacak",Pieces=Enumerable.Range(0,10000).Select(i=>new PatternPiece {
            Name=i<2 ? shared : $"Parça {i}",Closed=false,Points=[new(0,0),new(10,0)] }).ToList() };
        var sourceBefore=JsonSerializer.Serialize(studio.Document);var incomingBefore=JsonSerializer.Serialize(incoming);
        var name="kaynak\u202E"+new string('d',150)+".dxf";
        var preview=(Window)typeof(MainWindow).GetMethod("BuildImportPreview",PrivateInstance)!.Invoke(studio,[incoming,new List<string>(),1.0,name,new DxfUnitInfo(4,"mm",1,true)])!;
        var root=(FrameworkElement)preview.Content;
        Render(preview,Path.ChangeExtension(reportPath,".dxf-decoded-names-info.png"),500,380);
        var fullSource=Descendants<TextBox>(root).Single(t=>AutomationProperties.GetName(t)=="Tam kaynak DXF dosya adı");
        Check(fullSource.IsReadOnly && fullSource.Focusable && fullSource.Text.Contains("kaynak\\u202E") && fullSource.Text.EndsWith(".dxf"),
            "DXF review exposes safely escaped, copyable complete source filename",checks);
        var tabs=Descendants<TabControl>(root).Single();tabs.SelectedIndex=1;
        Render(preview,Path.ChangeExtension(reportPath,".dxf-decoded-names-compact.png"),500,380);
        var list=Descendants<ListBox>(root).Single(l=>AutomationProperties.GetName(l)=="İçe alınacak tüm parça adları");
        var selected=Descendants<TextBox>(root).Single(t=>AutomationProperties.GetName(t)=="Seçili içe alınacak parçanın tam adı ve kimliği");
        var cancel=Descendants<Button>(root).Single(b=>Equals(b.Content,"Vazgeç"));
        var accept=Descendants<Button>(root).Single(b=>Equals(b.Content,"İçe aktar ve projeyi değiştir"));
        Check(list.Items.Count==10000 && VirtualizingPanel.GetIsVirtualizing(list) && ScrollViewer.GetCanContentScroll(list)
            && selected.IsReadOnly && selected.Focusable && cancel.IsDefault && FullyVisible(cancel,root) && FullyVisible(accept,root),
            "DXF name review exposes every decoded piece in a virtualized list with safe default actions",checks);
        list.SelectedItem=list.Items[0];var first=selected.Text;
        list.SelectedItem=list.Items[1];var second=selected.Text;
        Check(first.Contains("Ön\\u202E") && first.Contains(incoming.Pieces[0].Id.ToString("D")) && first.Contains(new string('x',900))
            && second.Contains(incoming.Pieces[1].Id.ToString("D")) && !second.Contains(incoming.Pieces[0].Id.ToString("D")),
            "Same decoded names expose distinct identities and full escaped copyable text",checks);
        var listScroll=Descendants<ScrollViewer>(list).First();
        listScroll.ScrollToEnd();root.UpdateLayout();list.Dispatcher.Invoke(()=>{},DispatcherPriority.ContextIdle);root.UpdateLayout();
        var realized=Enumerable.Range(0,list.Items.Count).Count(i=>list.ItemContainerGenerator.ContainerFromIndex(i) is not null);
        Check(list.ItemContainerGenerator.ContainerFromIndex(9999) is not null && realized>0 && realized<100
            && JsonSerializer.Serialize(studio.Document)==sourceBefore && JsonSerializer.Serialize(incoming)==incomingBefore && !preview.IsVisible,
            $"DXF name review realizes offscreen pieces without materializing all rows or changing documents (realized={realized}, last={list.ItemContainerGenerator.ContainerFromIndex(9999) is not null}, source={JsonSerializer.Serialize(studio.Document)==sourceBefore}, incoming={JsonSerializer.Serialize(incoming)==incomingBefore}, hidden={!preview.IsVisible})",checks);
        preview.Close();
    }
    private static void VerifyDxfStrictOptions(MainWindow studio,string work,string reportPath,List<string> checks)
    {
        _=new WindowInteropHelper(studio).EnsureHandle();
        var state=new MainWindow.DxfOptionsState();
        var options=studio.CreateDxfOptionsWindow(state);
        Render(options,Path.ChangeExtension(reportPath,".dxf-strict-options.png"),440,340);
        var root=(FrameworkElement)options.Content;
        var method=Descendants<ComboBox>(root).Single(c=>AutomationProperties.GetName(c)=="DXF okuma yöntemi");
        var cancel=Descendants<Button>(root).Single(b=>Equals(b.Content,"Vazgeç"));
        Check(!state.UseBlockPlacements && method.SelectedIndex==0 && method.Items.Count>=2
            && method.Items.Cast<object>().Any(item=>item.ToString()!.Contains("Kontrollü aktarım"))
            && cancel.IsCancel && cancel.IsDefault && FullyVisible(cancel,root),
            "DXF options offer limited placed-block mode as explicit opt-in and default to cancel",checks);
        options.Close();


        const string drawing="0\nSECTION\n2\nTABLES\n0\nTABLE\n2\nLAYER\n"+
            "0\nLAYER\n2\n0\n70\n0\n62\n7\n290\n1\n6\nCONTINUOUS\n"+
            "0\nLAYER\n2\nKESİM\n70\n0\n62\n7\n290\n1\n6\nCONTINUOUS\n"+
            "0\nENDTAB\n0\nENDSEC\n0\nSECTION\n2\nBLOCKS\n0\nBLOCK\n2\nA\n10\n0\n20\n0\n"+
            "0\nLWPOLYLINE\n8\n0\n90\n3\n70\n1\n10\n0\n20\n0\n10\n10\n20\n0\n10\n0\n20\n10\n"+
            "0\nENDBLK\n0\nENDSEC\n"+
            "0\nSECTION\n2\nENTITIES\n0\nINSERT\n2\nA\n10\n0\n20\n0\n0\nENDSEC\n0\nEOF\n";
        using var stream=new MemoryStream(System.Text.Encoding.UTF8.GetBytes(drawing),writable:false);
        var inspection=DxfFormat.InspectBlockPlacements(stream,DxfTextEncoding.Utf8);
        var inventoryDrawing=drawing.Replace("0\nENDBLK\n", "0\nSPLINE\n70\n8\n0\nENDBLK\n")
            .Replace("0\nINSERT\n", "0\nSPLINE\n70\n8\n0\nINSERT\n");
        using var inventoryStream=new MemoryStream(System.Text.Encoding.UTF8.GetBytes(inventoryDrawing),writable:false);
        var inventoryInspection=DxfFormat.InspectBlockPlacements(inventoryStream,DxfTextEncoding.Utf8);
        var inventoryPicker=studio.CreateDxfLayerSelectionWindow(inventoryInspection,new MainWindow.DxfLayerSelectionState());
        Render(inventoryPicker,Path.ChangeExtension(reportPath,".dxf-source-inventory.png"),500,380);
        var inventoryRoot=(FrameworkElement)inventoryPicker.Content;
        var inventory=Descendants<TextBlock>(inventoryRoot).Single(t=>AutomationProperties.GetAutomationId(t)=="DxfSourceInventory");
        var inventoryScroller=Descendants<ScrollViewer>(inventoryRoot).Single(s=>AutomationProperties.GetName(s)=="DXF katman seçimi ve uyarılar");
        Check(inventoryScroller.IsAncestorOf(inventory) && inventory.Text.Contains("BLOCKS · SPLINE: 1") && inventory.Text.Contains("ENTITIES · SPLINE: 1")
            && inventory.Text.Contains("parça veya yerleştirme sayısı değildir")
            && Descendants<TextBlock>(inventoryRoot).Any(t=>t.Text.Contains("Kodlama ve destek sınırları")),
            "DXF inspection displays section source counts and preserves support warnings",checks);
        inventoryPicker.Close();
        var selection=new MainWindow.DxfLayerSelectionState();
        var picker=studio.CreateDxfLayerSelectionWindow(inspection,selection);
        Render(picker,Path.ChangeExtension(reportPath,".dxf-strict-layer-selection.png"),500,380);
        var pickerRoot=(FrameworkElement)picker.Content;
        var layerList=Descendants<ListBox>(pickerRoot).Single(l=>AutomationProperties.GetName(l)=="Onaylanacak DXF katmanları");
        var tolerance=Descendants<TextBox>(pickerRoot).Single(t=>AutomationProperties.GetName(t)=="DXF koordinat toleransı mm");
        var pickerCancel=Descendants<Button>(pickerRoot).Single(b=>Equals(b.Content,"Vazgeç"));
        Check(layerList.Items.Count==inspection.Layers.Length && layerList.SelectedItems.Count==0
            && selection.SelectedLayers.Count==0 && selection.ComponentToleranceMm==0
            && tolerance.Text.Trim() is "0" or "0,0" or "0.0"
            && pickerCancel.IsCancel && pickerCancel.IsDefault && FullyVisible(pickerCancel,pickerRoot),
            "Placed-block layer selection starts unchecked with zero tolerance and safe cancellation",checks);
        var error=Descendants<TextBlock>(pickerRoot).Single(t=>AutomationProperties.GetAutomationId(t)=="DxfLayerValidationError");
        var pickerScroller=Descendants<ScrollViewer>(pickerRoot).Single(s=>AutomationProperties.GetName(s)=="DXF katman seçimi ve uyarılar");
        var accept=Descendants<Button>(pickerRoot).Single(b=>Equals(b.Content,"Onayla ve okumayı başlat"));
        Check(error.Visibility==Visibility.Collapsed && !pickerScroller.IsAncestorOf(error)
            && AutomationProperties.GetLiveSetting(error)==AutomationLiveSetting.Assertive,
            "DXF layer error starts hidden, accessible and outside the scrollable body",checks);
        // Invalid paths return before assigning DialogResult; these events are
        // safe to raise on the off-screen, deliberately nonmodal window.
        accept.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Render(picker,Path.ChangeExtension(reportPath,".dxf-no-layer-error.png"),440,340);
        Check(error.Visibility==Visibility.Visible && error.Text.Contains("katman",StringComparison.OrdinalIgnoreCase)
            && AutomationProperties.GetName(error).Contains(error.Text,StringComparison.Ordinal)
            && FullyVisibleInCanvas(error,pickerRoot,440,340) && FullyVisibleInCanvas(pickerCancel,pickerRoot,440,340)
            && selection.SelectedLayers.Count==0,
            "Missing DXF layer approval is visible above the actions in the compact window",checks);
        tolerance.Text="-1";
        accept.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Render(picker,Path.ChangeExtension(reportPath,".dxf-tolerance-error.png"),440,340);
        Check(error.Visibility==Visibility.Visible && error.Text.Contains("tolerans",StringComparison.OrdinalIgnoreCase)
            && AutomationProperties.GetName(error).Contains(error.Text,StringComparison.Ordinal)
            && FullyVisibleInCanvas(error,pickerRoot,440,340) && FullyVisibleInCanvas(pickerCancel,pickerRoot,440,340)
            && selection.SelectedLayers.Count==0,
            "Invalid DXF tolerance is visible above the actions in the compact window",checks);
        tolerance.Text="0";
        Check(error.Visibility==Visibility.Collapsed,"Correcting DXF tolerance clears the stale validation error",checks);
        picker.Close();

        var path=Path.Combine(work,"dxf-strict-user-flow.dxf");
        var bytes=System.Text.Encoding.UTF8.GetBytes(drawing);File.WriteAllBytes(path,bytes);
        var oldContext=SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(studio.Dispatcher));
        var original=studio.Document.Clone();var selected=studio.SelectedId;
        try
        {

            studio.ConfirmDxfLayersForVerification=(_,_)=>false;
            var declined=studio.StartInspectedDxfPreparationAsync(path,1,DxfTextEncoding.Utf8);
            PumpUntil(()=>declined.IsCompleted);
            Check(declined.GetAwaiter().GetResult() is null && DocumentSession.ContentEquals(original,studio.Document)
                && studio.SelectedId==selected && File.ReadAllBytes(path).SequenceEqual(bytes),
                "Declining inspected DXF layers preserves source and current document",checks);

            studio.ConfirmDxfLayersForVerification=(_,s)=> { s.SelectedLayers=["0"];s.ComponentToleranceMm=0;return true; };
            var pending=studio.StartInspectedDxfPreparationAsync(path,1,DxfTextEncoding.Utf8);
            PumpUntil(()=>pending.IsCompleted);
            var imported=pending.GetAwaiter().GetResult();
            Check(imported is not null && imported.Project.Pieces.Count==1 && studio.IsDxfPreparationCurrent(imported)
                && DocumentSession.ContentEquals(original,studio.Document) && File.ReadAllBytes(path).SequenceEqual(bytes),
                "Inspected placed-block preparation succeeds without changing the current document",checks);
            var preview=(Window)typeof(MainWindow).GetMethod("BuildImportPreview",PrivateInstance)!.Invoke(studio,[imported!.Project,imported.Warnings,1.0,"strict.dxf",imported.Units])!;
            Render(preview,Path.ChangeExtension(reportPath,".dxf-strict-result.png"),500,380);
            Check(!Descendants<TextBlock>((FrameworkElement)preview.Content).Any(t=>t.Text.Contains("Blok yerleşimleri (INSERT) uygulanmadı")),
                "Successfully placed block preview does not claim that placement was skipped",checks);
            preview.Close();

            studio.ConfirmDxfLayersForVerification=(_,s)=> { s.SelectedLayers=["0"];s.ComponentToleranceMm=0;File.WriteAllText(path,drawing.Replace("10\n10\n20\n0","10\n11\n20\n0"));return true; };
            var changed=studio.StartInspectedDxfPreparationAsync(path,1,DxfTextEncoding.Utf8);
            PumpUntil(()=>changed.IsCompleted);
            try { changed.GetAwaiter().GetResult();throw new InvalidOperationException("Changed source unexpectedly accepted."); }
            catch(InvalidDataException) { }
            Check(DocumentSession.ContentEquals(original,studio.Document) && studio.SelectedId==selected && !studio.DxfImportRunning,
                "Changing a DXF after layer inspection rejects preparation without replacing the document",checks);
            File.WriteAllBytes(path,bytes);

            using var reached=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
            studio.AfterDxfInspectionForVerification=()=> { reached.Set();if(!release.Wait(TimeSpan.FromSeconds(30)))throw new TimeoutException("DXF inspection boundary timed out."); };
            var dxfCancelButton=(Button)typeof(MainWindow).GetField("dxfCancel",PrivateInstance)!.GetValue(studio)!;
            dxfCancelButton.Content="DXF kaynak kontrolünü iptal et";AutomationProperties.SetName(dxfCancelButton,"DXF kaynak kontrolünü iptal et");
            var cancelled=studio.StartInspectedDxfPreparationAsync(path,1,DxfTextEncoding.Utf8);
            try
            {
                PumpUntil(()=>reached.IsSet);
                Check(dxfCancelButton.Visibility==Visibility.Visible && dxfCancelButton.IsEnabled && Equals(dxfCancelButton.Content,"DXF okumayı iptal et") &&
                    AutomationProperties.GetName(dxfCancelButton)=="DXF okumayı iptal et",
                    "Inspected DXF preparation resets visible and accessible cancellation labels",checks);
                Invoke(studio,"RequestDxfCancellation");release.Set();PumpUntil(()=>cancelled.IsCompleted);
                Check(cancelled.GetAwaiter().GetResult() is null && DocumentSession.ContentEquals(original,studio.Document)
                    && studio.SelectedId==selected && File.ReadAllBytes(path).SequenceEqual(bytes),
                    "Cancellation during DXF inspection preserves the source and current document",checks);
            }
            finally { release.Set();PumpUntil(()=>cancelled.IsCompleted);studio.AfterDxfInspectionForVerification=null; }
        }
        finally { studio.ConfirmDxfLayersForVerification=null;studio.AfterDxfInspectionForVerification=null;SynchronizationContext.SetSynchronizationContext(oldContext); }
        VerifyDxfStrictWithoutInsert(studio,work,checks);
    }

    private static void VerifyDxfStrictWithoutInsert(MainWindow studio,string work,List<string> checks)
    {
        const string drawing="0\nSECTION\n2\nTABLES\n0\nTABLE\n2\nLAYER\n"+
            "0\nLAYER\n2\n0\n70\n0\n62\n7\n290\n1\n6\nCONTINUOUS\n0\nENDTAB\n0\nENDSEC\n"+
            "0\nSECTION\n2\nBLOCKS\n0\nBLOCK\n2\nKULLANILMAYAN\n10\n0\n20\n0\n"+
            "0\nLWPOLYLINE\n8\n0\n90\n3\n70\n1\n10\n100\n20\n100\n10\n110\n20\n100\n10\n100\n20\n110\n"+
            "0\nENDBLK\n0\nENDSEC\n0\nSECTION\n2\nENTITIES\n"+
            "0\nLWPOLYLINE\n8\n0\n90\n3\n70\n1\n10\n0\n20\n0\n10\n10\n20\n0\n10\n0\n20\n10\n0\nENDSEC\n0\nEOF\n";
        var path=Path.Combine(work,"dxf-strict-without-insert.dxf");
        var bytes=System.Text.Encoding.UTF8.GetBytes(drawing);File.WriteAllBytes(path,bytes);
        var original=studio.Document.Clone();var selected=studio.SelectedId;
        var oldContext=SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(studio.Dispatcher));
        try
        {
            var inspected=false;
            studio.ConfirmDxfLayersForVerification=(inspection,selection)=>
            {
                inspected=!inspection.HasModelInsert && inspection.Layers.Any(layer=>layer.Name=="0");
                selection.SelectedLayers=["0"];selection.ComponentToleranceMm=0;return true;
            };
            var pending=studio.StartInspectedDxfPreparationAsync(path,1,DxfTextEncoding.Utf8);
            PumpUntil(()=>pending.IsCompleted);
            var imported=pending.GetAwaiter().GetResult();
            Check(inspected && imported is not null && imported.Project.Pieces.Count==1
                && imported.Project.Pieces[0].Points.Contains(new Point2(10,0))
                && !imported.Project.Pieces[0].Points.Contains(new Point2(100,100))
                && studio.IsDxfPreparationCurrent(imported) && DocumentSession.ContentEquals(original,studio.Document)
                && studio.SelectedId==selected && File.ReadAllBytes(path).SequenceEqual(bytes),
                "Controlled DXF preparation imports model contours without INSERT or unused block definitions",checks);

            studio.ConfirmDxfLayersForVerification=(_,selection)=>
            {
                selection.SelectedLayers=["0"];selection.ComponentToleranceMm=0;
                File.WriteAllText(path,drawing.Replace("10\n10\n20\n0","10\n11\n20\n0"));return true;
            };
            var changed=studio.StartInspectedDxfPreparationAsync(path,1,DxfTextEncoding.Utf8);
            PumpUntil(()=>changed.IsCompleted);
            try { changed.GetAwaiter().GetResult();throw new InvalidOperationException("Changed no-INSERT DXF unexpectedly accepted."); }
            catch(InvalidDataException) { }
            Check(DocumentSession.ContentEquals(original,studio.Document) && studio.SelectedId==selected && !studio.DxfImportRunning,
                "Changed no-INSERT DXF is rejected without replacing the document",checks);
            File.WriteAllBytes(path,bytes);

            using var reached=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
            studio.AfterDxfInspectionForVerification=()=> { reached.Set();if(!release.Wait(TimeSpan.FromSeconds(30)))throw new TimeoutException("DXF inspection boundary timed out."); };
            var cancelled=studio.StartInspectedDxfPreparationAsync(path,1,DxfTextEncoding.Utf8);
            try
            {
                PumpUntil(()=>reached.IsSet);Invoke(studio,"RequestDxfCancellation");release.Set();PumpUntil(()=>cancelled.IsCompleted);
                Check(cancelled.GetAwaiter().GetResult() is null && DocumentSession.ContentEquals(original,studio.Document)
                    && studio.SelectedId==selected && File.ReadAllBytes(path).SequenceEqual(bytes),
                    "Cancelling no-INSERT DXF inspection preserves the source and current document",checks);
            }
            finally { release.Set();PumpUntil(()=>cancelled.IsCompleted);studio.AfterDxfInspectionForVerification=null; }
        }
        finally { studio.ConfirmDxfLayersForVerification=null;studio.AfterDxfInspectionForVerification=null;SynchronizationContext.SetSynchronizationContext(oldContext); }
    }

    private static void VerifyDxfPlacementRetry(MainWindow studio,string work,string reportPath,List<string> checks)
    {
        const string drawing="0\nSECTION\n2\nTABLES\n0\nTABLE\n2\nLAYER\n"+
            "0\nLAYER\n2\n0\n70\n0\n62\n7\n290\n1\n6\nCONTINUOUS\n0\nENDTAB\n0\nENDSEC\n"+
            "0\nSECTION\n2\nBLOCKS\n0\nBLOCK\n2\nA\n10\n0\n20\n0\n"+
            "0\nLWPOLYLINE\n8\n0\n90\n3\n70\n1\n10\n0\n20\n0\n10\n10\n20\n0\n10\n0\n20\n10\n"+
            "0\nENDBLK\n0\nENDSEC\n0\nSECTION\n2\nENTITIES\n0\nINSERT\n2\nA\n10\n20\n20\n30\n0\nENDSEC\n0\nEOF\n";
        var path=Path.Combine(work,"dxf-placement-retry.dxf");
        var bytes=System.Text.Encoding.UTF8.GetBytes(drawing);File.WriteAllBytes(path,bytes);
        var oldContext=SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(studio.Dispatcher));
        var original=studio.Document.Clone();var selected=studio.SelectedId;
        var preparedField=typeof(MainWindow).GetField("preparedDxf",PrivateInstance)!;
        DxfImportResult Legacy()
        {
            var pending=studio.StartDxfPreparationAsync(path,1,DxfTextEncoding.Utf8);
            PumpUntil(()=>pending.IsCompleted);
            return pending.GetAwaiter().GetResult() ?? throw new InvalidOperationException("Legacy DXF preparation unexpectedly cancelled.");
        }
        Window Preview(DxfImportResult result,Action retry) => (Window)typeof(MainWindow)
            .GetMethod("BuildImportPreviewCapturedWithState",PrivateInstance)!.Invoke(studio,
                [result.Project,result.Warnings,1.0,"retry.dxf",result.Units,result.SourceSha256,result.SourceByteLength,retry,result.SkippedRelevantInsertEntities])!;
        try
        {
            var legacy=Legacy();
            var preview=Preview(legacy,()=>{});
            Render(preview,Path.ChangeExtension(reportPath,".dxf-placement-retry.png"),500,380);
            var root=(FrameworkElement)preview.Content;
            Check(Descendants<Button>(root).Count(b=>Equals(b.Content,"Yerleşimleri kontrollü oku…"))==1
                && Descendants<Button>(root).Single(b=>Equals(b.Content,"Vazgeç")).IsDefault
                && !Descendants<Button>(root).Any(b=>Equals(b.Content,"İçe aktar ve projeyi değiştir") && b.IsEnabled),
                "Skipped INSERT review offers a controlled placement retry and no enabled unsafe apply action",checks);
            preview.Close();

            var session=(DocumentSession)typeof(MainWindow).GetField("session",PrivateInstance)!.GetValue(studio)!;
            var undoCount=session.UndoCount;var redoCount=session.RedoCount;var wasDirty=session.IsDirty;
            var discardCalls=0;
            studio.ConfirmDxfReviewForVerification=_=>true;
            studio.DxfDiscardDecisionForVerification=()=> { discardCalls++;return (true,false); };
            Check(legacy.SkippedInsertEntities is true && legacy.SkippedRelevantInsertEntities is true,"Standard INSERT parser identifies skipped placements independently of warning text",checks);
            foreach(var skipped in new bool?[] { true,null })
            {
                // Deliberately suppress textual warnings: enforcement must use the
                // structured parser state, not localized or omitted messages.
                var uncertain=legacy with { Warnings=[],SkippedRelevantInsertEntities=skipped };
                preparedField.SetValue(studio,uncertain);
                var uncertainPreview=Preview(uncertain,()=>{});
                Check(!Descendants<Button>((FrameworkElement)uncertainPreview.Content).Any(b=>Equals(b.Content,"İçe aktar ve projeyi değiştir") && b.IsEnabled),
                    $"INSERT state {skipped?.ToString() ?? "unknown"} disables import independently of warning text",checks);
                uncertainPreview.Close();
                var bypass=studio.ReviewPreparedDxfAsync();PumpUntil(()=>bypass.IsCompleted);bypass.GetAwaiter().GetResult();
                Check(discardCalls==0 && studio.IsDxfPreparationCurrent(uncertain)
                    && DocumentSession.ContentEquals(original,studio.Document) && studio.SelectedId==selected
                    && session.UndoCount==undoCount && session.RedoCount==redoCount && session.IsDirty==wasDirty
                    && File.ReadAllBytes(path).SequenceEqual(bytes),
                    $"Programmatic acceptance of INSERT state {skipped?.ToString() ?? "unknown"} preserves document and history",checks);
            }
            preparedField.SetValue(studio,legacy);
            studio.ConfirmDxfReviewForVerification=null;studio.DxfDiscardDecisionForVerification=null;

            var noInsert=new DxfImportResult(legacy.Project,["Atlanan desteklenmeyen DXF verileri: ARC"])
                { Units=legacy.Units,SourceSha256=legacy.SourceSha256,SourceByteLength=legacy.SourceByteLength,SkippedInsertEntities=false,SkippedRelevantInsertEntities=false };
            var ordinary=Preview(noInsert,()=>{});
            Render(ordinary,Path.ChangeExtension(reportPath,".dxf-placement-no-retry.png"),500,380);
            Check(!Descendants<Button>((FrameworkElement)ordinary.Content).Any(b=>Equals(b.Content,"Yerleşimleri kontrollü oku…"))
                && Descendants<Button>((FrameworkElement)ordinary.Content).Any(b=>Equals(b.Content,"İçe aktar ve projeyi değiştir") && b.IsEnabled),
                "Review permits ordinary import and does not offer placement retry when INSERT was not skipped",checks);
            ordinary.Close();

            studio.ConfirmDxfLayersForVerification=(_,_)=>false;
            var declined=studio.RetryPreparedDxfPlacementsAsync(legacy);PumpUntil(()=>declined.IsCompleted);declined.GetAwaiter().GetResult();
            Check(studio.IsDxfPreparationCurrent(legacy) && DocumentSession.ContentEquals(original,studio.Document)
                && studio.SelectedId==selected && File.ReadAllBytes(path).SequenceEqual(bytes),
                "Declining placement retry preserves the previous review, source and document",checks);

            var selectionCount=0;
            studio.ConfirmDxfLayersForVerification=(_,_)=> { selectionCount++;return false; };
            File.WriteAllText(path,drawing.Replace("10\n20\n20\n30","10\n21\n20\n30"));
            var changed=studio.RetryPreparedDxfPlacementsAsync(legacy);PumpUntil(()=>changed.IsCompleted);changed.GetAwaiter().GetResult();
            Check(selectionCount==0 && DocumentSession.ContentEquals(original,studio.Document) && studio.SelectedId==selected,
                "Changed source prevents placement retry before layer approval and preserves the document",checks);
            File.WriteAllBytes(path,bytes);

            legacy=Legacy();selectionCount=0;
            studio.ConfirmDxfLayersForVerification=(_,_)=> { selectionCount++;return false; };
            var replacement=System.Text.Encoding.UTF8.GetBytes(drawing.Replace("10\n20\n20\n30","10\n21\n20\n30"));
            if(replacement.Length!=bytes.Length)throw new InvalidOperationException("DXF retry race fixture must preserve byte length.");
            studio.AfterDxfSourceCheckForVerification=()=>File.WriteAllBytes(path,replacement);
            try
            {
                var raced=studio.RetryPreparedDxfPlacementsAsync(legacy);PumpUntil(()=>raced.IsCompleted);raced.GetAwaiter().GetResult();
                Check(selectionCount==0 && studio.IsDxfPreparationCurrent(legacy)
                    && DocumentSession.ContentEquals(original,studio.Document) && studio.SelectedId==selected
                    && File.ReadAllBytes(path).SequenceEqual(replacement),
                    "Same-size source mutation after retry source check is rejected before layer approval",checks);
            }
            finally { studio.AfterDxfSourceCheckForVerification=null;File.WriteAllBytes(path,bytes); }

            legacy=Legacy();
            studio.ConfirmDxfLayersForVerification=(_,s)=> { s.SelectedLayers=["0"];s.ComponentToleranceMm=0;return true; };
            var success=studio.RetryPreparedDxfPlacementsAsync(legacy);PumpUntil(()=>success.IsCompleted);success.GetAwaiter().GetResult();
            var strict=preparedField.GetValue(studio) as DxfImportResult;
            Check(strict is not null && !ReferenceEquals(strict,legacy) && studio.IsDxfPreparationCurrent(strict)
                && strict.SkippedInsertEntities is false && strict.SkippedRelevantInsertEntities is false
                && strict.Project.Pieces.Count==1 && strict.Project.Pieces[0].Points.Contains(new Point2(20,30))
                && DocumentSession.ContentEquals(original,studio.Document) && File.ReadAllBytes(path).SequenceEqual(bytes),
                "Controlled retry prepares placed geometry without changing the current document or source",checks);
            var strictPreview=Preview(strict!,()=>{});
            Render(strictPreview,Path.ChangeExtension(reportPath,".dxf-placement-retry-result.png"),500,380);
            Check(!Descendants<Button>((FrameworkElement)strictPreview.Content).Any(b=>Equals(b.Content,"Yerleşimleri kontrollü oku…"))
                && Descendants<Button>((FrameworkElement)strictPreview.Content).Any(b=>Equals(b.Content,"İçe aktar ve projeyi değiştir") && b.IsEnabled),
                "Successful controlled placement review permits import without an unnecessary retry",checks);
            strictPreview.Close();

            legacy=Legacy();
            studio.ConfirmDxfLayersForVerification=(_,s)=> { s.SelectedLayers=[];s.ComponentToleranceMm=0;return true; };
            var failed=studio.RetryPreparedDxfPlacementsAsync(legacy);PumpUntil(()=>failed.IsCompleted);failed.GetAwaiter().GetResult();
            Check(studio.IsDxfPreparationCurrent(legacy) && DocumentSession.ContentEquals(original,studio.Document)
                && File.ReadAllBytes(path).SequenceEqual(bytes),
                "Failed controlled placement retry restores the previous review without replacing the document",checks);

            legacy=Legacy();
            using var reached=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
            studio.AfterDxfInspectionForVerification=()=> { reached.Set();if(!release.Wait(TimeSpan.FromSeconds(30)))throw new TimeoutException("DXF retry inspection boundary timed out."); };
            var cancelled=studio.RetryPreparedDxfPlacementsAsync(legacy);
            try
            {
                PumpUntil(()=>reached.IsSet);Invoke(studio,"RequestDxfCancellation");release.Set();PumpUntil(()=>cancelled.IsCompleted);cancelled.GetAwaiter().GetResult();
                Check(studio.IsDxfPreparationCurrent(legacy) && DocumentSession.ContentEquals(original,studio.Document)
                    && File.ReadAllBytes(path).SequenceEqual(bytes),
                    "Cancelling controlled placement retry restores the previous review and preserves the source",checks);
            }
            finally { release.Set();PumpUntil(()=>cancelled.IsCompleted);studio.AfterDxfInspectionForVerification=null; }

            // An explicitly hidden INSERT is ignored, while independent model
            // geometry remains importable. Do not include block definitions:
            // direct block geometry alongside ignored INSERT requires review.
            const string hiddenDrawing="0\nSECTION\n2\nENTITIES\n"+
                "0\nLWPOLYLINE\n8\n0\n90\n3\n70\n1\n10\n0\n20\n0\n10\n10\n20\n0\n10\n0\n20\n10\n"+
                "0\nINSERT\n60\n1\n2\nHIDDEN\n10\n20\n20\n30\n0\nENDSEC\n0\nEOF\n";
            var hiddenPath=Path.Combine(work,"dxf-hidden-insert.dxf");
            var hiddenBytes=System.Text.Encoding.UTF8.GetBytes(hiddenDrawing);File.WriteAllBytes(hiddenPath,hiddenBytes);
            var hiddenPending=studio.StartDxfPreparationAsync(hiddenPath,1,DxfTextEncoding.Utf8);
            PumpUntil(()=>hiddenPending.IsCompleted);
            var hidden=hiddenPending.GetAwaiter().GetResult() ?? throw new InvalidOperationException("Hidden INSERT preparation unexpectedly cancelled.");
            var hiddenPreview=Preview(hidden,()=>{});
            Check(hidden.SkippedInsertEntities is true && hidden.SkippedRelevantInsertEntities is false && hidden.ImportedBlockDefinitions is false
                && Descendants<Button>((FrameworkElement)hiddenPreview.Content).Any(b=>Equals(b.Content,"İçe aktar ve projeyi değiştir") && b.IsEnabled),
                "Explicitly hidden INSERT with no imported block geometry permits normal review",checks);
            hiddenPreview.Close();
            studio.ConfirmDxfReviewForVerification=_=>true;
            studio.DxfDiscardDecisionForVerification=()=> (true,false);
            var applyHidden=studio.ReviewPreparedDxfAsync();PumpUntil(()=>applyHidden.IsCompleted);applyHidden.GetAwaiter().GetResult();
            var expectedHidden=hidden.Project.Clone();expectedHidden.ImportProvenance=new DxfImportProvenance {
                SourceFileName=Path.GetFileName(hiddenPath),SourceSha256=hidden.SourceSha256!,SourceByteLength=hidden.SourceByteLength!.Value,
                MillimetersPerUnit=1,Warnings=[..hidden.Warnings],PieceIds=expectedHidden.Pieces.Select(p=>p.Id).ToList() };
            Check(DocumentSession.ContentEquals(expectedHidden,studio.Document) && studio.Document.Pieces.Count==1
                && File.ReadAllBytes(hiddenPath).SequenceEqual(hiddenBytes),
                "Explicitly hidden INSERT permits applying independent model geometry without changing the source",checks);
            studio.LoadDocument(original);studio.Select(selected);
        }
        finally
        {
            studio.ConfirmDxfLayersForVerification=null;studio.AfterDxfInspectionForVerification=null;
            studio.ConfirmDxfReviewForVerification=null;studio.DxfDiscardDecisionForVerification=null;
            SynchronizationContext.SetSynchronizationContext(oldContext);
        }
    }

    private static void VerifyDxfBulgeOptions(MainWindow studio,string reportPath,List<string> checks)
    {
        var before=studio.Document.Clone();
        var window=new DxfBulgeOptionsWindow(studio);
        try
        {
            var root=(FrameworkElement)window.Content;
            Render(window,Path.ChangeExtension(reportPath,".dxf-bulge-options.png"),610,500);
            Check(window.EnableApproximation.IsChecked==false && !window.ToleranceInput.IsEnabled
                && window.ToleranceInput.Text=="" && window.SelectedToleranceMm is null
                && window.TryAccept(out var defaultValue) && defaultValue is null,
                "DXF bulge option defaults to disabled without a tolerance",checks);
            Check(AutomationProperties.GetName(window.EnableApproximation).Contains("bulge",StringComparison.OrdinalIgnoreCase)
                && AutomationProperties.GetName(window.ToleranceInput).Contains("milimetre")
                && AutomationProperties.GetLiveSetting(window.Error)==AutomationLiveSetting.Assertive
                && AutomationProperties.GetHelpText(window.ToleranceInput).Contains("üretim toleransı değildir"),
                "DXF bulge controls and validation expose accessible names and warning",checks);
            Check(Descendants<TextBlock>(root).Any(text=>text.Text.Contains("parametrik eğri saklanmaz") && text.Text.Contains("kesin geometrik hata sınırı"))
                && Descendants<TextBlock>(root).Any(text=>text.Text.Contains("BLOCK/INSERT") && text.Text.Contains("3B") && text.Text.Contains("ARC/CIRCLE")),
                "DXF bulge option explains approximation and unsupported geometry",checks);
            window.EnableApproximation.IsChecked=true;
            Check(window.ToleranceInput.IsEnabled,"Opting in enables the tolerance input",checks);
            foreach(var invalid in new[]{"", "0", "-1", "NaN", "Infinity", "1e999"})
            {
                window.ToleranceInput.Text=invalid;
                Check(!window.TryAccept(out var rejected) && rejected is null && window.Error.Visibility==Visibility.Visible
                    && window.Error.Text.Contains("Sıfırdan büyük"),$"DXF bulge option rejects invalid tolerance '{invalid}'",checks);
            }
            window.ToleranceInput.Text="0,25";
            Check(window.TryAccept(out var positive) && positive==0.25 && window.Error.Visibility==Visibility.Collapsed,
                "DXF bulge option accepts an explicitly entered positive millimetre value",checks);
            window.EnableApproximation.IsChecked=false;
            Check(!window.ToleranceInput.IsEnabled && window.TryAccept(out var disabled) && disabled is null,
                "Turning approximation off returns a null tolerance despite previous input",checks);
            var cancel=Descendants<Button>(root).Single(button=>Equals(button.Content,"Vazgeç"));
            bool nonModal=false;
            // This verification window remains hidden: WPF rejects setting a
            // DialogResult when it has not been opened by ShowDialog.
            try { cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
            catch(InvalidOperationException) { nonModal=true; }
            Check(nonModal && cancel.IsCancel && cancel.IsDefault && window.SelectedToleranceMm is null && DocumentSession.ContentEquals(before,studio.Document),
                "Hidden cancel handler preserves options and project; actual modal closure remains untested",checks);
        }
        finally { window.Close(); }
    }

    private static void VerifyDxfEncodingChoice(MainWindow studio,string work,string reportPath,List<string> checks)
    {
        _=new WindowInteropHelper(studio).EnsureHandle();
        var state=new MainWindow.DxfOptionsState();var options=studio.CreateDxfOptionsWindow(state);
        var root=(FrameworkElement)options.Content;
        Render(options,Path.ChangeExtension(reportPath,".dxf-encoding-options.png"),440,340);
        var encoding=Descendants<ComboBox>(root).Single(c=>AutomationProperties.GetName(c)=="DXF metin kodlaması");
        var cancel=Descendants<Button>(root).Single(b=>Equals(b.Content,"Vazgeç"));
        var read=Descendants<Button>(root).Single(b=>Equals(b.Content,"DXF okumayı başlat"));
        Check(encoding.SelectedIndex==0 && state.Encoding==DxfTextEncoding.Utf8 && encoding.Items.Count==3
            && encoding.Items[1]?.ToString()=="Türkçe Windows-1254" && encoding.Items[2]?.ToString()=="Batı Avrupa Windows-1252" && cancel.IsDefault && cancel.IsCancel && !read.IsDefault
            && FullyVisible(cancel,root) && FullyVisible(read,root) && Descendants<TextBlock>(root).Any(t=>t.Text.Contains("otomatik tahmin edilmez") && t.Text.Contains("kaynak dosyayı değiştirmez")),
            "DXF options default to UTF-8 and cancellation and explain explicit encoding selection",checks);
        options.Close();
        var westernState=new MainWindow.DxfOptionsState();var westernOptions=studio.CreateDxfOptionsWindow(westernState);
        var westernRoot=(FrameworkElement)westernOptions.Content;
        Render(westernOptions,Path.ChangeExtension(reportPath,".dxf-windows1252-options.png"),440,340);
        var westernChoice=Descendants<ComboBox>(westernRoot).Single(c=>AutomationProperties.GetName(c)=="DXF metin kodlaması");
        var westernRead=Descendants<Button>(westernRoot).Single(b=>Equals(b.Content,"DXF okumayı başlat"));
        westernOptions.Dispatcher.BeginInvoke(new Action(()=> { westernChoice.SelectedIndex=2;westernRead.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }));
        var westernAccepted=westernOptions.ShowDialog();
        Check(westernAccepted==true && westernState.Encoding==DxfTextEncoding.Windows1252,
            "DXF options commit explicit Windows-1252 selection",checks);
        var content="0\nSECTION\n2\nHEADER\n9\n$INSUNITS\n70\n4\n0\nENDSEC\n0\nSECTION\n2\nTABLES\n0\nTABLE\n2\nLAYER\n70\n1\n0\nLAYER\n2\nÖn\n70\n0\n62\n7\n0\nENDTAB\n0\nENDSEC\n0\nSECTION\n2\nENTITIES\n0\nLINE\n8\nÖn\n10\n0\n20\n0\n11\n10\n21\n0\n0\nENDSEC\n0\nEOF\n";
        var path=Path.Combine(work,"dxf-turkish-encoding.dxf");File.WriteAllBytes(path,System.Text.Encoding.Latin1.GetBytes(content));var bytes=File.ReadAllBytes(path);
        var source=studio.Document.Clone();var selected=studio.SelectedId;var oldContext=SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(studio.Dispatcher));
        try
        {
        var westernPath=Path.Combine(work,"dxf-western-encoding.dxf");
            File.WriteAllBytes(westernPath,System.Text.Encoding.Latin1.GetBytes(content.Replace("Ön","pièceÐ")));
            var westernBytes=File.ReadAllBytes(westernPath);
            var westernPending=studio.StartDxfPreparationAsync(westernPath,1,westernState.Encoding);PumpUntil(()=>westernPending.IsCompleted);
            var westernImport=westernPending.GetAwaiter().GetResult();
            Check(westernImport is not null && westernImport.Project.Pieces.Single().Name=="pièceÐ"
                && File.ReadAllBytes(westernPath).SequenceEqual(westernBytes) && DocumentSession.ContentEquals(source,studio.Document),
                "Windows-1252 prepares the decoded layer without changing source or workspace",checks);
            var westernPreview=(Window)typeof(MainWindow).GetMethod("BuildImportPreview",PrivateInstance)!.Invoke(studio,[westernImport!.Project,westernImport.Warnings,1.0,"dxf-western-encoding.dxf",westernImport.Units])!;
            Render(westernPreview,Path.ChangeExtension(reportPath,".dxf-windows1252-review.png"),500,380);
            var westernPreviewRoot=(FrameworkElement)westernPreview.Content;
            var westernNotice=Descendants<TextBlock>(westernPreviewRoot).Single(t=>t.Text.StartsWith("Windows-1252 seçildi"));
            var westernCancel=Descendants<Button>(westernPreviewRoot).Single(b=>Equals(b.Content,"Vazgeç"));
            Check(WarningReachable(westernNotice,westernPreviewRoot) && westernCancel.IsDefault && FullyVisible(westernCancel,westernPreviewRoot)
                && Descendants<TextBlock>(westernPreviewRoot).Any(t=>t.Text.Contains("Seçilen metin kodlaması: Batı Avrupa Windows-1252")),
                "Windows-1252 review identifies selected encoding and exposes warning",checks);
            westernPreview.Close();
            var invalid=studio.StartDxfPreparationAsync(path,1,DxfTextEncoding.Utf8);PumpUntil(()=>invalid.IsCompleted);
            try { invalid.GetAwaiter().GetResult();throw new InvalidOperationException("Invalid UTF-8 unexpectedly accepted."); }
            catch(InvalidDataException) { }
            Check(File.ReadAllBytes(path).SequenceEqual(bytes) && DocumentSession.ContentEquals(source,studio.Document) && studio.SelectedId==selected,
                "Strict UTF-8 rejection preserves source bytes and current workspace",checks);
            var pending=studio.StartDxfPreparationAsync(path,1,DxfTextEncoding.Windows1254);PumpUntil(()=>pending.IsCompleted);
            var imported=pending.GetAwaiter().GetResult();
            Check(imported is not null && imported.Project.Pieces.Single().Name=="Ön" && imported.Warnings.Any(w=>w.StartsWith("DXF metni kullanıcının seçtiği Windows-1254"))
                && File.ReadAllBytes(path).SequenceEqual(bytes) && DocumentSession.ContentEquals(source,studio.Document) && studio.SelectedId==selected,
                "Explicit Windows-1254 prepares Turkish name without changing source file or workspace",checks);
            var preview=(Window)typeof(MainWindow).GetMethod("BuildImportPreview",PrivateInstance)!.Invoke(studio,[imported!.Project,imported.Warnings,1.0,"dxf-turkish-encoding.dxf",imported.Units])!;
            Render(preview,Path.ChangeExtension(reportPath,".dxf-windows1254-review.png"),500,380);
            var previewRoot=(FrameworkElement)preview.Content;
            var notice=Descendants<TextBlock>(previewRoot).Single(t=>t.Text.StartsWith("Windows-1254 seçildi"));
            var previewCancel=Descendants<Button>(previewRoot).Single(b=>Equals(b.Content,"Vazgeç"));
            Check(WarningReachable(notice,previewRoot) && previewCancel.IsDefault && FullyVisible(previewCancel,previewRoot)
                && Descendants<TextBlock>(previewRoot).Any(t=>t.Text.Contains("Seçilen metin kodlaması: Türkçe Windows-1254")),
                "Windows-1254 import review warns of uncertain spelling and defaults to cancellation",checks);
            Descendants<TabControl>(previewRoot).Single().SelectedIndex=1;previewRoot.UpdateLayout();
            var decodedList=Descendants<ListBox>(previewRoot).Single(l=>AutomationProperties.GetName(l)=="İçe alınacak tüm parça adları");
            var decodedDetail=Descendants<TextBox>(previewRoot).Single(t=>AutomationProperties.GetName(t)=="Seçili içe alınacak parçanın tam adı ve kimliği");
            decodedList.SelectedIndex=0;
            Check(decodedDetail.Text.Contains("Ön") && decodedDetail.Text.Contains(imported.Project.Pieces[0].Id.ToString("D")) && DocumentSession.ContentEquals(source,studio.Document),
                "Windows-1254 decoded piece name and identity can be inspected before changing the project",checks);
            preview.Close();
            var headerWarnings=new List<string> { "DXF sürüm başlığı UTF-8 bekleyen 2007 veya sonrası; seçilen Windows-1254 farklıdır. Kodlama değiştirilmedi; kaynak dosyayı doğrulayın." };
            var headerPreview=(Window)typeof(MainWindow).GetMethod("BuildImportPreview",PrivateInstance)!.Invoke(studio,[imported.Project,headerWarnings,1.0,"header.dxf",new DxfUnitInfo(4,"mm",1,true)])!;
            Render(headerPreview,Path.ChangeExtension(reportPath,".dxf-encoding-header.png"),500,380);
            var headerRoot=(FrameworkElement)headerPreview.Content;
            var headerNotice=Descendants<TextBlock>(headerRoot).Single(t=>t.Text.Contains("DXF sürüm veya kod sayfası başlığıyla seçimin uyumu doğrulanamadı"));
            var headerCancel=Descendants<Button>(headerRoot).Single(b=>Equals(b.Content,"Vazgeç"));
            Check(WarningReachable(headerNotice,headerRoot) && headerCancel.IsDefault && FullyVisible(headerCancel,headerRoot),
                "DXF encoding header uncertainty is pinned and defaults to cancel even when units are reliable",checks);
            headerPreview.Close();
            using var reached=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
            studio.AfterDxfPreparationForVerification=()=> { reached.Set();release.Wait(); };
            var late=studio.StartDxfPreparationAsync(path,1,DxfTextEncoding.Windows1254);
            try
            {
                PumpUntil(()=>reached.IsSet);Invoke(studio,"RequestDxfCancellation");release.Set();PumpUntil(()=>late.IsCompleted);
                Check(late.GetAwaiter().GetResult() is null && File.ReadAllBytes(path).SequenceEqual(bytes)
                    && DocumentSession.ContentEquals(source,studio.Document) && studio.SelectedId==selected,
                    "Late Windows-1254 import cancellation preserves file, selection and workspace",checks);
            }
            finally { release.Set();PumpUntil(()=>late.IsCompleted);studio.AfterDxfPreparationForVerification=null; }
        }
        finally { studio.AfterDxfPreparationForVerification=null;SynchronizationContext.SetSynchronizationContext(oldContext); }
    }
    private static void VerifySelectedDraftConflict(MainWindow studio,List<string> checks)
    {
        var first=new PatternPiece { Name="Ön\nparça",Points=[new(0,0),new(10,0),new(0,10)] };
        var second=new PatternPiece { Name="Ön\nparça",Points=[new(20,0),new(30,0),new(20,10)] };
        studio.LoadDocument(new ProjectDocument { Pieces=[first,second] });studio.Select(first.Id);
        var name=(TextBox)typeof(MainWindow).GetField("name",PrivateInstance)!.GetValue(studio)!;
        var x=(TextBox)typeof(MainWindow).GetField("x",PrivateInstance)!.GetValue(studio)!;
        var y=(TextBox)typeof(MainWindow).GetField("y",PrivateInstance)!.GetValue(studio)!;
        var conflict=(TextBox)typeof(MainWindow).GetField("propertyDraftConflict",PrivateInstance)!.GetValue(studio)!;
        name.Text="Taslak adı";
        Check(conflict.Visibility==Visibility.Collapsed && x.Text=="0" && y.Text=="0","Matching selected draft has no conflict warning",checks);
        studio.Commit("Taslak sırasında taşıma",()=> {
            var piece=studio.Document.Pieces.Single(p=>p.Id==first.Id);
            piece.Points=TekstilCAD.Core.Geometry.Translate(piece.Points,10.25,-3.5);
        });
        var moved=studio.Document.Clone();
        Check(conflict.Visibility==Visibility.Visible && conflict.IsReadOnly && conflict.Focusable && conflict.AcceptsReturn
            && AutomationProperties.GetName(conflict)=="Seçili parça taslak çakışması ve mevcut değerler"
            && AutomationProperties.GetLiveSetting(conflict)==AutomationLiveSetting.Polite && conflict.Text.Contains("Ön\\u000Aparça")
            && conflict.Text.Contains("Mevcut X: 10.25 mm") && conflict.Text.Contains("Mevcut Y: -3.5 mm")
            && conflict.Text.Contains("taslak henüz uygulanmadı") && name.Text=="Taslak adı" && x.Text=="0" && y.Text=="0",
            "Stale selected draft exposes copyable current name and position without changing its inputs",checks);
        studio.Select(second.Id);
        Check(conflict.Visibility==Visibility.Collapsed && studio.SelectedId==second.Id && DocumentSession.ContentEquals(moved,studio.Document),
            "Another same-named selection does not inherit the first piece conflict",checks);
        studio.Select(first.Id);
        Check(conflict.Visibility==Visibility.Visible && name.Text=="Taslak adı" && studio.SelectedId==first.Id,
            "Returning to the conflicted piece retains its draft and warning",checks);
        Invoke(studio,"Undo");
        Check(conflict.Visibility==Visibility.Collapsed && name.Text=="Taslak adı" && studio.SelectedId==first.Id
            && TekstilCAD.Core.Geometry.Bounds(studio.Selected!.Points).MinX==0,
            "Undo to the original draft baseline clears the conflict without discarding the draft",checks);
        Invoke(studio,"Redo");
        Check(conflict.Visibility==Visibility.Visible && name.Text=="Taslak adı" && conflict.Text.Contains("Mevcut X: 10.25 mm"),
            "Redo reestablishes the selected draft conflict",checks);
        Invoke(studio,"Undo");Invoke(studio,"ApplyProperties");
        Check(conflict.Visibility==Visibility.Collapsed && studio.Selected!.Name=="Taslak adı" && TekstilCAD.Core.Geometry.Bounds(studio.Selected.Points).MinX==0,
            "After restoring the baseline the retained draft can be applied without losing selection",checks);
        Check(!studio.IsVisible,"Selected draft conflict checks remain hidden",checks);
    }
    private static void VerifyPropertyDrafts(MainWindow studio,string work,List<string> checks)
    {
        var first=new PatternPiece { Name="Birinci",Points=[new(0,0),new(10,0),new(10,10)] };
        var second=new PatternPiece { Name="İkinci",Points=[new(20,0),new(30,0),new(30,10)] };
        studio.LoadDocument(new ProjectDocument { Name="Taslak doğrulaması",Pieces=[first,second] });
        var path=Path.Combine(work,"property-drafts.tcad");Invoke(studio,"SaveTo",path);
        var name=(TextBox)typeof(MainWindow).GetField("name",PrivateInstance)!.GetValue(studio)!;
        var x=(TextBox)typeof(MainWindow).GetField("x",PrivateInstance)!.GetValue(studio)!;
        var y=(TextBox)typeof(MainWindow).GetField("y",PrivateInstance)!.GetValue(studio)!;
        var before=JsonSerializer.Serialize(studio.Document);
        studio.Select(first.Id);name.Text="Birinci taslak";x.Text="geçersiz";y.Text="5";
        Invoke(studio,"Refresh");
        Check(name.Text=="Birinci taslak" && x.Text=="geçersiz" && y.Text=="5",
            "Property edits survive refresh without changing project content",checks);
        studio.Select(second.Id);name.Text="İkinci taslak";x.Text="25";y.Text="3";
        studio.Select(first.Id);
        Check(name.Text=="Birinci taslak" && x.Text=="geçersiz" && y.Text=="5" && JsonSerializer.Serialize(studio.Document)==before,
            "Property drafts follow each piece across selection changes",checks);
        try { Invoke(studio,"Save");throw new InvalidOperationException("Invalid property draft was saved."); }
        catch(InvalidDataException) { }
        Check(JsonSerializer.Serialize(studio.Document)==before && JsonSerializer.Serialize(ProjectStorage.Load(path))==before
            && name.Text=="Birinci taslak" && x.Text=="geçersiz" && y.Text=="5",
            "Invalid draft prevents saving every pending draft and keeps the invalid text",checks);
        VerifyDraftOutputGuards(studio,first.Id,second.Id,path,before,"geçersiz","invalid",checks);
        x.Text="7";
        VerifyDraftOutputGuards(studio,first.Id,second.Id,path,before,"7","valid",checks);
        Invoke(studio,"Save");
        var saved=ProjectStorage.Load(path);
        Check(saved.Pieces[0].Name=="Birinci taslak" && TekstilCAD.Core.Geometry.Bounds(saved.Pieces[0].Points).MinX==7 && TekstilCAD.Core.Geometry.Bounds(saved.Pieces[0].Points).MinY==5
            && saved.Pieces[1].Name=="İkinci taslak" && TekstilCAD.Core.Geometry.Bounds(saved.Pieces[1].Points).MinX==25 && TekstilCAD.Core.Geometry.Bounds(saved.Pieces[1].Points).MinY==3,
            "Saving applies and persists both corrected property drafts",checks);
        Invoke(studio,"Undo");
        Check(JsonSerializer.Serialize(studio.Document)==before,
            "One undo restores both pieces after applying the draft transaction",checks);
        var matching=new GradingRuleSet { SourcePieceId=first.Id,SourceClosed=true,SourcePoints=[..first.Points],Deltas=[new(1,0),new(1,0),new(1,0)] };
        var stale=new GradingRuleSet { SourcePieceId=second.Id,SourceClosed=true,SourcePoints=[new(19,0),new(30,0),new(30,10)],Deltas=[new(2,0),new(2,0),new(2,0)] };
        studio.LoadDocument(new ProjectDocument { Pieces=[first,second],GradingRules=[matching,stale] });
        studio.Select(first.Id);x.Text="5";studio.Select(second.Id);x.Text="25";
        var mixedPath=Path.Combine(work,"property-drafts-stale-rule.tcad");Invoke(studio,"SaveTo",mixedPath);
        var mixedSaved=ProjectStorage.Load(mixedPath);
        var mixedStatus=(TextBlock)typeof(MainWindow).GetField("status",PrivateInstance)!.GetValue(studio)!;
        Check(mixedSaved.GradingRules.Single(r=>r.SourcePieceId==first.Id).SourcePoints.SequenceEqual(mixedSaved.Pieces.Single(p=>p.Id==first.Id).Points) &&
            mixedSaved.GradingRules.Single(r=>r.SourcePieceId==second.Id).SourcePoints.SequenceEqual(stale.SourcePoints) &&
            mixedStatus.Text.Contains("Proje kaydedildi",StringComparison.Ordinal) && mixedStatus.Text.Contains("eski serileme kuralı",StringComparison.Ordinal) &&
            mixedStatus.Text.Contains(second.Id.ToString("D"),StringComparison.Ordinal) && !mixedStatus.Text.Contains(first.Id.ToString("D"),StringComparison.Ordinal),
            "Saving mixed property drafts identifies the stale rule and persists both pieces",checks);
        Check(!studio.IsVisible,"Property draft verification remains hidden",checks);
    }
    private static void VerifyDraftRemovalConfirmationRace(MainWindow studio,List<string> checks)
    {
        _ = new WindowInteropHelper(studio).EnsureHandle();
        var first=new PatternPiece { Name="A",Points=[new(0,0),new(10,0),new(0,10)] };
        var second=new PatternPiece { Name="B",Points=[new(20,0),new(30,0),new(20,10)] };
        var name=(TextBox)typeof(MainWindow).GetField("name",PrivateInstance)!.GetValue(studio)!;
        var drafts=(System.Collections.IDictionary)typeof(MainWindow).GetField("propertyDrafts",PrivateInstance)!.GetValue(studio)!;
        void Reset()
        {
            studio.ConfirmClearPropertyDraftsForVerification=null;
            studio.LoadDocument(new ProjectDocument { Pieces=[first,second] });studio.Select(first.Id);name.Text="Eski taslak";
        }
        try
        {
            Reset();studio.ConfirmClearPropertyDraftsForVerification=_=> { name.Text="Yeni taslak";return true; };
            Invoke(studio,"ClearPropertyDrafts");
            Check(drafts.Count==1 && name.Text=="Yeni taslak","Bulk draft removal keeps a draft changed during confirmation",checks);
            Reset();studio.ConfirmClearPropertyDraftsForVerification=_=> { studio.Select(second.Id);name.Text="İkinci taslak";return true; };
            Invoke(studio,"ClearPropertyDrafts");
            Check(drafts.Count==2 && drafts.Contains(first.Id) && drafts.Contains(second.Id),"Bulk draft removal keeps newly added drafts during confirmation",checks);
            Reset();studio.ConfirmClearPropertyDraftsForVerification=_=> { studio.LoadDocument(new ProjectDocument { Pieces=[first,second] });studio.Select(first.Id);name.Text="Yeni proje taslağı";return true; };
            Invoke(studio,"ClearPropertyDrafts");
            Check(drafts.Count==1 && name.Text=="Yeni proje taslağı","Bulk draft removal cannot clear a replacement project's draft",checks);
            Reset();studio.ConfirmClearPropertyDraftsForVerification=_=>true;Invoke(studio,"ClearPropertyDrafts");
            Check(drafts.Count==0,"Bulk removal clears the unchanged approved drafts",checks);

            Reset();var review=studio.CreatePropertyDraftReviewWindow(_=> { name.Text="Tek değişen taslak";return true; });
            try
            {
                var root=(FrameworkElement)review.Content;root.Measure(new Size(650,470));root.Arrange(new Rect(0,0,650,470));root.UpdateLayout();
                var list=Descendants<ListBox>(root).Single(l=>AutomationProperties.GetName(l)=="Parça alan taslakları");
                var remove=Descendants<Button>(root).Single(b=>Equals(b.Content,"Seçili taslağı kaldır"));
                list.SelectedItem=list.Items.Cast<ListBoxItem>().Single(i=>Equals(i.Tag,first.Id));
                remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(drafts.Count==1 && name.Text=="Tek değişen taslak","Single draft removal keeps changes made during confirmation",checks);
            }
            finally { review.Close(); }

            Reset();review=studio.CreatePropertyDraftReviewWindow(_=> { studio.LoadDocument(new ProjectDocument { Pieces=[first,second] });studio.Select(first.Id);name.Text="Yeni belge taslağı";return true; });
            try
            {
                var root=(FrameworkElement)review.Content;root.Measure(new Size(650,470));root.Arrange(new Rect(0,0,650,470));root.UpdateLayout();
                var list=Descendants<ListBox>(root).Single(l=>AutomationProperties.GetName(l)=="Parça alan taslakları");
                var remove=Descendants<Button>(root).Single(b=>Equals(b.Content,"Seçili taslağı kaldır"));
                list.SelectedItem=list.Items.Cast<ListBoxItem>().Single(i=>Equals(i.Tag,first.Id));
                remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(drafts.Count==1 && name.Text=="Yeni belge taslağı","Single draft removal cannot clear a replacement project's draft",checks);
            }
            finally { review.Close(); }
        }
        finally { studio.ConfirmClearPropertyDraftsForVerification=null;studio.LoadDocument(new ProjectDocument { Name="Test temizliği" }); }
    }

    private static void VerifyFitSelectedPiece(MainWindow studio,List<string> checks)
    {
        var first=new PatternPiece { Name="Seçili",Points=[new(1000,2000),new(1100,2000),new(1000,2050)] };
        var other=new PatternPiece { Name="Uzak",Points=[new(-10000,-10000),new(-9000,-10000),new(-10000,-9000)] };
        studio.LoadDocument(new ProjectDocument { Pieces=[first,other] });studio.Select(first.Id);
        var canvas=(CadCanvas)typeof(MainWindow).GetField("canvas",PrivateInstance)!.GetValue(studio)!;
        var session=(DocumentSession)typeof(MainWindow).GetField("session",PrivateInstance)!.GetValue(studio)!;
        var scaleField=typeof(CadCanvas).GetField("scale",PrivateInstance)!;var offsetField=typeof(CadCanvas).GetField("offset",PrivateInstance)!;
        var dragField=typeof(CadCanvas).GetField("dragId",PrivateInstance)!;
        var before=studio.Document.Clone();var selected=studio.SelectedId;var undo=session.UndoCount;var redo=session.RedoCount;var dirty=session.IsDirty;
        canvas.Measure(new Size(600,400));canvas.Arrange(new Rect(0,0,600,400));canvas.UpdateLayout();
        canvas.FitSelectedPiece();
        var scale=(double)scaleField.GetValue(canvas)!;var offset=(Vector)offsetField.GetValue(canvas)!;
        Check(Math.Abs((1050*scale+offset.X)-canvas.ActualWidth/2)<1e-6 && Math.Abs((2025*scale+offset.Y)-canvas.ActualHeight/2)<1e-6
            && DocumentSession.ContentEquals(before,studio.Document) && studio.SelectedId==selected && session.UndoCount==undo && session.RedoCount==redo && session.IsDirty==dirty,
            "Fit selected centers only the chosen piece without changing the document, selection or history",checks);
        var priorScale=scale;var priorOffset=offset;dragField.SetValue(canvas,first.Id);
        try { canvas.FitSelectedPiece();Check((double)scaleField.GetValue(canvas)! == priorScale && (Vector)offsetField.GetValue(canvas)! == priorOffset,"Fit selected does not change view during an active drag",checks); }
        finally { dragField.SetValue(canvas,null); }
        studio.Select(null);canvas.FitSelectedPiece();
        Check((double)scaleField.GetValue(canvas)! == priorScale && (Vector)offsetField.GetValue(canvas)! == priorOffset,"Fit selected without a selection keeps the view unchanged",checks);
        Check(!studio.HandleCanvasShortcut(Key.G,false),"Fit selected shortcut does not consume input outside canvas focus",checks);
        var huge=new PatternPiece { Points=[new(-100000000,0),new(100000000,0),new(0,10)] };
        studio.LoadDocument(new ProjectDocument { Pieces=[huge] });studio.Select(huge.Id);canvas.FitSelectedPiece();
        var status=(TextBlock)typeof(MainWindow).GetField("status",PrivateInstance)!.GetValue(studio)!;
        Check(status.Text.Contains("tamamı görünmeyebilir",StringComparison.OrdinalIgnoreCase),"Fit selected reports a partial view at the minimum zoom limit",checks);
        studio.LoadDocument(new ProjectDocument { Name="Test temizliği" });
    }

    private static void VerifyDraftTranslationPrecision(MainWindow studio,string work,List<string> checks)
    {
        var ordinary=new PatternPiece { Name="Normal",Points=[new(0,0),new(10,0),new(0,10)] };
        var thin=new PatternPiece { Name="İnce",Points=[new(0,0),new(0.000000001,0),new(0,1)] };
        studio.LoadDocument(new ProjectDocument { Name="Taşıma hassasiyeti",Pieces=[ordinary,thin] });
        var path=Path.Combine(work,"draft-translation-precision.tcad");Invoke(studio,"SaveTo",path);
        var session=(DocumentSession)typeof(MainWindow).GetField("session",PrivateInstance)!.GetValue(studio)!;
        var name=(TextBox)typeof(MainWindow).GetField("name",PrivateInstance)!.GetValue(studio)!;
        var x=(TextBox)typeof(MainWindow).GetField("x",PrivateInstance)!.GetValue(studio)!;
        var y=(TextBox)typeof(MainWindow).GetField("y",PrivateInstance)!.GetValue(studio)!;
        var before=studio.Document.Clone();var disk=File.ReadAllBytes(path);var undo=session.UndoCount;var redo=session.RedoCount;
        studio.Select(ordinary.Id);name.Text="Normal taslak";x.Text="7.5";y.Text="2.25";
        studio.Select(thin.Id);name.Text="İnce taslak";x.Text="99999999";y.Text="0";
        InvalidDataException? error=null;
        try { Invoke(studio,"Save"); }
        catch(InvalidDataException ex) { error=ex; }
        Check(error is not null && error.Message.Contains(thin.Id.ToString("D")) && error.Message.Contains("Taslak korunuyor")
            && DocumentSession.ContentEquals(before,studio.Document) && File.ReadAllBytes(path).SequenceEqual(disk)
            && session.UndoCount==undo && session.RedoCount==redo && name.Text=="İnce taslak" && x.Text=="99999999",
            "Unsafe thin-piece draft preserves document, file, history and failing draft",checks);
        studio.Select(ordinary.Id);
        Check(name.Text=="Normal taslak" && x.Text=="7.5" && y.Text=="2.25",
            "A separate property draft survives another piece's precision failure",checks);
        Invoke(studio,"ApplyProperties");
        var bounds=TekstilCAD.Core.Geometry.Bounds(studio.Selected!.Points);
        Check(studio.Selected.Name=="Normal taslak" && bounds.MinX==7.5 && bounds.MinY==2.25
            && session.UndoCount==undo+1 && File.ReadAllBytes(path).SequenceEqual(disk),
            "Ordinary fractional property translation succeeds and remains undoable without saving",checks);
        studio.Select(thin.Id);
        Check(name.Text=="İnce taslak" && x.Text=="99999999",
            "Successful independent move retains the unsafe piece's draft for correction",checks);
        Invoke(studio,"Undo");
        Check(DocumentSession.ContentEquals(before,studio.Document) && File.ReadAllBytes(path).SequenceEqual(disk),
            "Undo restores the normal piece while the original file stays intact",checks);

        var far=new PatternPiece { Name="Uzak konum",Points=[new(-90000000,0),new(-89999990,0),new(-90000000,10)] };
        studio.LoadDocument(new ProjectDocument { Name="Hedef konum hassasiyeti",Pieces=[far] });
        var farPath=Path.Combine(work,"draft-target-precision.tcad");Invoke(studio,"SaveTo",farPath);
        studio.Select(far.Id);x.Text="0.000000001";y.Text="0";
        var farBefore=studio.Document.Clone();var farDisk=File.ReadAllBytes(farPath);undo=session.UndoCount;redo=session.RedoCount;
        error=null;
        try { Invoke(studio,"Save"); } catch(InvalidDataException ex) { error=ex; }
        Check(error is not null && error.Message.Contains("istenen konum") && error.Message.Contains(far.Id.ToString("D"))
            && DocumentSession.ContentEquals(farBefore,studio.Document) && File.ReadAllBytes(farPath).SequenceEqual(farDisk)
            && session.UndoCount==undo && session.RedoCount==redo && x.Text=="0.000000001",
            "Rounded-away target position is rejected without changing the draft, document, file or history",checks);
    }
    private static void VerifyRecoverySelectionDetails(MainWindow studio,List<string> checks)
    {
        _ = new WindowInteropHelper(studio).EnsureHandle();
        var stamp=new DateTime(2026,10,31,12,34,56,DateTimeKind.Utc);
        var first=new RecoveryCandidate(@"C:\example\first.json","Aynı proje",stamp,null);
        var second=new RecoveryCandidate(@"C:\example\second.json","Aynı proje",stamp,null);
        var bad=new RecoveryCandidate(@"C:\example\broken.json","Aynı proje",stamp,"Okuma hatası: örnek");
        var state=new MainWindow.RecoveryDialogState();
        var prompt=studio.CreateRecoverySelectionWindow([first,second,bad],2,false,true,true,state);
        try
        {
            var root=(FrameworkElement)prompt.Content;
            root.Measure(new Size(620,520));root.Arrange(new Rect(0,0,620,520));root.UpdateLayout();
            var list=Descendants<ListBox>(root).Single(l=>AutomationProperties.GetName(l)=="Kurtarılabilir proje kayıtları");
            var details=Descendants<TextBox>(root).Single(t=>AutomationProperties.GetName(t)=="Seçili kurtarma kaydı ayrıntıları");
            var open=Descendants<Button>(root).Single(b=>Equals(b.Content,"Seçili kaydı aç"));
            Check(!open.IsEnabled && details.Text=="" && details.IsReadOnly && details.Focusable,
                "Recovery details start empty and open requires a selection",checks);
            var items=list.Items.Cast<ListBoxItem>().ToArray();
            Check(items.Length==3 && items.All(item=>item.IsEnabled),"Unreadable recovery entries remain selectable for inspection",checks);
            list.SelectedItem=items[0];
            Check(open.IsEnabled && details.Text.Contains(first.Path) && details.Text.Contains(stamp.ToLocalTime().ToString("G",System.Globalization.CultureInfo.CurrentCulture)),
                "Recovery selection displays full path and timestamp with seconds",checks);
            list.SelectedItem=items[1];
            Check(open.IsEnabled && details.Text.Contains(second.Path) && !details.Text.Contains(first.Path),
                "Same-name same-time recovery entries are distinguishable by path",checks);
            list.SelectedItem=items[2];
            Check(!open.IsEnabled && details.Text.Contains(bad.Path) && details.Text.Contains(bad.Error!),
                "Unreadable recovery entry exposes path and error but cannot be opened",checks);
            open.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(state.ChosenPath is null,"Recovery open handler refuses unreadable entry",checks);
            list.SelectedItem=null;
            Check(!open.IsEnabled && details.Text=="" && Descendants<Button>(root).Any(b=>Equals(b.Content,"Önceki sayfa"))
                && Descendants<Button>(root).Any(b=>Equals(b.Content,"Sonraki sayfa")),
                "Recovery deselection clears details and paging remains available",checks);
            Check(!prompt.IsVisible && !studio.IsVisible,"Recovery selection verification remains hidden",checks);
        }
        finally { prompt.Close(); }
    }

    private static void VerifyOrphanDraftReview(MainWindow studio,string work,List<string> checks)
    {
        var first=new PatternPiece { Name="Aynı ad",Points=[new(0,0),new(10,0),new(10,10)] };
        var second=new PatternPiece { Name="Aynı ad",Points=[new(20,0),new(30,0),new(30,10)] };
        studio.LoadDocument(new ProjectDocument { Name="Taslak inceleme",Pieces=[first,second] });
        Window? review=null;
        try
        {
        var path=Path.Combine(work,"orphan-draft-review.tcad");Invoke(studio,"SaveTo",path);
        var originalFile=File.ReadAllBytes(path);
        var name=(TextBox)typeof(MainWindow).GetField("name",PrivateInstance)!.GetValue(studio)!;
        var x=(TextBox)typeof(MainWindow).GetField("x",PrivateInstance)!.GetValue(studio)!;
        var y=(TextBox)typeof(MainWindow).GetField("y",PrivateInstance)!.GetValue(studio)!;
        studio.Select(first.Id);name.Text="Silinen parça taslağı";x.Text="geçersiz";y.Text="12,5";
        studio.Select(second.Id);name.Text="Korunan taslak";x.Text="25";y.Text="3";
        // Simulate a programmatic document change that removes a piece while
        // its draft exists. The interactive delete command now blocks this.
        studio.Select(first.Id);
        studio.Commit("Programatik parça kaldırma",()=>studio.Document.Pieces.RemoveAll(p=>p.Id==first.Id));
        var deletedState=JsonSerializer.Serialize(studio.Document);
        try { Invoke(studio,"Save");throw new InvalidOperationException("Missing-piece draft was saved."); }
        catch(InvalidDataException) { }
        Check(File.ReadAllBytes(path).SequenceEqual(originalFile) && JsonSerializer.Serialize(studio.Document)==deletedState,
            "Orphan property draft blocks save without changing the document or existing file",checks);
        Invoke(studio,"Undo");studio.Select(first.Id);
        Check(studio.Document.Pieces.Any(p=>p.Id==first.Id) && name.Text=="Silinen parça taslağı" && x.Text=="geçersiz",
            "Undo restores deleted piece with its unapplied property draft",checks);
        Invoke(studio,"Redo");
        var confirms=0;var allow=false;
        _ = new WindowInteropHelper(studio).EnsureHandle();
        review=studio.CreatePropertyDraftReviewWindow(_=> { confirms++;return allow; });
        var root=(FrameworkElement)review.Content;
        // Materialize the ScrollViewer's visual content without showing the window.
        root.Measure(new Size(650,470));root.Arrange(new Rect(0,0,650,470));root.UpdateLayout();
        var list=Descendants<ListBox>(root).Single(l=>System.Windows.Automation.AutomationProperties.GetName(l)=="Parça alan taslakları");
        var remove=Descendants<Button>(root).Single(b=>Equals(b.Content,"Seçili taslağı kaldır"));
        var draftName=Descendants<TextBox>(root).Single(t=>AutomationProperties.GetName(t)=="Taslak adı");
        var draftX=Descendants<TextBox>(root).Single(t=>AutomationProperties.GetName(t)=="Taslak X konumu");
        var draftY=Descendants<TextBox>(root).Single(t=>AutomationProperties.GetName(t)=="Taslak Y konumu");
        var orphan=list.Items.Cast<ListBoxItem>().Single(item=>Equals(item.Tag,first.Id));
        var other=list.Items.Cast<ListBoxItem>().Single(item=>Equals(item.Tag,second.Id));
        Check(((TextBlock)orphan.Content).Text.Contains("projede yok") && ((TextBlock)other.Content).Text.Contains("Parça projede")
            && ((TextBlock)orphan.Content).Text.Contains(first.Id.ToString()) && ((TextBlock)other.Content).Text.Contains(second.Id.ToString()),
            "Draft review distinguishes same-named orphan and existing pieces by identity",checks);
        remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(confirms==0 && list.Items.Count==2,"Draft review does nothing without a selection",checks);
        Check(new[] { draftName,draftX,draftY }.All(t=>t.IsReadOnly && t.Focusable && t.Text==""),
            "Draft details start empty and are selectable read-only text",checks);
        list.SelectedItem=other;
        Check(draftName.Text=="Korunan taslak" && draftX.Text=="25" && draftY.Text=="3",
            "Draft review displays selected existing piece's actual name and coordinates",checks);
        list.SelectedItem=orphan;
        Check(draftName.Text=="Silinen parça taslağı" && draftX.Text=="geçersiz" && draftY.Text=="12,5",
            "Draft review displays selected orphan's actual and invalid draft values",checks);
        list.SelectedItem=null;
        Check(draftName.Text=="" && draftX.Text=="" && draftY.Text=="",
            "Draft details clear when selection is removed",checks);
        list.SelectedItem=orphan;remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(confirms==1 && list.Items.Count==2 && JsonSerializer.Serialize(studio.Document)==deletedState,
            "Declining orphan draft removal preserves drafts and document",checks);
        allow=true;remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(confirms==2 && list.Items.Count==1 && Equals(((ListBoxItem)list.Items[0]).Tag,second.Id)
            && JsonSerializer.Serialize(studio.Document)==deletedState,
            "Confirmed orphan removal keeps other draft and leaves document unchanged",checks);
        Check(draftName.Text=="" && draftX.Text=="" && draftY.Text=="",
            "Removing a draft clears its displayed details",checks);
        studio.Select(second.Id);
        Check(name.Text=="Korunan taslak" && x.Text=="25","Other property draft remains editable after orphan removal",checks);
        Invoke(studio,"Save");var saved=ProjectStorage.Load(path);
        Check(saved.Pieces.Count==1 && saved.Pieces[0].Id==second.Id && saved.Pieces[0].Name=="Korunan taslak"
            && TekstilCAD.Core.Geometry.Bounds(saved.Pieces[0].Points).MinX==25,
            "Save succeeds and persists remaining draft after orphan removal",checks);
        Check(!review.IsVisible && !studio.IsVisible,"Orphan draft review verification remains hidden",checks);
        }
        finally
        {
            review?.Close();
            studio.LoadDocument(new ProjectDocument { Name="Doğrulama temizliği" });
        }
    }
    private static void VerifyFailedDraftSaves(MainWindow studio,string work,List<string> checks)
    {
        foreach(var mode in new[] { "external", "locked" })
        {
            var piece=new PatternPiece { Name="Kaynak",Points=[new(0,0),new(10,0),new(10,10)] };
            studio.LoadDocument(new ProjectDocument { Name="İlk",Pieces=[piece] });
            var path=Path.Combine(work,$"failed-draft-{mode}.tcad");Invoke(studio,"SaveTo",path);
            studio.Commit("Birinci değişiklik",()=>studio.Document.Name="Birinci");
            studio.Commit("İkinci değişiklik",()=>studio.Document.Name="İkinci");
            Invoke(studio,"Undo");studio.Select(piece.Id);
            var session=(DocumentSession)typeof(MainWindow).GetField("session",PrivateInstance)!.GetValue(studio)!;
            var name=(TextBox)typeof(MainWindow).GetField("name",PrivateInstance)!.GetValue(studio)!;
            var x=(TextBox)typeof(MainWindow).GetField("x",PrivateInstance)!.GetValue(studio)!;
            name.Text="Korunan taslak";x.Text="7";
            var before=JsonSerializer.Serialize(studio.Document);var undo=session.UndoCount;var redo=session.RedoCount;
            if(mode=="external")
            {
                var external=ProjectStorage.Load(path);external.Name="Dış değişiklik";ProjectStorage.Save(external,path);
            }
            var diskBefore=File.ReadAllBytes(path);bool failed=false;
            using(var locked=mode=="locked"?new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read):null)
            {
                try { Invoke(studio,"Save"); }
                catch(Exception ex) when(ex is IOException or InvalidOperationException or UnauthorizedAccessException) { failed=true; }
            }
            Check(failed && File.ReadAllBytes(path).SequenceEqual(diskBefore) && JsonSerializer.Serialize(studio.Document)==before
                && name.Text=="Korunan taslak" && x.Text=="7" && session.UndoCount==undo && session.RedoCount==redo,
                $"{mode} save failure preserves disk, live document, draft and undo/redo history",checks);
            if(mode=="locked")
            {
                Invoke(studio,"Save");
                var saved=ProjectStorage.Load(path);
                Check(saved.Pieces[0].Name=="Korunan taslak" && TekstilCAD.Core.Geometry.Bounds(saved.Pieces[0].Points).MinX==7,
                    "Draft save succeeds after releasing the file lock",checks);
                Invoke(studio,"Undo");
                Check(JsonSerializer.Serialize(studio.Document)==before,
                    "One undo after successful retry restores the unchanged pre-save document",checks);
            }
        }
    }
    // Only known, side-effect-safe guard prefixes may precede the target guard.
    // Reject all other instructions before invoking an entry point: a regression
    // must never open a dialog during hidden verification.
    private static bool HasSafeGuardPrefix(MethodInfo target,MethodInfo guard)
    {
        var safePrior=typeof(MainWindow).GetMethod("EnsureNoExport",PrivateInstance)!;
        var dxfGuard=typeof(MainWindow).GetMethod("BlockForDxfImport",PrivateInstance)!;
        var il=target.GetMethodBody()?.GetILAsByteArray();
        if(il is null)return false;
        var offset=0;
        while(offset+6<=il.Length && il[offset]==0x02 && il[offset+1]==0x28)
        {
            var called=target.Module.ResolveMethod(BitConverter.ToInt32(il,offset+2));
            if(Equals(called,guard))return true;
            // if (BlockForDxfImport()) return; continues only on false.
            // Accept only the exact conditional branch over a single ret.
            if(Equals(called,dxfGuard))
            {
                var branch=offset+6;
                if(branch+3<=il.Length && il[branch]==0x2c && il[branch+1]==1 && il[branch+2]==0x2a)
                { offset=branch+3;continue; }
                if(branch+6<=il.Length && il[branch]==0x39 && BitConverter.ToInt32(il,branch+1)==1 && il[branch+5]==0x2a)
                { offset=branch+6;continue; }
                return false;
            }
            if(!Equals(called,safePrior))return false;
            offset+=6;
        }
        return false;
    }
    private static void VerifyPendingExportPreflightGuards(MainWindow studio,string work,List<string> checks)
    {
        var canvas=(CadCanvas)typeof(MainWindow).GetField("canvas",PrivateInstance)!.GetValue(studio)!;
        var session=(DocumentSession)typeof(MainWindow).GetField("session",PrivateInstance)!.GetValue(studio)!;
        var dragId=typeof(CadCanvas).GetField("dragId",PrivateInstance)!;
        var name=(TextBox)typeof(MainWindow).GetField("name",PrivateInstance)!.GetValue(studio)!;
        try
        {
            foreach(var format in new[] { 1,2,3 })
            foreach(var pending in new[] { "drag","drawing","draft" })
            {
                canvas.CancelDrawing();
                var piece=new PatternPiece { Name="Kaynak",Points=[new(0,0),new(20,0),new(20,10),new(0,10)] };
                studio.LoadDocument(new ProjectDocument { Name="Dışa aktarım",Pieces=[piece] });studio.Select(piece.Id);
                studio.SetTool(pending=="drawing" ? "Çoklu çizgi" : "Seç");
                var snapshot=studio.Document.Clone();var undo=session.UndoCount;var redo=session.RedoCount;
                var path=Path.Combine(work,$"pending-export-{format}-{pending}-{Guid.NewGuid():N}"+(format==1?".dxf":format==2?".svg":".plt"));
                File.WriteAllText(path,"önceden var olan dosya");var bytes=File.ReadAllBytes(path);var version=ProjectStorage.CaptureVersion(path);
                void CreatePending()
                {
                    if(pending=="drag")dragId.SetValue(canvas,piece.Id);
                    else if(pending=="drawing")canvas.RestorePendingDrawing(new RecoveryPendingDrawing("Çoklu çizgi",[new Point2(1,2)]));
                    else name.Text="Uygulanmamış yeni ad";
                }
                // HPGL invokes a modal confirmation callback. Other formats may
                // arrive here after their native/layer review dialogs.
                if(format!=3)
                {
                    CreatePending();var commonBlocked=false;
                    try { Invoke(studio,"EnsurePropertyDraftsApplied"); }
                    catch(InvalidOperationException) { commonBlocked=true; }
                    Check(commonBlocked,$"Common output guard blocks {pending} before a dialog",checks);
                }
                var confirmCalled=false;var blocked=false;
                try
                {
                    _=studio.StartExportAfterPreflight(snapshot,path,format,version,_=>
                    { confirmCalled=true;CreatePending();return true; });
                }
                catch(InvalidOperationException) { blocked=true; }
                Check(blocked && !studio.ExportRunning && (format!=3 || confirmCalled)
                    && File.ReadAllBytes(path).SequenceEqual(bytes) && DocumentSession.ContentEquals(snapshot,studio.Document)
                    && session.UndoCount==undo && session.RedoCount==redo
                    && (pending=="drag" ? canvas.HasActiveDragPreview : pending=="drawing" ? canvas.HasPendingDrawing : name.Text=="Uygulanmamış yeni ad"),
                    $"{(format==1?"DXF":format==2?"SVG":"HPGL")} preflight blocks {pending} work while preserving source, destination and history",checks);
                canvas.CancelDrawing();
            }
        }
        finally { canvas.CancelDrawing();studio.LoadDocument(new ProjectDocument { Name="Dışa aktarım koruma testi tamamlandı" }); }
    }

    private static void VerifyDraftOutputGuards(MainWindow studio,Guid first,Guid second,string path,string before,string firstX,string state,List<string> checks)
    {
        var name=(TextBox)typeof(MainWindow).GetField("name",PrivateInstance)!.GetValue(studio)!;
        var x=(TextBox)typeof(MainWindow).GetField("x",PrivateInstance)!.GetValue(studio)!;
        var y=(TextBox)typeof(MainWindow).GetField("y",PrivateInstance)!.GetValue(studio)!;
        var guard=typeof(MainWindow).GetMethod("EnsurePropertyDraftsApplied",PrivateInstance)!;
        bool stopped=false;
        try { Invoke(studio,"EnsurePropertyDraftsApplied"); }
        catch(InvalidOperationException ex) { stopped=ex.Message.Contains("taslağı"); }
        Check(stopped,$"{state} property drafts stop the output guard",checks);
        foreach(var method in new[] { "Export","OpenMachineCenter","OpenTextileTools" })
        {
            // Enter the method only after verifying its guard is preceded solely
            // by explicitly safe, non-dialog guard calls.
            var target=typeof(MainWindow).GetMethod(method,PrivateInstance)!;
            var guarded=HasSafeGuardPrefix(target,guard);
            Check(guarded,$"{method} checks drafts before any dialog or output",checks);
            var windows=Application.Current.Windows.Count;
            stopped=false;
            try { Invoke(studio,method); }
            catch(InvalidOperationException ex) { stopped=ex.Message.Contains("taslağı"); }
            Check(stopped && Application.Current.Windows.Count==windows && !studio.IsVisible
                && JsonSerializer.Serialize(studio.Document)==before && JsonSerializer.Serialize(ProjectStorage.Load(path))==before
                && name.Text=="Birinci taslak" && x.Text==firstX && y.Text=="5",
                $"{method} blocks {state} draft without opening a window or changing the document",checks);
        }
        studio.Select(second);
        Check(name.Text=="İkinci taslak" && x.Text=="25" && y.Text=="3",
            $"Other piece draft remains intact after {state} output attempts",checks);
        studio.Select(first);
        Check(name.Text=="Birinci taslak" && x.Text==firstX && y.Text=="5",
            $"Selected piece draft remains intact after {state} output attempts",checks);
    }
    private static void WaitRecovery(Task task)
    {
        PumpUntil(()=>task.IsCompleted);task.GetAwaiter().GetResult();
    }
    private static void SetRecovery(MainWindow studio,RecoveryStore? store) =>
        WaitRecovery(studio.SetRecoveryForVerificationAsync(store,store is null ? null : new RecoveryWriteCoordinator(store)));
    private static void RestoreRecoveryDirectory(MainWindow studio,string directory) => SetRecovery(studio,new RecoveryStore(directory));
    private sealed class RecoveryBarrierWriter(RecoveryStore store) : IRecoveryWriter
    {
        public ManualResetEventSlim Entered { get; }=new(false);
        public ManualResetEventSlim Release { get; }=new(false);
        public void Save(ProjectDocument document,IReadOnlyList<RecoveryPropertyDraft> drafts,RecoveryPendingDrawing? drawing)
        {
            Entered.Set();
            if(!Release.Wait(TimeSpan.FromSeconds(12)))throw new TimeoutException("Recovery verification barrier timed out.");
            store.Save(document,drafts,drawing);
        }
        public bool Clear()=>store.ClearOwn();
        public void Dispose()=>store.Dispose();
    }
    private static void VerifyRecoveryClearDuringWrite(MainWindow studio,string work,List<string> checks)
    {
        var original=(RecoveryStore)typeof(MainWindow).GetField("recoveryStore",PrivateInstance)!.GetValue(studio)!;
        var originalDirectory=Path.GetDirectoryName(original.RecoveryPath)!;
        var store=new RecoveryStore(Path.Combine(work,"recovery-write-race-"+Guid.NewGuid().ToString("N")));
        var barrier=new RecoveryBarrierWriter(store);
        WaitRecovery(studio.SetRecoveryForVerificationAsync(store,new RecoveryWriteCoordinator(barrier)));
        Task? save=null;
        try
        {
            studio.LoadDocument(new ProjectDocument { Name="İlk" });
            WaitRecovery(studio.ClearRecoveryForVerificationAsync());
            Invoke(studio,"SaveTo",Path.Combine(work,"recovery-write-race-"+Guid.NewGuid().ToString("N")+".tcad"));
            WaitRecovery(studio.ClearRecoveryForVerificationAsync());
            studio.Commit("Düzenle",()=>studio.Document.Name="Değişiklik");
            save=studio.TriggerRecoveryForVerificationAsync();
            PumpUntil(()=>barrier.Entered.IsSet);
            var dispatcherRan=false;
            studio.Dispatcher.BeginInvoke(new Action(()=>dispatcherRan=true));
            PumpUntil(()=>dispatcherRan);
            Invoke(studio,"Undo");
            var clear=studio.ClearRecoveryForVerificationAsync();
            Check(!clear.IsCompleted && !save.IsCompleted && studio.Document.Name=="İlk",
                "Recovery write does not block dispatcher and clean-state clear waits for active write",checks);
            barrier.Release.Set();
            WaitRecovery(save);WaitRecovery(clear);
            Check(!File.Exists(store.DraftRecoveryPath)
                && typeof(MainWindow).GetField("lastRecoveryAt",PrivateInstance)!.GetValue(studio) is null,
                "Clean-state clear follows in-flight recovery and suppresses stale completion",checks);
        }
        finally
        {
            barrier.Release.Set();
            if(save is not null)WaitRecovery(save);
            RestoreRecoveryDirectory(studio,originalDirectory);
            barrier.Entered.Dispose();barrier.Release.Dispose();
        }
    }
    private sealed class RecoveryFailingClearWriter(RecoveryStore store) : IRecoveryWriter
    {
        public int ClearCalls;
        public bool FailClear;
        public void Save(ProjectDocument document,IReadOnlyList<RecoveryPropertyDraft> drafts,RecoveryPendingDrawing? drawing) => store.Save(document,drafts,drawing);
        public bool Clear()
        {
            Interlocked.Increment(ref ClearCalls);
            if(FailClear)throw new IOException("Kontrollü temizleme hatası");
            return store.ClearOwn();
        }
        public void Dispose()=>store.Dispose();
    }
    private sealed class RecoveryFailingDisposeWriter(RecoveryStore store) : IRecoveryWriter
    {
        public void Save(ProjectDocument document,IReadOnlyList<RecoveryPropertyDraft> drafts,RecoveryPendingDrawing? drawing) => store.Save(document,drafts,drawing);
        public bool Clear()=>store.ClearOwn();
        // Deliberately retain the lease to model an indeterminate disposal failure.
        public void Dispose()=>throw new IOException("Kontrollü kapatma hatası");
    }
    private static void VerifyRecoveryCloseDisposeFailure(string work,List<string> checks)
    {
        var window=new MainWindow();StopTimer(window);
        var store=new RecoveryStore(Path.Combine(work,"recovery-dispose-failure-"+Guid.NewGuid().ToString("N")));
        var closed=false;window.Closed+=(_,_)=>closed=true;
        try
        {
            WaitRecovery(window.SetRecoveryForVerificationAsync(store,new RecoveryWriteCoordinator(new RecoveryFailingDisposeWriter(store))));
            _=new WindowInteropHelper(window).EnsureHandle();
            window.Close();
            var notice=(TextBlock)typeof(MainWindow).GetField("recoveryNotice",PrivateInstance)!.GetValue(window)!;
            PumpUntil(()=>notice.Visibility==Visibility.Visible && notice.Text.Contains("yeni oturum"));
            var replacement=(RecoveryStore?)typeof(MainWindow).GetField("recoveryStore",PrivateInstance)!.GetValue(window);
            var lockHeld=false;
            try { using var probe=new FileStream(Path.ChangeExtension(store.RecoveryPath,".lock"),FileMode.Open,FileAccess.ReadWrite,FileShare.None); }
            catch(IOException) { lockHeld=true; }
            Check(!closed && !window.IsVisible && window.IsEnabled && replacement is not null
                && replacement.SessionId!=store.SessionId && lockHeld,
                "Hidden close disposal failure keeps window enabled, retains uncertain lease and starts a new recovery session",checks);
            StopTimer(window);
            window.Close();PumpUntil(()=>closed);
            Check(!window.IsVisible,"Hidden window can close after recovery session replacement",checks);
        }
        finally
        {
            StopTimer(window);store.Dispose();
            if(!closed)
            {
                // Verification cleanup only: release any replacement lease before forced hidden-window close.
                try { WaitRecovery(window.SetRecoveryForVerificationAsync(null,null)); } catch(IOException) { }
                typeof(MainWindow).GetField("recoveryCloseReady",PrivateInstance)!.SetValue(window,true);
                window.Close();
            }
        }
    }
    private static void VerifyRecoveryClearFailure(MainWindow studio,string work,List<string> checks)
    {
        var original=(RecoveryStore)typeof(MainWindow).GetField("recoveryStore",PrivateInstance)!.GetValue(studio)!;
        var originalDirectory=Path.GetDirectoryName(original.RecoveryPath)!;
        var store=new RecoveryStore(Path.Combine(work,"recovery-clear-failure-"+Guid.NewGuid().ToString("N")));
        var writer=new RecoveryFailingClearWriter(store);
        WaitRecovery(studio.SetRecoveryForVerificationAsync(store,new RecoveryWriteCoordinator(writer)));
        try
        {
            studio.LoadDocument(new ProjectDocument { Name="İlk" });
            WaitRecovery(studio.ClearRecoveryForVerificationAsync());
            Invoke(studio,"SaveTo",Path.Combine(work,"recovery-clear-failure-"+Guid.NewGuid().ToString("N")+".tcad"));
            WaitRecovery(studio.ClearRecoveryForVerificationAsync());
            studio.Commit("Düzenle",()=>studio.Document.Name="Değişiklik");
            WaitRecovery(studio.TriggerRecoveryForVerificationAsync());
            var before=File.ReadAllBytes(store.DraftRecoveryPath);
            writer.FailClear=true;
            var count=writer.ClearCalls;
            Invoke(studio,"Undo");
            var failed=typeof(MainWindow).GetField("recoveryClearFailed",PrivateInstance)!;
            PumpUntil(()=>(bool)failed.GetValue(studio)!);
            var notice=(TextBlock)typeof(MainWindow).GetField("recoveryNotice",PrivateInstance)!.GetValue(studio)!;
            Check(notice.Visibility==Visibility.Visible && notice.Text.Contains("temizlenemedi")
                && File.ReadAllBytes(store.DraftRecoveryPath).SequenceEqual(before),
                "Recovery clear failure warns and retains snapshot bytes",checks);
            Invoke(studio,"Refresh");Invoke(studio,"Refresh");
            var dispatcherRan=false;studio.Dispatcher.BeginInvoke(new Action(()=>dispatcherRan=true));PumpUntil(()=>dispatcherRan);
            Check(writer.ClearCalls==count+1,"Recovery clear failure does not repeatedly retry during refresh",checks);
            writer.FailClear=false;
            WaitRecovery(studio.ClearRecoveryForVerificationAsync());
            Check(!(bool)failed.GetValue(studio)! && !File.Exists(store.DraftRecoveryPath) && writer.ClearCalls==count+2 && notice.Visibility==Visibility.Collapsed,
                "Explicit recovery clear retry succeeds and removes retained snapshot",checks);
        }
        finally { RestoreRecoveryDirectory(studio,originalDirectory); }
    }
    private static void VerifyObsoleteRecoveryCleared(MainWindow studio,string work,List<string> checks)
    {
        var piece=new PatternPiece { Name="İlk",Points=[new(0,0),new(10,0),new(10,10)] };
        studio.LoadDocument(new ProjectDocument { Name="İlk proje",Pieces=[piece] });
        Invoke(studio,"SaveTo",Path.Combine(work,"recovery-clean-source.tcad"));
        var timer=(DispatcherTimer)typeof(MainWindow).GetField("autosaveTimer",PrivateInstance)!.GetValue(studio)!;
        var store=(RecoveryStore)typeof(MainWindow).GetField("recoveryStore",PrivateInstance)!.GetValue(studio)!;
        var originalInterval=timer.Interval;
        try
        {
            timer.Interval=TimeSpan.FromMilliseconds(10);
            studio.Commit("Düzenleme",()=>studio.Document.Name="Düzenlenen proje");
            timer.Start();PumpUntil(()=>File.Exists(store.DraftRecoveryPath));timer.Stop();
            Check(File.Exists(store.DraftRecoveryPath),"Edited project produces recovery snapshot before undo",checks);
            Invoke(studio,"Undo");
            WaitRecovery(studio.ClearRecoveryForVerificationAsync());
            Check(studio.Document.Name=="İlk proje" && !File.Exists(store.DraftRecoveryPath)
                && !(bool)typeof(MainWindow).GetField("dirty",PrivateInstance)!.GetValue(studio)!,
                "Undo to saved state removes obsolete own recovery snapshot",checks);

            studio.Commit("İkinci düzenleme",()=>studio.Document.Name="Önceki çalışma");
            timer.Start();PumpUntil(()=>File.Exists(store.DraftRecoveryPath));timer.Stop();
            var next=new ProjectDocument { Name="Kabul edilen yeni proje",Pieces=[new PatternPiece { Name="Yeni",Points=[new(20,20),new(30,20),new(30,30)] }] };
            studio.LoadDocument(next);
            WaitRecovery(studio.ClearRecoveryForVerificationAsync());
            Check(studio.Document.Name==next.Name && !File.Exists(store.DraftRecoveryPath),
                "Replacing accepted project removes obsolete own recovery snapshot",checks);
        }
        finally { timer.Stop();timer.Interval=originalInterval; }
    }
    private static void VerifyDraftRecovery(MainWindow studio,string work,List<string> checks)
    {
        var piece=new PatternPiece { Name="Kurtarma",Points=[new(0,0),new(10,0),new(10,10)] };
        studio.LoadDocument(new ProjectDocument { Name="Taslak kurtarma",Pieces=[piece] });
        Invoke(studio,"SaveTo",Path.Combine(work,"recovery-draft-source.tcad"));
        var original=JsonSerializer.Serialize(studio.Document);
        studio.Select(piece.Id);
        var x=(TextBox)typeof(MainWindow).GetField("x",PrivateInstance)!.GetValue(studio)!;
        x.Text="geçersiz ölçü";
        Check(!(bool)typeof(MainWindow).GetField("dirty",PrivateInstance)!.GetValue(studio)!,
            "Draft recovery scenario has no applied document changes",checks);
        var timer=(DispatcherTimer)typeof(MainWindow).GetField("autosaveTimer",PrivateInstance)!.GetValue(studio)!;
        var field=typeof(MainWindow).GetField("recoveryStore",PrivateInstance)!;
        var owner=(RecoveryStore)field.GetValue(studio)!;
        var recoveryPath=owner.DraftRecoveryPath;
        var directory=Path.GetDirectoryName(recoveryPath)!;
        var originalInterval=timer.Interval;
        try
        {
            timer.Interval=TimeSpan.FromMilliseconds(10);timer.Start();
            PumpUntil(()=>File.Exists(recoveryPath));timer.Stop();
            var snapshot=File.ReadAllBytes(recoveryPath);
            Check(snapshot.Length>0 && JsonSerializer.Serialize(studio.Document)==original && x.Text=="geçersiz ölçü",
                "Draft-only edits trigger recovery without applying invalid text",checks);
            x.Text=new string('7',16385);
            timer.Start();
            PumpUntil(()=>(bool)typeof(MainWindow).GetField("recoverySaveWarning",PrivateInstance)!.GetValue(studio)!);timer.Stop();
            var notice=(TextBlock)typeof(MainWindow).GetField("recoveryNotice",PrivateInstance)!.GetValue(studio)!;
            Check(notice.Visibility==Visibility.Visible && notice.Text.Contains("güncellenemedi")
                && File.ReadAllBytes(recoveryPath).SequenceEqual(snapshot),
                "Failed oversized draft recovery warns and preserves the previous snapshot",checks);
            x.Text="geçersiz ölçü";
            timer.Start();
            PumpUntil(()=>!(bool)typeof(MainWindow).GetField("recoverySaveWarning",PrivateInstance)!.GetValue(studio)!);timer.Stop();
            var detailsButton=(Button)typeof(MainWindow).GetField("recoveryDetails",PrivateInstance)!.GetValue(studio)!;
            Check(notice.Visibility==Visibility.Collapsed && detailsButton.Visibility==Visibility.Collapsed,
                "Successful recovery hides both the previous save warning and its details action",checks);
            SetRecovery(studio,null);
            var reader=new RecoveryStore(directory);SetRecovery(studio,reader);
            var recovered=reader.LoadCandidate(recoveryPath);
            Check(JsonSerializer.Serialize(recovered.Document)==original && recovered.Drafts.Count==1
                && recovered.Drafts[0].PieceId==piece.Id && recovered.Drafts[0].X=="geçersiz ölçü"
                && JsonSerializer.Serialize(studio.Document)==original && !studio.IsVisible,
                "Recovery store loads the original invalid draft without applying it or showing a window",checks);
            Invoke(studio,"RestoreRecovery",recovered);
            studio.Select(piece.Id);
            Check(x.Text=="geçersiz ölçü" && JsonSerializer.Serialize(studio.Document)==original
                && (bool)typeof(MainWindow).GetField("dirty",PrivateInstance)!.GetValue(studio)! && !studio.IsVisible,
                "Restoring recovery puts raw invalid draft back in its piece field without applying it",checks);
            bool rejected=false;
            try { Invoke(studio,"ApplyProperties"); }
            catch(InvalidDataException) { rejected=true; }
            Check(rejected && x.Text=="geçersiz ölçü" && JsonSerializer.Serialize(studio.Document)==original,
                "Restored invalid draft stays editable and cannot change the document until corrected",checks);
        }
        finally { timer.Stop();timer.Interval=originalInterval; }
    }
    private static void VerifyPendingDrawingRecovery(MainWindow studio,string work,List<string> checks)
    {
        var storeField=typeof(MainWindow).GetField("recoveryStore",PrivateInstance)!;
        var previousStore=(RecoveryStore)storeField.GetValue(studio)!;
        var previousDirectory=Path.GetDirectoryName(previousStore.RecoveryPath)!;
        var timer=(DispatcherTimer)typeof(MainWindow).GetField("autosaveTimer",PrivateInstance)!.GetValue(studio)!;
        var originalInterval=timer.Interval;
        var directory=Path.Combine(work,"drawing-recovery-"+Guid.NewGuid().ToString("N"));
        var writer=new RecoveryStore(directory);
        RecoveryStore? reader=null;
        SetRecovery(studio,writer);
        var canvas=(CadCanvas)typeof(MainWindow).GetField("canvas",PrivateInstance)!.GetValue(studio)!;
        try
        {
            studio.LoadDocument(new ProjectDocument { Name="Çizim kurtarma" });
            studio.SetTool("Bézier");
            var pending=new RecoveryPendingDrawing("Bézier",[new(1,2),new(3,4),new(5,6)]);
            canvas.RestorePendingDrawing(pending);
            Check(!(bool)typeof(MainWindow).GetField("dirty",PrivateInstance)!.GetValue(studio)! && studio.Document.Pieces.Count==0,
                "Unfinished drawing alone leaves project geometry unchanged",checks);
            timer.Interval=TimeSpan.FromMilliseconds(10);timer.Start();
            PumpUntil(()=>File.Exists(writer.DraftRecoveryPath));timer.Stop();
            var path=writer.DraftRecoveryPath;
            var savedBytes=File.ReadAllBytes(path);
            Check(savedBytes.Length>0 && canvas.HasPendingDrawing && studio.Document.Pieces.Count==0,
                "Drawing alone triggers automatic recovery without committing geometry",checks);
            var projectPath=Path.Combine(work,"pending-drawing-saved-project.tcad");
            Invoke(studio,"SaveTo",projectPath);
            Check(File.Exists(path) && File.ReadAllBytes(path).SequenceEqual(savedBytes) && canvas.HasPendingDrawing && ProjectStorage.Load(projectPath).Pieces.Count==0,
                "Saving project preserves unfinished drawing recovery and excludes it from project",checks);
            SetRecovery(studio,null);
            reader=new RecoveryStore(directory);SetRecovery(studio,reader);
            var recovered=reader.LoadCandidate(path);
            Check(recovered.PendingDrawing is { Tool:"Bézier" } loaded && loaded.Points.SequenceEqual(pending.Points) && recovered.Document.Pieces.Count==0,
                "Recovery loads unfinished drawing and project as separate data",checks);
            Invoke(studio,"RestoreRecovery",recovered);
            var restored=canvas.CapturePendingDrawing();
            Check(restored is not null && restored.Tool==pending.Tool && restored.Points.SequenceEqual(pending.Points) && studio.Document.Pieces.Count==0,
                "Recovery restores editable unfinished preview without committing geometry",checks);
            Invoke(studio,"SaveTo",Path.Combine(work,"pending-drawing-restored-project.tcad"));
            timer.Start();PumpUntil(()=>File.Exists(reader.DraftRecoveryPath));timer.Stop();
            canvas.CancelDrawing();
            WaitRecovery(studio.ClearRecoveryForVerificationAsync());
            Check(!canvas.HasPendingDrawing && !File.Exists(reader.DraftRecoveryPath) && studio.Document.Pieces.Count==0,
                "Discarding final unfinished drawing clears obsolete own recovery",checks);
            Check(!File.Exists(path) && Directory.Exists(Path.Combine(directory,"archived")) && Directory.GetFiles(Path.Combine(directory,"archived"),"*.tcr").Length==1,
                "Saved recovered document archives its unchanged source after pending drawing is discarded",checks);
        }
        finally
        {
            timer.Stop();timer.Interval=originalInterval;
            RestoreRecoveryDirectory(studio,previousDirectory);
        }
        VerifyPendingArchiveConflict(studio,work,checks,false);
        VerifyPendingArchiveConflict(studio,work,checks,true);
    }
    private static void VerifyPendingArchiveConflict(MainWindow studio,string work,List<string> checks,bool makeDirty)
    {
        var field=typeof(MainWindow).GetField("recoveryStore",PrivateInstance)!;
        var previousStore=(RecoveryStore)field.GetValue(studio)!;
        var previousDirectory=Path.GetDirectoryName(previousStore.RecoveryPath)!;
        var directory=Path.Combine(work,"drawing-archive-"+Guid.NewGuid().ToString("N"));
        string path;
        using(var writer=new RecoveryStore(directory))
        {
            writer.Save(new ProjectDocument { Name="Arşiv kontrolü" },Array.Empty<RecoveryPropertyDraft>(),new RecoveryPendingDrawing("Çizgi",[new(1,2)]));
            path=writer.DraftRecoveryPath;
        }
        using var reader=new RecoveryStore(directory);
        SetRecovery(studio,reader);
        var canvas=(CadCanvas)typeof(MainWindow).GetField("canvas",PrivateInstance)!.GetValue(studio)!;
        try
        {
            var recovered=reader.LoadCandidate(path);
            Invoke(studio,"RestoreRecovery",recovered);
            Invoke(studio,"SaveTo",Path.Combine(work,"archive-conflict-"+Guid.NewGuid().ToString("N")+".tcad"));
            if(makeDirty) studio.AddPiece([new(0,0),new(4,0)],false);
            else File.AppendAllText(path," "); // Valid newer bytes must not be archived based on an old hash.
            var before=File.ReadAllBytes(path);
            canvas.CancelDrawing();
            Check(File.Exists(path) && File.ReadAllBytes(path).SequenceEqual(before),
                makeDirty ? "Dirty recovered document keeps its recovery source after pending drawing is discarded"
                          : "Changed recovery source is retained after pending drawing is discarded",checks);
        }
        finally { RestoreRecoveryDirectory(studio,previousDirectory); }
    }
    private static void VerifyBackgroundRecoveryScan(MainWindow studio,string work,string reportPath,List<string> checks)
    {
        var storeField=typeof(MainWindow).GetField("recoveryStore",PrivateInstance)!;
        var scanField=typeof(MainWindow).GetField("recoveryScan",PrivateInstance)!;
        var closedField=typeof(MainWindow).GetField("windowClosed",PrivateInstance)!;
        var originalStore=(RecoveryStore)storeField.GetValue(studio)!;
        var originalDirectory=Path.GetDirectoryName(originalStore.RecoveryPath)!;
        var originalClosed=(bool)closedField.GetValue(studio)!;
        var originalScan=scanField.GetValue(studio);
        var directory=Path.Combine(work,"background-recovery-scan-"+Guid.NewGuid().ToString("N"));
        using var own=new RecoveryStore(directory);
        using (var other=new RecoveryStore(directory))
            other.Save(new ProjectDocument { Name="Kurtarma adayı" },Array.Empty<RecoveryPropertyDraft>());
        var corrupt=Path.Combine(directory,"session-"+Guid.NewGuid().ToString("N")+".tcr");
        File.WriteAllText(corrupt,"{broken");
        SetRecovery(studio,own);
        try
        {
            studio.LoadDocument(new ProjectDocument { Name="Kullanıcının açık projesi" });
            var retry=(Button)typeof(MainWindow).GetField("recoveryRescan",PrivateInstance)!.GetValue(studio)!;
            var runningField=typeof(MainWindow).GetField("recoveryScanRunning",PrivateInstance)!;
            var scanMethod=typeof(MainWindow).GetMethod("BeginRecoveryScan",PrivateInstance)!;
            var rootBeforeScan=(FrameworkElement)studio.Content;
            Render(studio,Path.ChangeExtension(reportPath,".recovery-rescan-1024x640.png"),1024,640);
            Check(FullyVisible(retry,rootBeforeScan) && retry.IsEnabled && retry.Focusable && KeyboardNavigation.GetIsTabStop(retry),
                "Recovery rescan action is visible and exposes a tab stop at 1024x640",checks);
            var task=(Task)scanMethod.Invoke(studio,null)!;
            var duplicate=(Task)scanMethod.Invoke(studio,null)!;
            Check((bool)runningField.GetValue(studio)! && !retry.IsEnabled && duplicate.IsCompletedSuccessfully,
                "Running recovery scan disables retry and ignores duplicate requests",checks);
            studio.AddPiece([new(0,0),new(10,0)],false);
            var before=studio.Document.Clone();
            PumpUntil(()=>task.IsCompleted);
            task.GetAwaiter().GetResult();
            Check(!(bool)runningField.GetValue(studio)! && retry.IsEnabled,
                "Recovery rescan action is enabled again when scanning ends",checks);
            var scan=(RecoveryScanResult)scanField.GetValue(studio)!;
            var action=(Button)typeof(MainWindow).GetField("recoveryOpen",PrivateInstance)!.GetValue(studio)!;
            var notice=(TextBlock)typeof(MainWindow).GetField("recoveryScanNotice",PrivateInstance)!.GetValue(studio)!;
            var scanDetails=(Button)typeof(MainWindow).GetField("recoveryScanDetails",PrivateInstance)!.GetValue(studio)!;
            Check(DocumentSession.ContentEquals(before,studio.Document) && !studio.IsVisible,
                "Background recovery scan never replaces a document edited during scanning",checks);
            Check(scan.Candidates.Any(c=>c.IsRecoverable && c.ProjectName=="Kurtarma adayı") && scan.Errors.Count>0
                && action.Visibility==Visibility.Visible && notice.Text.Contains("okunamamış")
                && (notice.ToolTip as string)?.Length>0,
                "Background recovery scan exposes both valid candidates and corrupt-record warning",checks);
            Check(scanDetails.Visibility==Visibility.Visible && scanDetails.IsEnabled && scanDetails.Focusable
                && KeyboardNavigation.GetIsTabStop(scanDetails)
                && AutomationProperties.GetName(scanDetails)=="Kurtarma taraması hatası ayrıntıları"
                && AutomationProperties.GetLiveSetting(notice)==AutomationLiveSetting.Polite,
                "Recovery scan failure exposes a separately named keyboard-accessible details action and polite notice",checks);

            Invoke(studio,"ShowRecoveryWarning","Otomatik kurtarma güncellenemedi. Son değişikliklerinizi Kaydet ile koruyun.","Kayıt erişimi reddedildi.",RecoveryWarningState.Kind.General);
            var warning=(TextBlock)typeof(MainWindow).GetField("recoveryNotice",PrivateInstance)!.GetValue(studio)!;
            var details=(Button)typeof(MainWindow).GetField("recoveryDetails",PrivateInstance)!.GetValue(studio)!;
            var root=(FrameworkElement)studio.Content;
            Render(studio,Path.ChangeExtension(reportPath,".recovery-scan-and-warning-1024x640.png"),1024,640);
            Check(FullyVisible(warning,root) && FullyVisible(details,root) && FullyVisible(notice,root) && FullyVisible(action,root) && FullyVisible(scanDetails,root)
                && warning.ActualHeight+warning.Margin.Top+warning.Margin.Bottom>=warning.DesiredSize.Height-.5
                && notice.ActualHeight+notice.Margin.Top+notice.Margin.Bottom>=notice.DesiredSize.Height-.5
                && details.IsEnabled && details.Focusable && KeyboardNavigation.GetIsTabStop(details)
                && action.IsEnabled && action.Focusable && KeyboardNavigation.GetIsTabStop(action),
                "Recovery warning and background scan notice with both actions fit and expose tab stops at 1024x640",checks);

            File.Delete(corrupt);
            var retryTask=(Task)scanMethod.Invoke(studio,null)!;
            Check(scanDetails.Visibility==Visibility.Collapsed && notice.ToolTip is null,
                "Retry clears stale recovery scan detail before completing",checks);
            PumpUntil(()=>retryTask.IsCompleted);
            retryTask.GetAwaiter().GetResult();
            Check(((RecoveryScanResult)scanField.GetValue(studio)!).Errors.Count==0
                && scanDetails.Visibility==Visibility.Collapsed && notice.ToolTip is null
                && DocumentSession.ContentEquals(before,studio.Document),
                "Successful recovery rescan clears scan details without changing the open document",checks);

            scanField.SetValue(studio,null);
            var closingTask=(Task)typeof(MainWindow).GetMethod("BeginRecoveryScan",PrivateInstance)!.Invoke(studio,null)!;
            closedField.SetValue(studio,true);
            PumpUntil(()=>closingTask.IsCompleted);
            closingTask.GetAwaiter().GetResult();
            Check(scanField.GetValue(studio) is null && DocumentSession.ContentEquals(before,studio.Document),
                "Background recovery scan discards completion after window closing",checks);
        }
        finally
        {
            ((TextBlock)typeof(MainWindow).GetField("recoveryNotice",PrivateInstance)!.GetValue(studio)!).Visibility=Visibility.Collapsed;
            ((Button)typeof(MainWindow).GetField("recoveryDetails",PrivateInstance)!.GetValue(studio)!).Visibility=Visibility.Collapsed;
            ((TextBlock)typeof(MainWindow).GetField("recoveryScanNotice",PrivateInstance)!.GetValue(studio)!).Visibility=Visibility.Collapsed;
            ((Button)typeof(MainWindow).GetField("recoveryScanDetails",PrivateInstance)!.GetValue(studio)!).Visibility=Visibility.Collapsed;
            ((Button)typeof(MainWindow).GetField("recoveryOpen",PrivateInstance)!.GetValue(studio)!).Visibility=Visibility.Collapsed;
            closedField.SetValue(studio,originalClosed);
            scanField.SetValue(studio,originalScan);
            RestoreRecoveryDirectory(studio,originalDirectory);
        }
    }
    private static void VerifyRecoveryPaging(MainWindow studio,string work,List<string> checks)
    {
        var type=typeof(MainWindow);
        var storeField=type.GetField("recoveryStore",PrivateInstance)!;
        var scanField=type.GetField("recoveryScan",PrivateInstance)!;
        var cursorField=type.GetField("recoveryCursor",PrivateInstance)!;
        var moreField=type.GetField("recoveryHasMore",PrivateInstance)!;
        var runningField=type.GetField("recoveryScanRunning",PrivateInstance)!;
        var pagesField=type.GetField("recoveryPages",PrivateInstance)!;
        var pageIndexField=type.GetField("recoveryPageIndex",PrivateInstance)!;
        var firstNumberField=type.GetField("recoveryFirstPageNumber",PrivateInstance)!;
        var pages=(List<RecoveryScanPage>)pagesField.GetValue(studio)!;
        var originalPages=pages.ToArray();var originalIndex=pageIndexField.GetValue(studio);var originalFirstNumber=firstNumberField.GetValue(studio);
        var originalStore=(RecoveryStore)storeField.GetValue(studio)!;var originalDirectory=Path.GetDirectoryName(originalStore.RecoveryPath)!;var originalScan=scanField.GetValue(studio);
        var originalCursor=cursorField.GetValue(studio);var originalMore=moreField.GetValue(studio);
        cursorField.SetValue(studio,null);
        var directory=Path.Combine(work,"recovery-paging-"+Guid.NewGuid().ToString("N"));
        using var own=new RecoveryStore(directory);
        for(var i=0;i<50;i++)
            ProjectStorage.Save(new ProjectDocument { Name="Sayfa "+i },Path.Combine(directory,$"session-{Guid.NewGuid():N}.tcad"));
        SetRecovery(studio,own);
        try
        {
            studio.LoadDocument(new ProjectDocument { Name="Sayfalama sırasında açık proje" });
            studio.AddPiece([new(0,0),new(10,0)],false);
            var before=studio.Document.Clone();
            var begin=type.GetMethod("BeginRecoveryScan",PrivateInstance)!;
            var next=type.GetMethod("ContinueRecoveryScan",PrivateInstance)!;
            var first=(Task)begin.Invoke(studio,null)!;PumpUntil(()=>first.IsCompleted);first.GetAwaiter().GetResult();
            var firstPage=(RecoveryScanResult)scanField.GetValue(studio)!;
            Check(firstPage.Candidates.Count==50 && (bool)moreField.GetValue(studio)!,
                "Recovery first page is bounded and indicates possible continuation",checks);
            var cursorBefore=cursorField.GetValue(studio);
            var second=(Task)next.Invoke(studio,null)!;
            var duplicate=(Task)next.Invoke(studio,null)!;
            Check((bool)runningField.GetValue(studio)! && duplicate.IsCompletedSuccessfully,
                "Recovery paging ignores a second concurrent worker",checks);
            PumpUntil(()=>second.IsCompleted);second.GetAwaiter().GetResult();
            var last=(RecoveryScanResult)scanField.GetValue(studio)!;
            Check(last.Candidates.Count==0 && !(bool)moreField.GetValue(studio)! && DocumentSession.ContentEquals(before,studio.Document),
                "Empty final recovery page terminates without changing the open document",checks);
            pageIndexField.SetValue(studio,0);Invoke(studio,"ShowRecoveryPage");
            var previous=(RecoveryScanResult)scanField.GetValue(studio)!;
            Check(previous.Candidates.Select(c=>c.Path).SequenceEqual(firstPage.Candidates.Select(c=>c.Path))
                && DocumentSession.ContentEquals(before,studio.Document),
                "Previous recovery page restores the same candidates without changing the document",checks);
            pageIndexField.SetValue(studio,1);Invoke(studio,"ShowRecoveryPage");
            var action=(Button)type.GetField("recoveryOpen",PrivateInstance)!.GetValue(studio)!;
            Check(((RecoveryScanResult)scanField.GetValue(studio)!).Candidates.Count==0 && action.Visibility==Visibility.Visible
                && DocumentSession.ContentEquals(before,studio.Document),
                "Returning to an empty final page keeps previous-page navigation available",checks);
            var restart=(Task)begin.Invoke(studio,null)!;PumpUntil(()=>restart.IsCompleted);restart.GetAwaiter().GetResult();
            Check(!ReferenceEquals(cursorBefore,cursorField.GetValue(studio)) && ((RecoveryScanResult)scanField.GetValue(studio)!).Candidates.Count==50
                && DocumentSession.ContentEquals(before,studio.Document),
                "Recovery rescan starts a new cursor and retains the open document",checks);
        }
        finally
        {
            (cursorField.GetValue(studio) as IDisposable)?.Dispose();
            cursorField.SetValue(studio,originalCursor);moreField.SetValue(studio,originalMore);
            pages.Clear();pages.AddRange(originalPages);pageIndexField.SetValue(studio,originalIndex);firstNumberField.SetValue(studio,originalFirstNumber);
            scanField.SetValue(studio,originalScan);RestoreRecoveryDirectory(studio,originalDirectory);
        }
    }
    private static void VerifyPendingDrawingOutputGuards(MainWindow studio,List<string> checks)
    {
        studio.LoadDocument(new ProjectDocument { Name="Çıktı koruması", Pieces=[new PatternPiece { Name="Parça", Points=[new(0,0),new(10,0),new(10,10),new(0,10)] }] });
        studio.SetTool("Çoklu çizgi");
        var canvas=(CadCanvas)typeof(MainWindow).GetField("canvas",PrivateInstance)!.GetValue(studio)!;
        var points=(List<Point2>)typeof(CadCanvas).GetField("drawing",PrivateInstance)!.GetValue(canvas)!;
        points.AddRange([new(1,2),new(3,4)]);
        studio.PendingDrawingChanged();
        var before=studio.Document.Clone();var preview=points.ToArray();
        var session=(DocumentSession)typeof(MainWindow).GetField("session",PrivateInstance)!.GetValue(studio)!;
        var undo=session.UndoCount;var redo=session.RedoCount;
        var guard=typeof(MainWindow).GetMethod("EnsurePropertyDraftsApplied",PrivateInstance)!;
        try
        {
            foreach(var method in new[] { "Export", "OpenMachineCenter", "OpenTextileTools" })
            {
                // A removed guard must fail the check before a real modal dialog can open.
                var target=typeof(MainWindow).GetMethod(method,PrivateInstance)!;
                var guarded=HasSafeGuardPrefix(target,guard);
                Check(guarded,$"{method} checks unfinished drawing before opening a dialog",checks);
                var windows=Application.Current.Windows.Count;bool blocked=false;
                try { Invoke(studio,method); }
                catch(InvalidOperationException ex)
                { blocked=ex.Message.Contains("Tamamlanmamış çizim") && ex.Message.Contains("tamamlayın") && ex.Message.Contains("iptal"); }
                Check(blocked && Application.Current.Windows.Count==windows && !studio.IsVisible
                    && DocumentSession.ContentEquals(before,studio.Document) && points.SequenceEqual(preview)
                    && session.UndoCount==undo && session.RedoCount==redo,
                    $"{method} blocks pending drawing and preserves document, preview and history",checks);
            }
        }
        finally { canvas.CancelDrawing();studio.SetTool("Seç"); }
    }

    private static void VerifyCoordinateDrawing(MainWindow studio,List<string> checks)
    {
        var canvas=(CadCanvas)typeof(MainWindow).GetField("canvas",PrivateInstance)!.GetValue(studio)!;
        var session=(DocumentSession)typeof(MainWindow).GetField("session",PrivateInstance)!.GetValue(studio)!;
        canvas.CancelDrawing(); studio.LoadDocument(new ProjectDocument { Name="Koordinat çizim sınaması" });
        try
        {
            studio.SetTool("Çizgi");
            var initial=studio.Document.Clone();var undo=session.UndoCount;
            var window=new CoordinateDrawingWindow(studio,()=>canvas.ToolGuidance(studio.ActiveTool),canvas.AddDrawingPointAt,studio.CoordinateDrawingFinishProblem,studio.RemoveCoordinateDrawingPoint,studio.CoordinateDrawingSummary);
            Check(AutomationProperties.GetName(window.XInput).Contains("X") && AutomationProperties.GetName(window.YInput).Contains("Y")
                && window.XInput.Focusable && window.YInput.Focusable && window.AddPointButton.IsDefault,
                "Coordinate drawing exposes named keyboard-editable fields and default add action",checks);
            Check(window.RemovePointButton.Focusable && KeyboardNavigation.GetIsTabStop(window.RemovePointButton)
                && Equals(window.RemovePointButton.Content,"Son noktayı kaldır") && AutomationProperties.GetName(window.PointSummary).Contains(window.PointSummary.Text),
                "Coordinate point removal and its current summary are keyboard and automation accessible",checks);
            window.RemoveLastPoint();
            Check(window.Feedback.Text.Contains("yok",StringComparison.OrdinalIgnoreCase) && canvas.CapturePendingDrawing() is null
                && DocumentSession.ContentEquals(initial,studio.Document) && session.UndoCount==undo,
                "Removing a missing coordinate point reports a no-op without changing project history",checks);
            window.XInput.Text="geçersiz";window.YInput.Text="2";window.AddPoint();
            Check(window.Feedback.Text.Contains("X koordinatı") && canvas.CapturePendingDrawing() is null
                && DocumentSession.ContentEquals(initial,studio.Document) && session.UndoCount==undo,
                "Invalid coordinate preserves the document, history and pending drawing",checks);
            window.XInput.Text="1,25";window.YInput.Text="2,5";window.AddPoint();
            Check(window.Feedback.Text.Contains("Nokta işlendi") && canvas.CapturePendingDrawing()!.Points.SequenceEqual([new Point2(1.25,2.5)]),
                "Explicit fractional coordinates are retained without grid snapping",checks);
            Check(window.PointSummary.Text.Contains("1") && AutomationProperties.GetName(window.PointSummary).Contains(window.PointSummary.Text),
                "Coordinate point summary and accessible name update after adding a point",checks);
            var lineSummary=window.PointSummary.Text;
            window.RemoveLastPoint();
            Check(canvas.CapturePendingDrawing() is null && window.Feedback.Text.Contains("kaldırıldı",StringComparison.OrdinalIgnoreCase)
                && window.PointSummary.Text!=lineSummary && DocumentSession.ContentEquals(initial,studio.Document) && session.UndoCount==undo,
                "Removing the only coordinate point clears the pending drawing but preserves project history",checks);
            window.AddPoint();
            window.RequestFinish();
            Check(window.Feedback.Text.Contains("Çoklu çizgi") && canvas.CapturePendingDrawing()!.Points.SequenceEqual([new Point2(1.25,2.5)])
                && DocumentSession.ContentEquals(initial,studio.Document) && session.UndoCount==undo,
                "Finish request for a non-polyline tool reports the problem without changing the drawing",checks);
            window.AddPoint();
            Check(window.Feedback.Text.Contains("eklenmedi",StringComparison.OrdinalIgnoreCase)
                && canvas.CapturePendingDrawing()!.Points.Count==1 && session.UndoCount==undo,
                "A repeated line endpoint reports no change and preserves the first point",checks);
            window.XInput.Text="4,25";window.YInput.Text="5,5";window.AddPoint();
            Check(studio.Document.Pieces.Count==1 && !studio.Document.Pieces[0].Closed && canvas.CapturePendingDrawing() is null
                && studio.Document.Pieces[0].Points.SequenceEqual([new Point2(1.25,2.5),new Point2(4.25,5.5)])
                && session.UndoCount==undo+1,"Coordinate line commits exactly once and clears its preview",checks);
            Check(session.Undo() && DocumentSession.ContentEquals(initial,studio.Document),"Coordinate line can be undone",checks);

            canvas.CancelDrawing();studio.SetTool("Dikdörtgen");
            Check(canvas.AddDrawingPointAt(new(1,1)) is null && canvas.AddDrawingPointAt(new(1,5)) is not null
                && canvas.CapturePendingDrawing()!.Points.SequenceEqual([new Point2(1,1)]),
                "Degenerate rectangle endpoint is rejected with its first point preserved",checks);
            Check(canvas.AddDrawingPointAt(new(4,5)) is null && studio.Document.Pieces[^1].Closed
                && studio.Document.Pieces[^1].Points.Count==4,"Coordinate rectangle creates a closed four-corner contour",checks);

            canvas.CancelDrawing();studio.SetTool("Çoklu çizgi");
            Check(canvas.AddDrawingPointAt(new(0,0)) is null && canvas.AddDrawingPointAt(new(5,0)) is null,
                "Coordinate polyline accepts its initial two points",checks);
            window.RemoveLastPoint();
            Check(canvas.CapturePendingDrawing()!.Points.SequenceEqual([new Point2(0,0)]) && window.PointSummary.Text.Contains("1")
                && window.Feedback.Text.Contains("kaldırıldı",StringComparison.OrdinalIgnoreCase),
                "Coordinate removal keeps earlier polyline points and reports the new count",checks);
            Check(canvas.AddDrawingPointAt(new(5,0)) is null,"Coordinate polyline accepts a corrected replacement point",checks);
            var pending=canvas.CapturePendingDrawing()!.Points.ToArray();var beforeFinish=studio.Document.Clone();undo=session.UndoCount;
            window.RequestFinish();
            Check(window.Feedback.Text.Contains("en az üç nokta") && canvas.CapturePendingDrawing()!.Points.SequenceEqual(pending)
                && DocumentSession.ContentEquals(beforeFinish,studio.Document) && session.UndoCount==undo,
                "Finish request for fewer than three points keeps the pending drawing and reports how to proceed",checks);
            Check(canvas.AddDrawingPointAt(new(5,5)) is null && canvas.CapturePendingDrawing()!.Points.Count==3,
                "Coordinate polyline keeps its pending points available for recovery",checks);
            var count=studio.Document.Pieces.Count;canvas.FinishPolyline();
            Check(studio.Document.Pieces.Count==count+1 && studio.Document.Pieces[^1].Closed && canvas.CapturePendingDrawing() is null,
                "Coordinate polyline completes using the ordinary contour validation path",checks);

            canvas.CancelDrawing();studio.SetTool("Bézier");count=studio.Document.Pieces.Count;
            Check(canvas.AddDrawingPointAt(new(0,0)) is null && canvas.AddDrawingPointAt(new(1,2)) is null
                && canvas.AddDrawingPointAt(new(3,2)) is null && canvas.AddDrawingPointAt(new(4,0)) is null
                && studio.Document.Pieces.Count==count+1 && !studio.Document.Pieces[^1].Closed,
                "Coordinate Bézier uses four inputs and commits a sampled open curve",checks);
            var unchanged=studio.Document.Clone();undo=session.UndoCount;
            Check(canvas.AddDrawingPointAt(new(7,7)) is null && canvas.AddDrawingPointAt(new(7,7)) is null
                && canvas.AddDrawingPointAt(new(7,7)) is null && canvas.AddDrawingPointAt(new(7,7)) is not null
                && canvas.CapturePendingDrawing()!.Points.Count==3 && DocumentSession.ContentEquals(unchanged,studio.Document)
                && session.UndoCount==undo,"Invalid final Bézier point restores its three-point preview and history",checks);
            window.RemoveLastPoint();
            Check(canvas.CapturePendingDrawing() is { Tool:"Bézier" } remainingCurve && remainingCurve.Points.Count==2
                && window.Feedback.Text.Contains("kaldırıldı",StringComparison.OrdinalIgnoreCase)
                && DocumentSession.ContentEquals(unchanged,studio.Document) && session.UndoCount==undo,
                "Coordinate removal also preserves the earlier Bézier controls and project history",checks);

            canvas.CancelDrawing();studio.SetTool("Yay");count=studio.Document.Pieces.Count;
            Check(canvas.AddDrawingPointAt(new(0,0)) is null && canvas.AddDrawingPointAt(new(4,0)) is null
                && canvas.AddDrawingPointAt(new(0,4)) is null && studio.Document.Pieces.Count==count+1
                && !studio.Document.Pieces[^1].Closed,"Coordinate arc uses three inputs and commits a sampled open curve",checks);

            canvas.CancelDrawing();studio.SetTool("Seç");var before=studio.Document.Clone();undo=session.UndoCount;
            Check(canvas.AddDrawingPointAt(new(2,2)) is not null && canvas.AddDrawingPointAt(new(double.NaN,2)) is not null
                && DocumentSession.ContentEquals(before,studio.Document) && session.UndoCount==undo,
                "Coordinate input cannot add geometry while the selection tool is active",checks);
            window.RemoveLastPoint();
            Check(window.Feedback.Text.Contains("yok",StringComparison.OrdinalIgnoreCase) && !canvas.HasPendingDrawing
                && DocumentSession.ContentEquals(before,studio.Document) && session.UndoCount==undo,
                "Coordinate removal with selection tool is a safe reported no-op",checks);
        }
        finally { canvas.CancelDrawing();studio.SetTool("Seç"); }
    }

    private static void VerifyBackspaceDrawing(MainWindow studio,string work,List<string> checks)
    {
        var oldStore=(RecoveryStore)typeof(MainWindow).GetField("recoveryStore",PrivateInstance)!.GetValue(studio)!;
        var oldDirectory=Path.GetDirectoryName(oldStore.RecoveryPath)!;
        var canvas=(CadCanvas)typeof(MainWindow).GetField("canvas",PrivateInstance)!.GetValue(studio)!;
        var session=(DocumentSession)typeof(MainWindow).GetField("session",PrivateInstance)!.GetValue(studio)!;
        var store=new RecoveryStore(Path.Combine(work,"backspace-recovery-"+Guid.NewGuid().ToString("N")));
        SetRecovery(studio,store);
        try
        {
            studio.LoadDocument(new ProjectDocument { Name="Nokta geri al" });
            var before=studio.Document.Clone();var undo=session.UndoCount;var redo=session.RedoCount;
            studio.SetTool("Bézier");
            var loopControl = new Point2[] { new(0,0), new(100,100), new(-100,100) };
            canvas.RestorePendingDrawing(new RecoveryPendingDrawing("Bézier",loopControl));
            var loopError=canvas.AddDrawingPointAt(new Point2(0,0));
            Check(loopError is not null && canvas.CapturePendingDrawing()!.Points.SequenceEqual(loopControl) &&
                DocumentSession.ContentEquals(before,studio.Document) && session.UndoCount==undo && session.RedoCount==redo,
                "Intersecting open Bezier is rejected while its prior drawing and history remain intact",checks);
            canvas.CancelDrawing();
            foreach(var (tool,points) in new (string,Point2[])[] {
                ("Çizgi",[new(1,2)]), ("Dikdörtgen",[new(1,2)]),
                ("Bézier",[new(1,2),new(3,4),new(5,6)]),
                ("Yay",[new(1,2),new(3,4)]),
                ("Çoklu çizgi",[new(1,2),new(3,4),new(5,6)]) })
            {
                canvas.CancelDrawing();studio.SetTool(tool);
                canvas.RestorePendingDrawing(new RecoveryPendingDrawing(tool,points));
                Check(!studio.HandleCanvasShortcut(Key.Back,false) && canvas.CapturePendingDrawing()!.Points.SequenceEqual(points),
                    $"Backspace does not change {tool} without canvas focus",checks);
                Check(studio.HandleCanvasShortcut(Key.Back,true),$"Backspace is handled for {tool}",checks);
                var rest=canvas.CapturePendingDrawing();
                Check(points.Length==1 ? rest is null : rest is not null && rest.Tool==tool && rest.Points.SequenceEqual(points.SkipLast(1)),
                    $"Backspace removes only last uncommitted {tool} point",checks);
                Check(DocumentSession.ContentEquals(before,studio.Document) && session.UndoCount==undo && session.RedoCount==redo,
                    $"Backspace leaves document and history unchanged for {tool}",checks);
            }
            canvas.CancelDrawing();studio.SetTool("Çoklu çizgi");
            Check(studio.HandleCanvasShortcut(Key.Back,true) && !canvas.HasPendingDrawing && DocumentSession.ContentEquals(before,studio.Document),
                "Backspace on empty or cancelled drawing is a safe no-op",checks);
            canvas.RestorePendingDrawing(new RecoveryPendingDrawing("Çoklu çizgi",[new(1,2),new(3,4),new(5,6)]));
            studio.HandleCanvasShortcut(Key.Back,true);
            WaitRecovery(studio.TriggerRecoveryForVerificationAsync());
            var copyDirectory=Path.Combine(work,"backspace-recovery-read-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(copyDirectory);
            var candidateCopy=Path.Combine(copyDirectory,Path.GetFileName(store.DraftRecoveryPath));
            File.Copy(store.DraftRecoveryPath,candidateCopy);
            using var independentReader=new RecoveryStore(copyDirectory);
            var recovered=independentReader.LoadCandidate(candidateCopy);
            Check(recovered.PendingDrawing is { Tool:"Çoklu çizgi" } drawing && drawing.Points.SequenceEqual(new Point2[] { new(1,2),new(3,4) }),
                "Recovery records the remaining drawing after Backspace",checks);
            studio.HandleCanvasShortcut(Key.Back,true);
            studio.HandleCanvasShortcut(Key.Back,true);
            PumpUntil(()=>!File.Exists(store.DraftRecoveryPath));
            Check(!canvas.HasPendingDrawing && DocumentSession.ContentEquals(before,studio.Document) && !File.Exists(store.DraftRecoveryPath),
                "Removing the last point cleans the obsolete owned recovery when the project is clean",checks);
            var coordinateWindow=new CoordinateDrawingWindow(studio,()=>canvas.ToolGuidance(studio.ActiveTool),canvas.AddDrawingPointAt,
                studio.CoordinateDrawingFinishProblem,studio.RemoveCoordinateDrawingPoint,studio.CoordinateDrawingSummary);
            canvas.RestorePendingDrawing(new RecoveryPendingDrawing("Çoklu çizgi",[new(1,2),new(3,4),new(5,6)]));
            coordinateWindow.RemoveLastPoint();
            WaitRecovery(studio.TriggerRecoveryForVerificationAsync());
            var coordinateCopyDirectory=Path.Combine(work,"coordinate-recovery-read-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(coordinateCopyDirectory);
            var coordinateCopy=Path.Combine(coordinateCopyDirectory,Path.GetFileName(store.DraftRecoveryPath));
            File.Copy(store.DraftRecoveryPath,coordinateCopy);
            using(var coordinateReader=new RecoveryStore(coordinateCopyDirectory))
            {
                var snapshot=coordinateReader.LoadCandidate(coordinateCopy);
                Check(snapshot.PendingDrawing is { Tool:"Çoklu çizgi" } coordinateDrawing
                    && coordinateDrawing.Points.SequenceEqual(new Point2[] { new(1,2),new(3,4) }),
                    "Coordinate removal records the remaining points in recovery",checks);
            }
            coordinateWindow.RemoveLastPoint();coordinateWindow.RemoveLastPoint();
            PumpUntil(()=>!File.Exists(store.DraftRecoveryPath));
            Check(!canvas.HasPendingDrawing && DocumentSession.ContentEquals(before,studio.Document) && !File.Exists(store.DraftRecoveryPath),
                "Coordinate removal of the last point clears the obsolete owned recovery",checks);
            Check(AutomationProperties.GetHelpText(canvas).Contains("Backspace",StringComparison.OrdinalIgnoreCase),
                "Canvas accessible help describes Backspace",checks);
            var racingStore=new RecoveryStore(Path.Combine(work,"backspace-recovery-race-"+Guid.NewGuid().ToString("N")));
            var barrier=new RecoveryBarrierWriter(racingStore);
            WaitRecovery(studio.SetRecoveryForVerificationAsync(racingStore,new RecoveryWriteCoordinator(barrier)));
            Task? save=null;
            try
            {
                canvas.RestorePendingDrawing(new RecoveryPendingDrawing("Çoklu çizgi",[new(1,2)]));
                save=studio.TriggerRecoveryForVerificationAsync();
                PumpUntil(()=>barrier.Entered.IsSet);
                studio.HandleCanvasShortcut(Key.Back,true);
                Check(!canvas.HasPendingDrawing && !save.IsCompleted && DocumentSession.ContentEquals(before,studio.Document),
                    "Last-point Backspace remains responsive during an in-flight recovery write",checks);
                barrier.Release.Set();WaitRecovery(save);
                PumpUntil(()=>!File.Exists(racingStore.DraftRecoveryPath));
                Check(!File.Exists(racingStore.DraftRecoveryPath) && session.UndoCount==undo && session.RedoCount==redo,
                    "Last-point Backspace prevents an in-flight recovery from leaving a stale drawing",checks);
            }
            finally { barrier.Release.Set();if(save is not null)WaitRecovery(save);barrier.Entered.Dispose();barrier.Release.Dispose(); }
        }
        finally { canvas.CancelDrawing();studio.SetTool("Seç");RestoreRecoveryDirectory(studio,oldDirectory); }
    }

    private static void VerifyToolGuidance(MainWindow studio,List<string> checks)
    {
        studio.LoadDocument(new ProjectDocument { Name="Araç yönlendirmesi" });
        var canvas=(CadCanvas)typeof(MainWindow).GetField("canvas",PrivateInstance)!.GetValue(studio)!;
        var status=(TextBlock)typeof(MainWindow).GetField("status",PrivateInstance)!.GetValue(studio)!;
        var mouseMove=typeof(CadCanvas).GetMethod("Move",PrivateInstance)!.CreateDelegate<Action<object,MouseEventArgs>>(canvas);
        var safe=typeof(CadCanvas).GetMethod("Safe",PrivateInstance)!.CreateDelegate<Action<Action,bool,Action<string>?>>(canvas);
        var notice=(TextBlock)typeof(MainWindow).GetField("canvasError",PrivateInstance)!.GetValue(studio)!;
        var panel=(DockPanel)typeof(MainWindow).GetField("canvasErrorPanel",PrivateInstance)!.GetValue(studio)!;
        var viewer=(ScrollViewer)typeof(MainWindow).GetField("canvasErrorViewer",PrivateInstance)!.GetValue(studio)!;
        safe(()=>throw new ArgumentException("Kontur deneme hatası"),true,null);
        mouseMove(canvas,new MouseEventArgs(Mouse.PrimaryDevice,0));
        Check(panel.Visibility==Visibility.Visible && notice.Text.Contains("Kontur deneme hatası") && viewer.Focusable && KeyboardNavigation.GetIsTabStop(viewer),
            "Drawing error remains accessible after pointer movement",checks);
        safe(()=>studio.AddPiece([new Point2(0,0),new Point2(10,0)],false),true,null);
        Check(panel.Visibility==Visibility.Collapsed,"Successful explicit drawing action clears previous drawing error",checks);
        var root=(FrameworkElement)studio.Content;
        root.Measure(new Size(1024,640));root.Arrange(new Rect(0,0,1024,640));root.UpdateLayout();
        var metadataButton=Descendants<Button>(root).Single(b=>Equals(b.Content,"Parça bilgileri…"));
        var metadataTip=metadataButton.ToolTip as string ?? "";
        Check(metadataTip.Contains("Adetli pastal") && metadataTip.Contains("kesim rol") && metadataTip.Contains("Temel pastal") && metadataTip.Contains("bir kez") && metadataTip.Contains("makine"),
            "Piece metadata tooltip distinguishes quantity nesting, basic nesting and machine output",checks);
        var stages=new (string Tool,Point2[] Points,string Expected)[]
        {
            ("Çizgi",[],"başlangıcına"), ("Çizgi",[new(0,0)],"bitişine"),
            ("Dikdörtgen",[],"ilk köşesine"), ("Dikdörtgen",[new(0,0)],"Karşı köşeye"),
            ("Bézier",[],"başlangıcına"), ("Bézier",[new(0,0)],"Birinci kontrol"),
            ("Bézier",[new(0,0),new(10,0)],"İkinci kontrol"), ("Bézier",[new(0,0),new(10,0),new(10,10)],"bitişine"),
            ("Yay",[],"merkezine"), ("Yay",[new(0,0)],"yarıçapı belirler"),
            ("Yay",[new(0,0),new(10,0)],"yarıçap değişmez"),
            ("Çoklu çizgi",[],"en az 3"), ("Çoklu çizgi",[new(0,0),new(10,0),new(0,10)],"Enter")
        };
        try
        {
            foreach(var (tool,points,expected) in stages)
            {
                canvas.CancelDrawing();studio.SetTool(tool);
                if(points.Length>0)canvas.RestorePendingDrawing(new RecoveryPendingDrawing(tool,points));
                var guidance=canvas.ToolGuidance(tool);
                Check(guidance.Contains(expected,StringComparison.OrdinalIgnoreCase),$"Tool guidance {tool} stage {points.Length} describes next action",checks);
                mouseMove(canvas,new MouseEventArgs(Mouse.PrimaryDevice,0));
                Check(status.Text.Contains(guidance) && status.Text.Contains("X ") && status.Text.Contains("Y "),$"Pointer movement retains {tool} stage {points.Length} guidance",checks);
            }
            studio.LoadDocument(new ProjectDocument { Name="Klavye ile tamamlama" });studio.SetTool("Çoklu çizgi");
            canvas.RestorePendingDrawing(new RecoveryPendingDrawing("Çoklu çizgi",[new(0,0),new(10,0),new(0,10)]));
            var enter=studio.HandleCanvasShortcut(Key.Enter,true);
            Check(enter && !canvas.HasPendingDrawing && studio.Document.Pieces.Count==1,"Enter completes an eligible polyline when canvas-focused",checks);
            canvas.RestorePendingDrawing(new RecoveryPendingDrawing("Çoklu çizgi",[new(20,20),new(30,20)]));
            var before=studio.Document.Clone();
            foreach(var key in new[] { Key.V,Key.L,Key.P,Key.R,Key.B,Key.A,Key.F,Key.Enter,Key.Escape,Key.Delete })
                Check(!studio.HandleCanvasShortcut(key,false) && canvas.HasPendingDrawing && studio.ActiveTool=="Çoklu çizgi" && DocumentSession.ContentEquals(before,studio.Document),
                    $"{key} cannot act on the canvas without its focus",checks);
            var escape=studio.HandleCanvasShortcut(Key.Escape,true);
            Check(escape && !canvas.HasPendingDrawing && DocumentSession.ContentEquals(before,studio.Document),"Escape discards only the unfinished polyline when canvas-focused",checks);
            Check(studio.HandleCanvasShortcut(Key.L,true) && studio.ActiveTool=="Çizgi" && studio.HandleCanvasShortcut(Key.V,true) && studio.ActiveTool=="Seç",
                "Canvas-focused drawing shortcuts change tools",checks);
        }
        finally { canvas.CancelDrawing();studio.SetTool("Seç"); }
    }
    private static void VerifyMetadataDialogPieceIdentity(MainWindow studio,List<string> checks)
    {
        var first=new PatternPiece { Name="Aynı\u202E",Points=[new(0,0),new(10,0),new(0,10)] };
        var second=new PatternPiece { Name="Aynı\u202E",Points=[new(20,0),new(30,0),new(20,10)] };
        studio.LoadDocument(new ProjectDocument { Pieces=[first,second] });
        var session=(DocumentSession)typeof(MainWindow).GetField("session",PrivateInstance)!.GetValue(studio)!;
        Window Open(Guid id)
        {
            studio.Select(id);var dialog=studio.BuildPieceMetadataDialog()!;
            var root=(FrameworkElement)dialog.Content;root.Measure(new Size(360,320));root.Arrange(new Rect(0,0,360,320));root.UpdateLayout();
            return dialog;
        }
        var a=Open(first.Id);var ar=(FrameworkElement)a.Content;
        var aName=Descendants<TextBox>(ar).Single(t=>AutomationProperties.GetName(t)=="Düzenlenen parça adı");
        var aId=Descendants<TextBox>(ar).Single(t=>AutomationProperties.GetName(t)=="Düzenlenen parçanın tam kimliği");
        Check(aName.IsReadOnly && aId.IsReadOnly && aName.Focusable && aId.Focusable && aName.Text.Contains("\\u202E",StringComparison.OrdinalIgnoreCase)
            && !aName.Text.Contains('\u202E') && aId.Text==first.Id.ToString("D"),
            "Metadata form identifies the first same-named piece with safe copyable name and full ID",checks);
        var b=Open(second.Id);var br=(FrameworkElement)b.Content;
        var bName=Descendants<TextBox>(br).Single(t=>AutomationProperties.GetName(t)=="Düzenlenen parça adı");
        var bId=Descendants<TextBox>(br).Single(t=>AutomationProperties.GetName(t)=="Düzenlenen parçanın tam kimliği");
        Check(bName.Text==aName.Text && bId.Text==second.Id.ToString("D") && bId.Text!=aId.Text
            && Descendants<ScrollViewer>(br).Any(s=>AutomationProperties.GetName(s)=="Parça kimliği ve bilgileri"),
            "Same-named pieces remain distinguishable in the scrollable metadata form",checks);
        var before=studio.Document.Clone();var undo=session.UndoCount;var done=false;
        studio.MetadataDialogSuccessForVerification=_=>done=true;
        try
        {
            Descendants<TextBox>(br).Single(t=>AutomationProperties.GetName(t)=="Malzeme").Text="Pamuk";
            Descendants<Button>(br).Single(t=>Equals(t.Content,"Bilgileri uygula")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(done && studio.Document.Pieces.Single(p=>p.Id==second.Id).Metadata?.Material=="Pamuk"
                && studio.Document.Pieces.Single(p=>p.Id==first.Id).Metadata is null && session.UndoCount==undo+1,
                "Metadata applies only to the selected same-named piece",checks);
            var c=Open(first.Id);var cr=(FrameworkElement)c.Content;var after=studio.Document.Clone();undo=session.UndoCount;
            Descendants<TextBox>(cr).Single(t=>AutomationProperties.GetName(t)=="Malzeme").Text="İptal";
            var cancel=Descendants<Button>(cr).Single(t=>Equals(t.Content,"Vazgeç"));
            Check(cancel.IsCancel && cancel.Focusable && DocumentSession.ContentEquals(after,studio.Document) && session.UndoCount==undo,
                "Editing a metadata form without applying does not change the project or history",checks);
            c.Close();
        }
        finally { studio.MetadataDialogSuccessForVerification=null; a.Close();b.Close(); }
    }
    private static void VerifyMetadataDialogStaleProject(MainWindow studio,List<string> checks)
    {
        var piece=new PatternPiece { Name="Parça bilgileri",Points=[new(0,0),new(10,0),new(0,10)] };
        studio.LoadDocument(new ProjectDocument { Pieces=[piece] });studio.Select(piece.Id);
        var dialog=studio.BuildPieceMetadataDialog()!;var root=(FrameworkElement)dialog.Content;
        root.Measure(new Size(450,700));root.Arrange(new Rect(0,0,450,700));root.UpdateLayout();
        var size=Descendants<TextBox>(root).Single(t=>AutomationProperties.GetName(t)=="Beden");
        var material=Descendants<TextBox>(root).Single(t=>AutomationProperties.GetName(t)=="Malzeme");
        var quantity=Descendants<TextBox>(root).Single(t=>AutomationProperties.GetName(t)=="Adet (1–100000)");
        size.Text="M";material.Text="Pamuk";quantity.Text="25";
        studio.Commit("Arka planda geometri güncellendi",()=>studio.Document.Pieces[0].Points[1]=new Point2(12,0));
        var session=(DocumentSession)typeof(MainWindow).GetField("session",PrivateInstance)!.GetValue(studio)!;
        var beforeMetadata=studio.Document.Clone();var completed=false;
        studio.MetadataDialogSuccessForVerification=_=>completed=true;
        try
        {
            Descendants<Button>(root).Single(b=>Equals(b.Content,"Bilgileri uygula")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var saved=studio.Document.Pieces[0];
            Check(completed && saved.Points[1]==new Point2(12,0) && saved.Metadata?.Size=="M" && saved.Metadata.Material=="Pamuk" && saved.Metadata.Quantity==25,
                "Metadata form applies entered values to the current geometry after an unrelated edit",checks);
            Invoke(studio,"Undo");
            Check(DocumentSession.ContentEquals(beforeMetadata,studio.Document),"Undo metadata preserves the concurrent geometry edit",checks);
            Invoke(studio,"Redo");Check(studio.Document.Pieces[0].Metadata?.Quantity==25,"Redo restores metadata after rebasing",checks);

            var conflict=studio.BuildPieceMetadataDialog()!;var conflictRoot=(FrameworkElement)conflict.Content;
            conflictRoot.Measure(new Size(450,700));conflictRoot.Arrange(new Rect(0,0,450,700));conflictRoot.UpdateLayout();
            var conflictSize=Descendants<TextBox>(conflictRoot).Single(t=>AutomationProperties.GetName(t)=="Beden");conflictSize.Text="XL";
            studio.Commit("Başka bilgi güncellendi",()=>studio.Document.Pieces[0].Metadata!.Size="S");
            var changed=studio.Document.Clone();var undo=session.UndoCount;var redo=session.RedoCount;completed=false;
            Descendants<Button>(conflictRoot).Single(b=>Equals(b.Content,"Bilgileri uygula")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var error=Descendants<TextBlock>(conflictRoot).Single(t=>AutomationProperties.GetLiveSetting(t)==AutomationLiveSetting.Assertive);
            Check(error.Text.Contains("üzerine yazılmadı") && conflictSize.Text=="XL" && !completed
                && DocumentSession.ContentEquals(changed,studio.Document) && session.UndoCount==undo && session.RedoCount==redo,
                "Concurrent metadata conflict preserves both the current values and form input",checks);

            var oldProject=studio.BuildPieceMetadataDialog()!;var oldRoot=(FrameworkElement)oldProject.Content;
            oldRoot.Measure(new Size(450,700));oldRoot.Arrange(new Rect(0,0,450,700));oldRoot.UpdateLayout();
            studio.LoadDocument(changed);studio.Select(piece.Id);var loaded=studio.Document.Clone();completed=false;
            Descendants<Button>(oldRoot).Single(b=>Equals(b.Content,"Bilgileri uygula")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var oldError=Descendants<TextBlock>(oldRoot).Single(t=>AutomationProperties.GetLiveSetting(t)==AutomationLiveSetting.Assertive);
            Check(!completed && oldError.Text.Contains("bilgiler uygulanmadı") && DocumentSession.ContentEquals(loaded,studio.Document),
                "A newly loaded project with the same piece ID rejects the old metadata form",checks);
            var selectionDialog=studio.BuildPieceMetadataDialog()!;var selectionRoot=(FrameworkElement)selectionDialog.Content;
            selectionRoot.Measure(new Size(450,700));selectionRoot.Arrange(new Rect(0,0,450,700));selectionRoot.UpdateLayout();
            var selectionSize=Descendants<TextBox>(selectionRoot).Single(t=>AutomationProperties.GetName(t)=="Beden");selectionSize.Text="XXL";
            studio.Select(null);completed=false;
            Descendants<Button>(selectionRoot).Single(b=>Equals(b.Content,"Bilgileri uygula")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var selectionError=Descendants<TextBlock>(selectionRoot).Single(t=>AutomationProperties.GetLiveSetting(t)==AutomationLiveSetting.Assertive);
            Check(!completed && selectionError.Text.Contains("bilgiler uygulanmadı") && selectionSize.Text=="XXL" && DocumentSession.ContentEquals(loaded,studio.Document),
                "Changing selection rejects metadata update without losing the form input",checks);
            studio.Select(piece.Id);
            var invalidDialog=studio.BuildPieceMetadataDialog()!;var invalidRoot=(FrameworkElement)invalidDialog.Content;
            invalidRoot.Measure(new Size(450,700));invalidRoot.Arrange(new Rect(0,0,450,700));invalidRoot.UpdateLayout();
            var invalidQuantity=Descendants<TextBox>(invalidRoot).Single(t=>AutomationProperties.GetName(t)=="Adet (1–100000)");invalidQuantity.Text="0";
            var invalidApply=Descendants<Button>(invalidRoot).Single(b=>Equals(b.Content,"Bilgileri uygula"));
            invalidApply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var invalidError=Descendants<TextBlock>(invalidRoot).Single(t=>AutomationProperties.GetLiveSetting(t)==AutomationLiveSetting.Assertive);
            Check(!completed && invalidError.Text.Contains("Adet") && invalidQuantity.Text=="0" && DocumentSession.ContentEquals(loaded,studio.Document),
                "Invalid quantity leaves metadata form and document intact",checks);
            invalidQuantity.Text="30";invalidApply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(completed && studio.Document.Pieces[0].Metadata?.Quantity==30,"Corrected quantity can be applied without reopening the form",checks);
        }
        finally { studio.MetadataDialogSuccessForVerification=null; }
    }
    private static void VerifyBatchTransforms(MainWindow studio,List<string> checks)
    {
        var safeLabel=CadCanvas.CanvasPieceLabel("Ön\u202E\n😀");
        var longLabel=CadCanvas.CanvasPieceLabel(new string('A',78)+"😀B");
        Check(safeLabel=="Ön\\u202E\\u000A😀" && CadCanvas.CanvasPieceLabel("")=="(boş)"
            && longLabel.Length<=80 && longLabel.EndsWith('…') && !char.IsHighSurrogate(longLabel[^2]),
            "Canvas labels escape control characters and truncate without splitting emoji",checks);
        var oldPointer=new Point2(1,2);var newPointer=new Point2(3,4);
        Check(!CadCanvas.ShouldInvalidatePointerMove(false,false,false,oldPointer,newPointer)
            && !CadCanvas.ShouldInvalidatePointerMove(false,false,true,oldPointer,oldPointer)
            && CadCanvas.ShouldInvalidatePointerMove(true,false,false,oldPointer,newPointer)
            && CadCanvas.ShouldInvalidatePointerMove(false,true,false,oldPointer,newPointer)
            && CadCanvas.ShouldInvalidatePointerMove(false,false,true,oldPointer,newPointer),
            "Pointer movement redraws only when the canvas appearance changes",checks);
        const int pointCount=10000;
        var points=Enumerable.Range(0,pointCount).Select(i=> { var angle=2*Math.PI*i/pointCount;return new Point2(100*Math.Cos(angle),60*Math.Sin(angle)); }).ToList();
        var piece=new PatternPiece { Name="Dönüşüm sınaması",Points=points };
        studio.LoadDocument(new ProjectDocument { Pieces=[piece] });studio.Select(piece.Id);
        var root=(FrameworkElement)studio.Content;root.Measure(new Size(1024,640));root.Arrange(new Rect(0,0,1024,640));root.UpdateLayout();
        var compactButtons=Descendants<Button>(root).Where(b=>Equals(b.Content,"90° sağa")||Equals(b.Content,"Yatay aynala")).ToArray();
        Check(compactButtons.Length==2 && compactButtons.All(button=>
        {
            var text=new FormattedText((string)button.Content,System.Globalization.CultureInfo.CurrentUICulture,
                button.FlowDirection,new Typeface(button.FontFamily,button.FontStyle,button.FontWeight,button.FontStretch),
                button.FontSize,button.Foreground,VisualTreeHelper.GetDpi(button).PixelsPerDip);
            var available=button.ActualWidth-button.Padding.Left-button.Padding.Right-button.BorderThickness.Left-button.BorderThickness.Right;
            return available>=text.WidthIncludingTrailingWhitespace && !string.IsNullOrWhiteSpace(AutomationProperties.GetName(button))
                && button.ToolTip is string tip && !string.IsNullOrWhiteSpace(tip);
        }),"New transform labels fit at minimum window size and expose descriptive accessible names and tooltips",checks);
        var center=TekstilCAD.Core.Geometry.Bounds(points).Center;
        var expectedRotate=TekstilCAD.Core.Geometry.Rotate(points,90,center);
        Descendants<Button>(root).Single(b=>Equals(b.Content,"90° döndür")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(studio.Document.Pieces[0].Points.SequenceEqual(expectedRotate) && Enumerable.Range(0,pointCount).All(i=>
            Math.Abs(studio.Document.Pieces[0].Points[i].X+points[i].Y)<1e-8 && Math.Abs(studio.Document.Pieces[0].Points[i].Y-points[i].X)<1e-8),
            "Batch rotation matches analytic 90-degree coordinates for 10000 points",checks);
        Invoke(studio,"Undo");Check(studio.Document.Pieces[0].Points.SequenceEqual(points),"Undo restores the pre-rotation contour",checks);
        Invoke(studio,"Redo");Check(studio.Document.Pieces[0].Points.SequenceEqual(expectedRotate),"Redo restores the rotated 10000-point contour",checks);
        Invoke(studio,"Undo");
        var expectedMirror=TekstilCAD.Core.Geometry.Mirror(points,true,center.X);
        Descendants<Button>(root).Single(b=>Equals(b.Content,"Aynala")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(studio.Document.Pieces[0].Points.SequenceEqual(expectedMirror) && Enumerable.Range(0,pointCount).All(i=>
            Math.Abs(studio.Document.Pieces[0].Points[i].X+points[i].X)<1e-8 && Math.Abs(studio.Document.Pieces[0].Points[i].Y-points[i].Y)<1e-8),
            "Batch mirroring matches analytic coordinates for 10000 points",checks);
        Invoke(studio,"Undo");Check(studio.Document.Pieces[0].Points.SequenceEqual(points),"Undo restores the pre-mirror contour",checks);
        var expectedReverse=TekstilCAD.Core.Geometry.Rotate(points,-90,center);
        Descendants<Button>(root).Single(b=>Equals(b.Content,"90° sağa")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(studio.Document.Pieces[0].Points.SequenceEqual(expectedReverse),"Reverse rotation applies clockwise coordinates",checks);
        Invoke(studio,"Undo");
        Check(studio.Document.Pieces[0].Points.SequenceEqual(points),"Undo restores the contour after reverse rotation",checks);
        Invoke(studio,"Redo");
        Check(studio.Document.Pieces[0].Points.SequenceEqual(expectedReverse),"Redo restores reverse rotation",checks);
        Invoke(studio,"Undo");
        var expectedHorizontal=TekstilCAD.Core.Geometry.Mirror(points,false,center.Y);
        Descendants<Button>(root).Single(b=>Equals(b.Content,"Yatay aynala")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(studio.Document.Pieces[0].Points.SequenceEqual(expectedHorizontal),"Horizontal-axis mirror applies reflected coordinates",checks);
        Invoke(studio,"Undo");
        Check(studio.Document.Pieces[0].Points.SequenceEqual(points),"Undo restores the contour after horizontal-axis mirror",checks);
        Invoke(studio,"Redo");
        Check(studio.Document.Pieces[0].Points.SequenceEqual(expectedHorizontal),"Redo restores horizontal-axis mirror",checks);
        Invoke(studio,"Undo");
        var canvas=(CadCanvas)typeof(MainWindow).GetField("canvas",PrivateInstance)!.GetValue(studio)!;
        var session=(DocumentSession)typeof(MainWindow).GetField("session",PrivateInstance)!.GetValue(studio)!;
        var before=studio.Document.Clone();var undo=session.UndoCount;var redo=session.RedoCount;
        var transformButtons=Descendants<Button>(root).Where(b=>Equals(b.Content,"Aynala")||Equals(b.Content,"90° döndür")||Equals(b.Content,"90° sağa")||Equals(b.Content,"Yatay aynala")).ToArray();
        studio.SetTool("Çizgi");canvas.RestorePendingDrawing(new RecoveryPendingDrawing("Çizgi",[new Point2(1,2)]));
        foreach(var button in transformButtons)button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(canvas.HasPendingDrawing && DocumentSession.ContentEquals(before,studio.Document) && session.UndoCount==undo && session.RedoCount==redo,
            "Batch transforms preserve unfinished drawing and document history",checks);
        canvas.CancelDrawing();studio.SetTool("Seç");
        var drag=typeof(CadCanvas).GetField("dragId",PrivateInstance)!;drag.SetValue(canvas,piece.Id);
        try
        {
            foreach(var button in transformButtons)button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(DocumentSession.ContentEquals(before,studio.Document) && session.UndoCount==undo && session.RedoCount==redo,
                "Batch transforms do not alter the document during drag preview",checks);
        }
        finally { drag.SetValue(canvas,null); }
    }
    private static void VerifyNestingErrorNavigationLarge(MainWindow studio,string reportPath,List<string> checks)
    {
        var targetId=Guid.NewGuid();
        var all=Enumerable.Range(0,10000).Select(i=>new PatternPiece { Id=i==9999?targetId:Guid.NewGuid(),Name=i==0?targetId.ToString("D"):$"Parça {i}",Points=[new(0,0),new(10,0),new(0,10)],Metadata=new PieceMetadata { Role=i==9999?"Yardımcı":"Kesim",Material="Pamuk",Quantity=1 } }).ToList();
        studio.LoadDocument(new ProjectDocument { Pieces=all });studio.Select(targetId);var before=studio.Document.Clone();
        var window=new ProductionNestingWindow(studio,studio);Render(window,Path.ChangeExtension(reportPath,".nest-large-before.png"),680,520);
        var old=SynchronizationContext.Current;SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(studio.Dispatcher));
        try
        {
            var task=(Task)typeof(ProductionNestingWindow).GetMethod("Calculate",PrivateInstance)!.Invoke(window,null)!;PumpUntil(()=>task.IsCompleted);task.GetAwaiter().GetResult();
            var button=(Button)typeof(ProductionNestingWindow).GetField("locateError",PrivateInstance)!.GetValue(window)!;
            var choices=(ListBox)typeof(ProductionNestingWindow).GetField("choices",PrivateInstance)!.GetValue(window)!;
            var status=(TextBlock)typeof(ProductionNestingWindow).GetField("status",PrivateInstance)!.GetValue(window)!;
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var firstStatus=status.Text;
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(button.Visibility==Visibility.Visible && choices.Items.Count==1 && status.Text==firstStatus && DocumentSession.ContentEquals(before,studio.Document),"Error navigation targets exact identity in 10000 pieces and repeat clicks do not grow status",checks);
            var item=choices.Items[0];Check((Guid)item.GetType().GetProperty("Id")!.GetValue(item)! == targetId && (bool)item.GetType().GetProperty("IsSelected")!.GetValue(item)!,"Large-list navigation preserves intended source selection",checks);
            Render(window,Path.ChangeExtension(reportPath,".nest-large-navigation.png"),680,520);
        }
        finally { SynchronizationContext.SetSynchronizationContext(old);window.Close(); }
    }
    private static void VerifyNestingErrorNavigation(MainWindow studio,string reportPath,List<string> checks)
    {
        var a=new PatternPiece { Name="Ön\u202E",Points=[new(0,0),new(20,0),new(0,20)],Metadata=new PieceMetadata { Role="Kesim",Material="Pamuk",Quantity=1 } };
        var b=a.Clone();b.Id=Guid.NewGuid();b.Metadata!.Role="Yardımcı";
        studio.LoadDocument(new ProjectDocument { Pieces=[a,b] });studio.Select(a.Id);var before=studio.Document.Clone();
        var window=new ProductionNestingWindow(studio,studio);Render(window,Path.ChangeExtension(reportPath,".nest-error-navigation-before.png"),680,520);
        var items=(System.Collections.IList)typeof(ProductionNestingWindow).GetField("choiceItems",PrivateInstance)!.GetValue(window)!;
        var selected=items[1]!.GetType().GetProperty("IsSelected")!;selected.SetValue(items[1],true);
        var old=SynchronizationContext.Current;SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(studio.Dispatcher));
        try
        {
            var task=(Task)typeof(ProductionNestingWindow).GetMethod("Calculate",PrivateInstance)!.Invoke(window,null)!;PumpUntil(()=>task.IsCompleted);task.GetAwaiter().GetResult();
            var button=(Button)typeof(ProductionNestingWindow).GetField("locateError",PrivateInstance)!.GetValue(window)!;
            var status=(TextBlock)typeof(ProductionNestingWindow).GetField("status",PrivateInstance)!.GetValue(window)!;
            var query=(TextBox)typeof(ProductionNestingWindow).GetField("choiceSearch",PrivateInstance)!.GetValue(window)!;
            var versionField=typeof(ProductionNestingWindow).GetField("inputsVersion",PrivateInstance)!;var version=(int)versionField.GetValue(window)!;
            Check(button.Visibility==Visibility.Visible && status.Text.Contains(b.Id.ToString("D")),"Pastal piece error exposes navigation to the correct source",checks);
            query.Text=a.Id.ToString("D");button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var marked=(bool)items[1]!.GetType().GetProperty("IsNavigationTarget")!.GetValue(items[1])!;
            Check(query.Text==b.Id.ToString("D") && marked && (bool)selected.GetValue(items[0])! && (bool)selected.GetValue(items[1])! && (int)versionField.GetValue(window)! == version && status.Text.Contains("arama kimliğe ayarlandı") && DocumentSession.ContentEquals(before,studio.Document),"Error navigation reveals hidden source without changing selected pieces or project",checks);
            Render(window,Path.ChangeExtension(reportPath,".nest-error-navigation.png"),680,520);
            var choices=(ListBox)typeof(ProductionNestingWindow).GetField("choices",PrivateInstance)!.GetValue(window)!;
            var targetContainer=(DependencyObject)choices.ItemContainerGenerator.ContainerFromItem(items[1])!;
            var targetCheckbox=Descendants<CheckBox>(targetContainer).Single();
            Check(FullyVisible(targetCheckbox,(FrameworkElement)window.Content),"Navigating to an error scrolls target into view: bounds="+BoundsInRoot(targetCheckbox,(FrameworkElement)window.Content)+" visible="+VisibleBoundsInRoot(targetCheckbox,(FrameworkElement)window.Content)+" scroll="+string.Join(";",Descendants<ScrollViewer>(choices).Select(v=>$"{v.VerticalOffset}/{v.ScrollableHeight}/{v.ViewportHeight}/{v.CanContentScroll}")),checks);
            studio.Document.Pieces[0].Name="Yeni ad";typeof(ProductionNestingWindow).GetMethod("CheckVisibleResultCurrent",PrivateInstance)!.Invoke(window,null);
            Check(button.Visibility==Visibility.Collapsed && !(bool)items[1]!.GetType().GetProperty("IsNavigationTarget")!.GetValue(items[1])!,"Pastal navigation clears when project changes",checks);
        }
        finally { SynchronizationContext.SetSynchronizationContext(old);window.Close(); }
    }
    private static void VerifyNestingErrorIdentity(List<string> checks)
    {
        var one=new PatternPiece { Name="Ön\u202E"+char.ConvertFromUtf32(0xE0001)+"😀",Points=[new(0,0),new(20,0),new(0,20)],Metadata=new PieceMetadata { Role="Kesim",Material="Pamuk",Quantity=1 } };
        var two=one.Clone();two.Id=Guid.NewGuid();two.Metadata!.Role="Model";
        var document=new ProjectDocument { Pieces=[one,two] };var before=document.Clone();
        try { TekstilCAD.Nesting.ProductionNestingPlan.Arrange(document,[one.Id,two.Id],100);throw new InvalidOperationException("Expected validation failure."); }
        catch(ArgumentException ex)
        {
            var text=ProductionNestingWindow.FormatNestingFailure(ex,document);
            Check(text.Contains(two.Id.ToString("D")) && !text.Contains(one.Id.ToString("D")) && text.Contains("\\u202E\\uDB40\\uDC01😀") && !text.Contains('\u202E') && DocumentSession.ContentEquals(before,document),"Pastal failure identifies correct source with safely escaped invisible characters",checks);
        }
        two.Metadata!.Role="Kesim";
        try { TekstilCAD.Nesting.ProductionNestingPlan.Arrange(document,[two.Id],10);throw new InvalidOperationException("Expected width failure."); }
        catch(ArgumentException ex)
        {
            var text=ProductionNestingWindow.FormatNestingFailure(ex,document);
            Check(text.Contains(two.Id.ToString("D")) && text.Contains("sığmıyor"),"Pastal width failure maps generated copy identity to source",checks);
        }
        two.Closed=false;
        try { TekstilCAD.Nesting.ProductionNestingPlan.Arrange(document,[two.Id],100);throw new InvalidOperationException("Expected open contour failure."); }
        catch(ArgumentException ex)
        {
            var text=ProductionNestingWindow.FormatNestingFailure(ex,document);
            Check(text.Contains(two.Id.ToString("D")) && text.Contains("kapalı") && text.Contains("\\u202E"),"Pastal open contour error names the correct safely represented source",checks);
        }
    }
    private static void VerifyNestingCancellation(MainWindow studio,string reportPath,List<string> checks)
    {
        var piece=new PatternPiece { Name="İptal parçası",Points=[new(0,0),new(20,0),new(20,20),new(0,20)],Metadata=new PieceMetadata { Role="Kesim",Material="Pamuk",Quantity=2 } };
        studio.LoadDocument(new ProjectDocument { Pieces=[piece] });studio.Select(piece.Id);
        var before=studio.Document.Clone();var session=(DocumentSession)typeof(MainWindow).GetField("session",PrivateInstance)!.GetValue(studio)!;var undo=session.UndoCount;
        var window=new ProductionNestingWindow(studio,studio);
        var old=SynchronizationContext.Current;SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(studio.Dispatcher));
        using var reached=new ManualResetEventSlim();using var release=new ManualResetEventSlim();Task? task=null;
        try
        {
            window.BeforeCalculationForVerification=()=>{reached.Set();if(!release.Wait(TimeSpan.FromSeconds(30)))throw new TimeoutException();};
            task=(Task)typeof(ProductionNestingWindow).GetMethod("Calculate",PrivateInstance)!.Invoke(window,null)!;
            PumpUntil(()=>reached.IsSet);
            Render(window,Path.ChangeExtension(reportPath,".nesting-cancel.png"),680,520);
            var cancel=(Button)typeof(ProductionNestingWindow).GetField("cancelCalculation",PrivateInstance)!.GetValue(window)!;
            var status=(TextBlock)typeof(ProductionNestingWindow).GetField("status",PrivateInstance)!.GetValue(window)!;
            var preview=typeof(ProductionNestingWindow).GetField("preview",PrivateInstance)!.GetValue(window)!;
            Check(cancel.IsEnabled && cancel.Visibility==Visibility.Visible && FullyVisible(cancel,(FrameworkElement)window.Content),"Pastal cancellation is visible during calculation at minimum window size",checks);
            cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(!cancel.IsEnabled && status.Text.Contains("İptal istendi") && DocumentSession.ContentEquals(before,studio.Document),"Pastal cancellation requests stop without changing source",checks);
            window.Close();
            Check(!(bool)typeof(ProductionNestingWindow).GetField("closed",PrivateInstance)!.GetValue(window)!,"Pastal window waits for cancelled worker before closing",checks);
            release.Set();PumpUntil(()=>task.IsCompleted);task.GetAwaiter().GetResult();
            Check(status.Text.Contains("iptal edildi") && preview.GetType().GetProperty("Drawing")!.GetValue(preview) is null && DocumentSession.ContentEquals(before,studio.Document) && session.UndoCount==undo,"Cancelled pastal leaves no partial result, edits or history",checks);
        }
        finally { release.Set();if(task is not null)PumpUntil(()=>task.IsCompleted);SynchronizationContext.SetSynchronizationContext(old);window.Close(); }
    }
    private static void VerifyGrainlineEditorSafety(MainWindow studio,string work,List<string> checks)
    {
        var previousContext=SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(studio.Dispatcher));
        var canvas=(CadCanvas)typeof(MainWindow).GetField("canvas",PrivateInstance)!.GetValue(studio)!;
        var session=(DocumentSession)typeof(MainWindow).GetField("session",PrivateInstance)!.GetValue(studio)!;
        var first=new PatternPiece { Name="Ön",Points=[new(0,0),new(20,0),new(20,30),new(0,30)] };
        var second=new PatternPiece { Name="Arka",Points=[new(40,0),new(60,0),new(60,30),new(40,30)] };
        try
        {
            canvas.CancelDrawing();studio.LoadDocument(new ProjectDocument { Name="İplik testi",Pieces=[first,second] });studio.Select(first.Id);
            var stamp=studio.CaptureGrainlineEdit() ?? throw new InvalidOperationException("Grainline stamp missing.");
            var window=new GrainlineEditorWindow(studio,stamp.Snapshot.Pieces.Single(p=>p.Id==first.Id),()=>studio.GrainlineEditProblem(stamp));
            try
            {
                window.StartX.Text="geçersiz";window.StartY.Text="1";window.EndX.Text="5";window.EndY.Text="2";
                Check(!window.TryBuildCandidate(out _,out var invalid) && invalid.Contains("koordinat",StringComparison.OrdinalIgnoreCase),
                    "Grainline editor rejects invalid coordinate text",checks);
                window.StartX.Text="5";window.StartY.Text="2";window.EndX.Text="5";window.EndY.Text="2";
                Check(!window.TryBuildCandidate(out _,out var equal) && equal.Contains("farklı",StringComparison.OrdinalIgnoreCase),
                    "Grainline editor rejects coincident endpoints",checks);
                window.EndY.Text="18,5";window.Reviewed.IsChecked=false;
                Check(window.TryBuildCandidate(out var candidate,out _) && candidate is { NeedsReview:true } && candidate.Start==new Point2(5,2) && candidate.End==new Point2(5,18.5)
                    && AutomationProperties.GetName(window.StartX).Contains("Başlangıç",StringComparison.Ordinal),
                    "Grainline editor builds accessible finite coordinates without granting review",checks);
                window.Reviewed.IsChecked=true;
                Check(window.TryBuildCandidate(out candidate,out _) && candidate is { NeedsReview:false },
                    "Grainline editor records an explicit user review state",checks);
                var before=studio.Document.Clone();var undo=session.UndoCount;
                Check(studio.ApplyGrainlineEdit(stamp,candidate) && session.UndoCount==undo+1 && studio.Selected!.Grainline is { NeedsReview:false },
                    "Adding grainline commits exactly one document operation",checks);
                var created=studio.Document.Clone();
                Check(session.Undo() && DocumentSession.ContentEquals(before,studio.Document) && session.Redo() && DocumentSession.ContentEquals(created,studio.Document),
                    "Undo and redo preserve the grainline and document",checks);
                stamp=studio.CaptureGrainlineEdit()!;undo=session.UndoCount;var redo=session.RedoCount;
                Check(!studio.ApplyGrainlineEdit(stamp,studio.Selected!.Grainline!.Clone()) && session.UndoCount==undo && session.RedoCount==redo,
                    "Unchanged grainline does not add history",checks);
                var reviewEditor=new GrainlineEditorWindow(studio,stamp.Snapshot.Pieces.Single(p=>p.Id==first.Id),()=>studio.GrainlineEditProblem(stamp));
                try
                {
                    Check(reviewEditor.Reviewed.IsChecked==true && reviewEditor.TryBuildCandidate(out var reviewedInitial,out _) && reviewedInitial is { NeedsReview:false },
                        "Opening an unchanged reviewed grainline preserves the existing review",checks);
                    foreach(var field in new[] { reviewEditor.StartX,reviewEditor.StartY,reviewEditor.EndX,reviewEditor.EndY })
                    {
                        var initial=field.Text;field.Text="geçersiz";
                        Check(reviewEditor.Reviewed.IsChecked==false && !reviewEditor.TryBuildCandidate(out _,out _),
                            "Invalid coordinate edit clears prior review",checks);
                        field.Text=initial;
                        Check(reviewEditor.Reviewed.IsChecked==false && reviewEditor.TryBuildCandidate(out var restored,out _) && restored is { NeedsReview:true },
                            "Restoring coordinate text does not restore prior review",checks);
                        reviewEditor.Reviewed.IsChecked=true;
                    }
                    reviewEditor.StartX.Text="6";
                    Check(reviewEditor.Reviewed.IsChecked==false && reviewEditor.TryBuildCandidate(out var changed,out _) && changed is { NeedsReview:true },
                        "A valid coordinate edit also clears prior review",checks);
                    Check(reviewEditor.Feedback.Text.Contains("Koordinatlar değişti",StringComparison.Ordinal) &&
                        AutomationProperties.GetName(reviewEditor.Feedback).Contains("Koordinatlar değişti",StringComparison.Ordinal),
                        "Coordinate edit warning is available visually and to automation",checks);
                    reviewEditor.Reviewed.IsChecked=true;
                    Check(string.IsNullOrEmpty(reviewEditor.Feedback.Text) &&
                        !AutomationProperties.GetName(reviewEditor.Feedback).Contains("Koordinatlar değişti",StringComparison.Ordinal),
                        "Cleared grainline edit warning is not retained as an accessible name",checks);
                    Check(reviewEditor.TryBuildCandidate(out var reapproved,out _) && reapproved is { NeedsReview:false } &&
                        studio.ApplyGrainlineEdit(stamp,reapproved) && studio.Selected!.Grainline is { NeedsReview:false } saved && saved.Start==new Point2(6,2),
                        "An explicit new review can be committed with the edited coordinates",checks);
                    Check(session.Undo() && DocumentSession.ContentEquals(created,studio.Document),
                        "Undo restores the prior reviewed grainline after editing",checks);
                }
                finally { reviewEditor.Close(); }
                stamp=studio.CaptureGrainlineEdit()!;
                Check(studio.ApplyGrainlineEdit(stamp,new Grainline { Start=new(6,2),End=new(6,18),NeedsReview=true }) && studio.Selected!.Grainline is { NeedsReview:true },
                    "Editing grainline updates its coordinates and review state",checks);
                stamp=studio.CaptureGrainlineEdit()!;var edited=studio.Document.Clone();
                Check(studio.ApplyGrainlineEdit(stamp,null) && studio.Selected!.Grainline is null && session.Undo() && DocumentSession.ContentEquals(edited,studio.Document),
                    "Removing grainline is undoable",checks);
            }
            finally { window.Close(); }

            foreach(var scenario in new[]{"selection","document","pending"})
            {
                canvas.CancelDrawing();studio.LoadDocument(new ProjectDocument { Pieces=[first,second] });studio.Select(first.Id);
                stamp=studio.CaptureGrainlineEdit()!;
                if(scenario=="selection")studio.Select(second.Id);
                else if(scenario=="document")studio.Commit("Eşzamanlı değişim",()=>studio.Document.Name="Başka proje adı");
                else { studio.SetTool("Çizgi");canvas.RestorePendingDrawing(new RecoveryPendingDrawing("Çizgi",[new Point2(1,2)])); }
                var before=studio.Document.Clone();var undo=session.UndoCount;var redo=session.RedoCount;
                Check(studio.GrainlineEditProblem(stamp) is not null && !studio.ApplyGrainlineEdit(stamp,new Grainline { Start=new(1,1),End=new(1,4) })
                    && DocumentSession.ContentEquals(before,studio.Document) && session.UndoCount==undo && session.RedoCount==redo,
                    $"Stale grainline edit preserves document and history when {scenario} changes",checks);
            }
            canvas.CancelDrawing();studio.SetTool("Seç");studio.LoadDocument(new ProjectDocument { Pieces=[first] });studio.Select(first.Id);
            stamp=studio.CaptureGrainlineEdit()!;studio.ApplyGrainlineEdit(stamp,new Grainline { Start=new(2,3),End=new(2,25),NeedsReview=true });
            var outputNotice=studio.OutputLossNotice();
            Check(outputNotice is not null && outputNotice.Contains("SVG",StringComparison.Ordinal)
                && outputNotice.Contains("iplik yönünü görsel çizgi",StringComparison.OrdinalIgnoreCase)
                && outputNotice.Contains("DXF",StringComparison.Ordinal) && outputNotice.Contains("HPGL",StringComparison.Ordinal)
                && outputNotice.Contains("desteklenmiyor",StringComparison.Ordinal)
                && outputNotice.Contains("fiziksel",StringComparison.OrdinalIgnoreCase)
                && outputNotice.Contains("kesim yolu sanabilir",StringComparison.OrdinalIgnoreCase),
                "Output notice distinguishes SVG grainline drawing from unsupported DXF and HPGL and physical compatibility",checks);
            var savePath=Path.Combine(work,"grainline-ui-roundtrip.tcad");
            Invoke(studio,"SaveTo",savePath);
            using(var json=JsonDocument.Parse(File.ReadAllText(savePath)))
                Check(json.RootElement.ValueKind==JsonValueKind.Array && json.RootElement[0].GetInt32()==4,
                    "Saving a grainline uses a versioned format older readers reject",checks);
            Check(ProjectStorage.Load(savePath).Pieces[0].Grainline is { NeedsReview:true },
                "The saved project retains grainline review state",checks);
            foreach(var (format,extension) in new[]{(1,"dxf"),(3,"plt")})
            {
                var output=Path.Combine(work,$"grainline-guard.{extension}");File.WriteAllText(output,"önceki dosya");
                var before=File.ReadAllBytes(output);var expected=ProjectStorage.CaptureVersion(output);
                var task=studio.StartExportAsync(studio.Document.Clone(),output,format,expected);
                PumpUntil(()=>task.IsCompleted);task.GetAwaiter().GetResult();
                Check(studio.ExportError is not null && studio.ExportError.Contains("İplik yönü",StringComparison.OrdinalIgnoreCase)
                    && before.SequenceEqual(File.ReadAllBytes(output)),
                    $"Grainline {extension} UI export fails without replacing the previous file",checks);
            }
            var svgPath=Path.Combine(work,"grainline-ui.svg");File.WriteAllText(svgPath,"önceki dosya");
            var svgExpected=ProjectStorage.CaptureVersion(svgPath);
            var svgTask=studio.StartExportAsync(studio.Document.Clone(),svgPath,2,svgExpected);
            PumpUntil(()=>svgTask.IsCompleted);svgTask.GetAwaiter().GetResult();
            var svg=System.Xml.Linq.XDocument.Load(svgPath);
            System.Xml.Linq.XNamespace svgNs="http://www.w3.org/2000/svg";
            var marker=svg.Descendants(svgNs+"line").SingleOrDefault(line=>(string?)line.Attribute("data-piece-id")==first.Id.ToString("D"));
            Check(studio.ExportError is null && marker is not null
                && (string?)marker.Attribute("data-needs-review")=="true"
                && (string?)marker.Attribute("x1")=="2" && (string?)marker.Attribute("y1")=="3"
                && (string?)marker.Attribute("x2")=="2" && (string?)marker.Attribute("y2")=="25"
                && marker.Parent?.Attribute("data-tekstilcad-role")?.Value=="grainline",
                "SVG UI export writes the captured piece identity, grainline coordinates and review state",checks);
        }
        finally { canvas.CancelDrawing();studio.SetTool("Seç");SynchronizationContext.SetSynchronizationContext(previousContext); }
    }

    private static void VerifyGrainlineEditing(MainWindow studio,List<string> checks)
    {
        var previousContext=SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(studio.Dispatcher));
        var canvas=(CadCanvas)typeof(MainWindow).GetField("canvas",PrivateInstance)!.GetValue(studio)!;
        var session=(DocumentSession)typeof(MainWindow).GetField("session",PrivateInstance)!.GetValue(studio)!;
        var source=new PatternPiece { Name="İplik yönlü",Points=[new(0,0),new(10,0),new(10,10),new(0,10)],
            Grainline=new Grainline { Start=new(3,2),End=new(3,8),NeedsReview=false } };
        void Finish(Task task) { PumpUntil(()=>task.IsCompleted);task.GetAwaiter().GetResult(); }
        try
        {
            foreach(var operation in new[]{"insert","delete","move"})
            {
                canvas.CancelDrawing();studio.LoadDocument(new ProjectDocument { Pieces=[source] });studio.Select(source.Id);
                var before=studio.Document.Clone();var undo=session.UndoCount;var tools=new TextileToolsWindow(studio);
                try
                {
                    var task=operation switch { "insert"=>tools.InsertVertexAsync("0","5","0"),
                        "delete"=>tools.DeleteVertexAsync("1"),_=>tools.MoveVertexAsync("1","12","0") };
                    Finish(task);var edited=studio.Document.Pieces.Single();
                    Check(edited.Id==source.Id && edited.Grainline is { NeedsReview:true } line &&
                        line.Start==new Point2(3,2) && line.End==new Point2(3,8) && session.UndoCount==undo+1,
                        $"Grainline survives contour {operation} with review required and one undo",checks);
                    var after=studio.Document.Clone();
                    Check(session.Undo() && DocumentSession.ContentEquals(before,studio.Document) &&
                        studio.Document.Pieces[0].Grainline is { NeedsReview:false } && session.Redo() &&
                        DocumentSession.ContentEquals(after,studio.Document),
                        $"Undo and redo restore grainline review state for contour {operation}",checks);
                }
                finally { tools.Close(); }
            }
            canvas.CancelDrawing();studio.LoadDocument(new ProjectDocument { Pieces=[source] });studio.Select(source.Id);
            var stable=studio.Document.Clone();var count=session.UndoCount;var toolWindow=new TextileToolsWindow(studio);
            try
            {
                var offset=(Task)typeof(TextileToolsWindow).GetMethod("CreateOffsetAsync",PrivateInstance)!.Invoke(toolWindow,["1"])!;
                Finish(offset);var result=(TextBlock)typeof(TextileToolsWindow).GetField("result",PrivateInstance)!.GetValue(toolWindow)!;
                var originalPiece=studio.Document.Pieces.Single(p=>p.Id==source.Id);
                var generated=studio.Document.Pieces.Single(p=>p.Id!=source.Id);
                Check(studio.Document.Pieces.Count==2 && studio.SelectedId==generated.Id &&
                    !ReferenceEquals(originalPiece.Points,generated.Points) && !ReferenceEquals(originalPiece.Grainline,generated.Grainline) &&
                    generated.Grainline is { NeedsReview:true } line && line.Start==new Point2(3,2) && line.End==new Point2(3,8) &&
                    originalPiece.Grainline is { NeedsReview:false } && originalPiece.Points.SequenceEqual(stable.Pieces[0].Points) &&
                    session.UndoCount==count+1 && result.Text.Contains("iplik yönü",StringComparison.OrdinalIgnoreCase),
                    "Offset selects an independent grainline copy requiring review and preserves its source",checks);
                Check(session.Undo() && DocumentSession.ContentEquals(stable,studio.Document),
                    "Undo removes the grain-bearing offset and restores its source",checks);
                typeof(TextileToolsWindow).GetMethod("RefreshSources",PrivateInstance)!.Invoke(toolWindow,[source.Id,true]);
                var root=(FrameworkElement)toolWindow.Content;
                root.Measure(new Size(680,540));root.Arrange(new Rect(0,0,680,540));root.UpdateLayout();
                var create=Descendants<Button>(root).Single(b=>Equals(b.Content,"Yeni bedeni projeye ekle"));
                ClickAndWaitForGrade(toolWindow,create);
                var graded=studio.Document.Pieces.Single(p=>p.Id!=source.Id);
                var originalGrade=studio.Document.Pieces.Single(p=>p.Id==source.Id);
                Check(graded.Grainline is { NeedsReview:true } gradedLine &&
                    gradedLine.Start==new Point2(3,2) && gradedLine.End==new Point2(3,8) &&
                    !ReferenceEquals(originalGrade.Grainline,graded.Grainline) &&
                    originalGrade.Grainline is { NeedsReview:false },
                    $"Single grading retains an independent grainline requiring review (source={originalGrade.Grainline?.Start}/{originalGrade.Grainline?.End}/{originalGrade.Grainline?.NeedsReview}, output={graded.Grainline?.Start}/{graded.Grainline?.End}/{graded.Grainline?.NeedsReview})",checks);
                Check(session.UndoCount==count+1,
                    $"Single grading adds one history entry (before={count}, after={session.UndoCount})",checks);
                var gradeStatus=(TextBlock)typeof(TextileToolsWindow).GetField("result",PrivateInstance)!.GetValue(toolWindow)!;
                Check(gradeStatus.Text.Contains("İplik yönü",StringComparison.Ordinal) &&
                    gradeStatus.Text.Contains("kontrol",StringComparison.OrdinalIgnoreCase),
                    $"Single grading informs the user that grainline needs review (status={gradeStatus.Text})",checks);
                Check(session.Undo() && DocumentSession.ContentEquals(stable,studio.Document),
                    "Undo removes the grain-bearing grade and restores its source",checks);
            }
            finally { toolWindow.Close(); }
            studio.LoadDocument(new ProjectDocument { Pieces=[source] });studio.Select(source.Id);
            var batchBefore=studio.Document.Clone();var batchSource=studio.Document.Pieces.Single();var batchAdded=false;
            var batch=new BulkGradingWindow(studio,studio,batchSource,[new(0,0),new(1,0),new(1,1),new(0,1)],()=>true,()=>batchAdded=true);
            try
            {
                batch.Entries.Text="S; -1\nM; 0\nL; 1";Finish(batch.CalculateAsync());
                Check(batch.Ready is { Count:3 } && batch.Ready.All(p=>p.Grainline is { NeedsReview:true } line &&
                    line.Start==new Point2(3,2) && line.End==new Point2(3,8) && !ReferenceEquals(line,batchSource.Grainline)) &&
                    !ReferenceEquals(batch.Ready[0].Grainline,batch.Ready[1].Grainline) &&
                    batch.Status.Text.Contains("İplik yönü",StringComparison.Ordinal) &&
                    batch.Status.Text.Contains("kontrol",StringComparison.OrdinalIgnoreCase) &&
                    DocumentSession.ContentEquals(batchBefore,studio.Document),
                    "Bulk grade preview retains independent grainlines requiring review and warns the user",checks);
                var batchUndo=session.UndoCount;batch.AddReady();
                Check(batchAdded && studio.Document.Pieces.Count==4 && session.UndoCount==batchUndo+1 &&
                    studio.Document.Pieces.Where(p=>p.Id!=source.Id).All(p=>p.Grainline is { NeedsReview:true } line &&
                        line.Start==new Point2(3,2) && line.End==new Point2(3,8)),
                    "Bulk grading commits every reviewed grainline in one undo entry",checks);
                Check(session.Undo() && DocumentSession.ContentEquals(batchBefore,studio.Document),
                    "Undo restores the source after the grain-bearing grade batch",checks);
            }
            finally { batch.Close(); }
            foreach(var scenario in new[] { "cancel", "document", "selection", "allowance", "drawing", "draft" })
            {
                canvas.CancelDrawing();studio.SetTool("Seç");
                var other=source.Clone();other.Id=Guid.NewGuid();other.Grainline=null;
                studio.LoadDocument(new ProjectDocument { Pieces=[source,other] });studio.Select(source.Id);
                var guarded=new TextileToolsWindow(studio);
                using var reached=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
                guarded.BeforeOffsetForVerification=()=> { reached.Set();release.Wait(); };
                var selector=(ComboBox)typeof(TextileToolsWindow).GetField("source",PrivateInstance)!.GetValue(guarded)!;
                var amount=(TextBox)typeof(TextileToolsWindow).GetField("offsetAllowance",PrivateInstance)!.GetValue(guarded)!;
                var name=(TextBox)typeof(MainWindow).GetField("name",PrivateInstance)!.GetValue(studio)!;
                var root=(FrameworkElement)guarded.Content;
                root.Measure(new Size(680,540));root.Arrange(new Rect(0,0,680,540));root.UpdateLayout();
                ((TabControl)typeof(TextileToolsWindow).GetField("workshopTabs",PrivateInstance)!.GetValue(guarded)!).SelectedIndex=1;
                root.Measure(new Size(680,540));root.Arrange(new Rect(0,0,680,540));root.UpdateLayout();
                var hint=Descendants<TextBlock>(root).Single(t=>t.Text.Contains("Kaynak iplik yönü",StringComparison.Ordinal));
                selector.SelectedItem=selector.Items.Cast<PatternPiece>().Single(p=>p.Id==other.Id);
                Check(hint.Visibility==Visibility.Collapsed,"Offset grain hint clears for a source without a grainline",checks);
                selector.SelectedItem=selector.Items.Cast<PatternPiece>().Single(p=>p.Id==source.Id);
                Check(hint.Visibility==Visibility.Visible && hint.Text.Contains(source.Id.ToString("D"),StringComparison.Ordinal)
                    && hint.Text.Contains("yeniden inceleme",StringComparison.OrdinalIgnoreCase),
                    "Offset grain hint follows the selected source and explains review",checks);
                var pending=(Task)typeof(TextileToolsWindow).GetMethod("CreateOffsetAsync",PrivateInstance)!.Invoke(guarded,[amount.Text])!;
                try
                {
                    PumpUntil(()=>reached.IsSet);
                    if(scenario=="cancel") ((Button)typeof(TextileToolsWindow).GetField("cancelOffset",PrivateInstance)!.GetValue(guarded)!).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    else if(scenario=="document")studio.Commit("Hesap sırasında düzenleme",()=>studio.Document.Name="Değişen proje");
                    else if(scenario=="selection")selector.SelectedItem=selector.Items.Cast<PatternPiece>().Single(p=>p.Id==other.Id);
                    else if(scenario=="allowance")amount.Text="2";
                    else if(scenario=="drawing") { studio.SetTool("Çizgi");canvas.RestorePendingDrawing(new RecoveryPendingDrawing("Çizgi",[new Point2(1,2)])); }
                    else name.Text="Uygulanmamış ad";
                    var expectedOffsetContext=studio.Document.Clone();var undo=session.UndoCount;var redo=session.RedoCount;
                    release.Set();Finish(pending);
                    var status=(TextBlock)typeof(TextileToolsWindow).GetField("result",PrivateInstance)!.GetValue(guarded)!;
                    Check(DocumentSession.ContentEquals(expectedOffsetContext,studio.Document) && session.UndoCount==undo && session.RedoCount==redo
                        && (scenario!="drawing" || canvas.HasPendingDrawing) && (scenario!="draft" || (studio.HasPendingCanvasOrPropertyWork && name.Text=="Uygulanmamış ad"))
                        && (scenario!="allowance" || amount.Text=="2")
                        && status.Text.Contains(scenario=="cancel" ? "iptal" : "eklenmedi",StringComparison.OrdinalIgnoreCase),
                        $"Grain-bearing offset {scenario} preserves changed context and does not append a result",checks);
                }
                finally { release.Set();Finish(pending);guarded.Close();canvas.CancelDrawing(); }
            }
            studio.LoadDocument(new ProjectDocument { Pieces=[source] });studio.Select(source.Id);
            var original=studio.Document.Clone();var beforeUndo=session.UndoCount;
            var move=typeof(CadCanvas).GetMethod("MoveSelectionByKeyboard",PrivateInstance)!
                .CreateDelegate<Action<Key,ModifierKeys>>(canvas);
            move(Key.Right,ModifierKeys.None);
            var shifted=studio.Document.Pieces.Single().Grainline;
            Check(shifted is not null && shifted.Start==new Point2(4,2) && shifted.End==new Point2(4,8) &&
                !shifted.NeedsReview && session.UndoCount==beforeUndo+1,
                "Keyboard piece translation moves grainline with contour",checks);
            Check(session.Undo() && DocumentSession.ContentEquals(original,studio.Document),
                "Undo restores translated contour and grainline together",checks);
            studio.LoadDocument(original);studio.Select(source.Id);
            Func<IReadOnlyList<Point2>,Point2,List<Point2>> rotate=(points,center)=>TekstilCAD.Core.Geometry.Rotate(points,90,center);
            var expected=TekstilCAD.Core.Geometry.Rotate([new Point2(3,2),new Point2(3,8)],90,new Point2(5,5));
            Invoke(studio,"Transform",rotate);
            var rotated=studio.Document.Pieces.Single().Grainline;
            Check(rotated is not null && rotated.Start==expected[0] && rotated.End==expected[1] && !rotated.NeedsReview,
                "Piece rotation transforms grainline using the same origin and preserves review state",checks);
            Check(session.Undo() && DocumentSession.ContentEquals(original,studio.Document),
                "Undo restores rotated contour and grainline together",checks);
        }
        finally { canvas.CancelDrawing();SynchronizationContext.SetSynchronizationContext(previousContext); }
    }

    private static void VerifyNumericVertexMove(MainWindow studio,List<string> checks)
    {
        var previousContext=SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(studio.Dispatcher));
        var canvas=(CadCanvas)typeof(MainWindow).GetField("canvas",PrivateInstance)!.GetValue(studio)!;
        var session=(DocumentSession)typeof(MainWindow).GetField("session",PrivateInstance)!.GetValue(studio)!;
        var first=new PatternPiece { Name="Ön",Points=[new(0,0),new(10,0),new(10,10),new(0,10),new(0,0)],
            Metadata=new PieceMetadata { Role="Kesim",Material="Pamuk",Quantity=2 } };
        var second=new PatternPiece { Name="Arka",Points=[new(20,0),new(30,0),new(20,10)] };
        TextileToolsWindow Prepare()
        {
            canvas.CancelDrawing();studio.LoadDocument(new ProjectDocument { Pieces=[first,second] });studio.Select(first.Id);
            return new TextileToolsWindow(studio);
        }
        void Finish(Task task) { PumpUntil(()=>task.IsCompleted);task.GetAwaiter().GetResult(); }
        var tools=Prepare();
        try
        {
            var before=studio.Document.Clone();var undo=session.UndoCount;
            Finish(tools.MoveVertexAsync("0","-2","-1"));
            var edited=studio.Document.Pieces.Single(p=>p.Id==first.Id);
            Check(edited.Points[0]==new Point2(-2,-1) && edited.Points[^1]==edited.Points[0]
                && edited.Metadata?.Role=="Kesim" && edited.Metadata.Material=="Pamuk" && edited.Metadata.Quantity==2
                && session.UndoCount==undo+1,
                "Numeric vertex move preserves identity, metadata and repeated closed endpoint",checks);
            var after=studio.Document.Clone();
            Check(session.Undo() && DocumentSession.ContentEquals(before,studio.Document) && session.Redo() && DocumentSession.ContentEquals(after,studio.Document),
                "Numeric vertex move is a single undoable and redoable change",checks);
            // Undo/redo restores detached objects; reopen the workshop to select
            // the restored live source just as the application requires.
            tools.Close();studio.Select(first.Id);tools=new TextileToolsWindow(studio);
            var stable=studio.Document.Clone();undo=session.UndoCount;var redo=session.RedoCount;
            Finish(tools.MoveVertexAsync("0","-2","-1"));
            Check(DocumentSession.ContentEquals(stable,studio.Document) && session.UndoCount==undo && session.RedoCount==redo,
                "An unchanged numeric vertex creates no history entry",checks);
            Finish(tools.MoveVertexAsync("1","0","10"));
            var result=(TextBlock)typeof(TextileToolsWindow).GetField("result",PrivateInstance)!.GetValue(tools)!;
            Check(result.Text.Contains("uygulanmadı",StringComparison.OrdinalIgnoreCase) && DocumentSession.ContentEquals(stable,studio.Document)
                && session.UndoCount==undo && session.RedoCount==redo,
                "Invalid numeric vertex geometry preserves the project and history",checks);
        }
        finally { tools.Close(); }

        foreach(var scenario in new[]{"project","selection","input","pending","cancel"})
        {
            tools=Prepare();using var reached=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
            var before=studio.Document.Clone();var undo=session.UndoCount;Task? pending=null;
            tools.BeforeVertexMoveForVerification=()=> { reached.Set();release.Wait(); };
            try
            {
                pending=tools.MoveVertexAsync("1","12","0");PumpUntil(()=>reached.IsSet);
                var dispatched=false;studio.Dispatcher.BeginInvoke(new Action(()=>dispatched=true));PumpUntil(()=>dispatched);
                Check(dispatched && tools.VertexMoveRunningForVerification,$"Numeric move {scenario}: worker leaves dispatcher responsive",checks);
                if(scenario=="project")studio.Commit("Kaynak değişti",()=>studio.Document.Name="Değişti");
                else if(scenario=="selection")studio.Select(second.Id);
                else if(scenario=="input")((TextBox)typeof(TextileToolsWindow).GetField("vertexXInput",PrivateInstance)!.GetValue(tools)!).Text="13";
                else if(scenario=="pending") { studio.SetTool("Çizgi");canvas.RestorePendingDrawing(new RecoveryPendingDrawing("Çizgi",[new Point2(1,2)])); }
                else { tools.RequestVertexMoveCancellation();tools.Close();Check(tools.VertexMoveRunningForVerification,"Numeric move cannot close while cancellation is pending",checks); }
                release.Set();Finish(pending);
                var expected=before.Clone();if(scenario=="project")expected.Name="Değişti";
                var result=(TextBlock)typeof(TextileToolsWindow).GetField("result",PrivateInstance)!.GetValue(tools)!;
                Check(DocumentSession.ContentEquals(expected,studio.Document) && session.UndoCount==undo+(scenario=="project"?1:0)
                    && (scenario=="cancel" ? result.Text.Contains("iptal",StringComparison.OrdinalIgnoreCase) : result.Text.Contains("uygulanmadı",StringComparison.OrdinalIgnoreCase)),
                    $"Numeric move {scenario}: stale or cancelled result never changes geometry",checks);
            }
            finally { release.Set();if(pending is not null)Finish(pending);canvas.CancelDrawing();studio.SetTool("Seç");tools.Close(); }
        }

        // Exercise the UI's large-source worker/cancellation boundary without
        // claiming the cost of a completed 4097-point validation was measured.
        var big=new PatternPiece { Name="Büyük",Points=Enumerable.Range(0,4097).Select(i=>
        { var angle=Math.Tau*i/4097;return new Point2(100*Math.Cos(angle),100*Math.Sin(angle)); }).ToList() };
        canvas.CancelDrawing();studio.LoadDocument(new ProjectDocument { Pieces=[big] });studio.Select(big.Id);tools=new TextileToolsWindow(studio);
        using(var reached=new ManualResetEventSlim())using(var release=new ManualResetEventSlim())
        {
            var before=studio.Document.Clone();Task? pending=null;tools.BeforeVertexMoveForVerification=()=> { reached.Set();release.Wait(); };
            try
            {
                pending=tools.MoveVertexAsync("0","101","0");PumpUntil(()=>reached.IsSet);
                tools.RequestVertexMoveCancellation();release.Set();Finish(pending);
                Check(DocumentSession.ContentEquals(before,studio.Document) && !tools.VertexMoveRunningForVerification,
                    "A cancelled 4097-point numeric vertex operation leaves the source unchanged",checks);
            }
            finally { release.Set();if(pending is not null)Finish(pending);tools.Close(); }
        }
        canvas.CancelDrawing();studio.LoadDocument(new ProjectDocument { Name="Doğrulama" });studio.SetTool("Seç");
        SynchronizationContext.SetSynchronizationContext(previousContext);
    }

    private static void VerifyReverseClosedContour(MainWindow studio,List<string> checks)
    {
        var previous=SynchronizationContext.Current;SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(studio.Dispatcher));
        var piece=new PatternPiece { Points=[new(0,0),new(8,0),new(10,2),new(12,0),new(20,0),new(20,10),new(0,10),new(0,0)], Grainline=new Grainline { Start=new(5,3),End=new(5,8),NeedsReview=false } };
        var rule=new GradingRuleSet { SourcePieceId=piece.Id,SourceClosed=true,SourcePoints=[..piece.Points],Deltas=[new(0,0),new(1,0),new(2,0),new(3,0),new(4,0),new(5,0),new(6,0),new(0,0)] };
        studio.LoadDocument(new ProjectDocument { Pieces=[piece],GradingRules=[rule] });studio.Select(piece.Id);
        var before=studio.Document.Clone();var session=(DocumentSession)typeof(MainWindow).GetField("session",PrivateInstance)!.GetValue(studio)!;
        var tools=new TextileToolsWindow(studio);
        try
        {
            var task=tools.ReverseClosedContourAsync();PumpUntil(()=>task.IsCompleted);task.GetAwaiter().GetResult();
            var after=studio.Document.Clone();
            Check(after.Pieces[0].Points.SequenceEqual(new[] { piece.Points[0],piece.Points[6],piece.Points[5],piece.Points[4],piece.Points[3],piece.Points[2],piece.Points[1],piece.Points[7] }) &&
                after.GradingRules[0].SourcePoints.SequenceEqual(after.Pieces[0].Points) && after.GradingRules[0].Deltas[5]==rule.Deltas[2] && after.Pieces[0].Grainline is { NeedsReview:false } && session.UndoCount==1,
                "Closed contour reversal preserves notch and matching grading",checks);
            Check(session.Undo() && DocumentSession.ContentEquals(before,studio.Document),"Closed reversal undo restores both",checks);
            Check(session.Redo() && DocumentSession.ContentEquals(after,studio.Document),"Closed reversal redo restores both",checks);
        }
        finally { tools.Close();studio.LoadDocument(new ProjectDocument());SynchronizationContext.SetSynchronizationContext(previous); }
    }

    private static void VerifyReverseOpenPath(MainWindow studio,List<string> checks)
    {
        var previous=SynchronizationContext.Current;SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(studio.Dispatcher));
        var piece=new PatternPiece { Name="Açık",Closed=false,Points=[new(0,0),new(10,0),new(10,10)],Metadata=new PieceMetadata { Quantity=2 },Grainline=new Grainline { Start=new(2,2),End=new(2,5),NeedsReview=true } };
        var rule=new GradingRuleSet { SourcePieceId=piece.Id,SourceClosed=false,SourcePoints=[..piece.Points],Deltas=[new(1,0),new(0,0),new(0,0)] };
        studio.LoadDocument(new ProjectDocument { Pieces=[piece],GradingRules=[rule] });studio.Select(piece.Id);
        var before=studio.Document.Clone();var session=(DocumentSession)typeof(MainWindow).GetField("session",PrivateInstance)!.GetValue(studio)!;
        var tools=new TextileToolsWindow(studio);
        try
        {
            var task=tools.ReverseOpenPathAsync();PumpUntil(()=>task.IsCompleted);task.GetAwaiter().GetResult();
            Check(studio.Document.Pieces[0].Points.SequenceEqual(piece.Points.AsEnumerable().Reverse()) && studio.Document.Pieces[0].Metadata?.Quantity==2 && studio.Document.Pieces[0].Grainline is { NeedsReview:true } &&
                studio.Document.GradingRules[0].SourcePoints.SequenceEqual(piece.Points.AsEnumerable().Reverse()) &&
                studio.Document.GradingRules[0].Deltas.SequenceEqual(rule.Deltas.AsEnumerable().Reverse()) && session.UndoCount==1,
                "Reversing an open path reorders the matching grading rule in the same undo step",checks);
            Check(session.Undo() && DocumentSession.ContentEquals(before,studio.Document),"Open path reversal is one undo step",checks);
        }
        finally { tools.Close();studio.LoadDocument(new ProjectDocument());SynchronizationContext.SetSynchronizationContext(previous); }
    }

    private static void VerifyReverseOpenPathGradingReview(MainWindow studio,List<string> checks)
    {
        var previous=SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(studio.Dispatcher));
        try
        {
            foreach(var stale in new[] { false,true })
            {
                var piece=new PatternPiece { Name="Açık",Closed=false,Points=[new(0,0),new(10,0),new(10,10)] };
                var rule=new GradingRuleSet { SourcePieceId=piece.Id,SourceClosed=false,
                    SourcePoints=stale ? [new(0,0),new(9,0),new(10,10)] : [..piece.Points],
                    Deltas=[new(1,0),new(2,0),new(3,0)] };
                studio.LoadDocument(new ProjectDocument { Pieces=[piece],GradingRules=[rule] });studio.Select(piece.Id);
                var before=studio.Document.Clone();var session=(DocumentSession)typeof(MainWindow).GetField("session",PrivateInstance)!.GetValue(studio)!;
                var tools=new TextileToolsWindow(studio);
                try
                {
                    var table=(DataGrid)typeof(TextileToolsWindow).GetField("grades",PrivateInstance)!.GetValue(tools)!;
                    var rows=table.Items.Cast<TextileToolsWindow.GradeRow>().ToArray();
                    if(!stale)rows[0].Dx="7"; // A typed but unapplied increment must survive the edit.
                    var task=tools.ReverseOpenPathAsync();PumpUntil(()=>task.IsCompleted);task.GetAwaiter().GetResult();
                    var result=(TextBlock)typeof(TextileToolsWindow).GetField("result",PrivateInstance)!.GetValue(tools)!;
                    var sourceInfo=(TextBlock)typeof(TextileToolsWindow).GetField("sourceInfo",PrivateInstance)!.GetValue(tools)!;
                    Check(studio.Document.Pieces[0].Points.SequenceEqual(piece.Points.AsEnumerable().Reverse()) &&
                        JsonSerializer.Serialize(studio.Document.GradingRules)==JsonSerializer.Serialize(before.GradingRules) &&
                        session.UndoCount==1 && table.IsReadOnly && sourceInfo.Text.Contains("Geometri değişti") &&
                        result.Text.Contains("Mevcut serileme kuralı önceki kontura aittir") &&
                        (stale || table.Items.Cast<TextileToolsWindow.GradeRow>().First().Dx=="7"),
                        stale ? "Reversing with a stale grading rule preserves it and asks for review" :
                            "Reversing with an unapplied grading draft preserves it and asks for review",checks);
                    Check(session.Undo() && DocumentSession.ContentEquals(before,studio.Document),
                        stale ? "Undo restores contour with stale grading rule" : "Undo restores contour with unapplied grading draft",checks);
                }
                finally { tools.Close(); }
            }
        }
        finally { studio.LoadDocument(new ProjectDocument());SynchronizationContext.SetSynchronizationContext(previous); }
    }

    private static void VerifyNotchEdit(MainWindow studio,string reportPath,List<string> checks)
    {
        var previous=SynchronizationContext.Current;SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(studio.Dispatcher));
        var piece=new PatternPiece { Name="Çentik",Points=[new(0,0),new(20,0),new(20,10),new(0,10),new(0,0)],Metadata=new PieceMetadata { Quantity=2 } };
        var rule=new GradingRuleSet { SourcePieceId=piece.Id,SourceClosed=true,SourcePoints=[..piece.Points],Deltas=piece.Points.Select(_=>new Point2(0,0)).ToList() };
        var session=(DocumentSession)typeof(MainWindow).GetField("session",PrivateInstance)!.GetValue(studio)!;
        void Prepare() { studio.LoadDocument(new ProjectDocument { Pieces=[piece.Clone()],GradingRules=[rule.Clone()] });studio.Select(piece.Id); }
        void Finish(Task task) { PumpUntil(()=>task.IsCompleted);task.GetAwaiter().GetResult(); }
        try
        {
            Prepare();var before=studio.Document.Clone();var tools=new TextileToolsWindow(studio);
            try
            {
                var root=(FrameworkElement)tools.Content;
                var tabs=(TabControl)typeof(TextileToolsWindow).GetField("workshopTabs",PrivateInstance)!.GetValue(tools)!;
                tabs.SelectedIndex=1;
                root.Measure(new Size(680,540));root.Arrange(new Rect(0,0,680,540));root.UpdateLayout();
                TextBox Field(string name)=>(TextBox)typeof(TextileToolsWindow).GetField(name,PrivateInstance)!.GetValue(tools)!;
                var index=Field("vertexIndexInput");var fraction=Field("notchFractionInput");
                var width=Field("notchWidthInput");var depth=Field("notchDepthInput");
                var button=Descendants<Button>(root).Single(b=>Equals(b.Content,"Sonraki kenara V çentik aç"));
                Check(Descendants<TextBlock>(root).Any(t=>t.Text=="Çentik ağız genişliği · mm") &&
                    Descendants<TextBlock>(root).Any(t=>t.Text=="Çentik derinliği · mm") &&
                    Descendants<TextBlock>(root).Any(t=>t.Text.Contains("fiziksel olarak içe doğru V kesilir")),
                    "Notch controls identify independent dimensions and the physical cut",checks);
                var scroller=Descendants<ScrollViewer>(root).Single(v=>v.IsAncestorOf(width));
                void ScrollTo(FrameworkElement target)
                {
                    var content=(FrameworkElement)scroller.Content;
                    var y=target.TransformToAncestor(content).Transform(new Point(0,0)).Y;
                    scroller.ScrollToVerticalOffset(Math.Max(0,y-10));root.UpdateLayout();
                }
                ScrollTo(width);
                Check(scroller.ScrollableHeight>0 && FullyVisible(width,root) && FullyVisible(depth,root),
                    "Small workshop can scroll to both independent notch fields",checks);
                ScrollTo(button);
                Check(FullyVisible(button,root),"Small workshop can scroll to the notch cut button",checks);
                Render(tools,Path.ChangeExtension(reportPath,".notch-controls-compact.png"),680,540);
                index.Text="3";fraction.Text="0.5";width.Text="3";depth.Text="2";
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Finish(tools.VertexMoveTaskForVerification ?? throw new InvalidOperationException("Çentik düğmesi görevi başlatmadı."));
                var cut=studio.Document.Pieces[0].Points;
                Check(Math.Abs(TekstilCAD.Core.Geometry.Distance(cut[4],cut[6])-3)<1e-9 &&
                    Math.Abs(TekstilCAD.Core.Geometry.Distance((cut[4]+cut[6])*.5,cut[5])-2)<1e-9,
                    "Notch button applies the entered independent width and depth",checks);
                var result=(TextBlock)typeof(TextileToolsWindow).GetField("result",PrivateInstance)!.GetValue(tools)!;
                Check(studio.Document.Pieces[0].Points.Count==8 && studio.Document.Pieces[0].Points[0]==studio.Document.Pieces[0].Points[^1] &&
                    JsonSerializer.Serialize(studio.Document.GradingRules)==JsonSerializer.Serialize(before.GradingRules) && result.Text.Contains("serileme",StringComparison.OrdinalIgnoreCase) && session.UndoCount==1,
                    "Notch keeps explicit closure and original grading rule with review warning",checks);
                Check(session.Undo() && DocumentSession.ContentEquals(before,studio.Document),"Notch is one undoable edit",checks);
            }
            finally { tools.Close(); }
            foreach(var scenario in new[]{"cancel","source","width"})
            {
                Prepare();tools=new TextileToolsWindow(studio);using var reached=new ManualResetEventSlim();using var release=new ManualResetEventSlim();Task? pending=null;
                tools.BeforeVertexMoveForVerification=()=> { reached.Set();release.Wait(); };
                try
                {
                    pending=tools.InsertNotchAsync("0","0.5","3","2");PumpUntil(()=>reached.IsSet);
                    if(scenario=="cancel")tools.RequestVertexMoveCancellation();
                    else if(scenario=="source")studio.Commit("Kaynak değişti",()=>studio.Document.Pieces[0].Points[1]=new Point2(21,0));
                    else ((TextBox)typeof(TextileToolsWindow).GetField("notchWidthInput",PrivateInstance)!.GetValue(tools)!).Text="4";
                    var expected=studio.Document.Clone();var undo=session.UndoCount;release.Set();Finish(pending);
                    var message=(TextBlock)typeof(TextileToolsWindow).GetField("result",PrivateInstance)!.GetValue(tools)!;
                    Check(DocumentSession.ContentEquals(expected,studio.Document) && session.UndoCount==undo && message.Text.Contains(scenario=="cancel"?"iptal":"uygulanmadı",StringComparison.OrdinalIgnoreCase),
                        $"Notch {scenario} does not apply stale or cancelled geometry",checks);
                }
                finally { release.Set();if(pending is not null)Finish(pending);tools.Close(); }
            }
        }
        finally { studio.LoadDocument(new ProjectDocument());SynchronizationContext.SetSynchronizationContext(previous); }
    }
    private static void VerifyEdgeDistance(MainWindow studio,List<string> checks)
    {
        var previous=SynchronizationContext.Current;SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(studio.Dispatcher));
        var session=(DocumentSession)typeof(MainWindow).GetField("session",PrivateInstance)!.GetValue(studio)!;
        var piece=new PatternPiece { Name="Eğimli kenar",Closed=false,Points=[new(0,0),new(60,80)],Metadata=new PieceMetadata { Quantity=2 } };
        try
        {
            foreach(var scenario in new[] { "commit", "stale", "cancel" })
            {
                studio.LoadDocument(new ProjectDocument { Pieces=[piece.Clone()] });studio.Select(piece.Id);
                var before=studio.Document.Clone();var undo=session.UndoCount;var tools=new TextileToolsWindow(studio);
                using var reached=new ManualResetEventSlim();using var release=new ManualResetEventSlim();Task? task=null;
                try
                {
                    tools.BeforeVertexMoveForVerification=()=> { reached.Set();release.Wait(); };
                    task=tools.InsertEdgeDistanceAsync("0","30");PumpUntil(()=>reached.IsSet);
                    if(scenario=="stale")((TextBox)typeof(TextileToolsWindow).GetField("edgeDistanceInput",PrivateInstance)!.GetValue(tools)!).Text="40";
                    if(scenario=="cancel")tools.RequestVertexMoveCancellation();
                    release.Set();PumpUntil(()=>task.IsCompleted);task.GetAwaiter().GetResult();
                    if(scenario=="commit")
                    {
                        var edited=studio.Document.Pieces.Single();
                        Check(edited.Id==piece.Id && edited.Metadata?.Quantity==2 && edited.Points[1]==new Point2(18,24) && session.UndoCount==undo+1,
                            "Distance insertion commits diagonal geometry and preserves identity and metadata",checks);
                        Check(session.Undo() && DocumentSession.ContentEquals(before,studio.Document),"Distance insertion is one undo step",checks);
                    }
                    else Check(DocumentSession.ContentEquals(before,studio.Document) && session.UndoCount==undo && !tools.VertexMoveRunningForVerification,
                        $"Distance insertion {scenario} leaves the document and history unchanged",checks);
                }
                finally { release.Set();if(task is not null) { PumpUntil(()=>task.IsCompleted);task.GetAwaiter().GetResult(); } tools.Close(); }
            }
        }
        finally { studio.LoadDocument(new ProjectDocument());SynchronizationContext.SetSynchronizationContext(previous); }
    }

    private static void VerifyEdgePercentage(MainWindow studio,List<string> checks)
    {
        var previous=SynchronizationContext.Current;SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(studio.Dispatcher));
        var session=(DocumentSession)typeof(MainWindow).GetField("session",PrivateInstance)!.GetValue(studio)!;
        var piece=new PatternPiece { Name="Eğimli kenar",Closed=false,Points=[new(0,0),new(60,80)],Metadata=new PieceMetadata { Quantity=2 } };
        var rule=new GradingRuleSet { SourcePieceId=piece.Id,SourceClosed=false,SourcePoints=[..piece.Points],Deltas=[new(0,0),new(10,10)] };
        try
        {
            foreach(var scenario in new[] { "commit", "stale", "cancel" })
            {
                studio.LoadDocument(new ProjectDocument { Pieces=[piece.Clone()],GradingRules=[rule.Clone()] });studio.Select(piece.Id);
                var before=studio.Document.Clone();var undo=session.UndoCount;var tools=new TextileToolsWindow(studio);
                using var reached=new ManualResetEventSlim();using var release=new ManualResetEventSlim();Task? task=null;
                try
                {
                    tools.BeforeVertexMoveForVerification=()=> { reached.Set();release.Wait(); };
                    task=tools.InsertEdgePercentageAsync("0","30");PumpUntil(()=>reached.IsSet);
                    if(scenario=="stale")((TextBox)typeof(TextileToolsWindow).GetField("edgePercentageInput",PrivateInstance)!.GetValue(tools)!).Text="40";
                    if(scenario=="cancel")tools.RequestVertexMoveCancellation();
                    release.Set();PumpUntil(()=>task.IsCompleted);task.GetAwaiter().GetResult();
                    if(scenario=="commit")
                    {
                        var edited=studio.Document.Pieces.Single();
                        Check(edited.Id==piece.Id && edited.Metadata?.Quantity==2 && edited.Points[1]==new Point2(18,24) && session.UndoCount==undo+1,
                            "Percentage insertion commits diagonal geometry and preserves identity and metadata",checks);
                        var mapped=studio.Document.GradingRules.Single();
                        Check(mapped.SourcePoints.SequenceEqual(edited.Points) && mapped.Deltas.SequenceEqual(new[] { new Point2(0,0),new Point2(3,3),new Point2(10,10) }),
                            "Percentage insertion interpolates the matching grading rule",checks);
                        Check(session.Undo() && DocumentSession.ContentEquals(before,studio.Document),"Percentage insertion is one undo step",checks);
                    }
                    else Check(DocumentSession.ContentEquals(before,studio.Document) && session.UndoCount==undo && !tools.VertexMoveRunningForVerification,
                        $"Percentage insertion {scenario} leaves the document and history unchanged",checks);
                }
                finally { release.Set();if(task is not null) { PumpUntil(()=>task.IsCompleted);task.GetAwaiter().GetResult(); } tools.Close(); }
            }
        }
        finally { studio.LoadDocument(new ProjectDocument());SynchronizationContext.SetSynchronizationContext(previous); }
    }

    private static void VerifyEdgeMidpoint(MainWindow studio,List<string> checks)
    {
        var previous=SynchronizationContext.Current;SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(studio.Dispatcher));
        var piece=new PatternPiece { Name="Kenar",Points=[new(0,0),new(10,0),new(10,10),new(0,10)],Metadata=new PieceMetadata { Quantity=2 } };
        var rule=new GradingRuleSet { SourcePieceId=piece.Id,SourceClosed=true,SourcePoints=[..piece.Points],Deltas=[new(0,0),new(0,0),new(0,0),new(0,0)] };
        studio.LoadDocument(new ProjectDocument { Pieces=[piece],GradingRules=[rule] });studio.Select(piece.Id);
        var before=studio.Document.Clone();var session=(DocumentSession)typeof(MainWindow).GetField("session",PrivateInstance)!.GetValue(studio)!;
        var tools=new TextileToolsWindow(studio);
        try
        {
            var task=tools.InsertEdgeMidpointAsync("3");PumpUntil(()=>task.IsCompleted);task.GetAwaiter().GetResult();
            var edited=studio.Document.Pieces.Single();
            var updatedRule=studio.Document.GradingRules.Single();
            Check(edited.Points.Count==5 && edited.Points[4]==new Point2(0,5) && edited.Metadata?.Quantity==2 &&
                updatedRule.SourcePoints.SequenceEqual(edited.Points) && updatedRule.Deltas.Count==5 && updatedRule.Deltas[4]==new Point2(0,0) && session.UndoCount==1,
                "Closing edge midpoint preserves piece metadata and interpolates the grading rule",checks);
            var status=(TextBlock)typeof(TextileToolsWindow).GetField("result",PrivateInstance)!.GetValue(tools)!;
            Check(status.Text.Contains("komşu artışların ortalamasından",StringComparison.Ordinal),
                "Midpoint insertion identifies the interpolated grading increment",checks);
            Check(session.Undo() && DocumentSession.ContentEquals(before,studio.Document),"Midpoint insertion is one undo step",checks);
        }
        finally { tools.Close();studio.LoadDocument(new ProjectDocument());SynchronizationContext.SetSynchronizationContext(previous); }
    }

    private static void VerifyNumericVertexInsertDelete(MainWindow studio,List<string> checks)
    {
        var previous=SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(studio.Dispatcher));
        var canvas=(CadCanvas)typeof(MainWindow).GetField("canvas",PrivateInstance)!.GetValue(studio)!;
        var session=(DocumentSession)typeof(MainWindow).GetField("session",PrivateInstance)!.GetValue(studio)!;
        void Finish(Task task) { PumpUntil(()=>task.IsCompleted);task.GetAwaiter().GetResult(); }
        try
        {
            foreach(var operation in new[] { "insert","delete-first","delete-last" })
            {
                var closed=new PatternPiece { Name="Kapalı",Points=[new(0,0),new(10,0),new(10,10),new(0,10),new(0,0)] };
                canvas.CancelDrawing();studio.LoadDocument(new ProjectDocument { Pieces=[closed] });studio.Select(closed.Id);
                var before=studio.Document.Clone();var undo=session.UndoCount;var tools=new TextileToolsWindow(studio);
                try
                {
                    var task=operation=="insert" ? tools.InsertVertexAsync("4","5","0") : tools.DeleteVertexAsync(operation=="delete-first"?"0":"4");
                    Finish(task);var edited=studio.Document.Pieces.Single(p=>p.Id==closed.Id);
                    Check(edited.Points[0]==edited.Points[^1] && edited.Points.Count==(operation=="insert"?6:4) && session.UndoCount==undo+1,
                        $"Numeric {operation} treats an explicit closing point consistently",checks);
                    Check(session.Undo() && DocumentSession.ContentEquals(before,studio.Document),$"Numeric {operation} closing edit has one undo",checks);
                }
                finally { tools.Close(); }
            }
            foreach(var operation in new[] { "insert","delete" })
            {
                var big=new PatternPiece { Name="Büyük kontur",Metadata=new PieceMetadata { Role="Kesim",Material="Pamuk",Quantity=2 },
                    Points=Enumerable.Range(0,4097).Select(i=> { var a=Math.Tau*i/4097;return new Point2(100*Math.Cos(a),100*Math.Sin(a)); }).ToList() };
                canvas.CancelDrawing();studio.LoadDocument(new ProjectDocument { Pieces=[big] });studio.Select(big.Id);
                var before=studio.Document.Clone();var undo=session.UndoCount;var tools=new TextileToolsWindow(studio);
                try
                {
                    var point=(big.Points[0]+big.Points[1])*.5;
                    var task=operation=="insert" ? tools.InsertVertexAsync("0",point.X.ToString("R",System.Globalization.CultureInfo.InvariantCulture),point.Y.ToString("R",System.Globalization.CultureInfo.InvariantCulture)) : tools.DeleteVertexAsync("1");
                    Finish(task);
                    var edited=studio.Document.Pieces.Single(p=>p.Id==big.Id);
                    Check(edited.Points.Count==4097+(operation=="insert"?1:-1) && edited.Metadata?.Role=="Kesim"
                        && edited.Metadata.Material=="Pamuk" && edited.Metadata.Quantity==2 && session.UndoCount==undo+1,
                        $"Large numeric {operation} preserves identity and metadata and commits once",checks);
                    Check(session.Undo() && DocumentSession.ContentEquals(before,studio.Document),$"Large numeric {operation} restores original with one undo",checks);
                }
                finally { tools.Close(); }
            }
            foreach(var operation in new[] { "insert","delete" })
            foreach(var scenario in new[] { "project","input","pending","cancel" })
            {
                var piece=new PatternPiece { Name="Kaynak",Points=[new(0,0),new(10,0),new(10,10),new(0,10)] };
                canvas.CancelDrawing();studio.LoadDocument(new ProjectDocument { Pieces=[piece] });studio.Select(piece.Id);
                var before=studio.Document.Clone();var undo=session.UndoCount;var tools=new TextileToolsWindow(studio);
                using var reached=new ManualResetEventSlim();using var release=new ManualResetEventSlim();Task? task=null;
                tools.BeforeVertexMoveForVerification=()=> { reached.Set();release.Wait(); };
                try
                {
                    task=operation=="insert" ? tools.InsertVertexAsync("0","5","0") : tools.DeleteVertexAsync("1");
                    PumpUntil(()=>reached.IsSet);
                    if(scenario=="project")studio.Commit("Harici değişiklik",()=>studio.Document.Name="Değişti");
                    else if(scenario=="input")((TextBox)typeof(TextileToolsWindow).GetField("vertexIndexInput",PrivateInstance)!.GetValue(tools)!).Text="2";
                    else if(scenario=="pending") { studio.SetTool("Çizgi");canvas.RestorePendingDrawing(new RecoveryPendingDrawing("Çizgi",[new Point2(1,2)])); }
                    else { tools.RequestVertexMoveCancellation();tools.Close();Check(tools.VertexMoveRunningForVerification,$"Numeric {operation} waits for cancellation before closing",checks); }
                    release.Set();Finish(task);
                    var expected=before.Clone();if(scenario=="project")expected.Name="Değişti";
                    Check(DocumentSession.ContentEquals(expected,studio.Document) && session.UndoCount==undo+(scenario=="project"?1:0)
                        && !tools.VertexMoveRunningForVerification,
                        $"Numeric {operation} {scenario} preserves source geometry and only accepted history",checks);
                }
                finally { release.Set();if(task is not null)Finish(task);canvas.CancelDrawing();studio.SetTool("Seç");tools.Close(); }
            }
        }
        finally { canvas.CancelDrawing();studio.LoadDocument(new ProjectDocument { Name="Doğrulama" });studio.SetTool("Seç");SynchronizationContext.SetSynchronizationContext(previous); }
    }

    private static void VerifyBasicNestingAsync(MainWindow studio,string reportPath,List<string> checks)
    {
        var canvas=(CadCanvas)typeof(MainWindow).GetField("canvas",PrivateInstance)!.GetValue(studio)!;
        var session=(DocumentSession)typeof(MainWindow).GetField("session",PrivateInstance)!.GetValue(studio)!;
        var old=SynchronizationContext.Current;SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(studio.Dispatcher));
        var piece=new PatternPiece { Name="Temel pastal",Points=[new(40,40),new(60,40),new(60,60),new(40,60)] };
        TextileToolsWindow Prepare()
        {
            canvas.CancelDrawing();studio.LoadDocument(new ProjectDocument { Pieces=[piece.Clone()] });studio.Select(piece.Id);
            return new TextileToolsWindow(studio);
        }
        try
        {
            var tools=Prepare();var before=studio.Document.Clone();var undo=session.UndoCount;
            using(var reached=new ManualResetEventSlim())using(var release=new ManualResetEventSlim())
            {
                Task? pending=null;
                try
                {
                    tools.BeforeBasicNestingForVerification=()=>{reached.Set();if(!release.Wait(TimeSpan.FromSeconds(30)))throw new TimeoutException();};
                    pending=tools.CalculateBasicNestingAsync();PumpUntil(()=>reached.IsSet);
                    var dispatched=false;studio.Dispatcher.BeginInvoke(new Action(()=>dispatched=true));PumpUntil(()=>dispatched);
                    var duplicate=tools.CalculateBasicNestingAsync();PumpUntil(()=>duplicate.IsCompleted);duplicate.GetAwaiter().GetResult();
                    Render(tools,Path.ChangeExtension(reportPath,".basic-nesting-cancel.png"),680,540);
                    var cancel=(Button)typeof(TextileToolsWindow).GetField("cancelBasicNesting",PrivateInstance)!.GetValue(tools)!;
                    Check(dispatched && tools.BasicNestingRunningForVerification && !pending.IsCompleted && cancel.IsEnabled && cancel.Visibility==Visibility.Visible && FullyVisible(cancel,(FrameworkElement)tools.Content),
                        "Basic nesting keeps dispatcher responsive and exposes cancellation while worker runs",checks);
                    tools.RequestBasicNestingCancellation();tools.Close();
                    Check(tools.BasicNestingRunningForVerification && !cancel.IsEnabled && !(bool)typeof(TextileToolsWindow).GetField("windowClosed",PrivateInstance)!.GetValue(tools)!
                        && DocumentSession.ContentEquals(before,studio.Document),"Basic nesting waits for cancellation before closing and preserves source",checks);
                    release.Set();PumpUntil(()=>pending.IsCompleted);pending.GetAwaiter().GetResult();
                    var result=(TextBlock)typeof(TextileToolsWindow).GetField("result",PrivateInstance)!.GetValue(tools)!;
                    Check(!tools.BasicNestingRunningForVerification && result.Text.Contains("iptal",StringComparison.OrdinalIgnoreCase)
                        && DocumentSession.ContentEquals(before,studio.Document) && session.UndoCount==undo,
                        "Cancelled basic nesting applies no result and adds no history",checks);
                }
                finally { release.Set();if(pending is not null)PumpUntil(()=>pending.IsCompleted);tools.Close(); }
            }

            tools=Prepare();before=studio.Document.Clone();undo=session.UndoCount;
            try
            {
                var root=(FrameworkElement)tools.Content;
                root.Measure(new Size(1024,640));root.Arrange(new Rect(0,0,1024,640));root.UpdateLayout();
                var tabs=Descendants<TabControl>(root).Single();tabs.SelectedIndex=2;
                root.Measure(new Size(1024,640));root.Arrange(new Rect(0,0,1024,640));root.UpdateLayout();
                var impact=Descendants<TextBlock>(root).Single(t=>t.Text.Contains("Geri alma geçmişi boyut sınırına tabidir",StringComparison.Ordinal));
                Check(impact.Text.Contains("farklı adla kaydedin",StringComparison.OrdinalIgnoreCase)
                    && impact.Text.Contains("Adetli pastal önizlemesini",StringComparison.Ordinal)
                    && AutomationProperties.GetName(impact)==impact.Text,
                    "Basic nesting explains the history limit and a source-preserving alternative before calculation",checks);
                var success=tools.CalculateBasicNestingAsync();PumpUntil(()=>success.IsCompleted);success.GetAwaiter().GetResult();
                Check(session.UndoCount==undo+1 && !DocumentSession.ContentEquals(before,studio.Document),"Basic nesting commits one undoable change",checks);
                var result=(TextBlock)typeof(TextileToolsWindow).GetField("result",PrivateInstance)!.GetValue(tools)!;
                Check(result.Text.Contains("Hesaplanan boy",StringComparison.Ordinal) && result.Text.Contains("işlem geçmişini kontrol edin",StringComparison.OrdinalIgnoreCase)
                    && !result.Text.Contains("geri alınabilir",StringComparison.OrdinalIgnoreCase),
                    "Changed basic nesting reports the measured length without promising undo beyond the history limit",checks);
                Check(session.Undo() && DocumentSession.ContentEquals(before,studio.Document),"Undo restores source before basic nesting",checks);
            }
            finally { tools.Close(); }

            var alreadyPlaced=new PatternPiece { Name="Zaten yerleştirilmiş",Points=[new(0,0),new(10,0),new(10,10),new(0,10)] };
            canvas.CancelDrawing();studio.LoadDocument(new ProjectDocument { Pieces=[alreadyPlaced] });studio.Select(alreadyPlaced.Id);
            tools=new TextileToolsWindow(studio);before=studio.Document.Clone();undo=session.UndoCount;
            var redo=session.RedoCount;var wasDirty=session.IsDirty;
            try
            {
                var unchanged=tools.CalculateBasicNestingAsync();PumpUntil(()=>unchanged.IsCompleted);unchanged.GetAwaiter().GetResult();
                var result=(TextBlock)typeof(TextileToolsWindow).GetField("result",PrivateInstance)!.GetValue(tools)!;
                Check(DocumentSession.ContentEquals(before,studio.Document) && session.UndoCount==undo && session.RedoCount==redo
                    && session.IsDirty==wasDirty && studio.SelectedId==alreadyPlaced.Id,
                    "Already placed basic nesting preserves document, selection, dirty state and history",checks);
                Check(result.Text.Contains("değişmedi",StringComparison.OrdinalIgnoreCase)
                    && !result.Text.Contains("geri alınabilir",StringComparison.OrdinalIgnoreCase),
                    "Unchanged basic nesting does not claim a new undoable placement",checks);
            }
            finally { tools.Close(); }

            tools=Prepare();undo=session.UndoCount;
            using(var reached=new ManualResetEventSlim())using(var release=new ManualResetEventSlim())
            {
                Task? pending=null;
                try
                {
                    tools.BeforeBasicNestingForVerification=()=>{reached.Set();if(!release.Wait(TimeSpan.FromSeconds(30)))throw new TimeoutException();};
                    pending=tools.CalculateBasicNestingAsync();PumpUntil(()=>reached.IsSet);
                    studio.Commit("Harici değişiklik",()=>studio.Document.Name="Hesap sırasında değişti");
                    var current=studio.Document.Clone();var currentUndo=session.UndoCount;
                    release.Set();PumpUntil(()=>pending.IsCompleted);pending.GetAwaiter().GetResult();
                    var result=(TextBlock)typeof(TextileToolsWindow).GetField("result",PrivateInstance)!.GetValue(tools)!;
                    Check(result.Text.Contains("uygulanmadı",StringComparison.OrdinalIgnoreCase) && DocumentSession.ContentEquals(current,studio.Document)
                        && session.UndoCount==currentUndo && currentUndo==undo+1,"Changed project prevents a stale basic nesting result from committing",checks);
                }
                finally { release.Set();if(pending is not null)PumpUntil(()=>pending.IsCompleted);tools.Close(); }
            }

            tools=Prepare();studio.SetTool("Çizgi");before=studio.Document.Clone();undo=session.UndoCount;
            using(var reached=new ManualResetEventSlim())using(var release=new ManualResetEventSlim())
            {
                Task? pending=null;
                try
                {
                    tools.BeforeBasicNestingForVerification=()=>{reached.Set();if(!release.Wait(TimeSpan.FromSeconds(30)))throw new TimeoutException();};
                    pending=tools.CalculateBasicNestingAsync();PumpUntil(()=>reached.IsSet);
                    canvas.RestorePendingDrawing(new RecoveryPendingDrawing("Çizgi",[new Point2(1,2)]));
                    release.Set();PumpUntil(()=>pending.IsCompleted);pending.GetAwaiter().GetResult();
                    var result=(TextBlock)typeof(TextileToolsWindow).GetField("result",PrivateInstance)!.GetValue(tools)!;
                    Check(result.Text.Contains("uygulanmadı",StringComparison.OrdinalIgnoreCase) && canvas.PendingDrawingPointCount==1
                        && DocumentSession.ContentEquals(before,studio.Document) && session.UndoCount==undo,
                        "New pending drawing prevents basic nesting from overwriting the workspace",checks);
                }
                finally { release.Set();if(pending is not null)PumpUntil(()=>pending.IsCompleted);canvas.CancelDrawing();tools.Close(); }
            }

            tools=Prepare();before=studio.Document.Clone();undo=session.UndoCount;
            try
            {
                tools.BeforeBasicNestingForVerification=()=>throw new InvalidOperationException("injected basic nesting failure");
                var failed=tools.CalculateBasicNestingAsync();PumpUntil(()=>failed.IsCompleted);failed.GetAwaiter().GetResult();
                var result=(TextBlock)typeof(TextileToolsWindow).GetField("result",PrivateInstance)!.GetValue(tools)!;
                Check(result.Text.Contains("uygulanmadı",StringComparison.OrdinalIgnoreCase) && DocumentSession.ContentEquals(before,studio.Document)
                    && session.UndoCount==undo,"Failed basic nesting preserves source and history",checks);
            }
            finally { tools.Close(); }

            tools=Prepare();studio.Document.Pieces[0].Metadata=new PieceMetadata { Role="Kesim", Quantity=2 };
            var workerStarted=false;var confirmCalls=0;
            try
            {
                tools.BeforeBasicNestingForVerification=()=>workerStarted=true;
                tools.ConfirmBasicNestingForVerification=()=>
                {
                    confirmCalls++;
                    studio.Commit("Onay sırasında değişiklik",()=>studio.Document.Name="Onayda değişti");
                    return true;
                };
                var task=tools.CalculateBasicNestingAsync();PumpUntil(()=>task.IsCompleted);task.GetAwaiter().GetResult();
                var result=(TextBlock)typeof(TextileToolsWindow).GetField("result",PrivateInstance)!.GetValue(tools)!;
                Check(confirmCalls==1 && !workerStarted && !tools.BasicNestingRunningForVerification && studio.Document.Name=="Onayda değişti"
                    && result.Text.Contains("uygulanmadı",StringComparison.OrdinalIgnoreCase),
                    "Basic nesting never starts using a stale scope confirmation",checks);
            }
            finally { tools.Close(); }
        }
        finally { canvas.CancelDrawing();studio.SetTool("Seç");SynchronizationContext.SetSynchronizationContext(old); }
    }
    private static void VerifyQuantityChoiceIdentity(MainWindow studio,string reportPath,List<string> checks)
    {
        Check(ProductionNestingWindow.ValidateFabricInputs("1500,5","2,5","1,25",out var fw,out var pg,out var em) is null && fw==1500.5 && pg==2.5 && em==1.25,"Pastal accepts localized valid fabric dimensions",checks);
        Check(ProductionNestingWindow.ValidateFabricInputs("0","0","0",out _,out _,out _)!.Contains("Kumaş eni") && ProductionNestingWindow.ValidateFabricInputs("100","-1","0",out _,out _,out _)!.Contains("Parça boşluğu") && ProductionNestingWindow.ValidateFabricInputs("100","0","50",out _,out _,out _)!.Contains("kenar payı"),"Pastal explains invalid width, gap and unusable edge margin",checks);
        var a=new PatternPiece { Name="Aynı\u202E",Points=[new(0,0),new(10,0),new(0,20)],Metadata=new PieceMetadata { Role="Kesim",Quantity=2,Material="Pamuk" } };
        var b=new PatternPiece { Name="Aynı\u202E",Points=[new(0,0),new(30,0),new(0,40)],Metadata=new PieceMetadata { Role="Kesim",Quantity=2,Material="Pamuk" } };
        studio.LoadDocument(new ProjectDocument { Pieces=[a,b] });studio.Select(a.Id);
        var window=new ProductionNestingWindow(studio,studio);
        Render(window,Path.ChangeExtension(reportPath,".quantity-choice-identity.png"),680,520);
        var items=(System.Collections.IList)typeof(ProductionNestingWindow).GetField("choiceItems",PrivateInstance)!.GetValue(window)!;
        var labelProperty=items[0]!.GetType().GetProperty("Label")!;
        var first=(string)labelProperty.GetValue(items[0])!;var second=(string)labelProperty.GetValue(items[1])!;
        Check(first.Contains(a.Id.ToString("D")) && second.Contains(b.Id.ToString("D")) && first.Contains("10 × 20 mm")
            && second.Contains("30 × 40 mm") && first.Contains("\\u202E") && !first.Contains('\u202E'),
            "Quantity selection distinguishes duplicate names by ID and dimensions and escapes direction controls",checks);
        var list=(ListBox)typeof(ProductionNestingWindow).GetField("choices",PrivateInstance)!.GetValue(window)!;
        Check(list.ActualWidth>0 && list.ActualHeight>0 && ScrollViewer.GetHorizontalScrollBarVisibility(list)==ScrollBarVisibility.Disabled,
            "Quantity selection remains available in the minimum window without horizontal scrolling",checks);
        var safeName=(string)typeof(ProductionNestingWindow).GetMethod("SafeChoiceText",PrivateInstance|BindingFlags.Static)!
            .Invoke(null,["Ön"+char.ConvertFromUtf32(0xE0001)+"😀",int.MaxValue])!;
        Check(safeName=="Ön\\uDB40\\uDC01😀","Quantity names escape astral format controls and preserve emoji",checks);
        var firstContainer=(DependencyObject)list.ItemContainerGenerator.ContainerFromIndex(0)!;
        var firstCheckbox=Descendants<CheckBox>(firstContainer).Single();
        Check(AutomationProperties.GetHelpText(firstCheckbox).Contains("Aynı\\u202E") && firstCheckbox.ToolTip?.ToString()?.Contains("Aynı\\u202E")==true,
            "Quantity selection exposes the complete safe name through help and tooltip",checks);
        items[1]!.GetType().GetProperty("IsSelected")!.SetValue(items[1],true);
        var old=SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(studio.Dispatcher));
        try
        {
            var task=(Task)typeof(ProductionNestingWindow).GetMethod("Calculate",PrivateInstance)!.Invoke(window,null)!;
            PumpUntil(()=>task.IsCompleted);task.GetAwaiter().GetResult();
            var highlight=(ComboBox)typeof(ProductionNestingWindow).GetField("highlight",PrivateInstance)!.GetValue(window)!;
            var labels=highlight.Items.Cast<object>().Select(x=>x.ToString()!).ToArray();
            Check(labels.Length==4 && labels.Count(s=>s.Contains(a.Id.ToString("D")) && s.Contains("10 × 20 mm"))==2
                && labels.Count(s=>s.Contains(b.Id.ToString("D")) && s.Contains("30 × 40 mm"))==2
                && labels.All(s=>s.Contains("\\u202E") && !s.Contains('\u202E')),
                "Placement selector distinguishes both copies of duplicate source names",checks);
            highlight.SelectedIndex=2;
            var preview=typeof(ProductionNestingWindow).GetField("preview",PrivateInstance)!.GetValue(window)!;
            var highlighted=(Guid?)preview.GetType().GetProperty("HighlightId")!.GetValue(preview);
            var selectedId=(Guid)highlight.SelectedItem.GetType().GetProperty("Id")!.GetValue(highlight.SelectedItem)!;
            Check(highlighted==selectedId,"Placement selection still highlights the placed piece",checks);
            var query=(TextBox)typeof(ProductionNestingWindow).GetField("choiceSearch",PrivateInstance)!.GetValue(window)!;
            var searchStatus=(TextBlock)typeof(ProductionNestingWindow).GetField("choiceSearchStatus",PrivateInstance)!.GetValue(window)!;
            var versionField=typeof(ProductionNestingWindow).GetField("inputsVersion",PrivateInstance)!;
            var beforeFilter=(int)versionField.GetValue(window)!;
            var drawingBefore=preview.GetType().GetProperty("Drawing")!.GetValue(preview);
            query.Text=b.Id.ToString("D");
            Check(list.Items.Count==1 && (int)versionField.GetValue(window)! == beforeFilter
                && ReferenceEquals(drawingBefore,preview.GetType().GetProperty("Drawing")!.GetValue(preview))
                && searchStatus.Text.Contains("Arama dışında seçili: 1") && searchStatus.Text.Contains("hesaplamaya dahildir"),
                "Quantity filter retains hidden selections and the calculated preview",checks);
            query.Text="eşleşmeyen-örnek";
            Check(list.Items.Count==0 && searchStatus.Text.Contains("Eşleşen parça yok") && (int)versionField.GetValue(window)! == beforeFilter,
                "Quantity filter reports no matches without invalidating preview",checks);
            query.Clear();
            Check(list.Items.Count==2 && (bool)items[0]!.GetType().GetProperty("IsSelected")!.GetValue(items[0])!
                && (bool)items[1]!.GetType().GetProperty("IsSelected")!.GetValue(items[1])!,"Clearing quantity search restores both selected sources",checks);
            Render(window,Path.ChangeExtension(reportPath,".quantity-choice-selected.png"),680,520);
            Check(FullyVisible(highlight,(FrameworkElement)window.Content),"Placement selector remains visible in the minimum window",checks);
            query.Text=b.Id.ToString("D");
            var selectShown=(Button)typeof(ProductionNestingWindow).GetField("selectShownChoices",PrivateInstance)!.GetValue(window)!;
            var clearShown=(Button)typeof(ProductionNestingWindow).GetField("clearShownChoices",PrivateInstance)!.GetValue(window)!;
            selectShown.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check((int)versionField.GetValue(window)! == beforeFilter && ReferenceEquals(drawingBefore,preview.GetType().GetProperty("Drawing")!.GetValue(preview)),"Bulk selection with no changes preserves preview",checks);
            clearShown.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check((bool)items[0]!.GetType().GetProperty("IsSelected")!.GetValue(items[0])! && !(bool)items[1]!.GetType().GetProperty("IsSelected")!.GetValue(items[1])! && (int)versionField.GetValue(window)! == beforeFilter+1 && preview.GetType().GetProperty("Drawing")!.GetValue(preview)==null,"Bulk clear preserves hidden selection and invalidates preview once",checks);
            selectShown.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check((bool)items[0]!.GetType().GetProperty("IsSelected")!.GetValue(items[0])! && (bool)items[1]!.GetType().GetProperty("IsSelected")!.GetValue(items[1])! && (int)versionField.GetValue(window)! == beforeFilter+2,"Bulk select restores visible selection without touching hidden selection",checks);
            var length=(TextBox)typeof(ProductionNestingWindow).GetField("fabricLength",PrivateInstance)!.GetValue(window)!;
            var policy=(ComboBox)typeof(ProductionNestingWindow).GetField("grainPolicy",PrivateInstance)!.GetValue(window)!;
            var status=(TextBlock)typeof(ProductionNestingWindow).GetField("status",PrivateInstance)!.GetValue(window)!;
            var save=(Button)typeof(ProductionNestingWindow).GetField("saveLayout",PrivateInstance)!.GetValue(window)!;
            length.Text="0";
            var invalidLength=(Task)typeof(ProductionNestingWindow).GetMethod("Calculate",PrivateInstance)!.Invoke(window,null)!;
            PumpUntil(()=>invalidLength.IsCompleted);invalidLength.GetAwaiter().GetResult();
            Check(status.Text.Contains("Kumaş boyu") && !save.IsEnabled,"Quantity window rejects invalid fabric length",checks);
            length.Text="10";
            var tooShort=(Task)typeof(ProductionNestingWindow).GetMethod("Calculate",PrivateInstance)!.Invoke(window,null)!;
            PumpUntil(()=>tooShort.IsCompleted);tooShort.GetAwaiter().GetResult();
            Check(preview.GetType().GetProperty("Drawing")!.GetValue(preview)==null && !save.IsEnabled,"Quantity window rejects a layout exceeding fabric length",checks);
            length.Clear();policy.SelectedIndex=2;
            var missingGrain=(Task)typeof(ProductionNestingWindow).GetMethod("Calculate",PrivateInstance)!.Invoke(window,null)!;
            PumpUntil(()=>missingGrain.IsCompleted);missingGrain.GetAwaiter().GetResult();
            Check(status.Text.Contains("İplik yönü",StringComparison.OrdinalIgnoreCase) && !save.IsEnabled,"Quantity window rejects missing grain when grain validation is selected",checks);
            policy.SelectedIndex=0;
            var uncheckedGrain=(Task)typeof(ProductionNestingWindow).GetMethod("Calculate",PrivateInstance)!.Invoke(window,null)!;
            PumpUntil(()=>uncheckedGrain.IsCompleted);uncheckedGrain.GetAwaiter().GetResult();
            Check(save.IsEnabled && status.Text.Contains("İplik yönü denetlenmedi"),"Quantity window explicitly labels unchecked grain",checks);
        }
        finally { SynchronizationContext.SetSynchronizationContext(old); }
    }
    private static void VerifyDraftErrorIdentity(MainWindow studio,List<string> checks)
    {
        var unusual="Ön\u202E"+char.ConvertFromUtf32(0xE0001)+"😀";
        var first=new PatternPiece { Name=unusual,Points=[new(0,0),new(10,0),new(0,10)] };var second=first.Clone();second.Id=Guid.NewGuid();
        studio.LoadDocument(new ProjectDocument { Pieces=[first,second] });studio.Select(second.Id);var before=studio.Document.Clone();
        var x=(TextBox)typeof(MainWindow).GetField("x",PrivateInstance)!.GetValue(studio)!;x.Text="geçersiz";
        Exception? error=null;try { Invoke(studio,"ApplyProperties"); } catch(Exception ex){error=ex;}
        var drafts=(System.Collections.IDictionary)typeof(MainWindow).GetField("propertyDrafts",PrivateInstance)!.GetValue(studio)!;
        Check(error is InvalidDataException && error.Message.Contains(second.Id.ToString("D")) && !error.Message.Contains(first.Id.ToString("D")) && error.Message.Contains("\\u202E\\uDB40\\uDC01😀") && !error.Message.Contains('\u202E') && drafts.Contains(second.Id) && DocumentSession.ContentEquals(before,studio.Document),"Invalid draft identifies the correct safely escaped source and preserves the project",checks);
    }
    private static void VerifyOpenAndDraftPostCommit(MainWindow studio,string work,List<string> checks)
    {
        var source=new PatternPiece { Name="Dosyadaki parça",Points=[new(0,0),new(30,0),new(0,30)],Metadata=new PieceMetadata { Role="Kesim",Material="Pamuk",Quantity=1 } };
        var stored=new ProjectDocument { Name="Açılan proje",Pieces=[source] };var path=Path.Combine(work,"open-post-load-"+Guid.NewGuid().ToString("N")+".tcad");
        ProjectStorage.Save(stored,path);var originalBytes=File.ReadAllBytes(path);
        studio.LoadDocument(new ProjectDocument { Name="Önceki" });studio.Commit("Eski değişiklik",()=>studio.Document.Name="Önceki kirli");
        studio.AfterProjectLoadBeforeRefreshForVerification=()=>throw new InvalidOperationException("injected refresh failure");
        Exception? error=null;try { studio.OpenProjectFile(path); } catch(Exception ex){error=ex;} finally { studio.AfterProjectLoadBeforeRefreshForVerification=null; }
        var session=(DocumentSession)typeof(MainWindow).GetField("session",PrivateInstance)!.GetValue(studio)!;
        var filePath=(string?)typeof(MainWindow).GetField("filePath",PrivateInstance)!.GetValue(studio);
        var version=(ProjectFileVersion?)typeof(MainWindow).GetField("fileVersion",PrivateInstance)!.GetValue(studio);
        Check(error is InvalidOperationException && error.Message.Contains("Proje açıldı") && DocumentSession.ContentEquals(stored,studio.Document) && !session.IsDirty && session.UndoCount==0 && filePath==Path.GetFullPath(path) && version==ProjectStorage.CaptureVersion(path) && File.ReadAllBytes(path).SequenceEqual(originalBytes),"Open refresh failure keeps loaded clean project, file version and original bytes",checks);
        studio.Commit("Düzenle",()=>studio.Document.Name="Açıldıktan sonra düzenleme");Invoke(studio,"SaveTo",path);
        Check(ProjectStorage.Load(path).Name=="Açıldıktan sonra düzenleme","After failed open refresh the next save targets the opened file",checks);
        var beforeInvalid=studio.Document.Clone();var badPath=Path.Combine(work,"invalid-open-"+Guid.NewGuid().ToString("N")+".tcad");File.WriteAllText(badPath,"invalid");
        try { studio.OpenProjectFile(badPath);throw new InvalidOperationException("Expected invalid project failure"); } catch(InvalidDataException) { }
        Check(DocumentSession.ContentEquals(beforeInvalid,studio.Document) && (string?)typeof(MainWindow).GetField("filePath",PrivateInstance)!.GetValue(studio)==path,"Invalid project fails before replacing the current workspace",checks);
        studio.LoadDocument(stored);studio.Select(source.Id);var beforeDraft=studio.Document.Clone();var undo=session.UndoCount;
        var nameField=(TextBox)typeof(MainWindow).GetField("name",PrivateInstance)!.GetValue(studio)!;nameField.Text="Uygulanan taslak";
        studio.DuringRefreshForVerification=()=>throw new InvalidOperationException("injected refresh failure");error=null;
        try { Invoke(studio,"ApplyPropertyDrafts",new Guid[]{source.Id}); } catch(Exception ex){error=ex;} finally { studio.DuringRefreshForVerification=null; }
        var drafts=(System.Collections.IDictionary)typeof(MainWindow).GetField("propertyDrafts",PrivateInstance)!.GetValue(studio)!;
        Check(error is InvalidOperationException && error.Message.Contains("projeye uygulandı") && studio.Document.Pieces[0].Name=="Uygulanan taslak" && !drafts.Contains(source.Id) && session.IsDirty && session.UndoCount==undo+1 && !(bool)typeof(MainWindow).GetField("syncing",PrivateInstance)!.GetValue(studio)!,"Applied property draft reports committed state if refresh fails",checks);
        Check(session.Undo() && DocumentSession.ContentEquals(beforeDraft,studio.Document),"Applied property draft remains undoable after refresh failure",checks);
    }
    private static void VerifySaveAndCanvasPostCommit(MainWindow studio,string work,List<string> checks)
    {
        var canvas=(CadCanvas)typeof(MainWindow).GetField("canvas",PrivateInstance)!.GetValue(studio)!;
        var session=(DocumentSession)typeof(MainWindow).GetField("session",PrivateInstance)!.GetValue(studio)!;
        var error=(TextBlock)typeof(MainWindow).GetField("canvasError",PrivateInstance)!.GetValue(studio)!;
        canvas.CancelDrawing();studio.LoadDocument(new ProjectDocument { Name="Kayıt sonrası" });
        studio.Commit("Değiştir",()=>studio.Document.Name="Kaydedilen proje");
        var path=Path.Combine(work,"post-save-"+Guid.NewGuid().ToString("N")+".tcad");
        Exception? failure=null;
        studio.DuringRefreshForVerification=()=>throw new InvalidOperationException("injected refresh failure");
        try { Invoke(studio,"SaveTo",path); } catch(Exception ex) { failure=ex; }
        finally { studio.DuringRefreshForVerification=null; }
        var version=(ProjectFileVersion?)typeof(MainWindow).GetField("fileVersion",PrivateInstance)!.GetValue(studio);
        Check(failure is InvalidOperationException && failure.Message.Contains("Dosya kaydedildi")
            && ProjectStorage.Load(path).Name=="Kaydedilen proje" && !session.IsDirty
            && version==ProjectStorage.CaptureVersion(path),
            "Saved file and clean session survive a post-save refresh failure",checks);
        failure=null;studio.DuringRefreshForVerification=()=>throw new InvalidOperationException("injected refresh failure");
        try { Invoke(studio,"NewDocument"); } catch(Exception ex) { failure=ex; }
        finally { studio.DuringRefreshForVerification=null; }
        Check(failure is InvalidOperationException && failure.Message.Contains("Yeni proje açıldı")
            && studio.Document.Pieces.Count==0 && !session.IsDirty,
            "New document reports its committed state if refresh fails",checks);

        studio.LoadDocument(new ProjectDocument { Name="Çizim sonrası" });studio.SetTool("Çoklu çizgi");
        var pending=new RecoveryPendingDrawing("Çoklu çizgi",[new(0,0),new(20,0),new(0,20)]);
        canvas.RestorePendingDrawing(pending);
        var before=studio.Document.Clone();int undo=session.UndoCount;
        studio.AfterCommitBeforeRefreshForVerification=()=>throw new InvalidOperationException("injected refresh failure");
        try { canvas.FinishPolyline(); } finally { studio.AfterCommitBeforeRefreshForVerification=null; }
        Check(studio.Document.Pieces.Count==1 && session.UndoCount==undo+1 && !canvas.HasPendingDrawing
            && error.Text.Contains("projeye uygulandı"),
            "Completed drawing is not restored or duplicated after a refresh failure",checks);
        canvas.FinishPolyline();
        Check(studio.Document.Pieces.Count==1 && session.UndoCount==undo+1
            && session.Undo() && DocumentSession.ContentEquals(before,studio.Document),
            "Completed drawing remains a single undoable operation after refresh failure",checks);
        canvas.CancelDrawing();studio.SetTool("Seç");studio.ClearCanvasError();

        studio.LoadDocument(new ProjectDocument { Name="IO hatası sonrası çizim" });studio.SetTool("Çoklu çizgi");
        canvas.RestorePendingDrawing(pending);before=studio.Document.Clone();undo=session.UndoCount;
        studio.AfterCommitBeforeRefreshForVerification=()=>throw new IOException("injected post-commit I/O failure");
        try { canvas.FinishPolyline(); } finally { studio.AfterCommitBeforeRefreshForVerification=null; }
        Check(studio.Document.Pieces.Count==1 && session.UndoCount==undo+1 && !canvas.HasPendingDrawing
            && error.Text.Contains("projeye uygulandı") && session.Undo() && DocumentSession.ContentEquals(before,studio.Document),
            "Post-commit I/O failure reports the drawing as applied, clears preview and keeps one undo",checks);
        canvas.CancelDrawing();studio.SetTool("Seç");studio.ClearCanvasError();

        var piece=new PatternPiece { Name="Klavye",Points=[new(0,0),new(10,0),new(0,10)] };
        studio.LoadDocument(new ProjectDocument { Pieces=[piece] });studio.Select(piece.Id);
        before=studio.Document.Clone();undo=session.UndoCount;
        var move=typeof(CadCanvas).GetMethod("MoveSelectionByKeyboard",PrivateInstance)!
            .CreateDelegate<Action<Key,ModifierKeys>>(canvas);
        var safe=typeof(CadCanvas).GetMethod("Safe",PrivateInstance)!;
        var thinMove=new PatternPiece { Name="İnce klavye",Points=[new(0,0),new(0.000000001,0),new(0,1)] };
        var normalMove=new PatternPiece { Name="Normal klavye",Points=[new(0,0),new(10,0),new(0,10)] };
        studio.LoadDocument(new ProjectDocument { Pieces=[thinMove,normalMove] });studio.Select(thinMove.Id);
        var thinBefore=studio.Document.Clone();var thinUndo=session.UndoCount;
        safe.Invoke(canvas,[new Action(()=>move(Key.Right,ModifierKeys.None)),false,null]);
        Check(error.Text.Contains("İşlem uygulanmadı") && DocumentSession.ContentEquals(thinBefore,studio.Document)
            && session.UndoCount==thinUndo,
            "Unsafe keyboard translation reports failure and preserves document and history",checks);
        studio.Select(normalMove.Id);
        safe.Invoke(canvas,[new Action(()=>move(Key.Right,ModifierKeys.None)),false,null]);
        Check(string.IsNullOrEmpty(error.Text) && studio.Selected!.Points[0]==new Point2(1,0)
            && session.UndoCount==thinUndo+1,
            "Successful later keyboard translation clears the previous failure and records one edit",checks);
        studio.LoadDocument(new ProjectDocument { Pieces=[piece] });studio.Select(piece.Id);
        before=studio.Document.Clone();undo=session.UndoCount;studio.ClearCanvasError();
        studio.AfterCommitBeforeRefreshForVerification=()=>throw new InvalidOperationException("injected refresh failure");
        try { safe.Invoke(canvas,[new Action(()=>move(Key.Right,ModifierKeys.None)),false,null]); }
        finally { studio.AfterCommitBeforeRefreshForVerification=null; }
        Check(studio.Document.Pieces[0].Points[0]==new Point2(1,0) && session.UndoCount==undo+1
            && error.Text.Contains("projeye uygulandı") && session.Undo() && DocumentSession.ContentEquals(before,studio.Document),
            "Keyboard move reports committed geometry and remains undoable after refresh failure",checks);
        studio.ClearCanvasError();

        studio.AfterCommitBeforeRefreshForVerification=()=>throw new IOException("injected post-commit I/O failure");
        try { safe.Invoke(canvas,[new Action(()=>move(Key.Right,ModifierKeys.None)),false,null]); }
        finally { studio.AfterCommitBeforeRefreshForVerification=null; }
        Check(studio.Document.Pieces[0].Points[0]==new Point2(1,0) && session.UndoCount==undo+1
            && error.Text.Contains("projeye uygulandı") && session.Undo() && DocumentSession.ContentEquals(before,studio.Document),
            "Keyboard move reports post-commit I/O failure without a duplicate history entry",checks);
        studio.ClearCanvasError();

        studio.LoadDocument(new ProjectDocument { Name="İptal sonrası çizim" });studio.SetTool("Çoklu çizgi");
        canvas.RestorePendingDrawing(pending);before=studio.Document.Clone();undo=session.UndoCount;
        safe.Invoke(canvas,[new Action(()=>throw new OperationCanceledException("injected pre-commit cancellation")),true,null]);
        Check(canvas.HasPendingDrawing && session.UndoCount==undo && DocumentSession.ContentEquals(before,studio.Document)
            && error.Text.Contains("İşlem uygulanmadı"),
            "Pre-commit cancellation retains the pending drawing and document",checks);
        studio.AfterCommitBeforeRefreshForVerification=()=>throw new OperationCanceledException("injected post-commit cancellation");
        try { canvas.FinishPolyline(); } finally { studio.AfterCommitBeforeRefreshForVerification=null; }
        Check(studio.Document.Pieces.Count==1 && session.UndoCount==undo+1 && !canvas.HasPendingDrawing
            && error.Text.Contains("projeye uygulandı"),
            "Post-commit cancellation reports the applied drawing and clears its preview",checks);
        canvas.FinishPolyline();
        Check(studio.Document.Pieces.Count==1 && session.UndoCount==undo+1
            && session.Undo() && DocumentSession.ContentEquals(before,studio.Document),
            "Drawing remains a single undoable operation after post-commit cancellation",checks);
        canvas.CancelDrawing();studio.SetTool("Seç");studio.ClearCanvasError();

        piece=new PatternPiece { Name="İptal sonrası klavye",Points=[new(0,0),new(10,0),new(0,10)] };
        studio.LoadDocument(new ProjectDocument { Pieces=[piece] });studio.Select(piece.Id);
        before=studio.Document.Clone();undo=session.UndoCount;
        studio.AfterCommitBeforeRefreshForVerification=()=>throw new OperationCanceledException("injected post-commit cancellation");
        try { safe.Invoke(canvas,[new Action(()=>move(Key.Right,ModifierKeys.None)),false,null]); }
        finally { studio.AfterCommitBeforeRefreshForVerification=null; }
        Check(studio.Document.Pieces[0].Points[0]==new Point2(1,0) && session.UndoCount==undo+1
            && error.Text.Contains("projeye uygulandı") && session.Undo() && DocumentSession.ContentEquals(before,studio.Document),
            "Keyboard move remains a single undoable operation after post-commit cancellation",checks);
        studio.ClearCanvasError();

        studio.LoadDocument(new ProjectDocument { Name="Büyük çizim sonrası" });studio.SetTool("Çoklu çizgi");
        var large=Enumerable.Range(0,4097).Select(i=> { var angle=Math.Tau*i/4097;return new Point2(1000*Math.Cos(angle),1000*Math.Sin(angle)); }).ToArray();
        canvas.RestorePendingDrawing(new RecoveryPendingDrawing("Çoklu çizgi",large));
        before=studio.Document.Clone();undo=session.UndoCount;
        var old=SynchronizationContext.Current;SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(studio.Dispatcher));
        try
        {
            studio.AfterCommitBeforeRefreshForVerification=()=>throw new InvalidOperationException("injected refresh failure");
            canvas.FinishPolyline();var task=canvas.LargeCompletionTask ?? throw new InvalidOperationException("Large drawing task missing.");
            PumpUntil(()=>task.IsCompleted);task.GetAwaiter().GetResult();
            studio.AfterCommitBeforeRefreshForVerification=null;
            Check(studio.Document.Pieces.Count==1 && session.UndoCount==undo+1 && !canvas.HasPendingDrawing
                && error.Text.Contains("projeye eklendi") && session.Undo() && DocumentSession.ContentEquals(before,studio.Document),
                "Large drawing reports its committed result and remains undoable after refresh failure",checks);
            canvas.RestorePendingDrawing(new RecoveryPendingDrawing("Çoklu çizgi",large));
            studio.AfterCommitBeforeRefreshForVerification=()=>throw new IOException("injected post-commit I/O failure");
            canvas.FinishPolyline();task=canvas.LargeCompletionTask ?? throw new InvalidOperationException("Large drawing task missing.");
            PumpUntil(()=>task.IsCompleted);task.GetAwaiter().GetResult();
            studio.AfterCommitBeforeRefreshForVerification=null;
            Check(studio.Document.Pieces.Count==1 && session.UndoCount==undo+1 && !canvas.HasPendingDrawing
                && error.Text.Contains("projeye eklendi") && session.Undo() && DocumentSession.ContentEquals(before,studio.Document),
                "Large drawing reports post-commit I/O failure without restoring its preview",checks);
        }
        finally { studio.AfterCommitBeforeRefreshForVerification=null;SynchronizationContext.SetSynchronizationContext(old);canvas.CancelDrawing();studio.SetTool("Seç");studio.ClearCanvasError(); }
    }

    private static void VerifyHistoryAndRecoveryPostCommit(MainWindow studio,string work,List<string> checks)
    {
        var canvas=(CadCanvas)typeof(MainWindow).GetField("canvas",PrivateInstance)!.GetValue(studio)!;
        var session=(DocumentSession)typeof(MainWindow).GetField("session",PrivateInstance)!.GetValue(studio)!;
        canvas.CancelDrawing();studio.LoadDocument(new ProjectDocument { Name="Önce" });
        studio.Commit("Bir",()=>studio.Document.Name="Bir");studio.Commit("İki",()=>studio.Document.Name="İki");
        Exception? error=null;studio.DuringRefreshForVerification=()=>throw new IOException("injected history refresh failure");
        try { Invoke(studio,"Undo"); } catch(Exception ex) { error=ex; }
        finally { studio.DuringRefreshForVerification=null; }
        Check(error is InvalidOperationException && error.Message.Contains("Geri alma uygulandı")
            && studio.Document.Name=="Bir" && session.UndoCount==1 && session.RedoCount==1,
            "Undo reports its single applied history change when refresh throws",checks);
        error=null;studio.DuringRefreshForVerification=()=>throw new InvalidOperationException("injected history refresh failure");
        try { Invoke(studio,"Redo"); } catch(Exception ex) { error=ex; }
        finally { studio.DuringRefreshForVerification=null; }
        Check(error is InvalidOperationException && error.Message.Contains("Yineleme uygulandı")
            && studio.Document.Name=="İki" && session.UndoCount==2 && session.RedoCount==0,
            "Redo reports its single applied history change when refresh throws",checks);

        var storeField=typeof(MainWindow).GetField("recoveryStore",PrivateInstance)!;
        var previous=(RecoveryStore)storeField.GetValue(studio)!;
        var previousDirectory=Path.GetDirectoryName(previous.RecoveryPath)!;
        var directory=Path.Combine(work,"recovery-postcommit-"+Guid.NewGuid().ToString("N"));
        var piece=new PatternPiece { Name="Kurtarılan",Points=[new(0,0),new(10,0),new(0,10)] };
        var document=new ProjectDocument { Name="Kurtarma sonrası",Pieces=[piece] };
        var draft=new RecoveryPropertyDraft(piece.Id,"Taslak ad","hatalı x","2",piece.Name,0,0);
        var pending=new RecoveryPendingDrawing("Çoklu çizgi",[new(2,2),new(8,2),new(2,8)]);
        string path;
        using(var writer=new RecoveryStore(directory))
        { writer.Save(document,[draft],pending);path=writer.DraftRecoveryPath; }
        var originalBytes=File.ReadAllBytes(path);
        using var reader=new RecoveryStore(directory);
        try
        {
            SetRecovery(studio,reader);
            var recovered=reader.LoadCandidate(path);
            canvas.CancelDrawing();studio.LoadDocument(new ProjectDocument { Name="Eski çalışma" });
            error=null;studio.DuringRefreshForVerification=()=>throw new IOException("injected recovery refresh failure");
            try { Invoke(studio,"RestoreRecovery",recovered); } catch(Exception ex) { error=ex; }
            finally { studio.DuringRefreshForVerification=null; }
            var drawing=canvas.CapturePendingDrawing();
            var drafts=typeof(MainWindow).GetField("propertyDrafts",PrivateInstance)!.GetValue(studio)!;
            var count=(int)drafts.GetType().GetProperty("Count")!.GetValue(drafts)!;
            Check(error is InvalidOperationException && error.Message.Contains("Kurtarma projesi açıldı")
                && DocumentSession.ContentEquals(document,studio.Document) && session.IsDirty && count==1
                && drawing is not null && drawing.Tool==pending.Tool && drawing.Points.SequenceEqual(pending.Points)
                && File.ReadAllBytes(path).SequenceEqual(originalBytes),
                "Recovery refresh failure preserves recovered document, dirty state, draft, drawing and source",checks);

            canvas.CancelDrawing();studio.LoadDocument(new ProjectDocument { Name="Önceki belge" });
            var before=studio.Document.Clone();
            var invalid=recovered with { Drafts=[draft,draft] };
            try { Invoke(studio,"RestoreRecovery",invalid);throw new InvalidOperationException("Expected duplicate draft rejection."); }
            catch(ArgumentException) { }
            Check(DocumentSession.ContentEquals(before,studio.Document) && File.ReadAllBytes(path).SequenceEqual(originalBytes),
                "Recovery validation failure leaves the original document and source unchanged",checks);

            studio.BeforeRecoveredDrawingRestoreForVerification=()=>throw new IOException("injected pending drawing restore failure");
            error=null;
            try { Invoke(studio,"RestoreRecovery",recovered); } catch(Exception ex) { error=ex; }
            finally { studio.BeforeRecoveredDrawingRestoreForVerification=null; }
            Check(error is InvalidOperationException && error.Message.Contains("Kurtarma projesi açıldı")
                && session.IsDirty && DocumentSession.ContentEquals(document,studio.Document) && File.Exists(path),
                "Incomplete recovery reports its applied unsaved document and preserves the source",checks);
            var incompleteDrawing=canvas.CapturePendingDrawing();
            var incompleteTool=studio.ActiveTool;
            var incompleteUndo=session.UndoCount;var incompleteRedo=session.RedoCount;
            foreach(var key in new[] { Key.Enter,Key.Escape,Key.V,Key.L,Key.P,Key.Delete })
            {
                studio.HandleCanvasShortcut(key,true);
                var currentDrawing=canvas.CapturePendingDrawing();
                Check(!canvas.IsEnabled && currentDrawing is not null && incompleteDrawing is not null
                    && currentDrawing.Tool==incompleteDrawing.Tool && currentDrawing.Points.SequenceEqual(incompleteDrawing.Points)
                    && studio.ActiveTool==incompleteTool && DocumentSession.ContentEquals(document,studio.Document)
                    && session.UndoCount==incompleteUndo && session.RedoCount==incompleteRedo
                    && File.ReadAllBytes(path).SequenceEqual(originalBytes),
                    $"Incomplete recovery blocks {key} without changing the drawing, document, history or source",checks);
            }
            studio.SetTool("Çizgi");
            var afterTool=canvas.CapturePendingDrawing();
            Check(!canvas.IsEnabled && studio.ActiveTool==incompleteTool && afterTool is not null
                && incompleteDrawing is not null && afterTool.Tool==incompleteDrawing.Tool
                && afterTool.Points.SequenceEqual(incompleteDrawing.Points)
                && DocumentSession.ContentEquals(document,studio.Document) && File.ReadAllBytes(path).SequenceEqual(originalBytes),
                "Incomplete recovery blocks direct toolbar tool changes without losing its drawing",checks);
            // Drop the restored property draft so saving can proceed without UI interaction.
            ((System.Collections.IDictionary)drafts).Clear();
            var saved=Path.Combine(work,"partial-recovery-"+Guid.NewGuid().ToString("N")+".tcad");
            Invoke(studio,"SaveTo",saved);
            Invoke(studio,"ArchiveRecoveredSource");
            WaitRecovery(studio.ClearRecoveryForVerificationAsync());
            Check(File.Exists(path) && File.ReadAllBytes(path).SequenceEqual(originalBytes),
                "Saving an incompletely restored recovery does not archive its source",checks);
        }
        finally
        {
            studio.DuringRefreshForVerification=null;studio.BeforeRecoveredDrawingRestoreForVerification=null;
            RestoreRecoveryDirectory(studio,previousDirectory);
        }
    }

    private static void VerifyMainMutationPostCommit(MainWindow studio,List<string> checks)
    {
        var piece=new PatternPiece { Name="Dönüşüm",Points=[new(0,0),new(18,0),new(3,10)] };
        studio.LoadDocument(new ProjectDocument { Pieces=[piece] });studio.Select(piece.Id);
        var session=(DocumentSession)typeof(MainWindow).GetField("session",PrivateInstance)!.GetValue(studio)!;
        var original=studio.Document.Clone();var count=session.UndoCount;
        Func<IReadOnlyList<Point2>,Point2,List<Point2>> rotate=(points,center)=>TekstilCAD.Core.Geometry.Rotate(points,90,center);
        Exception? error=null;studio.AfterCommitBeforeRefreshForVerification=()=>throw new IOException("injected transform refresh failure");
        try { Invoke(studio,"Transform",rotate); } catch(Exception ex) { error=ex; }
        finally { studio.AfterCommitBeforeRefreshForVerification=null; }
        Check(error is InvalidOperationException && error.Message.Contains("uygulandı",StringComparison.OrdinalIgnoreCase)
            && !DocumentSession.ContentEquals(original,studio.Document) && session.UndoCount==count+1,
            "Transform reports a committed change when refresh fails",checks);
        var transformed=studio.Document.Clone();
        Invoke(studio,"Transform",rotate);
        Check(DocumentSession.ContentEquals(transformed,studio.Document) && session.UndoCount==count+1,
            "Transform retry first refreshes the committed change instead of applying it twice",checks);
        Check(session.Undo() && DocumentSession.ContentEquals(original,studio.Document),
            "Committed transform remains a single undo step after refresh failure",checks);
        studio.LoadDocument(original);studio.Select(piece.Id);count=session.UndoCount;error=null;
        studio.AfterCommitBeforeRefreshForVerification=()=>throw new IOException("injected delete refresh failure");
        try { Invoke(studio,"DeleteSelection"); } catch(Exception ex) { error=ex; }
        finally { studio.AfterCommitBeforeRefreshForVerification=null; }
        Check(error is InvalidOperationException && error.Message.Contains("silindi",StringComparison.OrdinalIgnoreCase)
            && studio.Document.Pieces.Count==0 && session.UndoCount==count+1,
            "Delete reports its committed result when refresh fails",checks);
        Check(session.Undo() && DocumentSession.ContentEquals(original,studio.Document),
            "Committed delete remains a single undo step after refresh failure",checks);

        studio.LoadDocument(original);studio.Select(piece.Id);
        var dialog=studio.BuildPieceMetadataDialog()!;var root=(FrameworkElement)dialog.Content;
        root.Measure(new Size(450,700));root.Arrange(new Rect(0,0,450,700));root.UpdateLayout();
        Descendants<TextBox>(root).Single(t=>AutomationProperties.GetName(t)=="Beden").Text="M";
        var apply=Descendants<Button>(root).Single(b=>Equals(b.Content,"Bilgileri uygula"));
        count=session.UndoCount;studio.AfterCommitBeforeRefreshForVerification=()=>throw new IOException("injected metadata refresh failure");
        try { apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
        finally { studio.AfterCommitBeforeRefreshForVerification=null; }
        var notice=Descendants<TextBlock>(root).Single(t=>AutomationProperties.GetLiveSetting(t)==AutomationLiveSetting.Assertive);
        Check(notice.Text.Contains("uygulandı",StringComparison.OrdinalIgnoreCase)
            && studio.Document.Pieces.Single().Metadata?.Size=="M" && session.UndoCount==count+1 && !apply.IsEnabled,
            "Metadata form reports the applied change and prevents duplicate submission after refresh failure",checks);
        Check(session.Undo() && DocumentSession.ContentEquals(original,studio.Document),
            "Committed metadata remains a single undo step after refresh failure",checks);
        dialog.Close();
    }

    private static void VerifyRecoveryRestoreDuringWrite(MainWindow studio,string work,List<string> checks)
    {
        var original=(RecoveryStore)typeof(MainWindow).GetField("recoveryStore",PrivateInstance)!.GetValue(studio)!;
        var originalDirectory=Path.GetDirectoryName(original.RecoveryPath)!;
        var directory=Path.Combine(work,"recovery-restore-race-"+Guid.NewGuid().ToString("N"));
        var recoveredDocument=new ProjectDocument { Name="Kurtarılan güncel belge" };
        string chosenPath;
        using(var other=new RecoveryStore(directory)) { other.Save(recoveredDocument,[],null);chosenPath=other.DraftRecoveryPath; }
        var chosenBytes=File.ReadAllBytes(chosenPath);
        var store=new RecoveryStore(directory);var barrier=new RecoveryBarrierWriter(store);
        WaitRecovery(studio.SetRecoveryForVerificationAsync(store,new RecoveryWriteCoordinator(barrier)));
        Task? pending=null;
        try
        {
            studio.LoadDocument(new ProjectDocument { Name="Eski oturum" });
            WaitRecovery(studio.ClearRecoveryForVerificationAsync());
            studio.Commit("Eski değişiklik",()=>studio.Document.Name="Eski bekleyen görüntü");
            pending=studio.TriggerRecoveryForVerificationAsync();PumpUntil(()=>barrier.Entered.IsSet);
            var previousGeneration=(long)typeof(MainWindow).GetField("recoveryGeneration",PrivateInstance)!.GetValue(studio)!;
            var chosen=store.LoadCandidate(chosenPath);
            Invoke(studio,"RestoreRecovery",chosen);
            var currentGeneration=(long)typeof(MainWindow).GetField("recoveryGeneration",PrivateInstance)!.GetValue(studio)!;
            Check(currentGeneration>previousGeneration && DocumentSession.ContentEquals(recoveredDocument,studio.Document)
                && typeof(MainWindow).GetField("lastRecoveryAt",PrivateInstance)!.GetValue(studio) is null
                && File.ReadAllBytes(chosenPath).SequenceEqual(chosenBytes),
                "Recovery restore invalidates the old in-flight generation before its completion",checks);
            barrier.Release.Set();WaitRecovery(pending);
            bool NewSnapshotPresent()
            {
                try { return File.Exists(store.DraftRecoveryPath) && DocumentSession.ContentEquals(recoveredDocument,RecoveryEnvelope.Deserialize(File.ReadAllBytes(store.DraftRecoveryPath)).Document); }
                catch(IOException) { return false; }
                catch(InvalidDataException) { return false; }
            }
            PumpUntil(NewSnapshotPresent);
            Check(File.ReadAllBytes(chosenPath).SequenceEqual(chosenBytes)
                && DocumentSession.ContentEquals(recoveredDocument,studio.Document),
                "Recovery queue replaces the old snapshot with recovered content without altering the selected source",checks);
        }
        finally
        {
            barrier.Release.Set();if(pending is not null)WaitRecovery(pending);
            RestoreRecoveryDirectory(studio,originalDirectory);barrier.Entered.Dispose();barrier.Release.Dispose();
        }
    }

    private static void VerifySingleGradePostCommit(MainWindow studio,List<string> checks)
    {
        var source=new PatternPiece { Name="Temel",Points=[new(0,0),new(20,0),new(20,20),new(0,20)] };
        studio.LoadDocument(new ProjectDocument { Pieces=[source] });studio.Select(source.Id);
        var before=studio.Document.Clone();var session=(DocumentSession)typeof(MainWindow).GetField("session",PrivateInstance)!.GetValue(studio)!;
        var tools=new TextileToolsWindow(studio);var root=(FrameworkElement)tools.Content;
        root.Measure(new Size(680,540));root.Arrange(new Rect(0,0,680,540));root.UpdateLayout();
        var button=Descendants<Button>(root).Single(b=>Equals(b.Content,"Yeni bedeni projeye ekle"));
        var result=(TextBlock)typeof(TextileToolsWindow).GetField("result",PrivateInstance)!.GetValue(tools)!;
        var old=SynchronizationContext.Current;SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(studio.Dispatcher));
        try
        {
            var undo=session.UndoCount;studio.AfterCommitBeforeRefreshForVerification=()=>throw new InvalidOperationException("injected refresh failure");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var task=tools.GradeTaskForVerification!;PumpUntil(()=>task.IsCompleted);task.GetAwaiter().GetResult();
            studio.AfterCommitBeforeRefreshForVerification=null;
            Check(studio.Document.Pieces.Count==2 && session.UndoCount==undo+1 && result.Text.Contains("projeye uygulandı") && !button.IsEnabled,"Single grading reports committed result and locks retry when UI refresh fails",checks);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(studio.Document.Pieces.Count==2 && session.UndoCount==undo+1,"Single grading cannot repeat after committed refresh failure",checks);
            Check(session.Undo() && DocumentSession.ContentEquals(before,studio.Document),"Single grading refresh failure remains undoable",checks);
        }
        finally { studio.AfterCommitBeforeRefreshForVerification=null;SynchronizationContext.SetSynchronizationContext(old);tools.Close(); }
    }
    private static void VerifyToolCommandPostCommit(MainWindow studio,List<string> checks)
    {
        foreach(var (tab,label) in new[] { (0,"Artışları projeye uygula"),(1,"Nokta ekle"),(2,"Tüm proje parçalarını yerleştir") })
        {
            var piece=new PatternPiece { Name="Araç kaynağı",Points=[new(50,50),new(70,50),new(70,70),new(50,70)] };
            studio.LoadDocument(new ProjectDocument { Pieces=[piece] });studio.Select(piece.Id);
            var before=studio.Document.Clone();var session=(DocumentSession)typeof(MainWindow).GetField("session",PrivateInstance)!.GetValue(studio)!;
            var tools=new TextileToolsWindow(studio);var root=(FrameworkElement)tools.Content;
            var old=SynchronizationContext.Current;SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(studio.Dispatcher));
            root.Measure(new Size(680,540));root.Arrange(new Rect(0,0,680,540));root.UpdateLayout();
            Descendants<TabControl>(root).Single().SelectedIndex=tab;root.UpdateLayout();
            if(tab==1)
            {
                Descendants<TextBox>(root).Single(t=>AutomationProperties.GetName(t)=="Yeni nokta X · mm").Text="60";
                Descendants<TextBox>(root).Single(t=>AutomationProperties.GetName(t)=="Yeni nokta Y · mm").Text="50";
            }
            var button=Descendants<Button>(root).Single(b=>Equals(b.Content,label));
            var result=(TextBlock)typeof(TextileToolsWindow).GetField("result",PrivateInstance)!.GetValue(tools)!;
            try
            {
                var undo=session.UndoCount;studio.AfterCommitBeforeRefreshForVerification=()=>throw new InvalidOperationException("injected refresh failure");
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if(tab==1)
                {
                    var vertexPending=tools.VertexMoveTaskForVerification ?? throw new InvalidOperationException("Nokta ekleme görevi başlatılmadı.");
                    PumpUntil(()=>vertexPending.IsCompleted);vertexPending.GetAwaiter().GetResult();
                }
                if(tab==2 && tools.BasicNestingTaskForVerification is { } pending) { PumpUntil(()=>pending.IsCompleted);pending.GetAwaiter().GetResult(); }
                studio.AfterCommitBeforeRefreshForVerification=null;
                Check(session.UndoCount==undo+1 && result.Text.Contains("projeye uygulandı") && !button.IsEnabled,
                    label+" reports committed state and locks retry after refresh failure",checks);
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if(tab==1 && tools.VertexMoveTaskForVerification is { } vertexRetry) { PumpUntil(()=>vertexRetry.IsCompleted);vertexRetry.GetAwaiter().GetResult(); }
                if(tab==2 && tools.BasicNestingTaskForVerification is { } retry) { PumpUntil(()=>retry.IsCompleted);retry.GetAwaiter().GetResult(); }
                Check(session.UndoCount==undo+1 && session.Undo() && DocumentSession.ContentEquals(before,studio.Document),
                    label+" cannot repeat and remains undoable after refresh failure",checks);
            }
            finally { studio.AfterCommitBeforeRefreshForVerification=null;SynchronizationContext.SetSynchronizationContext(old);tools.Close(); }
        }
    }

    private static void VerifyOffsetPostCommit(MainWindow studio,List<string> checks)
    {
        var source=new PatternPiece { Name="Pay kaynağı",Points=[new(0,0),new(20,0),new(20,20),new(0,20)] };
        studio.LoadDocument(new ProjectDocument { Pieces=[source] });studio.Select(source.Id);
        var before=studio.Document.Clone();var session=(DocumentSession)typeof(MainWindow).GetField("session",PrivateInstance)!.GetValue(studio)!;
        var tools=new TextileToolsWindow(studio);var root=(FrameworkElement)tools.Content;
        root.Measure(new Size(680,540));root.Arrange(new Rect(0,0,680,540));root.UpdateLayout();
        Descendants<TabControl>(root).Single().SelectedIndex=1;root.UpdateLayout();
        var button=Descendants<Button>(root).Single(b=>Equals(b.Content,"Dikiş payını ayrı parça oluştur"));
        var result=(TextBlock)typeof(TextileToolsWindow).GetField("result",PrivateInstance)!.GetValue(tools)!;
        var method=typeof(TextileToolsWindow).GetMethod("CreateOffsetAsync",PrivateInstance)!;
        var old=SynchronizationContext.Current;SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(studio.Dispatcher));
        try
        {
            var undo=session.UndoCount;studio.AfterCommitBeforeRefreshForVerification=()=>throw new InvalidOperationException("injected refresh failure");
            var task=(Task)method.Invoke(tools,["1"])!;PumpUntil(()=>task.IsCompleted);task.GetAwaiter().GetResult();
            studio.AfterCommitBeforeRefreshForVerification=null;
            Check(studio.Document.Pieces.Count==2 && session.UndoCount==undo+1 && result.Text.Contains("projeye eklendi") && result.Text.Contains("görünüm yenilenemedi") && !button.IsEnabled,
                "Offset reports committed result and prevents retry when refresh fails",checks);
            var retry=(Task)method.Invoke(tools,["1"])!;PumpUntil(()=>retry.IsCompleted);retry.GetAwaiter().GetResult();
            Check(studio.Document.Pieces.Count==2 && session.UndoCount==undo+1 && session.Undo() && DocumentSession.ContentEquals(before,studio.Document),
                "Offset retry after committed refresh failure cannot duplicate and remains undoable",checks);
        }
        finally { studio.AfterCommitBeforeRefreshForVerification=null;SynchronizationContext.SetSynchronizationContext(old);tools.Close(); }
    }
    private static void VerifyIndependentRecoveryWarnings(MainWindow studio,string reportPath,List<string> checks)
    {
        var show=typeof(MainWindow).GetMethod("ShowRecoveryWarning",PrivateInstance)!;
        var clear=typeof(MainWindow).GetMethod("ClearRecoveryWarning",PrivateInstance)!;
        var notice=(TextBlock)typeof(MainWindow).GetField("recoveryNotice",PrivateInstance)!.GetValue(studio)!;
        show.Invoke(studio,["Kaydetme hatası","save detail",RecoveryWarningState.Kind.Save]);
        show.Invoke(studio,["Temizleme hatası","clear detail",RecoveryWarningState.Kind.Clear]);
        Check(notice.Visibility==Visibility.Visible && notice.Text.Contains("Kaydetme hatası") && notice.Text.Contains("Temizleme hatası") && notice.ToolTip!.ToString()!.Contains("save detail") && notice.ToolTip.ToString()!.Contains("clear detail"),"Independent recovery warnings retain both messages and details",checks);
        clear.Invoke(studio,[RecoveryWarningState.Kind.Clear]);
        Check(notice.Visibility==Visibility.Visible && notice.Text.Contains("Kaydetme hatası") && !notice.Text.Contains("Temizleme hatası"),"Resolving clear warning keeps active recovery save warning visible",checks);
        show.Invoke(studio,["Temizleme hatası","clear detail",RecoveryWarningState.Kind.Clear]);
        clear.Invoke(studio,[RecoveryWarningState.Kind.Save]);
        Check(notice.Visibility==Visibility.Visible && notice.Text.Contains("Temizleme hatası") && !notice.Text.Contains("Kaydetme hatası"),"Resolving save warning keeps active recovery clear warning visible",checks);
        clear.Invoke(studio,[RecoveryWarningState.Kind.Clear]);
        var clearGeneral=typeof(MainWindow).GetMethod("ClearGeneralRecoveryWarning",PrivateInstance)!;
        show.Invoke(studio,["Birinci genel uyarı","detay bir",RecoveryWarningState.Kind.General]);
        show.Invoke(studio,["İkinci genel uyarı","detay iki",RecoveryWarningState.Kind.General]);
        Check(notice.Text.Contains("Birinci genel uyarı") && notice.Text.Contains("İkinci genel uyarı") && notice.ToolTip!.ToString()!.Contains("detay bir") && notice.ToolTip.ToString()!.Contains("detay iki"),"Separate general recovery warnings keep both details",checks);
        clearGeneral.Invoke(studio,["Birinci genel uyarı"]);
        Check(!notice.Text.Contains("Birinci genel uyarı") && notice.Text.Contains("İkinci genel uyarı"),"Resolving one general recovery warning keeps the other",checks);
        clear.Invoke(studio,[RecoveryWarningState.Kind.General]);
        studio.LoadDocument(new ProjectDocument { Name="Kurtarma uyarı testi" });
        studio.Commit("Düzenleme",()=>studio.Document.Name="Kaydedilecek çalışma");
        show.Invoke(studio,["Otomatik kurtarma güncellenemedi. Son değişikliklerinizi Kaydet ile koruyun.","diagnostic retained",RecoveryWarningState.Kind.Save]);
        var savePath=Path.Combine(Path.GetDirectoryName(reportPath)!,"warning-save-"+Guid.NewGuid().ToString("N")+".tcad");
        Invoke(studio,"SaveTo",savePath);
        Check(notice.Visibility==Visibility.Visible && notice.Text.Contains("kaydedilmemiş değişiklik yok") && notice.ToolTip!.ToString()!.Contains("diagnostic retained"),"Manual save keeps recovery failure visible with advice matching saved state",checks);
        studio.Commit("Yeni düzenleme",()=>studio.Document.Name="Yeni değişiklik");
        Check(notice.Text.Contains("Son değişikliklerinizi Kaydet") && notice.ToolTip!.ToString()!.Contains("diagnostic retained"),"New edits restore save advice while recovery remains unavailable",checks);
        clear.Invoke(studio,[RecoveryWarningState.Kind.Save]);
    }
    private static void VerifyBulkPreviewDirection(List<string> checks)
    {
        // Check the pixels actually drawn for an asymmetric open contour; WPF's
        // flattened fill geometry can be empty for a non-filled open path.
        var source=new PatternPiece { Closed=false,Points=[new(0,0),new(10,0),new(10,10),new(3,8)] };
        var preview=new BulkGradePreview { Width=300,Height=200,Margin=new Thickness(0) };
        static bool HasStroke(byte[] pixels,int stride,double expectedY,bool sourceStroke)
        {
            var center=(int)Math.Round(expectedY);
            for(var y=Math.Max(0,center-3);y<=Math.Min(199,center+3);y++)
            for(var x=130;x<=170;x++)
            {
                var offset=y*stride+x*4;
                var b=pixels[offset];var g=pixels[offset+1];var r=pixels[offset+2];
                if(sourceStroke ? r>180 && g>120 && b<160 : r<120 && g>130 && b>110)return true;
            }
            return false;
        }
        foreach(var displacement in new[] { 1d,2d })
        {
            var grade=new PatternPiece { Closed=false,Points=source.Points.Select(p=>new Point2(p.X,p.Y+displacement)).ToList() };
            preview.SetPieces(source,grade,$"Beden {displacement}");
            preview.Measure(new Size(300,200));preview.Arrange(new Rect(0,0,300,200));preview.UpdateLayout();
            var bitmap=new RenderTargetBitmap(300,200,96,96,PixelFormats.Pbgra32);bitmap.Render(preview);
            const int stride=300*4;var pixels=new byte[stride*200];bitmap.CopyPixels(pixels,stride,0);
            var expectedScale=168d/(10d+displacement);
            Check(preview.SelectedId==grade.Id && HasStroke(pixels,stride,16,true)
                && HasStroke(pixels,stride,16+displacement*expectedScale,false),
                $"Bulk grade preview draws positive Y downwards at shared scale for offset {displacement}",checks);
        }
    }
    private static void VerifyBulkPreviewGrainline(List<string> checks)
    {
        var source=new PatternPiece { Closed=true,Points=[new(0,0),new(100,0),new(100,100),new(0,100)] };
        var grade=source.Clone();grade.Id=Guid.NewGuid();
        var preview=new BulkGradePreview { Width=300,Height=200,Margin=new Thickness(0) };
        byte[] RenderPixels()
        {
            preview.Measure(new Size(300,200));preview.Arrange(new Rect(0,0,300,200));preview.UpdateLayout();
            var bitmap=new RenderTargetBitmap(300,200,96,96,PixelFormats.Pbgra32);bitmap.Render(preview);
            var pixels=new byte[300*4*200];bitmap.CopyPixels(pixels,300*4,0);return pixels;
        }
        static bool ChangedIn(byte[] first,byte[] second,int minX,int maxX,int minY,int maxY)
        {
            var changed=0;
            for(var y=minY;y<=maxY;y++)for(var x=minX;x<=maxX;x++)
            {
                var i=(y*300+x)*4;
                if(Math.Abs(first[i]-second[i])+Math.Abs(first[i+1]-second[i+1])+Math.Abs(first[i+2]-second[i+2])>30)changed++;
            }
            return changed>5;
        }
        preview.SetPieces(source,grade,"L");var empty=RenderPixels();
        source.Grainline=new Grainline { Start=new(20,20),End=new(80,20) };
        preview.SetPieces(source,grade,"L");var baseOnly=RenderPixels();
        Check(ChangedIn(empty,baseOnly,100,200,47,53),"Bulk preview renders the source grainline inside its contour",checks);
        source.Grainline=null;grade.Grainline=new Grainline { Start=new(20,80),End=new(80,80),NeedsReview=true };
        preview.SetPieces(source,grade,"L");var gradeOnly=RenderPixels();
        Check(ChangedIn(empty,gradeOnly,100,200,147,153),"Bulk preview renders the graded grainline inside its contour",checks);
        source.Grainline=new Grainline { Start=new(20,20),End=new(80,20) };
        preview.SetPieces(source,grade,"L");var both=RenderPixels();var help=AutomationProperties.GetHelpText(preview);
        Check(ChangedIn(gradeOnly,both,100,200,47,53) && ChangedIn(baseOnly,both,100,200,147,153) &&
            help.Contains("Temel iplik yönü",StringComparison.Ordinal) && help.Contains("Seçili bedenin iplik yönü",StringComparison.Ordinal) && help.Contains("kontrol",StringComparison.Ordinal),
            "Bulk preview shows both grainlines and explains them accessibly",checks);
        source.Grainline=new Grainline { Start=new(-50,50),End=new(150,50) };grade.Grainline=null;
        preview.SetPieces(source,grade,"L");var outside=RenderPixels();
        Check(ChangedIn(empty,outside,13,23,97,103),"Bulk preview includes grainline endpoints outside the contour in its viewport",checks);
        preview.Clear();
        Check(!preview.HasGeometry && preview.SelectedId is null && !AutomationProperties.GetHelpText(preview).Contains("Temel iplik yönü",StringComparison.Ordinal),
            "Clearing bulk preview removes grainline geometry and its accessible description",checks);
    }
    private static void VerifyBulkPreviewFit(List<string> checks)
    {
        // Render and measure visible colored pixels, so this verifies the actual
        // viewport rather than private scaling fields or an implementation formula.
        static (int Left,int Top,int Right,int Bottom,int Count) Bounds(BulkGradePreview preview,int width,int height)
        {
            preview.Width=width;preview.Height=height;preview.Measure(new Size(width,height));
            preview.Arrange(new Rect(0,0,width,height));preview.UpdateLayout();
            var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(preview);
            var stride=width*4;var data=new byte[stride*height];bitmap.CopyPixels(data,stride,0);
            var left=width;var top=height;var right=-1;var bottom=-1;var count=0;
            for(var y=0;y<height;y++)for(var x=0;x<width;x++)
            {
                var i=y*stride+x*4;var b=data[i];var g=data[i+1];var r=data[i+2];
                if(!((r>170 && g>100 && b<180) || (r<130 && g>120 && b>105)))continue;
                left=Math.Min(left,x);right=Math.Max(right,x);top=Math.Min(top,y);bottom=Math.Max(bottom,y);count++;
            }
            return (left,top,right,bottom,count);
        }
        var preview=new BulkGradePreview { Margin=new Thickness(0) };
        var wide=new PatternPiece { Closed=true,Points=[new(0,0),new(200,0),new(200,50),new(0,50)] };
        var grade=wide.Clone();grade.Id=Guid.NewGuid();preview.SetPieces(wide,grade,"L");
        var horizontal=Bounds(preview,360,160);var hw=horizontal.Right-horizontal.Left;var hh=horizontal.Bottom-horizontal.Top;
        Check(hw>=320 && hw<=334 && hh>=78 && hh<=86 && Math.Abs((double)hw/hh-4)<0.25 && horizontal.Left>=12 && horizontal.Right<=347,
            $"Wide bulk preview uses available width while preserving proportions ({hw}×{hh})",checks);
        var tall=new PatternPiece { Closed=true,Points=[new(0,0),new(50,0),new(50,200),new(0,200)] };
        grade=tall.Clone();grade.Id=Guid.NewGuid();preview.SetPieces(tall,grade,"L");
        var vertical=Bounds(preview,160,360);var vw=vertical.Right-vertical.Left;var vh=vertical.Bottom-vertical.Top;
        Check(vh>=320 && vh<=334 && vw>=78 && vw<=86 && Math.Abs((double)vh/vw-4)<0.25 && vertical.Top>=12 && vertical.Bottom<=347,
            $"Tall bulk preview uses available height while preserving proportions ({vw}×{vh})",checks);
        wide.Grainline=new Grainline { Start=new(-100,25),End=new(300,25) };grade=wide.Clone();grade.Id=Guid.NewGuid();
        preview.SetPieces(wide,grade,"L");var grain=Bounds(preview,360,160);
        Check(grain.Left>=12 && grain.Left<=20 && grain.Right>=339 && grain.Right<=347,
            "Bulk preview fits both grainline endpoints outside the contour inside the padded viewport",checks);
        var line=new PatternPiece { Closed=false,Points=[new(0,0),new(100,0)] };grade=line.Clone();grade.Id=Guid.NewGuid();
        preview.SetPieces(line,grade,"L");var hline=Bounds(preview,360,160);
        Check(hline.Count>0 && hline.Right-hline.Left>=320 && hline.Top>=75 && hline.Bottom<=85,
            "Bulk preview handles zero-height geometry at the center without losing its width",checks);
        line.Points=[new(0,0),new(0,100)];grade=line.Clone();grade.Id=Guid.NewGuid();preview.SetPieces(line,grade,"L");
        var vline=Bounds(preview,160,360);
        Check(vline.Count>0 && vline.Bottom-vline.Top>=320 && vline.Left>=75 && vline.Right<=85,
            "Bulk preview handles zero-width geometry at the center without losing its height",checks);
        preview.SetPieces(new PatternPiece { Points=[] },grade,"empty");
        Check(!preview.HasGeometry && Bounds(preview,160,360).Count==0,"Bulk preview clears empty geometry safely",checks);
    }
    private static void VerifyBulkGrading(MainWindow studio,string reportPath,List<string> checks)
    {
        var original=new PatternPiece { Name="Ön",Closed=true,Points=[new(0,0),new(100,0),new(100,100),new(0,100)],Metadata=new PieceMetadata { Role="Kesim",Material="Pamuk",Quantity=7 } };
        studio.LoadDocument(new ProjectDocument { Pieces=[original] });original=studio.Document.Pieces[0];studio.Select(original.Id);
        var before=studio.Document.Clone();var session=(DocumentSession)typeof(MainWindow).GetField("session",PrivateInstance)!.GetValue(studio)!;
        var added=false;var window=new BulkGradingWindow(studio,studio,original,[new(0,0),new(1,0),new(1,1),new(0,1)],()=>true,()=>added=true);
        Render(window,Path.ChangeExtension(reportPath,".bulk-grading.png"),680,540);
        var bulkRoot=(FrameworkElement)window.Content;
        var bulkInstructions=Descendants<TextBlock>(bulkRoot).Single(t=>t.Text.Contains("isteğe bağlı kesim adedi"));
        var quantityInstructions=Descendants<TextBlock>(bulkRoot).Single(t=>t.Text.Contains("Adet kaynaktan kopyalanmaz"));
        Check(WarningReachable(bulkInstructions,bulkRoot) && WarningReachable(quantityInstructions,bulkRoot)
            && WarningReachable(window.Entries,bulkRoot) && AutomationProperties.GetHelpText(window.Entries).Contains("12"),
            "Bulk grading quantity instructions and input remain reachable at 680 × 540",checks);
        var old=SynchronizationContext.Current;SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(studio.Dispatcher));
        try
        {
            void Calculate(){var t=window.CalculateAsync();PumpUntil(()=>t.IsCompleted);t.GetAwaiter().GetResult();}
            window.Entries.Text="S; -1; 2\nM; 0\nL; 1; 3";window.IncludeSize.IsChecked=true;window.CopyRole.IsChecked=true;Calculate();
            Check(window.Ready?.Count==3 && window.AddButton.IsEnabled && DocumentSession.ContentEquals(before,studio.Document),"Bulk grading previews three sizes without changing source project",checks);
            Check(window.Ready!.Select(p=>p.Id).Distinct().Count()==3 && window.Ready.Select(p=>p.Metadata?.Quantity).SequenceEqual(new int?[]{2,null,3}) && window.Ready.All(p=>p.Metadata?.Role=="Kesim" && p.Metadata?.Material=="Pamuk") && window.Ready.Select(p=>p.Metadata!.Size).SequenceEqual(new[]{"S","M","L"}),"Bulk grading previews entered quantities and leaves missing quantity unset",checks);
            var quantityLabels=window.PreviewList.Items.Cast<string>().ToArray();
            Check(quantityLabels[0].Contains("adet 2") && quantityLabels[1].Contains("adet girilmedi") && quantityLabels[2].Contains("adet 3"),
                "Bulk grading preview identifies entered and missing quantities",checks);
            Check(window.GeometryPreview.HasGeometry && window.GeometryPreview.SelectedId==window.Ready![0].Id,"Bulk grading displays first generated contour against the source",checks);
            window.PreviewList.SelectedIndex=1;
            Check(window.ChangeSummary.Text.Contains("aynı geometri") && window.ChangeSummary.Text.Contains("0 mm"),"Unchanged graded geometry reports zero displacement",checks);
            window.PreviewList.SelectedIndex=2;
            Check(window.GeometryPreview.SelectedId==window.Ready![2].Id && AutomationProperties.GetHelpText(window.GeometryPreview).Contains("aynı koordinat"),"Choosing a size updates the accessible shared-scale geometry preview",checks);
            Check(window.ChangeSummary.Text.Contains("3 / 4") && window.ChangeSummary.Text.Contains(MeasurementInput.Format(Math.Sqrt(2))),"Grading comparison reports changed vertices and maximum displacement",checks);
            Render(window,Path.ChangeExtension(reportPath,".bulk-grade-geometry.png"),720,1000);
            window.Entries.Text="S; -1; 5\nM; 0\nL; 1; 3";
            Check(!window.GeometryPreview.HasGeometry && window.ChangeSummary.Text=="" && window.Status.Text.Contains("yeniden hesaplayın") && !window.AddButton.IsEnabled,"Editing batch inputs clears the geometry and old ready status",checks);
            window.AddReady();
            Check(DocumentSession.ContentEquals(before,studio.Document),"Changed batch text cannot apply a stale quantity preview",checks);
            window.Entries.Text="L; 1\n l ; 2";Calculate();
            Check(window.Ready is null && !window.AddButton.IsEnabled && DocumentSession.ContentEquals(before,studio.Document),"Duplicate size names reject entire bulk preview",checks);
            window.Entries.Text="S; -1\n\n l ; 1\nL; 2";Calculate();
            Check(window.Status.Text.Contains("4. satır") && window.Status.Text.Contains("3. satır") && DocumentSession.ContentEquals(before,studio.Document),"Duplicate batch size reports both original line numbers",checks);
            window.Entries.Text="S; -1\nX; bozuk";Calculate();
            Check(window.Status.Text.Contains("2. satır") && DocumentSession.ContentEquals(before,studio.Document),"Invalid batch step identifies its input line",checks);
            Render(window,Path.ChangeExtension(reportPath,".bulk-grading-error.png"),550,500);
            Check(FullyVisible(window.Status,(FrameworkElement)window.Content),"Bulk grading error remains visible in minimum window",checks);
            window.Entries.Text="S; -1; 2\nL; 1; 0";Calculate();
            Check(window.Ready is null && !window.AddButton.IsEnabled && window.Status.Text.Contains("2. satır") && DocumentSession.ContentEquals(before,studio.Document),
                "Invalid quantity rejects the complete batch and identifies its line",checks);
            window.Entries.Text="S; -1\nL; 1000000000";Calculate();
            Check(window.Ready is null && !window.AddButton.IsEnabled && DocumentSession.ContentEquals(before,studio.Document),"A later invalid size rejects the entire batch without modifying the project",checks);
            window.Entries.Text=new string('x',1_000_001);Calculate();
            Check(window.Ready is null && window.Status.Text.Contains("çok uzun") && DocumentSession.ContentEquals(before,studio.Document),"Oversized batch input is rejected before parsing without modifying the project",checks);
            window.Entries.Text="S; -1; 2\nM; 0\nL; 1; 3";Calculate();
            var count=session.UndoCount;window.AddReady();
            Check(added && studio.Document.Pieces.Count==4 && session.UndoCount==count+1,"Bulk grading commits all sizes in one history entry",checks);
            Check(studio.Document.Pieces.Skip(1).Select(p=>p.Metadata?.Quantity).SequenceEqual(new int?[]{2,null,3})
                && studio.Document.Pieces[0].Metadata?.Quantity==7,
                "Bulk grading applies each entered quantity without copying or changing the source",checks);
            Check(session.Undo() && DocumentSession.ContentEquals(before,studio.Document),"One undo removes the complete bulk grading batch",checks);
        }
        finally { SynchronizationContext.SetSynchronizationContext(old); }
        studio.LoadDocument(before);var postSource=studio.Document.Pieces[0];studio.Select(postSource.Id);
        var postWindow=new BulkGradingWindow(studio,studio,postSource,[new(0,0),new(0,0),new(0,0),new(0,0)],()=>true,()=>{});
        var priorContext=SynchronizationContext.Current;SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(studio.Dispatcher));
        try
        {
            postWindow.Entries.Text="XL; 1";var task=postWindow.CalculateAsync();PumpUntil(()=>task.IsCompleted);task.GetAwaiter().GetResult();
            var oldCount=session.UndoCount;studio.AfterCommitBeforeRefreshForVerification=()=>throw new InvalidOperationException("injected refresh failure");
            postWindow.AddReady();studio.AfterCommitBeforeRefreshForVerification=null;
            Check(studio.Document.Pieces.Count==2 && session.UndoCount==oldCount+1 && postWindow.Status.Text.Contains("projeye eklendi") && !postWindow.AddButton.IsEnabled,"Post-commit display failure reports added sizes and prevents duplicate retry",checks);
            Check(session.Undo() && DocumentSession.ContentEquals(before,studio.Document),"Post-commit display failure preserves one undo for the full batch",checks);
        }
        finally { studio.AfterCommitBeforeRefreshForVerification=null;SynchronizationContext.SetSynchronizationContext(priorContext);postWindow.Close(); }
        studio.LoadDocument(before);var source=studio.Document.Pieces[0];studio.Select(source.Id);
        var stale=new BulkGradingWindow(studio,studio,source,[new(0,0),new(0,0),new(0,0),new(0,0)],()=>true,()=>{});
        var sync=SynchronizationContext.Current;SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(studio.Dispatcher));
        try
        {
            stale.Entries.Text="XL; 1";var task=stale.CalculateAsync();PumpUntil(()=>task.IsCompleted);task.GetAwaiter().GetResult();
            studio.Document.Name="Değişen proje";var expected=studio.Document.Clone();stale.AddReady();
            Check(DocumentSession.ContentEquals(expected,studio.Document) && stale.Ready is null,"Bulk grading rejects a changed project after preview",checks);
        }
        finally { SynchronizationContext.SetSynchronizationContext(sync);stale.Close(); }
        studio.LoadDocument(before);source=studio.Document.Pieces[0];studio.Select(source.Id);
        var cancelWindow=new BulkGradingWindow(studio,studio,source,[new(0,0),new(0,0),new(0,0),new(0,0)],()=>true,()=>{});
        var prior=SynchronizationContext.Current;SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(studio.Dispatcher));
        using var reached=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
        try
        {
            cancelWindow.Entries.Text="S; -1\nL; 1";
            cancelWindow.BeforeBulkCalculationForVerification=()=>{reached.Set();if(!release.Wait(TimeSpan.FromSeconds(20)))throw new TimeoutException();};
            var task=cancelWindow.CalculateAsync();PumpUntil(()=>reached.IsSet);
            cancelWindow.CancelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(!cancelWindow.CancelButton.IsEnabled && cancelWindow.Status.Text.Contains("İptal istendi") && DocumentSession.ContentEquals(before,studio.Document),"Bulk grading acknowledges cancellation while the worker is stopping",checks);
            release.Set();PumpUntil(()=>task.IsCompleted);task.GetAwaiter().GetResult();
            Check(cancelWindow.Ready is null && !cancelWindow.AddButton.IsEnabled && cancelWindow.Status.Text.Contains("iptal") && DocumentSession.ContentEquals(before,studio.Document),"Cancelling bulk calculation leaves the project unchanged",checks);
        }
        finally { release.Set();SynchronizationContext.SetSynchronizationContext(prior);cancelWindow.Close(); }
    }
    private static void VerifySeamLengthComparison(MainWindow studio,List<string> checks)
    {
        var a=new PatternPiece { Name="Kapalı kenar",Closed=true,Points=[new(0,0),new(10,0),new(10,5),new(0,5),new(0,0)] };
        var b=new PatternPiece { Name="Açık kenar",Closed=false,Points=[new(0,0),new(0,7)] };
        ProjectDocument Fixture()=>new() { Pieces=[a.Clone(),b.Clone()] };
        studio.LoadDocument(Fixture());studio.Select(a.Id);Guid? source=a.Id;
        var window=new SeamLengthComparisonWindow(studio,studio,()=>source);
        object Side(string name)=>typeof(SeamLengthComparisonWindow).GetField(name,PrivateInstance)!.GetValue(window)!;
        T Control<T>(object side,string name) where T:class => (T)side.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public)!.GetValue(side)!;
        var first=Side("first");var second=Side("second");
        var start=Control<ComboBox>(first,"Start");var end=Control<ComboBox>(first,"End");var direction=Control<ComboBox>(first,"Direction");
        var otherDirection=Control<ComboBox>(second,"Direction");
        var fullLoop=Control<CheckBox>(first,"FullLoop");var otherFullLoop=Control<CheckBox>(second,"FullLoop");
        var preview=Control<FrameworkElement>(first,"Preview");
        var result=(TextBlock)typeof(SeamLengthComparisonWindow).GetField("result",PrivateInstance)!.GetValue(window)!;
        var compare=(Button)typeof(SeamLengthComparisonWindow).GetField("compare",PrivateInstance)!.GetValue(window)!;
        var expectedEase=(TextBox)typeof(SeamLengthComparisonWindow).GetField("expectedEase",PrivateInstance)!.GetValue(window)!;
        var tolerance=(TextBox)typeof(SeamLengthComparisonWindow).GetField("tolerance",PrivateInstance)!.GetValue(window)!;
        try
        {
            Check(direction.IsEnabled && !otherDirection.IsEnabled && AutomationProperties.GetName(start).Contains("başlangıç")
                && AutomationProperties.GetName(end).Contains("bitiş") && AutomationProperties.GetName(direction).Contains("yönü")
                && AutomationProperties.GetName(preview).Contains("önizlemesi") && AutomationProperties.GetLiveSetting(result)==AutomationLiveSetting.Polite,
                "Seam comparison exposes named controls and enables direction only for closed contours",checks);
            compare.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(result.Text.Contains("1. yol: 10 mm") && result.Text.Contains("2. yol: 7 mm")
                && result.Text.Contains("(2 − 1): -3 mm") && result.Text.Contains("Mutlak fark: 3 mm") && result.Text.Contains("üretim kabulü değildir"),
                "Seam comparison reports both segment lengths and signed second-minus-first difference",checks);
            expectedEase.Text="-2";
            Check(!result.Text.Contains("1. yol:") && preview.GetType().GetField("path",PrivateInstance)!.GetValue(preview) is null,
                "Changing the target clears the previous comparison and preview",checks);
            tolerance.Text="1";
            compare.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(result.Text.Contains("Hedeften sapma: -1 mm") && result.Text.Contains("Tolerans: içinde (±1 mm)"),
                "Negative target accepts a deviation exactly on the tolerance boundary",checks);
            tolerance.Text="0,5";
            Check(!result.Text.Contains("1. yol:") && preview.GetType().GetField("path",PrivateInstance)!.GetValue(preview) is null,
                "Changing the tolerance clears the previous comparison and preview",checks);
            compare.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(result.Text.Contains("Hedeften sapma: -1 mm") && result.Text.Contains("Tolerans: dışında (±0.5 mm)"),
                "Deviation beyond the entered tolerance is reported outside",checks);
            expectedEase.Text="2";tolerance.Text="5";
            compare.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(result.Text.Contains("Hedeften sapma: -5 mm") && result.Text.Contains("Tolerans: içinde (±5 mm)"),
                "Positive target retains second-minus-first sign and includes the boundary",checks);
            tolerance.Text="-1";
            compare.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(result.Text.Contains("Tolerans için sıfır veya pozitif") && preview.GetType().GetField("path",PrivateInstance)!.GetValue(preview) is null,
                "Negative tolerance is rejected without displaying an old preview",checks);
            expectedEase.Text="0";tolerance.Text="0";
            direction.SelectedIndex=1;
            Check(!result.Text.Contains("1. yol:") && preview.GetType().GetField("path",PrivateInstance)!.GetValue(preview) is null,
                "Changing direction clears the old result and preview",checks);
            compare.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var path=(ContourPath)preview.GetType().GetField("path",PrivateInstance)!.GetValue(preview)!;
            Check(result.Text.Contains("1. yol: 20 mm") && result.Text.Contains("(2 − 1): -13 mm")
                && path.StartIndex==0 && path.EndIndex==1 && path.Direction==ContourDirection.Backward,
                "Closed backward path follows the other three edges",checks);
            // Measure the element without its layout margin and sample around
            // calculated segment midpoints, allowing for WPF antialiasing.
            preview.Margin=new Thickness(0);preview.Height=170;
            preview.Measure(new Size(300,170));preview.Arrange(new Rect(0,0,300,170));preview.UpdateLayout();
            int width=(int)Math.Ceiling(preview.ActualWidth),height=(int)Math.Ceiling(preview.ActualHeight);
            var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(preview);
            var pixels=new byte[width*height*4];bitmap.CopyPixels(pixels,width*4,0);
            bool RedNear(double x,double y)
            {
                for(int py=Math.Max(0,(int)Math.Floor(y)-3);py<=Math.Min(height-1,(int)Math.Ceiling(y)+3);py++)
                for(int px=Math.Max(0,(int)Math.Floor(x)-3);px<=Math.Min(width-1,(int)Math.Ceiling(x)+3);px++)
                { var i=(py*width+px)*4;if(pixels[i+2]>150 && pixels[i+2]>pixels[i+1]*2 && pixels[i+2]>pixels[i]*2)return true; }
                return false;
            }
            double scale=Math.Min((preview.ActualWidth-20)/10,(preview.ActualHeight-20)/5);
            Check(RedNear(10,10+2.5*scale) && RedNear(10+5*scale,10+5*scale) && !RedNear(10+5*scale,10),
                "Preview draws the actual backward path and uses the main canvas Y direction",checks);
            end.SelectedIndex=2;
            Check(!result.Text.Contains("1. yol:") && preview.GetType().GetField("path",PrivateInstance)!.GetValue(preview) is null,
                "Changing an endpoint clears the old comparison",checks);
            compare.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            // Full-loop mode must never silently interpret distinct endpoints as a partial path.
            fullLoop.IsChecked=true;
            Check(!result.Text.Contains("1. yol:") && preview.GetType().GetField("path",PrivateInstance)!.GetValue(preview) is null,
                "Changing full-loop mode clears the previous result and preview",checks);
            compare.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(result.Text.Contains("Tam tur için") && !result.Text.Contains("1. yol:") && preview.GetType().GetField("path",PrivateInstance)!.GetValue(preview) is null,
                "Full loop rejects distinct endpoints without a misleading partial measurement",checks);
            end.SelectedIndex=0;
            foreach(var selectedDirection in new[]{0,1})
            {
                direction.SelectedIndex=selectedDirection;
                compare.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                path=(ContourPath)preview.GetType().GetField("path",PrivateInstance)!.GetValue(preview)!;
                Check(result.Text.Contains("1. yol: 30 mm") && path.FullLoop && path.StartIndex==path.EndIndex &&
                    path.Direction==(selectedDirection==0 ? ContourDirection.Forward : ContourDirection.Backward),
                    $"Full loop measures the complete contour in direction {selectedDirection}",checks);
            }
            end.SelectedIndex=4; // Explicit repeated closing vertex aliases vertex zero.
            compare.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            path=(ContourPath)preview.GetType().GetField("path",PrivateInstance)!.GetValue(preview)!;
            Check(result.Text.Contains("1. yol: 30 mm") && path.FullLoop && path.StartIndex==0 && path.EndIndex==4,
                "Full loop accepts the repeated closing vertex as the starting vertex",checks);
            fullLoop.IsChecked=false;
            Check(!result.Text.Contains("1. yol:") && preview.GetType().GetField("path",PrivateInstance)!.GetValue(preview) is null,
                "Turning full-loop mode off clears the previous result and preview",checks);
            compare.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(result.Text.Contains("1. yol: 0 mm") && !otherFullLoop.IsEnabled,
                "Repeated endpoints retain zero-length behavior outside full-loop mode and open paths cannot enable it",checks);
            var root=(FrameworkElement)window.Content;
            root.Measure(new Size(650,500));root.Arrange(new Rect(0,0,650,500));root.UpdateLayout();
            var scroller=Descendants<ScrollViewer>(root).Single(v=>v.IsAncestorOf(fullLoop));
            void ScrollTo(FrameworkElement target)
            {
                var content=(FrameworkElement)scroller.Content;
                var y=target.TransformToAncestor(content).Transform(new Point(0,0)).Y;
                scroller.ScrollToVerticalOffset(Math.Max(0,y-10));root.UpdateLayout();
            }
            ScrollTo(fullLoop);
            Check(scroller.ScrollableHeight>0 && FullyVisible(fullLoop,root) && FullyVisible(otherFullLoop,root),
                $"Small comparison window can scroll to both full-loop controls: scroll={scroller.VerticalOffset}/{scroller.ScrollableHeight}, first={BoundsInRoot(fullLoop,root)} visible={VisibleBoundsInRoot(fullLoop,root)}, second={BoundsInRoot(otherFullLoop,root)} visible={VisibleBoundsInRoot(otherFullLoop,root)}",checks);
            ScrollTo(compare);
            Check(FullyVisible(compare,root),"Small comparison window can scroll to the comparison action",checks);
            source=null;typeof(SeamLengthComparisonWindow).GetMethod("CheckCurrent",PrivateInstance)!.Invoke(window,null);
            Check(!compare.IsEnabled && result.Text.Contains("geçersiz") && preview.GetType().GetField("path",PrivateInstance)!.GetValue(preview) is null,
                "Workshop source change invalidates the result and preview",checks);
        }
        finally { window.Close(); }
        foreach(var scenario in new[]{"geometry","selection","draft"})
        {
            studio.LoadDocument(Fixture());studio.Select(a.Id);source=a.Id;
            var staleWindow=new SeamLengthComparisonWindow(studio,studio,()=>source);
            try
            {
                var button=(Button)typeof(SeamLengthComparisonWindow).GetField("compare",PrivateInstance)!.GetValue(staleWindow)!;
                var text=(TextBlock)typeof(SeamLengthComparisonWindow).GetField("result",PrivateInstance)!.GetValue(staleWindow)!;
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if(scenario=="geometry")studio.Document.Pieces[0].Points[1]=new Point2(11,0);
                else if(scenario=="selection")studio.Select(b.Id);
                else ((TextBox)typeof(MainWindow).GetField("name",PrivateInstance)!.GetValue(studio)!).Text="Bekleyen taslak";
                typeof(SeamLengthComparisonWindow).GetMethod("CheckCurrent",PrivateInstance)!.Invoke(staleWindow,null);
                Check(!button.IsEnabled && text.Text.Contains("geçersiz"),$"Seam comparison rejects stale {scenario}",checks);
            }
            finally { staleWindow.Close(); }
        }
        studio.LoadDocument(Fixture());
    }

    private static void VerifyPointDistance(MainWindow studio,string reportPath,List<string> checks)
    {
        var source=new PatternPiece { Name="Ölçüm",Closed=true,Points=[new(0,0),new(3,4),new(0,0)] };
        studio.LoadDocument(new ProjectDocument { Pieces=[source] });
        source=studio.Document.Pieces[0]; studio.Select(source.Id);
        var before=studio.Document.Clone(); Guid? chosen=source.Id;
        var window=new PointDistanceWindow(studio,studio,source,()=>chosen);
        Render(window,Path.ChangeExtension(reportPath,".point-distance.png"),510,390);
        Check(window.Result.Text.Contains("Düz mesafe: 5 mm") && window.Result.Text.Contains("ΔX: 3 mm") && window.Result.Text.Contains("ΔY: 4 mm"),"Point measurement computes 3-4-5 distance and signed components",checks);
        Check(window.Preview.StartIndex==0 && window.Preview.EndIndex==1 && AutomationProperties.GetHelpText(window.Preview).Contains("A 0") && AutomationProperties.GetHelpText(window.Preview).Contains("B 1"),"Measurement preview identifies selected points accessibly",checks);
        Check(window.ContourResult.Text.Contains("ileri 5 mm") && window.ContourResult.Text.Contains("ters 5 mm"),"Closed contour measurement reports both paths",checks);
        Check(FullyVisible(window.Result,(FrameworkElement)window.Content),"Point measurement result stays fully visible at minimum window size",checks);
        Render(window,Path.ChangeExtension(reportPath,".point-preview.png"),670,720);
        Check(FullyVisible(window.Preview,(FrameworkElement)window.Content),"Measurement contour preview is fully visible in a larger window",checks);
        window.StartSelector.SelectedIndex=1; window.EndSelector.SelectedIndex=0;
        Check(window.Result.Text.Contains("Düz mesafe: 5 mm") && window.Result.Text.Contains("ΔX: -3 mm") && window.Result.Text.Contains("ΔY: -4 mm"),"Point measurement preserves component direction when reversed",checks);
        window.StartSelector.SelectedIndex=0; window.EndSelector.SelectedIndex=2;
        Check(window.Result.Text.Contains("Düz mesafe: 0 mm") && window.EndSelector.SelectedItem!.ToString()!.Contains("kapanış noktası"),"Point measurement handles repeated closing vertex",checks);
        window.EndSelector.SelectedIndex=0;
        Check(window.Result.Text.Contains("Düz mesafe: 0 mm") && DocumentSession.ContentEquals(before,studio.Document),"Measuring one point is zero and leaves the document unchanged",checks);
        chosen=null; window.CheckCurrent();
        Check(window.IsStale && !window.StartSelector.IsEnabled && !window.PickStart.IsEnabled && !window.PickEnd.IsEnabled && window.PickStatus.Text=="" && window.ContourResult.Text=="" && window.Preview.Visibility==Visibility.Collapsed && window.Result.Text.Contains("geçersiz"),"Point measurement invalidates stale source selection",checks);
        window.Close();
        chosen=source.Id;
        var changedWindow=new PointDistanceWindow(studio,studio,source,()=>chosen);
        source.Points[1]=new(6,8); changedWindow.CheckCurrent();
        Check(changedWindow.IsStale && !changedWindow.EndSelector.IsEnabled && changedWindow.Result.Text.Contains("geçersiz"),"Point measurement rejects geometry changed after opening",checks);
        changedWindow.Close();
        var openSource=new PatternPiece { Name="Açık ölçüm",Closed=false,Points=[new(0,0),new(3,0),new(3,4)] };
        studio.LoadDocument(new ProjectDocument { Pieces=[openSource] }); openSource=studio.Document.Pieces[0];studio.Select(openSource.Id); chosen=openSource.Id;
        var openWindow=new PointDistanceWindow(studio,studio,openSource,()=>chosen);
        openWindow.EndSelector.SelectedIndex=2;
        Check(openWindow.Result.Text.Contains("Düz mesafe: 5 mm") && openWindow.ContourResult.Text.Contains("arası: 7 mm"),"Open contour length follows edges rather than straight chord",checks);
        openWindow.StartSelector.SelectedIndex=2;openWindow.EndSelector.SelectedIndex=0;
        Check(openWindow.ContourResult.Text.Contains("arası: 7 mm"),"Open contour measurement works in reverse selection order",checks);
        openWindow.Close();
        var closedSource=new PatternPiece { Name="Kapalı ölçüm",Closed=true,Points=[new(0,0),new(3,0),new(3,4)] };
        studio.LoadDocument(new ProjectDocument { Pieces=[closedSource] });closedSource=studio.Document.Pieces[0];studio.Select(closedSource.Id);chosen=closedSource.Id;
        var closedWindow=new PointDistanceWindow(studio,studio,closedSource,()=>chosen);closedWindow.EndSelector.SelectedIndex=2;
        Check(closedWindow.ContourResult.Text.Contains("ileri 7 mm") && closedWindow.ContourResult.Text.Contains("ters 5 mm")
            && closedWindow.ContourResult.Text.Contains("Toplam kapalı kontur uzunluğu: 12 mm"),"Closed contour measurement distinguishes both directions and reports perimeter",checks);
        closedWindow.EndSelector.SelectedIndex=0;
        Check(closedWindow.ContourResult.Text.Contains("ileri 0 mm") && closedWindow.ContourResult.Text.Contains("ters 0 mm"),"Same contour point produces zero in both directions",checks);
        closedWindow.Close();
        var explicitClosed=new PatternPiece { Name="Tekrarlı kapanış",Closed=true,Points=[new(0,0),new(3,0),new(3,4),new(0,0)] };
        studio.LoadDocument(new ProjectDocument { Pieces=[explicitClosed] });explicitClosed=studio.Document.Pieces[0];studio.Select(explicitClosed.Id);chosen=explicitClosed.Id;
        var explicitWindow=new PointDistanceWindow(studio,studio,explicitClosed,()=>chosen);
        explicitWindow.EndSelector.SelectedIndex=3;
        Check(explicitWindow.ContourResult.Text.Contains("ileri 0 mm") && explicitWindow.ContourResult.Text.Contains("ters 0 mm") && explicitWindow.ContourResult.Text.Contains("uzunluğu: 12 mm"),"Explicit closing vertex measures zero from first vertex while perimeter remains available",checks);
        explicitWindow.StartSelector.SelectedIndex=3;explicitWindow.EndSelector.SelectedIndex=0;
        Check(explicitWindow.ContourResult.Text.Contains("ileri 0 mm") && explicitWindow.ContourResult.Text.Contains("ters 0 mm"),"Explicit closing vertex measures zero in reverse selection order",checks);
        explicitWindow.EndSelector.SelectedIndex=2;
        Check(explicitWindow.ContourResult.Text.Contains("ileri 7 mm") && explicitWindow.ContourResult.Text.Contains("ters 5 mm"),"Closing vertex retains correct paths to other vertices",checks);
        explicitWindow.Close();
        var clickable=new PointMeasurePreview(new PatternPiece { Closed=false,Points=[new(0,0),new(3,0),new(3,4)] });
        clickable.Measure(new Size(400,180));clickable.Arrange(new Rect(0,0,400,180));clickable.UpdateLayout();
        Check(clickable.HitTestPoint(new Point(147.5,20))==0 && clickable.HitTestPoint(new Point(252.5,160))==2 && clickable.HitTestPoint(new Point(0,0))==-1,"Preview picks only a nearby unambiguous contour vertex with increasing Y downwards",checks);
        var ambiguous=new PointMeasurePreview(new PatternPiece { Closed=false,Points=[new(0,0),new(0,0),new(3,4)] });
        ambiguous.Measure(new Size(400,180));ambiguous.Arrange(new Rect(0,0,400,180));ambiguous.UpdateLayout();
        Check(ambiguous.HitTestPoint(new Point(147.5,20))==-2,"Preview rejects coincident vertices instead of guessing",checks);
        var asymmetric=new PointMeasurePreview(new PatternPiece { Closed=false,Points=[new(0,0),new(8,2),new(3,10),new(10,6)] });
        asymmetric.Height=200;asymmetric.Margin=new Thickness(0);
        asymmetric.Measure(new Size(300,200));asymmetric.Arrange(new Rect(0,0,300,200));asymmetric.UpdateLayout();
        Check(asymmetric.HitTestPoint(new Point(70,20))==0 && asymmetric.HitTestPoint(new Point(198,52))==1
            && asymmetric.HitTestPoint(new Point(118,180))==2 && asymmetric.HitTestPoint(new Point(230,116))==3,
            "Asymmetric point preview uses the main canvas Y direction and hit-tests the shown vertices",checks);
        byte[] PreviewPixels(int endIndex)
        {
            var diagonal=new PointMeasurePreview(new PatternPiece { Closed=true,Points=[new(0,0),new(10,0),new(10,10),new(0,10)] }) { Height=200,Margin=new Thickness(0) };
            diagonal.SetSelection(0,endIndex);
            diagonal.Measure(new Size(300,200));diagonal.Arrange(new Rect(0,0,300,200));diagonal.UpdateLayout();
            var bitmap=new RenderTargetBitmap(300,200,96,96,PixelFormats.Pbgra32);bitmap.Render(diagonal);
            var pixels=new byte[300*200*4];bitmap.CopyPixels(pixels,300*4,0);return pixels;
        }
        var beforeLine=PreviewPixels(-1);var afterLine=PreviewPixels(2);
        var changed=0;
        for(var py=88;py<112;py++)for(var px=138;px<162;px++)
        {
            var at=(py*300+px)*4;
            if(beforeLine[at]!=afterLine[at] || beforeLine[at+1]!=afterLine[at+1] || beforeLine[at+2]!=afterLine[at+2])changed++;
        }
        Check(changed>5,"Point measurement preview renders the selected straight line through the contour interior",checks);
        var safeSource=(string)typeof(TextileToolsWindow).GetMethod("SafeSourceName",PrivateInstance|BindingFlags.Static)!.Invoke(null,["Ön"+char.ConvertFromUtf32(0xE0001)+"😀"])!;
        Check(safeSource=="Ön\\uDB40\\uDC01😀","Textile source names escape invisible supplementary format characters",checks);
    }
    private static void VerifyPointPicker(MainWindow studio,string reportPath,List<string> checks)
    {
        var initial=new PatternPiece { Name="Nokta seçimi",Closed=false,Points=[new(0,0),new(8,2),new(3,10),new(10,6)] };
        studio.LoadDocument(new ProjectDocument { Pieces=[initial] });
        var piece=studio.Document.Pieces.Single();studio.Select(piece.Id);
        Guid? currentSource=piece.Id;var before=studio.Document.Clone();
        var session=(DocumentSession)typeof(MainWindow).GetField("session",PrivateInstance)!.GetValue(studio)!;
        var undo=session.UndoCount;var redo=session.RedoCount;var dirty=session.IsDirty;
        var picker=new PointPickerWindow(studio,studio,piece,()=>currentSource,1);
        try
        {
            Render(picker,Path.ChangeExtension(reportPath,".point-picker.png"),670,510);
            Check(picker.PointSelector.SelectedIndex==1 && picker.Preview.StartIndex==1 && picker.UseButton.IsEnabled
                && AutomationProperties.GetName(picker.PointSelector).Contains("indeksi",StringComparison.OrdinalIgnoreCase),
                "Point picker exposes the current index and marks it in the preview",checks);
            picker.PointSelector.SelectedIndex=2;
            Check(picker.Preview.StartIndex==2 && picker.PickStatus.Text.Contains("2 numaralı",StringComparison.Ordinal)
                && DocumentSession.ContentEquals(before,studio.Document) && session.UndoCount==undo && session.RedoCount==redo && session.IsDirty==dirty,
                "Selecting a point updates its marker without changing the project or history",checks);
            currentSource=null;picker.CheckCurrent();
            Check(picker.IsStale && !picker.PointSelector.IsEnabled && !picker.UseButton.IsEnabled
                && picker.Preview.Visibility==Visibility.Collapsed && picker.SelectedPointIndex is null
                && DocumentSession.ContentEquals(before,studio.Document),
                "Point picker disables selection when its source becomes stale",checks);
        }
        finally { picker.Close(); }
    }

    private static void VerifyGradePointPicker(MainWindow studio,string reportPath,List<string> checks)
    {
        var piece=new PatternPiece { Name="Serileme",Closed=false,Points=Enumerable.Range(0,10000).Select(i=>new Point2(i,i%2)).ToList() };
        studio.LoadDocument(new ProjectDocument { Pieces=[piece] });studio.Select(piece.Id);
        var window=new TextileToolsWindow(studio);
        window.WindowStartupLocation=WindowStartupLocation.Manual;window.Left=-32000;window.Top=-32000;window.ShowInTaskbar=false;
        window.Show();
        Render(window,Path.ChangeExtension(reportPath,".grade-point-picker.png"),930,820);
        var grid=(DataGrid)typeof(TextileToolsWindow).GetField("grades",PrivateInstance)!.GetValue(window)!;
        var row=(TextileToolsWindow.GradeRow)grid.Items[3];row.Dx="2,5";row.Dy="-1";
        var button=Descendants<Button>((FrameworkElement)window.Content).Single(b=>Equals(b.Content,"Konturdan nokta seç…"));
        var before=studio.Document.Clone();
        window.GradePickerOpenedForVerification=picker=> { picker.PointSelector.SelectedIndex=9999;picker.UseButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); };
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(grid.SelectedItem is TextileToolsWindow.GradeRow selectedRow && selectedRow.Index==9999 && grid.CurrentCell.Column==grid.Columns[3] && row.Dx=="2,5" && row.Dy=="-1" && DocumentSession.ContentEquals(before,studio.Document),
            "Grade picker maps a large contour index to the matching row without changing drafts or project: "+((TextBlock)typeof(TextileToolsWindow).GetField("result",PrivateInstance)!.GetValue(window)!).Text,checks);
        window.GradePickerOpenedForVerification=picker=>picker.Close();
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(((TextileToolsWindow.GradeRow)grid.SelectedItem).Index==9999 && row.Dx=="2,5", "Cancelling grade point selection preserves row and draft",checks);
        window.GradePickerOpenedForVerification=picker=> { studio.Document.Pieces[0].Name="Değişti";picker.CheckCurrent();picker.UseButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));picker.Close(); };
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(((TextileToolsWindow.GradeRow)grid.SelectedItem).Index==9999 && row.Dx=="2,5", "Stale grade picker cannot change row or draft",checks);
        window.GradePickerOpenedForVerification=null;
        window.Hide();window.Close();
    }
    private static void VerifyMachineChoiceIdentity(MainWindow studio,string work,string reportPath,List<string> checks)
    {
        var first=new PatternPiece { Name="Ön\u202E",Points=[new(0,0),new(10,0),new(0,20)] };
        var second=new PatternPiece { Name="Ön\u202E",Points=[new(0,0),new(30,0),new(0,40)] };
        studio.LoadDocument(new ProjectDocument { Pieces=[first,second] });studio.Select(first.Id);
        var window=new MachineCenterWindow(studio,new SimulationQueue(Path.Combine(work,"machine-choice-identity")));
        Render(window,Path.ChangeExtension(reportPath,".machine-choice-identity.png"),720,540);
        var models=(System.Collections.IList)typeof(MachineCenterWindow).GetField("partChoices",PrivateInstance)!.GetValue(window)!;
        var label=models[0]!.GetType().GetProperty("Label")!;
        var a=(string)label.GetValue(models[0])!;var b=(string)label.GetValue(models[1])!;
        Check(a.Contains(first.Id.ToString("D")) && b.Contains(second.Id.ToString("D")) && a.Contains("10 × 20 mm") && b.Contains("30 × 40 mm")
            && a.Contains("\\u202E") && !a.Contains('\u202E'),"Machine selection identifies duplicate names by source ID and dimensions",checks);
        models[1]!.GetType().GetProperty("IsSelected")!.SetValue(models[1],true);
        Check(window.SelectedPieceIds.ToHashSet().SetEquals(new[] { first.Id,second.Id }),"Machine selection keeps the intended source identities",checks);
        var query=(TextBox)typeof(MachineCenterWindow).GetField("choiceSearch",PrivateInstance)!.GetValue(window)!;
        var searchStatus=(TextBlock)typeof(MachineCenterWindow).GetField("choiceSearchStatus",PrivateInstance)!.GetValue(window)!;
        var list=(ListBox)typeof(MachineCenterWindow).GetField("choiceList",PrivateInstance)!.GetValue(window)!;
        list.ScrollIntoView(models[0]);((FrameworkElement)window.Content).UpdateLayout();
        var container=(DependencyObject)list.ItemContainerGenerator.ContainerFromIndex(0)!;
        var checkBox=Descendants<CheckBox>(container).Single();
        var fullName=(string)models[0]!.GetType().GetProperty("FullDisplayName")!.GetValue(models[0])!;
        Check(fullName.Contains("Ön\\u202E") && AutomationProperties.GetHelpText(checkBox).Contains(fullName)
            && checkBox.ToolTip?.ToString()?.Contains(fullName)==true,"Machine selection exposes the full safe source name",checks);
        query.Text=second.Id.ToString("D");
        Check(list.Items.Count==1 && window.SelectedPieceIds.Count==2 && searchStatus.Text.Contains("Arama dışında 1") && searchStatus.Text.Contains("dosyaya alınır"),
            "Machine search reports the hidden selected source without removing it",checks);
        var footerSelection=(TextBlock)typeof(MachineCenterWindow).GetField("footerSelection",PrivateInstance)!.GetValue(window)!;
        var prepareButton=(Button)typeof(MachineCenterWindow).GetField("prepare",PrivateInstance)!.GetValue(window)!;
        foreach(var (w,h) in new[] { (720,540),(1024,640) })
        {
            Render(window,Path.ChangeExtension(reportPath,$".machine-hidden-selection-{w}.png"),w,h);
            Check(footerSelection.Text.Contains("Seçili: 2") && footerSelection.Text.Contains("Arama dışında: 1")
                && footerSelection.Text.Contains("dosyaya dahil") && FullyVisible(footerSelection,(FrameworkElement)window.Content)
                && FullyVisible(prepareButton,(FrameworkElement)window.Content),
                $"Machine footer discloses hidden selections at {w}x{h}",checks);
        }
        query.Text="eşleşmeyen-örnek";
        Check(list.Items.Count==0 && window.SelectedPieceIds.Count==2 && searchStatus.Text.Contains("Eşleşen parça yok"),
            "No machine search results retain selected sources",checks);
        var allButtons=Descendants<Button>((FrameworkElement)window.Content).ToArray();
        allButtons.Single(b=>Equals(b.Content,"Tüm seçimleri temizle")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(window.SelectedPieceIds.Count==0,"Machine bulk clear includes filtered-out sources",checks);
        allButtons.Single(b=>Equals(b.Content,"Tüm parçaları seç")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(window.SelectedPieceIds.Count==2,"Machine bulk selection includes filtered-out sources",checks);
        var setPreparing=typeof(MachineCenterWindow).GetMethod("SetPreparing",PrivateInstance)!;
        setPreparing.Invoke(window,[true]);Check(!query.IsEnabled,"Machine search is disabled while preparing",checks);
        setPreparing.Invoke(window,[false]);query.Clear();
        Check(query.IsEnabled && list.Items.Count==2 && window.SelectedPieceIds.Count==2,"Clearing machine search restores the list without losing selections",checks);
    }
    private static void VerifyPieceSearch(MainWindow studio,List<string> checks)
    {
        var all=Enumerable.Range(0,10000).Select(i=>new PatternPiece { Name=i==0 ? "ÖN DENEME" : i==2 ? "Ön\nparça" : i==3 ? "" : $"Parça {i}",Closed=false,Points=[new(0,0),new(10,0)] }).ToList();
        studio.LoadDocument(new ProjectDocument { Pieces=all });studio.Select(all[0].Id);
        var search=(TextBox)typeof(MainWindow).GetField("pieceSearch",PrivateInstance)!.GetValue(studio)!;
        var status=(TextBlock)typeof(MainWindow).GetField("pieceSearchStatus",PrivateInstance)!.GetValue(studio)!;
        var list=(ListBox)typeof(MainWindow).GetField("pieces",PrivateInstance)!.GetValue(studio)!;
        var name=(TextBox)typeof(MainWindow).GetField("name",PrivateInstance)!.GetValue(studio)!;
        var session=(DocumentSession)typeof(MainWindow).GetField("session",PrivateInstance)!.GetValue(studio)!;
        name.Text="Uygulanmamış taslak";var before=studio.Document.Clone();var undo=session.UndoCount;var redo=session.RedoCount;
        search.Text="ön deneme";
        Check(list.Items.Count==1 && studio.SelectedId==all[0].Id && name.Text=="Uygulanmamış taslak", $"Name search finds Turkish text without changing selection or draft (count={list.Items.Count}, selected={studio.SelectedId==all[0].Id}, draft={name.Text})",checks);
        search.Text="Ön\\u000Aparça";
        Check(list.Items.Count==1 && ((PatternPiece)list.Items[0]).Id==all[2].Id && studio.SelectedId==all[0].Id && name.Text=="Uygulanmamış taslak",
            "Searching the visible escaped piece name preserves selection and draft",checks);
        search.Text="(boş)";
        Check(list.Items.Count==1 && ((PatternPiece)list.Items[0]).Id==all[3].Id,
            "Searching the displayed empty-name label finds the unnamed piece",checks);
        search.Text=all[1].Id.ToString("D");
        Check(list.Items.Count==1 && studio.SelectedId==all[0].Id && status.Text.Contains("arama dışında") && name.Text=="Uygulanmamış taslak",
            "ID search retains a selected piece hidden by the filter",checks);
        search.Text="bulunmayan-ad-xyz";
        Check(list.Items.Count==0 && status.Text.Contains("Eşleşen parça yok") && DocumentSession.ContentEquals(before,studio.Document)
            && studio.SelectedId==all[0].Id && session.UndoCount==undo && session.RedoCount==redo,
            "Zero search results preserve document, selection and history",checks);
        Check(AutomationProperties.GetLiveSetting(status)==AutomationLiveSetting.Polite
            && AutomationProperties.GetName(status).Contains("Eşleşen parça yok") && AutomationProperties.GetName(status).Contains("arama dışında"),
            "Search result and hidden selection are exposed through accessibility properties",checks);
        search.Clear();
        Check(list.Items.Count==10000 && (list.SelectedItem as PatternPiece)?.Id==all[0].Id && name.Text=="Uygulanmamış taslak",
            "Clearing search restores the selected item in a 10000-piece list",checks);
        search.Text=all[1].Id.ToString("D");list.SelectedItem=list.Items[0];
        Check(studio.SelectedId==all[1].Id,"Selecting a filtered result explicitly changes the active piece",checks);
        search.Clear();studio.Select(all[0].Id);
        Check(name.Text=="Uygulanmamış taslak" && DocumentSession.ContentEquals(before,studio.Document),
            "Switching filtered results preserves the first piece's unapplied draft",checks);
        search.Text=all[1].Id.ToString("D");studio.LoadDocument(new ProjectDocument { Pieces=all.Take(2).Select(p=>p.Clone()).ToList() });
        Check(list.Items.Count==1 && ((PatternPiece)list.Items[0]).Id==all[1].Id,
            "Active search is reapplied when another document is loaded",checks);
        var scrolledToSearch=false;
        RequestBringIntoViewEventHandler onRequest=(_,_)=>scrolledToSearch=true;
        search.RequestBringIntoView+=onRequest;
        try
        {
            var beforeFocus=studio.Document.Clone();var previousQuery=search.Text;
            studio.FocusPieceSearch();
            Check(scrolledToSearch && search.Text==previousQuery && DocumentSession.ContentEquals(beforeFocus,studio.Document),
                "Piece search command reveals the search box without modifying the project",checks);
        }
        finally { search.RequestBringIntoView-=onRequest; }
        search.Clear();
    }
    private static void VerifyLayeredPointerSelection(MainWindow studio,List<string> checks)
    {
        var bottom=new PatternPiece { Name="Alt",Points=[new(0,0),new(100,0),new(0,100)] };
        var top=new PatternPiece { Name="Üst çizgi",Closed=false,Points=[new(-20,3),new(20,3)] };
        studio.LoadDocument(new ProjectDocument { Pieces=[bottom,top] });studio.Select(bottom.Id);
        var canvas=(CadCanvas)typeof(MainWindow).GetField("canvas",PrivateInstance)!.GetValue(studio)!;
        var scale=typeof(CadCanvas).GetField("scale",PrivateInstance)!;var oldScale=scale.GetValue(canvas);scale.SetValue(canvas,1d);
        try
        {
            var visibleHandle=canvas.ResolvePointerSelection(new Point2(0,0));
            Check(visibleHandle.Piece?.Id==bottom.Id && visibleHandle.Vertex==0 && !visibleHandle.Ambiguous,
                "A nearby upper line does not steal a visible selected handle",checks);
            studio.Commit("Üst çizgiyi yaklaştır",()=>studio.Document.Pieces[1].Points=[new(-20,1),new(20,1)]);
            var coveredHandle=canvas.ResolvePointerSelection(new Point2(0,0));
            Check(coveredHandle.Piece?.Id==top.Id && coveredHandle.Vertex==-1 && !coveredHandle.Ambiguous,
                "An upper stroke covering the selected handle receives the pointer",checks);
            studio.Commit("Üst parçayı kapat",()=> { studio.Document.Pieces[1].Closed=true;studio.Document.Pieces[1].Points=[new(-5,-5),new(5,-5),new(5,5),new(-5,5)]; });
            var coveredFill=canvas.ResolvePointerSelection(new Point2(0,0));
            Check(coveredFill.Piece?.Id==top.Id && coveredFill.Vertex==-1 && !coveredFill.Ambiguous,
                "An upper opaque fill receives the pointer instead of a hidden handle",checks);
            var nearHidden=canvas.ResolvePointerSelection(new Point2(7,0));
            Check(nearHidden.Piece is null && nearHidden.Vertex==-1 && nearHidden.Ambiguous,
                "Picking near a hidden handle does not edit the obscured vertex",checks);
            foreach(var zoom in new[] { .02,30d })
            {
                var lower=new PatternPiece { Points=[new(0,0),new(100/zoom,0),new(0,100/zoom)] };
                var upper=new PatternPiece { Points=[new(-5/zoom,-5/zoom),new(5/zoom,-5/zoom),new(5/zoom,5/zoom),new(-5/zoom,5/zoom)] };
                studio.LoadDocument(new ProjectDocument { Pieces=[lower,upper] });studio.Select(lower.Id);scale.SetValue(canvas,zoom);
                var covered=canvas.ResolvePointerSelection(new Point2(7/zoom,0));
                Check(covered.Piece is null && covered.Ambiguous,$"Hidden handle protection works at zoom {zoom}",checks);
            }
        }
        finally { scale.SetValue(canvas,oldScale); }
    }
    private static void VerifyGradeApplyConflicts(MainWindow studio,List<string> checks)
    {
        var piece=new PatternPiece { Name="Artış çakışması",Points=[new(0,0),new(10,0),new(0,10)] };
        studio.LoadDocument(new ProjectDocument { Pieces=[piece] });studio.Select(piece.Id);
        var tools=new TextileToolsWindow(studio);var root=(FrameworkElement)tools.Content;
        root.Measure(new Size(1024,640));root.Arrange(new Rect(0,0,1024,640));root.UpdateLayout();
        var table=(DataGrid)typeof(TextileToolsWindow).GetField("grades",PrivateInstance)!.GetValue(tools)!;
        table.Items.Cast<TextileToolsWindow.GradeRow>().First().Dx="7";
        studio.Commit("Geometri değişti",()=>studio.Document.Pieces[0].Points[1]=new Point2(12,0));
        var changed=studio.Document.Clone();
        var apply=Descendants<Button>(root).Single(b=>Equals(b.Content,"Artışları projeye uygula"));
        apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var status=(TextBlock)typeof(TextileToolsWindow).GetField("result",PrivateInstance)!.GetValue(tools)!;
        Check(status.Text.Contains("uygulanmadı") && table.Items.Cast<TextileToolsWindow.GradeRow>().First().Dx=="7"
            && DocumentSession.ContentEquals(changed,studio.Document),"Stale geometry cannot be used to apply grading and draft stays intact",checks);

        var savedPiece=new PatternPiece { Name="Kural çakışması",Points=[new(0,0),new(10,0),new(0,10)] };
        var rule=new GradingRuleSet { SourcePieceId=savedPiece.Id,SourceClosed=true,SourcePoints=[..savedPiece.Points],Deltas=[new(1,2),new(0,0),new(0,0)] };
        studio.LoadDocument(new ProjectDocument { Pieces=[savedPiece],GradingRules=[rule] });studio.Select(savedPiece.Id);
        var conflictTools=new TextileToolsWindow(studio);var conflictRoot=(FrameworkElement)conflictTools.Content;
        conflictRoot.Measure(new Size(1024,640));conflictRoot.Arrange(new Rect(0,0,1024,640));conflictRoot.UpdateLayout();
        var conflictTable=(DataGrid)typeof(TextileToolsWindow).GetField("grades",PrivateInstance)!.GetValue(conflictTools)!;
        conflictTable.Items.Cast<TextileToolsWindow.GradeRow>().First().Dx="7";
        studio.Commit("Kural değişti",()=>studio.Document.GradingRules[0].Deltas[0]=new Point2(3,4));
        var conflictBefore=studio.Document.Clone();
        var conflictApply=Descendants<Button>(conflictRoot).Single(b=>Equals(b.Content,"Artışları projeye uygula"));
        conflictApply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        conflictApply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var conflictStatus=(TextBlock)typeof(TextileToolsWindow).GetField("result",PrivateInstance)!.GetValue(conflictTools)!;
        Check(conflictStatus.Text.Contains("üzerine yazılmadı") && conflictTable.Items.Cast<TextileToolsWindow.GradeRow>().First().Dx=="7"
            && DocumentSession.ContentEquals(conflictBefore,studio.Document),"Changed saved rule is not overwritten after rebinding source",checks);
        var create=Descendants<Button>(conflictRoot).Single(b=>Equals(b.Content,"Yeni bedeni projeye ekle"));
        ClickAndWaitForGrade(conflictTools,create);
        Check(conflictStatus.Text.Contains("kural") && conflictTable.Items.Cast<TextileToolsWindow.GradeRow>().First().Dx=="7"
            && DocumentSession.ContentEquals(conflictBefore,studio.Document),"Changed saved rule cannot be used to generate a size from stale draft increments",checks);
    }
    private static void VerifyGradeInputFeedback(MainWindow studio,List<string> checks)
    {
        var piece=new PatternPiece { Name="Artış denetimi",Points=[new(0,0),new(10,0),new(0,10)] };
        studio.LoadDocument(new ProjectDocument { Pieces=[piece] });studio.Select(piece.Id);
        var tools=new TextileToolsWindow(studio);var root=(FrameworkElement)tools.Content;
        root.Measure(new Size(1024,640));root.Arrange(new Rect(0,0,1024,640));root.UpdateLayout();
        var table=(DataGrid)typeof(TextileToolsWindow).GetField("grades",PrivateInstance)!.GetValue(tools)!;
        var rows=table.Items.Cast<TextileToolsWindow.GradeRow>().ToArray();
        var result=(TextBlock)typeof(TextileToolsWindow).GetField("result",PrivateInstance)!.GetValue(tools)!;
        var create=Descendants<Button>(root).Single(b=>Equals(b.Content,"Yeni bedeni projeye ekle"));
        var size=Descendants<TextBox>(root).Single(t=>AutomationProperties.GetName(t)=="Yeni beden adı");
        var steps=Descendants<TextBox>(root).Single(t=>AutomationProperties.GetName(t)=="Temel bedenden adım sayısı");
        var sizeHint=((Panel)size.Parent).Children.OfType<TextBlock>().Single(t=>AutomationProperties.GetLiveSetting(t)==AutomationLiveSetting.Polite);
        var stepsHint=((Panel)steps.Parent).Children.OfType<TextBlock>().Single(t=>AutomationProperties.GetLiveSetting(t)==AutomationLiveSetting.Polite);
        size.Text="  ";Check(!create.IsEnabled && sizeHint.Visibility==Visibility.Visible,"Empty size name explains why creation is unavailable",checks);
        size.Text="L";steps.Text="abc";Check(!create.IsEnabled && stepsHint.Visibility==Visibility.Visible,"Invalid grading step explains why creation is unavailable",checks);
        steps.Text="-0,5";Check(create.IsEnabled && sizeHint.Visibility==Visibility.Collapsed && stepsHint.Visibility==Visibility.Collapsed,"Negative fractional grading steps remain supported",checks);
        steps.Text="1";
        var before=studio.Document.Clone();
        rows[2].Dx="abc";
        Descendants<Button>(root).Single(b=>Equals(b.Content,"Artışları projeye uygula")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(result.Text.Contains("Nokta 2, ΔX") && rows[2].Dx=="abc" && DocumentSession.ContentEquals(before,studio.Document),
            "Applying invalid increments identifies the point and X column without changing the project",checks);
        rows[2].Dx="0";rows[1].Dy="abc";
        ClickAndWaitForGrade(tools,create);
        Check(result.Text.Contains("Nokta 1, ΔY") && rows[1].Dy=="abc" && DocumentSession.ContentEquals(before,studio.Document),
            "Creating a size identifies the point and Y column without changing the project",checks);
    }
    private static void VerifyOffsetSourceContext(MainWindow studio,List<string> checks)
    {
        var previousContext=SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(studio.Dispatcher));
        var first=new PatternPiece { Name="A\u202EB",Points=[new(0,0),new(100,0),new(100,100),new(52,100),new(52,20),new(48,20),new(48,100),new(0,100)] };
        var second=first.Clone();second.Id=Guid.NewGuid();
        studio.LoadDocument(new ProjectDocument { Pieces=[first,second] });studio.Select(first.Id);
        var session=(DocumentSession)typeof(MainWindow).GetField("session",PrivateInstance)!.GetValue(studio)!;
        var before=studio.Document.Clone();var undo=session.UndoCount;var redo=session.RedoCount;
        var tools=new TextileToolsWindow(studio);
        try
        {
            var selector=(ComboBox)typeof(TextileToolsWindow).GetField("source",PrivateInstance)!.GetValue(tools)!;
            var result=(TextBlock)typeof(TextileToolsWindow).GetField("result",PrivateInstance)!.GetValue(tools)!;
            var run=typeof(TextileToolsWindow).GetMethod("CreateOffsetAsync",PrivateInstance)!;
            var failed=(Task)run.Invoke(tools,["5"])!;PumpUntil(()=>failed.IsCompleted);failed.GetAwaiter().GetResult();
            Check(result.Text.Contains(first.Id.ToString("D"),StringComparison.Ordinal) && result.Text.Contains("A\\u202EB",StringComparison.Ordinal)
                && !result.Text.Contains('\u202E') && result.Text.Contains("5 mm",StringComparison.Ordinal)
                && DocumentSession.ContentEquals(before,studio.Document) && session.UndoCount==undo && session.RedoCount==redo,
                "Rejected offset identifies the captured source and requested amount without changing document or history",checks);
            var message=result.Text;
            selector.SelectedItem=selector.Items.Cast<PatternPiece>().Single(p=>p.Id==second.Id);
            Check(result.Text==message && result.Text.Contains(first.Id.ToString("D"),StringComparison.Ordinal)
                && !result.Text.Contains(second.Id.ToString("D"),StringComparison.Ordinal),
                "Changing selection keeps the rejected offset tied to its original source",checks);
            using var reached=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
            tools.BeforeOffsetForVerification=()=> { reached.Set();release.Wait(); };
            var cancelled=(Task)run.Invoke(tools,["2"])!;
            try
            {
                PumpUntil(()=>reached.IsSet);
                var cancel=(Button)typeof(TextileToolsWindow).GetField("cancelOffset",PrivateInstance)!.GetValue(tools)!;
                Check(result.Text.Contains(second.Id.ToString("D"),StringComparison.Ordinal) && result.Text.Contains("2 mm",StringComparison.Ordinal),
                    "Running offset identifies the current source and requested amount",checks);
                cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));release.Set();PumpUntil(()=>cancelled.IsCompleted);cancelled.GetAwaiter().GetResult();
                Check(result.Text.Contains("iptal",StringComparison.OrdinalIgnoreCase) && result.Text.Contains(second.Id.ToString("D"),StringComparison.Ordinal)
                    && result.Text.Contains("2 mm",StringComparison.Ordinal) && DocumentSession.ContentEquals(before,studio.Document)
                    && session.UndoCount==undo && session.RedoCount==redo,
                    "Cancelled offset retains its source context without changing document or history",checks);
            }
            finally { release.Set();PumpUntil(()=>cancelled.IsCompleted);tools.BeforeOffsetForVerification=null; }
            Check(AutomationProperties.GetLiveSetting(result)==AutomationLiveSetting.Polite && !tools.IsVisible,
                "Offset result uses an accessible live region and verification remains hidden",checks);
        }
        finally { tools.Close();SynchronizationContext.SetSynchronizationContext(previousContext); }
    }
    private static void VerifyOffsetUnverifiedDetailGuidance(MainWindow studio,List<string> checks)
    {
        var previousContext=SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(studio.Dispatcher));
        var lobed=new PatternPiece { Name="Loblu örnek", Points=Enumerable.Range(0,512).Select(i=>
        {
            var angle=Math.Tau*i/512;var radius=1000+200*Math.Cos(3*angle);
            return new Point2(radius*Math.Cos(angle),radius*Math.Sin(angle));
        }).ToList() };
        var narrow=new PatternPiece { Name="Dar girinti",Points=[new(0,0),new(100,0),new(100,100),new(52,100),new(52,20),new(48,20),new(48,100),new(0,100)] };
        studio.LoadDocument(new ProjectDocument { Pieces=[lobed,narrow] });studio.Select(lobed.Id);
        var session=(DocumentSession)typeof(MainWindow).GetField("session",PrivateInstance)!.GetValue(studio)!;
        var before=studio.Document.Clone();var undo=session.UndoCount;var redo=session.RedoCount;
        var tools=new TextileToolsWindow(studio);
        try
        {
            var result=(TextBlock)typeof(TextileToolsWindow).GetField("result",PrivateInstance)!.GetValue(tools)!;
            var source=(ComboBox)typeof(TextileToolsWindow).GetField("source",PrivateInstance)!.GetValue(tools)!;
            var run=typeof(TextileToolsWindow).GetMethod("CreateOffsetAsync",PrivateInstance)!;
            var task=(Task)run.Invoke(tools,["1"])!;PumpUntil(()=>task.IsCompleted);task.GetAwaiter().GetResult();
            Check(result.Text.Contains("yerel kontur ayrıntısının korunduğu doğrulanamadı",StringComparison.Ordinal)
                && result.Text.Contains("kesin hatalı olduğu anlamına gelmez",StringComparison.Ordinal)
                && result.Text.Contains("kaynak çizimle karşılaştırın",StringComparison.Ordinal)
                && result.Text.Contains("üretim sorumlusuna danışın",StringComparison.Ordinal)
                && result.Text.Contains(lobed.Id.ToString("D"),StringComparison.Ordinal)
                && result.Text.Contains("1 mm",StringComparison.Ordinal)
                && DocumentSession.ContentEquals(before,studio.Document) && session.UndoCount==undo && session.RedoCount==redo,
                "An unverified local offset detail reports its source and measured next steps without claiming the contour is invalid",checks);
            var previewButton=(Button)typeof(TextileToolsWindow).GetField("unverifiedOffsetPreview",PrivateInstance)!.GetValue(tools)!;
            Check(previewButton.Visibility==Visibility.Visible && previewButton.IsEnabled &&
                AutomationProperties.GetHelpText(previewButton).Contains("dışa aktarılmaz",StringComparison.Ordinal),
                "Unverified offset offers an accessible display-only preview",checks);
            new System.Windows.Interop.WindowInteropHelper(tools).EnsureHandle();
            var preview=new OffsetUnverifiedPreviewWindow(tools,lobed.Points,lobed.Points);
            try
            {
                var panel=(DockPanel)preview.Content;
                var warning=panel.Children.OfType<TextBlock>().Single();
                var buttons=panel.Children.OfType<Button>().ToArray();
                var drawing=panel.Children.OfType<FrameworkElement>().Single(c=>c is not TextBlock && c is not Button);
                var top=OffsetUnverifiedPreviewWindow.MapPoint(new Point2(0,0),0,0,1,100,100,0,0);
                var bottom=OffsetUnverifiedPreviewWindow.MapPoint(new Point2(0,10),0,0,1,100,100,0,0);
                Check(bottom.Y>top.Y,"Offset preview uses the same Y direction as the main canvas",checks);
                Check(!preview.IsVisible && warning.Focusable && warning.Text.Contains("DOĞRULANMAMIŞ",StringComparison.Ordinal) &&
                    warning.Text.Contains("çentik korunumu kanıtı değildir",StringComparison.Ordinal) &&
                    AutomationProperties.GetName(warning)==warning.Text && drawing.Focusable &&
                    AutomationProperties.GetHelpText(drawing).Contains("ok tuşlarıyla",StringComparison.Ordinal) &&
                    buttons.Length==1 && Equals(buttons[0].Content,"Kapat") && buttons[0].IsCancel &&
                    DocumentSession.ContentEquals(before,studio.Document) && session.UndoCount==undo && session.RedoCount==redo,
                    "Preview declares its limitations, has keyboard guidance and no save or export action",checks);
            }
            finally { preview.Close(); }
            source.SelectedItem=source.Items.Cast<PatternPiece>().Single(p=>p.Id==narrow.Id);
            previewButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(previewButton.Visibility==Visibility.Collapsed && result.Text.Contains("eski önizleme gösterilmedi",StringComparison.Ordinal) &&
                DocumentSession.ContentEquals(before,studio.Document) && session.UndoCount==undo && session.RedoCount==redo,
                "Changed selection rejects the stored preview without changing or saving the document",checks);
            task=(Task)run.Invoke(tools,["0.0000001"])!;PumpUntil(()=>task.IsCompleted);task.GetAwaiter().GetResult();
            Check(result.Text.Contains("Dikiş payı uygulanmadı",StringComparison.Ordinal)
                && result.Text.Contains("Dikiş payı en az 0,000001 mm olmalıdır",StringComparison.Ordinal)
                && !result.Text.Contains("kesin hatalı olduğu anlamına gelmez",StringComparison.Ordinal)
                && result.Text.Contains(narrow.Id.ToString("D"),StringComparison.Ordinal)
                && result.Text.Contains(MeasurementInput.Format(0.0000001)+" mm",StringComparison.Ordinal)
                && DocumentSession.ContentEquals(before,studio.Document) && session.UndoCount==undo && session.RedoCount==redo,
                "An unrelated offset rejection does not receive the local-detail-specific guidance",checks);
        }
        finally { tools.Close();SynchronizationContext.SetSynchronizationContext(previousContext); }
    }
    private static void VerifyOffsetSourceHint(MainWindow studio,List<string> checks)
    {
        var closed=new PatternPiece { Name="Kapalı",Points=[new(0,0),new(10,0),new(0,10)] };
        var open=new PatternPiece { Name="Açık",Closed=false,Points=[new(20,0),new(30,0)] };
        studio.LoadDocument(new ProjectDocument { Pieces=[closed,open] });studio.Select(closed.Id);
        var tools=new TextileToolsWindow(studio);
        var input=(TextBox)typeof(TextileToolsWindow).GetField("offsetAllowance",PrivateInstance)!.GetValue(tools)!;
        var panel=(Panel)input.Parent;
        var hint=panel.Children.Cast<UIElement>().SkipWhile(child=>!ReferenceEquals(child,input)).Skip(1).OfType<TextBlock>().First(t=>AutomationProperties.GetLiveSetting(t)==AutomationLiveSetting.Polite);
        var button=panel.Children.OfType<Button>().Single(b=>Equals(b.Content,"Dikiş payını ayrı parça oluştur"));
        var source=(ComboBox)typeof(TextileToolsWindow).GetField("source",PrivateInstance)!.GetValue(tools)!;
        source.SelectedItem=source.Items.Cast<PatternPiece>().Single(p=>p.Id==open.Id);
        Check(!button.IsEnabled && hint.Text.Contains("kapalı bir parça"),"Open source explains why allowance is unavailable",checks);
        input.Text="abc";
        Check(hint.Text.Contains("0,000001 ile 100000"),"Invalid amount takes priority over the open-source explanation",checks);
        input.Text="10";source.SelectedItem=null;
        Check(!button.IsEnabled && hint.Text.Contains("kaynak parça seçin"),"Missing source explains why allowance is unavailable",checks);
        source.SelectedItem=source.Items.Cast<PatternPiece>().Single(p=>p.Id==closed.Id);
        Check(button.IsEnabled && hint.Visibility==Visibility.Collapsed,"Choosing a closed source clears the allowance explanation",checks);
    }
    private static void VerifyGradingGeometryNotice(MainWindow studio,string reportPath,List<string> checks)
    {
        var piece=new PatternPiece { Name="Serileme durumu",Points=[new(0,0),new(10,0),new(0,10)] };
        var rule=new GradingRuleSet { SourcePieceId=piece.Id,SourceClosed=true,SourcePoints=[..piece.Points],Deltas=[new(1,2),new(0,0),new(0,0)] };
        studio.LoadDocument(new ProjectDocument { Pieces=[piece],GradingRules=[rule] });studio.Select(piece.Id);
        var notice=(TextBlock)typeof(MainWindow).GetField("gradingGeometryNotice",PrivateInstance)!.GetValue(studio)!;
        Check(notice.Visibility==Visibility.Collapsed,"Matching grading geometry has no stale-rule warning",checks);
        studio.Commit("Taşı",()=>studio.Document.Pieces[0].Points=studio.Document.Pieces[0].Points.Select(p=>new Point2(p.X+5,p.Y+7)).ToList());
        Check(notice.Visibility==Visibility.Collapsed,"Translating grading source does not trigger stale-rule warning",checks);
        studio.Commit("Şekli değiştir",()=>studio.Document.Pieces[0].Points[1]=new Point2(18,7));
        Check(notice.Visibility==Visibility.Visible && notice.Text.Contains("eski geometri") && studio.Document.GradingRules.Count==1,
            "Shape changes reveal the stale-rule warning without deleting the rule",checks);
        Render(studio,Path.ChangeExtension(reportPath,".stale-grading-compact.png"),1024,640);
        Check(FullyVisible(notice,(FrameworkElement)studio.Content) && AutomationProperties.GetLiveSetting(notice)==AutomationLiveSetting.Polite
            && AutomationProperties.GetName(notice).Contains("eski geometri"),"Stale grading warning is visible and named in the compact inspector",checks);
        studio.Select(null);
        Check(notice.Visibility==Visibility.Collapsed,"No selection hides the grading warning",checks);
        studio.Select(piece.Id);
        studio.Commit("Açık çizgi",()=> { studio.Document.Pieces[0].Points[1]=new Point2(15,7);studio.Document.Pieces[0].Closed=false; });
        Check(notice.Visibility==Visibility.Visible,"A closed-state change keeps the stale-rule warning visible",checks);
    }
    private static void VerifyGradeResetConfirmation(MainWindow studio,List<string> checks)
    {
        var piece=new PatternPiece { Name="Taslak sınaması",Points=[new(0,0),new(10,0),new(0,10)] };
        studio.LoadDocument(new ProjectDocument { Pieces=[piece] });studio.Select(piece.Id);
        var tools=new TextileToolsWindow(studio);
        var table=(DataGrid)typeof(TextileToolsWindow).GetField("grades",PrivateInstance)!.GetValue(tools)!;
        table.Items.Cast<TextileToolsWindow.GradeRow>().First().Dx="2";
        studio.Commit("Geometri düzenlendi",()=>studio.Document.Pieces[0].Points[1]=new Point2(12,0));
        typeof(TextileToolsWindow).GetMethod("RefreshSources",PrivateInstance)!.Invoke(tools,[piece.Id,true]);
        var reset=(Button)typeof(TextileToolsWindow).GetField("resetRulesButton",PrivateInstance)!.GetValue(tools)!;
        var asked=0;tools.ConfirmGradeResetForVerification=()=>{asked++;return false;};
        reset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(asked==1 && table.Items.Cast<TextileToolsWindow.GradeRow>().First().Dx=="2" && studio.Document.GradingRules.Count==0,
            "Declining grading reset preserves unsaved increments",checks);
        tools.ConfirmGradeResetForVerification=()=>{asked++;return true;};
        reset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(asked==2 && table.Items.Cast<TextileToolsWindow.GradeRow>().First().Dx=="0" && studio.Document.GradingRules.Count==0,
            "Confirming grading reset clears unsaved increments",checks);
        var savedPiece=new PatternPiece { Name="Kayıtlı artış",Points=[new(0,0),new(10,0),new(0,10)] };
        var savedRule=new GradingRuleSet { SourcePieceId=savedPiece.Id,SourceClosed=true,SourcePoints=[..savedPiece.Points],Deltas=[new(1,2),new(0,0),new(0,0)] };
        studio.LoadDocument(new ProjectDocument { Pieces=[savedPiece],GradingRules=[savedRule] });studio.Select(savedPiece.Id);
        var savedTools=new TextileToolsWindow(studio);
        var savedTable=(DataGrid)typeof(TextileToolsWindow).GetField("grades",PrivateInstance)!.GetValue(savedTools)!;
        savedTable.Items.Cast<TextileToolsWindow.GradeRow>().First().Dx="geçersiz";
        studio.Commit("Geometri düzenlendi",()=>studio.Document.Pieces[0].Points[1]=new Point2(12,0));
        typeof(TextileToolsWindow).GetMethod("RefreshSources",PrivateInstance)!.Invoke(savedTools,[savedPiece.Id,true]);
        var savedBefore=studio.Document.GradingRules[0].Deltas.ToArray();
        var savedReset=(Button)typeof(TextileToolsWindow).GetField("resetRulesButton",PrivateInstance)!.GetValue(savedTools)!;
        var savedAsked=0;savedTools.ConfirmGradeResetForVerification=()=>{savedAsked++;return false;};
        savedReset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(savedAsked==1 && savedTable.Items.Cast<TextileToolsWindow.GradeRow>().First().Dx=="geçersiz" && studio.Document.GradingRules[0].Deltas.SequenceEqual(savedBefore),
            "Declining grading reset preserves invalid draft input and saved grading increments",checks);
        savedTools.ConfirmGradeResetForVerification=()=>{savedAsked++;savedTable.Items.Cast<TextileToolsWindow.GradeRow>().First().Dx="3";return true;};
        savedReset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(savedAsked==2 && savedTable.Items.Cast<TextileToolsWindow.GradeRow>().First().Dx=="3" && studio.Document.GradingRules[0].Deltas.SequenceEqual(savedBefore),
            "A draft change while confirming prevents grading reset",checks);
        savedTools.ConfirmGradeResetForVerification=()=>{savedAsked++;return true;};
        savedReset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(savedAsked==3 && savedTable.Items.Cast<TextileToolsWindow.GradeRow>().First().Dx=="0" && studio.Document.GradingRules[0].Deltas.SequenceEqual(savedBefore),
            "Confirming grading reset does not alter saved grading increments",checks);
    }
    private static void VerifyDuplicateGrading(MainWindow studio,List<string> checks)
    {
        var source=new PatternPiece { Name="Kaynak",Points=[new(0,0),new(10,0),new(0,10)] };
        var rule=new GradingRuleSet { SourcePieceId=source.Id,SourceClosed=true,SourcePoints=[..source.Points],Deltas=[new(1,1),new(2,2),new(3,3)] };
        var initial=new ProjectDocument { Pieces=[source],GradingRules=[rule] };
        var session=(DocumentSession)typeof(MainWindow).GetField("session",PrivateInstance)!.GetValue(studio)!;
        try
        {
            var dialog=new DuplicatePieceWindow(studio,source,"Kaynak",rule);
            Check(dialog.CopyGrading.IsEnabled && !dialog.Options.CopyGrading,"Grading copy requires explicit selection",checks);dialog.Close();
            foreach(var copyRule in new[] { false,true })
            {
                studio.LoadDocument(initial);studio.Select(source.Id);var before=studio.Document.Clone();
                studio.ChooseDuplicateForVerification=_=>new DuplicateOptions(false,false,copyRule);
                studio.DuplicateSelectedPiece();
                var copy=studio.Document.Pieces.Single(p=>p.Id!=source.Id);
                var copied=studio.Document.GradingRules.SingleOrDefault(r=>r.SourcePieceId==copy.Id);
                Check((copied is not null)==copyRule && studio.Document.GradingRules.Count==(copyRule?2:1) &&
                    (!copyRule || (copied!.SourcePoints.SequenceEqual(copy.Points) && copied.Deltas.SequenceEqual(rule.Deltas) &&
                    !ReferenceEquals(copied.SourcePoints,studio.Document.GradingRules[0].SourcePoints) && !ReferenceEquals(copied.Deltas,studio.Document.GradingRules[0].Deltas))) &&
                    JsonSerializer.Serialize(studio.Document.GradingRules[0])==JsonSerializer.Serialize(before.GradingRules[0]) && session.UndoCount==1,
                    $"Duplicate grading option {copyRule} preserves source and binds an independent rule to the new identity",checks);
                Check(session.Undo() && DocumentSession.ContentEquals(before,studio.Document),"Duplicate and copied grading undo together",checks);
            }
            var stale=initial.Clone();stale.Pieces[0].Points[0]=new Point2(-1,0);
            studio.LoadDocument(stale);studio.Select(source.Id);var staleBefore=studio.Document.Clone();
            var staleDialog=new DuplicatePieceWindow(studio,stale.Pieces[0],"Kaynak",stale.GradingRules[0]);
            Check(!staleDialog.CopyGrading.IsEnabled,"A stale grading rule is unavailable in duplicate options",checks);staleDialog.Close();
            studio.ChooseDuplicateForVerification=_=>new DuplicateOptions(false,false,true);studio.DuplicateSelectedPiece();
            Check(DocumentSession.ContentEquals(staleBefore,studio.Document) && session.UndoCount==0,"A stale grading rule cannot be copied even if requested",checks);
            studio.LoadDocument(initial);studio.Select(source.Id);
            studio.ChooseDuplicateForVerification=_=> { studio.Commit("Kural değişikliği",()=>studio.Document.GradingRules[0].Deltas[0]=new Point2(4,4));return new DuplicateOptions(false,false,true); };
            studio.DuplicateSelectedPiece();
            Check(studio.Document.Pieces.Count==1 && studio.Document.GradingRules.Count==1 && session.UndoCount==1,
                "Grading changes during duplicate confirmation reject the pending operation",checks);
        }
        finally { studio.ChooseDuplicateForVerification=null;studio.LoadDocument(new ProjectDocument()); }
    }

    private static void VerifyDuplicateOptions(MainWindow studio,List<string> checks)
    {
        var source=new PatternPiece { Name="Ön",Points=[new(0,0),new(10,0),new(0,10)],
            Metadata=new PieceMetadata { Role="Kesim",Size="M",Material="Pamuk",Quantity=3 },
            Grainline=new Grainline { Start=new(1,1),End=new(1,5),NeedsReview=true } };
        var rule=new GradingRuleSet { SourcePieceId=source.Id,SourceClosed=true,SourcePoints=[..source.Points],Deltas=[new(0,0),new(0,0),new(0,0)] };
        var initial=new ProjectDocument { Pieces=[source],GradingRules=[rule] };
        var session=(DocumentSession)typeof(MainWindow).GetField("session",PrivateInstance)!.GetValue(studio)!;
        try
        {
            var dialog=new DuplicatePieceWindow(studio,source,"Ön");
            Check(dialog.CopyMetadata.IsEnabled && dialog.CopyGrainline.IsEnabled && !dialog.Options.CopyMetadata && !dialog.Options.CopyGrainline,
                "Duplicate options require explicit opt-in for metadata and grainline",checks);
            dialog.Close();
            foreach(var metadata in new[] { false,true })
            foreach(var grain in new[] { false,true })
            {
                studio.LoadDocument(initial);studio.Select(source.Id);var before=studio.Document.Clone();
                studio.ChooseDuplicateForVerification=_=>new DuplicateOptions(metadata,grain);
                studio.DuplicateSelectedPiece();
                var original=studio.Document.Pieces.Single(p=>p.Id==source.Id);var copy=studio.Document.Pieces.Single(p=>p.Id!=source.Id);
                Check((copy.Metadata is not null)==metadata && (copy.Grainline is not null)==grain &&
                    (!metadata || (copy.Metadata!.Quantity==3 && copy.Metadata.Size=="M" && !ReferenceEquals(copy.Metadata,original.Metadata))) &&
                    (!grain || (copy.Grainline!.NeedsReview && copy.Grainline.Start==original.Grainline!.Start && !ReferenceEquals(copy.Grainline,original.Grainline))) &&
                    JsonSerializer.Serialize(original)==JsonSerializer.Serialize(before.Pieces[0]) &&
                    JsonSerializer.Serialize(studio.Document.GradingRules)==JsonSerializer.Serialize(before.GradingRules) && session.UndoCount==1,
                    $"Duplicate respects metadata={metadata} grainline={grain} and preserves source/rules",checks);
                Check(session.Undo() && DocumentSession.ContentEquals(before,studio.Document),"Duplicate options undo in one step",checks);
            }
            studio.LoadDocument(initial);studio.Select(source.Id);
            studio.ChooseDuplicateForVerification=_=> { studio.Commit("Kaynak değişikliği",()=>studio.Document.Pieces[0].Metadata!.Quantity=4);return new DuplicateOptions(true,true); };
            studio.DuplicateSelectedPiece();
            Check(studio.Document.Pieces.Count==1 && studio.Document.Pieces[0].Metadata!.Quantity==4,
                "Duplicate option confirmation is rejected after source metadata changes",checks);
        }
        finally { studio.ChooseDuplicateForVerification=null;studio.LoadDocument(new ProjectDocument()); }
    }

    private static void VerifyDuplicatePiece(MainWindow studio,List<string> checks)
    {
        var canvas=(CadCanvas)typeof(MainWindow).GetField("canvas",PrivateInstance)!.GetValue(studio)!;
        var session=(DocumentSession)typeof(MainWindow).GetField("session",PrivateInstance)!.GetValue(studio)!;
        var status=(TextBlock)typeof(MainWindow).GetField("status",PrivateInstance)!.GetValue(studio)!;
        var source=new PatternPiece { Name="Ön",Points=[new(2,3),new(12,3),new(2,13)],
            Metadata=new PieceMetadata { Role="Kesim",Size="M",Material="Pamuk",Quantity=3 } };
        var rule=new GradingRuleSet { SourcePieceId=source.Id,SourceClosed=true,SourcePoints=[..source.Points],
            Deltas=[new(1,1),new(1,1),new(1,1)] };
        var initial=new ProjectDocument { Pieces=[source],GradingRules=[rule] };
        try
        {
            canvas.CancelDrawing();studio.LoadDocument(initial);studio.Select(source.Id);
            var buttons=(IEnumerable<Button>)typeof(MainWindow).GetField("selectionCommands",PrivateInstance)!.GetValue(studio)!;
            var duplicateButton=buttons.Single(b=>Equals(b.Content,"Parçayı çoğalt…"));
            Check(duplicateButton.IsEnabled && (duplicateButton.ToolTip as string ?? "").Contains("kopyalanmaz",StringComparison.OrdinalIgnoreCase)
                && UIElementAutomationPeer.CreatePeerForElement(duplicateButton)?.GetName()=="Parçayı çoğalt…",
                "Duplicate command is accessible and states project history is not copied",checks);
            var before=studio.Document.Clone();var undo=session.UndoCount;var calls=0;
            studio.ConfirmDuplicateForVerification=p=> { calls++; return false; };
            studio.DuplicateSelectedPiece();
            Check(calls==1 && DocumentSession.ContentEquals(before,studio.Document) && session.UndoCount==undo,
                "Declining duplication keeps source and history unchanged",checks);
            studio.ConfirmDuplicateForVerification=p=>true;
            studio.DuplicateSelectedPiece();
            var copy=studio.Document.Pieces.Single(p=>p.Id!=source.Id);
            var original=studio.Document.Pieces.Single(p=>p.Id==source.Id);
            Check(copy.Id!=Guid.Empty && copy.Id!=source.Id && copy.Name.EndsWith(" · kopya",StringComparison.Ordinal)
                && copy.Closed==original.Closed && copy.Points.SequenceEqual(original.Points) && !ReferenceEquals(copy.Points,original.Points)
                && copy.Metadata is null && studio.Document.GradingRules.Count==1 && studio.Document.GradingRules[0].SourcePieceId==source.Id
                && studio.SelectedId==copy.Id && session.UndoCount==undo+1
                && status.Text.Contains(source.Id.ToString("D"),StringComparison.Ordinal) && status.Text.Contains(copy.Id.ToString("D"),StringComparison.Ordinal),
                "Duplicate creates independent same-position geometry with a new selected identity and no production metadata or grading rules",checks);
            var duplicated=studio.Document.Clone();
            Check(session.Undo() && DocumentSession.ContentEquals(before,studio.Document) && session.UndoCount==undo,
                "Duplicate is one undo step",checks);
            Check(session.Redo() && DocumentSession.ContentEquals(duplicated,studio.Document),
                "Redo restores the duplicate with its identity",checks);

            studio.LoadDocument(initial);studio.Select(source.Id);studio.SetTool("Çoklu çizgi");
            canvas.RestorePendingDrawing(new RecoveryPendingDrawing("Çoklu çizgi",[new(1,1),new(2,1)]));
            before=studio.Document.Clone();undo=session.UndoCount;calls=0;
            studio.ConfirmDuplicateForVerification=p=> { calls++;return true; };
            studio.DuplicateSelectedPiece();
            Check(calls==0 && DocumentSession.ContentEquals(before,studio.Document) && canvas.PendingDrawingPointCount==2 && session.UndoCount==undo,
                "Duplicate refuses to disturb a pending drawing",checks);
            canvas.CancelDrawing();studio.SetTool("Seç");
            var nameInput=(TextBox)typeof(MainWindow).GetField("name",PrivateInstance)!.GetValue(studio)!;
            nameInput.Text="Bekleyen taslak";before=studio.Document.Clone();calls=0;
            studio.DuplicateSelectedPiece();
            Check(calls==0 && DocumentSession.ContentEquals(before,studio.Document) && nameInput.Text=="Bekleyen taslak" && session.UndoCount==undo,
                "Duplicate refuses to disturb a property draft",checks);

            studio.LoadDocument(initial);studio.Select(source.Id);before=studio.Document.Clone();undo=session.UndoCount;
            studio.ConfirmDuplicateForVerification=p=> { studio.Commit("Test eşzamanlı değişim",()=>studio.Document.Name="Değişti");return true; };
            studio.DuplicateSelectedPiece();
            Check(studio.Document.Pieces.Count==1 && studio.Document.Name=="Değişti" && session.UndoCount==undo+1,
                "Duplicate does not apply a stale confirmation to a changed project",checks);

            studio.LoadDocument(initial);studio.Select(source.Id);before=studio.Document.Clone();undo=session.UndoCount;
            studio.ConfirmDuplicateForVerification=p=>true;
            studio.AfterCommitBeforeRefreshForVerification=()=>throw new IOException("injected duplicate refresh failure");
            Exception? error=null;
            try { studio.DuplicateSelectedPiece(); } catch(Exception ex) { error=ex; }
            finally { studio.AfterCommitBeforeRefreshForVerification=null; }
            Check(error is InvalidOperationException && error.Message.Contains("çoğaltıldı",StringComparison.OrdinalIgnoreCase)
                && studio.Document.Pieces.Count==2 && session.UndoCount==undo+1,
                "Duplicate reports that it committed if subsequent refresh fails",checks);
            var after=studio.Document.Clone();studio.DuplicateSelectedPiece();
            Check(DocumentSession.ContentEquals(after,studio.Document) && session.UndoCount==undo+1,
                "Duplicate retry after a refresh failure does not add another piece",checks);
            Check(session.Undo() && DocumentSession.ContentEquals(before,studio.Document),
                "Committed duplicate can be undone after refresh failure",checks);

            var many=Enumerable.Range(0,ProjectStorage.MaximumPieces).Select(i=>new PatternPiece
                { Name="Limit",Closed=false,Points=[new(i,0),new(i+1,0)] }).ToList();
            studio.LoadDocument(new ProjectDocument { Pieces=many });studio.Select(many[0].Id);
            before=studio.Document.Clone();undo=session.UndoCount;error=null;
            try { studio.DuplicateSelectedPiece(); } catch(Exception ex) { error=ex; }
            Check(error is InvalidDataException && DocumentSession.ContentEquals(before,studio.Document)
                && session.UndoCount==undo && studio.SelectedId==many[0].Id,
                "Project piece limit rejects duplicate atomically",checks);
        }
        finally
        {
            studio.ConfirmDuplicateForVerification=null;studio.AfterCommitBeforeRefreshForVerification=null;
            canvas.CancelDrawing();studio.LoadDocument(new ProjectDocument());studio.SetTool("Seç");
        }
    }

    private static void VerifyDeleteShortcutSafety(MainWindow studio,List<string> checks)
    {
        var piece=new PatternPiece { Name="Çizim sırasında seçili parça",Points=[new(0,0),new(10,0),new(0,10)] };
        var rule=new GradingRuleSet { SourcePieceId=piece.Id,SourceClosed=true,SourcePoints=[..piece.Points],Deltas=[new(0,0),new(0,0),new(0,0)] };
        studio.LoadDocument(new ProjectDocument { Pieces=[piece],GradingRules=[rule] });
        studio.SetTool("Seç");studio.Select(piece.Id);
        var canvas=(CadCanvas)typeof(MainWindow).GetField("canvas",PrivateInstance)!.GetValue(studio)!;
        var session=(DocumentSession)typeof(MainWindow).GetField("session",PrivateInstance)!.GetValue(studio)!;
        var status=(TextBlock)typeof(MainWindow).GetField("status",PrivateInstance)!.GetValue(studio)!;
        void DeleteKey()
        {
            Check(studio.HandleCanvasShortcut(Key.Delete,true),"Delete shortcut is handled when canvas-focused",checks);
        }
        var before=studio.Document.Clone(); var undo=session.UndoCount;var redo=session.RedoCount;var dirty=session.IsDirty;
        try
        {
            foreach(var tool in new[] { "Çizgi","Dikdörtgen","Bézier","Yay","Çoklu çizgi" })
            {
                canvas.CancelDrawing();studio.SetTool(tool);
                foreach(var pending in new[] { false,true })
                {
                    if(pending)canvas.RestorePendingDrawing(new RecoveryPendingDrawing(tool,[new Point2(1,2)]));
                    var drawing=canvas.CapturePendingDrawing();DeleteKey();
                    Check(DocumentSession.ContentEquals(before,studio.Document) && studio.SelectedId==piece.Id && session.UndoCount==undo && session.RedoCount==redo && session.IsDirty==dirty
                        && (drawing is null ? !canvas.HasPendingDrawing : canvas.CapturePendingDrawing()!.Points.SequenceEqual(drawing.Points)) && status.Text.Contains("silinmedi"),
                        $"Delete during {tool} with pending={pending} preserves piece, grading rule, drawing and history",checks);
                }
            }
            canvas.CancelDrawing();studio.SetTool("Seç");
            var drag=typeof(CadCanvas).GetField("dragId",PrivateInstance)!;drag.SetValue(canvas,piece.Id);
            try { DeleteKey();Check(DocumentSession.ContentEquals(before,studio.Document) && status.Text.Contains("Sürüklemeyi"),"Delete preserves piece during drag preview",checks); }
            finally { drag.SetValue(canvas,null); }
            foreach(var fieldName in new[] { "largeVertexCancellation","largeCompletionCancellation" })
            {
                var field=typeof(CadCanvas).GetField(fieldName,PrivateInstance)!;
                using var cts=new CancellationTokenSource();field.SetValue(canvas,cts);
                try { DeleteKey();Check(DocumentSession.ContentEquals(before,studio.Document) && status.Text.Contains("Doğrulamanın"),"Delete preserves piece while "+fieldName+" is pending",checks); }
                finally { field.SetValue(canvas,null); }
            }
            var deleteButton=Descendants<Button>((DependencyObject)studio.Content).Single(b=>Equals(b.Content,"Parçayı sil"));
            studio.SetTool("Çizgi");canvas.RestorePendingDrawing(new RecoveryPendingDrawing("Çizgi",[new Point2(1,2)]));
            studio.ConfirmDeleteWithGradingForVerification=(_,_)=>true;
            deleteButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(DocumentSession.ContentEquals(before,studio.Document) && canvas.HasPendingDrawing && status.Text.Contains("silinmedi"),
                "Toolbar deletion preserves an unfinished drawing and the selected piece",checks);
            canvas.CancelDrawing();studio.SetTool("Seç");
            var nameField=(TextBox)typeof(MainWindow).GetField("name",PrivateInstance)!.GetValue(studio)!;
            nameField.Text="Uygulanmamış ad";
            DeleteKey();
            Check(DocumentSession.ContentEquals(before,studio.Document) && studio.SelectedId==piece.Id && session.UndoCount==undo && session.RedoCount==redo && nameField.Text=="Uygulanmamış ad" && status.Text.Contains("taslağ"),
                "Delete preserves the selected piece and its unapplied property draft",checks);
            nameField.Text=piece.Name;
            var asked=0;var askedCorrectly=false;
            studio.ConfirmDeleteWithGradingForVerification=(candidate,count)=>
            { asked++;askedCorrectly=candidate.Id==piece.Id && count==1;return false; };
            DeleteKey();
            Check(asked==1 && askedCorrectly && DocumentSession.ContentEquals(before,studio.Document) && studio.SelectedId==piece.Id && session.UndoCount==undo && session.RedoCount==redo && session.IsDirty==dirty,
                "Rejecting deletion preserves the selected piece, grading rule, and history",checks);
            studio.ConfirmDeleteWithGradingForVerification=(candidate,count)=>
            { asked++;return candidate.Id==piece.Id && count==1; };
            DeleteKey();
            Check(studio.Document.Pieces.Count==0 && studio.Document.GradingRules.Count==0 && session.UndoCount==undo+1,"Delete in selection mode removes selected piece and associated rules",checks);
            Invoke(studio,"Undo");
            Check(DocumentSession.ContentEquals(before,studio.Document) && studio.SelectedId==piece.Id,"Undo restores deleted piece and grading rule",checks);
            studio.ConfirmDeleteWithGradingForVerification=(_,_)=>
            { studio.Commit("Onay sırasında düzenleme",()=>studio.Document.Pieces[0].Name="Değişen parça");return true; };
            DeleteKey();
            Check(studio.Document.Pieces.Count==1 && studio.Document.Pieces[0].Name=="Değişen parça" && studio.Document.GradingRules.Count==1 && status.Text.Contains("silinmedi"),
                "A project change while confirming prevents deletion",checks);
            studio.ConfirmDeleteWithGradingForVerification=(_,_)=> { nameField.Text="Onay sırasında taslak";return true; };
            DeleteKey();
            Check(studio.Document.Pieces.Count==1 && nameField.Text=="Onay sırasında taslak" && status.Text.Contains("silinmedi"),
                "A draft entered while confirming prevents deletion and remains available",checks);
            var plain=new PatternPiece { Name="Kuralsız",Points=[new(0,0),new(10,0),new(0,10)] };
            studio.LoadDocument(new ProjectDocument { Pieces=[plain] });studio.Select(plain.Id);
            var unexpectedPrompt=false;
            studio.ConfirmDeleteWithGradingForVerification=(_,_)=>{ unexpectedPrompt=true;return false; };
            DeleteKey();
            Check(!unexpectedPrompt && studio.Document.Pieces.Count==0,"Deleting a piece without grading rules does not request grading confirmation",checks);
        }
        finally { studio.ConfirmDeleteWithGradingForVerification=null;canvas.CancelDrawing();studio.SetTool("Seç"); }
    }
    private static void VerifyActiveDragGuards(MainWindow studio,string work,List<string> checks)
    {
        var piece=new PatternPiece { Points=[new(0,0),new(10,0),new(0,10)] };
        var other=new PatternPiece { Name="Diğer",Points=[new(20,0),new(30,0),new(20,10)] };
        studio.LoadDocument(new ProjectDocument { Name="Sürükleme koruması",Pieces=[piece,other] });
        studio.SetTool("Seç");studio.Select(piece.Id);
        studio.Commit("İlk düzenleme",()=>studio.Document.Pieces[0].Name="İlk");
        studio.Commit("İkinci düzenleme",()=>studio.Document.Pieces[0].Name="İkinci");
        Invoke(studio,"Undo");
        var canvas=(CadCanvas)typeof(MainWindow).GetField("canvas",PrivateInstance)!.GetValue(studio)!;
        var session=(DocumentSession)typeof(MainWindow).GetField("session",PrivateInstance)!.GetValue(studio)!;
        var status=(TextBlock)typeof(MainWindow).GetField("status",PrivateInstance)!.GetValue(studio)!;
        var drag=typeof(CadCanvas).GetField("dragId",PrivateInstance)!;
        var original=typeof(CadCanvas).GetField("dragOriginal",PrivateInstance)!;
        var preview=typeof(CadCanvas).GetField("dragPreview",PrivateInstance)!;
        var points=piece.Points.ToList(); var moved=points.Select(p=>new Point2(p.X+5,p.Y)).ToList();
        drag.SetValue(canvas,piece.Id);original.SetValue(canvas,points);preview.SetValue(canvas,moved);
        var before=studio.Document.Clone();var undo=session.UndoCount;var redo=session.RedoCount;var dirty=session.IsDirty;
        var target=Path.Combine(work,"active-drag-save.tcad");var bytes=new byte[] { 1,2,3,4 };File.WriteAllBytes(target,bytes);
        try
        {
            foreach(var method in new[] { "Save","SaveAs","SaveTo" })
            {
                try { if(method=="SaveTo")Invoke(studio,method,target);else Invoke(studio,method);throw new InvalidOperationException("Save unexpectedly accepted a drag preview."); }
                catch(InvalidOperationException ex) when(ex.Message.Contains("Sürükleme henüz")) { }
                Check(File.ReadAllBytes(target).SequenceEqual(bytes) && canvas.HasActiveDragPreview && DocumentSession.ContentEquals(before,studio.Document)
                    && session.UndoCount==undo && session.RedoCount==redo && session.IsDirty==dirty,
                    method+" blocks active drag without changing file, preview or history",checks);
            }
            var canDiscard=(bool)typeof(MainWindow).GetMethod("CanDiscard",PrivateInstance,null,Type.EmptyTypes,null)!.Invoke(studio,null)!;
            Invoke(studio,"NewDocument");
            Check(!canDiscard && canvas.HasActiveDragPreview && DocumentSession.ContentEquals(before,studio.Document) && studio.SelectedId==piece.Id
                && status.Text.Contains("Sürükleme henüz"),"Close/replacement guard and New preserve active drag",checks);
            studio.SetTool("Seç");studio.SetTool("Çizgi");
            Check(studio.ActiveTool=="Seç" && canvas.HasActiveDragPreview && ((List<Point2>)preview.GetValue(canvas)!).SequenceEqual(moved),
                "Same and different tool shortcuts preserve active drag preview",checks);
            Invoke(studio,"Undo");Invoke(studio,"Redo");
            Check(canvas.HasActiveDragPreview && ((List<Point2>)preview.GetValue(canvas)!).SequenceEqual(moved)
                && DocumentSession.ContentEquals(before,studio.Document) && session.UndoCount==undo && session.RedoCount==redo && session.IsDirty==dirty,
                "Undo and redo preserve active drag, document and history",checks);
            studio.Select(other.Id);
            var list=(ListBox)typeof(MainWindow).GetField("pieces",PrivateInstance)!.GetValue(studio)!;
            list.SelectedItem=studio.Document.Pieces.Single(p=>p.Id==other.Id);
            Check(studio.SelectedId==piece.Id && (list.SelectedItem as PatternPiece)?.Id==piece.Id && canvas.HasActiveDragPreview
                && ((List<Point2>)preview.GetValue(canvas)!).SequenceEqual(moved),
                "Programmatic and list selection changes preserve the dragged piece selection",checks);
            canvas.CancelDrawing();studio.SetTool("Çizgi");
            Check(!canvas.HasActiveDragPreview && studio.ActiveTool=="Çizgi" && DocumentSession.ContentEquals(before,studio.Document),
                "Cancel drag releases tool change and leaves document unchanged",checks);
            Invoke(studio,"Undo");Invoke(studio,"Redo");
            Check(DocumentSession.ContentEquals(before,studio.Document) && session.UndoCount==undo && session.RedoCount==redo,
                "Undo and redo work again after canceling a drag",checks);
        }
        finally { canvas.CancelDrawing();studio.SetTool("Seç"); }
    }
    private static void VerifyPolylineMouseEntryLimits(MainWindow studio,List<string> checks)
    {
        studio.LoadDocument(new ProjectDocument { Name="Çoklu çizgi giriş sınırı" });
        studio.SetTool("Çoklu çizgi");
        var canvas=(CadCanvas)typeof(MainWindow).GetField("canvas",PrivateInstance)!.GetValue(studio)!;
        var drawing=(List<Point2>)typeof(CadCanvas).GetField("drawing",PrivateInstance)!.GetValue(canvas)!;
        var down=typeof(CadCanvas).GetMethod("Down",PrivateInstance)!.CreateDelegate<Action<object,MouseButtonEventArgs>>(canvas);
        var safe=typeof(CadCanvas).GetMethod("Safe",PrivateInstance)!;
        var world=typeof(CadCanvas).GetMethod("World",PrivateInstance)!;
        var click=new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.Left);
        var point=(Point2)world.Invoke(canvas,[click.GetPosition(canvas),true])!;
        // The synthetic event uses the existing pointer location without moving it.
        // Surround that coordinate with a valid contour, so the actual mouse handler
        // adds a distinct last point that can be validated and committed.
        Point2[] Prior(int count) => Enumerable.Range(1,count).Select(i=>
        {
            var angle=2*Math.PI*i/(count+1);
            return new Point2(point.X-1000+1000*Math.Cos(angle),point.Y+1000*Math.Sin(angle));
        }).ToArray();
        void Click() => safe.Invoke(canvas,[new Action(()=>down(canvas,click)),true,null]);
        try
        {
            drawing.AddRange(Prior(4096));
            Click();
            var accepted=canvas.CapturePendingDrawing()!.Points.ToArray();
            Check(accepted.Length==4097 && accepted[^1]==point && !studio.IsVisible,
                "Mouse input accepts the 4097th polyline point without displaying a window",checks);
            canvas.FinishPolyline();
            var task=canvas.LargeCompletionTask ?? throw new InvalidOperationException("Large mouse drawing did not start background validation.");
            Check(canvas.LargeCompletionRunning && studio.Document.Pieces.Count==0,
                "Mouse-entered large polyline validates in background before commit",checks);
            Click();
            Check(canvas.LargeCompletionRunning && ReferenceEquals(task,canvas.LargeCompletionTask)
                && canvas.CapturePendingDrawing()!.Points.SequenceEqual(accepted) && studio.Document.Pieces.Count==0,
                "Mouse input cannot change or cancel a polyline while its completion is validating",checks);
            PumpUntil(()=>task.IsCompleted);task.GetAwaiter().GetResult();
            Check(!canvas.HasPendingDrawing && studio.Document.Pieces.Count==1 && studio.Document.Pieces[0].Points.SequenceEqual(accepted),
                "Mouse-entered 4097-point polyline commits unchanged after validation",checks);

            studio.LoadDocument(new ProjectDocument { Name="Çoklu çizgi üst sınırı" });
            drawing=(List<Point2>)typeof(CadCanvas).GetField("drawing",PrivateInstance)!.GetValue(canvas)!;
            drawing.AddRange(Prior(LargeContourValidator.MaximumStoredPoints));
            var before=drawing.ToArray();var document=studio.Document.Clone();
            Click();
            var status=(TextBlock)typeof(MainWindow).GetField("status",PrivateInstance)!.GetValue(studio)!;
            Check(canvas.CapturePendingDrawing()!.Points.SequenceEqual(before) && DocumentSession.ContentEquals(document,studio.Document)
                && status.Text.Contains(LargeContourValidator.MaximumStoredPoints.ToString("N0",System.Globalization.CultureInfo.CurrentCulture))
                && status.Text.Contains("korundu"),
                "Mouse input rejects point 10001 and preserves the entire preview and document",checks);
        }
        finally { canvas.CancelDrawing();studio.SetTool("Seç"); }
    }

    private static void VerifyLargePendingPolyline(MainWindow studio,List<string> checks)
    {
        // Exercise real worker completion without showing a window. Inputs remain
        // independent from the active document until the UI dispatcher commits.
        studio.LoadDocument(new ProjectDocument { Name="Büyük çizim doğrulaması" });
        studio.SetTool("Çoklu çizgi");
        var canvas=(CadCanvas)typeof(MainWindow).GetField("canvas",PrivateInstance)!.GetValue(studio)!;
        var points=Enumerable.Range(0,4097).Select(i=>
        {
            var angle=2*Math.PI*i/4097;
            return new Point2(1000*Math.Cos(angle),1000*Math.Sin(angle));
        }).ToArray();
        var pending=new RecoveryPendingDrawing("Çoklu çizgi",points);
        try
        {
            studio.Commit("Önceki 1",()=>studio.Document.Name="Önceki 1");
            studio.Commit("Önceki 2",()=>studio.Document.Name="Önceki 2");
            var history=(DocumentSession)typeof(MainWindow).GetField("session",PrivateInstance)!.GetValue(studio)!;
            if(!history.Undo())throw new InvalidOperationException("Could not prepare redo history.");
            canvas.RestorePendingDrawing(pending);
            var unchanged=studio.Document.Clone();
            var undoBefore=history.UndoCount;var redoBefore=history.RedoCount;
            canvas.FinishPolyline();
            var first=canvas.LargeCompletionTask ?? throw new InvalidOperationException("Large drawing validation did not start.");
            Invoke(studio,"Undo");Invoke(studio,"Redo");
            Check(canvas.LargeCompletionRunning && DocumentSession.ContentEquals(unchanged,studio.Document)
                && canvas.CapturePendingDrawing()!.Points.SequenceEqual(points) && history.UndoCount==undoBefore && history.RedoCount==redoBefore,
                "Undo and redo are blocked without discarding pending drawing while large completion runs",checks);
            canvas.FinishPolyline();
            Check(canvas.LargeCompletionRunning && ReferenceEquals(first,canvas.LargeCompletionTask)
                && DocumentSession.ContentEquals(unchanged,studio.Document) && canvas.CapturePendingDrawing()!.Points.SequenceEqual(points),
                "Large pending drawing runs once and remains uncommitted during validation",checks);
            PumpUntil(()=>first.IsCompleted); first.GetAwaiter().GetResult();
            Check(!canvas.LargeCompletionRunning && !canvas.HasPendingDrawing && studio.Document.Pieces.Count==1
                && studio.Document.Pieces[0].Closed && studio.Document.Pieces[0].Points.SequenceEqual(points),
                "Valid 4097-point recovered drawing commits its unchanged contour",checks);
            var status=(TextBlock)typeof(MainWindow).GetField("status",PrivateInstance)!.GetValue(studio)!;
            Check(status.Text.Contains("4096") && Equals(status.ToolTip,status.Text),
                "Large drawing completion discloses downstream limits in status and tooltip",checks);
            var session=(DocumentSession)typeof(MainWindow).GetField("session",PrivateInstance)!.GetValue(studio)!;
            Check(session.UndoCount==undoBefore+1 && session.Undo() && studio.Document.Pieces.Count==0,
                "Large drawing completion is a single undoable edit",checks);

            studio.LoadDocument(new ProjectDocument { Name="Geçersiz büyük çizim" });
            var invalid=points.ToArray(); invalid[2048]=invalid[0];
            canvas.RestorePendingDrawing(new RecoveryPendingDrawing("Çoklu çizgi",invalid));
            var beforeFailure=studio.Document.Clone();
            canvas.FinishPolyline();var failed=canvas.LargeCompletionTask!;
            PumpUntil(()=>failed.IsCompleted);failed.GetAwaiter().GetResult();
            Check(canvas.CapturePendingDrawing()!.Points.SequenceEqual(invalid)
                && DocumentSession.ContentEquals(beforeFailure,studio.Document) && session.UndoCount==0,
                "Invalid large drawing preserves preview, document and history",checks);

            canvas.RestorePendingDrawing(pending);
            canvas.FinishPolyline();var stale=canvas.LargeCompletionTask!;
            studio.AddPiece([new Point2(0,0),new Point2(10,0)],false);
            var edited=studio.Document.Clone();
            PumpUntil(()=>stale.IsCompleted);stale.GetAwaiter().GetResult();
            Check(canvas.CapturePendingDrawing()!.Points.SequenceEqual(points)
                && DocumentSession.ContentEquals(edited,studio.Document),
                "Large drawing result is discarded when the document changes",checks);

            canvas.FinishPolyline();var cancelled=canvas.LargeCompletionTask!;
            var replacement=new RecoveryPendingDrawing("Çoklu çizgi",[new(5,5),new(8,5),new(8,9)]);
            canvas.RestorePendingDrawing(replacement);
            PumpUntil(()=>cancelled.IsCompleted);cancelled.GetAwaiter().GetResult();
            Check(canvas.CapturePendingDrawing()!.Points.SequenceEqual(replacement.Points)
                && DocumentSession.ContentEquals(edited,studio.Document) && !canvas.LargeCompletionRunning,
                "Replacing a pending drawing cancels the old validation without committing it",checks);
        }
        finally { canvas.CancelDrawing();studio.SetTool("Seç"); }
    }

    private static void VerifyPendingDrawingFailurePreservesPoints(MainWindow studio,List<string> checks)
    {
        studio.LoadDocument(new ProjectDocument { Name="Çizim testi" });
        studio.SetTool("Çoklu çizgi");
        var canvas=(CadCanvas)typeof(MainWindow).GetField("canvas",PrivateInstance)!.GetValue(studio)!;
        var field=typeof(CadCanvas).GetField("drawing",PrivateInstance)!;
        var points=(List<Point2>)field.GetValue(canvas)!;
        points.AddRange([new(0,0),new(10,10),new(0,10),new(10,0)]);
        var before=points.ToArray();var documentBefore=studio.Document.Clone();
        canvas.FinishPolyline();
        Check(((List<Point2>)field.GetValue(canvas)!).SequenceEqual(before)
            && DocumentSession.ContentEquals(documentBefore,studio.Document) && canvas.HasPendingDrawing,
            "Invalid finished polyline preserves all pending points and document",checks);

        // Exercise the same rollback wrapper used around a final mouse click without
        // synthesizing a mouse event or displaying a window.
        var safe=typeof(CadCanvas).GetMethod("Safe",PrivateInstance)!;
        Action invalidFinalClick=()=>
        {
            ((List<Point2>)field.GetValue(canvas)!).Add(new Point2(50,50));
            throw new InvalidOperationException("Son nokta doğrulaması başarısız");
        };
        safe.Invoke(canvas,[invalidFinalClick,true,null]);
        Check(((List<Point2>)field.GetValue(canvas)!).SequenceEqual(before)
            && DocumentSession.ContentEquals(documentBefore,studio.Document),
            "Failed final drawing click restores earlier pending points",checks);
        canvas.CancelDrawing();studio.SetTool("Seç");
    }
    private static void VerifyRecoveryWarningPresentation(MainWindow studio,string reportPath,List<string> checks)
    {
        var notice=(TextBlock)typeof(MainWindow).GetField("recoveryNotice",PrivateInstance)!.GetValue(studio)!;
        var details=(Button)typeof(MainWindow).GetField("recoveryDetails",PrivateInstance)!.GetValue(studio)!;
        var message="Kurtarma taraması tamamlanamadı veya bazı kayıtlar okunamadı; tüm kayıtlar listelenmemiş olabilir. Ayrıntıları inceleyin ve kurtarma dosyalarını silmeden saklayın.";
        var explanation=string.Join("\n",Enumerable.Range(0,30).Select(i=>$"{i+1}: Kurtarma kaydı taranmadı; özgün dosyayı koruyun."));
        Invoke(studio,"ShowRecoveryWarning",message,explanation,RecoveryWarningState.Kind.General);
        var root=(FrameworkElement)studio.Content;
        Check(notice.Text.Contains(message) && notice.ToolTip?.ToString()?.Contains(explanation)==true && details.IsEnabled
            && details.Focusable && KeyboardNavigation.GetIsTabStop(details)
            && !string.IsNullOrWhiteSpace(details.Content as string),
            "Recovery warning keeps complete details and exposes an enabled tab-stop action",checks);
        foreach(var (width,height) in new[] { (720,540),(1024,640) })
        {
            Render(studio,Path.ChangeExtension(reportPath,$".recovery-warning-{width}x{height}.png"),width,height);
            Check(FullyVisible(notice,root) && FullyVisible(details,root)
                && notice.ActualHeight+notice.Margin.Top+notice.Margin.Bottom>=notice.DesiredSize.Height-.5,
                $"Recovery warning and details action are fully visible at {width}x{height}",checks);
        }
        // This only checks WPF focus/tab properties and layout while hidden; it does not
        // simulate keyboard traversal or activate the modal details dialog.
        notice.Visibility=Visibility.Collapsed; details.Visibility=Visibility.Collapsed;
    }
    private static bool WarningReachable(FrameworkElement notice,FrameworkElement root)
    {
        if(FullyVisible(notice,root))return true;
        var viewer=Descendants<ScrollViewer>(root).FirstOrDefault(s=>AutomationProperties.GetName(s)=="DXF aktarım uyarıları" && s.IsAncestorOf(notice));
        if(viewer is null || !viewer.Focusable || !KeyboardNavigation.GetIsTabStop(viewer) || viewer.ScrollableHeight<=0)return false;
        var offset=notice.TransformToAncestor(viewer).Transform(new Point(0,0)).Y+viewer.VerticalOffset;
        viewer.ScrollToVerticalOffset(Math.Clamp(offset,0,viewer.ScrollableHeight));root.UpdateLayout();
        var visible=VisibleBoundsInRoot(notice,root);
        var reachable=visible.Width>0 && visible.Height>0;
        viewer.ScrollToVerticalOffset(0);root.UpdateLayout();
        return reachable;
    }
    private static bool FullyVisible(FrameworkElement element,FrameworkElement root)
    {
        var bounds=BoundsInRoot(element,root);var visible=VisibleBoundsInRoot(element,root);
        return bounds.Width>0 && bounds.Height>0 && !visible.IsEmpty
            && visible.Width>=bounds.Width-.5 && visible.Height>=bounds.Height-.5;
    }
    private static bool FullyVisibleInCanvas(FrameworkElement element,FrameworkElement root,double width,double height)
    {
        if(!FullyVisible(element,root))return false;
        var bounds=BoundsInRoot(element,root);bounds.Offset(VisualTreeHelper.GetOffset(root));
        return bounds.Left>=-.5 && bounds.Top>=-.5 && bounds.Right<=width+.5 && bounds.Bottom<=height+.5;
    }
    private static bool RowVisible(DataGridRow row,FrameworkElement root,double gridWidth)
    {
        var bounds=BoundsInRoot(row,root);var visible=VisibleBoundsInRoot(row,root);
        return bounds.Width>0 && bounds.Height>0 && !visible.IsEmpty
            && visible.Height>=bounds.Height-.5 && visible.Width>=Math.Min(bounds.Width,gridWidth)-1;
    }
    private static Rect BoundsInRoot(FrameworkElement element,FrameworkElement root)
        => ReferenceEquals(element,root)?new Rect(root.RenderSize):element.TransformToAncestor(root).TransformBounds(new Rect(element.RenderSize));
    private static Rect VisibleBoundsInRoot(FrameworkElement element,FrameworkElement root)
    {
        var visible=BoundsInRoot(element,root);
        visible.Intersect(new Rect(root.RenderSize));
        for(DependencyObject? current=element;current is not null;current=VisualTreeHelper.GetParent(current))
        {
            if(current is UIElement ui && ui.Visibility!=Visibility.Visible)return Rect.Empty;
            if(current is FrameworkElement frame && (frame.ClipToBounds || frame is ScrollContentPresenter or ScrollViewer or DataGrid))
                visible.Intersect(BoundsInRoot(frame,root));
            if(current is Visual visual && VisualTreeHelper.GetClip(visual) is { } clip)
            {
                var clipBounds=ReferenceEquals(visual,root)?clip.Bounds:visual.TransformToAncestor(root).TransformBounds(clip.Bounds);
                visible.Intersect(clipBounds);
            }
            if(ReferenceEquals(current,root) || visible.IsEmpty)break;
        }
        return visible;
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
    private static void Render(Window window, string path, int width, int height)
    {
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(width, height)); content.Arrange(new Rect(0, 0, width, height)); content.UpdateLayout();
        content.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        content.UpdateLayout();
        if (window is MainWindow studio) { studio.FitView(); content.UpdateLayout(); }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        var backing = new DrawingVisual();
        using (var dc = backing.RenderOpen()) dc.DrawRectangle(window.Background, null, new Rect(0, 0, width, height));
        bitmap.Render(backing); bitmap.Render(content);
        ProjectStorage.EnsureWritableDestination(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        encoder.Save(stream);
    }
    private static void StopTimer(MainWindow window) => (typeof(MainWindow).GetField("autosaveTimer", PrivateInstance)?.GetValue(window) as DispatcherTimer)?.Stop();
    private static void Invoke(MainWindow target, string method, params object[] args)
    {
        var member = typeof(MainWindow).GetMethod(method, PrivateInstance) ?? throw new MissingMethodException(method);
        try { member.Invoke(target, args); }
        catch (TargetInvocationException ex) when (ex.InnerException is not null) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); }
    }
}


