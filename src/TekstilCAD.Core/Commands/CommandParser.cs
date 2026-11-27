using System.Globalization;
using System.Text.RegularExpressions;

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
            var ctx = context ?? ParserContext.Empty;
            var result = ParseCore(text ?? string.Empty, ctx);
            return CouldGrowIntoCommand(result, text ?? string.Empty, ctx) ? result.AsIncomplete() : result;
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
        public bool UnitAssumed;
    }

    private static ParseResult ParseCore(string text, ParserContext context)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0) return ParseResult.Refused(Refusals.Unrecognized(string.Empty));
        if (trimmed.Length > TurkishText.MaxInputLength) return ParseResult.Refused(Refusals.TextTooLong());

        var norm = TurkishText.Normalize(trimmed);
        if (norm.Length == 0) return ParseResult.Refused(Refusals.Unrecognized(string.Empty));
        var folded = TurkishText.Fold(norm);
        var state = new State { Norm = norm, Folded = folded, Names = (context.PieceNames ?? Array.Empty<string>()).Where(n => !string.IsNullOrWhiteSpace(n)).ToList() };
        var lexed = Lex(norm, folded);

        var measuredNesting = TryMeasuredNesting(state);
        if (measuredNesting is not null) return measuredNesting;

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
        state.Items = BuildItems(state.Raw, norm);
        if (state.Items.Count > 0 && state.Items[0].IsWord("yamuk"))
            return ParseTrapezoid(state);
        if (TryMultiPhrase(state)) return ParseResult.Refused(Refusals.TwoJobsRefusal());
        var classified = Classify(state);
        return state.UnitAssumed ? classified.WithUnitAssumed() : classified;
    }

    private static ParseResult? TryMeasuredNesting(State state)
    {
        if (!state.Folded.Contains("kumas", StringComparison.Ordinal) ||
            !(state.Folded.Contains("yerlestir", StringComparison.Ordinal) || state.Folded.Contains("diz", StringComparison.Ordinal))) return null;

        var match = Regex.Match(state.Folded,
            @"^(?<value>[0-9]+(?:[,.][0-9]+)?)\s*(?<unit>mm|cm|m|milim(?:etre)?|santim(?:etre)?|metre)\s+kumas(?:a|in|inda)?\s+(?<items>.+?)\s+(?:yerlestir|diz|hazirla)$",
            RegexOptions.CultureInvariant);
        if (!match.Success || Regex.IsMatch(match.Groups["value"].Value,@"[,.][0-9]{3}$",RegexOptions.CultureInvariant) ||
            !decimal.TryParse(match.Groups["value"].Value.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) ||
            !UnitParser.TryParseUnitWord(match.Groups["unit"].Value, out var unit, out _) || value <= 0) return ParseResult.Refused(Refusals.Unrecognized(state.Norm));

        var millimetersPerUnit=UnitParser.MillimetersPerUnit(unit);
        if(value>100_000_000m/millimetersPerUnit) return ParseResult.Refused(Refusals.OutOfRangeMeasure());
        var width = UnitParser.ToMillimeters(value, unit);
        if (width < (decimal)MinLengthMm || width > 100_000_000m) return ParseResult.Refused(Refusals.OutOfRangeMeasure());
        var names = state.Names.Select(n => (Name:n, Folded:TurkishText.Fold(n.Trim())))
            .Where(x => x.Folded.Length > 0).OrderByDescending(x => x.Folded.Length).ToArray();
        var assignments = new List<NestingPieceCount>();
        var foldedClauses = Regex.Split(match.Groups["items"].Value.Trim(), @"\s*,\s*|\s+ve\s+", RegexOptions.CultureInvariant);
        var originalItems=state.Norm.Substring(match.Groups["items"].Index,match.Groups["items"].Length).Trim();
        var originalClauses=Regex.Split(originalItems,@"\s*,\s*|\s+ve\s+",RegexOptions.CultureInvariant);
        if(foldedClauses.Length!=originalClauses.Length) return ParseResult.Refused(Refusals.Unrecognized(state.Norm));
        for(var clauseIndex=0;clauseIndex<foldedClauses.Length;clauseIndex++)
        {
            var remainder = foldedClauses[clauseIndex].Trim();
            var candidate = names.FirstOrDefault(x => remainder.EndsWith(x.Folded, StringComparison.Ordinal));
            if (candidate.Folded is null) return ParseResult.Refused(Refusals.Unrecognized(state.Norm));
            if (names.Count(x=>string.Equals(x.Folded,candidate.Folded,StringComparison.Ordinal))!=1)
                return ParseResult.Refused(Refusals.Unrecognized(state.Norm));
            var prefix = remainder[..^candidate.Folded.Length].TrimEnd();
            var originalClause=originalClauses[clauseIndex].Trim();
            var originalPrefix=originalClause.Length>=candidate.Folded.Length ? originalClause[..^candidate.Folded.Length].TrimEnd() : string.Empty;
            if (prefix.EndsWith("adet",StringComparison.Ordinal)) { prefix=prefix[..^4].TrimEnd();originalPrefix=Regex.Replace(originalPrefix,@"\s*adet$","",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant).TrimEnd(); }
            else if(prefix.EndsWith("tane",StringComparison.Ordinal)) { prefix=prefix[..^4].TrimEnd();originalPrefix=Regex.Replace(originalPrefix,@"\s*tane$","",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant).TrimEnd(); }
            if (prefix.Length == 0 || !TryNestingCount(originalPrefix, out var quantity) || quantity is < 1 or > 10_000)
                return ParseResult.Refused(Refusals.Unrecognized(state.Norm));
            if (assignments.Any(x => string.Equals(x.Name, candidate.Name, StringComparison.Ordinal)))
                return ParseResult.Refused(Refusals.Unrecognized(state.Norm));
            assignments.Add(new NestingPieceCount(candidate.Name, quantity));
        }
        if (assignments.Count == 0) return ParseResult.Refused(Refusals.Unrecognized(state.Norm));
        return ParseResult.Understood(new HandoffCommand(HandoffTarget.TextileTools, 3, (double)width,
            new MeasuredNestingRequest((double)width, assignments)));
    }

    private static bool TryNestingCount(string text, out int count)
    {
        count = 0;
        var original=text.Trim();
        var folded = TurkishText.Fold(original);
        if (int.TryParse(folded, NumberStyles.None, CultureInfo.InvariantCulture, out count)) return true;
        var words = folded.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        // Preserve the distinction between “ön” and “on” (ten) before folding.
        var originalWords=original.Split(' ',StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < words.Length; i++)
            if (words[i] == "on" && (i>=originalWords.Length || TurkishText.ToLower(originalWords[i]) != "on")) return false;
        return NumberWords.TryRead(words, 0, out var parsed) && parsed.WordCount == words.Length &&
            parsed.Value is >= 1 and <= 10_000 && decimal.Truncate(parsed.Value) == parsed.Value && (count=(int)parsed.Value) > 0;
    }

    // ================================================================================================================
    // Rename
    // ================================================================================================================

    private static ParseResult? TryRename(State s, List<Tok> lexed)
    {
        var index = 0;
        while (index < lexed.Count && lexed[index].Kind == TokKind.Word && (FillerWords.Contains(lexed[index].Text) || lexed[index].Text is "parcanin" or "parcamin")) index++;
        var project = index < lexed.Count && lexed[index].Kind == TokKind.Word
            && lexed[index].Text is "proje" or "projenin" or "projemin";
        if (project) index++;
        if (index >= lexed.Count) return null;
        var label = lexed[index];
        if (label.Kind != TokKind.Word || !RenameLabels.Contains(label.Text)) return null;

        // Every rename form ends in a verb ("olsun", "yap", ...); a line that does not is something else
        // ("isim kartını sil" names a piece). Polite fillers after the verb ("lütfen") are ignored.
        var endIndex = lexed.Count - 1;
        while (endIndex > index && lexed[endIndex].Kind == TokKind.Word && FillerWords.Contains(lexed[endIndex].Text)) endIndex--;
        var nameEnd = s.Norm.Length;
        if (endIndex == index) return ParseResult.Refused(Refusals.EmptyNameRefusal());
        if (lexed[endIndex].Kind != TokKind.Word || !RenameVerbs.Contains(lexed[endIndex].Text)) return null;
        nameEnd = lexed[endIndex].Start;
        endIndex--;
        if (endIndex > index && lexed[endIndex].Kind == TokKind.Word && lexed[endIndex].Text == "olarak")
            nameEnd = lexed[endIndex].Start;

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
        return ParseResult.Understood(project ? new RenameProjectCommand(name) : new RenameCommand(name));
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
        var raw = s.Raw.ToList();
        if (raw.Count >= 3 && raw[^1].Kind == TokKind.Word && TrailingVerbs.Contains(raw[^1].Text)
            && raw[^2].Kind == TokKind.Word && (DeleteVerbs.Contains(raw[^2].Text) || SelectVerbs.Contains(raw[^2].Text)))
            raw.RemoveAt(raw.Count - 1); // "kolu sil yap"
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

        // An ending glued to the last word ("ön beden 2'yi") is not part of the name.
        while (nameTokens.Count > 0 && nameTokens[^1].Kind == TokKind.Joiner) nameTokens.RemoveAt(nameTokens.Count - 1);
        if (nameTokens.Count == 0) return null;

        var isDelete = DeleteVerbs.Contains(verb.Text);
        if (nameTokens.All(t => t.Kind == TokKind.Word && SelectedPieceWords.Contains(t.Text)))
            return isDelete ? ParseResult.Understood(new DeleteCommand(PieceTarget.Selected)) : null;

        var phrase = s.Norm.Substring(nameTokens[0].Start, nameTokens[^1].End - nameTokens[0].Start);
        var resolved = ResolvePieceName(phrase, s.Names);
        if (resolved.Matches.Count == 0) return null;
        var verbText = isDelete ? "sil" : "seç";
        if (resolved.Matches.Count > 1) return ParseResult.Refused(Refusals.NameAmbiguousFor(resolved.Matches, verbText));
        return ParseResult.Understood(isDelete
            ? new DeleteCommand(PieceTarget.Named, resolved.Query)
            : new SelectCommand(PieceTarget.Named, resolved.Query));
    }

    private readonly record struct NameMatch(IReadOnlyList<string> Matches, string Query);

    /// <summary>
    /// The piece names a name query refers to, by the parser's own rule: lower case with Turkish rules, diacritic folding, the
    /// query appearing inside the name, and a single exactly equal name winning. Use it with the <c>Name</c> of a named
    /// <see cref="SelectCommand"/> or <see cref="DeleteCommand"/> so the planner and the parser can never disagree.
    /// Returns every match (none, one or several); the caller asks when it is not exactly one.
    /// </summary>
    public static IReadOnlyList<string> FindPieces(string? query, IReadOnlyList<string>? pieceNames) =>
        string.IsNullOrWhiteSpace(query) || pieceNames is null
            ? Array.Empty<string>()
            : ResolvePieceName(query, pieceNames.Where(n => !string.IsNullOrWhiteSpace(n)).ToList()).Matches;

    /// <summary>
    /// Finds the pieces a typed name refers to. A single exactly equal name wins as typed; otherwise the typed text is tried as
    /// written and then without its ending: the accusative ("kolu" → "kol"), with the softened last letter ("eteği" → "etek",
    /// "cebi" → "cep") and the "-yi" form ("payı" keeps its y when "pay" matches). The first form that matches decides.
    /// </summary>
    private static NameMatch ResolvePieceName(string phrase, IReadOnlyList<string> names)
    {
        var typedLower = TurkishText.ToLower(phrase.Replace('-', ' ')).Trim();
        var typedFold = TurkishText.Fold(typedLower);
        if (typedFold.Length < 2 || names.Count == 0) return new NameMatch(Array.Empty<string>(), typedLower);

        var folded = names.Select(n => TurkishText.Fold(n.Replace('-', ' '))).ToArray();

        var exact = new List<string>();
        for (var i = 0; i < names.Count; i++) if (folded[i] == typedFold) exact.Add(names[i]);
        if (exact.Count == 1) return new NameMatch(exact, typedLower);

        foreach (var candidate in NameCandidates(typedLower, typedFold))
        {
            var key = TurkishText.Fold(candidate);
            if (key.Length < 2) continue;
            var hits = new List<string>();
            for (var i = 0; i < names.Count; i++) if (folded[i].Contains(key, StringComparison.Ordinal)) hits.Add(names[i]);
            if (hits.Count > 0) return new NameMatch(hits, candidate);
        }
        return new NameMatch(Array.Empty<string>(), typedLower);
    }

    /// <summary>The typed name as written, then without its Turkish ending, most likely stem first.</summary>
    private static IEnumerable<string> NameCandidates(string lower, string folded)
    {
        yield return lower;
        var space = folded.LastIndexOf(' ');
        var word = folded.Substring(space + 1);
        if (word.Length > 2 && (word.EndsWith('i') || word.EndsWith('u')))
        {
            var one = lower.Substring(0, lower.Length - 1);
            yield return one;
            var last = one[^1];
            var softened = last switch { 'b' => 'p', 'd' => 't', 'ğ' => 'k', 'g' => 'k', _ => '\0' };
            if (softened != '\0') yield return one.Substring(0, one.Length - 1) + softened;
        }
        if (word.Length > 3 && (word.EndsWith("yi", StringComparison.Ordinal) || word.EndsWith("yu", StringComparison.Ordinal)
            || word.EndsWith("ni", StringComparison.Ordinal) || word.EndsWith("nu", StringComparison.Ordinal)))
            yield return lower.Substring(0, lower.Length - 2); // "payı" → "pay", "kartını" → "kartı"
    }


    // ================================================================================================================
    // Items: merged words, numbers, units
    // ================================================================================================================

    private static List<Tok> BuildItems(List<Tok> raw, string norm)
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
        // "on" is ten, but "ön" folds to the same letters: only a word typed without the umlaut is a number.
        for (var i = 0; i < merged.Count; i++)
            if (texts[i] == "on" && TurkishText.ToLower(norm.Substring(merged[i].Start, merged[i].End - merged[i].Start)) != "on") texts[i] = string.Empty;
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
                    HasWordSuffix = match.HasSuffix,
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
            && words.Any(w => w == "yap" || w == "olsun" || w == "ayarla" || w == "degistir" || w == "getir")
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
