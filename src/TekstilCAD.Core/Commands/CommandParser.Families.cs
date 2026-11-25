namespace TekstilCAD.Core.Commands;

public static partial class CommandParser
{
    // ================================================================================================================
    // Shared checks
    // ================================================================================================================

    /// <summary>
    /// Refuses unknown symbols, leftover words (never ignored) and unusable numbers. A leftover that spells an existing
    /// piece in a command that works on the selected piece gets R27 ("select the piece first"). Returns null when fine.
    /// </summary>
    private static ParseResult? Preflight(State s, HashSet<string> allowed, bool actsOnSelection)
    {
        if (s.Items.Any(t => t.Kind == TokKind.Junk)) return Unrecognized(s);

        var leftover = s.Items.Where(t => t.Kind == TokKind.Word && !allowed.Contains(t.Text)).ToList();
        if (leftover.Count > 0)
        {
            if (actsOnSelection && s.Names.Count > 0)
            {
                var phrase = string.Join(' ', leftover.Select(t => s.Norm.Substring(t.Start, t.End - t.Start)));
                if (ResolvePieceName(phrase, s.Names).Matches.Count > 0)
                    return ParseResult.Refused(Refusals.NamedPieceCannotMove());
            }
            return Unrecognized(s);
        }
        return CheckNumbers(s);
    }

    private static ParseResult? CheckNumbers(State s)
    {
        foreach (var t in s.Items)
        {
            if (!t.IsNumber) continue;
            if (t.Bad) return Unrecognized(s);
            if (t.Overflow) return ParseResult.Refused(Refusals.OutOfRangeMeasure());
        }
        foreach (var t in s.Items)
        {
            if (!t.IsNumber || !t.Hazard) continue;
            var digitsStart = t.NumberEnd - t.Text.Length;
            return ParseResult.Refused(Refusals.AmbiguousNumberText(t.Text, t.HazardDecimal, t.HazardWhole, s.Norm, digitsStart, t.Text.Length));
        }
        if (s.Items.Any(t => t.IsNumber && t.Negative)) return ParseResult.Refused(Refusals.NegativeNumberRefusal());

        // Two numbers side by side are a pair only when both are written in digits ("dikdörtgen 120 80") or form a compound
        // ("1 santim 2 milim"). Words ("yüz yirmi seksen") could be split in more than one way: refuse instead of guessing.
        for (var i = 0; i + 1 < s.Items.Count; i++)
        {
            var a = s.Items[i];
            var b = s.Items[i + 1];
            if (a.IsNumber && b.IsNumber && (a.FromWords || b.FromWords) && a.Unit is null) return Unrecognized(s);
        }
        return null;
    }

    /// <summary>Consecutive number items with nothing between them (a compound like "1 santim 2 milim" is one group).</summary>
    private static List<List<Tok>> NumberGroups(List<Tok> items)
    {
        var groups = new List<List<Tok>>();
        List<Tok>? current = null;
        foreach (var t in items)
        {
            if (t.IsNumber)
            {
                if (current is null) { current = new List<Tok>(); groups.Add(current); }
                current.Add(t);
            }
            else current = null;
        }
        return groups;
    }

    /// <summary>A length as written: one number with an optional unit, or a compound of descending units.</summary>
    private sealed class Meas
    {
        public decimal Value;
        public LengthUnit? Unit;
        public bool Compound;
        public decimal CompoundMm;
        public int NumberEnd;
        public int Start;
    }

    private static Meas? ToMeas(List<Tok> group)
    {
        if (group.Count == 1)
        {
            var t = group[0];
            return new Meas { Value = t.Value, Unit = t.Unit, NumberEnd = t.NumberEnd, Start = t.Start };
        }
        var parts = new List<(decimal Value, LengthUnit Unit)>();
        foreach (var t in group)
        {
            if (t.Unit is null) return null;
            parts.Add((t.Value, t.Unit.Value));
        }
        if (!UnitParser.TryCombine(parts, out var mm)) return null;
        return new Meas { Value = mm, Unit = group[^1].Unit, Compound = true, CompoundMm = mm, NumberEnd = group[^1].NumberEnd, Start = group[0].Start };
    }

    private sealed class MeasureRules
    {
        public double Min = MinLengthMm;
        public double Max = MaxLengthMm;
        public bool AllowZero;
        public bool AskUnit;
    }

    /// <summary>
    /// Applies the unit rules: the last unit written applies to every bare number (default millimetre); zero is refused;
    /// a bare number below 30 with no unit anywhere is asked about; the range is checked in millimetres.
    /// Returns the millimetre values in the order given, or a refusal.
    /// </summary>
    private static (double[]? Millimeters, ParseResult? Refusal) ResolveMeasures(State s, IReadOnlyList<Meas> list, MeasureRules rules)
    {
        LengthUnit? shared = null;
        var anyExplicit = false;
        foreach (var m in list.OrderBy(m => m.Start))
        {
            if (m.Unit is not null) { shared = m.Unit; anyExplicit = true; }
        }

        if (!anyExplicit && list.Any(m => !m.Compound)) s.UnitAssumed = true;

        if (!rules.AllowZero && list.Any(m => !m.Compound && m.Value == 0))
            return (null, ParseResult.Refused(Refusals.OutOfRangeMeasure()));

        if (rules.AskUnit && !anyExplicit && list.All(m => (double)m.Value < UnitQuestionBelow))
        {
            var end = list.Max(m => m.NumberEnd);
            // The unit goes right after the last number; an ending glued to it ("10'luk", "beşlik") is dropped so the fill text parses.
            string With(string unit)
            {
                var number = s.Items.FirstOrDefault(t => t.IsNumber && t.NumberEnd == end);
                if (number is { HasWordSuffix: true })
                    return s.Norm.Substring(0, number.Start) + TurkishText.FormatNumber(number.Value) + " " + unit + s.Norm.Substring(number.End);
                var joiner = s.Raw.FirstOrDefault(t => t.Kind == TokKind.Joiner && t.Start == end);
                var rest = joiner is null ? end : joiner.End;
                return s.Norm.Substring(0, end) + " " + unit + s.Norm.Substring(rest);
            }
            return (null, ParseResult.Refused(Refusals.UnitQuestionFor(s.Norm, With("mm"), With("cm"))));
        }

        var result = new double[list.Count];
        for (var i = 0; i < list.Count; i++)
        {
            var m = list[i];
            var mm = m.Compound
                ? m.CompoundMm
                : UnitParser.ToMillimeters(m.Value, m.Unit ?? shared ?? LengthUnit.Millimeter);
            var value = (double)mm;
            if (value < rules.Min || value > rules.Max)
                return (null, ParseResult.Refused(Refusals.OutOfRangeMeasure()));
            result[i] = value;
        }
        return (result, null);
    }

    // ================================================================================================================
    // Drawing: rectangle, square, line
    // ================================================================================================================

    private static ParseResult ParseTrapezoid(State s)
    {
        const string example = "yamuk alt 60 cm üst 40 cm yükseklik 50 cm";
        if (CheckNumbers(s) is { } error) return error;
        var items = s.Items;
        if (items.Count != 7 || !items[1].IsWord("alt") || !items[3].IsWord("ust") || !items[5].IsWord("yukseklik"))
            return ParseResult.Refused(Refusals.NumberMissingFor(example));
        var dimensions = new[] { items[2], items[4], items[6] };
        if (dimensions.Any(t => !t.IsNumber || t.Unit is null))
            return ParseResult.Refused(Refusals.NumberMissingFor(example));
        var measures = dimensions.Select(t => new Meas { Value = t.Value, Unit = t.Unit, Start = t.Start }).ToList();
        var (mm, refusal) = ResolveMeasures(s, measures, new MeasureRules());
        if (refusal is not null) return refusal;
        return ParseResult.Understood(new TrapezoidCommand(mm![0], mm[1], mm[2]));
    }

    private static ParseResult ParseDraw(State s)
    {
        var refusal = Preflight(s, DrawAllowed, actsOnSelection: false);
        if (refusal is not null) return refusal;

        var isRect = s.Items.Any(t => t.Kind == TokKind.Word && RectWords.Contains(t.Text));
        var isSquare = s.Items.Any(t => t.Kind == TokKind.Word && SquareWords.Contains(t.Text));
        var isLine = s.Items.Any(t => t.Kind == TokKind.Word && LineWords.Contains(t.Text))
            || (!isRect && !isSquare);
        if ((isRect ? 1 : 0) + (isSquare ? 1 : 0) + (s.Items.Any(t => t.Kind == TokKind.Word && LineWords.Contains(t.Text)) ? 1 : 0) > 1)
            return ParseResult.Refused(Refusals.TwoJobsRefusal());

        var orientations = s.Items.Where(t => t.Kind == TokKind.Word && OrientationWords.Contains(t.Text)).Select(t => t.Text).Distinct().ToList();
        if (orientations.Count > 1) return Unrecognized(s);
        if (orientations.Count == 1 && !isLine) return Unrecognized(s);

        var hasLabels = s.Items.Any(t => t.Kind == TokKind.Word && (WidthLabels.Contains(t.Text) || HeightLabels.Contains(t.Text)));
        if (hasLabels && !isRect) return Unrecognized(s);

        var groups = NumberGroups(s.Items);

        if (isLine)
        {
            if (groups.Count == 0) return ParseResult.Refused(Refusals.NumberMissingFor("150 çizgi"));
            if (groups.Count > 1) return Unrecognized(s);
            var meas = ToMeas(groups[0]);
            if (meas is null) return Unrecognized(s);
            var (values, bad) = ResolveMeasures(s, new[] { meas }, new MeasureRules { AskUnit = true });
            if (bad is not null) return bad;
            var orientation = orientations.Count == 1 && orientations[0] == "dikey" ? LineOrientation.Vertical : LineOrientation.Horizontal;
            return ParseResult.Understood(new LineCommand(values![0], orientation));
        }

        if (isSquare)
        {
            if (groups.Count == 0) return ParseResult.Refused(Refusals.NumberMissingFor("kare 100"));
            var all = s.Items.Where(t => t.IsNumber).ToList();
            List<Meas> measures = new();
            if (groups.Count == 1 && groups[0].Count >= 2 && groups[0].All(t => t.Unit is not null) && groups[0].Select(t => t.Unit).Distinct().Count() == groups[0].Count)
            {
                var one = ToMeas(groups[0]);
                if (one is null) return Unrecognized(s);
                measures.Add(one);
            }
            else
            {
                foreach (var n in all)
                    measures.Add(new Meas { Value = n.Value, Unit = n.Unit, NumberEnd = n.NumberEnd, Start = n.Start });
                if (measures.Count > 2) return Unrecognized(s);
            }
            var (values, bad) = ResolveMeasures(s, measures, new MeasureRules { AskUnit = true });
            if (bad is not null) return bad;
            if (values!.Length == 2 && Math.Abs(values[0] - values[1]) > 1e-9) return Unrecognized(s);
            return ParseResult.Understood(new RectangleCommand(values[0], values[0], IsSquare: true));
        }

        // Rectangle: exactly two numbers; "eni 100 boyu 150" labels them.
        var numbers = s.Items.Where(t => t.IsNumber).ToList();
        double? width = null, height = null;
        var order = new List<Tok>();
        if (hasLabels)
        {
            Tok? widthTok = null, heightTok = null;
            for (var i = 0; i < s.Items.Count; i++)
            {
                var t = s.Items[i];
                if (t.Kind != TokKind.Word) continue;
                var isWidth = WidthLabels.Contains(t.Text);
                var isHeight = HeightLabels.Contains(t.Text);
                if (!isWidth && !isHeight) continue;
                if (i + 1 >= s.Items.Count || !s.Items[i + 1].IsNumber) return Unrecognized(s);
                if (isWidth) { if (widthTok is not null) return Unrecognized(s); widthTok = s.Items[i + 1]; }
                else { if (heightTok is not null) return Unrecognized(s); heightTok = s.Items[i + 1]; }
            }
            if (numbers.Count < 2 && (widthTok is null || heightTok is null)) return ParseResult.Refused(Refusals.TwoMeasuresRequired());
            if (widthTok is null || heightTok is null || numbers.Count != 2) return Unrecognized(s);
            order.Add(widthTok);
            order.Add(heightTok);
        }
        else
        {
            if (numbers.Count < 2) return ParseResult.Refused(Refusals.TwoMeasuresRequired());
            if (numbers.Count > 2) return Unrecognized(s);
            order.AddRange(numbers);
        }

        var measuresRect = order.Select(n => new Meas { Value = n.Value, Unit = n.Unit, NumberEnd = n.NumberEnd, Start = n.Start }).ToList();
        var (mm, refused) = ResolveMeasures(s, measuresRect, new MeasureRules { AskUnit = true });
        if (refused is not null) return refused;
        width = mm![0];
        height = mm[1];
        return ParseResult.Understood(new RectangleCommand(width.Value, height.Value));
    }

    // ================================================================================================================
    // Moving
    // ================================================================================================================

    private static ParseResult ParseMove(State s)
    {
        var refusal = Preflight(s, MoveAllowed, actsOnSelection: true);
        if (refusal is not null) return refusal;

        if (s.Items.Any(t => t.Kind == TokKind.Word && PositionMarkers.Contains(t.Text))) return ParseMoveTo(s);

        // Build "number group" and "direction" events in text order.
        var events = new List<(bool IsDirection, (int Right, int Down) Direction, List<Tok>? Group)>();
        for (var i = 0; i < s.Items.Count; i++)
        {
            var t = s.Items[i];
            if (t.IsNumber)
            {
                var group = new List<Tok> { t };
                while (i + 1 < s.Items.Count && s.Items[i + 1].IsNumber) group.Add(s.Items[++i]);
                events.Add((false, default, group));
            }
            else if (t.Kind == TokKind.Word && Directions.TryGetValue(t.Text, out var d))
            {
                events.Add((true, d, null));
            }
        }

        var groupCount = events.Count(e => !e.IsDirection);
        var directionCount = events.Count(e => e.IsDirection);
        if (groupCount == 0) return ParseResult.Refused(Refusals.NumberMissingFor(Refusals.MoveExample));
        if (directionCount == 0 || groupCount != directionCount || groupCount > 2) return Unrecognized(s);

        // "20 sağa 10 yukarı" (number first) or "sağa 20 yukarı 10" (direction first).
        var numberFirst = !events[0].IsDirection;
        var pairs = new List<((int Right, int Down) Direction, List<Tok> Group)>();
        for (var i = 0; i + 1 < events.Count; i += 2)
        {
            var a = events[i];
            var b = events[i + 1];
            if (numberFirst && !a.IsDirection && b.IsDirection) pairs.Add((b.Direction, a.Group!));
            else if (!numberFirst && a.IsDirection && !b.IsDirection) pairs.Add((a.Direction, b.Group!));
            else return Unrecognized(s);
        }
        if (pairs.Count == 2 && (pairs[0].Direction.Right != 0) == (pairs[1].Direction.Right != 0)) return Unrecognized(s);

        var measures = new List<Meas>();
        foreach (var pair in pairs)
        {
            var meas = ToMeas(pair.Group);
            if (meas is null) return Unrecognized(s);
            measures.Add(meas);
        }
        // Two distances with a unit on only one of them ("25 sağa 1 cm yukarı") could mean either unit for the other: refuse.
        if (measures.Count == 2 && measures.Count(m => m.Unit is not null) == 1) return Unrecognized(s);
        var (values, bad) = ResolveMeasures(s, measures, new MeasureRules());
        if (bad is not null) return bad;

        double right = 0, down = 0;
        for (var i = 0; i < pairs.Count; i++)
        {
            right += values![i] * pairs[i].Direction.Right;
            down += values[i] * pairs[i].Direction.Down;
        }
        return ParseResult.Understood(new MoveByCommand(right + 0.0, down + 0.0));
    }

    private static ParseResult ParseMoveTo(State s)
    {
        var items = s.Items;
        Tok? xTok = null, yTok = null;
        var bound = new HashSet<Tok>();
        for (var i = 0; i < items.Count; i++)
        {
            var t = items[i];
            if (t.Kind != TokKind.Word) continue;
            var isX = XLabels.Contains(t.Text);
            var isY = YLabels.Contains(t.Text);
            if (!isX && !isY) continue;
            if (i + 1 >= items.Count || !items[i + 1].IsNumber) continue;
            if (t.Text == "x" && i > 0 && items[i - 1].IsNumber) continue; // "100 x 50" is a pair, not a label
            if (isX) { if (xTok is not null) return Unrecognized(s); xTok = items[i + 1]; }
            else { if (yTok is not null) return Unrecognized(s); yTok = items[i + 1]; }
            bound.Add(items[i + 1]);
        }

        var numbers = items.Where(t => t.IsNumber).ToList();
        if (numbers.Count == 0) return ParseResult.Refused(Refusals.NumberMissingFor("x 100 y 50 konumuna taşı"));
        if (xTok is not null || yTok is not null)
        {
            if (xTok is null || yTok is null || numbers.Count != 2 || bound.Count != 2) return Unrecognized(s);
        }
        else
        {
            if (numbers.Count != 2) return Unrecognized(s);
            xTok = numbers[0];
            yTok = numbers[1];
        }

        var measures = new[] { xTok, yTok }
            .Select(n => new Meas { Value = n.Value, Unit = n.Unit, NumberEnd = n.NumberEnd, Start = n.Start }).ToList();
        var (values, bad) = ResolveMeasures(s, measures, new MeasureRules { Min = 0, AllowZero = true });
        if (bad is not null) return bad;
        return ParseResult.Understood(new MoveToCommand(values![0], values[1]));
    }

    private static ParseResult ParseGroupMove(State s)
    {
        var refusal = Preflight(s, GroupAllowed, actsOnSelection: false);
        if (refusal is not null) return refusal;
        // "hepsini 20 sağa taşı": the distance is only checked here; the window asks for it again.
        if (s.Items.Any(t => t.IsNumber) || s.Items.Any(t => t.Kind == TokKind.Word && Directions.ContainsKey(t.Text)))
        {
            var probe = ParseMoveForGroup(s);
            if (probe is not null) return probe;
        }
        return ParseResult.Understood(new HandoffCommand(HandoffTarget.GroupMove));
    }

    private static ParseResult? ParseMoveForGroup(State s)
    {
        var trimmed = new State { Norm = s.Norm, Folded = s.Folded, Names = s.Names, Raw = s.Raw };
        trimmed.Items = s.Items.Where(t => !(t.Kind == TokKind.Word && (GroupWords.Contains(t.Text) || t.Text is "parcayi" or "birden" or "fazla"))).ToList();
        var result = ParseMove(trimmed);
        return result.IsCommand ? null : result;
    }

    // ================================================================================================================
    // Rotating and mirroring
    // ================================================================================================================

    private static ParseResult ParseRotate(State s)
    {
        var refusal = Preflight(s, RotateAllowed, actsOnSelection: true);
        if (refusal is not null) return refusal;

        var words = s.Items.Where(t => t.Kind == TokKind.Word).Select(t => t.Text).ToList();
        var numbers = s.Items.Where(t => t.IsNumber).ToList();
        if (numbers.Count > 1) return Unrecognized(s);

        int? angle = null;
        if (words.Contains("yarimtur")) angle = 180;
        if (words.Contains("basasagi")) { if (angle is not null) return Unrecognized(s); angle = 180; }
        if (words.Contains("ceyrektur")) { if (angle is not null) return Unrecognized(s); angle = 90; }
        if (numbers.Count == 1)
        {
            if (angle is not null || numbers[0].Unit is not null) return Unrecognized(s);
            var v = numbers[0].Value;
            if (v == 90 || v == 180 || v == 270) angle = (int)v;
            else return ParseResult.Refused(Refusals.AngleNotSupported());
        }

        var clockwise = words.Contains("saga") || words.Contains("saatyonunde");
        var counter = words.Contains("sola") || words.Contains("saatyonununtersine") || words.Contains("tersyone");
        if (clockwise && counter) return Unrecognized(s);

        // "çevir" alone could flip or rotate; with a number, a direction or another rotation word it rotates.
        if (words.Contains("cevir") && angle is null && !clockwise && !counter && !words.Contains("dondur") && !words.Contains("derece"))
            return ParseResult.Refused(Refusals.FlipOrRotate());

        return ParseResult.Understood(new RotateCommand(angle ?? 90, counter ? RotationDirection.CounterClockwise : RotationDirection.Clockwise));
    }

    private static ParseResult ParseMirror(State s)
    {
        var refusal = Preflight(s, MirrorAllowed, actsOnSelection: true);
        if (refusal is not null) return refusal;
        if (s.Items.Any(t => t.IsNumber)) return Unrecognized(s);

        var words = s.Items.Where(t => t.Kind == TokKind.Word).Select(t => t.Text).ToList();
        var vertical = words.Contains("sagsol") || words.Contains("dikey");
        var horizontal = words.Contains("altust") || words.Contains("yukariasagi") || words.Contains("yatay");
        if (vertical && horizontal) return Unrecognized(s);
        return ParseResult.Understood(new MirrorCommand(horizontal ? MirrorAxis.Horizontal : MirrorAxis.Vertical));
    }

    // ================================================================================================================
    // Resizing
    // ================================================================================================================

    private static ParseResult ParseResize(State s)
    {
        var refusal = Preflight(s, ResizeAllowed, actsOnSelection: true);
        if (refusal is not null) return refusal;

        var items = s.Items;
        var numbers = items.Where(t => t.IsNumber).ToList();
        Tok? widthTok = null, heightTok = null;
        var labelled = false;
        var both = false;
        for (var i = 0; i < items.Count; i++)
        {
            var t = items[i];
            if (t.Kind != TokKind.Word) continue;
            var isWidth = WidthLabels.Contains(t.Text);
            var isHeight = HeightLabels.Contains(t.Text);
            if (BothLabels.Contains(t.Text)) both = true;
            if (!isWidth && !isHeight) continue;
            labelled = true;
            if (i + 1 >= items.Count || !items[i + 1].IsNumber) return Unrecognized(s);
            if (isWidth) { if (widthTok is not null) return Unrecognized(s); widthTok = items[i + 1]; }
            else { if (heightTok is not null) return Unrecognized(s); heightTok = items[i + 1]; }
        }

        if (numbers.Count == 0) return ParseResult.Refused(Refusals.NumberMissingFor("genişliği 95 yap"));

        List<Tok?> order;
        if (labelled)
        {
            var bound = (widthTok is null ? 0 : 1) + (heightTok is null ? 0 : 1);
            if (bound != numbers.Count) return Unrecognized(s);
            order = new List<Tok?> { widthTok, heightTok };
        }
        else
        {
            if (numbers.Count != 2) return Unrecognized(s);
            order = new List<Tok?> { numbers[0], numbers[1] };
            _ = both;
        }

        var present = order.Where(t => t is not null).Select(t => t!).ToList();
        var measures = present.Select(n => new Meas { Value = n.Value, Unit = n.Unit, NumberEnd = n.NumberEnd, Start = n.Start }).ToList();
        var (values, bad) = ResolveMeasures(s, measures, new MeasureRules { AskUnit = true });
        if (bad is not null) return bad;

        double? width = null, height = null;
        var k = 0;
        if (order[0] is not null) width = values![k++];
        if (order[1] is not null) height = values![k];
        return ParseResult.Understood(new ResizeCommand(width, height));
    }

    // ================================================================================================================
    // Names that matched nothing, undo, hand-offs with a number
    // ================================================================================================================

    private static ParseResult ParseUnresolvedName(State s, Fam family)
    {
        var verbs = family == Fam.Delete ? DeleteVerbs : SelectVerbs;
        var tokens = s.Raw.Where(t => !(t.Kind == TokKind.Word && verbs.Contains(t.Text))).ToList();
        if (tokens.Count == 0 || tokens.Any(t => t.Kind == TokKind.Sep && false)) return Unrecognized(s);
        if (s.Names.Count == 0) return ParseResult.Refused(Refusals.NoPiecesYet());
        var phrase = s.Norm.Substring(tokens[0].Start, tokens[^1].End - tokens[0].Start);
        return ParseResult.Refused(Refusals.NameNotFoundFor(phrase, s.Names, family == Fam.Delete ? "sil" : "seç"));
    }

    private static ParseResult ParseUndo(State s)
    {
        var refusal = Preflight(s, UndoAllowed, actsOnSelection: false);
        if (refusal is not null) return refusal;
        var numbers = s.Items.Where(t => t.IsNumber).ToList();
        if (numbers.Count != 1 || numbers[0].Unit is not null) return Unrecognized(s);
        var v = numbers[0].Value;
        if (v != Math.Floor(v) || v < 1 || v > MaxUndoSteps) return Unrecognized(s);
        return ParseResult.Understood(new UndoCommand((int)v));
    }

    private static ParseResult ParseSeam(State s) => ParseHandoffWithLength(s, SeamAllowed, 2);

    private static ParseResult ParseMarker(State s) => ParseHandoffWithLength(s, MarkerAllowed, 3);

    private static ParseResult ParseHandoffWithLength(State s, HashSet<string> allowed, int tab)
    {
        var refusal = Preflight(s, allowed, actsOnSelection: false);
        if (refusal is not null) return refusal;
        var groups = NumberGroups(s.Items);
        if (groups.Count == 0) return ParseResult.Understood(new HandoffCommand(HandoffTarget.TextileTools, tab));
        if (groups.Count > 1) return Unrecognized(s);
        var meas = ToMeas(groups[0]);
        if (meas is null) return Unrecognized(s);
        var (values, bad) = ResolveMeasures(s, new[] { meas }, new MeasureRules());
        if (bad is not null) return bad;
        return ParseResult.Understood(new HandoffCommand(HandoffTarget.TextileTools, tab, values![0]));
    }

    private static ParseResult ParseMeta(State s)
    {
        var refusal = Preflight(s, MetaAllowed, actsOnSelection: false);
        if (refusal is not null) return refusal;
        var numbers = s.Items.Where(t => t.IsNumber).ToList();
        if (numbers.Count == 0) return ParseResult.Understood(new HandoffCommand(HandoffTarget.PieceMetadata));
        if (numbers.Count != 1 || numbers[0].Unit is not null) return Unrecognized(s);
        var v = numbers[0].Value;
        if (v != Math.Floor(v) || v < 1 || v > 999) return Unrecognized(s);
        return ParseResult.Understood(new HandoffCommand(HandoffTarget.PieceMetadata, null, (double)v));
    }
}
