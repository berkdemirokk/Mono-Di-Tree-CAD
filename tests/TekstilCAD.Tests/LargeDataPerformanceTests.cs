using System.Diagnostics;
using TekstilCAD.Core;
using Xunit;
using Xunit.Abstractions;

namespace TekstilCAD.Tests;

/// <summary>
/// Büyük-veri performans kanıtları. Bu testler hız garantisi vermez; sadece
/// ciddi bir regresyonu (sonsuz döngü, katlayıcı kötüleşme) yakalamak için çok
/// geniş üst sınırlar içerir ve sonuçları çıktıya yazar. Zamanlar makineye bağlıdır.
/// </summary>
public class LargeDataPerformanceTests(ITestOutputHelper output)
{
    private static Point2[] Ring(int count)
    {
        var points = new Point2[count];
        for (var i = 0; i < count; i++)
        {
            var angle = Math.Tau * i / count;
            points[i] = new Point2(1000 * Math.Cos(angle), 1000 * Math.Sin(angle));
        }
        return points;
    }

    private static (double Ms, long Bytes) Measure(Action action)
    {
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        var before = GC.GetAllocatedBytesForCurrentThread();
        var start = Stopwatch.GetTimestamp();
        action();
        return (Stopwatch.GetElapsedTime(start).TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void LargeRingValidationCompletesWithinLooseBound()
    {
        foreach (var count in new[] { 100, 1000, 4097 })
        {
            var points = Ring(count);
            var (ms, bytes) = Measure(() => LargeContourValidator.Validate(points));
            output.WriteLine($"LargeContourValidator.Validate ring-{count}: {ms:F2} ms, {bytes:#,0} allocated bytes");
            // Sadece çarpıksız bir çalışma; zaman bir başarış kriteri değildir.
            Assert.True(ms < 60_000, $"Ring-{count} doğrulaması çok uzun sürdü: {ms:F2} ms");
        }
    }

    [Fact]
    public void BroadphaseStatisticsAreConsistentAndDoWork()
    {
        foreach (var count in new[] { 4097, 1000 })
        {
            var points = Ring(count);
            var stats = LargeContourBroadphaseDiagnostic.Validate(points);
            output.WriteLine($"ring-{count}: pairs={stats.PairCount:#,0} rejected={stats.BroadphaseRejected:#,0} predicates={stats.PredicateCalls:#,0}");
            // Komşu kenar çiftleri (toplam count) sayaçları arttırmadan atlanır.
            Assert.Equal(stats.PairCount, stats.BroadphaseRejected + stats.PredicateCalls + points.Length);
            // Boş bir konturda broadphase hiç çalışmaz; en az bir çift değerlendirilmelidir.
            Assert.True(stats.PredicateCalls > 0 || stats.BroadphaseRejected > 0, $"ring-{count} broadphase hiç çift değerlendirmedi");
        }
    }

    [Fact]
    public void ClosedPerimeterScalesAcrossSizes()
    {
        var results = new List<(int Count, double Ms, double Perimeter)>();
        foreach (var count in new[] { 100, 1000, 4097 })
        {
            var points = Ring(count);
            var perimeter = ContourMeasurement.ClosedPerimeter(points);
            var (ms, _) = Measure(() => ContourMeasurement.ClosedPerimeter(points));
            results.Add((count, ms, perimeter));
            output.WriteLine($"ClosedPerimeter ring-{count}: {perimeter:F4} mm, {ms:F3} ms");
        }
        // Büyük konturda cevre uzunluğu sınırı içinde olmalı (doğrulama, zaman eşiği değil).
        Assert.All(results, r => Assert.InRange(r.Perimeter, 1000, 7000));
    }

    [Fact]
    public void TenThousandPointValidationIsOptIn()
    {
        // 10.000 nokta (saklama üst sınırı) yalnız belirtilirse çalışır; CI'de atlanır.
        if (Environment.GetEnvironmentVariable("TEKSTILCAD_LARGE_CONTOUR_BENCH") != "1")
        {
            output.WriteLine("10.000-noktalı testi atlandı (TEKSTILCAD_LARGE_CONTOUR_BENCH=1 gerekli).");
            return;
        }
        var points = Ring(10_000);
        var (ms, bytes) = Measure(() => LargeContourValidator.Validate(points));
        output.WriteLine($"LargeContourValidator.Validate ring-10000: {ms:F2} ms, {bytes:#,0} allocated bytes");
        Assert.True(ms < 120_000, $"Ring-10000 doğrulaması çok uzun sürdü: {ms:F2} ms");
    }
}
