using System.Globalization;
using System.Text;
using TekstilCAD.Core;

namespace TekstilCAD.FileFormats;

/// <summary>Single-page vector inspection output, with a page size in physical PDF points.</summary>
public static class PdfFormat
{
    private const double PointsPerMillimeter = 72.0 / 25.4;
    private const double MarginMillimeters = 5;
    private const double MaximumPagePoints = 14400;
    private static string Number(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);

    public static void Export(ProjectDocument project, string path, ProjectFileVersion expectedVersion)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(expectedVersion);
        ProjectStorage.Validate(project);
        var copy = project.Clone();
        ProjectStorage.Validate(copy);
        ExperimentalOutputGuard.EnsureAllowed(copy.Pieces);
        NotchExportGuard.EnsureSupported(copy);
        GrainlineExportGuard.EnsureSupported(copy);
        if (copy.Pieces.Count == 0 || copy.Pieces.Any(p => p.Points.Count < 2))
            throw new InvalidDataException("PDF incelemesi için en az iki noktalı parçalar gerekir.");
        var points = copy.Pieces.SelectMany(p => p.Points).ToArray();
        var minX = points.Min(p => p.X) - MarginMillimeters;
        var maxY = points.Max(p => p.Y) + MarginMillimeters;
        var width = (points.Max(p => p.X) - minX + MarginMillimeters) * PointsPerMillimeter;
        var height = (maxY - points.Min(p => p.Y) + MarginMillimeters) * PointsPerMillimeter;
        if (!double.IsFinite(width) || !double.IsFinite(height) || width > MaximumPagePoints || height > MaximumPagePoints)
            throw new InvalidDataException("PDF inceleme sayfası çok büyük; bu sürümde çok sayfalı çıktı desteklenmez.");
        var content = new StringBuilder("0 0 0 RG\n0.5 w\n1 J 1 j\n");
        foreach (var piece in copy.Pieces)
        {
            string? first = null, previous = null;
            for (var i = 0; i < piece.Points.Count; i++)
            {
                var p = piece.Points[i];
                // PDF Y grows upward, while the CAD canvas Y grows downward.
                var point = Number((p.X - minX) * PointsPerMillimeter) + " " + Number((maxY - p.Y) * PointsPerMillimeter);
                if (previous == point)
                    throw new InvalidDataException("PDF koordinat hassasiyeti bir kenarı sıfıra indirdi; çıktı oluşturulmadı.");
                content.Append(point).Append(i == 0 ? " m\n" : " l\n");
                first ??= point; previous = point;
            }
            if (piece.Closed && piece.Points[0] != piece.Points[^1] && first == previous)
                throw new InvalidDataException("PDF koordinat hassasiyeti kapanış kenarını sıfıra indirdi; çıktı oluşturulmadı.");
            if (piece.Closed) content.Append("h\n");
            content.Append("S\n");
        }
        // Every character is ASCII, so character offsets equal PDF byte offsets.
        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int> { 0 };
        void Object(string value) { offsets.Add(pdf.Length); pdf.Append(offsets.Count - 1).Append(" 0 obj\n").Append(value).Append("\nendobj\n"); }
        Object("<< /Type /Catalog /Pages 2 0 R >>");
        Object("<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        Object($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {Number(width)} {Number(height)}] /Resources << >> /Contents 4 0 R >>");
        Object($"<< /Length {content.Length} >>\nstream\n{content}endstream");
        var xref = pdf.Length;
        pdf.Append("xref\n0 5\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1)) pdf.Append(offset.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        pdf.Append("trailer\n<< /Size 5 /Root 1 0 R >>\nstartxref\n").Append(xref).Append("\n%%EOF\n");
        ProjectStorage.AtomicWriteIfUnchanged(path, pdf.ToString(), expectedVersion);
    }
}
