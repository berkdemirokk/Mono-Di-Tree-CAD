using System.Text.Json;
using TekstilCAD.Core.Commands;

namespace TekstilCAD.Tests;

/// <summary>
/// Runs every case of the shared, language-neutral vector file (Apple/Tests/Fixtures/command-vectors.json) against the
/// parser. The iPhone app reads the same file, so the expectation is written in neutral terms (see the file's "_comment").
/// </summary>
public sealed class CommandVectorsTests
{
    private static readonly Lazy<JsonDocument> Document = new(() =>
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "command-vectors.json");
        return JsonDocument.Parse(File.ReadAllBytes(path));
    });

    public static IEnumerable<object[]> Cases()
    {
        var cases = Document.Value.RootElement.GetProperty("cases");
        var index = 0;
        foreach (var c in cases.EnumerateArray())
        {
            yield return new object[] { index, c.GetProperty("input").GetString() ?? string.Empty };
            index++;
        }
    }

    [Fact]
    public void VectorFileHasTheDocumentedShapeAndEnoughCases()
    {
        var root = Document.Value.RootElement;
        Assert.True(root.TryGetProperty("_comment", out var comment));
        Assert.False(string.IsNullOrWhiteSpace(comment.GetString()));
        Assert.True(root.TryGetProperty("contexts", out var contexts));
        Assert.True(contexts.TryGetProperty("B0", out _));
        Assert.True(contexts.TryGetProperty("B1", out _));
        Assert.True(contexts.TryGetProperty("B8", out _));
        var cases = root.GetProperty("cases");
        Assert.True(cases.GetArrayLength() >= 400, "the vector file must cover the 60 phrases and the table-driven cases");
        foreach (var c in cases.EnumerateArray())
        {
            Assert.True(c.TryGetProperty("input", out _));
            Assert.True(c.TryGetProperty("context", out var ctx));
            Assert.True(contexts.TryGetProperty(ctx.GetString()!, out _), $"unknown context in {c}");
            var expect = c.GetProperty("expect");
            Assert.True(expect.TryGetProperty("kind", out _) ^ expect.TryGetProperty("refusal", out _), $"expect needs exactly one of kind/refusal: {c}");
        }
    }

    [Fact]
    public void VectorFileHasNoByteOrderMark()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "command-vectors.json");
        var bytes = File.ReadAllBytes(path);
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void EveryVectorMatchesTheParser(int index, string input)
    {
        var root = Document.Value.RootElement;
        var c = root.GetProperty("cases")[index];
        Assert.Equal(input, c.GetProperty("input").GetString());
        var contextName = c.GetProperty("context").GetString()!;
        var names = root.GetProperty("contexts").GetProperty(contextName).EnumerateArray().Select(e => e.GetString()!).ToArray();

        var result = CommandParser.Parse(input, new ParserContext(names));

        var failure = Compare(c.GetProperty("expect"), result);
        Assert.True(failure is null, $"[{index}] \"{input}\" ({contextName}): {failure}");
    }

    private static string? Compare(JsonElement expect, ParseResult result)
    {
        if (expect.TryGetProperty("refusal", out var code))
        {
            if (result.IsCommand) return $"expected refusal {code.GetString()}, got command {Describe(result.Command!)}";
            if (result.Code != code.GetString()) return $"expected refusal {code.GetString()}, got {result.Code}: {result.Notice!.Message}";
            if (expect.TryGetProperty("incomplete", out var inc) && inc.GetBoolean() != result.IsIncomplete) return $"incomplete: expected {inc.GetBoolean()}, got {result.IsIncomplete}";
            if (expect.TryGetProperty("buttons", out var buttons))
            {
                var want = buttons.EnumerateArray().Select(b => b.GetString()).ToArray();
                var got = result.Notice!.Buttons.Select(b => b.Label).ToArray();
                if (!want.SequenceEqual(got)) return $"buttons: expected [{string.Join(" | ", want)}], got [{string.Join(" | ", got)}]";
            }
            if (expect.TryGetProperty("fills", out var fills))
            {
                var want = fills.EnumerateArray().Select(b => b.GetString()).ToArray();
                var got = result.Notice!.Buttons.Select(b => b.FillText).ToArray();
                if (!want.SequenceEqual(got)) return $"fills: expected [{string.Join(" | ", want)}], got [{string.Join(" | ", got)}]";
            }
            return null;
        }

        if (!result.IsCommand) return $"expected {expect.GetProperty("kind").GetString()}, got refusal {result.Code}: {result.Notice!.Message}";
        var actual = Canonical(result.Command!);
        var kind = expect.GetProperty("kind").GetString();
        if (!string.Equals((string?)actual["kind"], kind, StringComparison.Ordinal)) return $"expected kind {kind}, got {Describe(result.Command!)}";

        if (expect.TryGetProperty("unitAssumed", out var assumed) && assumed.GetBoolean() != result.UnitAssumed) return $"unitAssumed: expected {assumed.GetBoolean()}, got {result.UnitAssumed}";
        var keys = new HashSet<string>(actual.Keys);
        foreach (var p in expect.EnumerateObject()) keys.Add(p.Name);
        foreach (var key in keys)
        {
            if (key is "kind" or "unitAssumed") continue;
            actual.TryGetValue(key, out var have);
            JsonElement want = default;
            var hasWant = expect.TryGetProperty(key, out want) && want.ValueKind != JsonValueKind.Null;
            if (!hasWant && have is null) continue;
            if (!hasWant || have is null) return $"field {key}: expected {(hasWant ? want.ToString() : "absent")}, got {Describe(result.Command!)}";
            switch (have)
            {
                case double d:
                    if (want.ValueKind != JsonValueKind.Number || Math.Abs(want.GetDouble() - d) > 1e-9) return $"field {key}: expected {want}, got {d}";
                    break;
                case int i:
                    if (want.ValueKind != JsonValueKind.Number || want.GetInt32() != i) return $"field {key}: expected {want}, got {i}";
                    break;
                case bool b:
                    if (want.ValueKind is not (JsonValueKind.True or JsonValueKind.False) || want.GetBoolean() != b) return $"field {key}: expected {want}, got {b}";
                    break;
                case string text:
                    if (want.ValueKind != JsonValueKind.String || want.GetString() != text) return $"field {key}: expected {want}, got {text}";
                    break;
            }
        }
        return null;
    }

    private static string Describe(Command command) =>
        string.Join(", ", Canonical(command).Where(p => p.Value is not null).Select(p => $"{p.Key}={p.Value}"));

    private static string Camel(Enum value)
    {
        var name = value.ToString();
        return char.ToLowerInvariant(name[0]) + name.Substring(1);
    }

    /// <summary>The neutral field names and values documented in the vector file; absent optional fields are null.</summary>
    private static Dictionary<string, object?> Canonical(Command command)
    {
        var d = new Dictionary<string, object?>();
        switch (command)
        {
            case RectangleCommand r:
                d["kind"] = "Rect"; d["width"] = r.WidthMm; d["height"] = r.HeightMm; d["square"] = r.IsSquare ? true : null; break;
            case LineCommand l:
                d["kind"] = "Line"; d["length"] = l.LengthMm; d["orientation"] = Camel(l.Orientation); break;
            case MoveByCommand m:
                d["kind"] = "Move"; d["right"] = m.RightMm; d["down"] = m.DownMm; break;
            case MoveToCommand m:
                d["kind"] = "MoveTo"; d["x"] = m.XMm; d["y"] = m.YMm; break;
            case RotateCommand r:
                d["kind"] = "Rotate"; d["degrees"] = r.Degrees; d["direction"] = r.Direction == RotationDirection.Clockwise ? "clockwise" : "counterclockwise"; break;
            case MirrorCommand m:
                d["kind"] = "Mirror"; d["axis"] = Camel(m.Axis); break;
            case ResizeCommand r:
                d["kind"] = "Resize"; d["width"] = r.WidthMm; d["height"] = r.HeightMm; break;
            case RenameCommand r:
                d["kind"] = "Rename"; d["name"] = r.Name; break;
            case DeleteCommand x:
                d["kind"] = "Delete"; d["target"] = Camel(x.Target); d["name"] = x.Name; break;
            case SelectCommand x:
                d["kind"] = "Select"; d["target"] = Camel(x.Target); d["name"] = x.Name; break;
            case UndoCommand u:
                d["kind"] = "Undo"; d["steps"] = u.Steps; break;
            case RedoCommand:
                d["kind"] = "Redo"; break;
            case ViewCommand v:
                d["kind"] = "View"; d["action"] = Camel(v.Action); break;
            case ToolCommand t:
                d["kind"] = "Tool"; d["tool"] = Camel(t.Tool); break;
            case FinishDrawingCommand f:
                d["kind"] = "FinishDrawing"; d["action"] = Camel(f.Action); break;
            case FileCommand f:
                d["kind"] = "File"; d["action"] = Camel(f.Action); break;
            case HandoffCommand h:
                d["kind"] = "Handoff"; d["target"] = Camel(h.Target); d["tab"] = h.Tab; d["value"] = h.Value; break;
            case HelpCommand:
                d["kind"] = "Help"; break;
            case CancelPreviewCommand:
                d["kind"] = "CancelPreview"; break;
            default:
                d["kind"] = command.GetType().Name; break;
        }
        return d;
    }
}
