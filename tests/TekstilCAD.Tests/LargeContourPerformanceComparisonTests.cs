using System.Diagnostics;
using TekstilCAD.Core;
using Xunit.Abstractions;

namespace TekstilCAD.Tests;

internal sealed class LargeContourBenchmarkAttribute : FactAttribute
{
    public LargeContourBenchmarkAttribute()
    {
        if (Environment.GetEnvironmentVariable("TEKSTILCAD_LARGE_CONTOUR_BENCH") != "1")
            Skip = "Set TEKSTILCAD_LARGE_CONTOUR_BENCH=1 to run the opt-in performance comparison.";
    }
}

// Opt in explicitly on the designated benchmark machine; never a timing gate in CI.
public sealed class LargeContourPerformanceComparisonTests(ITestOutputHelper output)
{
    [LargeContourBenchmark]
    public void ComparePreviousPairScanWithCurrentValidator()
    {
        output.WriteLine($"Runtime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}; OS: {System.Runtime.InteropServices.RuntimeInformation.OSDescription}; architecture: {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}; processors: {Environment.ProcessorCount}");
        static Point2[] Ring(int count) => Enumerable.Range(0, count).Select(i =>
        {
            var angle = Math.Tau * i / count;
            return new Point2(1000 * Math.Cos(angle), 1000 * Math.Sin(angle));
        }).ToArray();
        static Point2[] Comb(int teeth)
        {
            var width = 4 * teeth;
            var points = new List<Point2> { new(0, 0), new(width, 0), new(width, 100) };
            for (var x = width - 2; x >= 2; x -= 4)
            {
                points.Add(new(x, 100)); points.Add(new(x, 10));
                points.Add(new(x - 2, 10)); points.Add(new(x - 2, 100));
            }
            return points.ToArray();
        }
        foreach (var (name, points) in new[] { ("ring-4097", Ring(4097)), ("ring-10000", Ring(10000)), ("comb-9999", Comb(2499)) })
        {
            Previous(points); LargeContourValidator.Validate(points);
            var oldTimes = new List<double>(10); var newTimes = new List<double>(10);
            var oldBytes = new List<long>(10); var newBytes = new List<long>(10);
            for (int run = 0; run < 10; run++)
            {
                void Measure(Action action, List<double> values, List<long> allocated)
                {
                    var before = GC.GetAllocatedBytesForCurrentThread();
                    var start = Stopwatch.GetTimestamp(); action();
                    values.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                    allocated.Add(GC.GetAllocatedBytesForCurrentThread() - before);
                }
                if ((run & 1) == 0) { Measure(() => Previous(points), oldTimes, oldBytes); Measure(() => LargeContourValidator.Validate(points), newTimes, newBytes); }
                else { Measure(() => LargeContourValidator.Validate(points), newTimes, newBytes); Measure(() => Previous(points), oldTimes, oldBytes); }
            }
            oldTimes.Sort(); newTimes.Sort();
            output.WriteLine($"{name} ({points.Length} points); previous pair scan median {(oldTimes[4] + oldTimes[5]) / 2:F2} ms; current full validation median {(newTimes[4] + newTimes[5]) / 2:F2} ms; previous range {oldTimes[0]:F2}–{oldTimes[^1]:F2}; current range {newTimes[0]:F2}–{newTimes[^1]:F2}");
            output.WriteLine($"Managed thread allocations (not peak memory): previous scan {oldBytes.Min()}–{oldBytes.Max()} bytes; current full validation {newBytes.Min()}–{newBytes.Max()} bytes.");
        }
    }

    // Reconstructed previous hot loop, not the original compiled implementation.
    // Snapshot, uniqueness, area and cancellation checks are omitted. Timings
    // include JIT/code-placement effects and cannot certify exact before/after speed.
    private static void Previous(Point2[] points)
    {
        static int Orientation(Point2 a, Point2 b, Point2 p)
        {
            var edge = b - a; var relative = p - a;
            var cross = edge.X * relative.Y - edge.Y * relative.X;
            var tolerance = Geometry.Epsilon * Math.Max(1, Geometry.Distance(a, b));
            return cross > tolerance ? 1 : cross < -tolerance ? -1 : 0;
        }
        static bool Separated(double a, double b, double c, double d)
        {
            var lo1 = Math.Min(a, b); var hi1 = Math.Max(a, b);
            var lo2 = Math.Min(c, d); var hi2 = Math.Max(c, d);
            return (lo2 > hi1 + Geometry.Epsilon && hi1 < lo2 - Geometry.Epsilon) ||
                (lo1 > hi2 + Geometry.Epsilon && hi2 < lo1 - Geometry.Epsilon);
        }
        for (int i = 0; i < points.Length; i++)
        {
            var a = points[i]; var b = points[(i + 1) % points.Length];
            if (Geometry.Distance(a, b) <= Geometry.Epsilon) throw new ArgumentException();
            for (int j = i + 1; j < points.Length; j++)
            {
                var c = points[j]; var d = points[(j + 1) % points.Length];
                if (j == i + 1 || i == 0 && j == points.Length - 1)
                {
                    var common = j == i + 1 ? b : a;
                    var first = j == i + 1 ? a : b;
                    var second = j == i + 1 ? d : c;
                    var f = first - common; var s = second - common;
                    if (Orientation(first, common, second) == 0 && f.X * s.X + f.Y * s.Y > Geometry.Epsilon) throw new ArgumentException();
                    continue;
                }
                var separate = Separated(a.X, b.X, c.X, d.X) || Separated(a.Y, b.Y, c.Y, d.Y);
                if (separate && Orientation(a, b, c) * Orientation(a, b, d) >= 0) continue;
                if (Geometry.SegmentsIntersect(a, b, c, d)) throw new ArgumentException();
            }
        }
    }
}
