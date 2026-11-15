namespace TekstilCAD.Core.Commands;

/// <summary>The three things the parser can answer with.</summary>
public enum ParseKind
{
    /// <summary>Understood: <see cref="ParseResult.Command"/> is set.</summary>
    Command,
    /// <summary>A question the user answers with a button (today only R06, "milimetre mi, santimetre mi?").</summary>
    Question,
    /// <summary>Refused with a reason and an example; nothing was understood and nothing may change.</summary>
    Refusal,
}

/// <summary>
/// A button under a refusal or question. Pressing it only FILLS the command box with <see cref="FillText"/>; it never applies anything.
/// </summary>
public sealed record RefusalButton(string Label, string? Fill = null)
{
    /// <summary>The text put into the command box (the label when no separate fill text exists).</summary>
    public string FillText => Fill ?? Label;
}

/// <summary>A refusal or question with its code (R01-R28b), the full Turkish message and its buttons.</summary>
public sealed record Refusal(string Code, string Message, IReadOnlyList<RefusalButton> Buttons)
{
    /// <summary>True for a question rather than a plain refusal.</summary>
    public bool IsQuestion => Code == Refusals.UnitQuestion;

    /// <summary>The message text spells out an example, or at least one button offers one.</summary>
    public bool HasExample =>
        Buttons.Count > 0
        || Message.Contains("Örnek:", StringComparison.Ordinal)
        || Message.Contains("Şöyle yazabilirsiniz:", StringComparison.Ordinal);
}

/// <summary>Result of <see cref="CommandParser.Parse"/>. Exactly one of <see cref="Command"/> and <see cref="Notice"/> is set.</summary>
public sealed class ParseResult
{
    private ParseResult(ParseKind kind, Command? command, Refusal? notice, bool unitAssumed = false, bool incomplete = false)
    {
        Kind = kind;
        Command = command;
        Notice = notice;
        UnitAssumed = unitAssumed;
        IsIncomplete = incomplete;
    }

    /// <summary>
    /// True when the command carries a length and the text wrote no unit anywhere, so millimetres were assumed
    /// (a tailor may have meant centimetres). The planner shows "Birim yazmadınız; milimetre sayıldı." and offers a cm chip.
    /// </summary>
    public bool UnitAssumed { get; }

    /// <summary>The unit that was assumed: <see cref="LengthUnit.Millimeter"/> when <see cref="UnitAssumed"/>, otherwise null.</summary>
    public LengthUnit? AssumedUnit => UnitAssumed ? LengthUnit.Millimeter : null;

    /// <summary>
    /// True for a refusal whose text could still grow into a valid command (for example "120 x", "dikdört", "dikdörtgen 120").
    /// The kind and code stay those of the refusal that applies if the user presses Enter now; a command box stays quiet
    /// while this is true and shows <see cref="Notice"/> only on Enter. Always false for commands and questions.
    /// </summary>
    public bool IsIncomplete { get; }

    internal ParseResult WithUnitAssumed() => IsCommand && !UnitAssumed ? new(Kind, Command, Notice, true, IsIncomplete) : this;

    internal ParseResult AsIncomplete() => Kind == ParseKind.Refusal && !IsIncomplete ? new(Kind, Command, Notice, UnitAssumed, true) : this;

    public ParseKind Kind { get; }

    /// <summary>The understood command; null unless <see cref="Kind"/> is <see cref="ParseKind.Command"/>.</summary>
    public Command? Command { get; }

    /// <summary>The refusal or question; null when a command was understood.</summary>
    public Refusal? Notice { get; }

    public bool IsCommand => Kind == ParseKind.Command;

    /// <summary>The refusal/question code ("R05"), or null when a command was understood.</summary>
    public string? Code => Notice?.Code;

    public static ParseResult Understood(Command command) =>
        new(ParseKind.Command, command ?? throw new ArgumentNullException(nameof(command)), null);

    public static ParseResult Refused(Refusal notice)
    {
        ArgumentNullException.ThrowIfNull(notice);
        return new(notice.IsQuestion ? ParseKind.Question : ParseKind.Refusal, null, notice);
    }

    public override string ToString() => IsCommand ? Command!.ToString() : $"{Notice!.Code}: {Notice.Message}";
}

/// <summary>What the parser needs to know about the project. The parser never reads the document itself.</summary>
/// <param name="PieceNames">Current piece names in list order; used only to resolve "ön bedeni seç" / "kolu sil" and to tell a piece name from nonsense.</param>
/// <param name="DrawingInProgress">Kept for the executor's use; the parse result does not depend on it (a drawing in progress is refused by the planner, R07).</param>
public sealed record ParserContext(IReadOnlyList<string> PieceNames, bool DrawingInProgress = false)
{
    /// <summary>A project without pieces.</summary>
    public static ParserContext Empty { get; } = new(Array.Empty<string>());
}
