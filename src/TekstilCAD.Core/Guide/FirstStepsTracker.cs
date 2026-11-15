using System.Text;
using System.Text.Json;

namespace TekstilCAD.Core.Guide;

/// <summary>One item of the "İlk adımlar" checklist.</summary>
public enum FirstStep
{
    DrawPiece = 0,
    ChangeSize = 1,
    MovePiece = 2,
    Undo = 3,
    Save = 4,
    ReopenFile = 5,
}

/// <summary>What happened in the program that may tick a checklist item (or hide the list).</summary>
public enum FirstStepEvent
{
    /// <summary>A piece was added to the project (any way).</summary>
    PieceDrawn = 0,
    /// <summary>A piece's width or height was changed on purpose.</summary>
    SizeChanged = 1,
    /// <summary>A piece was moved on purpose.</summary>
    PieceMoved = 2,
    /// <summary>An undo was performed.</summary>
    UndoDone = 3,
    /// <summary>The project was saved to a file.</summary>
    ProjectSaved = 4,
    /// <summary>A project file was opened. Counts only after a save has been seen.</summary>
    ProjectReopened = 5,
    /// <summary>The person pressed "Gizle".</summary>
    Dismissed = 6,
}

/// <summary>
/// The "İlk adımlar" checklist as an immutable value. <see cref="Observe"/> returns the next value and never
/// changes this one; observing is idempotent and monotonic (a done item never becomes undone).
/// No clock, no I/O. <see cref="ToJson"/> / <see cref="FromJson"/> give a tiny versioned document the
/// preferences file can embed as a string:
/// <code>{"v":1,"done":["draw","size"],"hidden":false}</code>
/// Item ids are stable: draw, size, move, undo, save, reopen. <see cref="FromJson"/> is tolerant: null, huge,
/// corrupt, wrongly typed or unknown-version input gives <see cref="Fresh"/>; unknown item ids and unknown
/// properties are ignored; it never throws.
/// </summary>
public sealed class FirstStepsTracker : IEquatable<FirstStepsTracker>
{
    /// <summary>Number of checklist items (the "n" in "İlk adımlar 0/n").</summary>
    public const int Total = 6;

    /// <summary>Current JSON schema version.</summary>
    public const int JsonVersion = 1;

    /// <summary>Longest JSON text <see cref="FromJson"/> reads; longer input is treated as corrupt. A real document is under 100 characters.</summary>
    public const int MaxJsonLength = 512;

    private static readonly string[] Ids = ["draw", "size", "move", "undo", "save", "reopen"];

    private static readonly string[] Labels =
    [
        "Parça çizin",
        "Ölçüsünü değiştirin",
        "Parçayı taşıyın",
        "Yaptığınızı geri alın",
        "Çalışmayı kaydedin",
        "Dosyayı yeniden açın",
    ];

    private readonly int mask;
    private readonly bool dismissed;

    private FirstStepsTracker(int mask, bool dismissed)
    {
        this.mask = mask;
        this.dismissed = dismissed;
    }

    /// <summary>A new person: nothing done, list visible.</summary>
    public static FirstStepsTracker Fresh { get; } = new(0, false);

    private static int Bit(FirstStep step) => 1 << (int)step;

    /// <summary>The item is ticked.</summary>
    public bool IsDone(FirstStep step) => Enum.IsDefined(step) && (mask & Bit(step)) != 0;

    /// <summary>How many items are ticked (0 to <see cref="Total"/>).</summary>
    public int DoneCount => int.PopCount(mask);

    /// <summary>All items are ticked.</summary>
    public bool IsComplete => DoneCount == Total;

    /// <summary>The person pressed "Gizle".</summary>
    public bool IsDismissed => dismissed;

    /// <summary>The checklist should be shown: neither finished nor dismissed.</summary>
    public bool IsVisible => !dismissed && !IsComplete;

    /// <summary>The person has already changed a size or moved a piece (feeds <see cref="GuideSituation.HasTriedEditing"/>).</summary>
    public bool HasTriedEditing => IsDone(FirstStep.ChangeSize) || IsDone(FirstStep.MovePiece);

    /// <summary>The first item that is not ticked yet, or null when all are.</summary>
    public FirstStep? NextPending
    {
        get
        {
            for (var i = 0; i < Total; i++)
            {
                if ((mask & (1 << i)) == 0)
                {
                    return (FirstStep)i;
                }
            }
            return null;
        }
    }

    /// <summary>The header text, for example "İlk adımlar 2/6".</summary>
    public string Heading => $"İlk adımlar {DoneCount}/{Total}";

    /// <summary>Turkish label of an item (no terminal punctuation).</summary>
    public static string LabelOf(FirstStep step) =>
        Enum.IsDefined(step) ? Labels[(int)step] : throw new ArgumentOutOfRangeException(nameof(step));

    /// <summary>Stable text of the "Gizle" link.</summary>
    public const string DismissLabel = "Gizle";

    /// <summary>All item labels in display order.</summary>
    public static IReadOnlyList<string> AllLabels => Labels;

    /// <summary>Returns the state after the event. Unknown events and events that do not apply return the same instance.</summary>
    public FirstStepsTracker Observe(FirstStepEvent observed)
    {
        switch (observed)
        {
            case FirstStepEvent.PieceDrawn: return Tick(FirstStep.DrawPiece);
            case FirstStepEvent.SizeChanged: return Tick(FirstStep.ChangeSize);
            case FirstStepEvent.PieceMoved: return Tick(FirstStep.MovePiece);
            case FirstStepEvent.UndoDone: return Tick(FirstStep.Undo);
            case FirstStepEvent.ProjectSaved: return Tick(FirstStep.Save);
            case FirstStepEvent.ProjectReopened:
                // Opening "the file again" only means something once the person has saved one.
                return IsDone(FirstStep.Save) ? Tick(FirstStep.ReopenFile) : this;
            case FirstStepEvent.Dismissed:
                return dismissed ? this : new FirstStepsTracker(mask, true);
            default:
                return this;
        }
    }

    private FirstStepsTracker Tick(FirstStep step) =>
        (mask & Bit(step)) != 0 ? this : new FirstStepsTracker(mask | Bit(step), dismissed);

    /// <summary>Serialises to the versioned JSON document. Always valid and shorter than <see cref="MaxJsonLength"/>.</summary>
    public string ToJson()
    {
        var sb = new StringBuilder(64);
        sb.Append("{\"v\":").Append(JsonVersion).Append(",\"done\":[");
        var first = true;
        for (var i = 0; i < Total; i++)
        {
            if ((mask & (1 << i)) == 0)
            {
                continue;
            }
            if (!first)
            {
                sb.Append(',');
            }
            first = false;
            sb.Append('"').Append(Ids[i]).Append('"');
        }
        sb.Append("],\"hidden\":").Append(dismissed ? "true" : "false").Append('}');
        return sb.ToString();
    }

    /// <summary>Reads a document written by <see cref="ToJson"/>. Never throws; anything unreadable gives <see cref="Fresh"/>.</summary>
    public static FirstStepsTracker FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > MaxJsonLength)
        {
            return Fresh;
        }
        try
        {
            var options = new JsonDocumentOptions { MaxDepth = 4, AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow };
            using var doc = JsonDocument.Parse(json, options);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("v", out var version)
                || version.ValueKind != JsonValueKind.Number
                || !version.TryGetInt32(out var v)
                || v != JsonVersion)
            {
                return Fresh;
            }
            var newMask = 0;
            if (root.TryGetProperty("done", out var done) && done.ValueKind == JsonValueKind.Array)
            {
                var seen = 0;
                foreach (var item in done.EnumerateArray())
                {
                    if (++seen > 64)
                    {
                        return Fresh;
                    }
                    if (item.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }
                    var index = Array.IndexOf(Ids, item.GetString());
                    if (index >= 0)
                    {
                        newMask |= 1 << index;
                    }
                }
            }
            // Same rule as Observe: "reopen" means nothing without "save".
            if ((newMask & Bit(FirstStep.Save)) == 0)
            {
                newMask &= ~Bit(FirstStep.ReopenFile);
            }
            var hidden = root.TryGetProperty("hidden", out var h) && h.ValueKind == JsonValueKind.True;
            return newMask == 0 && !hidden ? Fresh : new FirstStepsTracker(newMask, hidden);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return Fresh;
        }
    }

    public bool Equals(FirstStepsTracker? other) => other is not null && other.mask == mask && other.dismissed == dismissed;

    public override bool Equals(object? obj) => Equals(obj as FirstStepsTracker);

    public override int GetHashCode() => HashCode.Combine(mask, dismissed);

    public override string ToString() => ToJson();
}
