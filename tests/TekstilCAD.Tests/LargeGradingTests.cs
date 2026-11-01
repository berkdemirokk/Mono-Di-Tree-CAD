using TekstilCAD.Core;
using TekstilCAD.FileFormats;
using TekstilCAD.Grading;
namespace TekstilCAD.Tests;
public class LargeGradingTests
{
    private static PatternPiece Circle(int count)
    {
        var points=Enumerable.Range(0,count).Select(i=> { var angle=Math.Tau*i/count;return new Point2(1000*Math.Cos(angle),1000*Math.Sin(angle)); }).ToList();
        return new PatternPiece { Name="Büyük kalıp",Points=points,Notches=[new NotchMark { VertexIndex=0,Position=points[0] }] };
    }
    [Fact]
    public void Grade4097PointContourRetainsMarkerAndIsUndoable()
    {
        var source=Circle(4097);var increments=Enumerable.Repeat(new Point2(0,0),source.Points.Count).ToArray();increments[0]=new Point2(.001,0);
        var original=new ProjectDocument { Pieces=[source.Clone()] };var outputs=GradingEngine.GenerateSizes(source,increments,[new GradeSize("L",1)]);
        var result=Assert.Single(outputs);Assert.Equal(source.Points.Count,result.Points.Count);Assert.Equal(new Point2(1000.001,0),result.Points[0]);
        Assert.Equal(result.Points[0],result.Notches!.Single().Position);Assert.True(DocumentSession.ContentEquals(original,new ProjectDocument { Pieces=[source] }));
        var session=new DocumentSession(original);Assert.True(session.Execute(doc=>doc.Pieces.Add(result),ProjectStorage.Validate));
        Assert.True(session.Undo());Assert.True(DocumentSession.ContentEquals(original,session.Document));
    }
    [Fact]
    public void OversizedAndMultiSizeWorkAreRejectedBeforeQuadraticValidation()
    {
        Assert.True((long)GradingEngine.MaximumGradePoints*GradingEngine.MaximumGradePoints<=GradingEngine.MaximumBatchValidationWork);
        Assert.True((long)(GradingEngine.MaximumGradePoints+1)*(GradingEngine.MaximumGradePoints+1)>GradingEngine.MaximumBatchValidationWork);
        var huge=new PatternPiece { Closed=false,Points=Enumerable.Repeat(new Point2(0,0),GradingEngine.MaximumGradePoints+1).ToList() };
        var hugeDeltas=Enumerable.Repeat(new Point2(0,0),huge.Points.Count).ToArray();
        Assert.Throws<ArgumentException>(()=>GradingEngine.Generate(huge,hugeDeltas,1,"L"));
        var p=new PatternPiece { Closed=false,Points=Enumerable.Repeat(new Point2(0,0),5001).ToList() };var deltas=Enumerable.Repeat(new Point2(0,0),5001).ToArray();
        var error=Assert.Throws<ArgumentException>(()=>GradingEngine.GenerateSizes(p,deltas,[new GradeSize("L",1),new GradeSize("XL",2)]));
        Assert.Contains("grubu çok büyük",error.Message);
        using var c=new CancellationTokenSource();c.Cancel();Assert.Throws<OperationCanceledException>(()=>GradingEngine.Generate(p,deltas,1,"L",c.Token));
    }
    [Fact]
    public void LargeStandaloneRuleRoundTripsWithinItsNewLimit()
    {
        var rule=new GradeRule { Increments=Enumerable.Repeat(new Point2(0,0),4097).ToList() };
        var decoded=GradeRule.FromJson(rule.ToJson());Assert.Equal(4097,decoded.Increments.Count);
    }
    [Fact]
    public void LongTranslationAndAreaObserveCancellationDuringTheirWork()
    {
        var points=Enumerable.Range(0,600).Select(i=>new Point2(i,i%3)).ToArray();
        using var translateCancel=new CancellationTokenSource();
        var translatedInput=new CancellingList(points,translateCancel,points.Length*2+20);
        Assert.Throws<OperationCanceledException>(()=>Geometry.Translate(translatedInput,1,0,translateCancel.Token));
        using var areaCancel=new CancellationTokenSource();var areaInput=new CancellingList(points,areaCancel,points.Length+100);
        Assert.Throws<OperationCanceledException>(()=>Geometry.SignedArea(areaInput,areaCancel.Token));
    }
    private sealed class CancellingList(Point2[] points,CancellationTokenSource cancellation,int threshold) : IReadOnlyList<Point2>
    {
        private int reads;
        public int Count=>points.Length;
        public Point2 this[int index] { get { if(++reads==threshold)cancellation.Cancel();return points[index]; } }
        public IEnumerator<Point2> GetEnumerator()=>((IEnumerable<Point2>)points).GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()=>GetEnumerator();
    }
}
