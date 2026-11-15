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
/// 1. a decision is waiting (a written command awaits Enter or Esc);
/// 2. a drawing is in progress (the next click matters more than anything else on screen);
/// 3. work is at risk (recovery warning);
/// 4. something just came in from another program and needs checking;
/// 5. a drawing tool is chosen and nothing is drawn yet;
/// 6. the project has no pieces (first launch wording differs);
/// 7. the built-in example is loaded (invites trying things, says nothing about saving);
/// 8. saving (changes on a known file; or never saved once the person has tried editing);
/// 9. a piece is selected (teach editing; then say "saved"; then a general edit hint);
/// 10. fallback: pieces exist and none is selected.
///
/// How contradictions are read: <see cref="GuideSituation.IsEmptyProject"/> or a non-positive
/// <see cref="GuideSituation.PieceCount"/> means "no pieces" and wins over a selection;
/// a pending drawing or preview wins over every project fact; the example rules apply only while no file is known;
/// <see cref="GuideSituation.IsFirstLaunch"/> only matters for an empty project.
/// </summary>
public static class GuideRules
{
    private readonly record struct Facts(
        bool NoPieces,
        bool Selected,
        bool Dirty,
        bool HasPath,
        bool Recovery,
        bool Imported,
        bool FirstLaunch,
        GuideTool Tool,
        bool PendingDrawing,
        bool PendingPreview,
        bool Example,
        bool Tried);

    private sealed record Rule(string Id, string Condition, string Sentence, string? Command, Func<Facts, bool> Applies);

    // Order matters: first match wins. Keep the table the only place where order is decided.
    private static readonly Rule[] Table =
    [
        new("pending-preview", "A written command awaits Enter, Uygula or Esc.",
            "Yukarıdaki cümleyi okuyun. Doğruysa Enter ya da Uygula; değilse Esc.", null,
            f => f.PendingPreview),

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
            "Eğriyi tıklayarak sürdürün; Esc vazgeçer.", null,
            f => f.PendingDrawing && f.Tool == GuideTool.Curve),
        new("drawing-arc", "Drawing in progress with the arc tool.",
            "Yayı tıklayarak sürdürün; Esc vazgeçer.", null,
            f => f.PendingDrawing && f.Tool == GuideTool.Arc),
        new("drawing-other", "Drawing in progress with any other or unknown tool.",
            "Çizimi bitirin ya da Esc ile vazgeçin.", null,
            f => f.PendingDrawing),

        new("recovery-notice", "A recovery warning is showing.",
            "Üstteki kurtarma uyarısını okuyun; çalışmanızı Kaydet ile koruyun.", "projeyi kaydet",
            f => f.Recovery),

        new("import-review", "Pieces were brought in from another program and not checked yet.",
            "Başka programdan alınan parçaların ölçülerini kontrol edin.", null,
            f => f.Imported),

        new("tool-line", "Line tool chosen, nothing drawn yet.",
            "Çizginin başlangıcına tıklayın.", "150 çizgi",
            f => f.Tool == GuideTool.Line),
        new("tool-rectangle", "Rectangle tool chosen, nothing drawn yet.",
            "Dikdörtgenin ilk köşesine tıklayın.", "120 x 80 dikdörtgen",
            f => f.Tool == GuideTool.Rectangle),
        new("tool-polyline", "Multi-segment line tool chosen, nothing drawn yet.",
            "Çizim alanında ilk yere tıklayın.", null,
            f => f.Tool == GuideTool.Polyline),
        new("tool-curve", "Curve tool chosen, nothing drawn yet.",
            "Eğrinin başlangıcına tıklayın.", null,
            f => f.Tool == GuideTool.Curve),
        new("tool-arc", "Arc tool chosen, nothing drawn yet.",
            "Yayın merkezine tıklayın.", null,
            f => f.Tool == GuideTool.Arc),

        new("empty-first-launch", "No pieces and this is the first launch.",
            "Kutuya yazın: 120 x 80 dikdörtgen. Ya da aşağıdan bir seçim yapın.", "120 x 80 dikdörtgen",
            f => f.NoPieces && f.FirstLaunch),
        new("empty-project", "No pieces, not the first launch.",
            "Henüz parça yok. Kutuya yazın: 120 x 80 dikdörtgen.", "120 x 80 dikdörtgen",
            f => f.NoPieces),

        new("example-pick", "Example loaded, no file yet, no piece selected.",
            "Örnek yüklendi. Denemek için bir parçaya tıklayın.", null,
            f => f.Example && !f.Selected),
        new("example-try", "Example loaded, no file yet, a piece is selected.",
            "Örneği denemek için yazın: 25 sağa taşı. Geri alabilirsiniz.", "25 sağa taşı",
            f => f.Example),

        new("save-changes", "Unsaved changes on a project that already has a file.",
            "Değişiklikleri saklamak için Kaydet'e basın.", "projeyi kaydet",
            f => f.Dirty && f.HasPath),
        new("save-first-time", "Unsaved work that has never been saved, and the person has already tried editing.",
            "Çalışmanız henüz kaydedilmedi. Kaybolmaması için Kaydet'e basın.", "projeyi kaydet",
            f => f.Dirty && f.Tried),

        new("selected-first-edit", "A piece is selected and the person has not tried editing yet.",
            "Ölçüyü değiştirmek için yazın: genişliği 95 yap.", "genişliği 95 yap",
            f => f.Selected && !f.Tried),
        new("selected-saved", "A piece is selected and everything is saved to a file.",
            "Çalışmanız kayıtlı. Yeni bir parça için yazın: 80 x 60 dikdörtgen.", "80 x 60 dikdörtgen",
            f => f.Selected && !f.Dirty && f.HasPath),
        new("selected-edit", "A piece is selected (any other state).",
            "Seçili parçayı taşımak için yazın: 25 sağa taşı.", "25 sağa taşı",
            f => f.Selected),

        new("pick-piece", "Fallback: pieces exist and none is selected.",
            "Bir parçaya tıklayarak seçin; sonra ölçüsünü değiştirin ya da taşıyın.", null,
            _ => true),
    ];

    /// <summary>The documented rule table in evaluation order; <see cref="NextStep"/> follows exactly this order.</summary>
    public static IReadOnlyList<GuideRuleInfo> Rules { get; } =
        Table.Select((r, i) => new GuideRuleInfo(i + 1, r.Id, r.Condition, r.Sentence, r.Command)).ToArray();

    /// <summary>Returns the one next step for the situation. Deterministic; the last rule always matches.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="situation"/> is null.</exception>
    public static GuideStep NextStep(GuideSituation situation)
    {
        ArgumentNullException.ThrowIfNull(situation);
        var noPieces = situation.IsEmptyProject || situation.PieceCount <= 0;
        var selected = situation.HasSelectedPiece && !noPieces;
        var facts = new Facts(
            NoPieces: noPieces,
            Selected: selected,
            Dirty: situation.HasUnsavedChanges,
            HasPath: situation.FilePathKnown,
            Recovery: situation.HasRecoveryNotice,
            Imported: situation.ImportedWithWarnings,
            FirstLaunch: situation.IsFirstLaunch,
            Tool: ClassifyTool(situation.ActiveToolName),
            PendingDrawing: situation.HasPendingDrawing,
            PendingPreview: situation.HasPendingPreview,
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

    /// <summary>Maps a tool name (display name or code identifier) to the kind the guide understands.</summary>
    public static GuideTool ClassifyTool(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return GuideTool.None;
        }
        var n = name.Trim();
        if (n.Length > 64)
        {
            return GuideTool.None;
        }
        return n switch
        {
            _ when Is(n, "Çizgi") => GuideTool.Line,
            _ when Is(n, "Nokta nokta çiz") || Is(n, "Çoklu çizgi") => GuideTool.Polyline,
            _ when Is(n, "Dikdörtgen") => GuideTool.Rectangle,
            _ when Is(n, "Eğri") || Is(n, "Bézier") || Is(n, "Bezier") => GuideTool.Curve,
            _ when Is(n, "Yay") => GuideTool.Arc,
            _ => GuideTool.None,
        };

        static bool Is(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }
}
