using System.Text.Json;

namespace TekstilCAD.Core.Theme;

/// <summary>
/// Interface preferences stored in <c>ui.json</c>: the layout (<c>view</c>), the colour theme (<c>theme</c>), the text scale
/// (<c>textScale</c>, percent), the recent project files (<c>recent</c>) and the completed first steps (<c>firstSteps</c>).
/// The file carries a <c>schema</c> number (see <see cref="CurrentSchema"/>). The file is tiny and optional: a missing, oversized, corrupt or partly wrong file yields the defaults for what is wrong and
/// keeps what is valid. It is read only by the interactive start path, never by a window constructor, so headless verification
/// does not depend on it. Writing goes through a temporary file so a crash never leaves a half-written file.
/// </summary>
public sealed record UiPreferences
{
    public const string FileName = "ui.json";
    public const int MaximumBytes = 64 * 1024;
    public const int MaximumRecent = 8;
    public const string DetailedView = "detailed";
    public const string SimpleView = "simple";

    /// <summary>
    /// The schema number this build writes. A file without it (or with a smaller one) comes from a build whose defaults were
    /// "detailed" + "Dark": that exact pair is read as "never chose" and moves to the new defaults. From schema 2 on, every value is
    /// the user's own choice and is kept.
    /// </summary>
    public const int CurrentSchema = 2;

    /// <summary>"simple" (the default) or "detailed" (the original dense layout).</summary>
    public string View { get; init; } = SimpleView;

    /// <summary>"Light" (the default) or "Dark"; the names of <see cref="ThemePalette"/>.</summary>
    public string Theme { get; init; } = ThemePalette.Light.Name;

    /// <summary>Text scale in percent: 100, 115 or 130.</summary>
    public int TextScale { get; init; } = 100;

    /// <summary>Recent project files, newest first, at most <see cref="MaximumRecent"/>.</summary>
    public IReadOnlyList<string> Recent { get; init; } = [];

    /// <summary>Identifiers of the first-step tips the user has finished.</summary>
    public IReadOnlyList<string> FirstSteps { get; init; } = [];

    public ThemePalette Palette => ThemePalette.ByName(Theme) ?? ThemePalette.Light;

    /// <summary>Reads preferences from JSON text; never throws. Invalid parts fall back to their defaults.</summary>
    public static UiPreferences Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > MaximumBytes) return new UiPreferences();
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
            if (document.RootElement.ValueKind != JsonValueKind.Object) return new UiPreferences();
            var root = document.RootElement;
            var view = Text(root, "view");
            var theme = Text(root, "theme");
            var scale = root.TryGetProperty("textScale", out var scaleElement) && scaleElement.ValueKind == JsonValueKind.Number && scaleElement.TryGetInt32(out var percent) ? percent : 100;
            var schema = root.TryGetProperty("schema", out var schemaElement) && schemaElement.ValueKind == JsonValueKind.Number && schemaElement.TryGetInt32(out var number) ? number : 1;
            var neverChose = schema < CurrentSchema && view == DetailedView && ThemePalette.ByName(theme) == ThemePalette.Dark;
            return new UiPreferences
            {
                View = !neverChose && view == DetailedView ? DetailedView : SimpleView,
                Theme = !neverChose && ThemePalette.ByName(theme) is { } palette ? palette.Name : ThemePalette.Light.Name,
                TextScale = scale is 100 or 115 or 130 ? scale : 100,
                Recent = RecentProjects.Normalize(Strings(root, "recent")),
                FirstSteps = Strings(root, "firstSteps").Where(step => step.Length is > 0 and <= 64).Distinct(StringComparer.Ordinal).Take(64).ToArray(),
            };
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException) { return new UiPreferences(); }
    }

    /// <summary>The JSON text of these preferences.</summary>
    public string ToJson() => JsonSerializer.Serialize(new Dictionary<string, object>
    {
        ["schema"] = CurrentSchema,
        ["view"] = View,
        ["theme"] = Theme,
        ["textScale"] = TextScale,
        ["recent"] = Recent,
        ["firstSteps"] = FirstSteps,
    }, new JsonSerializerOptions { WriteIndented = true });

    /// <summary>Reads a preferences file; never throws.</summary>
    public static UiPreferences Load(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists && info.Length <= MaximumBytes ? Parse(File.ReadAllText(path)) : new UiPreferences();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException or NotSupportedException)
        {
            return new UiPreferences();
        }
    }

    /// <summary>Writes the file through a temporary file in the same folder; returns false when it could not be written.</summary>
    public bool Save(string path)
    {
        string? temporary = null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temporary, ToJson());
            File.Move(temporary, path, overwrite: true);
            temporary = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException or NotSupportedException)
        {
            return false;
        }
        finally
        {
            if (temporary is not null) { try { File.Delete(temporary); } catch (Exception) { /* the stray temporary file is harmless */ } }
        }
    }

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String ? element.GetString() : null;

    private static IEnumerable<string> Strings(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.Array) yield break;
        foreach (var item in element.EnumerateArray())
            if (item.ValueKind == JsonValueKind.String && item.GetString() is { } text) yield return text;
    }
}
