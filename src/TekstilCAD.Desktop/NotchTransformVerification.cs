using System.Reflection;
using TekstilCAD.Core;
using Geometry=TekstilCAD.Core.Geometry;

namespace TekstilCAD.Desktop;

internal static class NotchTransformVerification
{
    private const BindingFlags PrivateInstance=BindingFlags.NonPublic|BindingFlags.Instance;
    internal static void Run(MainWindow studio,List<string> checks)
    {
        static void Check(bool value,string description,List<string> log)
        { if(!value)throw new InvalidOperationException(description);log.Add(description); }
        var session=(DocumentSession)typeof(MainWindow).GetField("session",PrivateInstance)!.GetValue(studio)!;
        var piece=new PatternPiece { Name="Kaynak",Points=[new(0,0),new(10,0),new(10,10),new(0,10)],
            Notches=[new NotchMark { VertexIndex=1,Position=new(10,0) },new NotchMark { VertexIndex=3,Position=new(0,10) }],
            Metadata=new PieceMetadata { Role="Kesim",Material="Pamuk",Quantity=2 },
            Grainline=new Grainline { Start=new(2,2),End=new(2,8),NeedsReview=true } };
        var rule=new GradingRuleSet { SourcePieceId=piece.Id,SourceClosed=true,SourcePoints=[..piece.Points],Deltas=[new(0,0),new(1,0),new(0,0),new(0,0)] };
        var initial=new ProjectDocument { Pieces=[piece],GradingRules=[rule] };
        try
        {
            foreach(var copyDetails in new[] { false,true })
            {
                studio.LoadDocument(initial);studio.Select(piece.Id);var before=studio.Document.Clone();
                studio.ChooseDuplicateForVerification=_=>new DuplicateOptions(copyDetails,copyDetails,copyDetails);
                studio.DuplicateSelectedPiece();
                var original=studio.Document.Pieces.Single(p=>p.Id==piece.Id);var copy=studio.Document.Pieces.Single(p=>p.Id!=piece.Id);
                var copiedRule=studio.Document.GradingRules.SingleOrDefault(r=>r.SourcePieceId==copy.Id);
                Check(copy.Notches is { Count:2 } && copy.Notches.Zip(original.Notches!).All(pair=>pair.First.VertexIndex==pair.Second.VertexIndex && pair.First.Position==pair.Second.Position && !ReferenceEquals(pair.First,pair.Second)) &&
                    !ReferenceEquals(copy.Notches,original.Notches) && (copy.Metadata is not null)==copyDetails && (copy.Grainline is not null)==copyDetails &&
                    (copiedRule is not null)==copyDetails && session.UndoCount==1,
                    $"Duplicate marked piece preserves independent notches and requested details={copyDetails}",checks);
                Check(session.Undo() && DocumentSession.ContentEquals(before,studio.Document),"Duplicate marked piece undoes as one transaction",checks);
            }
            studio.LoadDocument(initial);studio.Select(piece.Id);
            studio.ChooseDuplicateForVerification=_=> { studio.Commit("Kaynak değişikliği",()=>studio.Document.Name="Değişti");return new DuplicateOptions(true,true,true); };
            studio.DuplicateSelectedPiece();
            Check(studio.Document.Pieces.Count==1 && studio.Document.Pieces[0].Notches!.Count==2 && session.UndoCount==1,
                "Duplicate confirmation changed during review cannot copy stale marked source",checks);
            studio.ChooseDuplicateForVerification=null;
            foreach(var operation in new[] { "rotate","mirror" })
            {
                studio.LoadDocument(initial);studio.Select(piece.Id);var before=studio.Document.Clone();
                Func<IReadOnlyList<Point2>,Point2,List<Point2>> transform=operation=="rotate"
                    ? (points,center)=>Geometry.Rotate(points,90,center)
                    : (points,center)=>Geometry.Mirror(points,true,center.X);
                typeof(MainWindow).GetMethod("Transform",PrivateInstance)!.Invoke(studio,[transform]);
                var transformed=studio.Document.Pieces.Single();
                Check(transformed.Notches is { Count:2 } && transformed.Notches.All(n=>n.Position==transformed.Points[n.VertexIndex]) &&
                    transformed.Notches.Select(n=>n.VertexIndex).SequenceEqual(new[] { 1,3 }) && transformed.Metadata!.Quantity==2 &&
                    transformed.Grainline is { NeedsReview:true } && studio.Document.GradingRules.Single().Deltas.SequenceEqual(rule.Deltas) &&
                    studio.Document.GradingRules.Single().SourcePoints.SequenceEqual(rule.SourcePoints) && session.UndoCount==1,
                    $"Marked {operation} keeps markers on corresponding transformed vertices",checks);
                Check(session.Undo() && DocumentSession.ContentEquals(before,studio.Document),$"Marked {operation} is one undo step",checks);
            }
            studio.LoadDocument(initial);studio.Select(piece.Id);var stable=studio.Document.Clone();
            Func<IReadOnlyList<Point2>,Point2,List<Point2>> crossing=(points,_)=>points.Count==4 ? [points[0],points[2],points[1],points[3]] : points.ToList();
            try { typeof(MainWindow).GetMethod("Transform",PrivateInstance)!.Invoke(studio,[crossing]);throw new InvalidOperationException("Invalid contour was accepted."); }
            catch(TargetInvocationException ex) when(ex.InnerException is ArgumentException) { }
            Check(DocumentSession.ContentEquals(stable,studio.Document) && session.UndoCount==0,"Invalid transformed marked contour is rejected atomically",checks);
            VerifyLarge(studio,session,checks);
        }
        finally { studio.ChooseDuplicateForVerification=null;studio.LoadDocument(new ProjectDocument()); }
    }
    private static void VerifyLarge(MainWindow studio,DocumentSession session,List<string> checks)
    {
        var previous=SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext(studio.Dispatcher));
        void Pump(Func<bool> done)=>typeof(DesktopVerification).GetMethod("PumpUntil",PrivateInstance|BindingFlags.Static)!.Invoke(null,[done]);
        PatternPiece Create(int count)
        {
            var points=Enumerable.Range(0,count).Select(i=> { var a=Math.Tau*i/count;return new Point2(100*Math.Cos(a),100*Math.Sin(a)); }).ToList();
            return new PatternPiece { Points=points,Notches=[new NotchMark { VertexIndex=7,Position=points[7] }] };
        }
        var transform=typeof(MainWindow).GetMethod("Transform",PrivateInstance)!;
        Func<IReadOnlyList<Point2>,Point2,List<Point2>> mirror=(points,center)=>Geometry.Mirror(points,true,center.X);
        try
        {
            var piece=Create(513);studio.LoadDocument(new ProjectDocument { Pieces=[piece] });studio.Select(piece.Id);var before=studio.Document.Clone();
            transform.Invoke(studio,[mirror]);var task=studio.MarkedTransformTaskForVerification ?? throw new InvalidOperationException("Marked worker did not start.");
            Pump(()=>task.IsCompleted);task.GetAwaiter().GetResult();
            if(session.UndoCount!=1 || studio.Document.Pieces[0].Notches![0].Position!=studio.Document.Pieces[0].Points[7])throw new InvalidOperationException("Large marked transform failed.");
            if(!session.Undo() || !DocumentSession.ContentEquals(before,studio.Document))throw new InvalidOperationException("Large marked transform did not undo.");
            checks.Add("Large marked transform retains marker and undoes in one step");
            foreach(var cancel in new[] { false,true })
            {
                piece=Create(cancel?10000:513);studio.LoadDocument(new ProjectDocument { Pieces=[piece] });studio.Select(piece.Id);
                using var reached=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
                studio.BeforeMarkedTransformForVerification=()=> { reached.Set();release.Wait(); };
                try
                {
                    transform.Invoke(studio,[mirror]);task=studio.MarkedTransformTaskForVerification!;Pump(()=>reached.IsSet);
                    if(cancel)
                        ((CancellationTokenSource)typeof(MainWindow).GetField("markedTransformCancellation",PrivateInstance)!.GetValue(studio)!).Cancel();
                    else studio.Commit("Kaynak değişimi",()=>studio.Document.Name="Değişti");
                    var expected=studio.Document.Clone();var undo=session.UndoCount;release.Set();Pump(()=>task.IsCompleted);task.GetAwaiter().GetResult();
                    if(!DocumentSession.ContentEquals(expected,studio.Document) || session.UndoCount!=undo)throw new InvalidOperationException("Cancelled/stale marked transform changed document.");
                    checks.Add(cancel ? "10000-point marked transform cancellation preserves document" : "Stale marked transform does not overwrite project changes");
                }
                finally { release.Set();studio.BeforeMarkedTransformForVerification=null; }
            }
        }
        finally { studio.BeforeMarkedTransformForVerification=null;SynchronizationContext.SetSynchronizationContext(previous); }
    }
}
