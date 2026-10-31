using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using TekstilCAD.FileFormats;

namespace TekstilCAD.MachineGateway;

/// <summary>
/// Conservative inspection of this application's HPGL dialect, for simulation only.
/// This does not establish device compatibility, qualification, or physical accuracy.
/// </summary>
public static class PlotterHpglPreflight
{
    public const int MaximumCharacters = 16 * 1024 * 1024;

    public sealed record Result(string Sha256, int ByteCount, int CommandCount);

    public static Result Inspect(HpglPreparedOutput output, PlotterCapabilityProfile profile, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(profile);
        if (!profile.IsComplete || output.UnitsPerMillimeter != profile.UnitsPerMillimeter)
            throw new InvalidOperationException("Profil eksik veya HPGL birimi eşleşmiyor.");
        return InspectText(output.Text, profile, cancellationToken);
    }

    private static Result InspectText(string text, PlotterCapabilityProfile profile, CancellationToken cancellationToken)
    {
        // ASCII is enforced below, so character count equals the transmitted byte count.
        if (text.Length == 0 || text.Length > MaximumCharacters || text.Length > profile.MaximumJobBytes)
            throw new InvalidDataException("HPGL boyutu geçersiz.");
        for (var i = 0; i < text.Length; i++)
        {
            if ((i & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
            if (text[i] > 127) throw new InvalidDataException("HPGL ASCII olmalıdır.");
        }
        // Compute integer bounds exactly from the binary profile values once per job.
        // Floating division can round a coordinate just outside the bed onto its edge.
        var maxX = Math.Min(profile.MaximumXPlotUnits, ExactMaximumCoordinate(profile.WidthMillimeters, profile.UnitsPerMillimeter));
        var maxY = Math.Min(profile.MaximumYPlotUnits, ExactMaximumCoordinate(profile.HeightMillimeters, profile.UnitsPerMillimeter));
        var offset = 0;
        var count = 0;
        // 0: IN, 1: SP1, 2: PA, 3: PU coordinates or SP0,
        // 4: PD coordinates, 5: PU (lift), 6: finished.
        var state = 0;
        var contours = 0;
        while (offset < text.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            while (offset < text.Length && (text[offset] == '\n' || text[offset] == '\r'))
            {
                if ((offset & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
                offset++;
            }
            if (offset == text.Length) break;
            var end = text.IndexOf(';', offset);
            if (end < 0 || end - offset + 1 > profile.MaximumCommandBytes)
                throw new InvalidDataException("HPGL komutu eksik veya profil sınırını aşıyor.");
            var command = text.AsSpan(offset, end - offset);
            count++;
            if (state == 0 && command.SequenceEqual("IN")) state = 1;
            else if (state == 1 && command.SequenceEqual("SP1")) state = 2;
            else if (state == 2 && command.SequenceEqual("PA")) state = 3;
            else if (state == 3 && command.SequenceEqual("SP0") && contours > 0) state = 6;
            else if (state == 3 && command.StartsWith("PU") && CoordinatesValid(command[2..], maxX, maxY, 1, 1, cancellationToken)) state = 4;
            else if (state == 4 && command.StartsWith("PD") && CoordinatesValid(command[2..], maxX, maxY, 1, int.MaxValue, cancellationToken)) state = 5;
            else if (state == 5 && command.SequenceEqual("PU")) { state = 3; contours++; }
            else throw new InvalidDataException("HPGL komut sırası veya koordinatlar desteklenmiyor.");
            offset = end + 1;
        }
        if (state != 6) throw new InvalidDataException("HPGL işi tamamlanmamış.");
        cancellationToken.ThrowIfCancellationRequested();
        var bytes = Encoding.ASCII.GetBytes(text);
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        cancellationToken.ThrowIfCancellationRequested();
        return new Result(hash, bytes.Length, count);
    }

    private static int ExactMaximumCoordinate(double millimeters, double unitsPerMillimeter)
    {
        static (BigInteger Significand, int Exponent) Decompose(double number)
        {
            var bits = (ulong)BitConverter.DoubleToInt64Bits(number);
            var exponentBits = (int)((bits >> 52) & 0x7ff);
            var fraction = bits & 0x000f_ffff_ffff_ffffUL;
            return exponentBits == 0
                ? (new BigInteger(fraction), -1074)
                : (new BigInteger(fraction | (1UL << 52)), exponentBits - 1075);
        }
        var (a, aExponent) = Decompose(millimeters);
        var (b, bExponent) = Decompose(unitsPerMillimeter);
        var exponent = aExponent + bExponent;
        var product = a * b;
        var maximum = exponent >= 0 ? product << exponent : product >> -exponent;
        return maximum >= int.MaxValue ? int.MaxValue : (int)maximum;
    }

    private static bool CoordinatesValid(ReadOnlySpan<char> values, int maxX, int maxY, int minimumPairs, int maximumPairs, CancellationToken cancellationToken)
    {
        var index = 0;
        var pairCount = 0;
        while (!values.IsEmpty)
        {
            if ((index & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
            var comma = values.IndexOf(',');
            var token = comma < 0 ? values : values[..comma];
            if (token.IsEmpty || !int.TryParse(token, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value) || value < 0)
                return false;
            var bound = (index & 1) == 0 ? maxX : maxY;
            if (value > bound) return false;
            index++;
            if ((index & 1) == 0) pairCount++;
            if (comma < 0) break;
            values = values[(comma + 1)..];
            if (values.IsEmpty) return false;
        }
        return (index & 1) == 0 && pairCount >= minimumPairs && pairCount <= maximumPairs;
    }
}
