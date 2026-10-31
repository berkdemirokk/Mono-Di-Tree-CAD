using System.Reflection;
using System.Windows.Threading;
using TekstilCAD.Core;

namespace TekstilCAD.Desktop;
internal static class NotchPathJoinClosureVerification
{
    private const BindingFlags Private=BindingFlags.NonPublic|BindingFlags.Instance;
    internal static void Run(MainWindow studio,List<string> checks)
    {
        var previous=SynchronizationContext.Current;SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(studio.Dispatcher));
        var session=(DocumentSession)typeof(MainWindow).GetField("session",Private)!.GetValue(studio)!;
        void Wait(Task task) { typeof(DesktopVerification).GetMethod("PumpUntil",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,[new Func<bool>(()=>task.IsCompleted)]);task.GetAwaiter().GetResult(); }
        try
        {
            var a=new PatternPiece { Closed=false,Points=[new(0,0),new(10,0)],Notches=[new NotchMark { VertexIndex=1,Position=new(10,0) }] };
            var b=new PatternPiece { Closed=false,Points=[new(10,0),new(20,0)] };
            studio.LoadDocument(new ProjectDocument { Pieces=[a,b] });var before=studio.Document.Clone();
            var join=new OpenPathJoinWindow(studio,studio);
            try
            {
                Wait(join.CalculateForVerification());
                if(!join.ApplyButtonForVerification.IsEnabled || !join.MessageForVerification.Contains("1 köşe işareti"))throw new InvalidOperationException("Marked join preview missing.");
                join.ApplyForVerification();var result=studio.Document.Pieces[2];
                if(result.Notches?.Single().Position!=new Point2(10,0) || result.Notches[0].VertexIndex!=1 || session.UndoCount!=1)
                    throw new InvalidOperationException("Marked join lost marker.");
                if(!session.Undo() || !DocumentSession.ContentEquals(before,studio.Document))throw new InvalidOperationException("Marked join undo failed.");
                checks.Add("Marked join previews and preserves source and marker with one undo");
            }
            finally { join.Close(); }
            var c=new PatternPiece { Closed=false,Points=[new(0,0),new(10,0),new(10,10),new(0,0)],
                Notches=[new NotchMark { VertexIndex=3,Position=new(0,0) }] };
            studio.LoadDocument(new ProjectDocument { Pieces=[c] });before=studio.Document.Clone();
            var closure=new OpenPathClosureWindow(studio,studio);
            try
            {
                Wait(closure.CalculateForVerification());
                if(!closure.ApplyButtonForVerification.IsEnabled || !closure.MessageForVerification.Contains("1 köşe işareti"))throw new InvalidOperationException("Marked closure preview missing.");
                closure.ApplyForVerification();var result=studio.Document.Pieces[1];
                if(result.Notches?.Single().VertexIndex!=0 || result.Notches[0].Position!=new Point2(0,0) || session.UndoCount!=1)
                    throw new InvalidOperationException("Marked closure alias lost.");
                if(!session.Undo() || !DocumentSession.ContentEquals(before,studio.Document))throw new InvalidOperationException("Marked closure undo failed.");
                checks.Add("Marked closure canonicalizes endpoint and preserves source with one undo");
            }
            finally { closure.Close(); }
            MarkedStale(studio,checks,Wait);
        }
        finally { studio.LoadDocument(new ProjectDocument());SynchronizationContext.SetSynchronizationContext(previous); }
    }
    private static void MarkedStale(MainWindow studio,List<string> checks,Action<Task> wait)
    {
        var p=new PatternPiece { Closed=false,Points=[new(0,0),new(10,0),new(10,10),new(0,10)],Notches=[new NotchMark { VertexIndex=0,Position=new(0,0) }] };
        studio.LoadDocument(new ProjectDocument { Pieces=[p] });var w=new OpenPathClosureWindow(studio,studio);
        try
        {
            w.ToleranceForVerification.Text="10";wait(w.CalculateForVerification());
            studio.Commit("İşaret değişikliği",()=> { var mark=studio.Document.Pieces[0].Notches![0];mark.VertexIndex=1;mark.Position=studio.Document.Pieces[0].Points[1]; });
            var expected=studio.Document.Clone();w.ApplyForVerification();
            if(!DocumentSession.ContentEquals(expected,studio.Document))throw new InvalidOperationException("Stale marker preview applied.");
            checks.Add("Changed marker invalidates closure preview");
        }
        finally { w.Close(); }
    }
}
