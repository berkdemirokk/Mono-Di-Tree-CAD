using System.Collections;
using System.Reflection;
using System.Text;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class DxfImportCancellationTests
{
    private const string Sample = "0\nSECTION\n2\nENTITIES\n0\nLINE\n8\nA\n10\n0\n20\n0\n11\n10\n21\n0\n0\nENDSEC\n0\nEOF\n";

    [Fact]
    public void AlreadyCancelledStreamIsNotReadOrDisposed()
    {
        using var source = new MemoryStream(Encoding.UTF8.GetBytes(Sample));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => DxfFormat.Import(source, "test", 1, cancellation.Token));
        Assert.Equal(0, source.Position);
        Assert.True(source.CanRead);
    }

    [Fact]
    public void CancellationDuringReadStopsImportAndLeavesSourceOpenAndUnchanged()
    {
        var bytes = Encoding.UTF8.GetBytes(Sample);
        using var cancellation = new CancellationTokenSource();
        using var source = new CancellingStream(bytes, cancellation);

        Assert.Throws<OperationCanceledException>(() => DxfFormat.Import(source, "test", 1, cancellation.Token));
        Assert.True(source.CanRead);
        Assert.Equal(bytes, source.ToArray());
    }

    [Fact]
    public void NonCancelledOverloadMatchesExistingImport()
    {
        var bytes = Encoding.UTF8.GetBytes(Sample);
        using var first = new MemoryStream(bytes);
        using var second = new MemoryStream(bytes);
        var existing = DxfFormat.Import(first, "test", 1);
        var cancellable = DxfFormat.Import(second, "test", 1, CancellationToken.None);
        Assert.Equal(existing.Warnings, cancellable.Warnings);
        Assert.Equal(existing.Units, cancellable.Units);
        Assert.Equal(existing.Project.Pieces.Count, cancellable.Project.Pieces.Count);
        Assert.Equal(existing.Project.Pieces[0].Points, cancellable.Project.Pieces[0].Points);
        Assert.Equal(bytes, second.ToArray());
    }

    [Fact]
    public void LineSweepObservesCancellationDuringCandidateScan()
    {
        using var cancellation = new CancellationTokenSource();
        var edges = new CancellingEdges(cancellation);
        var method = typeof(DxfFormat).GetMethod("HasAmbiguousLineConnectionsCancellable", BindingFlags.NonPublic | BindingFlags.Static)!;
        var exception = Assert.Throws<TargetInvocationException>(() => method.Invoke(null, [edges, cancellation.Token]));
        Assert.IsType<OperationCanceledException>(exception.InnerException);
        Assert.True(edges.CancelledDuringCandidateScan);
    }

    private sealed class CancellingStream(byte[] bytes, CancellationTokenSource cancellation) : MemoryStream(bytes)
    {
        public override int Read(byte[] buffer, int offset, int count)
        {
            var result = base.Read(buffer, offset, count);
            cancellation.Cancel();
            return result;
        }
    }

    private sealed class CancellingEdges(CancellationTokenSource cancellation) : IReadOnlyList<(Point2 A, Point2 B)>
    {
        private const int EdgeCount = 40;
        private int accesses;
        public bool CancelledDuringCandidateScan { get; private set; }
        public int Count => EdgeCount;
        public (Point2 A, Point2 B) this[int index]
        {
            get
            {
                // Ordering reads each endpoint eight times per edge. Later indexed
                // access comes from the candidate-pair scan.
                if (++accesses == EdgeCount * 8 + 4)
                {
                    CancelledDuringCandidateScan = true;
                    cancellation.Cancel();
                }
                return (new Point2(0, index * 2), new Point2(100, 100 + index * 2));
            }
        }
        public IEnumerator<(Point2 A, Point2 B)> GetEnumerator()
        {
            for (var i = 0; i < Count; i++)
                yield return (new Point2(0, i * 2), new Point2(100, 100 + i * 2));
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
