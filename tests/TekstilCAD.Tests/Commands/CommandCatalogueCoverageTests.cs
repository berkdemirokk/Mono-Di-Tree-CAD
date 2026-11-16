using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using TekstilCAD.Core.Commands;

namespace TekstilCAD.Tests;

/// <summary>
/// Keeps the "Tüm araçlar" catalogue and the classic main window in step. The WPF <c>MainWindow</c> cannot be
/// created inside xUnit, so these tests read <c>src\TekstilCAD.Desktop\MainWindow.cs</c> as text (see <see cref="ClassicButtonScanner"/>
/// for what that can and cannot see). Every classic button must be named by exactly one catalogue entry or be a header button.
/// </summary>
public class CommandCatalogueCoverageTests
{
    /// <summary>
    /// The number of classic main-window buttons: 8 header notice buttons (field declarations) and 60 constructor buttons.
    /// Change it only together with the catalogue: add or remove the entry (or HeaderButtons item) for the button you added or removed.
    /// </summary>
    private const int ExpectedButtonCount = 68;

    private static readonly string[] CompactFullNames =
    [
        "Yeni dosya olarak güvenli kopya…", "Koordinatla nokta gir…", "Seçili parçayı A4 PDF çıkar…",
        "Seçili parçayı göster", "Parametrik konturu poliline dönüştür…",
    ];

    // Names the headless verification looks up (with Single()) that are not set by a literal SetName call we can read.
    private static readonly string[] VerificationNames =
    [
        "Proje parçaları", "Seçili parça işlem sınırları", "Dışa aktarma durumu", "Kurtarma taraması hatası ayrıntıları",
        "Kalıp çalışma alanı", "Dosya aktarımı", "Tekstil ve makine", "Mono Di Tree CAD",
    ];

    // ---------------------------------------------------------------- helpers

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !(File.Exists(Path.Combine(dir.FullName, "AGENTS.md")) && File.Exists(Path.Combine(dir.FullName, "TekstilCAD.slnx"))))
            dir = dir.Parent;
        Assert.True(dir is not null, "Repository root (a folder holding AGENTS.md and TekstilCAD.slnx) not found above " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    private static string LoadMainWindowSource()
    {
        var path = Path.Combine(FindRepoRoot(), "src", "TekstilCAD.Desktop", "MainWindow.cs");
        Assert.True(File.Exists(path), "MainWindow.cs not found at " + path);
        return File.ReadAllText(path, Encoding.UTF8);
    }

    // The five orthogonal transform buttons are built from the OrthogonalOps table (AddOrthogonalButton), so the scanner reads their labels there.
    private static string LoadCommandsSource()
    {
        var path = Path.Combine(FindRepoRoot(), "src", "TekstilCAD.Desktop", "MainWindow.Commands.cs");
        Assert.True(File.Exists(path), "MainWindow.Commands.cs not found at " + path);
        return File.ReadAllText(path, Encoding.UTF8);
    }

    /// <summary>Scans the real window source and fails with every extraction problem listed, so a refactor cannot hide buttons.</summary>
    private static ClassicButtonScanner.Extraction ScanWindow(out string text)
    {
        text = LoadMainWindowSource();
        var extraction = ClassicButtonScanner.Scan(text, LoadCommandsSource());
        Assert.True(extraction.Problems.Count == 0,
            "The button extractor could not read MainWindow.cs cleanly (a refactor may have hidden buttons from the catalogue check; "
            + "fix the window code or extend ClassicButtonScanner):\n" + string.Join("\n", extraction.Problems));
        return extraction;
    }

    private static IEnumerable<string> CatalogueLabels() =>
        CommandCatalogue.Entries.Select(e => e.ClassicButtonLabel).Concat(CommandCatalogue.HeaderButtons.Select(h => h.ClassicButtonLabel))
            .Where(l => l is not null).Select(l => l!);

    // ------------------------------------------------------------------ tests

    [Fact]
    public void ExtractedButtonCount_Is68()
    {
        var buttons = ScanWindow(out _).Buttons;
        Assert.True(buttons.Count == ExpectedButtonCount,
            $"MainWindow.cs yields {buttons.Count} classic buttons, the catalogue was written for {ExpectedButtonCount}. "
            + "If a button was added or removed on purpose, add or remove its entry in CommandCatalogueData (or its HeaderButtons item) "
            + "and change ExpectedButtonCount in this test together with it, never alone.\n"
            + string.Join("\n", buttons.Select(b => $"  line {b.Line} [{b.Origin}] {b.Label}")));
        var duplicates = buttons.GroupBy(b => b.Label, StringComparer.Ordinal).Where(g => g.Count() > 1)
            .Select(g => $"\"{g.Key}\" at lines {string.Join(", ", g.Select(b => b.Line))}").ToList();
        Assert.True(duplicates.Count == 0, "Classic button labels must be distinct (the catalogue finds buttons by label):\n" + string.Join("\n", duplicates));
    }

    [Fact]
    public void EveryClassicButton_IsCoveredExactlyOnce()
    {
        var labels = ScanWindow(out _).Buttons.Select(b => b.Label).ToList();
        var offenders = new List<string>();
        foreach (var label in labels)
        {
            var count = CommandCatalogue.Entries.Count(e => string.Equals(e.ClassicButtonLabel, label, StringComparison.Ordinal))
                + CommandCatalogue.HeaderButtons.Count(h => string.Equals(h.ClassicButtonLabel, label, StringComparison.Ordinal));
            if (count != 1) offenders.Add($"\"{label}\" is named {count} times (must be exactly once)");
        }
        Assert.True(offenders.Count == 0, "Classic buttons not covered exactly once by the catalogue:\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void NoOrphanLabels()
    {
        var present = ScanWindow(out _).Buttons.Select(b => b.Label).ToHashSet(StringComparer.Ordinal);
        var orphans = CatalogueLabels().Where(l => !present.Contains(l)).Distinct(StringComparer.Ordinal)
            .Select(l => $"\"{l}\" is named by the catalogue but no classic button has this exact text").ToList();
        Assert.True(orphans.Count == 0, "Catalogue labels without a classic button (renamed or removed in MainWindow.cs?):\n" + string.Join("\n", orphans));
    }

    [Fact]
    public void GlyphButtons_AccessibleNamesMatchWindow()
    {
        var scan = ScanWindow(out var text);
        var views = scan.Buttons.Where(b => b.Origin == ClassicButtonScanner.ViewButtonOrigin).ToList();
        Assert.True(views.Count == 6, $"Expected 6 ViewButton calls, found {views.Count}.");
        var offenders = new List<string>();
        foreach (var view in views)
        {
            var entry = CommandCatalogue.FindByClassicLabel(view.Label);
            if (entry is null) offenders.Add($"ViewButton \"{view.Label}\" has no catalogue entry");
            else if (!string.Equals(entry.ClassicAccessibleName, view.Name, StringComparison.Ordinal))
                offenders.Add($"ViewButton \"{view.Label}\": window name \"{view.Name}\", catalogue ClassicAccessibleName \"{entry.ClassicAccessibleName}\" (entry {entry.Id})");
        }
        foreach (var (glyph, name, variable) in new[] { ("↶", "Geri al", "undoCommand"), ("↷", "Yinele", "redoCommand") })
        {
            if (!Regex.IsMatch(text, $@"\b{variable}\s*=\s*AddButton\(\s*\w+\s*,\s*""{glyph}"""))
                offenders.Add($"{variable} is no longer created by AddButton(<panel>, \"{glyph}\", ...)");
            if (!Regex.IsMatch(text, $@"AutomationProperties\.SetName\(\s*{variable}\s*,\s*""{name}""\s*\)"))
                offenders.Add($"{variable} no longer gets AutomationProperties.SetName(..., \"{name}\")");
            var entry = CommandCatalogue.FindByClassicLabel(glyph);
            if (entry is null) offenders.Add($"glyph button \"{glyph}\" has no catalogue entry");
            else if (!string.Equals(entry.ClassicAccessibleName, name, StringComparison.Ordinal))
                offenders.Add($"glyph button \"{glyph}\": window name \"{name}\", catalogue ClassicAccessibleName \"{entry.ClassicAccessibleName}\" (entry {entry.Id})");
        }
        Assert.True(offenders.Count == 0, "Glyph button names differ between window and catalogue:\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void OrthogonalButtons_KeepTheirAccessibleNamesInTheCatalogue()
    {
        var commands = LoadCommandsSource();
        var offenders = new List<string>();
        foreach (Match m in Regex.Matches(commands, @"OrthogonalOp\s+\w+\s*=\s*new\(\s*""[^""]*""\s*,\s*""([^""]*)""\s*,\s*""[^""]*""\s*,\s*(null|""([^""]*)"")"))
        {
            var label = m.Groups[1].Value; var name = m.Groups[3].Success ? m.Groups[3].Value : null;
            var entry = CommandCatalogue.FindByClassicLabel(label);
            if (entry is null) offenders.Add($"\"{label}\" has no entry");
            else if (entry.ClassicAccessibleName != name) offenders.Add($"\"{label}\": window name \"{name}\", catalogue \"{entry.ClassicAccessibleName}\"");
        }
        Assert.Empty(offenders);
        Assert.Equal(5, Regex.Matches(commands, @"OrthogonalOp\s+\w+\s*=\s*new\(").Count);
    }

    [Fact]
    public void CompactLabelButtons_UseFullNameInCatalogue()
    {
        var scan = ScanWindow(out _);
        Assert.Equal(CompactFullNames.OrderBy(n => n, StringComparer.Ordinal), scan.CompactLabels.Select(c => c.Full).OrderBy(n => n, StringComparer.Ordinal));
        foreach (var full in CompactFullNames)
        {
            var entry = CommandCatalogue.FindByClassicLabel(full);
            Assert.True(entry is not null, $"No catalogue entry has ClassicButtonLabel \"{full}\" (the full name of a CompactLabel button).");
            Assert.Equal(full, entry!.ClassicButtonLabel);
        }
    }

    [Fact]
    public void CompactShortLabels_AreNotCatalogueLabels()
    {
        var labels = CatalogueLabels().ToHashSet(StringComparer.Ordinal);
        var offenders = ScanWindow(out _).CompactLabels.Where(c => labels.Contains(c.Short))
            .Select(c => $"short label \"{c.Short}\" (of \"{c.Full}\") is used as a ClassicButtonLabel; use the full name").ToList();
        Assert.True(offenders.Count == 0, string.Join("\n", offenders));
    }

    [Fact]
    public void NoCatalogueTitleCollidesWithAClassicLabel()
    {
        var scan = ScanWindow(out var text);
        var taken = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var b in scan.Buttons) taken.TryAdd(b.Label, $"classic button label (line {b.Line})");
        foreach (var name in ClassicButtonScanner.SetNameLiterals(text)) taken.TryAdd(name, "AutomationProperties.SetName literal in MainWindow.cs");
        foreach (var name in VerificationNames) taken.TryAdd(name, "name looked up by the headless verification");
        var offenders = new List<string>();
        foreach (var e in CommandCatalogue.Entries)
            if (taken.TryGetValue(e.Title, out var what)) offenders.Add($"entry {e.Id}: title \"{e.Title}\" equals a {what}");
        foreach (var g in CommandCatalogue.Groups)
            if (taken.TryGetValue(g.Title, out var what)) offenders.Add($"group {g.Id}: title \"{g.Title}\" equals a {what}");
        Assert.True(offenders.Count == 0,
            "Verification finds controls by content or automation name with Single(); the new page's buttons must not clash:\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void ExtractionSelfTest()
    {
        const string fake = """
            class W
            {
                private readonly Button hdr = new() { Content="Header düğmesi", Visibility=Visibility.Collapsed };
                private readonly Button hdr2 = new() { Content = "İkinci başlık" };
                // private readonly Button commented = new() { Content="Yorum" };
                internal MainWindow(DocumentSession? sessionForVerification)
                {
                    AddButton(files, "Düz", Act, "ipucu");
                    AddButton(files, "Brace { in \"quoted\" A \\ string", Act);   // } trailing comment with a brace
                    var c = '}'; var q = '"';
                    // AddButton(files, "Commented out", Act);
                    CompactLabel(AddButton(tools, "Tam ad…", Act), "Kısa…");
                    var b = new Button { Content="Çıkış", Padding=new Thickness(1) };
                    foreach (var (tool, glyph, shortcut) in new[] { ("Bir", "1", "A"), ("İki", "2", "B") })
                    {
                        AddButton(tools, tool, () => { }, tool);
                    }
                    void ViewButton(string glyph,string label,Action action,string hint) { AddButton(nav, glyph, action, hint); }
                    ViewButton("+", "Yakınlaştır", Zoom, "ip");
                    AddButton(files, dynamicLabel, Act);
                }
                void Dialog()
                {
                    AddButton(actions, "Vazgeç", Act);
                    var d = new Button { Content="Kapat" };
                }
            }
            """;
        var scan = ClassicButtonScanner.Scan(fake);
        var expected = new[] { "Header düğmesi", "İkinci başlık", "Düz", "Brace { in \"quoted\" A \\ string", "Tam ad…", "Çıkış", "Bir", "İki", "+" };
        Assert.Equal(expected.OrderBy(l => l, StringComparer.Ordinal), scan.Buttons.Select(b => b.Label).OrderBy(l => l, StringComparer.Ordinal));
        Assert.Equal(("Tam ad…", "Kısa…"), Assert.Single(scan.CompactLabels));
        Assert.Equal("Yakınlaştır", Assert.Single(scan.Buttons, b => b.Origin == ClassicButtonScanner.ViewButtonOrigin).Name);
        Assert.Equal(2, scan.Buttons.Count(b => b.Origin == ClassicButtonScanner.FieldOrigin));
        var problem = Assert.Single(scan.Problems);
        Assert.Contains("dynamicLabel", problem);
        Assert.Contains("line 20", problem);

        // Duplicates are kept so the count test can report them; concatenated labels and a missing constructor are problems.
        var twice = ClassicButtonScanner.Scan("class W { internal MainWindow(DocumentSession? sessionForVerification) { AddButton(a, \"X\", F); AddButton(b, \"X\", F); AddButton(c, \"Y\" + z, F); } }");
        Assert.Equal(2, twice.Buttons.Count(b => b.Label == "X"));
        Assert.Single(twice.Problems);
        Assert.Single(ClassicButtonScanner.Scan("class W { void M() { AddButton(a, \"X\", F); } }").Problems, p => p.Contains("constructor"));
    }

    // ---------------------------------------------------------------- scanner

    /// <summary>
    /// Finds the classic buttons in the TEXT of <c>MainWindow.cs</c>. It recognises exactly these shapes and reports anything else
    /// that looks like a button as a problem instead of skipping it: (1) field lines <c>Button x = new() { Content="..." }</c>
    /// anywhere in the file; inside the body of <c>internal MainWindow(DocumentSession? sessionForVerification)</c> only:
    /// (2) <c>AddButton(panel, "literal", ...)</c>, (3) <c>new Button { Content="literal" ... }</c>, (4) the tuple array of the
    /// <c>foreach (var (tool, glyph, shortcut) in new[] {...})</c> tool loop, (5) <c>ViewButton("glyph", "accessible name", ...)</c>,
    /// plus <c>CompactLabel(AddButton(panel, "full", ...), "short")</c> pairs.
    /// (6) <c>AddOrthogonalButton(panel, OrthogonalOps.Member)</c>, whose label is read from the OrthogonalOps table in MainWindow.Commands.cs;
    /// any other <c>Add...Button(</c> helper is reported as a problem.
    /// LIMITS: it is a lexer-assisted text scan, not a C# parser. Comments, strings, verbatim strings and char literals are
    /// skipped when matching braces and parentheses, but interpolated strings are treated as plain strings (a nested quote inside
    /// <c>{...}</c> would confuse it), raw string literals (<c>"""</c>) and constants or concatenations as labels are not understood
    /// (they are reported as problems, never silently dropped). Only the escapes \" \\ \' \n \r \t \0 \uXXXX are decoded. AddButton calls
    /// outside the constructor (dialogs) are ignored on purpose; buttons built there or elsewhere, by other helpers or by code
    /// that is not in this constructor, are invisible to it, as are runtime changes of Content. An AddButton whose label is an identifier
    /// other than the tool-loop variable <c>tool</c> or the ViewButton parameter <c>glyph</c> is a problem. Field lines are matched
    /// by text shape anywhere in the file, not by class.
    /// </summary>
    private static class ClassicButtonScanner
    {
        public const string ConstructorSignature = "internal MainWindow(DocumentSession? sessionForVerification)";
        public const string FieldOrigin = "field";
        public const string ViewButtonOrigin = "ViewButton";

        public sealed record Found(string Label, string Origin, int Line, string? Name = null);

        public sealed record Extraction(List<Found> Buttons, List<string> Problems, List<(string Full, string Short)> CompactLabels);

        private static readonly Regex FieldButton = new(
            @"^[ \t]*(?:(?:private|internal|public|protected)[ \t]+)?(?:readonly[ \t]+)?Button[ \t]+\w+[ \t]*=[ \t]*new\(\)[ \t]*\{[ \t]*Content[ \t]*=[ \t]*",
            RegexOptions.Multiline);
        private static readonly Regex FirstArg = new(@"\G\s*[\w.]+\s*,");
        private static readonly Regex VariableArg = new(@"\G\s*(\w+)\s*,");
        private static readonly Regex NewButton = new(@"\bnew\s+Button\b");
        private static readonly Regex ContentInit = new(@"\G\s*\{\s*Content\s*=\s*");
        private static readonly Regex ToolLoop = new(@"foreach\s*\(\s*var\s*\(\s*tool\s*,\s*glyph\s*,\s*shortcut\s*\)\s*in\s*new\[\]\s*\{");
        private static readonly Regex ViewCall = new(@"(?<!\bvoid\s+)\bViewButton\(");
        private static readonly Regex CompactCall = new(@"\bCompactLabel\(\s*AddButton\(");
        private static readonly Regex SetNameCall = new(@"AutomationProperties\.SetName\(\s*[^,\r\n]+,\s*");
        private static readonly HashSet<string> AllowedVariables = ["tool", "glyph"];

        private static readonly Regex OrthogonalCall = new(@"AddOrthogonalButton\(\s*\w+\s*,\s*OrthogonalOps\.(\w+)\s*\)");
        private static readonly Regex OtherHelper = new(@"(?!AddButton|AddOrthogonalButton)(Add\w*Button)\(");

        /// <summary>The label of table entry <paramref name="member"/> in the OrthogonalOps declaration text, or null.</summary>
        private static string? OrthogonalLabel(string commandsText, string member)
        {
            var m = Regex.Match(commandsText, @"OrthogonalOp\s+" + Regex.Escape(member) + @"\s*=\s*new\(\s*""[^""]*""\s*,\s*""([^""]*)""");
            return m.Success ? m.Groups[1].Value : null;
        }

        public static Extraction Scan(string text, string? commandsText = null)
        {
            var buttons = new List<Found>();
            var problems = new List<string>();
            var compact = new List<(string Full, string Short)>();
            void Problem(int offset, string why) => problems.Add($"MainWindow.cs line {LineAt(text, offset)}: {why} :: {LineText(text, offset)}");
            var result = new Extraction(buttons, problems, compact);

            foreach (Match m in FieldButton.Matches(text))
            {
                var i = m.Index + m.Length;
                if (ParseLiteral(text, ref i, out var label)) buttons.Add(new(label, FieldOrigin, LineAt(text, m.Index)));
                else Problem(m.Index, "header button Content is not a plain string literal");
            }

            var sig = text.IndexOf(ConstructorSignature, StringComparison.Ordinal);
            var open = sig < 0 ? -1 : text.IndexOf('{', sig + ConstructorSignature.Length);
            var close = open < 0 ? -1 : MatchClose(text, open);
            if (close < 0) { problems.Add("constructor not found or its braces do not match: " + ConstructorSignature); return result; }
            var body = Mask(text.Substring(open, close - open + 1));
            void P(int k, string why) => Problem(open + k, why);
            int Line(int k) => LineAt(text, open + k);

            foreach (Match m in Regex.Matches(body, @"\bAddButton\("))
            {
                var i = m.Index + m.Length;
                var first = FirstArg.Match(body, i);
                if (!first.Success) { P(m.Index, "AddButton call whose first argument is not a plain identifier"); continue; }
                i = first.Index + first.Length;
                if (ParseLiteral(body, ref i, out var label)) { buttons.Add(new(label, "AddButton", Line(m.Index))); continue; }
                var variable = VariableArg.Match(body, i);
                if (variable.Success && AllowedVariables.Contains(variable.Groups[1].Value)) continue;
                P(m.Index, "AddButton whose label is not a plain string literal (the button would be invisible to the catalogue check)");
            }

            foreach (Match m in OrthogonalCall.Matches(body))
            {
                var label = commandsText is null ? null : OrthogonalLabel(commandsText, m.Groups[1].Value);
                if (label is null) P(m.Index, "AddOrthogonalButton for an OrthogonalOps member whose label could not be read from MainWindow.Commands.cs");
                else buttons.Add(new(label, "AddOrthogonalButton", Line(m.Index)));
            }
            foreach (Match m in OtherHelper.Matches(body))
                P(m.Index, $"{m.Groups[1].Value}(...) is a button helper the scanner does not know");

            foreach (Match m in NewButton.Matches(body))
            {
                var init = ContentInit.Match(body, m.Index + m.Length);
                var i = init.Index + init.Length;
                if (init.Success && ParseLiteral(body, ref i, out var label)) buttons.Add(new(label, "new Button", Line(m.Index)));
                else P(m.Index, "new Button without a literal Content initializer");
            }

            var loop = ToolLoop.Match(body);
            if (loop.Success)
            {
                var i = loop.Index + loop.Length;
                while (true)
                {
                    while (i < body.Length && (char.IsWhiteSpace(body[i]) || body[i] == ',')) i++;
                    if (i >= body.Length || body[i] == '}') break;
                    var start = i;
                    if (!Expect(body, ref i, '(') || !ParseLiteral(body, ref i, out var tool) || !Expect(body, ref i, ',')
                        || !ParseLiteral(body, ref i, out _) || !Expect(body, ref i, ',') || !ParseLiteral(body, ref i, out _) || !Expect(body, ref i, ')'))
                    { P(start, "tool-loop tuple is not (\"label\", \"glyph\", \"shortcut\")"); break; }
                    buttons.Add(new(tool, "tool loop", Line(start)));
                }
            }

            foreach (Match m in ViewCall.Matches(body))
            {
                var i = m.Index + m.Length;
                if (ParseLiteral(body, ref i, out var glyph) && Expect(body, ref i, ',') && ParseLiteral(body, ref i, out var name))
                    buttons.Add(new(glyph, ViewButtonOrigin, Line(m.Index), name));
                else P(m.Index, "ViewButton call without two string literals");
            }

            var compactCalls = Regex.Matches(body, @"\bCompactLabel\(").Count;
            var compactMatches = CompactCall.Matches(body);
            if (compactCalls != compactMatches.Count) problems.Add($"MainWindow.cs: {compactCalls} CompactLabel calls in the constructor but only {compactMatches.Count} have the form CompactLabel(AddButton(...), \"short\")");
            foreach (Match m in compactMatches)
            {
                var paren = m.Index + m.Length - 1;
                var first = FirstArg.Match(body, paren + 1);
                var end = MatchClose(body, paren);
                var j = first.Index + first.Length;
                var k = end + 1;
                string full = "", shortLabel = "";
                if (first.Success && end > 0 && ParseLiteral(body, ref j, out full) && Expect(body, ref k, ',') && ParseLiteral(body, ref k, out shortLabel))
                    compact.Add((full, shortLabel));
                else P(m.Index, "CompactLabel(AddButton(...), ...) with a non-literal name");
            }
            return result;
        }

        /// <summary>Every plain-literal name passed to AutomationProperties.SetName anywhere in the text (concatenations are skipped).</summary>
        public static List<string> SetNameLiterals(string text)
        {
            var names = new List<string>();
            foreach (Match m in SetNameCall.Matches(text))
            {
                var i = m.Index + m.Length;
                if (ParseLiteral(text, ref i, out var name)) names.Add(name);
            }
            return names;
        }

        private static int LineAt(string text, int offset)
        {
            var line = 1;
            for (var i = 0; i < offset && i < text.Length; i++) if (text[i] == '\n') line++;
            return line;
        }

        private static string LineText(string text, int offset)
        {
            var start = text.LastIndexOf('\n', Math.Max(0, Math.Min(offset, text.Length - 1))) + 1;
            var end = text.IndexOf('\n', start);
            var line = text[start..(end < 0 ? text.Length : end)].Trim();
            return line.Length > 140 ? line[..140] + "…" : line;
        }

        /// <summary>The index after a string, verbatim string, char literal or comment that starts at <paramref name="i"/>; <paramref name="i"/> itself when none starts there.</summary>
        private static int SkipToken(string s, int i)
        {
            var c = s[i];
            var next = i + 1 < s.Length ? s[i + 1] : '\0';
            if (c == '/' && next == '/') { var e = s.IndexOf('\n', i); return e < 0 ? s.Length : e; }
            if (c == '/' && next == '*') { var e = s.IndexOf("*/", i + 2, StringComparison.Ordinal); return e < 0 ? s.Length : e + 2; }
            if (c == '@' && next == '"')
            {
                for (var j = i + 2; j < s.Length; j++)
                {
                    if (s[j] != '"') continue;
                    if (j + 1 < s.Length && s[j + 1] == '"') { j++; continue; }
                    return j + 1;
                }
                return s.Length;
            }
            if (c is '"' or '\'')
            {
                for (var j = i + 1; j < s.Length; j++)
                {
                    if (s[j] == '\\') j++;
                    else if (s[j] == c) return j + 1;
                    else if (s[j] == '\n') return j;
                }
                return s.Length;
            }
            return i;
        }

        /// <summary>The text with comments replaced by spaces (newlines kept), so offsets and line numbers stay valid.</summary>
        private static string Mask(string s)
        {
            var sb = new StringBuilder(s.Length);
            for (var i = 0; i < s.Length;)
            {
                var t = SkipToken(s, i);
                if (t == i) { sb.Append(s[i++]); continue; }
                var comment = s[i] == '/';
                for (var k = i; k < t; k++) sb.Append(comment && s[k] != '\n' && s[k] != '\r' ? ' ' : s[k]);
                i = t;
            }
            return sb.ToString();
        }

        /// <summary>The index of the bracket closing the one at <paramref name="open"/> (one of { ( ), ignoring literals and comments, or -1.</summary>
        private static int MatchClose(string s, int open)
        {
            var o = s[open];
            var c = o == '{' ? '}' : ')';
            var depth = 0;
            for (var i = open; i < s.Length;)
            {
                var t = SkipToken(s, i);
                if (t > i) { i = t; continue; }
                if (s[i] == o) depth++;
                else if (s[i] == c && --depth == 0) return i;
                i++;
            }
            return -1;
        }

        private static bool Expect(string s, ref int i, char c)
        {
            var j = i;
            while (j < s.Length && char.IsWhiteSpace(s[j])) j++;
            if (j >= s.Length || s[j] != c) return false;
            i = j + 1;
            return true;
        }

        /// <summary>
        /// Reads a plain regular string literal after optional white space. Succeeds only when the closing quote is directly followed
        /// (after white space) by , ) or }, so <c>"a" + b</c> is rejected. On success <paramref name="i"/> is just after the closing quote.
        /// </summary>
        private static bool ParseLiteral(string s, ref int i, out string value)
        {
            value = "";
            var j = i;
            while (j < s.Length && char.IsWhiteSpace(s[j])) j++;
            if (j >= s.Length || s[j] != '"' || (j > 0 && s[j - 1] is '$' or '@') || (j + 2 < s.Length && s[j + 1] == '"' && s[j + 2] == '"')) return false;
            var sb = new StringBuilder();
            for (j++; j < s.Length && s[j] != '"'; j++)
            {
                if (s[j] == '\n') return false;
                if (s[j] != '\\') { sb.Append(s[j]); continue; }
                if (++j >= s.Length) return false;
                if (s[j] == 'u')
                {
                    if (j + 4 >= s.Length || !int.TryParse(s.AsSpan(j + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code)) return false;
                    sb.Append((char)code);
                    j += 4;
                    continue;
                }
                char? mapped = s[j] switch { '"' => '"', '\\' => '\\', '\'' => '\'', 'n' => '\n', 'r' => '\r', 't' => '\t', '0' => '\0', _ => null };
                if (mapped is null) return false;
                sb.Append(mapped.Value);
            }
            if (j >= s.Length) return false;
            var after = j + 1;
            var k = after;
            while (k < s.Length && char.IsWhiteSpace(s[k])) k++;
            if (k >= s.Length || s[k] is not (',' or ')' or '}')) return false;
            value = sb.ToString();
            i = after;
            return true;
        }
    }
}
