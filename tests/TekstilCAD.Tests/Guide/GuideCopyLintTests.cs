using System.Text.RegularExpressions;
using TekstilCAD.Core.Guide;

namespace TekstilCAD.Tests.Guide;

/// <summary>Copy rules for every string the guide can show (FINAL_SPEC: plain words, sentence case, short, polite).</summary>
public sealed class GuideCopyLintTests
{
    // Words that must never appear (FINAL_SPEC b.6 plus the tone rules of FINAL_COMMANDS).
    private static readonly string[] Banned =
    [
        "kontur", "nokta", "ızgara", "izgara", "snap", "yakalama", "katman", "offset", "poliline", "serileme",
        "vertex", "bezier", "bézier", "parametrik", "yapay zeka", "yapay zekâ", "asistan", "model", "dxf",
    ];

    private const int MaxSentenceLength = 100;
    private const int MaxLabelLength = 40;

    private static IEnumerable<string> RuleSentences => GuideRules.Rules.Select(r => r.Sentence);

    private static IEnumerable<string> StartTaskSentences =>
    [
        .. StartTasks.FixedStrings.Where(s => s.EndsWith('.')),
    ];

    private static IEnumerable<string> Labels =>
    [
        .. FirstStepsTracker.AllLabels,
        FirstStepsTracker.DismissLabel,
        .. Enumerable.Range(0, FirstStepsTracker.Total + 1).Select(i => $"İlk adımlar {i}/{FirstStepsTracker.Total}"),
        .. StartTasks.FixedStrings.Where(s => !s.EndsWith('.') && s != StartTasks.Heading),
    ];

    private static IEnumerable<string> Everything =>
    [
        .. RuleSentences, .. StartTaskSentences, .. Labels, StartTasks.Heading,
    ];

    [Fact]
    public void Nothing_is_empty_padded_doubled_or_odd()
    {
        foreach (var s in Everything)
        {
            Assert.False(string.IsNullOrWhiteSpace(s));
            Assert.Equal(s.Trim(), s);
            Assert.DoesNotContain("  ", s);
            Assert.DoesNotContain("...", s);
            Assert.DoesNotContain("!", s);
            Assert.DoesNotContain("\n", s);
            Assert.True(char.IsUpper(s[0]), $"'{s}' must start with a capital letter.");
            Assert.Matches(@"^[\p{L}\p{N} .,;:'/?\-]+$", s);
        }
    }

    [Fact]
    public void No_banned_words()
    {
        foreach (var s in Everything)
        {
            foreach (var word in Banned)
            {
                Assert.False(s.Contains(word, StringComparison.OrdinalIgnoreCase), $"'{s}' contains banned word '{word}'.");
            }
        }
    }

    [Fact]
    public void No_shouting_words()
    {
        foreach (var s in Everything)
        {
            foreach (Match m in Regex.Matches(s, @"\p{L}{4,}"))
            {
                Assert.False(m.Value == m.Value.ToUpperInvariant() && m.Value != m.Value.ToLowerInvariant(), $"'{s}' shouts '{m.Value}'.");
            }
        }
    }

    [Fact]
    public void Sentences_are_short_and_end_with_a_period()
    {
        foreach (var s in RuleSentences.Concat(StartTaskSentences))
        {
            Assert.True(s.Length <= MaxSentenceLength, $"'{s}' is {s.Length} characters.");
            Assert.EndsWith(".", s);
            Assert.False(s.EndsWith("..", StringComparison.Ordinal));
            // At most two short sentences (the owner's wording for the pending preview and the first screen is two).
            var count = s.Count(c => c == '.');
            Assert.True(count is 1 or 2, $"'{s}' has {count} sentences.");
            Assert.DoesNotContain("?", s);
        }
    }

    [Fact]
    public void Labels_are_short_and_have_no_terminal_punctuation()
    {
        foreach (var s in Labels)
        {
            Assert.True(s.Length <= MaxLabelLength, $"'{s}' is {s.Length} characters.");
            Assert.DoesNotMatch(@"[.,;:!?]$", s);
        }
    }

    [Fact]
    public void Heading_is_the_owner_s_wording()
    {
        Assert.Equal("Nereden başlayalım?", StartTasks.Heading);
        Assert.Equal("İlk adımlar 0/6", FirstStepsTracker.Fresh.Heading);
    }

    [Fact]
    public void Owner_wording_from_the_spec_is_used_verbatim()
    {
        var sentences = GuideRules.Rules.ToDictionary(r => r.Id, r => r.Sentence);
        Assert.Equal("Yukarıdaki cümleyi okuyun. Doğruysa Enter ya da Uygula; değilse Esc.", sentences["pending-preview"]);
        Assert.Equal("Kutuya yazın: 120 x 80 dikdörtgen. Ya da aşağıdan bir seçim yapın.", sentences["empty-first-launch"]);
        Assert.Equal("Ölçüyü değiştirmek için yazın: genişliği 95 yap.", sentences["selected-first-edit"]);
        Assert.Equal("Parça çizin", FirstStepsTracker.LabelOf(FirstStep.DrawPiece));
        Assert.Equal("Ölçüsünü değiştirin", FirstStepsTracker.LabelOf(FirstStep.ChangeSize));
        Assert.Equal("Parçayı taşıyın", FirstStepsTracker.LabelOf(FirstStep.MovePiece));
        Assert.Equal("Çalışmayı kaydedin", FirstStepsTracker.LabelOf(FirstStep.Save));
        Assert.Equal("Gizle", FirstStepsTracker.DismissLabel);
    }

    [Fact]
    public void Sentences_and_labels_are_all_distinct()
    {
        var all = RuleSentences.Concat(Labels).ToArray();
        Assert.Equal(all.Length, all.Distinct(StringComparer.Ordinal).Count());
    }
}
