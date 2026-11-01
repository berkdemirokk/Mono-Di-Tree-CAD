using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using TekstilCAD.Core;

namespace TekstilCAD.Desktop;
internal static class GroupMoveVerification
{
    internal static void Run(MainWindow studio,List<string> checks)
    {
        var previous=SynchronizationContext.Current;SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(studio.Dispatcher));
        const BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Instance;
        var session=(DocumentSession)typeof(MainWindow).GetField("session",flags)!.GetValue(studio)!;
        void Wait(Task task) { typeof(DesktopVerification).GetMethod("PumpUntil",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,[new Func<bool>(()=>task.IsCompleted)]);task.GetAwaiter().GetResult(); }
        void Pump(Func<bool> test)=>typeof(DesktopVerification).GetMethod("PumpUntil",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,[test]);
        var a=new PatternPiece { Name="Aynı",Points=[new(0,0),new(10,0),new(0,10)],
            Metadata=new PieceMetadata { Role="Kesim",Quantity=2 },Grainline=new Grainline { Start=new(1,1),End=new(1,5),NeedsReview=true } };
        var b=new PatternPiece { Name="Aynı",Points=[new(20,0),new(30,0),new(20,10)] };
        var rule=new GradingRuleSet { SourcePieceId=a.Id,SourceClosed=true,SourcePoints=[..a.Points],Deltas=[new(0,0),new(0,0),new(0,0)] };
        void Reset() { studio.LoadDocument(new ProjectDocument { Pieces=[a,b],GradingRules=[rule] });studio.Select(a.Id); }
        GroupMoveWindow Open() { var w=new GroupMoveWindow(studio);w.ChoicesForVerification.SelectedItems.Add(w.ChoicesForVerification.Items[0]);w.ChoicesForVerification.SelectedItems.Add(w.ChoicesForVerification.Items[1]);return w; }
        try
        {
            Reset();var before=studio.Document.Clone();var window=new GroupMoveWindow(studio);
            try
            {
                if(window.ChoicesForVerification.SelectionMode!=SelectionMode.Multiple || window.SelectionCountForVerification!="0 / 2 parça seçildi")
                    throw new InvalidOperationException("Group move must support modifier-free selection and show the selection count.");
                window.SelectAllForVerification.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                if(window.ChoicesForVerification.SelectedItems.Count!=2 || window.SelectionCountForVerification!="2 / 2 parça seçildi")
                    throw new InvalidOperationException("Select all must select and count all pieces.");
                window.ClearSelectionForVerification.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                if(window.ChoicesForVerification.SelectedItems.Count!=0 || window.SelectionCountForVerification!="0 / 2 parça seçildi")
                    throw new InvalidOperationException("Clear selection must clear and count all pieces.");
                checks.Add("Group move uses modifier-free multiple selection with select all and clear actions");
                window.ChoicesForVerification.SelectedItems.Add(window.ChoicesForVerification.Items[0]);
                window.XForVerification.Text="1";Wait(window.ApplyForVerification());
                if(!DocumentSession.ContentEquals(before,studio.Document) || session.UndoCount!=0 || !window.StatusForVerification.Contains("en az iki"))
                    throw new InvalidOperationException("A single selected piece must not start a group move.");
                checks.Add("Group move requires at least two selected pieces");
            }
            finally { window.Close(); }
            window=Open();
            try
            {
                window.XForVerification.Text="0,1";window.YForVerification.Text="2";Wait(window.ApplyForVerification());
                var first=studio.Document.Pieces[0];var second=studio.Document.Pieces[1];
                if(first.Points[0]!=new Point2(.1,2) || second.Points[0]!=new Point2(20.1,2) || first.Metadata?.Quantity!=2 || first.Metadata.Role!="Kesim" ||
                   first.Grainline!.Start!=new Point2(1.1,3) || !first.Grainline.NeedsReview ||
                   !studio.Document.GradingRules.Single().SourcePoints.SequenceEqual(first.Points) || !studio.Document.GradingRules.Single().Deltas.SequenceEqual(rule.Deltas) || session.UndoCount!=1)
                    throw new InvalidOperationException("Group move lost geometry, marker, grain or source rule.");
                var after=studio.Document.Clone();
                if(!session.Undo() || !DocumentSession.ContentEquals(before,studio.Document) || !session.Redo() || !DocumentSession.ContentEquals(after,studio.Document))
                    throw new InvalidOperationException("Group move did not undo and redo atomically.");
                checks.Add("Multiple same-named pieces move with metadata, grain and matching grading rule as one undoable transaction");
            }
            finally { window.Close(); }
            Reset();studio.Commit("Eski kural",()=>studio.Document.GradingRules.Single().SourcePoints[0]=new Point2(-1,0));
            before=studio.Document.Clone();window=Open();
            try
            {
                window.XForVerification.Text="1";Wait(window.ApplyForVerification());
                if(!studio.Document.GradingRules.Single().SourcePoints.SequenceEqual(before.GradingRules.Single().SourcePoints) ||
                   !window.StatusForVerification.Contains("eski serileme kuralı") || !window.StatusForVerification.Contains(a.Id.ToString("D")))
                    throw new InvalidOperationException("Stale grading rule must remain unchanged and be identified after the move.");
                if(!session.Undo() || !DocumentSession.ContentEquals(before,studio.Document))
                    throw new InvalidOperationException("Undo must restore the group move with its stale rule.");
                checks.Add("Group move preserves and identifies stale grading rules and undoes atomically");
            }
            finally { window.Close(); }
            Reset();window=Open();
            try
            {
                before=studio.Document.Clone();window.XForVerification.Text="100000000";Wait(window.ApplyForVerification());
                if(!DocumentSession.ContentEquals(before,studio.Document) || session.UndoCount!=0 || !window.StatusForVerification.Contains("uygulanmadı"))
                    throw new InvalidOperationException("Invalid group translation partially applied.");
                checks.Add("An invalid member rejects the entire group translation");
            }
            finally { window.Close(); }
            Reset();studio.Document.Pieces[0].Notches=[new NotchMark { VertexIndex=0,Position=studio.Document.Pieces[0].Points[0] }];
            before=studio.Document.Clone();window=Open();
            try
            {
                window.XForVerification.Text="1";Wait(window.ApplyForVerification());
                var moved=studio.Document.Pieces[0];
                if(moved.Notches is not { Count:1 } || moved.Notches[0].Position!=moved.Points[0] || moved.Points[0]!=new Point2(1,0) || session.UndoCount!=1)
                    throw new InvalidOperationException("Group move must retain a mark at its translated vertex.");
                if(!session.Undo() || !DocumentSession.ContentEquals(before,studio.Document))
                    throw new InvalidOperationException("Undo must restore the original marked group.");
                checks.Add("Group move translates marked vertices and restores them on undo");
            }
            finally { window.Close();a.Notches=null; }
            foreach(var cancel in new[] { false,true })
            {
                Reset();window=Open();using var reached=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
                window.XForVerification.Text="1";window.BeforeCalculationForVerification=()=> { reached.Set();release.Wait(); };Task? task=null;
                try
                {
                    task=window.ApplyForVerification();Pump(()=>reached.IsSet);
                    if(cancel)window.CancelForVerification();else studio.Commit("Eşzamanlı değişim",()=>studio.Document.Name="Değişti");
                    var expected=studio.Document.Clone();var undo=session.UndoCount;release.Set();Wait(task);
                    if(!DocumentSession.ContentEquals(expected,studio.Document) || session.UndoCount!=undo)
                        throw new InvalidOperationException("Cancelled or stale group translation changed the project.");
                    checks.Add(cancel?"Cancelled group move leaves project unchanged":"Stale group move preserves newer project changes");
                }
                finally { release.Set();if(task is not null)Wait(task);window.Close(); }
            }
        }
        finally { studio.LoadDocument(new ProjectDocument());SynchronizationContext.SetSynchronizationContext(previous); }
    }
}
