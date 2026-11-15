namespace TekstilCAD.Core.Commands;

/// <summary>
/// The Turkish command box parser: pure, deterministic, offline and rule based. It reads one line of text
/// (typed or dictated) and answers with a <see cref="Command"/>, a question or a refusal that carries an example.
/// It never touches a document, window, file or clock, never depends on the current culture, never guesses
/// (an unclear line is refused) and never throws. Grammar: tasarim/FINAL_COMMANDS.md.
/// </summary>
public static partial class CommandParser
{
    /// <summary>Smallest and largest length a drawing or resizing command accepts, in millimetres (R04).</summary>
    public const double MinLengthMm = 1;
    public const double MaxLengthMm = 20000;

    /// <summary>A bare number below this is asked about ("milimetre mi, santimetre mi?") when no unit is written (R06).</summary>
    public const double UnitQuestionBelow = 30;

    /// <summary>Largest number of steps "N adım geri" accepts.</summary>
    public const int MaxUndoSteps = 10;

    /// <summary>Longest piece name a rename accepts.</summary>
    public const int MaxNameLength = 128;

    /// <summary>
    /// Parses one line. <paramref name="context"/> only supplies the current piece names (to resolve "ön bedeni seç");
    /// without it the project is taken as empty. Never throws.
    /// </summary>
    public static ParseResult Parse(string? text, ParserContext? context = null)
    {
        try
        {
            return ParseCore(text ?? string.Empty, context ?? ParserContext.Empty);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            var echo = text is null ? string.Empty : TurkishText.Normalize(text.Length > TurkishText.MaxInputLength ? text.Substring(0, TurkishText.MaxInputLength) : text);
            return ParseResult.Refused(Refusals.Unrecognized(echo));
        }
    }

    private enum Fam { Draw, Move, Rotate, Mirror, Resize, Delete, Select, Undo, Seam, Marker, Meta }

    private sealed class State
    {
        public required string Norm;
        public required string Folded;
        public required IReadOnlyList<string> Names;
        public List<Tok> Raw = new();
        public List<Tok> Items = new();
    }

    private static ParseResult ParseCore(string text, ParserContext context)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0) return ParseResult.Refused(Refusals.Unrecognized(string.Empty));
        if (trimmed.Length > TurkishText.MaxInputLength) return ParseResult.Refused(Refusals.TextTooLong());

        var norm = TurkishText.Normalize(trimmed);
        if (norm.Length == 0) return ParseResult.Refused(Refusals.Unrecognized(string.Empty));
        var folded = TurkishText.Fold(norm);
        var state = new State { Norm = norm, Folded = folded, Names = context.PieceNames ?? Array.Empty<string>() };
        var lexed = Lex(norm, folded);

        // 1. Rename claims the whole line: "adı Ön Beden olsun".
        var rename = TryRename(state, lexed);
        if (rename is not null) return rename;

        // 2. Filler words are ignored.
        state.Raw = RemoveFillers(lexed);

        // 3. Whole phrases ("geri al", "dxf al", "çizimi kapat").
        var phrase = TryPhrase(state.Raw);
        if (phrase is not null) return phrase;

        // 4. "ön bedeni seç", "kolu sil".
        var named = TryNamedPiece(state);
        if (named is not null) return named;

        // 5. Everything else is built from numbers, units, directions and keywords.
        state.Items = BuildItems(state.Raw);
        return Classify(state);
    }

    // ================================================================================================================
    // Rename
    // ================================================================================================================

    private static ParseResult? TryRename(State s, List<Tok> lexed)
    {
        var index = 0;
        while (index < lexed.Count && lexed[index].Kind == TokKind.Word && FillerWords.Contains(lexed[index].Text)) index++;
        if (index >= lexed.Count) return null;
        var label = lexed[index];
        if (label.Kind != TokKind.Word || !RenameLabels.Contains(label.Text)) return null;

        var endIndex = lexed.Count - 1;
        var nameEnd = s.Norm.Length;
        if (endIndex > index && lexed[endIndex].Kind == TokKind.Word && RenameVerbs.Contains(lexed[endIndex].Text))
        {
            nameEnd = lexed[endIndex].Start;
            endIndex--;
            if (endIndex > index && lexed[endIndex].Kind == TokKind.Word && lexed[endIndex].Text == "olarak")
                nameEnd = lexed[endIndex].Start;
        }
        else if (endIndex == index)
        {
            return ParseResult.Refused(Refusals.EmptyNameRefusal());
        }

        var from = label.End;
        if (nameEnd < from) nameEnd = from;
        var name = s.Norm.Substring(from, nameEnd - from)
            .TrimStart(' ', ':', '=', '\'', '"', '“', '”')
            .TrimEnd(' ', '\'', '"', '“', '”');
        var hasLetterOrDigit = false;
        foreach (var c in name)
        {
            if (char.IsLetterOrDigit(c)) { hasLetterOrDigit = true; break; }
        }
        if (!hasLetterOrDigit) return ParseResult.Refused(Refusals.EmptyNameRefusal());
        if (name.Length > MaxNameLength) return ParseResult.Refused(Refusals.Unrecognized(s.Norm));
        return ParseResult.Understood(new RenameCommand(name));
    }

    // ================================================================================================================
    // Fillers and phrases
    // ================================================================================================================

    private static List<Tok> RemoveFillers(List<Tok> lexed)
    {
        var result = new List<Tok>(lexed.Count);
        for (var i = 0; i < lexed.Count; i++)
        {
            var t = lexed[i];
            if (t.Kind == TokKind.Word)
            {
                if (FillerWords.Contains(t.Text)) continue;
                if (t.Text == "rica" && i + 1 < lexed.Count && lexed[i + 1].IsWord("ederim")) { i++; continue; }
                if (t.Text == "bir" && i + 1 < lexed.Count && lexed[i + 1].IsWord("tane")) { i++; continue; }
            }
            result.Add(t);
        }
        return result;
    }

    private static ParseResult? TryPhrase(List<Tok> raw)
    {
        if (raw.Count == 0) return null;
        foreach (var t in raw) if (t.Kind != TokKind.Word) return null;
        var key = string.Join(' ', raw.Select(t => t.Text));
        if (Phrases.TryGetValue(key, out var hit)) return hit;
        if (raw.Count > 1 && TrailingVerbs.Contains(raw[^1].Text))
        {
            var shorter = string.Join(' ', raw.Take(raw.Count - 1).Select(t => t.Text));
            if (Phrases.TryGetValue(shorter, out hit)) return hit;
        }
        return null;
    }

    // ================================================================================================================
    // Pieces by name
    // ================================================================================================================

    private static ParseResult? TryNamedPiece(State s)
    {
        var raw = s.Raw;
        if (raw.Count < 2) return null;
        List<Tok> nameTokens;
        Tok verb;
        if (raw[^1].Kind == TokKind.Word && (DeleteVerbs.Contains(raw[^1].Text) || SelectVerbs.Contains(raw[^1].Text)))
        {
            verb = raw[^1];
            nameTokens = raw.Take(raw.Count - 1).ToList();
        }
        else if (raw[0].Kind == TokKind.Word && (DeleteVerbs.Contains(raw[0].Text) || SelectVerbs.Contains(raw[0].Text)))
        {
            verb = raw[0];
            nameTokens = raw.Skip(1).ToList();
        }
        else return null;

        var isDelete = DeleteVerbs.Contains(verb.Text);
        if (nameTokens.All(t => t.Kind == TokKind.Word && SelectedPieceWords.Contains(t.Text)))
            return isDelete ? ParseResult.Understood(new DeleteCommand(PieceTarget.Selected)) : null;

        var phrase = s.Norm.Substring(nameTokens[0].Start, nameTokens[^1].End - nameTokens[0].Start);
        var resolved = ResolvePieceName(phrase, s.Names);
        if (resolved.Matches.Count == 0) return null;
        var verbText = isDelete ? "sil" : "seç";
        if (resolved.Matches.Count > 1) return ParseResult.Refused(Refusals.NameAmbiguousFor(resolved.Matches, verbText));
        PieceCommandKind kind = isDelete ? PieceCommandKind.Delete : PieceCommandKind.Select;
        return ParseResult.Understood(kind == PieceCommandKind.Delete
            ? new DeleteCommand(PieceTarget.Named, resolved.Query)
            : new SelectCommand(PieceTarget.Named, resolved.Query));
    }

    private enum PieceCommandKind { Delete, Select }

    private readonly record struct NameMatch(IReadOnlyList<string> Matches, string Query);

    /// <summary>
    /// Finds the pieces a typed name refers to. Matching is the search box's: lower case with Turkish rules, the typed text
    /// appearing inside the name, plus diacritic folding. A single exactly equal name wins as typed; otherwise the typed text
    /// is tried as written and then without its object suffix ("ön bedeni" → "ön beden").
    /// </summary>
    private static NameMatch ResolvePieceName(string phrase, IReadOnlyList<string> names)
    {
        var typedLower = TurkishText.ToLower(phrase).Trim();
        var typedFold = TurkishText.Fold(typedLower);
        var strippedLower = StripObjectSuffix(typedLower, typedFold);
        var strippedFold = TurkishText.Fold(strippedLower);
        if (typedFold.Length == 0) return new NameMatch(Array.Empty<string>(), typedLower);

        var folded = names.Select(n => TurkishText.Fold(n)).ToArray();

        var exact = new List<string>();
        for (var i = 0; i < names.Count; i++) if (folded[i] == typedFold) exact.Add(names[i]);
        if (exact.Count == 1) return new NameMatch(exact, typedLower);

        var typedHits = new List<string>();
        for (var i = 0; i < names.Count; i++) if (folded[i].Contains(typedFold, StringComparison.Ordinal)) typedHits.Add(names[i]);
        if (typedHits.Count > 0) return new NameMatch(typedHits, typedLower);

        if (strippedFold.Length > 0 && strippedFold != typedFold)
        {
            var strippedHits = new List<string>();
            for (var i = 0; i < names.Count; i++) if (folded[i].Contains(strippedFold, StringComparison.Ordinal)) strippedHits.Add(names[i]);
            if (strippedHits.Count > 0) return new NameMatch(strippedHits, strippedLower);
        }
        return new NameMatch(Array.Empty<string>(), typedLower);
    }

    /// <summary>Removes the accusative ending of the last word ("kolu" → "kol", "ön bedeni" → "ön beden").</summary>
    private static string StripObjectSuffix(string lower, string folded)
    {
        var space = folded.LastIndexOf(' ');
        var word = folded.Substring(space + 1);
        var cut = 0;
        if (word.Length > 3 && (word.EndsWith("yi", StringComparison.Ordinal) || word.EndsWith("yu", StringComparison.Ordinal))) cut = 2;
        else if (word.Length > 2 && (word.EndsWith('i') || word.EndsWith('u'))) cut = 1;
        return cut == 0 ? lower : lower.Substring(0, lower.Length - cut);
    }

    // ================================================================================================================
    // Items: merged words, numbers, units
    // ================================================================================================================

    private static List<Tok> BuildItems(List<Tok> raw)
    {
        // 1. Word sequences that read as one ("yarım tur", "saat yönünde", "alt üst").
        var merged = new List<Tok>(raw.Count);
        for (var i = 0; i < raw.Count; i++)
        {
            var done = false;
            if (raw[i].Kind == TokKind.Word)
            {
                foreach (var (words, mergedText) in MultiWords)
                {
                    if (i + words.Length > raw.Count) continue;
                    var all = true;
                    for (var k = 0; k < words.Length; k++)
                        if (!raw[i + k].IsWord(words[k])) { all = false; break; }
                    if (!all) continue;
                    merged.Add(new Tok { Kind = TokKind.Word, Text = mergedText, Start = raw[i].Start, End = raw[i + words.Length - 1].End });
                    i += words.Length - 1;
                    done = true;
                    break;
                }
            }
            if (!done) merged.Add(raw[i]);
        }

        // 2. Number words ("yüz yirmi", "bir buçuk", "on iki virgül beş").
        var texts = new string[merged.Count];
        for (var i = 0; i < merged.Count; i++) texts[i] = merged[i].Kind == TokKind.Word ? merged[i].Text : string.Empty;
        var numbered = new List<Tok>(merged.Count);
        for (var i = 0; i < merged.Count; i++)
        {
            var t = merged[i];
            if (t.Kind == TokKind.Word && NumberWords.IsNumberWord(t.Text) && NumberWords.TryRead(texts, i, out var match) && match.WordCount > 0)
            {
                var last = merged[i + match.WordCount - 1];
                numbered.Add(new Tok
                {
                    Kind = TokKind.Number,
                    Text = string.Join(' ', texts.Skip(i).Take(match.WordCount)),
                    Start = t.Start,
                    End = last.End,
                    NumberEnd = last.End,
                    Value = match.Value,
                    FromWords = true,
                    Overflow = match.TooLarge,
                });
                if (match.HasSuffix) numbered.Add(new Tok { Kind = TokKind.Joiner, Text = "suffix", Start = last.End, End = last.End });
                i += match.WordCount - 1;
            }
            else numbered.Add(t);
        }

        // 3. "eksi 20" is negative; a unit word right after a number belongs to it.
        var items = new List<Tok>(numbered.Count);
        for (var i = 0; i < numbered.Count; i++)
        {
            var t = numbered[i];
            if (t.IsWord("eksi") && i + 1 < numbered.Count && numbered[i + 1].IsNumber)
            {
                numbered[i + 1].Negative = true;
                continue;
            }
            if (t.Kind == TokKind.Word && items.Count > 0 && items[^1].IsNumber && items[^1].Unit is null
                && UnitParser.TryParseUnitWord(t.Text, out var unit, out var glued))
            {
                items[^1].Unit = unit;
                items[^1].End = t.End;
                if (glued) items.Add(new Tok { Kind = TokKind.Joiner, Text = "suffix", Start = t.End, End = t.End });
                continue;
            }
            items.Add(t);
        }
        return items;
    }

    // ================================================================================================================
    // Classification
    // ================================================================================================================

    private static ParseResult Classify(State s)
    {
        var words = s.Items.Where(t => t.Kind == TokKind.Word).Select(t => t.Text).ToList();
        var numbers = s.Items.Count(t => t.IsNumber);

        var strong = new HashSet<Fam>();
        var hasDirection = false;
        var hasResizeLabel = false;
        var hasOrientation = false;
        var hasGroup = false;
        foreach (var w in words)
        {
            if (RectWords.Contains(w) || SquareWords.Contains(w) || LineWords.Contains(w)) strong.Add(Fam.Draw);
            else if (MoveVerbs.Contains(w) || PositionMarkers.Contains(w)) strong.Add(Fam.Move);
            else if (RotateWords.Contains(w) || w == "cevir") strong.Add(Fam.Rotate);
            else if (MirrorWords.Contains(w)) strong.Add(Fam.Mirror);
            else if (DeleteVerbs.Contains(w)) strong.Add(Fam.Delete);
            else if (SelectVerbs.Contains(w)) strong.Add(Fam.Select);
            else if (w == "geri") strong.Add(Fam.Undo);
            else if (SeamWords.Contains(w)) strong.Add(Fam.Seam);
            else if (MarkerWords.Contains(w)) strong.Add(Fam.Marker);
            else if (w == "adet") strong.Add(Fam.Meta);
            if (Directions.ContainsKey(w)) hasDirection = true;
            if (WidthLabels.Contains(w) || HeightLabels.Contains(w) || BothLabels.Contains(w)) hasResizeLabel = true;
            if (OrientationWords.Contains(w)) hasOrientation = true;
            if (GroupWords.Contains(w)) hasGroup = true;
        }

        // Directions read as a move unless another job owns them (rotation direction, names inside a name command).
        if (hasDirection)
        {
            var owners = strong.Count == 0
                || strong.All(f => f is Fam.Rotate or Fam.Move or Fam.Delete or Fam.Select);
            if (strong.Count == 0) strong.Add(Fam.Move);
            else if (!owners) strong.Add(Fam.Move);
        }

        // Size words steer a resize unless a drawing or the marker window owns them.
        if (hasResizeLabel && !strong.Contains(Fam.Draw) && !strong.Contains(Fam.Marker) && !strong.Contains(Fam.Seam))
            strong.Add(Fam.Resize);

        if (strong.Count == 0 && hasOrientation && numbers >= 1) strong.Add(Fam.Draw);

        if (strong.Count == 0 && numbers == 2
            && words.Any(w => w == "yap" || w == "olsun" || w == "ayarla" || w == "degistir")
            && !s.Items.Any(t => t.Kind == TokKind.Junk))
            strong.Add(Fam.Resize);

        if (strong.Count == 0) return Unrecognized(s);
        if (strong.Count > 1) return ParseResult.Refused(Refusals.TwoJobsRefusal());

        var family = strong.First();
        if (hasGroup && family == Fam.Move) return ParseGroupMove(s);
        return family switch
        {
            Fam.Draw => ParseDraw(s),
            Fam.Move => ParseMove(s),
            Fam.Rotate => ParseRotate(s),
            Fam.Mirror => ParseMirror(s),
            Fam.Resize => ParseResize(s),
            Fam.Delete or Fam.Select => ParseUnresolvedName(s, family),
            Fam.Undo => ParseUndo(s),
            Fam.Seam => ParseSeam(s),
            Fam.Marker => ParseMarker(s),
            Fam.Meta => ParseMeta(s),
            _ => Unrecognized(s),
        };
    }

    private static ParseResult Unrecognized(State s)
    {
        RefusalButton? hint = null;
        foreach (var t in s.Raw)
        {
            if (t.Kind != TokKind.Word || t.Text.Length <= 5 || KnownWords.Value.Contains(t.Text)) continue;
            if (NumberWords.IsNumberWord(t.Text)) continue;
            string? found = null;
            var count = 0;
            foreach (var (key, display) in DidYouMeanByFolded)
            {
                if (!WithinOneEdit(t.Text, key)) continue;
                count++;
                found = display;
            }
            if (count == 1 && found is not null)
            {
                var fill = s.Norm.Substring(0, t.Start) + found + s.Norm.Substring(t.End);
                hint = new RefusalButton("Şunu mu demek istediniz: " + found, fill);
                break;
            }
        }
        return ParseResult.Refused(Refusals.Unrecognized(s.Norm, hint));
    }

    private static readonly Lazy<HashSet<string>> KnownWords = new(BuildKnownWords);

    private static HashSet<string> BuildKnownWords()
    {
        var all = Union(DrawAllowed, MoveAllowed, RotateAllowed, MirrorAllowed, ResizeAllowed, UndoAllowed, SeamAllowed,
            MarkerAllowed, GroupAllowed, MetaAllowed, DeleteVerbs, SelectVerbs, SelectedPieceWords, RenameLabels, RenameVerbs,
            FillerWords, TrailingVerbs);
        all.UnionWith(Directions.Keys);
        foreach (var key in Phrases.Keys)
            foreach (var part in key.Split(' ')) all.Add(part);
        return all;
    }

    /// <summary>True when two words differ by exactly one inserted, removed or replaced letter.</summary>
    private static bool WithinOneEdit(string a, string b)
    {
        if (a == b) return false;
        var diff = a.Length - b.Length;
        if (diff > 1 || diff < -1) return false;
        var i = 0;
        var j = 0;
        var edits = 0;
        while (i < a.Length && j < b.Length)
        {
            if (a[i] == b[j]) { i++; j++; continue; }
            if (++edits > 1) return false;
            if (a.Length > b.Length) i++;
            else if (a.Length < b.Length) j++;
            else { i++; j++; }
        }
        edits += (a.Length - i) + (b.Length - j);
        return edits == 1;
    }
}
