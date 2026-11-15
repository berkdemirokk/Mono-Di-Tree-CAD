using TekstilCAD.Core.Guide;

namespace TekstilCAD.Tests.Guide;

public sealed class GuideRulesTests
{
    private static readonly GuideSituation Empty = new();
    // Two pieces, nothing selected, everything quiet.
    private static readonly GuideSituation Some = new() { PieceCount = 2 };
    // A selected piece that cannot be resized by writing (not a plain rectangle).
    private static readonly GuideSituation Picked = Some with { HasSelectedPiece = true };
    // A selected plain rectangle.
    private static readonly GuideSituation Rect = Picked with { SelectedCanResize = true };

    public static TheoryData<string, GuideSituation, string> Situations => new()
    {
        // --- no pieces -----------------------------------------------------------------------------------
        { "default situation is an empty project", Empty, "empty-project" },
        { "empty and first launch", Empty with { IsFirstLaunch = true }, "empty-first-launch" },
        { "empty flag only", new GuideSituation { IsEmptyProject = true }, "empty-project" },
        { "zero pieces but a selection is claimed", new GuideSituation { PieceCount = 0, HasSelectedPiece = true, SelectedCanResize = true }, "empty-project" },
        { "negative piece count", new GuideSituation { PieceCount = -5 }, "empty-project" },
        { "int.MinValue piece count", new GuideSituation { PieceCount = int.MinValue }, "empty-project" },
        { "empty flag beats a positive count", Rect with { PieceCount = 3, IsEmptyProject = true }, "empty-project" },
        { "empty and first launch with a known file", Empty with { IsFirstLaunch = true, FilePathKnown = true }, "empty-first-launch" },
        { "empty after deleting everything on a saved file", Empty with { HasUnsavedChanges = true, FilePathKnown = true, HasTriedEditing = true }, "empty-project" },
        { "empty with the Select tool", Empty with { ActiveToolName = "Seç" }, "empty-project" },
        { "empty with tried editing", Empty with { HasTriedEditing = true }, "empty-project" },
        { "empty with an import flag and nothing to check", Empty with { ImportedWithWarnings = true }, "empty-project" },

        // --- pieces, no selection ------------------------------------------------------------------------
        { "pieces and nothing selected", Some, "pick-piece" },
        { "pieces, nothing selected, first launch", Some with { IsFirstLaunch = true }, "pick-piece" },
        { "very large piece count, nothing selected", new GuideSituation { PieceCount = int.MaxValue }, "pick-piece" },
        { "unselected, tried editing, clean, no file", Some with { HasTriedEditing = true }, "pick-piece" },
        { "unselected, tried editing, clean, saved", Some with { HasTriedEditing = true, FilePathKnown = true }, "pick-piece" },
        { "unselected, unsaved, no file, never edited", Some with { HasUnsavedChanges = true }, "pick-piece" },
        { "unselected, unsaved, file known, never edited (no nagging yet)", Some with { HasUnsavedChanges = true, FilePathKnown = true }, "pick-piece" },
        { "overlong tool name is not a tool", Some with { ActiveToolName = new string('Ç', 1000) }, "pick-piece" },
        { "unknown tool name is not a tool", Some with { ActiveToolName = "Boya" }, "pick-piece" },
        { "blank tool name is not a tool", Some with { ActiveToolName = "   " }, "pick-piece" },

        // --- a piece is selected -------------------------------------------------------------------------
        { "selected rectangle, never edited, clean", Rect, "selected-first-edit" },
        { "selected rectangle, never edited, unsaved, no file", Rect with { HasUnsavedChanges = true }, "selected-first-edit" },
        { "selected rectangle, never edited, saved", Rect with { FilePathKnown = true }, "selected-first-edit" },
        { "selected rectangle with the Select tool", Rect with { ActiveToolName = "Seç" }, "selected-first-edit" },
        { "selected rectangle, first launch", Rect with { IsFirstLaunch = true }, "selected-first-edit" },
        { "selected rectangle with a huge piece count", new GuideSituation { PieceCount = int.MaxValue, HasSelectedPiece = true, SelectedCanResize = true }, "selected-first-edit" },
        { "selected rectangle, never edited, unsaved, file known (teach before nagging)", Rect with { HasUnsavedChanges = true, FilePathKnown = true }, "selected-first-edit" },
        { "selected piece that cannot be resized is not told to resize", Picked, "selected-edit" },
        { "selected non-rectangle, unsaved, no file, never edited", Picked with { HasUnsavedChanges = true }, "selected-edit" },
        { "selected non-rectangle, saved, never edited", Picked with { FilePathKnown = true }, "selected-saved" },
        { "resize flag without a selection is ignored", Some with { SelectedCanResize = true }, "pick-piece" },
        { "selected, edited, clean, saved", Rect with { HasTriedEditing = true, FilePathKnown = true }, "selected-saved" },
        { "selected, edited, clean, no file", Rect with { HasTriedEditing = true }, "selected-edit" },
        { "selected, edited, clean, no file, first launch", Picked with { HasTriedEditing = true, IsFirstLaunch = true }, "selected-edit" },

        // --- saving (only after the person has tried editing) --------------------------------------------
        { "selected, edited, unsaved, no file", Rect with { HasTriedEditing = true, HasUnsavedChanges = true }, "save-first-time" },
        { "unselected, edited, unsaved, no file", Some with { HasTriedEditing = true, HasUnsavedChanges = true }, "save-first-time" },
        { "selected, edited, unsaved, file known", Rect with { HasTriedEditing = true, HasUnsavedChanges = true, FilePathKnown = true }, "save-changes" },
        { "unselected, edited, unsaved, file known", Some with { HasTriedEditing = true, HasUnsavedChanges = true, FilePathKnown = true }, "save-changes" },
        { "saving beats a still-active drawing tool", Some with { HasTriedEditing = true, HasUnsavedChanges = true, ActiveToolName = "Dikdörtgen" }, "save-first-time" },

        // --- example -------------------------------------------------------------------------------------
        { "example loaded, nothing selected", Some with { ExampleLoaded = true }, "example-pick" },
        { "example loaded, piece selected", Picked with { ExampleLoaded = true }, "example-try" },
        { "example loaded, unsaved, nothing selected, never edited", Some with { ExampleLoaded = true, HasUnsavedChanges = true }, "example-pick" },
        { "edited example with unsaved changes reaches the save nudge", Picked with { ExampleLoaded = true, HasTriedEditing = true, HasUnsavedChanges = true }, "save-first-time" },
        { "edited example that is clean again keeps inviting", Picked with { ExampleLoaded = true, HasTriedEditing = true }, "example-try" },
        { "example flag with a known file is a normal project (clean)", Picked with { ExampleLoaded = true, FilePathKnown = true, HasTriedEditing = true }, "selected-saved" },
        { "example flag with a known file is a normal project (unsaved)", Some with { ExampleLoaded = true, FilePathKnown = true, HasUnsavedChanges = true, HasTriedEditing = true }, "save-changes" },
        { "example flag on an empty project is ignored", new GuideSituation { ExampleLoaded = true }, "empty-project" },
        { "example loaded but a recovery notice is showing", Picked with { ExampleLoaded = true, HasRecoveryNotice = true }, "recovery-notice" },

        // --- tools chosen --------------------------------------------------------------------------------
        { "Çizgi tool on an empty project", Empty with { ActiveToolName = "Çizgi" }, "tool-line" },
        { "Dikdörtgen tool on an empty project", Empty with { ActiveToolName = "Dikdörtgen" }, "tool-rectangle" },
        { "Nokta nokta çiz tool", Empty with { ActiveToolName = "Nokta nokta çiz" }, "tool-polyline" },
        { "Eğri tool", Empty with { ActiveToolName = "Eğri" }, "tool-curve" },
        { "Yay tool", Empty with { ActiveToolName = "Yay" }, "tool-arc" },
        { "tool name in lower case", Empty with { ActiveToolName = "çizgi" }, "tool-line" },
        { "tool name padded and upper case", Empty with { ActiveToolName = "  YAY " }, "tool-arc" },
        { "tool name in Turkish capitals", Empty with { ActiveToolName = "ÇİZGİ" }, "tool-line" },
        { "tool name in ASCII-style capitals", Empty with { ActiveToolName = "ÇIZGI" }, "tool-line" },
        { "tool beats first launch", Empty with { IsFirstLaunch = true, ActiveToolName = "Çizgi" }, "tool-line" },
        { "tool over existing pieces says how to leave it", Picked with { ActiveToolName = "Çizgi", HasUnsavedChanges = true }, "tool-leave" },
        { "tool-leave beats the example", Picked with { ActiveToolName = "Yay", ExampleLoaded = true }, "tool-leave" },
        { "tool-leave with nothing selected", Some with { ActiveToolName = "Eğri" }, "tool-leave" },

        // --- imported ------------------------------------------------------------------------------------
        { "imported with warnings, nothing selected", Some with { ImportedWithWarnings = true }, "import-review" },
        { "imported beats a tool", Some with { ImportedWithWarnings = true, ActiveToolName = "Çizgi" }, "import-review" },
        { "imported beats unsaved changes on a file", Some with { ImportedWithWarnings = true, HasUnsavedChanges = true, FilePathKnown = true, HasTriedEditing = true }, "import-review" },
        { "imported beats saved state", Some with { ImportedWithWarnings = true, HasTriedEditing = true, FilePathKnown = true }, "import-review" },
        { "selecting a piece clears the import step", Picked with { ImportedWithWarnings = true }, "selected-edit" },

        // --- recovery ------------------------------------------------------------------------------------
        { "recovery notice alone", Empty with { HasRecoveryNotice = true }, "recovery-notice" },
        { "recovery beats unsaved and selection", Rect with { HasRecoveryNotice = true, HasUnsavedChanges = true, HasTriedEditing = true }, "recovery-notice" },
        { "recovery beats imported", Some with { HasRecoveryNotice = true, ImportedWithWarnings = true }, "recovery-notice" },
        { "recovery beats a chosen tool", Some with { HasRecoveryNotice = true, ActiveToolName = "Yay" }, "recovery-notice" },
        { "recovery beats first launch", Empty with { HasRecoveryNotice = true, IsFirstLaunch = true }, "recovery-notice" },

        // --- drawing in progress -------------------------------------------------------------------------
        { "drawing a line", Empty with { HasPendingDrawing = true, ActiveToolName = "Çizgi" }, "drawing-line" },
        { "drawing a rectangle", Empty with { HasPendingDrawing = true, ActiveToolName = "Dikdörtgen" }, "drawing-rectangle" },
        { "drawing with Nokta nokta çiz", Empty with { HasPendingDrawing = true, ActiveToolName = "Nokta nokta çiz" }, "drawing-polyline" },
        { "drawing with the code name Çoklu çizgi", Empty with { HasPendingDrawing = true, ActiveToolName = "Çoklu çizgi" }, "drawing-polyline" },
        { "drawing a curve", Empty with { HasPendingDrawing = true, ActiveToolName = "Eğri" }, "drawing-curve" },
        { "drawing a curve with the code name Bézier", Empty with { HasPendingDrawing = true, ActiveToolName = "Bézier" }, "drawing-curve" },
        { "drawing a curve in Turkish capitals", Empty with { HasPendingDrawing = true, ActiveToolName = "EĞRİ" }, "drawing-curve" },
        { "drawing an arc", Empty with { HasPendingDrawing = true, ActiveToolName = "Yay" }, "drawing-arc" },
        { "drawing with the Select tool is contradictory", Some with { HasPendingDrawing = true, ActiveToolName = "Seç" }, "drawing-other" },
        { "drawing with no tool name", Some with { HasPendingDrawing = true }, "drawing-other" },
        { "drawing with an unknown tool", Some with { HasPendingDrawing = true, ActiveToolName = "Boya" }, "drawing-other" },
        { "drawing beats recovery", Some with { HasPendingDrawing = true, ActiveToolName = "Çizgi", HasRecoveryNotice = true }, "drawing-line" },
        { "drawing beats imported", Some with { HasPendingDrawing = true, ActiveToolName = "Yay", ImportedWithWarnings = true }, "drawing-arc" },
        { "drawing beats first launch and empty project", Empty with { HasPendingDrawing = true, ActiveToolName = "Çizgi", IsFirstLaunch = true }, "drawing-line" },

        // --- preview waiting -----------------------------------------------------------------------------
        { "a written command is waiting", Empty with { HasPendingPreview = true }, "pending-preview" },
        { "a dangerous command is waiting", Rect with { HasPendingPreview = true, PendingPreviewKind = GuidePreviewKind.Danger }, "pending-preview-danger" },
        { "a question is waiting", Empty with { HasPendingPreview = true, PendingPreviewKind = GuidePreviewKind.Question }, "pending-preview-question" },
        { "an unknown preview kind reads as an ordinary preview", Empty with { HasPendingPreview = true, PendingPreviewKind = (GuidePreviewKind)99 }, "pending-preview" },
        { "the preview kind is ignored without a preview", Empty with { PendingPreviewKind = GuidePreviewKind.Danger }, "empty-project" },
        { "preview beats drawing, recovery and imported", Some with { HasPendingPreview = true, HasPendingDrawing = true, ActiveToolName = "Çizgi", HasRecoveryNotice = true, ImportedWithWarnings = true }, "pending-preview" },
        { "preview beats first launch", Empty with { HasPendingPreview = true, IsFirstLaunch = true }, "pending-preview" },
        { "preview beats unsaved work with a selection", Rect with { HasPendingPreview = true, HasUnsavedChanges = true, FilePathKnown = true, HasTriedEditing = true }, "pending-preview" },
    };

    [Theory]
    [MemberData(nameof(Situations))]
    public void Situation_gives_the_expected_single_step(string name, GuideSituation situation, string expectedId)
    {
        var step = GuideRules.NextStep(situation);
        Assert.True(expectedId == step.Id, $"{name}: expected '{expectedId}' but got '{step.Id}'.");
    }

    [Fact]
    public void Table_has_at_least_sixty_situations_and_touches_every_rule()
    {
        Assert.True(Situations.Count() >= 60, $"Only {Situations.Count()} situations.");
        var covered = Situations.Select(r => (string)r[2]).ToHashSet();
        var missing = GuideRules.Rules.Select(r => r.Id).Where(id => !covered.Contains(id)).ToArray();
        Assert.True(missing.Length == 0, "Rules without a table row: " + string.Join(", ", missing));
    }

    [Fact]
    public void Rule_order_is_pinned()
    {
        string[] expected =
        [
            "pending-preview", "pending-preview-danger", "pending-preview-question",
            "drawing-line", "drawing-rectangle", "drawing-polyline", "drawing-curve", "drawing-arc", "drawing-other",
            "recovery-notice",
            "import-review",
            "tool-line", "tool-rectangle", "tool-polyline", "tool-curve", "tool-arc",
            "empty-first-launch", "empty-project",
            "save-changes", "save-first-time",
            "tool-leave",
            "example-pick", "example-try",
            "selected-first-edit", "selected-saved", "selected-edit",
            "pick-piece",
        ];
        Assert.Equal(expected, GuideRules.Rules.Select(r => r.Id).ToArray());
        Assert.Equal(Enumerable.Range(1, expected.Length), GuideRules.Rules.Select(r => r.Order));
    }

    [Fact]
    public void Ids_are_unique_stable_looking_and_documented()
    {
        var ids = GuideRules.Rules.Select(r => r.Id).ToArray();
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
        foreach (var rule in GuideRules.Rules)
        {
            Assert.Matches("^[a-z]+(-[a-z]+)*$", rule.Id);
            Assert.False(string.IsNullOrWhiteSpace(rule.Condition));
            Assert.False(string.IsNullOrWhiteSpace(rule.Sentence));
        }
    }

    [Fact]
    public void Rule_list_cannot_be_changed_through_a_cast()
    {
        Assert.False(GuideRules.Rules is GuideRuleInfo[]);
        Assert.Throws<NotSupportedException>(() => ((IList<GuideRuleInfo>)GuideRules.Rules).Add(null!));
    }

    [Fact]
    public void Suggested_commands_come_from_the_known_command_list()
    {
        // The written commands FINAL_COMMANDS.md defines (C01 rectangle, line, C43 save, move, resize).
        // This is a hand-kept list: the command parser lives in another branch and is not referenced from here.
        var known = new HashSet<string>(StringComparer.Ordinal)
        {
            "120 x 80 dikdörtgen", "150 çizgi", "25 sağa taşı", "genişliği 95 yap", "projeyi kaydet", "80 x 60 dikdörtgen",
        };
        foreach (var rule in GuideRules.Rules.Where(r => r.SuggestedCommand is not null))
        {
            Assert.Contains(rule.SuggestedCommand!, known);
        }
        foreach (var task in StartTasks.For(new GuideSituation(), "x.tcad").Where(t => t.SuggestedCommand is not null))
        {
            Assert.Contains(task.SuggestedCommand!, known.Append("aç"));
        }
    }

    // Every combination of the situation facts: 2 counts x 7 tools x 2^11 booleans x resize x 3 preview kinds.
    private static IEnumerable<GuideSituation> AllSituations()
    {
        string?[] tools = [null, "Seç", "Çizgi", "Nokta nokta çiz", "Dikdörtgen", "Eğri", "Yay"];
        int[] counts = [-1, 2];
        GuidePreviewKind[] kinds = [GuidePreviewKind.Normal, GuidePreviewKind.Danger, GuidePreviewKind.Question];
        foreach (var count in counts)
        foreach (var tool in tools)
        foreach (var kind in kinds)
        foreach (var resize in new[] { false, true })
        for (var bits = 0; bits < 1 << 11; bits++)
        {
            yield return new GuideSituation
            {
                PieceCount = count,
                ActiveToolName = tool,
                PendingPreviewKind = kind,
                SelectedCanResize = resize,
                HasSelectedPiece = (bits & 1) != 0,
                HasUnsavedChanges = (bits & 2) != 0,
                FilePathKnown = (bits & 4) != 0,
                HasRecoveryNotice = (bits & 8) != 0,
                ImportedWithWarnings = (bits & 16) != 0,
                IsFirstLaunch = (bits & 32) != 0,
                HasPendingDrawing = (bits & 64) != 0,
                HasPendingPreview = (bits & 128) != 0,
                IsEmptyProject = (bits & 256) != 0,
                ExampleLoaded = (bits & 512) != 0,
                HasTriedEditing = (bits & 1024) != 0,
            };
        }
    }

    private const int AllSituationCount = 2 * 7 * 3 * 2 * (1 << 11);

    [Fact]
    public void Every_situation_maps_to_exactly_one_known_step_deterministically()
    {
        var byId = GuideRules.Rules.ToDictionary(r => r.Id);
        var fired = new HashSet<string>();
        var total = 0;
        foreach (var s in AllSituations())
        {
            var a = GuideRules.NextStep(s);
            Assert.Equal(a, GuideRules.NextStep(s with { }));
            Assert.True(byId.TryGetValue(a.Id, out var rule), $"Unknown id {a.Id}");
            Assert.Equal(rule!.Sentence, a.Sentence);
            Assert.Equal(rule.SuggestedCommand, a.SuggestedCommand);
            fired.Add(a.Id);
            total++;
        }
        Assert.Equal(AllSituationCount, total);
        var dead = byId.Keys.Except(fired).ToArray();
        Assert.True(dead.Length == 0, "Rules that no situation can reach: " + string.Join(", ", dead));
    }

    [Fact]
    public void Priority_properties_hold_for_every_situation()
    {
        foreach (var s in AllSituations())
        {
            var id = GuideRules.NextStep(s).Id;
            var noPieces = s.IsEmptyProject || s.PieceCount <= 0;
            var selected = s.HasSelectedPiece && !noPieces;
            Assert.Equal(noPieces, s.HasNoPieces);

            if (s.HasPendingPreview)
            {
                Assert.StartsWith("pending-preview", id);
            }
            else if (s.HasPendingDrawing)
            {
                Assert.StartsWith("drawing-", id);
            }
            else if (s.HasRecoveryNotice)
            {
                Assert.Equal("recovery-notice", id);
            }
            else if (s.ImportedWithWarnings && !noPieces && !selected)
            {
                Assert.Equal("import-review", id);
            }
            else if (noPieces)
            {
                Assert.True(id is "empty-first-launch" or "empty-project" || id.StartsWith("tool-"), id);
                Assert.DoesNotContain("tool-leave", id);
            }

            // The two guide states that talk about saving only ever show to someone who has tried editing and has unsaved work.
            if (id.StartsWith("save-", StringComparison.Ordinal))
            {
                Assert.True(s.HasTriedEditing && s.HasUnsavedChanges && !s.HasPendingPreview && !s.HasPendingDrawing && !s.HasRecoveryNotice);
                Assert.Equal(s.FilePathKnown, id == "save-changes");
            }
            // Resizing is suggested only for a selected piece that can be resized and only until editing was tried.
            if (id == "selected-first-edit")
            {
                Assert.True(selected && s.SelectedCanResize && !s.HasTriedEditing);
            }
            // Telling someone how to leave a tool only happens while pieces exist.
            if (id == "tool-leave")
            {
                Assert.False(noPieces);
                Assert.NotEqual(GuideTool.None, GuideRules.ClassifyTool(s.ActiveToolName));
            }
            // The example invitation never shows once a file is known.
            if (id.StartsWith("example-", StringComparison.Ordinal))
            {
                Assert.False(s.FilePathKnown);
                Assert.False(noPieces);
            }

            // The selection and the resize flag never matter while there are no pieces.
            if (noPieces)
            {
                Assert.Equal(id, GuideRules.NextStep(s with { HasSelectedPiece = !s.HasSelectedPiece }).Id);
                Assert.Equal(id, GuideRules.NextStep(s with { SelectedCanResize = !s.SelectedCanResize }).Id);
            }
            else
            {
                // First launch only matters for an empty project.
                Assert.Equal(id, GuideRules.NextStep(s with { IsFirstLaunch = !s.IsFirstLaunch }).Id);
            }
            // The preview kind only matters while a preview is waiting.
            if (!s.HasPendingPreview)
            {
                Assert.Equal(id, GuideRules.NextStep(s with { PendingPreviewKind = GuidePreviewKind.Question }).Id);
            }
        }
    }

    [Fact]
    public void Null_situation_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => GuideRules.NextStep(null!));
    }

    [Theory]
    [InlineData(null, GuideTool.None)]
    [InlineData("", GuideTool.None)]
    [InlineData("Seç", GuideTool.None)]
    [InlineData("Çizgi", GuideTool.Line)]
    [InlineData("çizgi", GuideTool.Line)]
    [InlineData("ÇİZGİ", GuideTool.Line)]
    [InlineData("ÇIZGI", GuideTool.Line)]
    [InlineData("Çoklu çizgi", GuideTool.Polyline)]
    [InlineData("ÇOKLU ÇİZGİ", GuideTool.Polyline)]
    [InlineData("Nokta nokta çiz", GuideTool.Polyline)]
    [InlineData("NOKTA NOKTA ÇİZ", GuideTool.Polyline)]
    [InlineData("Dikdörtgen", GuideTool.Rectangle)]
    [InlineData("DİKDÖRTGEN", GuideTool.Rectangle)]
    [InlineData("Bézier", GuideTool.Curve)]
    [InlineData("Bezier", GuideTool.Curve)]
    [InlineData("Eğri", GuideTool.Curve)]
    [InlineData("eğri", GuideTool.Curve)]
    [InlineData("EĞRİ", GuideTool.Curve)]
    [InlineData("Yay", GuideTool.Arc)]
    [InlineData("YAY", GuideTool.Arc)]
    [InlineData("Yayy", GuideTool.None)]
    [InlineData("Dikdörtgen aracı", GuideTool.None)]
    public void ClassifyTool_reads_display_and_code_names(string? name, GuideTool expected)
    {
        Assert.Equal(expected, GuideRules.ClassifyTool(name));
    }

    [Fact]
    public void Situation_is_an_immutable_value()
    {
        var a = new GuideSituation { PieceCount = 2 };
        var b = a with { HasSelectedPiece = true };
        Assert.False(a.HasSelectedPiece);
        Assert.True(b.HasSelectedPiece);
        Assert.Equal(a, a with { });
        Assert.NotEqual(a, b);
        var defaults = new GuideSituation();
        Assert.Equal(0, defaults.PieceCount);
        Assert.Null(defaults.ActiveToolName);
        Assert.False(defaults.IsFirstLaunch);
        Assert.True(defaults.HasNoPieces);
        Assert.False(a.HasNoPieces);
    }
}
