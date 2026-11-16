using TekstilCAD.Core.Guide;

namespace TekstilCAD.Tests.Guide;

public sealed class StartTasksTests
{
    private static readonly GuideSituation Empty = new() { IsFirstLaunch = true };

    [Fact]
    public void Empty_project_shows_the_four_tasks_in_order()
    {
        var tasks = StartTasks.For(Empty, null);
        Assert.Equal(["type-size", "open-file", "try-example", "continue-last"], tasks.Select(t => t.Id).ToArray());
        Assert.Equal(["Ölçü yazarak başla", "Dosya aç", "Örnekle dene", "Kaldığım yerden devam et"], tasks.Select(t => t.Title).ToArray());
        Assert.Equal("Nereden başlayalım?", StartTasks.Heading);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(5, true)]
    public void Project_with_pieces_shows_no_tasks(int pieces, bool selected)
    {
        Assert.Empty(StartTasks.For(new GuideSituation { PieceCount = pieces, HasSelectedPiece = selected }, "a.tcad"));
    }

    [Fact]
    public void Empty_flag_wins_over_a_piece_count_and_negative_counts_are_empty()
    {
        Assert.Equal(4, StartTasks.For(new GuideSituation { PieceCount = 3, IsEmptyProject = true }, null).Count);
        Assert.Equal(4, StartTasks.For(new GuideSituation { PieceCount = -2 }, null).Count);
    }

    [Fact]
    public void Example_is_offered_only_to_a_clean_empty_project()
    {
        Assert.True(Task(StartTasks.For(Empty, null), "try-example").Enabled);
        foreach (var dirty in new[]
        {
            Empty with { HasUnsavedChanges = true },
            Empty with { FilePathKnown = true },
            Empty with { HasPendingDrawing = true },
            Empty with { HasPendingPreview = true },
        })
        {
            var task = Task(StartTasks.For(dirty, null), "try-example");
            Assert.False(task.Enabled);
            Assert.False(string.IsNullOrWhiteSpace(task.DisabledReason));
        }
        Assert.Null(Task(StartTasks.For(Empty, null), "try-example").DisabledReason);
    }

    [Fact]
    public void Continue_needs_a_recent_file_and_shows_its_name()
    {
        var none = Task(StartTasks.For(Empty, null), "continue-last");
        Assert.False(none.Enabled);
        Assert.NotNull(none.DisabledReason);
        Assert.False(Task(StartTasks.For(Empty, "   "), "continue-last").Enabled);

        var some = Task(StartTasks.For(Empty, "gomlek-on-parca.tcad"), "continue-last");
        Assert.True(some.Enabled);
        Assert.Null(some.DisabledReason);
        Assert.Equal("gomlek-on-parca.tcad", some.Description);
    }

    [Theory]
    [InlineData(@"C:\Users\ayse\Belgeler\palto.tcad", "palto.tcad")]
    [InlineData("/home/ayse/palto.tcad", "palto.tcad")]
    [InlineData("palto\u0007\n.tcad", "palto.tcad")]
    [InlineData("  palto.tcad  ", "palto.tcad")]
    public void File_names_are_stripped_of_folders_and_control_characters(string given, string shown)
    {
        Assert.Equal(shown, Task(StartTasks.For(Empty, given), "continue-last").Description);
    }

    [Theory]
    [InlineData(@"C:\folder\")]
    [InlineData("\u0001\u0002")]
    public void Names_with_nothing_usable_count_as_no_recent_file(string given)
    {
        Assert.False(Task(StartTasks.For(Empty, given), "continue-last").Enabled);
    }

    [Fact]
    public void Long_names_are_shortened_without_splitting_a_character()
    {
        var long1 = new string('a', 500) + ".tcad";
        var shown = Task(StartTasks.For(Empty, long1), "continue-last").Description;
        Assert.True(shown.Length <= StartTasks.MaxFileNameLength);
        Assert.EndsWith("…", shown);

        var emoji = string.Concat(Enumerable.Repeat("\U0001F600", 100));
        var shownEmoji = Task(StartTasks.For(Empty, emoji), "continue-last").Description;
        Assert.True(shownEmoji.Length <= StartTasks.MaxFileNameLength);
        // EnumerateRunes replaces a lone surrogate with U+FFFD; a clean cut leaves none.
        Assert.DoesNotContain('�', string.Concat(shownEmoji.EnumerateRunes().Select(r => r.ToString())));
    }

    [Fact]
    public void Suggested_commands_are_the_known_ones()
    {
        var tasks = StartTasks.For(Empty, "x.tcad");
        Assert.Equal("120 x 80 dikdörtgen", Task(tasks, "type-size").SuggestedCommand);
        Assert.Equal("aç", Task(tasks, "open-file").SuggestedCommand);
        Assert.Null(Task(tasks, "try-example").SuggestedCommand);
        Assert.Null(Task(tasks, "continue-last").SuggestedCommand);
    }

    [Fact]
    public void Null_situation_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => StartTasks.For(null!, null));
    }

    private static StartTask Task(IReadOnlyList<StartTask> tasks, string id) => tasks.Single(t => t.Id == id);
}
