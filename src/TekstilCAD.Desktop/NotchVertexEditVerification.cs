using System.Reflection;
using System.Windows.Controls;
using System.Windows.Threading;
using TekstilCAD.Core;

namespace TekstilCAD.Desktop;

internal static class NotchVertexEditVerification
{
    private const BindingFlags PrivateInstance=BindingFlags.NonPublic|BindingFlags.Instance;
    internal static void Run(MainWindow studio,List<string> checks)
    {
        var previous=SynchronizationContext.Current;SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(studio.Dispatcher));
        var piece=new PatternPiece { Points=[new(0,0),new(10,0),new(10,10),new(0,10)],
            Notches=[new NotchMark { VertexIndex=1,Position=new(10,0) }],Metadata=new PieceMetadata { Size="M" } };
        var rule=new GradingRuleSet { SourcePieceId=piece.Id,SourceClosed=true,SourcePoints=[..piece.Points],Deltas=[new(0,0),new(0,0),new(0,0),new(0,0)] };
        var session=(DocumentSession)typeof(MainWindow).GetField("session",PrivateInstance)!.GetValue(studio)!;
        void Wait(Task task) { typeof(DesktopVerification).GetMethod("PumpUntil",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,[new Func<bool>(()=>task.IsCompleted)]);task.GetAwaiter().GetResult(); }
        try
        {
            foreach(var operation in new[] { "insert","move","delete" })
            {
                studio.LoadDocument(new ProjectDocument { Pieces=[piece],GradingRules=[rule] });studio.Select(piece.Id);var before=studio.Document.Clone();
                var tools=new TextileToolsWindow(studio);
                try
                {
                    var task=operation switch { "insert"=>tools.InsertVertexAsync("0","5","0"),"move"=>tools.MoveVertexAsync("1","11","0"),_=>tools.DeleteVertexAsync("1") };
                    Wait(task);var current=studio.Document.Pieces.Single();
                    if(operation=="delete")
                    {
                        var text=((TextBlock)typeof(TextileToolsWindow).GetField("result",PrivateInstance)!.GetValue(tools)!).Text;
                        if(!text.Contains("İşaretli nokta silinemez") || !DocumentSession.ContentEquals(before,studio.Document) || session.UndoCount!=0)
                            throw new InvalidOperationException("Marked vertex delete did not reject safely.");
                    }
                    else
                    {
                        var expected=operation=="insert" ? 2 : 1;var mark=current.Notches!.Single();
                        if(mark.VertexIndex!=expected || mark.Position!=current.Points[expected] || current.Metadata?.Size!="M" ||
                           !studio.Document.GradingRules.Single().SourcePoints.SequenceEqual(rule.SourcePoints) || session.UndoCount!=1)
                            throw new InvalidOperationException("Marked vertex edit lost state.");
                        if(!session.Undo() || !DocumentSession.ContentEquals(before,studio.Document))throw new InvalidOperationException("Marked vertex edit did not undo.");
                    }
                    checks.Add($"Marked numeric vertex {operation} preserves source rules and correct undo or rejection");
                }
                finally { tools.Close(); }
            }
            foreach(var operation in new[] { "midpoint","reverse" })
            {
                var selected=piece.Clone();selected.Closed=operation!="reverse";
                var selectedRule=new GradingRuleSet { SourcePieceId=selected.Id,SourceClosed=selected.Closed,SourcePoints=[..selected.Points],Deltas=[..rule.Deltas] };
                studio.LoadDocument(new ProjectDocument { Pieces=[selected],GradingRules=[selectedRule] });studio.Select(selected.Id);var before=studio.Document.Clone();
                var tools=new TextileToolsWindow(studio);
                try
                {
                    Wait(operation=="midpoint" ? tools.InsertEdgeMidpointAsync("0") : tools.ReverseOpenPathAsync());
                    var current=studio.Document.Pieces.Single();
                    if(current.Notches!.Single().VertexIndex!=2 || current.Notches[0].Position!=new Point2(10,0) ||
                       !studio.Document.GradingRules.Single().SourcePoints.SequenceEqual(selectedRule.SourcePoints) || session.UndoCount!=1)
                        throw new InvalidOperationException("Marked midpoint or reversal lost marker or source rule.");
                    if(!session.Undo() || !DocumentSession.ContentEquals(before,studio.Document))throw new InvalidOperationException("Marked midpoint or reversal did not undo.");
                    checks.Add($"Marked {operation} retains marker and source grading rule and undoes");
                }
                finally { tools.Close(); }
            }
            foreach(var operation in new[] { "move","midpoint","reverse" })
            foreach(var cancel in new[] { false,true })
            {
                var selected=piece.Clone();selected.Closed=operation!="reverse";
                var selectedRule=new GradingRuleSet { SourcePieceId=selected.Id,SourceClosed=selected.Closed,SourcePoints=[..selected.Points],Deltas=[..rule.Deltas] };
                studio.LoadDocument(new ProjectDocument { Pieces=[selected],GradingRules=[selectedRule] });studio.Select(selected.Id);
                var tools=new TextileToolsWindow(studio);using var reached=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
                tools.BeforeVertexMoveForVerification=()=> { reached.Set();release.Wait(); };
                Task? task=null;
                try
                {
                    task=operation switch { "midpoint"=>tools.InsertEdgeMidpointAsync("0"),"reverse"=>tools.ReverseOpenPathAsync(),_=>tools.MoveVertexAsync("1","11","0") };
                    typeof(DesktopVerification).GetMethod("PumpUntil",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,[new Func<bool>(()=>reached.IsSet)]);
                    if(cancel)((CancellationTokenSource)typeof(TextileToolsWindow).GetField("vertexMoveCancellation",PrivateInstance)!.GetValue(tools)!).Cancel();
                    else studio.Commit("Eşzamanlı değişiklik",()=>studio.Document.Name="Değişti");
                    var expected=studio.Document.Clone();var undo=session.UndoCount;release.Set();Wait(task);
                    if(!DocumentSession.ContentEquals(expected,studio.Document) || session.UndoCount!=undo)
                        throw new InvalidOperationException("Stale or cancelled marked edit changed the project.");
                    checks.Add(cancel ? $"Cancelled marked {operation} preserves project" : $"Stale marked {operation} does not overwrite project changes");
                }
                finally { release.Set();if(task is not null)Wait(task);tools.Close(); }
            }
        }
        finally { studio.LoadDocument(new ProjectDocument());SynchronizationContext.SetSynchronizationContext(previous); }
    }
}
