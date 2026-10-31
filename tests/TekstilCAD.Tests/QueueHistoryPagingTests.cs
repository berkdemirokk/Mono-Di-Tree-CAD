using TekstilCAD.Core;
using TekstilCAD.MachineGateway;

namespace TekstilCAD.Tests;

public sealed class QueueHistoryPagingTests
{
    [Fact]
    public void DefaultFirstPageContinuesPastTenThousandInvalidDirectoriesToValidJob()
    {
        var path = Path.Combine(Path.GetTempPath(), "TekstilCAD-history-large-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(path);
            // Invalid names sort before any GUID, ensuring the valid job is
            // outside the first page independently of filesystem enumeration order.
            for (var i = 0; i < 10_001; i++)
                Directory.CreateDirectory(Path.Combine(path, "!invalid-" + i.ToString("D5")));
            var document = new ProjectDocument { Name = "Late", Pieces = [new PatternPiece { Closed = false, Points = [new(0, 0), new(10, 0)] }] };
            var queue = new SimulationQueue(path);
            Assert.False(queue.HistoryScanComplete);
            Assert.Equal(10_000, queue.HistoryScanIssues.Count);
            Assert.Empty(queue.Jobs);
            var created = queue.Prepare(document, new MachineProfile("test"), JobFormat.Dxf);
            var reader = new SimulationQueue(path);
            Assert.False(reader.HistoryScanComplete);
            Assert.Empty(reader.Jobs);
            reader.LoadNextHistoryPage();
            Assert.True(reader.HistoryScanComplete);
            Assert.Equal(10_001, reader.HistoryScanIssues.Count);
            Assert.Equal(created.Id, Assert.Single(reader.Jobs).Id);
        }
        finally { if (Directory.Exists(path)) Directory.Delete(path, true); }
    }

    [Fact]
    public void PagesExposeBadRecordsAndRestartFindsLaterJobsWithoutDuplicates()
    {
        var path = Path.Combine(Path.GetTempPath(), "TekstilCAD-history-" + Guid.NewGuid().ToString("N"));
        try
        {
            var queue = new SimulationQueue(path);
            var document = new ProjectDocument { Name = "Page", Pieces = [new PatternPiece { Closed = false, Points = [new(0, 0), new(10, 0)] }] };
            var first = queue.Prepare(document, new MachineProfile("test"), JobFormat.Dxf);
            Directory.CreateDirectory(Path.Combine(path, "bad-record"));
            var reader = new SimulationQueue(path);
            reader.RestartHistoryScan();
            reader.LoadNextHistoryPage(1);
            Assert.False(reader.HistoryScanComplete);
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            Assert.Throws<OperationCanceledException>(() => reader.LoadNextHistoryPage(1, cancelled.Token));
            while (!reader.HistoryScanComplete) reader.LoadNextHistoryPage(1);
            Assert.Contains(reader.HistoryScanIssues, issue => issue.Path.EndsWith("bad-record"));
            Assert.Contains(reader.Jobs, job => job.Id == first.Id);
            var later = queue.Prepare(document, new MachineProfile("test"), JobFormat.Dxf);
            Assert.DoesNotContain(reader.Jobs, job => job.Id == later.Id);
            reader.RestartHistoryScan();
            while (!reader.HistoryScanComplete) reader.LoadNextHistoryPage(1);
            Assert.Equal(2, reader.Jobs.Count);
            Assert.Contains(reader.Jobs, job => job.Id == later.Id);
            Assert.Contains(reader.HistoryScanIssues, issue => issue.Path.EndsWith("bad-record"));
        }
        finally { if (Directory.Exists(path)) Directory.Delete(path, true); }
    }

    [Fact]
    public void EnumerationFailureIsIncompleteAndCanBeRetried()
    {
        var path = Path.Combine(Path.GetTempPath(), "TekstilCAD-history-" + Guid.NewGuid().ToString("N"));
        try
        {
            File.WriteAllText(path, "not a directory");
            var queue = new SimulationQueue(path);
            Assert.False(queue.HistoryScanComplete);
            Assert.NotNull(queue.HistoryScanError);
            File.Delete(path);
            Directory.CreateDirectory(path);
            queue.RestartHistoryScan();
            queue.LoadNextHistoryPage(1);
            Assert.True(queue.HistoryScanComplete);
            Assert.Null(queue.HistoryScanError);
        }
        finally { if (Directory.Exists(path)) Directory.Delete(path, true); else if (File.Exists(path)) File.Delete(path); }
    }
}

