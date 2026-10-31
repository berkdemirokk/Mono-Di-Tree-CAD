using System.Numerics;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Tests;

// Experimental bridge only: proves exact equality of two directed grid boundaries,
// allowing strictly ordered collinear subdivisions. It does not prove that either
// boundary is an offset, simple, or preserves features. No floating tolerance.
internal static class SeamExactOutputBridge
{
    internal static bool Matches(IReadOnlyList<GridPoint> indexedCandidate,
        IReadOnlyList<GridPoint> actual, long maxChecks, CancellationToken cancellationToken)
    {
        int n = indexedCandidate.Count, m = actual.Count;
        if (n < 3 || n > 512 || m < n || m > 2048 || maxChecks <= 0) return false;
        long checks = 0;
        // Only rotations are allowed: reversal changes the indexed orientation.
        for (int start = 0; start < m; start++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (actual[start] != indexedCandidate[0]) continue;
            int offset = 0;
            bool ok = true;
            for (int i = 0; i < n && ok; i++)
            {
                var a = indexedCandidate[i]; var b = indexedCandidate[(i + 1) % n];
                BigInteger dx = (BigInteger)b.X - a.X, dy = (BigInteger)b.Y - a.Y;
                BigInteger length = dx * dx + dy * dy, previous = 0;
                if (length.IsZero) { ok = false; break; }
                bool foundEnd = false;
                while (++offset <= m)
                {
                    if (++checks > maxChecks) return false;
                    cancellationToken.ThrowIfCancellationRequested();
                    var point = actual[(start + offset) % m];
                    BigInteger x = (BigInteger)point.X - a.X, y = (BigInteger)point.Y - a.Y;
                    BigInteger dot = x * dx + y * dy;
                    if (x * dy != y * dx || dot <= previous || dot > length)
                    { ok = false; break; }
                    previous = dot;
                    if (dot == length) { foundEnd = true; break; }
                }
                if (!foundEnd) ok = false;
            }
            if (ok && offset == m) return true;
        }
        return false;
    }
}
