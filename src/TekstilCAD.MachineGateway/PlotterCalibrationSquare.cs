using TekstilCAD.Core;
using TekstilCAD.FileFormats;
using System.Security.Cryptography;
using System.Text;

namespace TekstilCAD.MachineGateway;

/// <summary>Creates only a local reference file. It never connects to a device.</summary>
public static class PlotterCalibrationSquare
{
    public sealed class Prepared
    {
        internal Prepared(HpglPreparedOutput output, Guid pieceId, Guid? markerPieceId)
        { Output = output; PieceId = pieceId; MarkerPieceId = markerPieceId; ContentSha256=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(output.Text))); }
        public HpglPreparedOutput Output { get; }
        public Guid PieceId { get; }
        public Guid? MarkerPieceId { get; }
        /// <summary>Hash of the prepared UTF-8 file bytes; not a later disk recheck or a physical-output identity.</summary>
        public string ContentSha256 { get; }
    }

    public static Prepared Prepare(double unitsPerMillimeter, double scale, CancellationToken cancellationToken = default)
        => PrepareCore(unitsPerMillimeter, scale, false, cancellationToken);

    /// <summary>Adds a separate asymmetric outline inside the square for manual axis review.</summary>
    public static Prepared PrepareWithOrientationMarker(double unitsPerMillimeter, double scale, CancellationToken cancellationToken = default)
        => PrepareCore(unitsPerMillimeter, scale, true, cancellationToken);

    private static Prepared PrepareCore(double unitsPerMillimeter, double scale, bool orientationMarker, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var profile = new MachineProfile("100 mm referans", unitsPerMillimeter, scale);
        profile.Validate();
        // The comparison in the UI is for 100 mm, so scaled references must not
        // silently be treated as calibration samples of that length.
        if (scale != 1) throw new ArgumentOutOfRangeException(nameof(scale), "100 mm referans için ölçek 1 olmalıdır.");
        var sideUnits = PlotterScaleCheck.ReferenceMillimeters * unitsPerMillimeter;
        // Decimal values entered by a user (for example 0.07 units/mm) can
        // land one representable double away from the exact integer product.
        // Allow only two floating-point steps, not a physical-unit tolerance.
        var nearestUnits = Math.Round(sideUnits);
        var roundingStep = Math.Abs(Math.BitIncrement(sideUnits) - sideUnits);
        if (!double.IsFinite(sideUnits) || nearestUnits < 1 || Math.Abs(nearestUnits - sideUnits) > 2 * roundingStep)
            throw new ArgumentOutOfRangeException(nameof(unitsPerMillimeter), "100 mm kenar seçilen birimde tam sayı olmalıdır.");
        var piece = new PatternPiece { Name = "100 mm referans", Closed = true,
            Points = [new(0, 0), new(100, 0), new(100, 100), new(0, 100)] };
        PatternPiece? marker = null;
        if (orientationMarker)
        {
            // At coarser grids the mark can collapse or become ambiguous.
            if (unitsPerMillimeter < 0.1) throw new ArgumentOutOfRangeException(nameof(unitsPerMillimeter), "Yön işareti için birim/mm en az 0,1 olmalıdır.");
            marker = new PatternPiece { Name = "Yön inceleme işareti", Closed = true,
                Points = [new(20, 20), new(70, 20), new(20, 60)] };
        }
        var project = new ProjectDocument { Name = "100 mm referans", Pieces = marker is null ? [piece] : [piece, marker] };
        return new Prepared(HpglFormat.Prepare(project, unitsPerMillimeter, cancellationToken), piece.Id, marker?.Id);
    }

    /// <summary>Creates a new .plt/.hpgl file only; existing files are never replaced.</summary>
    public static void WriteNew(Prepared prepared, string path, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        cancellationToken.ThrowIfCancellationRequested();
        ExportDestination.ValidateExtension(path, 3);
        ProjectStorage.EnsureWritableDestination(path);
        var version = ProjectStorage.CaptureVersion(path, cancellationToken);
        if (version.Sha256 is not null) throw new IOException("Dosya zaten var; yeni bir dosya adı seçin.");
        HpglFormat.WritePrepared(prepared.Output, path, version, cancellationToken);
    }
}
