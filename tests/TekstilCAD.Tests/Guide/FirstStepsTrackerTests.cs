using TekstilCAD.Core.Guide;

namespace TekstilCAD.Tests.Guide;

public sealed class FirstStepsTrackerTests
{
    private static readonly FirstStepEvent[] Real =
    [
        FirstStepEvent.PieceDrawn, FirstStepEvent.SizeChanged, FirstStepEvent.PieceMoved,
        FirstStepEvent.UndoDone, FirstStepEvent.ProjectSaved, FirstStepEvent.ProjectReopened,
    ];

    private static FirstStepsTracker Apply(FirstStepsTracker t, params FirstStepEvent[] events) =>
        events.Aggregate(t, (s, e) => s.Observe(e));

    [Fact]
    public void Fresh_state_has_nothing_done_and_is_visible()
    {
        var t = FirstStepsTracker.Fresh;
        Assert.Equal(0, t.DoneCount);
        Assert.Equal(6, FirstStepsTracker.Total);
        Assert.True(t.IsVisible);
        Assert.False(t.IsComplete);
        Assert.False(t.IsDismissed);
        Assert.False(t.HasTriedEditing);
        Assert.Equal(FirstStep.DrawPiece, t.NextPending);
        Assert.Equal("İlk adımlar 0/6", t.Heading);
        foreach (var step in Enum.GetValues<FirstStep>())
        {
            Assert.False(t.IsDone(step));
        }
    }

    [Theory]
    [InlineData(FirstStepEvent.PieceDrawn, FirstStep.DrawPiece)]
    [InlineData(FirstStepEvent.SizeChanged, FirstStep.ChangeSize)]
    [InlineData(FirstStepEvent.PieceMoved, FirstStep.MovePiece)]
    [InlineData(FirstStepEvent.UndoDone, FirstStep.Undo)]
    [InlineData(FirstStepEvent.ProjectSaved, FirstStep.Save)]
    public void Each_event_ticks_exactly_its_item(FirstStepEvent observed, FirstStep expected)
    {
        var t = FirstStepsTracker.Fresh.Observe(observed);
        Assert.Equal(1, t.DoneCount);
        foreach (var step in Enum.GetValues<FirstStep>())
        {
            Assert.Equal(step == expected, t.IsDone(step));
        }
        Assert.Equal("İlk adımlar 1/6", t.Heading);
    }

    [Fact]
    public void Reopen_counts_only_after_a_save()
    {
        var before = FirstStepsTracker.Fresh.Observe(FirstStepEvent.ProjectReopened);
        Assert.Equal(FirstStepsTracker.Fresh, before);
        Assert.Same(FirstStepsTracker.Fresh, before);
        var after = Apply(FirstStepsTracker.Fresh, FirstStepEvent.ProjectSaved, FirstStepEvent.ProjectReopened);
        Assert.True(after.IsDone(FirstStep.Save));
        Assert.True(after.IsDone(FirstStep.ReopenFile));
    }

    [Fact]
    public void Observe_is_idempotent_and_returns_the_same_instance_when_nothing_changes()
    {
        foreach (var e in Real)
        {
            var once = Apply(FirstStepsTracker.Fresh, FirstStepEvent.ProjectSaved).Observe(e);
            var twice = once.Observe(e);
            Assert.Equal(once, twice);
            Assert.Same(once, twice);
        }
        var dismissed = FirstStepsTracker.Fresh.Observe(FirstStepEvent.Dismissed);
        Assert.Same(dismissed, dismissed.Observe(FirstStepEvent.Dismissed));
    }

    [Fact]
    public void Observe_never_changes_the_value_it_was_called_on()
    {
        var t = FirstStepsTracker.Fresh.Observe(FirstStepEvent.PieceDrawn);
        var json = t.ToJson();
        var next = t.Observe(FirstStepEvent.SizeChanged);
        Assert.Equal(json, t.ToJson());
        Assert.Equal(1, t.DoneCount);
        Assert.Equal(2, next.DoneCount);
        Assert.NotEqual(t, next);
    }

    [Fact]
    public void Items_never_become_undone()
    {
        var t = Apply(FirstStepsTracker.Fresh, Real);
        foreach (var e in Enum.GetValues<FirstStepEvent>())
        {
            var next = t.Observe(e);
            Assert.True(next.DoneCount >= t.DoneCount);
        }
    }

    [Fact]
    public void The_five_independent_events_commute()
    {
        var independent = Real.Take(5).ToArray();
        var expected = Apply(FirstStepsTracker.Fresh, independent);
        var rng = new Random(7);
        for (var i = 0; i < 200; i++)
        {
            var shuffled = independent.OrderBy(_ => rng.Next()).ToArray();
            Assert.Equal(expected, Apply(FirstStepsTracker.Fresh, shuffled));
        }
    }

    [Fact]
    public void Completing_everything_hides_the_list()
    {
        var t = Apply(FirstStepsTracker.Fresh, Real);
        Assert.True(t.IsComplete);
        Assert.False(t.IsVisible);
        Assert.Equal(6, t.DoneCount);
        Assert.Null(t.NextPending);
        Assert.Equal("İlk adımlar 6/6", t.Heading);
    }

    [Fact]
    public void Dismissing_hides_the_list_and_keeps_the_progress()
    {
        var t = Apply(FirstStepsTracker.Fresh, FirstStepEvent.PieceDrawn, FirstStepEvent.Dismissed);
        Assert.True(t.IsDismissed);
        Assert.False(t.IsVisible);
        Assert.Equal(1, t.DoneCount);
        var more = t.Observe(FirstStepEvent.SizeChanged);
        Assert.True(more.IsDismissed);
        Assert.Equal(2, more.DoneCount);
    }

    [Fact]
    public void HasTriedEditing_follows_size_and_move()
    {
        Assert.False(FirstStepsTracker.Fresh.Observe(FirstStepEvent.PieceDrawn).HasTriedEditing);
        Assert.True(FirstStepsTracker.Fresh.Observe(FirstStepEvent.SizeChanged).HasTriedEditing);
        Assert.True(FirstStepsTracker.Fresh.Observe(FirstStepEvent.PieceMoved).HasTriedEditing);
        Assert.False(FirstStepsTracker.Fresh.Observe(FirstStepEvent.UndoDone).HasTriedEditing);
    }

    [Fact]
    public void NextPending_walks_the_list_in_order()
    {
        var t = FirstStepsTracker.Fresh;
        foreach (var (e, next) in new[]
        {
            (FirstStepEvent.PieceDrawn, FirstStep.ChangeSize), (FirstStepEvent.SizeChanged, FirstStep.MovePiece),
            (FirstStepEvent.PieceMoved, FirstStep.Undo), (FirstStepEvent.UndoDone, FirstStep.Save),
            (FirstStepEvent.ProjectSaved, FirstStep.ReopenFile),
        })
        {
            t = t.Observe(e);
            Assert.Equal(next, t.NextPending);
        }
    }

    [Fact]
    public void Unknown_enum_values_are_harmless()
    {
        var t = FirstStepsTracker.Fresh;
        Assert.Same(t, t.Observe((FirstStepEvent)99));
        Assert.Same(t, t.Observe((FirstStepEvent)(-1)));
        Assert.False(t.IsDone((FirstStep)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => FirstStepsTracker.LabelOf((FirstStep)99));
    }

    [Fact]
    public void Labels_are_six_and_ordered()
    {
        Assert.Equal(6, FirstStepsTracker.AllLabels.Count);
        foreach (var step in Enum.GetValues<FirstStep>())
        {
            Assert.Equal(FirstStepsTracker.AllLabels[(int)step], FirstStepsTracker.LabelOf(step));
        }
    }

    // ------------------------------------------------------------------------------------- JSON

    [Fact]
    public void Json_has_the_documented_shape()
    {
        Assert.Equal("{\"v\":1,\"done\":[],\"hidden\":false}", FirstStepsTracker.Fresh.ToJson());
        var t = Apply(FirstStepsTracker.Fresh, FirstStepEvent.PieceDrawn, FirstStepEvent.SizeChanged, FirstStepEvent.Dismissed);
        Assert.Equal("{\"v\":1,\"done\":[\"draw\",\"size\"],\"hidden\":true}", t.ToJson());
        var all = Apply(FirstStepsTracker.Fresh, Real);
        Assert.Equal("{\"v\":1,\"done\":[\"draw\",\"size\",\"move\",\"undo\",\"save\",\"reopen\"],\"hidden\":false}", all.ToJson());
    }

    [Fact]
    public void Json_round_trips_every_reachable_state()
    {
        // Every ordered subset of the real events, with and without dismissal.
        var seen = new HashSet<string>();
        for (var subset = 0; subset < 1 << Real.Length; subset++)
        {
            for (var dismiss = 0; dismiss < 2; dismiss++)
            {
                var t = FirstStepsTracker.Fresh;
                for (var i = 0; i < Real.Length; i++)
                {
                    if ((subset & (1 << i)) != 0)
                    {
                        t = t.Observe(Real[i]);
                    }
                }
                if (dismiss == 1)
                {
                    t = t.Observe(FirstStepEvent.Dismissed);
                }
                var json = t.ToJson();
                Assert.True(json.Length < 120, json);
                Assert.True(json.Length <= FirstStepsTracker.MaxJsonLength);
                var back = FirstStepsTracker.FromJson(json);
                Assert.Equal(t, back);
                Assert.Equal(json, back.ToJson());
                seen.Add(json);
            }
        }
        Assert.True(seen.Count >= 64);
    }

    public static TheoryData<string?> NotUsable => new()
    {
        { null },
        { "" },
        { "   " },
        { "not json at all" },
        { "{" },
        { "[]" },
        { "null" },
        { "42" },
        { "\"text\"" },
        { "{}" },
        { "{\"done\":[\"draw\"]}" },
        { "{\"v\":0,\"done\":[\"draw\"],\"hidden\":false}" },
        { "{\"v\":2,\"done\":[\"draw\"],\"hidden\":false}" },
        { "{\"v\":-1,\"done\":[\"draw\"],\"hidden\":false}" },
        { "{\"v\":\"1\",\"done\":[\"draw\"],\"hidden\":false}" },
        { "{\"v\":1.5,\"done\":[\"draw\"],\"hidden\":false}" },
        { "{\"v\":null,\"done\":[\"draw\"]}" },
        { "{\"v\":99999999999999999999,\"done\":[\"draw\"]}" },
        { "{\"v\":1,\"done\":[\"draw\"" },
        { "{\"v\":1,\"done\":[\"draw\"],}" },
        { "{\"v\":1,\"done\":[\"draw\"]} trailing" },
        { "{\"v\":1,\"done\":[\"draw\"]}{\"v\":1}" },
        { "// comment\n{\"v\":1,\"done\":[\"draw\"]}" },
        { "{\"v\":1,\"done\":\"draw\"}" },
        { "{\"v\":1,\"done\":{\"draw\":true}}" },
        { "{\"v\":1,\"done\":[\"draw\"],\"hidden\":false}\0" },
        { "\uD800{\"v\":1}" },
    };

    [Theory]
    [MemberData(nameof(NotUsable))]
    public void Unusable_input_gives_a_fresh_state_and_never_throws(string? json)
    {
        var t = FirstStepsTracker.FromJson(json);
        Assert.Equal(FirstStepsTracker.Fresh, t);
    }

    [Fact]
    public void Unknown_items_and_properties_are_ignored()
    {
        var t = FirstStepsTracker.FromJson(
            "{\"v\":1,\"done\":[\"draw\",\"bogus\",5,null,{\"a\":1},\"draw\",\"size\",\"\"],\"hidden\":false,\"future\":{\"a\":[1,2]}}");
        Assert.True(t.IsDone(FirstStep.DrawPiece));
        Assert.True(t.IsDone(FirstStep.ChangeSize));
        Assert.Equal(2, t.DoneCount);
        Assert.False(t.IsDismissed);
    }

    [Fact]
    public void Missing_or_wrongly_typed_fields_fall_back_to_their_defaults()
    {
        Assert.Equal(FirstStepsTracker.Fresh, FirstStepsTracker.FromJson("{\"v\":1}"));
        Assert.True(FirstStepsTracker.FromJson("{\"v\":1,\"hidden\":true}").IsDismissed);
        Assert.False(FirstStepsTracker.FromJson("{\"v\":1,\"hidden\":\"true\"}").IsDismissed);
        Assert.False(FirstStepsTracker.FromJson("{\"v\":1,\"hidden\":1}").IsDismissed);
        Assert.Equal(1, FirstStepsTracker.FromJson("{\"v\":1,\"done\":[\"save\"],\"hidden\":null}").DoneCount);
    }

    [Fact]
    public void Reopen_without_save_in_a_stored_document_is_dropped()
    {
        var t = FirstStepsTracker.FromJson("{\"v\":1,\"done\":[\"draw\",\"reopen\"],\"hidden\":false}");
        Assert.True(t.IsDone(FirstStep.DrawPiece));
        Assert.False(t.IsDone(FirstStep.ReopenFile));
        var ok = FirstStepsTracker.FromJson("{\"v\":1,\"done\":[\"save\",\"reopen\"],\"hidden\":false}");
        Assert.True(ok.IsDone(FirstStep.ReopenFile));
    }

    [Fact]
    public void Input_is_bounded()
    {
        var item = "\"draw\",";
        // Valid JSON, but longer than the limit: treated as corrupt even though it parses.
        var longDone = "{\"v\":1,\"done\":[" + string.Concat(Enumerable.Repeat(item, 200)) + "\"size\"],\"hidden\":false}";
        Assert.True(longDone.Length > FirstStepsTracker.MaxJsonLength);
        Assert.Equal(FirstStepsTracker.Fresh, FirstStepsTracker.FromJson(longDone));
        // A huge string is rejected without being parsed.
        Assert.Equal(FirstStepsTracker.Fresh, FirstStepsTracker.FromJson(new string('x', 5_000_000)));
        Assert.Equal(FirstStepsTracker.Fresh, FirstStepsTracker.FromJson("{\"v\":1,\"done\":[\"draw\"],\"pad\":\"" + new string('a', 600) + "\"}"));
        // Just under the limit still works.
        var pad = new string('a', 400);
        var t = FirstStepsTracker.FromJson("{\"v\":1,\"done\":[\"draw\"],\"pad\":\"" + pad + "\"}");
        Assert.True(t.IsDone(FirstStep.DrawPiece));
        // More than 64 array items, even when short enough, is corrupt.
        var many = "{\"v\":1,\"done\":[" + string.Join(",", Enumerable.Repeat("1", 70)) + "]}";
        Assert.True(many.Length < FirstStepsTracker.MaxJsonLength);
        Assert.Equal(FirstStepsTracker.Fresh, FirstStepsTracker.FromJson(many));
    }

    [Fact]
    public void Deep_nesting_is_rejected()
    {
        var deep = "{\"v\":1,\"done\":[[[[[[[[[[1]]]]]]]]]]}";
        Assert.Equal(FirstStepsTracker.Fresh, FirstStepsTracker.FromJson(deep));
        var bomb = new string('[', 400);
        Assert.Equal(FirstStepsTracker.Fresh, FirstStepsTracker.FromJson(bomb));
        Assert.Equal(FirstStepsTracker.Fresh, FirstStepsTracker.FromJson("{\"v\":1,\"x\":" + new string('[', 100) + new string(']', 100) + "}"));
    }

    [Fact]
    public void Random_damage_never_throws_and_output_stays_bounded()
    {
        var rng = new Random(12345);
        var seed = Apply(FirstStepsTracker.Fresh, Real).Observe(FirstStepEvent.Dismissed).ToJson();
        const string junk = "{}[]\",:\\/0123456789truefalsnl \t\n\u0000\uD800￿ç";
        for (var i = 0; i < 3000; i++)
        {
            var chars = seed.ToCharArray().ToList();
            var edits = rng.Next(1, 6);
            for (var e = 0; e < edits; e++)
            {
                var pos = rng.Next(0, chars.Count + 1);
                switch (rng.Next(3))
                {
                    case 0 when chars.Count > 0: chars.RemoveAt(Math.Min(pos, chars.Count - 1)); break;
                    case 1: chars.Insert(pos, junk[rng.Next(junk.Length)]); break;
                    default:
                        if (chars.Count > 0)
                        {
                            chars[Math.Min(pos, chars.Count - 1)] = junk[rng.Next(junk.Length)];
                        }
                        break;
                }
            }
            var t = FirstStepsTracker.FromJson(new string(chars.ToArray()));
            Assert.NotNull(t);
            Assert.True(t.ToJson().Length <= FirstStepsTracker.MaxJsonLength);
            Assert.InRange(t.DoneCount, 0, FirstStepsTracker.Total);
            Assert.Equal(t, FirstStepsTracker.FromJson(t.ToJson()));
        }
    }

    [Fact]
    public void Equality_and_hash_follow_the_value()
    {
        var a = Apply(FirstStepsTracker.Fresh, FirstStepEvent.PieceDrawn);
        var b = Apply(FirstStepsTracker.Fresh, FirstStepEvent.PieceDrawn);
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.False(a.Equals(null));
        Assert.False(a.Equals((object)"x"));
        Assert.NotEqual(a, a.Observe(FirstStepEvent.Dismissed));
        Assert.Equal(a.ToJson(), a.ToString());
    }
}
