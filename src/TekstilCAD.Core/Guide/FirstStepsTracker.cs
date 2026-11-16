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
    /// <summary>A piece was added by the person (typed, drawn or imported). Do not send it when the built-in example is loaded.</summary>
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
/// Item ids are stable: draw, size, move, undo, save, reopen. <see cref="FromJson"/> is tolerant: null, huge
/// (over <see cref="MaxJsonLength"/>), corrupt, not-an-object, missing or unknown-version (not 1) input and
/// documents that repeat a property name give <see cref="Fresh"/>. Inside a valid version-1 document a missing or
/// wrongly typed "done" or "hidden" falls back to its default, and unknown item ids and unknown properties are
/// ignored. It never throws (out-of-memory excepted). A preferences file that prefers a plain array can use
/// <see cref="DoneIds"/>, <see cref="IsDismissed"/> and <see cref="FromParts"/> instead of the JSON string.
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
        "Son işlemi geri alın",
        "Çalışmayı kaydedin",
        "Kaydettiğiniz dosyayı yeniden açın",
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
    public static IReadOnlyList<string> AllLabels { get; } = Array.AsReadOnly((string[])Labels.Clone());

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
            var options = new JsonDocumentOptions { MaxDepth = 8, AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow };
            using var doc = JsonDocument.Parse(json, options);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Fresh;
            }
            // One pass over the properties; a repeated name (even spelled with escapes) makes the document ambiguous.
            var names = new HashSet<string>(StringComparer.Ordinal);
            var version = 0;
            var versionSeen = false;
            List<string>? ids = null;
            var hidden = false;
            foreach (var property in root.EnumerateObject())
            {
                if (!names.Add(property.Name) || names.Count > 64)
                {
                    return Fresh;
                }
                switch (property.Name)
                {
                    case "v":
                        if (property.Value.ValueKind != JsonValueKind.Number || !property.Value.TryGetInt32(out version))
                        {
                            return Fresh;
                        }
                        versionSeen = true;
                        break;
                    case "done" when property.Value.ValueKind == JsonValueKind.Array:
                        ids = new List<string>();
                        foreach (var item in property.Value.EnumerateArray())
                        {
                            if (ids.Count >= 64)
                            {
                                return Fresh;
                            }
                            // Non-string items are kept as an empty placeholder so they count toward the limit but name no item.
                            ids.Add(item.ValueKind == JsonValueKind.String ? item.GetString() ?? "" : "");
                        }
                        break;
                    case "hidden":
                        hidden = property.Value.ValueKind == JsonValueKind.True;
                        break;
                }
            }
            return !versionSeen || version != JsonVersion ? Fresh : FromParts(ids, hidden);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return Fresh;
        }
    }

    /// <summary>The stable ids of the ticked items in display order (draw, size, move, undo, save, reopen), for a preferences file that stores them as a plain array.</summary>
    public IReadOnlyList<string> DoneIds
    {
        get
        {
            var list = new List<string>(Total);
            for (var i = 0; i < Total; i++)
            {
                if ((mask & (1 << i)) != 0)
                {
                    list.Add(Ids[i]);
                }
            }
            return list;
        }
    }

    /// <summary>
    /// Builds a state from stored parts (the shape <see cref="DoneIds"/> and <see cref="IsDismissed"/> give).
    /// Unknown ids are ignored, duplicates collapse, and "reopen" without "save" is dropped. Never throws.
    /// </summary>
    public static FirstStepsTracker FromParts(IEnumerable<string?>? doneIds, bool dismissed)
    {
        var newMask = 0;
        if (doneIds is not null)
        {
            var seen = 0;
            foreach (var id in doneIds)
            {
                if (++seen > 64)
                {
                    return Fresh;
                }
                var index = id is null ? -1 : Array.IndexOf(Ids, id);
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
        return newMask == 0 && !dismissed ? Fresh : new FirstStepsTracker(newMask, dismissed);
    }

    public bool Equals(FirstStepsTracker? other) => other is not null && other.mask == mask && other.dismissed == dismissed;

    public override bool Equals(object? obj) => Equals(obj as FirstStepsTracker);

    public override int GetHashCode() => HashCode.Combine(mask, dismissed);

    public override string ToString() => ToJson();
}
