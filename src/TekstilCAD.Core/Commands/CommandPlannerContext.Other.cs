using static TekstilCAD.Core.Commands.CommandPlannerText;

namespace TekstilCAD.Core.Commands;

// Commands that change no geometry in Core: the preview sentence is fixed text (FINAL_COMMANDS.md C24-C66) and the Desktop does the work.
internal sealed partial class PlannerContext
{
    // ---- undo and redo -----------------------------------------------------------------------------------------------------

    private PlanResult PlanUndo(UndoCommand c)
    {
        if (view.DrawingInProgress) return Refuse(Refusals.DrawingPending());
        if (view.UndoCount <= 0) return Refuse(Refusals.NothingToUndoRefusal());
        var steps = Math.Clamp(c.Steps, 1, Math.Min(10, view.UndoCount));
        var label = OneLine(view.LastUndoLabel) is { Length: > 0 } cleaned ? LowerFirst(cleaned) : null;
        var preview = steps > 1
            ? $"Son {steps} işlem geri alınacak."
            : label is null ? "Son işlem geri alınacak." : $"Son işlem geri alınacak: {label}.";
        return Of(Window(PlanKind.Undo, PlanClass.NoChange, new UndoCommand(steps), preview, "Geri alma", "Geri alındı. Yinelemek için “yinele” yazın."));
    }

    private PlanResult PlanRedo(RedoCommand c)
    {
        if (view.DrawingInProgress) return Refuse(Refusals.DrawingPending());
        if (view.RedoCount <= 0) return Refuse(Refusals.NothingToRedoRefusal());
        return Of(Window(PlanKind.Redo, PlanClass.NoChange, c, "Geri alınan işlem yinelenecek.", "Yineleme"));
    }

    // ---- view ----------------------------------------------------------------------------------------------------------------

    private PlanResult PlanView(ViewCommand c)
    {
        string? text = c.Action switch
        {
            ViewAction.FitAll => "Tüm parçalar ekrana sığdırılacak. Parçalar değişmez.",
            ViewAction.FitSelected => null,
            ViewAction.ZoomIn => "Görünüm yakınlaştırılacak. Parçalar değişmez.",
            ViewAction.ZoomOut => "Görünüm uzaklaştırılacak. Parçalar değişmez.",
            ViewAction.PanLeft => "Görünüm sola kaydırılacak. Parçalar değişmez.",
            ViewAction.PanRight => "Görünüm sağa kaydırılacak. Parçalar değişmez.",
            ViewAction.PanUp => "Görünüm yukarı kaydırılacak. Parçalar değişmez.",
            ViewAction.PanDown => "Görünüm aşağı kaydırılacak. Parçalar değişmez.",
            ViewAction.DetailedView => "Ayrıntılı görünüme geçilecek. Hiçbir parça değişmez.",
            ViewAction.AllTools => "Tüm araçlar bölümü açılacak. Hiçbir parça değişmez.",
            ViewAction.TextLarger => "Ekrandaki yazılar büyütülecek. Parçalar değişmez; yazılar büyüdükçe çalışma alanı küçülür.",
            ViewAction.TextSmaller => "Ekrandaki yazılar küçültülecek. Parçalar değişmez.",
            ViewAction.ThemeDark => "Koyu tema uygulanacak. Parçalar değişmez.",
            ViewAction.ThemeLight => "Açık tema uygulanacak. Parçalar değişmez.",
            _ => string.Empty,
        };
        if (text is "") return Refuse(Condition("Bu görünüm komutu tanınmıyor"));
        if (c.Action == ViewAction.FitSelected)
        {
            if (!TrySelected(out var piece, out var refusal)) return refusal;
            return Of(new Plan(PlanKind.View, PlanClass.NoChange, c, "Seçili parça ekrana sığdırılacak. Parçalar değişmez.",
                PieceStamp.Capture(view, selection, piece.Id, StampScope.Piece))
            {
                ActionName = "Görünüm",
                TargetPieceId = piece.Id,
                NeedsSelectedPiece = true,
            });
        }
        // Switching to the detailed view is refused while a drawing or typed drafts are open (g.1 of the specification).
        if (c.Action == ViewAction.DetailedView && (view.DrawingInProgress || view.HasUnappliedDrafts || view.FileOperationInProgress))
            return Refuse(view.DrawingInProgress ? Refusals.DrawingPending() : view.HasUnappliedDrafts ? Refusals.AreaDraftsPending() : Refusals.Busy());
        return Of(Window(PlanKind.View, PlanClass.NoChange, c, text!, "Görünüm"));
    }

    // ---- tools and drawing ------------------------------------------------------------------------------------------------------

    private PlanResult PlanTool(ToolCommand c)
    {
        if (view.DrawingInProgress) return Refuse(Refusals.DrawingPending());
        var text = c.Tool switch
        {
            DrawingTool.Select => "Fare aracı: Seç. Parçaya tıklayın; sürükleyerek taşıyın.",
            DrawingTool.Line => "Fare aracı: Çizgi. Başlangıç ve bitiş noktasına tıklayın.",
            DrawingTool.Polyline => "Fare aracı: Nokta nokta çiz. Noktalara tıklayın; Enter şekli kapatır, Esc çizimi iptal eder.",
            DrawingTool.Rectangle => "Fare aracı: Dikdörtgen. İki köşeye tıklayın.",
            DrawingTool.Bezier => "Fare aracı: Eğri. Dört noktaya tıklayın.",
            DrawingTool.Arc => "Fare aracı: Yay. Üç noktaya tıklayın.",
            _ => string.Empty,
        };
        if (text.Length == 0) return Refuse(Condition("Bu araç tanınmıyor"));
        return Of(Window(PlanKind.Tool, PlanClass.NoChange, c, text, "Araç seçimi"));
    }

    private PlanResult PlanFinishDrawing(FinishDrawingCommand c)
    {
        if (!view.DrawingInProgress)
            return Refuse(Condition("Sürmekte olan bir çizim yok"));
        switch (c.Action)
        {
            case FinishDrawingAction.Close:
                if (view.DrawingPointCount < 3)
                    return Refuse(Condition("Çizimi kapatmak için en az 3 nokta gerekir"));
                return Of(Window(PlanKind.FinishDrawing, PlanClass.Geometry, c, "Çizim kapatılacak ve parça olacak (en az 3 nokta gerekir).", "Çizim"));
            case FinishDrawingAction.FinishOpen:
                if (view.DrawingPointCount < 2)
                    return Refuse(Condition("Çizimi açık bırakmak için en az 2 nokta gerekir"));
                return Of(Window(PlanKind.FinishDrawing, PlanClass.Geometry, c, "Çizim uçları birleştirilmeden bitirilecek (en az 2 nokta gerekir).", "Çizim"));
            case FinishDrawingAction.Cancel:
                return Of(Window(PlanKind.FinishDrawing, PlanClass.Dangerous, c, "Sürmekte olan çizim silinecek; noktalar kaybolur.", "Çizim"));
            case FinishDrawingAction.RemoveLastPoint:
                return Of(Window(PlanKind.FinishDrawing, PlanClass.NoChange, c, "Son nokta silinecek.", "Çizim"));
            default:
                return Refuse(Condition("Bu çizim komutu tanınmıyor"));
        }
    }

    // ---- files ---------------------------------------------------------------------------------------------------------------------

    private PlanResult PlanFile(FileCommand c)
    {
        if (view.FileOperationInProgress) return Refuse(Refusals.Busy());
        var fileName = OneLine(view.FileName, 120);
        var named = fileName.Length > 0;
        var dirty = view.HasUnsavedChanges;
        switch (c.Action)
        {
            case FileAction.Save:
                return Of(Window(PlanKind.File, PlanClass.Window, c,
                    named ? $"Proje kaydedilecek: {fileName}" : "Proje kaydedilecek; dosya adı sorulacak.", "Kaydetme"));
            case FileAction.SaveAs:
                return Of(Window(PlanKind.File, PlanClass.Window, c, "Proje yeni bir adla kaydedilecek; dosya adı sorulacak.", "Kaydetme"));
            case FileAction.SaveCopy:
                return Of(Window(PlanKind.File, PlanClass.Window, c, "Açık projeyi değiştirmeden yeni bir kopya kaydedilecek; dosya adı sorulacak.", "Kaydetme"));
            case FileAction.Open:
                return Of(Window(PlanKind.File, dirty ? PlanClass.Dangerous : PlanClass.Window, c,
                    dirty ? "Kayıtlı bir proje açılacak; dosya seçmeniz istenecek. Kaydedilmemiş değişiklikleriniz var; önce kaydetmek isteyip istemediğiniz sorulacak."
                          : "Kayıtlı bir proje açılacak; dosya seçmeniz istenecek.", "Açma"));
            case FileAction.New:
                return Of(Window(PlanKind.File, dirty ? PlanClass.Dangerous : PlanClass.Window, c,
                    dirty ? "Kaydedilmemiş değişiklikleriniz var. Yeni projeye geçmeden önce kaydetmek isteyip istemediğiniz sorulacak."
                          : "Yeni, boş bir proje başlayacak.", "Yeni proje"));
            default:
                return Refuse(Condition("Bu dosya komutu tanınmıyor"));
        }
    }

    // ---- hand-offs to windows ----------------------------------------------------------------------------------------------------

    private PlanResult PlanHandoff(HandoffCommand c)
    {
        string text;
        var needsPiece = false;
        var planClass = PlanClass.Window;
        switch (c.Target)
        {
            case HandoffTarget.Duplicate:
                needsPiece = true;
                text = "Çoğaltma penceresi açılacak. Kopyanın nasıl oluşacağını orada seçersiniz.";
                break;
            case HandoffTarget.CoordinateDrawing:
                text = "Koordinat penceresi açılacak. Noktanın yerini soldan ve yukarıdan milimetre olarak yazarsınız.";
                break;
            case HandoffTarget.ImportDxf:
                text = "Başka bir programdan DXF dosyası alınacak. Dosyayı ve ölçü birimini siz seçersiniz; ölçüleri kontrol edene kadar projeniz değişmez.";
                break;
            case HandoffTarget.Export:
                planClass = PlanClass.Dangerous;
                text = "Dışa aktarma penceresi açılacak. Dosya yalnız inceleme içindir; üretim onayı değildir ve makineye gönderilmez.";
                break;
            case HandoffTarget.ExportProjectA4:
                if (Doc.Pieces.Count == 0) return Refuse(Refusals.NoPiecesYet());
                planClass = PlanClass.Dangerous;
                text = "Tüm proje gerçek boyutlu A4 PDF sayfalarına yazılacak. Dosyayı açıp %100 ölçekte yazdırabilirsiniz.";
                break;
            case HandoffTarget.ExportSelectedDxf:
                needsPiece = true;
                planClass = PlanClass.Dangerous;
                text = "Seçili parça ayrı bir DXF dosyasına yazılacak. Dosya yalnız inceleme içindir; üretim onayı değildir.";
                break;
            case HandoffTarget.ExportSelectedA4:
                needsPiece = true;
                planClass = PlanClass.Dangerous;
                text = "Seçili parça gerçek boyutlu A4 sayfalarına PDF dosyası olarak yazılacak. Program yazıcıya göndermez; dosya yalnız inceleme içindir.";
                break;
            case HandoffTarget.TextileTools:
                needsPiece = c.Tab != 3;
                if (c.Tab == 3 && Doc.Pieces.Count == 0) return Refuse(Refusals.NoPiecesYet());
                text = TextileToolsText(c);
                break;
            case HandoffTarget.MachineCenter:
                text = "Makine dosyası penceresi açılacak. Makineye bağlanmaz; yalnız deneme dosyası hazırlar.";
                break;
            case HandoffTarget.Grainline:
                needsPiece = true;
                text = "İplik yönü penceresi açılacak.";
                break;
            case HandoffTarget.PieceMetadata:
                needsPiece = true;
                text = "Parça bilgileri penceresi açılacak (rol, adet, malzeme).";
                break;
            case HandoffTarget.MirrorCopy:
                needsPiece = true;
                text = "Yanına aynalı parça penceresi açılacak. Önizlemeyi orada görürsünüz.";
                break;
            case HandoffTarget.GroupMove:
                if (Doc.Pieces.Count < 2) return Refuse(Condition("Birden fazla parçayı taşımak için en az iki parça gerekir"));
                text = "Birden fazla parçayı taşıma penceresi açılacak.";
                break;
            case HandoffTarget.Layers:
                planClass = PlanClass.NoChange;
                text = "Katmanlar Ayrıntılı görünümde yönetilir. Ayrıntılı görünüme geçilecek.";
                break;
            case HandoffTarget.About:
                text = "Hakkında penceresi açılacak.";
                break;
            case HandoffTarget.DesignRequest:
                text = "Elbise isteği penceresi açılacak. İstek hazırlar; kalıp oluşturmaz.";
                break;
            case HandoffTarget.RecoveryScan:
                planClass = PlanClass.NoChange;
                text = "Kurtarma kayıtları yeniden taranacak; açık proje değişmez.";
                break;
            default:
                return Refuse(Condition("Bu pencere komutu tanınmıyor"));
        }
        if (needsPiece)
        {
            if (!TrySelected(out var piece, out var refusal)) return refusal;
            return Of(new Plan(PlanKind.Window, planClass, c, text, PieceStamp.Capture(view, selection, piece.Id, StampScope.Piece))
            {
                ActionName = "Pencere",
                TargetPieceId = piece.Id,
                NeedsSelectedPiece = true,
            });
        }
        return Of(Window(PlanKind.Window, planClass, c, text, "Pencere"));
    }

    private static string TextileToolsText(HandoffCommand c)
    {
        switch (c.Tab)
        {
            case 1:
                return "Beden oluşturmak için serileme kuralı gerekir. “Beden oluştur…” penceresi açılacak.";
            case 2:
                return c.Value is { } seam && double.IsFinite(seam)
                    ? $"Dikiş payı şimdilik “Dikiş payı ver…” penceresinde yapılır. Pencere {MmCm(seam)} değeriyle açılacak; önizlemeyi görüp siz uygularsınız."
                    : "Dikiş payı şimdilik “Dikiş payı ver…” penceresinde yapılır. Pencere açılacak; önizlemeyi görüp siz uygularsınız.";
            case 3:
                var basic = "Pastal şimdilik “Pastal yerleştir…” penceresinde yapılır. Pencere açılacak; bütün parçaların yeri değiştirilmeden önce orada sorulur.";
                return c.Value is { } width && double.IsFinite(width) ? basic + $" Kumaş eni {MmCm(width)} olarak girilecek." : basic;
            default:
                return "Tekstil araçları penceresi açılacak.";
        }
    }
}
