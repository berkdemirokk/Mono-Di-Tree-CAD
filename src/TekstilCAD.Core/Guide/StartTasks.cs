namespace TekstilCAD.Core.Guide;

/// <summary>One of the four buttons under "Nereden başlayalım?".</summary>
/// <param name="Id">Stable identifier: type-size, open-file, try-example, continue-last.</param>
/// <param name="Title">Button title, no terminal punctuation.</param>
/// <param name="Description">Second line of the button (for continue-last: the last file's name), no terminal punctuation.</param>
/// <param name="Enabled">False when the task cannot be done now; <paramref name="DisabledReason"/> then says why.</param>
/// <param name="DisabledReason">Why the task is off, a sentence that ends with a period; null when enabled.</param>
/// <param name="SuggestedCommand">A command the command box may be filled with instead of acting, or null.</param>
public sealed record StartTask(string Id, string Title, string Description, bool Enabled, string? DisabledReason, string? SuggestedCommand);

/// <summary>
/// The four start tasks of an empty project. Pure: the window decides what a click does; this only says which
/// tasks exist, how they read and which are available.
/// </summary>
public static class StartTasks
{
    /// <summary>Heading above the tasks.</summary>
    public const string Heading = "Nereden başlayalım?";

    /// <summary>Longest file name shown in the description of "Kaldığım yerden devam et".</summary>
    public const int MaxFileNameLength = 60;

    internal const string TypeSizeTitle = "Ölçü yazarak başla";
    internal const string TypeSizeDescription = "Kutuya 120 x 80 dikdörtgen yazın";
    internal const string OpenFileTitle = "Dosya aç";
    internal const string OpenFileDescription = "Kayıtlı bir projeyi seçin";
    internal const string TryExampleTitle = "Örnekle dene";
    internal const string TryExampleDescription = "Hazır bir örnek açılır";
    internal const string TryExampleDisabledReason = "Örnek yalnız boş ve temiz bir projeye yüklenir.";
    internal const string ContinueTitle = "Kaldığım yerden devam et";
    internal const string ContinueNoFileDescription = "Son çalışmanız burada görünür";
    internal const string ContinueDisabledReason = "Henüz kayıtlı bir çalışma yok.";

    /// <summary>
    /// The tasks to show for the situation: four of them when the project has no pieces, otherwise none.
    /// </summary>
    /// <param name="situation">What the person is doing now.</param>
    /// <param name="recentFileName">File name (not a path) of the most recent saved work, or null when there is none.</param>
    public static IReadOnlyList<StartTask> For(GuideSituation situation, string? recentFileName)
    {
        ArgumentNullException.ThrowIfNull(situation);
        if (!(situation.IsEmptyProject || situation.PieceCount <= 0))
        {
            return [];
        }
        var clean = !situation.HasUnsavedChanges && !situation.FilePathKnown && !situation.HasPendingDrawing
            && !situation.HasPendingPreview;
        var recent = CleanFileName(recentFileName);
        return
        [
            new StartTask("type-size", TypeSizeTitle, TypeSizeDescription, true, null, "120 x 80 dikdörtgen"),
            new StartTask("open-file", OpenFileTitle, OpenFileDescription, true, null, "aç"),
            new StartTask("try-example", TryExampleTitle, TryExampleDescription, clean,
                clean ? null : TryExampleDisabledReason, null),
            new StartTask("continue-last", ContinueTitle,
                recent ?? ContinueNoFileDescription, recent is not null,
                recent is not null ? null : ContinueDisabledReason, null),
        ];
    }

    /// <summary>Strips folders and control characters and shortens a name; returns null when nothing usable is left.</summary>
    internal static string? CleanFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }
        var cut = name.LastIndexOfAny(['\\', '/']);
        var text = cut >= 0 ? name[(cut + 1)..] : name;
        var chars = text.Where(c => !char.IsControl(c)).ToArray();
        var cleaned = new string(chars).Trim();
        if (cleaned.Length == 0)
        {
            return null;
        }
        if (cleaned.Length > MaxFileNameLength)
        {
            var head = cleaned[..(MaxFileNameLength - 1)];
            if (char.IsHighSurrogate(head[^1]))
            {
                head = head[..^1];
            }
            cleaned = head + "…";
        }
        return cleaned;
    }

    /// <summary>Every fixed string this class can show, for copy checks.</summary>
    public static IReadOnlyList<string> FixedStrings { get; } =
    [
        Heading, TypeSizeTitle, TypeSizeDescription, OpenFileTitle, OpenFileDescription, TryExampleTitle,
        TryExampleDescription, TryExampleDisabledReason, ContinueTitle, ContinueNoFileDescription, ContinueDisabledReason,
    ];
}
