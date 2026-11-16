import Foundation

// Port of CommandParser.cs, CommandParser.Families.cs and CommandParser.Incomplete.cs.
// The Turkish command box parser: pure, deterministic, offline and rule based. It reads one line of text (typed or
// dictated) and answers with a command, a question or a refusal that carries an example. It never touches a
// document, window, file or clock, never depends on the current locale, never guesses (an unclear line is
// refused) and never traps. All offsets are UTF-16 indices (see TurkishText).

enum CommandParser {
    /// Smallest and largest length a drawing or resizing command accepts, in millimetres (R04).
    static let minLengthMm = 1.0
    static let maxLengthMm = 20000.0

    /// A bare number below this is asked about ("milimetre mi, santimetre mi?") when no unit is written (R06).
    static let unitQuestionBelow = 30.0

    /// Largest number of steps "N adım geri" accepts.
    static let maxUndoSteps = 10

    /// Longest piece name a rename accepts.
    static let maxNameLength = 128

    /// Parses one line. `context` only supplies the current piece names (to resolve "ön bedeni seç"); without it the
    /// project is taken as empty. Never traps.
    static func parse(_ text: String?, context: ParserContext = .empty) -> ParseResult {
        let input = TurkishText.units(text)
        let result = parseCore(input, context)
        return couldGrowIntoCommand(result, input, context) ? result.asIncomplete() : result
    }
}

extension CommandParser {
    enum Family { case draw, move, rotate, mirror, resize, delete, select, undo, seam, marker, meta }

    final class ParseState {
        let norm: [UInt16]
        let folded: [UInt16]
        let names: [String]
        var raw: [Tok] = []
        var items: [Tok] = []
        var unitAssumed = false

        init(norm: [UInt16], folded: [UInt16], names: [String]) {
            self.norm = norm
            self.folded = folded
            self.names = names
        }
    }

    static func refuse(_ refusal: CommandRefusal) -> ParseResult {
        ParseResult.refused(refusal)
    }

    static func parseCore(_ text: [UInt16], _ context: ParserContext) -> ParseResult {
        let trimmed = TurkishText.trim(text)
        if trimmed.isEmpty { return refuse(Refusals.unrecognized("")) }
        if trimmed.count > TurkishText.maxInputLength { return refuse(Refusals.textTooLong()) }

        let norm = TurkishText.normalize16(trimmed)
        if norm.isEmpty { return refuse(Refusals.unrecognized("")) }
        let folded = TurkishText.fold16(norm)
        let names = context.pieceNames.filter { !isNullOrWhiteSpace($0) }
        let state = ParseState(norm: norm, folded: folded, names: names)
        let lexed = lex(folded)

        // 1. Rename claims the whole line: "adı Ön Beden olsun".
        if let rename = tryRename(state, lexed) { return rename }

        // 2. Filler words are ignored.
        state.raw = removeFillers(lexed)

        // 3. Whole phrases ("geri al", "dxf al", "çizimi kapat").
        if let phrase = tryPhrase(state.raw) { return phrase }

        // 4. "ön bedeni seç", "kolu sil".
        if let named = tryNamedPiece(state) { return named }

        // 5. Everything else is built from numbers, units, directions and keywords.
        state.items = buildItems(state.raw, norm)
        if tryMultiPhrase(state) { return refuse(Refusals.twoJobsRefusal()) }
        let classified = classify(state)
        return state.unitAssumed ? classified.withUnitAssumed() : classified
    }

    // ================================================================================================================
    // Rename
    // ================================================================================================================

    static func tryRename(_ s: ParseState, _ lexed: [Tok]) -> ParseResult? {
        var index = 0
        while index < lexed.count && lexed[index].kind == .word && isRenamePrefixWord(lexed[index].text) { index += 1 }
        if index >= lexed.count { return nil }
        let label = lexed[index]
        if label.kind != .word || !renameLabels.contains(label.text) { return nil }

        // Every rename form ends in a verb ("olsun", "yap", ...); a line that does not is something else
        // ("isim kartını sil" names a piece). Polite fillers after the verb ("lütfen") are ignored.
        var endIndex = lexed.count - 1
        while endIndex > index && lexed[endIndex].kind == .word && fillerWords.contains(lexed[endIndex].text) { endIndex -= 1 }
        var nameEnd = s.norm.count
        if endIndex == index { return refuse(Refusals.emptyNameRefusal()) }
        if lexed[endIndex].kind != .word || !renameVerbs.contains(lexed[endIndex].text) { return nil }
        nameEnd = lexed[endIndex].start
        endIndex -= 1
        if endIndex > index && lexed[endIndex].kind == .word && lexed[endIndex].text == "olarak" {
            nameEnd = lexed[endIndex].start
        }

        let from = label.end
        if nameEnd < from { nameEnd = from }
        var name = TurkishText.sub(s.norm, from, nameEnd)
        // TrimStart(' ', ':', '=', '\'', '"', '“', '”') and TrimEnd(' ', '\'', '"', '“', '”')
        let trimStartSet: [UInt16] = [0x20, 0x3A, 0x3D, 0x27, 0x22, 0x201C, 0x201D]
        let trimEndSet: [UInt16] = [0x20, 0x27, 0x22, 0x201C, 0x201D]
        var lo = 0
        var hi = name.count
        while lo < hi && trimStartSet.contains(name[lo]) { lo += 1 }
        while hi > lo && trimEndSet.contains(name[hi - 1]) { hi -= 1 }
        name = TurkishText.sub(name, lo, hi)

        var hasLetterOrDigit = false
        for c in name where TurkishText.isLetterOrDigit(c) {
            hasLetterOrDigit = true
            break
        }
        if !hasLetterOrDigit { return refuse(Refusals.emptyNameRefusal()) }
        if name.count > maxNameLength { return refuse(Refusals.unrecognized(TurkishText.string(s.norm))) }
        return ParseResult.understood(.rename(TurkishText.string(name)))
    }

    static func isRenamePrefixWord(_ text: String) -> Bool {
        return fillerWords.contains(text) || text == "parcanin" || text == "parcamin"
    }

    static func isNullOrWhiteSpace(_ text: String) -> Bool {
        for u in text.utf16 where !TurkishText.isWhiteSpace(u) { return false }
        return true
    }

    // ================================================================================================================
    // Fillers and phrases
    // ================================================================================================================

    static func removeFillers(_ lexed: [Tok]) -> [Tok] {
        var result: [Tok] = []
        result.reserveCapacity(lexed.count)
        var i = 0
        while i < lexed.count {
            let t = lexed[i]
            if t.kind == .word {
                if fillerWords.contains(t.text) { i += 1; continue }
                if t.text == "rica" && i + 1 < lexed.count && lexed[i + 1].isWord("ederim") { i += 2; continue }
                if t.text == "bir" && i + 1 < lexed.count && lexed[i + 1].isWord("tane") { i += 2; continue }
            }
            result.append(t)
            i += 1
        }
        return result
    }

    static func tryPhrase(_ raw: [Tok]) -> ParseResult? {
        if raw.isEmpty { return nil }
        for t in raw where t.kind != .word { return nil }
        let key = raw.map { $0.text }.joined(separator: " ")
        if let hit = phrases[key] { return hit }
        if raw.count > 1, let lastWord = raw.last, trailingVerbs.contains(lastWord.text) {
            let shorter = raw.dropLast().map { $0.text }.joined(separator: " ")
            if let hit = phrases[shorter] { return hit }
        }
        return nil
    }

    // ================================================================================================================
    // Pieces by name
    // ================================================================================================================

    static func tryNamedPiece(_ s: ParseState) -> ParseResult? {
        var raw = s.raw
        if raw.count >= 3 {
            let lastTok = raw[raw.count - 1]
            let prevTok = raw[raw.count - 2]
            let prevIsVerb = prevTok.kind == .word && (deleteVerbs.contains(prevTok.text) || selectVerbs.contains(prevTok.text))
            if lastTok.kind == .word && trailingVerbs.contains(lastTok.text) && prevIsVerb {
                raw.removeLast() // "kolu sil yap"
            }
        }
        if raw.count < 2 { return nil }
        let verb: Tok
        var nameTokens: [Tok]
        let first = raw[0]
        let last = raw[raw.count - 1]
        if last.kind == .word && (deleteVerbs.contains(last.text) || selectVerbs.contains(last.text)) {
            verb = last
            nameTokens = Array(raw.dropLast())
        } else if first.kind == .word && (deleteVerbs.contains(first.text) || selectVerbs.contains(first.text)) {
            verb = first
            nameTokens = Array(raw.dropFirst())
        } else {
            return nil
        }
        // An ending glued to the last word ("ön beden 2'yi") is not part of the name.
        while let lastTok = nameTokens.last, lastTok.kind == .joiner { nameTokens.removeLast() }
        guard let firstName = nameTokens.first, let lastName = nameTokens.last else { return nil }

        let isDelete = deleteVerbs.contains(verb.text)
        let allSelectedWords = nameTokens.allSatisfy { $0.kind == .word && selectedPieceWords.contains($0.text) }
        if allSelectedWords {
            return isDelete ? ParseResult.understood(.delete(.selected, name: nil)) : nil
        }

        let phrase = TurkishText.sub(s.norm, firstName.start, lastName.end)
        let resolved = resolvePieceName(phrase, s.names)
        if resolved.matches.isEmpty { return nil }
        let verbText = isDelete ? "sil" : "seç"
        if resolved.matches.count > 1 { return refuse(Refusals.nameAmbiguousFor(resolved.matches, verbText)) }
        if isDelete {
            return ParseResult.understood(.delete(.named, name: resolved.query))
        }
        return ParseResult.understood(.select(.named, name: resolved.query))
    }

    /// The piece names a name query refers to, by the parser's own rule (see `resolvePieceName`). Returns every match
    /// (none, one or several); the caller asks when it is not exactly one.
    static func findPieces(_ query: String?, _ pieceNames: [String]?) -> [String] {
        guard let query, !isNullOrWhiteSpace(query), let pieceNames else { return [] }
        let names = pieceNames.filter { !isNullOrWhiteSpace($0) }
        return resolvePieceName(TurkishText.units(query), names).matches
    }

    static func replaceHyphens(_ units: [UInt16]) -> [UInt16] {
        return units.map { $0 == 0x2D ? 0x20 : $0 }
    }

    /// Finds the pieces a typed name refers to. A single exactly equal name wins as typed; otherwise the typed text is
    /// tried as written and then without its ending: the accusative ("kolu" -> "kol"), with the softened last letter
    /// ("eteği" -> "etek", "cebi" -> "cep") and the "-yi" form. The first form that matches decides.
    static func resolvePieceName(_ phrase: [UInt16], _ names: [String]) -> (matches: [String], query: String) {
        let typedLower16 = TurkishText.trim(TurkishText.toLower16(replaceHyphens(phrase)))
        let typedFold = TurkishText.fold16(typedLower16)
        let typedLower = TurkishText.string(typedLower16)
        if typedFold.count < 2 || names.isEmpty { return ([], typedLower) }

        let foldedNames = names.map { TurkishText.fold16(replaceHyphens(TurkishText.units($0))) }

        var exact: [String] = []
        for i in 0..<names.count where foldedNames[i] == typedFold { exact.append(names[i]) }
        if exact.count == 1 { return (exact, typedLower) }

        for candidate in nameCandidates(typedLower16, typedFold) {
            let key = TurkishText.fold16(candidate)
            if key.count < 2 { continue }
            var hits: [String] = []
            for i in 0..<names.count where TurkishText.containsOrdinal(foldedNames[i], key) { hits.append(names[i]) }
            if !hits.isEmpty { return (hits, TurkishText.string(candidate)) }
        }
        return ([], typedLower)
    }

    /// The typed name as written, then without its Turkish ending, most likely stem first.
    static func nameCandidates(_ lower: [UInt16], _ folded: [UInt16]) -> [[UInt16]] {
        var result: [[UInt16]] = [lower]
        var space = -1
        var k = folded.count - 1
        while k >= 0 {
            if folded[k] == 0x20 { space = k; break }
            k -= 1
        }
        let word = TurkishText.sub(folded, space + 1, folded.count)
        guard let lastUnit = word.last else { return result }
        let endsWithIOrU = lastUnit == 0x69 || lastUnit == 0x75
        if word.count > 2 && endsWithIOrU {
            let one = TurkishText.sub(lower, 0, lower.count - 1)
            result.append(one)
            if let last = one.last {
                var softened: UInt16 = 0
                switch last {
                case 0x62: softened = 0x70        // b -> p
                case 0x64: softened = 0x74        // d -> t
                case 0x11F, 0x67: softened = 0x6B // ğ, g -> k
                default: softened = 0
                }
                if softened != 0 { result.append(TurkishText.sub(one, 0, one.count - 1) + [softened]) }
            }
        }
        if word.count > 3 {
            let prev = word[word.count - 2]
            let endsYi = prev == 0x79 && endsWithIOrU   // yi, yu
            let endsNi = prev == 0x6E && endsWithIOrU   // ni, nu
            if endsYi || endsNi { result.append(TurkishText.sub(lower, 0, lower.count - 2)) } // "payı" -> "pay", "kartını" -> "kartı"
        }
        return result
    }

    // ================================================================================================================
    // Items: merged words, numbers, units
    // ================================================================================================================

    static func buildItems(_ raw: [Tok], _ norm: [UInt16]) -> [Tok] {
        // 1. Word sequences that read as one ("yarım tur", "saat yönünde", "alt üst").
        var merged: [Tok] = []
        merged.reserveCapacity(raw.count)
        var i = 0
        while i < raw.count {
            var done = false
            if raw[i].kind == .word {
                for entry in multiWords {
                    let parts = entry.words
                    if i + parts.count > raw.count { continue }
                    var all = true
                    for k in 0..<parts.count where !raw[i + k].isWord(parts[k]) {
                        all = false
                        break
                    }
                    if !all { continue }
                    merged.append(Tok(kind: .word, text: entry.merged, start: raw[i].start, end: raw[i + parts.count - 1].end))
                    i += parts.count - 1
                    done = true
                    break
                }
            }
            if !done { merged.append(raw[i]) }
            i += 1
        }

        // 2. Number words ("yüz yirmi", "bir buçuk", "on iki virgül beş").
        var texts: [String] = merged.map { $0.kind == .word ? $0.text : "" }
        // "on" is ten, but "ön" folds to the same letters: only a word typed without the umlaut is a number.
        for k in 0..<merged.count where texts[k] == "on" {
            let typed = TurkishText.toLower16(TurkishText.sub(norm, merged[k].start, merged[k].end))
            if typed != Array("on".utf16) { texts[k] = "" }
        }
        var numbered: [Tok] = []
        numbered.reserveCapacity(merged.count)
        i = 0
        while i < merged.count {
            let t = merged[i]
            if t.kind == .word, NumberWords.isNumberWord(t.text), let match = NumberWords.tryRead(texts, i), match.wordCount > 0 {
                let lastIndex = min(i + match.wordCount - 1, merged.count - 1)
                let last = merged[lastIndex]
                let upper = min(i + match.wordCount, texts.count)
                let tok = Tok(kind: .number, text: texts[i..<upper].joined(separator: " "), start: t.start, end: last.end)
                tok.numberEnd = last.end
                tok.value = match.value
                tok.fromWords = true
                tok.hasWordSuffix = match.hasSuffix
                tok.overflow = match.tooLarge
                numbered.append(tok)
                if match.hasSuffix {
                    numbered.append(Tok(kind: .joiner, text: "suffix", start: last.end, end: last.end))
                }
                i += match.wordCount - 1
            } else {
                numbered.append(t)
            }
            i += 1
        }

        // 3. "eksi 20" is negative; a unit word right after a number belongs to it.
        var items: [Tok] = []
        items.reserveCapacity(numbered.count)
        i = 0
        while i < numbered.count {
            let t = numbered[i]
            if t.isWord("eksi") && i + 1 < numbered.count && numbered[i + 1].isNumber {
                numbered[i + 1].negative = true
                i += 1
                continue
            }
            if t.kind == .word, let previous = items.last, previous.isNumber, previous.unit == nil,
               let parsedUnit = UnitParser.tryParseUnitWord(t.text) {
                previous.unit = parsedUnit.unit
                previous.end = t.end
                if parsedUnit.hasSuffix {
                    items.append(Tok(kind: .joiner, text: "suffix", start: t.end, end: t.end))
                }
                i += 1
                continue
            }
            items.append(t)
            i += 1
        }
        return items
    }

    // ================================================================================================================
    // Classification
    // ================================================================================================================

    static func classify(_ s: ParseState) -> ParseResult {
        let words: [String] = s.items.filter { $0.kind == .word }.map { $0.text }
        let numbers = s.items.filter { $0.isNumber }.count

        var strong = Set<Family>()
        var hasDirection = false
        var hasResizeLabel = false
        var hasOrientation = false
        var hasGroup = false
        for w in words {
            if rectWords.contains(w) || squareWords.contains(w) || lineWords.contains(w) {
                strong.insert(.draw)
            } else if moveVerbs.contains(w) || positionMarkers.contains(w) {
                strong.insert(.move)
            } else if rotateWords.contains(w) || w == "cevir" {
                strong.insert(.rotate)
            } else if mirrorWords.contains(w) {
                strong.insert(.mirror)
            } else if deleteVerbs.contains(w) {
                strong.insert(.delete)
            } else if selectVerbs.contains(w) {
                strong.insert(.select)
            } else if w == "geri" {
                strong.insert(.undo)
            } else if seamWords.contains(w) {
                strong.insert(.seam)
            } else if markerWords.contains(w) {
                strong.insert(.marker)
            } else if w == "adet" {
                strong.insert(.meta)
            }
            if directions[w] != nil { hasDirection = true }
            if widthLabels.contains(w) || heightLabels.contains(w) || bothLabels.contains(w) { hasResizeLabel = true }
            if orientationWords.contains(w) { hasOrientation = true }
            if groupWords.contains(w) { hasGroup = true }
        }

        // Directions read as a move unless another job owns them (rotation direction, names inside a name command).
        if hasDirection {
            let owningFamilies: Set<Family> = [.rotate, .move, .delete, .select]
            let owners = strong.isEmpty || strong.isSubset(of: owningFamilies)
            if strong.isEmpty {
                strong.insert(.move)
            } else if !owners {
                strong.insert(.move)
            }
        }

        // Size words steer a resize unless a drawing or the marker window owns them.
        if hasResizeLabel && !strong.contains(.draw) && !strong.contains(.marker) && !strong.contains(.seam) {
            strong.insert(.resize)
        }

        if strong.isEmpty && hasOrientation && numbers >= 1 { strong.insert(.draw) }

        if strong.isEmpty && numbers == 2 {
            let setVerbs: Set<String> = ["yap", "olsun", "ayarla", "degistir", "getir"]
            let hasSetVerb = words.contains { setVerbs.contains($0) }
            let hasJunk = s.items.contains { $0.kind == .junk }
            if hasSetVerb && !hasJunk { strong.insert(.resize) }
        }

        if strong.isEmpty { return unrecognized(s) }
        if strong.count > 1 { return refuse(Refusals.twoJobsRefusal()) }

        guard let family = strong.first else { return unrecognized(s) }
        if hasGroup && family == .move { return parseGroupMove(s) }
        switch family {
        case .draw: return parseDraw(s)
        case .move: return parseMove(s)
        case .rotate: return parseRotate(s)
        case .mirror: return parseMirror(s)
        case .resize: return parseResize(s)
        case .delete, .select: return parseUnresolvedName(s, family)
        case .undo: return parseUndo(s)
        case .seam: return parseSeam(s)
        case .marker: return parseMarker(s)
        case .meta: return parseMeta(s)
        }
    }

    static func unrecognized(_ s: ParseState) -> ParseResult {
        var hint: RefusalButton?
        for t in s.raw {
            if t.kind != .word || t.text.utf16.count <= 5 || knownWords.contains(t.text) { continue }
            if NumberWords.isNumberWord(t.text) { continue }
            let typedUnits = Array(t.text.utf16)
            var found: String?
            var count = 0
            for entry in didYouMeanList {
                if !withinOneEdit(typedUnits, entry.key) { continue }
                count += 1
                found = entry.display
            }
            if count == 1, let found {
                let before = TurkishText.string(TurkishText.sub(s.norm, 0, t.start))
                let after = TurkishText.string(TurkishText.sub(s.norm, t.end, s.norm.count))
                hint = RefusalButton("Şunu mu demek istediniz: " + found, fill: before + found + after)
                break
            }
        }
        return refuse(Refusals.unrecognized(TurkishText.string(s.norm), didYouMean: hint))
    }

    /// The folded "did you mean" words as unit arrays (dictionary order is irrelevant: only a single hit counts).
    static let didYouMeanList: [(key: [UInt16], display: String)] = didYouMeanByFolded.map { (key: Array($0.key.utf16), display: $0.value) }

    static let knownWords: Set<String> = buildKnownWords()

    static func buildKnownWords() -> Set<String> {
        var all = unionOf([drawAllowed, moveAllowed, rotateAllowed, mirrorAllowed, resizeAllowed, undoAllowed, seamAllowed,
                           markerAllowed, groupAllowed, metaAllowed, deleteVerbs, selectVerbs, selectedPieceWords,
                           renameLabels, renameVerbs, fillerWords, trailingVerbs])
        for key in directions.keys { all.insert(key) }
        for key in phrases.keys {
            for part in key.split(separator: " ", omittingEmptySubsequences: false) { all.insert(String(part)) }
        }
        return all
    }

    /// True when two words differ by exactly one inserted, removed or replaced letter.
    static func withinOneEdit(_ a: [UInt16], _ b: [UInt16]) -> Bool {
        if a == b { return false }
        let diff = a.count - b.count
        if diff > 1 || diff < -1 { return false }
        var i = 0
        var j = 0
        var edits = 0
        while i < a.count && j < b.count {
            if a[i] == b[j] {
                i += 1
                j += 1
                continue
            }
            edits += 1
            if edits > 1 { return false }
            if a.count > b.count {
                i += 1
            } else if a.count < b.count {
                j += 1
            } else {
                i += 1
                j += 1
            }
        }
        edits += (a.count - i) + (b.count - j)
        return edits == 1
    }
}

// ====================================================================================================================
// Families (CommandParser.Families.cs)
// ====================================================================================================================

extension CommandParser {
    // ---- Shared checks -----------------------------------------------------------------------------------------------

    /// Refuses unknown symbols, leftover words (never ignored) and unusable numbers. A leftover that spells an existing
    /// piece in a command that works on the selected piece gets R27 ("select the piece first"). Nil when fine.
    static func preflight(_ s: ParseState, _ allowed: Set<String>, actsOnSelection: Bool) -> ParseResult? {
        if s.items.contains(where: { $0.kind == .junk }) { return unrecognized(s) }

        let leftover = s.items.filter { $0.kind == .word && !allowed.contains($0.text) }
        if !leftover.isEmpty {
            if actsOnSelection && !s.names.isEmpty {
                var phrase: [UInt16] = []
                for (k, t) in leftover.enumerated() {
                    if k > 0 { phrase.append(0x20) }
                    phrase.append(contentsOf: TurkishText.sub(s.norm, t.start, t.end))
                }
                if !resolvePieceName(phrase, s.names).matches.isEmpty {
                    return refuse(Refusals.namedPieceCannotMove())
                }
            }
            return unrecognized(s)
        }
        return checkNumbers(s)
    }

    static func checkNumbers(_ s: ParseState) -> ParseResult? {
        for t in s.items {
            if !t.isNumber { continue }
            if t.bad { return unrecognized(s) }
            if t.overflow { return refuse(Refusals.outOfRangeMeasure()) }
        }
        for t in s.items {
            if !t.isNumber || !t.hazard { continue }
            let textLength = t.text.utf16.count
            let digitsStart = t.numberEnd - textLength
            return refuse(Refusals.ambiguousNumberText(t.text, t.hazardDecimal, t.hazardWhole,
                                                       typed: TurkishText.string(s.norm),
                                                       numberStart: digitsStart, numberLength: textLength))
        }
        if s.items.contains(where: { $0.isNumber && $0.negative }) { return refuse(Refusals.negativeNumberRefusal()) }

        // Two numbers side by side are a pair only when both are written in digits or form a compound
        // ("1 santim 2 milim"). Words ("yüz yirmi seksen") could be split in more than one way: refuse instead of guessing.
        var pairIndex = 0
        while pairIndex + 1 < s.items.count {
            let a = s.items[pairIndex]
            let b = s.items[pairIndex + 1]
            if a.isNumber && b.isNumber && (a.fromWords || b.fromWords) && a.unit == nil { return unrecognized(s) }
            pairIndex += 1
        }
        return nil
    }

    /// Consecutive number items with nothing between them (a compound like "1 santim 2 milim" is one group).
    static func numberGroups(_ items: [Tok]) -> [[Tok]] {
        var groups: [[Tok]] = []
        var current: [Tok]?
        for t in items {
            if t.isNumber {
                current = (current ?? []) + [t]
            } else {
                if let done = current { groups.append(done) }
                current = nil
            }
        }
        if let done = current { groups.append(done) }
        return groups
    }

    /// A length as written: one number with an optional unit, or a compound of descending units.
    struct Meas {
        var value: Decimal
        var unit: LengthUnit?
        var compound = false
        var compoundMm: Decimal = 0
        var numberEnd: Int
        var start: Int

        init(value: Decimal, unit: LengthUnit?, numberEnd: Int, start: Int, compound: Bool = false, compoundMm: Decimal = 0) {
            self.value = value
            self.unit = unit
            self.numberEnd = numberEnd
            self.start = start
            self.compound = compound
            self.compoundMm = compoundMm
        }

        init(_ t: Tok) {
            self.init(value: t.value, unit: t.unit, numberEnd: t.numberEnd, start: t.start)
        }
    }

    static func toMeas(_ group: [Tok]) -> Meas? {
        guard let first = group.first, let last = group.last else { return nil }
        if group.count == 1 { return Meas(first) }
        var parts: [(value: Decimal, unit: LengthUnit)] = []
        for t in group {
            guard let u = t.unit else { return nil }
            parts.append((t.value, u))
        }
        guard let mm = UnitParser.tryCombine(parts) else { return nil }
        return Meas(value: mm, unit: last.unit, numberEnd: last.numberEnd, start: first.start, compound: true, compoundMm: mm)
    }

    struct MeasureRules {
        var min: Double = CommandParser.minLengthMm
        var max: Double = CommandParser.maxLengthMm
        var allowZero = false
        var askUnit = false
    }

    /// Applies the unit rules: the last unit written applies to every bare number (default millimetre); zero is
    /// refused; a bare number below 30 with no unit anywhere is asked about; the range is checked in millimetres.
    /// Returns the millimetre values in the order given, or a refusal.
    static func resolveMeasures(_ s: ParseState, _ list: [Meas], _ rules: MeasureRules) -> (values: [Double]?, refusal: ParseResult?) {
        var shared: LengthUnit?
        var anyExplicit = false
        let ordered = list.enumerated().sorted { a, b in
            if a.element.start != b.element.start { return a.element.start < b.element.start }
            return a.offset < b.offset
        }
        for entry in ordered {
            if let u = entry.element.unit {
                shared = u
                anyExplicit = true
            }
        }

        if !anyExplicit && list.contains(where: { !$0.compound }) { s.unitAssumed = true }

        if !rules.allowZero && list.contains(where: { !$0.compound && $0.value == 0 }) {
            return (nil, refuse(Refusals.outOfRangeMeasure()))
        }

        if rules.askUnit && !anyExplicit && list.allSatisfy({ DecimalMath.toDouble($0.value) < unitQuestionBelow }) {
            let end = list.map { $0.numberEnd }.max() ?? 0
            // The unit goes right after the last number; an ending glued to it ("10'luk", "beşlik") is dropped so the
            // fill text parses.
            func with(_ unit: String) -> String {
                if let number = s.items.first(where: { $0.isNumber && $0.numberEnd == end }), number.hasWordSuffix {
                    let before = TurkishText.string(TurkishText.sub(s.norm, 0, number.start))
                    let after = TurkishText.string(TurkishText.sub(s.norm, number.end, s.norm.count))
                    return before + TurkishText.formatDecimal(number.value) + " " + unit + after
                }
                var rest = end
                if let joiner = s.raw.first(where: { $0.kind == .joiner && $0.start == end }) { rest = joiner.end }
                let before = TurkishText.string(TurkishText.sub(s.norm, 0, end))
                let after = TurkishText.string(TurkishText.sub(s.norm, rest, s.norm.count))
                return before + " " + unit + after
            }
            let typed = TurkishText.string(s.norm)
            return (nil, refuse(Refusals.unitQuestionFor(typed, with("mm"), with("cm"))))
        }

        var result: [Double] = []
        result.reserveCapacity(list.count)
        for m in list {
            let mm: Decimal
            if m.compound {
                mm = m.compoundMm
            } else {
                mm = UnitParser.toMillimeters(m.value, m.unit ?? shared ?? .millimeter)
            }
            let value = DecimalMath.toDouble(mm)
            if value < rules.min || value > rules.max {
                return (nil, refuse(Refusals.outOfRangeMeasure()))
            }
            result.append(value)
        }
        return (result, nil)
    }

    // ---- Drawing: rectangle, square, line ----------------------------------------------------------------------------

    static func parseDraw(_ s: ParseState) -> ParseResult {
        if let refusal = preflight(s, drawAllowed, actsOnSelection: false) { return refusal }

        let isRect = s.items.contains { $0.kind == .word && rectWords.contains($0.text) }
        let isSquare = s.items.contains { $0.kind == .word && squareWords.contains($0.text) }
        let hasLineWord = s.items.contains { $0.kind == .word && lineWords.contains($0.text) }
        let isLine = hasLineWord || (!isRect && !isSquare)
        let jobCount = (isRect ? 1 : 0) + (isSquare ? 1 : 0) + (hasLineWord ? 1 : 0)
        if jobCount > 1 { return refuse(Refusals.twoJobsRefusal()) }

        var orientations: [String] = []
        for t in s.items where t.kind == .word && orientationWords.contains(t.text) {
            if !orientations.contains(t.text) { orientations.append(t.text) }
        }
        if orientations.count > 1 { return unrecognized(s) }
        if orientations.count == 1 && !isLine { return unrecognized(s) }

        let hasLabels = s.items.contains { $0.kind == .word && (widthLabels.contains($0.text) || heightLabels.contains($0.text)) }
        if hasLabels && !isRect { return unrecognized(s) }

        let groups = numberGroups(s.items)

        if isLine {
            if groups.isEmpty { return refuse(Refusals.numberMissingFor("150 çizgi")) }
            if groups.count > 1 { return unrecognized(s) }
            guard let meas = toMeas(groups[0]) else { return unrecognized(s) }
            var rules = MeasureRules()
            rules.askUnit = true
            let resolved = resolveMeasures(s, [meas], rules)
            if let bad = resolved.refusal { return bad }
            guard let values = resolved.values, let length = values.first else { return unrecognized(s) }
            let orientation: LineOrientation = (orientations.count == 1 && orientations[0] == "dikey") ? .vertical : .horizontal
            return ParseResult.understood(.line(length: length, orientation: orientation))
        }

        if isSquare {
            if groups.isEmpty { return refuse(Refusals.numberMissingFor("kare 100")) }
            let all = s.items.filter { $0.isNumber }
            var measures: [Meas] = []
            let firstGroup = groups[0]
            let allHaveUnits = firstGroup.allSatisfy { $0.unit != nil }
            let distinctUnits = Set(firstGroup.compactMap { $0.unit }).count
            if groups.count == 1 && firstGroup.count >= 2 && allHaveUnits && distinctUnits == firstGroup.count {
                guard let one = toMeas(firstGroup) else { return unrecognized(s) }
                measures.append(one)
            } else {
                for n in all { measures.append(Meas(n)) }
                if measures.count > 2 { return unrecognized(s) }
            }
            var rules = MeasureRules()
            rules.askUnit = true
            let resolved = resolveMeasures(s, measures, rules)
            if let bad = resolved.refusal { return bad }
            guard let values = resolved.values, let side = values.first else { return unrecognized(s) }
            if values.count == 2 && abs(values[0] - values[1]) > 1e-9 { return unrecognized(s) }
            return ParseResult.understood(.rectangle(width: side, height: side, isSquare: true))
        }

        // Rectangle: exactly two numbers; "eni 100 boyu 150" labels them.
        let numbers = s.items.filter { $0.isNumber }
        var order: [Tok] = []
        if hasLabels {
            var widthTok: Tok?
            var heightTok: Tok?
            for i in 0..<s.items.count {
                let t = s.items[i]
                if t.kind != .word { continue }
                let isWidth = widthLabels.contains(t.text)
                let isHeight = heightLabels.contains(t.text)
                if !isWidth && !isHeight { continue }
                if i + 1 >= s.items.count || !s.items[i + 1].isNumber { return unrecognized(s) }
                if isWidth {
                    if widthTok != nil { return unrecognized(s) }
                    widthTok = s.items[i + 1]
                } else {
                    if heightTok != nil { return unrecognized(s) }
                    heightTok = s.items[i + 1]
                }
            }
            if numbers.count < 2 && (widthTok == nil || heightTok == nil) { return refuse(Refusals.twoMeasuresRequired()) }
            guard let w = widthTok, let h = heightTok, numbers.count == 2 else { return unrecognized(s) }
            order.append(w)
            order.append(h)
        } else {
            if numbers.count < 2 { return refuse(Refusals.twoMeasuresRequired()) }
            if numbers.count > 2 { return unrecognized(s) }
            order.append(contentsOf: numbers)
        }

        let measuresRect = order.map { Meas($0) }
        var rectRules = MeasureRules()
        rectRules.askUnit = true
        let resolved = resolveMeasures(s, measuresRect, rectRules)
        if let refused = resolved.refusal { return refused }
        guard let mm = resolved.values, mm.count == 2 else { return unrecognized(s) }
        return ParseResult.understood(.rectangle(width: mm[0], height: mm[1], isSquare: false))
    }

    // ---- Moving ------------------------------------------------------------------------------------------------------

    struct MoveEvent {
        var isDirection: Bool
        var right: Int
        var down: Int
        var group: [Tok]
    }

    struct MovePair {
        var right: Int
        var down: Int
        var group: [Tok]
    }

    static func parseMove(_ s: ParseState) -> ParseResult {
        if let refusal = preflight(s, moveAllowed, actsOnSelection: true) { return refusal }

        if s.items.contains(where: { $0.kind == .word && positionMarkers.contains($0.text) }) { return parseMoveTo(s) }

        // Build "number group" and "direction" events in text order.
        var events: [MoveEvent] = []
        var i = 0
        while i < s.items.count {
            let t = s.items[i]
            if t.isNumber {
                var group: [Tok] = [t]
                while i + 1 < s.items.count && s.items[i + 1].isNumber {
                    i += 1
                    group.append(s.items[i])
                }
                events.append(MoveEvent(isDirection: false, right: 0, down: 0, group: group))
            } else if t.kind == .word, let d = directions[t.text] {
                events.append(MoveEvent(isDirection: true, right: d.right, down: d.down, group: []))
            }
            i += 1
        }

        let groupCount = events.filter { !$0.isDirection }.count
        let directionCount = events.filter { $0.isDirection }.count
        if groupCount == 0 { return refuse(Refusals.numberMissingFor(Refusals.moveExample)) }
        if directionCount == 0 || groupCount != directionCount || groupCount > 2 { return unrecognized(s) }

        // "20 sağa 10 yukarı" (number first) or "sağa 20 yukarı 10" (direction first).
        let numberFirst = !events[0].isDirection
        var pairs: [MovePair] = []
        var k = 0
        while k + 1 < events.count {
            let a = events[k]
            let b = events[k + 1]
            if numberFirst && !a.isDirection && b.isDirection {
                pairs.append(MovePair(right: b.right, down: b.down, group: a.group))
            } else if !numberFirst && a.isDirection && !b.isDirection {
                pairs.append(MovePair(right: a.right, down: a.down, group: b.group))
            } else {
                return unrecognized(s)
            }
            k += 2
        }
        if pairs.count == 2 && (pairs[0].right != 0) == (pairs[1].right != 0) { return unrecognized(s) }

        var measures: [Meas] = []
        for pair in pairs {
            guard let meas = toMeas(pair.group) else { return unrecognized(s) }
            measures.append(meas)
        }
        // Two distances with a unit on only one of them ("25 sağa 1 cm yukarı") could mean either unit for the other: refuse.
        if measures.count == 2 && measures.filter({ $0.unit != nil }).count == 1 { return unrecognized(s) }
        let resolved = resolveMeasures(s, measures, MeasureRules())
        if let bad = resolved.refusal { return bad }
        guard let values = resolved.values, values.count == pairs.count else { return unrecognized(s) }

        var right = 0.0
        var down = 0.0
        for n in 0..<pairs.count {
            right += values[n] * Double(pairs[n].right)
            down += values[n] * Double(pairs[n].down)
        }
        return ParseResult.understood(.moveBy(right: right + 0.0, down: down + 0.0))
    }

    static func parseMoveTo(_ s: ParseState) -> ParseResult {
        let items = s.items
        var xTok: Tok?
        var yTok: Tok?
        var bound = Set<ObjectIdentifier>()
        for i in 0..<items.count {
            let t = items[i]
            if t.kind != .word { continue }
            let isX = xLabels.contains(t.text)
            let isY = yLabels.contains(t.text)
            if !isX && !isY { continue }
            if i + 1 >= items.count || !items[i + 1].isNumber { continue }
            if t.text == "x" && i > 0 && items[i - 1].isNumber { continue } // "100 x 50" is a pair, not a label
            if isX {
                if xTok != nil { return unrecognized(s) }
                xTok = items[i + 1]
            } else {
                if yTok != nil { return unrecognized(s) }
                yTok = items[i + 1]
            }
            bound.insert(ObjectIdentifier(items[i + 1]))
        }

        let numbers = items.filter { $0.isNumber }
        if numbers.isEmpty { return refuse(Refusals.numberMissingFor("x 100 y 50 konumuna taşı")) }
        let px: Tok
        let py: Tok
        if xTok != nil || yTok != nil {
            guard let labelledX = xTok, let labelledY = yTok, numbers.count == 2, bound.count == 2 else { return unrecognized(s) }
            px = labelledX
            py = labelledY
        } else {
            if numbers.count != 2 { return unrecognized(s) }
            px = numbers[0]
            py = numbers[1]
        }

        let measures = [Meas(px), Meas(py)]
        var rules = MeasureRules()
        rules.min = 0
        rules.allowZero = true
        let resolved = resolveMeasures(s, measures, rules)
        if let bad = resolved.refusal { return bad }
        guard let values = resolved.values, values.count == 2 else { return unrecognized(s) }
        return ParseResult.understood(.moveTo(x: values[0], y: values[1]))
    }

    static func parseGroupMove(_ s: ParseState) -> ParseResult {
        if let refusal = preflight(s, groupAllowed, actsOnSelection: false) { return refusal }
        // "hepsini 20 sağa taşı": the distance is only checked here; the window asks for it again.
        let hasNumber = s.items.contains { $0.isNumber }
        let hasDirectionWord = s.items.contains { $0.kind == .word && directions[$0.text] != nil }
        if hasNumber || hasDirectionWord {
            if let probe = parseMoveForGroup(s) { return probe }
        }
        return ParseResult.understood(.handoff(.groupMove, tab: nil, value: nil))
    }

    static func parseMoveForGroup(_ s: ParseState) -> ParseResult? {
        let trimmed = ParseState(norm: s.norm, folded: s.folded, names: s.names)
        trimmed.raw = s.raw
        trimmed.items = s.items.filter { t in
            if t.kind != .word { return true }
            let dropped = groupWords.contains(t.text) || t.text == "parcayi" || t.text == "birden" || t.text == "fazla"
            return !dropped
        }
        let result = parseMove(trimmed)
        return result.isCommand ? nil : result
    }

    // ---- Rotating and mirroring --------------------------------------------------------------------------------------

    static func parseRotate(_ s: ParseState) -> ParseResult {
        if let refusal = preflight(s, rotateAllowed, actsOnSelection: true) { return refusal }

        let words: [String] = s.items.filter { $0.kind == .word }.map { $0.text }
        let numbers = s.items.filter { $0.isNumber }
        if numbers.count > 1 { return unrecognized(s) }

        var angle: Int?
        if words.contains("yarimtur") { angle = 180 }
        if words.contains("basasagi") {
            if angle != nil { return unrecognized(s) }
            angle = 180
        }
        if words.contains("ceyrektur") {
            if angle != nil { return unrecognized(s) }
            angle = 90
        }
        if numbers.count == 1 {
            if angle != nil || numbers[0].unit != nil { return unrecognized(s) }
            let v = numbers[0].value
            if v == 90 {
                angle = 90
            } else if v == 180 {
                angle = 180
            } else if v == 270 {
                angle = 270
            } else {
                return refuse(Refusals.angleNotSupported())
            }
        }

        let clockwise = words.contains("saga") || words.contains("saatyonunde")
        let counter = words.contains("sola") || words.contains("saatyonununtersine") || words.contains("tersyone")
        if clockwise && counter { return unrecognized(s) }

        // "çevir" alone could flip or rotate; with a number, a direction or another rotation word it rotates.
        let hasTurnWord = words.contains("dondur") || words.contains("derece")
        if words.contains("cevir") && angle == nil && !clockwise && !counter && !hasTurnWord {
            return refuse(Refusals.flipOrRotate())
        }

        return ParseResult.understood(.rotate(degrees: angle ?? 90, direction: counter ? .counterClockwise : .clockwise))
    }

    static func parseMirror(_ s: ParseState) -> ParseResult {
        if let refusal = preflight(s, mirrorAllowed, actsOnSelection: true) { return refusal }
        if s.items.contains(where: { $0.isNumber }) { return unrecognized(s) }

        let words: [String] = s.items.filter { $0.kind == .word }.map { $0.text }
        let vertical = words.contains("sagsol") || words.contains("dikey")
        let horizontal = words.contains("altust") || words.contains("yukariasagi") || words.contains("yatay")
        if vertical && horizontal { return unrecognized(s) }
        return ParseResult.understood(.mirror(horizontal ? .horizontal : .vertical))
    }

    // ---- Resizing ----------------------------------------------------------------------------------------------------

    static func parseResize(_ s: ParseState) -> ParseResult {
        if let refusal = preflight(s, resizeAllowed, actsOnSelection: true) { return refusal }

        let items = s.items
        let numbers = items.filter { $0.isNumber }
        var widthTok: Tok?
        var heightTok: Tok?
        var labelled = false
        for i in 0..<items.count {
            let t = items[i]
            if t.kind != .word { continue }
            let isWidth = widthLabels.contains(t.text)
            let isHeight = heightLabels.contains(t.text)
            if !isWidth && !isHeight { continue }
            labelled = true
            if i + 1 >= items.count || !items[i + 1].isNumber { return unrecognized(s) }
            if isWidth {
                if widthTok != nil { return unrecognized(s) }
                widthTok = items[i + 1]
            } else {
                if heightTok != nil { return unrecognized(s) }
                heightTok = items[i + 1]
            }
        }

        if numbers.isEmpty { return refuse(Refusals.numberMissingFor("genişliği 95 yap")) }

        let order: [Tok?]
        if labelled {
            let bound = (widthTok == nil ? 0 : 1) + (heightTok == nil ? 0 : 1)
            if bound != numbers.count { return unrecognized(s) }
            order = [widthTok, heightTok]
        } else {
            if numbers.count != 2 { return unrecognized(s) }
            order = [numbers[0], numbers[1]]
        }

        var measures: [Meas] = []
        for slot in order {
            if let t = slot { measures.append(Meas(t)) }
        }
        var rules = MeasureRules()
        rules.askUnit = true
        let resolved = resolveMeasures(s, measures, rules)
        if let bad = resolved.refusal { return bad }
        guard let values = resolved.values, values.count == measures.count else { return unrecognized(s) }

        var width: Double?
        var height: Double?
        var k = 0
        if order[0] != nil {
            width = values[k]
            k += 1
        }
        if order[1] != nil {
            height = values[k]
        }
        return ParseResult.understood(.resize(width: width, height: height))
    }

    // ---- Names that matched nothing, undo, hand-offs with a number ---------------------------------------------------

    static func parseUnresolvedName(_ s: ParseState, _ family: Family) -> ParseResult {
        let verbs = family == .delete ? deleteVerbs : selectVerbs
        let tokens = s.raw.filter { !($0.kind == .word && verbs.contains($0.text)) }
        guard let firstToken = tokens.first, let lastToken = tokens.last else { return unrecognized(s) }
        if s.names.isEmpty { return refuse(Refusals.noPiecesYet()) }
        let phrase = TurkishText.string(TurkishText.sub(s.norm, firstToken.start, lastToken.end))
        return refuse(Refusals.nameNotFoundFor(phrase, s.names, family == .delete ? "sil" : "seç"))
    }

    static func parseUndo(_ s: ParseState) -> ParseResult {
        if let refusal = preflight(s, undoAllowed, actsOnSelection: false) { return refusal }
        let numbers = s.items.filter { $0.isNumber }
        if numbers.count != 1 || numbers[0].unit != nil { return unrecognized(s) }
        let v = numbers[0].value
        if !DecimalMath.isWhole(v) || v < 1 || v > Decimal(maxUndoSteps) { return unrecognized(s) }
        let steps = Int(DecimalMath.toDouble(v))
        return ParseResult.understood(.undo(steps: steps))
    }

    static func parseSeam(_ s: ParseState) -> ParseResult { parseHandoffWithLength(s, seamAllowed, 2) }

    static func parseMarker(_ s: ParseState) -> ParseResult { parseHandoffWithLength(s, markerAllowed, 3) }

    static func parseHandoffWithLength(_ s: ParseState, _ allowed: Set<String>, _ tab: Int) -> ParseResult {
        if let refusal = preflight(s, allowed, actsOnSelection: false) { return refusal }
        let groups = numberGroups(s.items)
        if groups.isEmpty { return ParseResult.understood(.handoff(.textileTools, tab: tab, value: nil)) }
        if groups.count > 1 { return unrecognized(s) }
        guard let meas = toMeas(groups[0]) else { return unrecognized(s) }
        let resolved = resolveMeasures(s, [meas], MeasureRules())
        if let bad = resolved.refusal { return bad }
        guard let values = resolved.values, let value = values.first else { return unrecognized(s) }
        return ParseResult.understood(.handoff(.textileTools, tab: tab, value: value))
    }

    static func parseMeta(_ s: ParseState) -> ParseResult {
        if let refusal = preflight(s, metaAllowed, actsOnSelection: false) { return refusal }
        let numbers = s.items.filter { $0.isNumber }
        if numbers.isEmpty { return ParseResult.understood(.handoff(.pieceMetadata, tab: nil, value: nil)) }
        if numbers.count != 1 || numbers[0].unit != nil { return unrecognized(s) }
        let v = numbers[0].value
        if !DecimalMath.isWhole(v) || v < 1 || v > 999 { return unrecognized(s) }
        return ParseResult.understood(.handoff(.pieceMetadata, tab: nil, value: DecimalMath.toDouble(v)))
    }
}

// ====================================================================================================================
// Incomplete input (CommandParser.Incomplete.cs)
// ====================================================================================================================

extension CommandParser {
    // ---- Two whole phrases in one line ("kaydet ve sil", "geri al yinele"): two jobs -----------------------------------

    static func tryMultiPhrase(_ s: ParseState) -> Bool {
        let words = s.raw
        if words.count < 2 || words.count > 12 { return false }
        for t in words where t.kind != .word { return false }
        let texts = words.map { $0.text }
        // parts[i] = the words from i to the end split into whole phrases (a lone "ve" between phrases is skipped),
        // as a phrase count; -1 when impossible.
        var parts = [Int](repeating: -1, count: texts.count + 1)
        parts[texts.count] = 0
        var i = texts.count - 1
        while i >= 0 {
            if texts[i] == "ve" && i + 1 < texts.count && parts[i + 1] >= 0 {
                parts[i] = parts[i + 1]
                i -= 1
                continue
            }
            var j = i + 1
            while j <= texts.count {
                if parts[j] < 0 {
                    j += 1
                    continue
                }
                let key = texts[i..<j].joined(separator: " ")
                if phrases[key] == nil {
                    j += 1
                    continue
                }
                parts[i] = parts[j] + 1
                break
            }
            i -= 1
        }
        return parts[0] >= 2
    }

    // ---- Plausible prefixes ("120 x", "dikdört", "20 sağ"): the refusal is kept but flagged incomplete ----------------

    static let incompleteCodes: Set<String> = [
        Refusals.notUnderstood, Refusals.numberMissing, Refusals.twoMeasuresNeeded, Refusals.emptyName, Refusals.namedPieceMove,
    ]

    /// Endings tried after the typed text to see whether it could become a working command.
    static let endingTexts: [String] = [
        "", " 120 80", " 120 x 80", " 80", " 80 dikdörtgen", " 100 çizgi", " 100", " 10 sağa taşı", " 10 yukarı", " 5 aşağı",
        " santim yukarı", " santim sağa", " mm sağa", " milim sağa", " cm", " mm", " 50 konumuna taşı", " pastal", " payı",
        " 80 yap", " seç", " sil", " olsun", " yap", " tema", " 3 adım geri", " adım geri", " geri", " 60 yap",
        " 2 yap", " kaydır", " aracı", " 90 derece", " döndür", " beden büyüt", " 10 çizgi", " x 80 dikdörtgen", " çarpı seksen dikdörtgen",
        " dikdörtgen", " yukarı", " aşağı", " sağa", " konumuna taşı", " üst aynala", " aynala", " santim aşağı", "0 yukarı", "0 konumuna taşı", "0 çizgi", "0 sağa",
    ]

    static let endings: [[UInt16]] = endingTexts.map { Array($0.utf16) }

    static let phraseKeyUnits: [[UInt16]] = phrases.keys.map { Array($0.utf16) }

    /// Known words plus the unit words and the "did you mean" words, sorted ordinally (UTF-16 order).
    static let prefixVocabulary: [[UInt16]] = {
        var all = CommandParser.knownWords
        let extraWords = ["mm", "milim", "milimetre", "cm", "santim", "santimetre", "metre", "tur", "bucuk", "yarim", "virgul",
                          "sifir", "bir", "iki", "uc", "dort", "bes", "alti", "yedi", "sekiz", "dokuz", "on", "yirmi", "otuz", "kirk", "elli", "altmis",
                          "yetmis", "seksen", "doksan", "yuz", "bin"]
        for unit in extraWords { all.insert(unit) }
        for key in CommandParser.didYouMeanByFolded.keys { all.insert(key) }
        let arrays = all.map { Array($0.utf16) }
        return arrays.sorted { $0.lexicographicallyPrecedes($1) }
    }()

    static func couldGrowIntoCommand(_ result: ParseResult, _ text: [UInt16], _ context: ParserContext) -> Bool {
        guard result.kind == .refusal, let code = result.code, incompleteCodes.contains(code) else { return false }
        let trimmed = TurkishText.trim(text)
        if trimmed.isEmpty || trimmed.count > 100 { return false }
        let norm = TurkishText.normalize16(trimmed)
        guard let last = norm.last else { return false }
        if norm == [0x3F] { return false }

        let foldedNorm = TurkishText.fold16(norm)
        for key in phraseKeyUnits where key.starts(with: foldedNorm) { return true }

        // "adı olsun" already ends in its verb: the empty name is final, not unfinished typing.
        if code == Refusals.emptyName {
            var lastSpace = -1
            var k = foldedNorm.count - 1
            while k >= 0 {
                if foldedNorm[k] == 0x20 { lastSpace = k; break }
                k -= 1
            }
            let lastWord = TurkishText.string(TurkishText.sub(foldedNorm, lastSpace + 1, foldedNorm.count))
            if renameVerbs.contains(lastWord) { return false }
        }

        var bases: [[UInt16]] = [norm]
        if last == 0x27 {
            bases.append(norm + [0x65])
        } else if last == 0x2C || last == 0x2E {
            bases.append(norm + [0x35])
        }

        // The last word may be the start of a longer word.
        var start = norm.count
        while start > 0 && TurkishText.isLetter(norm[start - 1]) { start -= 1 }
        if start < norm.count {
            let typed = TurkishText.fold16(TurkishText.sub(norm, start, norm.count))
            let head = TurkishText.sub(norm, 0, start)
            var count = 0
            for word in prefixVocabulary {
                if word.count <= typed.count || !word.starts(with: typed) { continue }
                bases.append(head + word)
                count += 1
                if count >= 60 { break }
            }
        }

        for b in bases {
            for ending in endings {
                let attempt = parseCore(b + ending, context)
                if attempt.kind != .refusal { return true }
            }
        }
        return false
    }
}
