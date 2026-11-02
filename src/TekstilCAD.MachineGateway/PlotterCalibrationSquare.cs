using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.MachineGateway;

/// <summary>Creates only a local reference file. It never connects to a device.</summary>
public static class PlotterCalibrationSquare
{
    public sealed class Prepared
    {
        internal Prepared(HpglPreparedOutput output, Guid pieceId) { Output = output; PieceId = pieceId; }
        public HpglPreparedOutput Output { get; }
        public Guid PieceId { get; }
    }

    public static Prepared Prepare(double unitsPerMillimeter, double scale, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var profile = new MachineProfile("100 mm referans", unitsPerMillimeter, scale);
        profile.Validate();
        // The comparison in the UI is for 100 mm, so scaled references must not
        // silently be treated as calibration samples of that length.
        if (scale != 1) throw new ArgumentOutOfRangeException(nameof(scale), "100 mm referans için ölçek 1 olmalıdır.");
        var sideUnits = PlotterScaleCheck.ReferenceMillimeters * unitsPerMillimeter;
        if (!double.IsFinite(sideUnits) || Math.Round(sideUnits) != sideUnits)
            throw new ArgumentOutOfRangeException(nameof(unitsPerMillimeter), "100 mm kenar seçilen birimde tam sayı olmalıdır.");
        var piece = new PatternPiece { Name = "100 mm referans", Closed = true,
            Points = [new(0, 0), new(100, 0), new(100, 100), new(0, 100)] };
        var project = new ProjectDocument { Name = "100 mm referans", Pieces = [piece] };
        return new Prepared(HpglFormat.Prepare(project, unitsPerMillimeter, cancellationToken), piece.Id);
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
