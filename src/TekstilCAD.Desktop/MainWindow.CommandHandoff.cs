using System.Globalization;
using TekstilCAD.Core.Commands;

namespace TekstilCAD.Desktop;

// The executor's hand-offs (C18, C42, C48-C60, C62, C64, C65) and the view, theme and text-size commands. Every hand-off calls the
// same guarded method the matching button calls; nothing here repeats what those methods check.
public partial class MainWindow
{
    internal Action<MeasuredNestingRequest>? MeasuredNestingForVerification { get; set; }

    private void OpenMeasuredProductionNesting(MeasuredNestingRequest request)
    {
        if(BlockForDxfImport())return;
        EnsurePropertyDraftsApplied();
        if(MeasuredNestingForVerification is { } verification) { verification(request);return; }
        new ProductionNestingWindow(this,this,request).ShowDialog();
    }

    /// <summary>What a hand-off runs and the sentence that tells it ended.</summary>
    private sealed record HandoffRoute(Action Run, string Done);

    /// <summary>Verification only: when set, the executor passes it a key ("Handoff.Target" or "File.Action") and the method it would run instead of running it (every one of them opens a dialog).</summary>
    internal Action<string, Action>? HandoffForVerification { get; set; }

    private HandoffRoute? HandoffRouteFor(HandoffCommand hand) => hand.Target switch
    {
        HandoffTarget.ImportDxf => new(ImportDxf, "DXF alma isteği gönderildi."),
        HandoffTarget.Export => new(Export, "Dışa aktarma isteği gönderildi."),
        HandoffTarget.ExportHpgl => new(ExportPlotter, "HPGL / PLT dışa aktarma isteği gönderildi."),
        HandoffTarget.ExportProjectA4 => new(ExportProjectA4, "Projenin A4 PDF çıktısı istendi."),
        HandoffTarget.ExportSelectedDxf => new(ExportSelectedDxf, "Seçili parçayı DXF yazma isteği gönderildi."),
        HandoffTarget.ExportSelectedA4 => new(ExportSelectedA4, "A4 çıktı isteği gönderildi."),
        HandoffTarget.Duplicate => new(DuplicateSelectedPiece, "Çoğaltma isteği gönderildi."),
        HandoffTarget.CoordinateDrawing => new(OpenCoordinateDrawing, "Koordinatla çizim penceresi kapandı."),
        HandoffTarget.Grainline => new(EditGrainline, "İplik yönü penceresi kapandı."),
        HandoffTarget.PieceMetadata => new(EditPieceMetadata, "Parça bilgileri penceresi kapandı."),
        HandoffTarget.MirrorCopy => new(OpenMirrorCopy, "Aynalı parça penceresi kapandı."),
        HandoffTarget.GroupMove => new(OpenGroupMove, "Toplu taşıma penceresi kapandı."),
        HandoffTarget.DesignRequest => new(OpenDesignRequest, "Elbise isteği penceresi kapandı."),
        HandoffTarget.RecoveryScan => new(RescanRecovery, "Kurtarma taraması başlatıldı."),
        HandoffTarget.About => new(OpenAbout, "Hakkında penceresi kapandı."),
        HandoffTarget.MachineCenter => new(OpenMachineCenter, "Makine dosyası penceresi kapandı."),
        HandoffTarget.TextileTools => new(() =>
        {
            if(hand.NestingRequest is { } request) OpenMeasuredProductionNesting(request);
            else OpenTextileToolsAt(hand.Tab is { } tab ? Math.Clamp(tab - 1, 0, 2) : 0,
                hand.Value is { } value && hand.Tab is >= 2 ? value.ToString("0.###", CultureInfo.InvariantCulture) : null);
        }, hand.NestingRequest is null ? "Tekstil araçları penceresi kapandı." : "Adetli pastal önizlemesi kapandı."),
        _ => null,
    };

    /// <summary>Runs the guarded method a button also runs (or, in verification, hands it to the seam) and reports the hand-off as done.</summary>
    private CommandOutcome RunHandoff(string key, Action run, string done)
    {
        if (HandoffForVerification is { } seam) { seam(key, run); return new(CommandResult.Done, done); }
        // The guarded method says in the status line when it refused or ended differently; never claim more than that.
        var before = statusWrites;
        run();
        return new(CommandResult.Done, statusWrites != before ? status.Text : done);
    }

    private void OpenDesignRequest() => new DesignRequestWindow(this).ShowDialog();

    private async void RescanRecovery() => await BeginRecoveryScan();

    /// <summary>"Ayrıntılı görünüm" and the layers hand-off: the same switch the F9 key and the header button use, with its guards.</summary>
    private CommandOutcome ApplyDetailedView(string done)
    {
        if (IsSimpleView)
        {
            ToggleSimpleView();
            if (IsSimpleView) return new(CommandResult.Refused, status.Text, CommandGuards.PendingWork);
        }
        return new(CommandResult.Done, done);
    }

    private CommandOutcome ApplyAllTools()
    {
        if (OpenAllToolsSheet()) return new(CommandResult.Done, "Tüm araçlar bölümü açıldı.");
        const string text = "Tüm araçlar bölümü Basit görünümde bulunur; Ayrıntılı görünümde bütün düğmeler zaten ekranda.";
        SetStatus(text);
        return new(CommandResult.Refused, text, CommandGuards.Unsupported);
    }

    /// <summary>One step up or down of the text size (100, 115, 130 percent); it shows in the simple view and is saved for the next start.</summary>
    private CommandOutcome ApplyTextScale(bool larger)
    {
        var current = simpleView?.TextScale ?? 100;
        var next = larger ? (current < 115 ? 115 : 130) : (current > 115 ? 115 : 100);
        if (next == current)
            return new(CommandResult.Done, larger ? "Yazılar zaten en büyük boyutta (%130)." : "Yazılar zaten en küçük boyutta (%100).");
        ApplySimpleTextScale(next);
        var saved = InterfaceSettings.SaveTextScale(next);
        return new(CommandResult.Done, $"Yazı boyutu %{next} oldu." + (IsSimpleView ? "" : " Basit görünümde uygulanır.") + (saved ? "" : " Seçim kaydedilemedi."));
    }
}
