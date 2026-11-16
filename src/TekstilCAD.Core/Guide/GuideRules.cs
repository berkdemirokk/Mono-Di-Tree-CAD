namespace TekstilCAD.Core.Guide;

/// <summary>The drawing tool the person has chosen, as far as the guide cares.</summary>
public enum GuideTool
{
    /// <summary>Select tool, an unknown name, or no name: no drawing tool is active.</summary>
    None = 0,
    Line,
    Polyline,
    Rectangle,
    Curve,
    Arc,
}

/// <summary>
/// Chooses the single "Sıradaki adım" sentence. Pure: no clock, no I/O, no state; the same
/// <see cref="GuideSituation"/> always gives the same <see cref="GuideStep"/>.
///
/// Rule order is explicit (see <see cref="Rules"/>). The first rule whose condition holds wins, and the last
/// rule always holds, so every situation maps to exactly one step. Order, from most to least urgent:
/// 1. a decision is waiting (a written command awaits an answer; wording depends on its kind);
/// 2. a drawing is in progress (the next click matters more than anything else on screen);
/// 3. a recovery warning is showing (read it; the guide never tells the person to save over a recovery);
/// 4. pieces came in from another program and none is selected yet (selecting one shows its size and clears this);
/// 5. a drawing tool is chosen and the project has no pieces yet;
/// 6. the project has no pieces (first launch wording differs);
/// 7. saving, once the person has tried editing (changes on a known file; or work never saved);
/// 8. a drawing tool is still active over existing pieces (how to leave it);
/// 9. the built-in example is loaded (invites trying things);
/// 10. a piece is selected (teach resizing only when that is possible; then "saved"; then a general edit hint);
/// 11. fallback: pieces exist and none is selected.
/// Saving is gated on <see cref="GuideSituation.HasTriedEditing"/> so that the guide teaches before it nags,
/// and so that a person who never edits is not told to save after every click.
///
/// How contradictions are read: <see cref="GuideSituation.HasNoPieces"/> wins over a selection;
/// a pending preview wins over a pending drawing, which wins over every project fact;
/// the example rules apply only while no file is known; <see cref="GuideSituation.IsFirstLaunch"/> only matters for
/// an empty project; <see cref="GuideSituation.SelectedCanResize"/> and the selection are ignored without pieces.
/// </summary>
public static class GuideRules
{
    private readonly record struct Facts(
        bool NoPieces,
        bool Selected,
        bool CanResize,
        bool Dirty,
        bool HasPath,
        bool Recovery,
        bool Imported,
        bool FirstLaunch,
        GuideTool Tool,
        bool PendingDrawing,
        bool PendingPreview,
        GuidePreviewKind PreviewKind,
        bool Example,
        bool Tried);

    private sealed record Rule(string Id, string Condition, string Sentence, string? Command, Func<Facts, bool> Applies);

    // Order matters: first match wins. Keep the table the only place where order is decided.
    private static readonly Rule[] Table =
    [
        new("pending-preview", "A written command awaits Enter, Uygula or Esc.",
            "Yukarıdaki cümleyi okuyun. Doğruysa Enter ya da Uygula; değilse Esc.", null,
            f => f.PendingPreview && f.PreviewKind is not (GuidePreviewKind.Danger or GuidePreviewKind.Question)),
        new("pending-preview-danger", "A dangerous command awaits a deliberate answer (Enter only moves focus).",
            "Yukarıdaki cümleyi okuyun. Emin değilseniz Esc; onaylamak için kırmızı düğmeye basın.", null,
            f => f.PendingPreview && f.PreviewKind == GuidePreviewKind.Danger),
        new("pending-preview-question", "A question with answer buttons awaits an answer.",
            "Yukarıdaki soruyu düğmelerden biriyle yanıtlayın.", null,
            f => f.PendingPreview && f.PreviewKind == GuidePreviewKind.Question),

        new("drawing-line", "Drawing in progress with the Line tool.",
            "Çizginin bitişine tıklayın; Esc vazgeçer.", null,
            f => f.PendingDrawing && f.Tool == GuideTool.Line),
        new("drawing-rectangle", "Drawing in progress with the Rectangle tool.",
            "Karşı köşeye tıklayın; Esc vazgeçer.", null,
            f => f.PendingDrawing && f.Tool == GuideTool.Rectangle),
        new("drawing-polyline", "Drawing in progress with the multi-segment line tool.",
            "Tıklayarak devam edin; Enter kapatır, Esc vazgeçer.", null,
            f => f.PendingDrawing && f.Tool == GuideTool.Polyline),
        new("drawing-curve", "Drawing in progress with the curve tool.",
            "Eğriyi sürdürmek için tıklayın; Esc vazgeçer.", null,
            f => f.PendingDrawing && f.Tool == GuideTool.Curve),
        new("drawing-arc", "Drawing in progress with the arc tool.",
            "Yayı sürdürmek için tıklayın; Esc vazgeçer.", null,
            f => f.PendingDrawing && f.Tool == GuideTool.Arc),
        new("drawing-other", "Drawing in progress with any other or unknown tool.",
            "Çizimi bitirin ya da Esc ile vazgeçin.", null,
            f => f.PendingDrawing),

        // Deliberately no "save" advice and no suggested command: saving over an incomplete recovery could
        // overwrite the only good copy. The warning in the header says what to do.
        new("recovery-notice", "A recovery warning is showing.",
            "Üstteki kurtarma uyarısını okuyun.", null,
            f => f.Recovery),

        // Clears itself: selecting a piece shows its size on the side card.
        new("import-review", "Pieces were brought in from another program and none is selected yet.",
            "Alınan parçaları kontrol edin: bir parçaya tıklayın, ölçüsü sağdaki kartta görünür.", null,
            f => f.Imported && !f.NoPieces && !f.Selected),

        new("tool-line", "No pieces yet; Line tool chosen.",
            "Çizginin başlangıcına tıklayın.", "150 çizgi",
            f => f.NoPieces && f.Tool == GuideTool.Line),
        new("tool-rectangle", "No pieces yet; Rectangle tool chosen.",
            "Dikdörtgenin ilk köşesine tıklayın.", "120 x 80 dikdörtgen",
            f => f.NoPieces && f.Tool == GuideTool.Rectangle),
        new("tool-polyline", "No pieces yet; multi-segment line tool chosen.",
            "Başlamak için çizim alanında bir yere tıklayın.", null,
            f => f.NoPieces && f.Tool == GuideTool.Polyline),
        new("tool-curve", "No pieces yet; curve tool chosen.",
            "Eğrinin başlangıcına tıklayın.", null,
            f => f.NoPieces && f.Tool == GuideTool.Curve),
        new("tool-arc", "No pieces yet; arc tool chosen.",
            "Yayın merkezine tıklayın.", null,
            f => f.NoPieces && f.Tool == GuideTool.Arc),

        new("empty-first-launch", "No pieces and this is the first launch.",
            "Kutuya yazın: 120 x 80 dikdörtgen. Ya da aşağıdan bir seçim yapın.", "120 x 80 dikdörtgen",
            f => f.NoPieces && f.FirstLaunch),
        new("empty-project", "No pieces, not the first launch.",
            "Kutuya yazın: 120 x 80 dikdörtgen.", "120 x 80 dikdörtgen",
            f => f.NoPieces),

        new("save-changes", "Unsaved changes on a project that already has a file, after the person has tried editing.",
            "Değişiklikleri kaydetmek için Kaydet'e basın.", "projeyi kaydet",
            f => f.Dirty && f.HasPath && f.Tried),
        new("save-first-time", "Unsaved work that has never been saved, after the person has tried editing.",
            "Çalışmanızı kaybetmemek için Kaydet'e basın.", "projeyi kaydet",
            f => f.Dirty && !f.HasPath && f.Tried),

        new("tool-leave", "Pieces exist but a drawing tool is still active.",
            "Düzenlemek için soldan Seç'e basın; çizmek için çizim alanına tıklayın.", null,
            f => f.Tool != GuideTool.None),

        new("example-pick", "Example loaded, no file yet, no piece selected.",
            "Örnek açıldı. Denemek için bir parçaya tıklayın.", null,
            f => f.Example && !f.Selected),
        new("example-try", "Example loaded, no file yet, a piece is selected.",
            "Örneği denemek için yazın: 25 sağa taşı. Yanlış olursa Geri al'a basın.", "25 sağa taşı",
            f => f.Example),

        new("selected-first-edit", "A piece that can be resized is selected and the person has not tried editing yet.",
            "Ölçüyü değiştirmek için yazın: genişliği 95 yap.", "genişliği 95 yap",
            f => f.Selected && f.CanResize && !f.Tried),
        new("selected-saved", "A piece is selected and everything is saved to a file.",
            "Çalışmanız kayıtlı. Yeni bir parça için yazın: 80 x 60 dikdörtgen.", "80 x 60 dikdörtgen",
            f => f.Selected && !f.Dirty && f.HasPath),
        new("selected-edit", "A piece is selected (any other state).",
            "Seçili parçayı taşımak için yazın: 25 sağa taşı.", "25 sağa taşı",
            f => f.Selected),

        new("pick-piece", "Fallback: pieces exist and none is selected.",
            "Değiştirmek istediğiniz parçaya tıklayın.", null,
            _ => true),
    ];

    /// <summary>The documented rule table in evaluation order; <see cref="NextStep"/> follows exactly this order.</summary>
    public static IReadOnlyList<GuideRuleInfo> Rules { get; } = Array.AsReadOnly(
        Table.Select((r, i) => new GuideRuleInfo(i + 1, r.Id, r.Condition, r.Sentence, r.Command)).ToArray());

    /// <summary>Returns the one next step for the situation. Deterministic; the last rule always matches.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="situation"/> is null.</exception>
    public static GuideStep NextStep(GuideSituation situation)
    {
        ArgumentNullException.ThrowIfNull(situation);
        var noPieces = situation.HasNoPieces;
        var selected = situation.HasSelectedPiece && !noPieces;
        var facts = new Facts(
            NoPieces: noPieces,
            Selected: selected,
            CanResize: selected && situation.SelectedCanResize,
            Dirty: situation.HasUnsavedChanges,
            HasPath: situation.FilePathKnown,
            Recovery: situation.HasRecoveryNotice,
            Imported: situation.ImportedWithWarnings,
            FirstLaunch: situation.IsFirstLaunch,
            Tool: ClassifyTool(situation.ActiveToolName),
            PendingDrawing: situation.HasPendingDrawing,
            PendingPreview: situation.HasPendingPreview,
            PreviewKind: situation.PendingPreviewKind,
            Example: situation.ExampleLoaded && !situation.FilePathKnown && !noPieces,
            Tried: situation.HasTriedEditing);
        foreach (var rule in Table)
        {
            if (rule.Applies(facts))
            {
                return new GuideStep(rule.Id, rule.Sentence, rule.Command);
            }
        }
        // Unreachable: the last rule always applies.
        throw new InvalidOperationException("Guide rule table has no fallback.");
    }

    /// <summary>
    /// Maps a tool name (display name or code identifier) to the kind the guide understands. Comparison ignores
    /// case and the Turkish dotted/dotless i difference, so "ÇİZGİ", "ÇIZGI" and "çizgi" all match.
    /// </summary>
    public static GuideTool ClassifyTool(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 64)
        {
            return GuideTool.None;
        }
        var n = Fold(name);
        return n switch
        {
            "çizgi" => GuideTool.Line,
            "nokta nokta çiz" or "çoklu çizgi" => GuideTool.Polyline,
            "dikdörtgen" => GuideTool.Rectangle,
            "eğri" or "bézier" or "bezier" => GuideTool.Curve,
            "yay" => GuideTool.Arc,
            _ => GuideTool.None,
        };
    }

    // Lower-case without culture rules, with İ, I, ı and i all read as 'i'; then the constants above are written the same way.
    private static string Fold(string text)
    {
        var chars = text.Trim().ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = chars[i] is 'İ' or 'I' or 'ı' ? 'i' : char.ToLowerInvariant(chars[i]);
        }
        return new string(chars);
    }
}
