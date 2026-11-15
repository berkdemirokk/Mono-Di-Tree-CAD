import Foundation

// A deterministic, offline parser for the Turkish command field. No model, no network:
// a sentence is either understood exactly, answered with one question, or refused with an
// example. It never guesses. The grammar follows tasarim/FINAL_COMMANDS.md and is kept in
// step with the .NET parser through Apple/Tests/Fixtures/command-vectors.json.
//
// The parser does not look at the drawing; it only knows the piece names (to resolve
// "ön bedeni seç"). Everything that depends on the document (nothing selected, a piece that
// cannot be resized, ...) is decided by CommandPlanner.

/// What a sentence means. Distances are millimetres; "right" and "down" are screen directions
/// (the .NET parser uses the same convention, the iPhone executor turns "down" into its Y axis).
enum ParsedCommand: Equatable {
    case addRectangle(width: Double, height: Double)
    case addLine(length: Double, vertical: Bool)
    case move(right: Double, down: Double)
    case moveTo(x: Double, y: Double)
    case rotate(degrees: Int, clockwise: Bool)
    case resize(width: Double?, height: Double?)
    case rename(String)
    case duplicate
    case delete(name: String?)
    case deleteAt(index: Int, name: String)
    case select(name: String)
    case selectAt(index: Int, name: String)
    case selectAdjacent(next: Bool)
    case clearSelection
    case undo(steps: Int)
    case redo
    case save
    case saveCopy
    case help
    case cancel
}

/// A refusal or a question. `suggestions` are sentences that fill the command field;
/// `exactChoices` pick one named piece when several matched.
struct CommandRefusal: Equatable, Error {
    struct ExactChoice: Equatable {
        var label: String
        var command: ParsedCommand
    }
    var code: String
    var message: String
    var suggestions: [String] = []
    var exactChoices: [ExactChoice] = []
}

enum CommandOutcome: Equatable {
    case command(ParsedCommand)
    case ask(CommandRefusal)
    case refuse(CommandRefusal)
}

/// Text helpers shared by the parser and the planner.
enum CommandText {
    static let turkish = Locale(identifier: "tr_TR")

    static func lower(_ text: String) -> String { text.lowercased(with: turkish) }

    /// Lower case with the Turkish letters folded, so "dikdortgen" and "dikdörtgen" match.
    static func fold(_ text: String) -> String {
        var out = ""
        for ch in lower(text) {
            switch ch {
            case "ç": out.append("c")
            case "ğ": out.append("g")
            case "ı": out.append("i")
            case "ö": out.append("o")
            case "ş": out.append("s")
            case "ü": out.append("u")
            case "â": out.append("a")
            case "î": out.append("i")
            case "û": out.append("u")
            default: out.append(ch)
            }
        }
        return out
    }

    /// A number with a decimal comma and at most three decimals: 12,5 and 120.
    static func number(_ value: Double) -> String {
        let rounded = (value * 1000).rounded() / 1000
        var text = String(format: "%.3f", rounded)
        while text.hasSuffix("0") { text.removeLast() }
        if text.hasSuffix(".") { text.removeLast() }
        if text == "-0" { text = "0" }
        return text.replacingOccurrences(of: ".", with: ",")
    }

    static func length(_ millimetres: Double) -> String {
        "\(number(millimetres)) mm (\(number(millimetres / 10)) cm)"
    }

    static func size(_ width: Double, _ height: Double) -> String {
        "\(number(width)) × \(number(height)) mm (\(number(width / 10)) × \(number(height / 10)) cm)"
    }
}

enum CommandParser {
    static let maxLength = 200
    static let minSize = 1.0
    static let maxSize = 20_000.0
    static let examples = ["120 x 80 dikdörtgen", "25 sağa taşı", "90 sağa döndür", "geri al"]

    static func parse(_ input: String, pieceNames: [String] = []) -> CommandOutcome {
        var parsing = Parsing(input: input, pieceNames: pieceNames)
        return parsing.run()
    }

    // MARK: Refusal texts (FINAL_COMMANDS.md section 3)

    static func unclear(_ text: String, suggestions: [String]? = nil, prefix: String = "") -> CommandRefusal {
        let shown = text.count > 80 ? String(text.prefix(80)) + "…" : text
        return CommandRefusal(
            code: "R01",
            message: prefix + "Bu yazı anlaşılmadı: “\(shown)”. Şöyle yazabilirsiniz: " + examples.joined(separator: " · "),
            suggestions: suggestions ?? Array(examples.prefix(3)))
    }

    static func missingNumber(example: String) -> CommandRefusal {
        CommandRefusal(code: "R02", message: "Kaç milimetre? Örnek: \(example)", suggestions: [example])
    }

    static func noPieces() -> CommandRefusal {
        CommandRefusal(code: "R03",
                       message: "Henüz parça yok. Önce çizin. Örnek: 120 x 80 dikdörtgen",
                       suggestions: ["120 x 80 dikdörtgen"])
    }

    static func outOfRange() -> CommandRefusal {
        CommandRefusal(code: "R04",
                       message: "Ölçü 1 ile 20000 mm arasında olmalı. Örnek: 120 x 80 dikdörtgen",
                       suggestions: ["120 x 80 dikdörtgen"])
    }

    static func negative() -> CommandRefusal {
        CommandRefusal(code: "R26",
                       message: "Negatif ölçü yazılamaz. Yön için sağa, sola, yukarı veya aşağı yazın. Örnek: 25 sola taşı",
                       suggestions: ["25 sola taşı"])
    }

    static func twoJobs() -> CommandRefusal {
        CommandRefusal(code: "R16",
                       message: "Bu yazı iki ayrı iş gibi okunuyor. Tek iş yazın. Örnek: 120 x 80 dikdörtgen",
                       suggestions: ["120 x 80 dikdörtgen"])
    }

    static func twoDimensionsNeeded() -> CommandRefusal {
        CommandRefusal(code: "R19",
                       message: "Dikdörtgen için iki ölçü gerekir: en ve boy. Örnek: 120 x 80 dikdörtgen",
                       suggestions: ["120 x 80 dikdörtgen"])
    }

    static func angle() -> CommandRefusal {
        CommandRefusal(code: "R13",
                       message: "Şimdilik yalnız 90, 180 ve 270 derece döndürülebilir. Örnek: 90 sağa döndür",
                       suggestions: ["90 sağa döndür"])
    }

    /// A feature the Windows program has and the iPhone does not.
    static func windowsOnly(_ feature: String) -> CommandRefusal {
        CommandRefusal(
            code: "W01",
            message: "\(feature) yalnız Windows programında var; iPhone'da bu işlem yok. Örnek: 25 sağa taşı",
            suggestions: ["25 sağa taşı"])
    }

    /// Something the iPhone does through a button or another screen rather than a command.
    static func elsewhere(_ message: String) -> CommandRefusal {
        CommandRefusal(code: "P01", message: message, suggestions: ["25 sağa taşı"])
    }

    // MARK: Tokens

    fileprivate struct Token {
        enum Kind: Equatable {
            case number(value: Double, ambiguous: Bool, negative: Bool)
            case unit(Double)
            case separator
            case suffix
            case word
        }
        var kind: Kind
        var text: String      // lower case, Turkish letters kept
        var folded: String
        var lo: Int           // character range in the cleaned sentence
        var hi: Int

        var isWord: Bool { kind == .word }
        var isNumber: Bool {
            if case .number = kind { return true }
            return false
        }
    }

    fileprivate struct Quantity {
        var raw: Double            // as written, in the unit written
        var factor: Double?        // millimetres per written unit, nil when no unit was written
        var end: Int               // where the number ends in the sentence
        var mm: Double
    }

    fileprivate enum Item {
        case qty(Quantity)
        case word(String)
        case sep
    }

    fileprivate enum NumberWord {
        case digit(Int), tens(Int), hundred, thousand, half, andHalf, comma
    }

    // MARK: Parsing of one sentence

    fileprivate struct Parsing {
        let input: String
        let pieceNames: [String]
        var chars: [Character] = []

        init(input: String, pieceNames: [String]) {
            self.input = input
            self.pieceNames = pieceNames
        }

        // Word lists, all folded.
        static let fillers: Set<String> = ["lutfen", "sunu", "bunu", "su", "bu", "hemen", "bana", "rica", "ederim"]
        static let softVerbs: Set<String> = ["ciz", "cek", "yap", "et", "ver", "olsun", "edin", "al"]
        static let connectors: Set<String> = ["ve", "ile"]
        static let pieceWords: Set<String> = ["parca", "parcayi", "parcanin", "parcasini", "secili", "seciliyi", "seciligi"]
        static let directions: Set<String> = ["saga", "sag", "sola", "sol", "yukari", "yukariya", "asagi", "asagiya"]
        static let widthWords: Set<String> = ["eni", "en", "genisligi", "genislik"]
        static let heightWords: Set<String> = ["boyu", "boy", "yuksekligi", "yukseklik"]
        static let lengthWords: Set<String> = ["uzunlugu", "uzunluk", "uzunlugunda"]
        static let renameWords: Set<String> = ["adi", "adini", "ismi", "ismini"]
        static let suffixes: Set<String> = ["e", "a", "ye", "ya", "de", "da", "te", "ta", "lik", "luk", "li", "lu", "den", "dan"]

        // MARK: Entry

        mutating func run() -> CommandOutcome {
            let trimmed = input.trimmingCharacters(in: .whitespacesAndNewlines)
            if trimmed.count > CommandParser.maxLength {
                return .refuse(CommandRefusal(
                    code: "R23",
                    message: "Yazı çok uzun (en fazla 200 karakter). Kısaltın. Örnek: 120 x 80 dikdörtgen",
                    suggestions: ["120 x 80 dikdörtgen"]))
            }
            if trimmed == "?" { return .command(.help) }
            var cleaned: [Character] = trimmed.map { ch -> Character in
                switch ch {
                case "’", "‘", "´", "`": return "'"
                default: return ch
                }
            }
            while let last = cleaned.last, ".!?,;:".contains(last) || last.isWhitespace {
                cleaned.removeLast()
            }
            chars = cleaned
            if chars.isEmpty {
                return .refuse(CommandParser.unclear("", prefix: "Bir komut yazın. "))
            }
            let shown = String(chars)

            let scanned = Self.scan(chars)
            let tokens = Self.dropFillers(Self.mergeNumbers(Self.unitWords(scanned)))
            let words = tokens.filter { $0.isWord }.map { $0.folded }
            let wordSet = Set(words)

            // 1. Withdrawing and help.
            let cancelWords: Set<String> = ["vazgec", "vazgectim", "iptal", "et"]
            if !wordSet.isEmpty, wordSet.isSubset(of: cancelWords), !wordSet.isDisjoint(with: ["vazgec", "vazgectim", "iptal"]) {
                return .command(.cancel)
            }
            if wordSet.contains("yardim") || wordSet.contains("neler") || (wordSet.contains("ornekleri") && wordSet.contains("goster")) {
                return .command(.help)
            }

            // 2. Renaming takes everything after the keyword as the name.
            if let renamed = rename(scanned) { return renamed }

            // 3. Two-meaning words and things the iPhone does not do.
            if wordSet.contains("ters") && wordSet.contains("cevir") {
                return .ask(CommandRefusal(
                    code: "R16b",
                    message: "“ters çevir” aynalamak da döndürmek de olabilir. İPhone'da aynalama yok; döndürmek için: 90 sağa döndür",
                    suggestions: ["90 sağa döndür", "90 sola döndür"]))
            }
            if (wordSet.contains("buyut") || wordSet.contains("kucult")) && !words.contains(where: { $0.hasPrefix("beden") }) {
                if !wordSet.contains("yaziyi") {
                    return .ask(CommandRefusal(
                        code: "R16b",
                        message: "“büyüt” görünümü mü, parça ölçüsünü mü? Örnek: genişliği 95 yap",
                        suggestions: ["genişliği 95 yap", "95 x 60 yap"]))
                }
            }
            if let other = Self.elsewhere(words: words, wordSet: wordSet) { return .refuse(other) }

            // 4. Which job is it?
            return dispatch(tokens: tokens, words: words, wordSet: wordSet, shown: shown)
        }

        // MARK: Scanning

        static func unitFactor(_ folded: String) -> Double? {
            switch folded {
            case "mm", "milim", "milimetre": return 1
            case "cm", "santim", "santimetre": return 10
            case "m", "metre": return 1000
            default:
                for suffix in ["lik", "luk"] where folded.hasSuffix(suffix) {
                    let stem = String(folded.dropLast(suffix.count))
                    if stem != folded, let f = unitFactor(stem) { return f }
                }
                return nil
            }
        }

        static func scan(_ chars: [Character]) -> [Token] {
            var tokens: [Token] = []
            var i = 0
            func digit(_ c: Character) -> Bool { c >= "0" && c <= "9" }
            func make(_ kind: Token.Kind, _ lo: Int, _ hi: Int) -> Token {
                let lower = CommandText.lower(String(chars[lo..<hi]))
                return Token(kind: kind, text: lower, folded: CommandText.fold(lower), lo: lo, hi: hi)
            }
            while i < chars.count {
                let c = chars[i]
                if c.isWhitespace { i += 1; continue }
                if c == "×" || c == "*" {
                    tokens.append(make(.separator, i, i + 1))
                    i += 1
                    continue
                }
                if c == "'" {
                    var j = i + 1
                    while j < chars.count && chars[j].isLetter { j += 1 }
                    if j > i + 1 { tokens.append(make(.suffix, i, j)) }
                    i = j
                    continue
                }
                let negative = c == "-" && i + 1 < chars.count && digit(chars[i + 1])
                if digit(c) || negative {
                    var j = negative ? i + 1 : i
                    var integer = ""
                    while j < chars.count && digit(chars[j]) { integer.append(chars[j]); j += 1 }
                    var fraction = ""
                    var separators = 0
                    while j + 1 < chars.count && (chars[j] == "." || chars[j] == ",") && digit(chars[j + 1]) {
                        separators += 1
                        j += 1
                        var part = ""
                        while j < chars.count && digit(chars[j]) { part.append(chars[j]); j += 1 }
                        if separators == 1 { fraction = part }
                    }
                    if separators >= 2 {
                        // 1.500.000 and the like: not a number the parser will read.
                        tokens.append(make(.word, i, j))
                    } else {
                        let text = fraction.isEmpty ? integer : integer + "." + fraction
                        let value = Double(text) ?? 0
                        let whole = Double(integer) ?? 0
                        let ambiguous = separators == 1 && fraction.count == 3 && whole != 0
                        tokens.append(make(.number(value: value, ambiguous: ambiguous, negative: negative), i, j))
                    }
                    i = j
                    // Letters glued to the number: a unit, a separator or a case ending.
                    if i < chars.count && chars[i].isLetter {
                        var m = i
                        while m < chars.count && chars[m].isLetter { m += 1 }
                        let folded = CommandText.fold(String(chars[i..<m]))
                        if let factor = unitFactor(folded) {
                            tokens.append(make(.unit(factor), i, m))
                        } else if folded == "x" {
                            tokens.append(make(.separator, i, m))
                        } else if suffixes.contains(folded) {
                            tokens.append(make(.suffix, i, m))
                        } else {
                            tokens.append(make(.word, i, m))
                        }
                        i = m
                    }
                    continue
                }
                if c.isLetter {
                    let isAxis = c == "x" || c == "X" || c == "y" || c == "Y"
                    if isAxis && i + 1 < chars.count && digit(chars[i + 1]) {
                        tokens.append(make(.word, i, i + 1))
                        i += 1
                        continue
                    }
                    var j = i
                    while j < chars.count && (chars[j].isLetter || digit(chars[j])) { j += 1 }
                    tokens.append(make(.word, i, j))
                    i = j
                    continue
                }
                i += 1
            }
            return tokens
        }

        static func unitWords(_ tokens: [Token]) -> [Token] {
            tokens.map { token in
                guard token.isWord, let factor = unitFactor(token.folded) else { return token }
                var unit = token
                unit.kind = .unit(factor)
                return unit
            }
        }

        static func dropFillers(_ tokens: [Token]) -> [Token] {
            tokens.filter { !($0.isWord && fillers.contains($0.folded)) }
        }

        // MARK: Number words

        static func numberWord(folded: String, text: String) -> NumberWord? {
            switch folded {
            case "sifir": return .digit(0)
            case "bir": return .digit(1)
            case "iki": return .digit(2)
            case "uc": return .digit(3)
            case "dort": return .digit(4)
            case "bes": return .digit(5)
            case "alti": return .digit(6)
            case "yedi": return .digit(7)
            case "sekiz": return .digit(8)
            case "dokuz": return .digit(9)
            // "ön" folds to "on": only the plain spelling is the number ten.
            case "on": return text == "on" ? .tens(10) : nil
            case "yirmi": return .tens(20)
            case "otuz": return .tens(30)
            case "kirk": return .tens(40)
            case "elli": return .tens(50)
            case "altmis": return .tens(60)
            case "yetmis": return .tens(70)
            case "seksen": return .tens(80)
            case "doksan": return .tens(90)
            case "yuz": return .hundred
            case "bin": return .thousand
            case "yarim": return .half
            case "bucuk": return .andHalf
            case "virgul": return .comma
            default: return nil
            }
        }

        static func numberWord(_ token: Token) -> NumberWord? {
            guard token.isWord else { return nil }
            return numberWord(folded: token.folded, text: token.text)
        }

        /// "yüze", "ikiye", "yüzlük": a number word with a case ending.
        static func numberWordWithEnding(_ token: Token) -> (NumberWord, String)? {
            guard token.isWord else { return nil }
            for ending in ["ye", "ya", "e", "a", "lik", "luk"] where token.folded.hasSuffix(ending) {
                let stem = String(token.folded.dropLast(ending.count))
                let stemText = String(token.text.dropLast(ending.count))
                guard !stem.isEmpty, let word = numberWord(folded: stem, text: stemText) else { continue }
                switch word {
                case .half, .andHalf, .comma: continue
                default: return (word, ending)
                }
            }
            return nil
        }

        static func parseNumberWords(_ list: [Token], from start: Int) -> (value: Double, count: Int, ending: String?)? {
            var idx = start
            var total = 0
            var group = 0
            var stage = 0        // 0 nothing yet, 1 hundreds, 2 tens, 3 units
            var sawInteger = false
            var thousandsUsed = false
            var ending: String?

            loop: while idx < list.count {
                var piece = numberWord(list[idx])
                var stripped: String?
                if piece == nil, let withEnding = numberWordWithEnding(list[idx]) {
                    piece = withEnding.0
                    stripped = withEnding.1
                }
                guard let word = piece else { break loop }
                switch word {
                case .digit(let d):
                    guard stage < 3 else { break loop }
                    if d == 0 {
                        if sawInteger { break loop }
                        sawInteger = true
                        idx += 1
                        ending = stripped
                        break loop
                    }
                    group += d
                    stage = 3
                case .tens(let t):
                    guard stage < 2 else { break loop }
                    group += t
                    stage = 2
                case .hundred:
                    if stage == 0 {
                        group = 100
                    } else if stage == 3 && group >= 1 && group <= 9 {
                        group *= 100
                    } else {
                        break loop
                    }
                    stage = 1
                case .thousand:
                    guard !thousandsUsed else { break loop }
                    total += (stage == 0 ? 1 : group) * 1000
                    group = 0
                    stage = 0
                    thousandsUsed = true
                case .half, .andHalf, .comma:
                    break loop
                }
                sawInteger = true
                idx += 1
                if let stripped {
                    ending = stripped
                    break loop
                }
            }
            guard sawInteger else { return nil }
            var value = Double(total + group)
            var count = idx - start
            if ending == nil, idx < list.count, let next = numberWord(list[idx]) {
                switch next {
                case .andHalf:
                    value += 0.5
                    count += 1
                case .comma:
                    var j = idx + 1
                    var digits = ""
                    while j < list.count && digits.count < 3 {
                        if let dw = numberWord(list[j]), case .digit(let d) = dw {
                            digits += String(d)
                            j += 1
                        } else {
                            break
                        }
                    }
                    if digits.isEmpty, j < list.count, list[j].isNumber, isPlainDigits(list[j].text) {
                        digits = list[j].text
                        j += 1
                    }
                    if !digits.isEmpty {
                        value += Double("0." + digits) ?? 0
                        count = j - start
                    }
                default:
                    break
                }
            }
            return (value, count, ending)
        }

        static func isPlainDigits(_ text: String) -> Bool {
            !text.isEmpty && text.allSatisfy { $0 >= "0" && $0 <= "9" }
        }

        static func mergeNumbers(_ list: [Token]) -> [Token] {
            var out: [Token] = []
            var k = 0
            while k < list.count {
                let t = list[k]
                // "bir tane" is filler.
                if t.isWord, t.folded == "bir", k + 1 < list.count, list[k + 1].folded == "tane" {
                    k += 2
                    continue
                }
                // "10 virgül 5"
                if case let .number(_, ambiguous, negative) = t.kind, !ambiguous, !negative,
                   isPlainDigits(t.text), k + 2 < list.count, list[k + 1].folded == "virgul",
                   list[k + 2].isNumber, isPlainDigits(list[k + 2].text) {
                    let value = Double(t.text + "." + list[k + 2].text) ?? 0
                    out.append(Token(kind: .number(value: value, ambiguous: false, negative: false),
                                     text: t.text + "," + list[k + 2].text, folded: t.text + "," + list[k + 2].text,
                                     lo: t.lo, hi: list[k + 2].hi))
                    k += 3
                    continue
                }
                if t.isWord {
                    if let word = numberWord(t), case .half = word {
                        // "yarım tur" is a rotation, not a number.
                        if k + 1 < list.count && list[k + 1].folded == "tur" {
                            out.append(t)
                            k += 1
                            continue
                        }
                        out.append(Token(kind: .number(value: 0.5, ambiguous: false, negative: false),
                                         text: t.text, folded: t.folded, lo: t.lo, hi: t.hi))
                        k += 1
                        continue
                    }
                    if let parsed = parseNumberWords(list, from: k) {
                        let slice = list[k..<(k + parsed.count)]
                        let text = slice.map { $0.text }.joined(separator: " ")
                        let folded = slice.map { $0.folded }.joined(separator: " ")
                        let last = list[k + parsed.count - 1]
                        out.append(Token(kind: .number(value: parsed.value, ambiguous: false, negative: false),
                                         text: text, folded: folded, lo: t.lo, hi: last.hi))
                        if let ending = parsed.ending {
                            out.append(Token(kind: .suffix, text: "'" + ending, folded: ending, lo: last.hi, hi: last.hi))
                        }
                        k += parsed.count
                        continue
                    }
                }
                out.append(t)
                k += 1
            }
            return out
        }

        // MARK: Quantities

        enum ItemsResult {
            case ok([Item], anyExplicit: Bool)
            case refusal(CommandRefusal)
        }

        func insert(_ text: String, at position: Int) -> String {
            String(chars[0..<position]) + text + String(chars[position...])
        }

        func replace(_ lo: Int, _ hi: Int, with text: String) -> String {
            String(chars[0..<lo]) + text + String(chars[hi...])
        }

        /// Turns the tokens into numbers with units, words and separators. `global` lets the last
        /// unit apply to every number without one (sizes); otherwise it stays within one run of
        /// numbers (movements: "10 sağa 5 cm yukarı" is 10 mm and 5 cm).
        func items(_ tokens: [Token], global: Bool) -> ItemsResult {
            var result: [Item] = []
            var k = 0
            while k < tokens.count {
                let t = tokens[k]
                switch t.kind {
                case .number(let value, let ambiguous, let negative):
                    if negative { return .refusal(CommandParser.negative()) }
                    if ambiguous { return .refusal(ambiguousNumber(t)) }
                    var next = k + 1
                    var factor: Double?
                    var total = value
                    if next < tokens.count, case .unit(let f) = tokens[next].kind {
                        factor = f
                        next += 1
                        // "1 santim 2 milim": the smaller unit adds to the larger one.
                        if next + 1 < tokens.count,
                           case .number(let v2, let amb2, let neg2) = tokens[next].kind, !amb2, !neg2,
                           case .unit(let f2) = tokens[next + 1].kind, f2 < f {
                            total = value * f + v2 * f2
                            factor = 1
                            next += 2
                        }
                    }
                    let mm = factor.map { total * $0 } ?? total
                    result.append(.qty(Quantity(raw: total, factor: factor, end: t.hi, mm: mm)))
                    k = next
                case .unit:
                    result.append(.word(t.folded))
                    k += 1
                case .separator, .suffix:
                    result.append(.sep)
                    k += 1
                case .word:
                    if t.folded == "eksi", k + 1 < tokens.count, tokens[k + 1].isNumber {
                        return .refusal(CommandParser.negative())
                    }
                    var isSeparator = false
                    if t.folded == "x" || t.folded == "carpi", k + 1 < tokens.count, tokens[k + 1].isNumber,
                       let previous = result.last, case .qty = previous {
                        isSeparator = true
                    }
                    result.append(isSeparator ? .sep : .word(t.folded))
                    k += 1
                }
            }
            // Units: the last one written applies to the numbers without one.
            var anyExplicit = false
            for item in result {
                if case .qty(let q) = item, q.factor != nil { anyExplicit = true }
            }
            var index = 0
            while index < result.count {
                // A segment is the run of items up to the next word (or the whole sentence).
                var end = index
                while end < result.count {
                    if !global, case .word = result[end] { break }
                    end += 1
                }
                var lastFactor: Double?
                for i in index..<end {
                    if case .qty(let q) = result[i], let f = q.factor { lastFactor = f }
                }
                if let lastFactor {
                    for i in index..<end {
                        if case .qty(var q) = result[i], q.factor == nil {
                            q.mm = q.raw * lastFactor
                            result[i] = .qty(q)
                        }
                    }
                }
                index = max(end, index + 1)
            }
            return .ok(result, anyExplicit: anyExplicit)
        }

        func ambiguousNumber(_ token: Token) -> CommandRefusal {
            let text = String(chars[token.lo..<token.hi])
            let parts = text.split(whereSeparator: { $0 == "." || $0 == "," }).map(String.init)
            let whole = parts.first ?? text
            let fraction = parts.count > 1 ? parts[1] : ""
            var trimmed = fraction
            while trimmed.hasSuffix("0") { trimmed.removeLast() }
            let decimal = whole + (trimmed.isEmpty ? "" : "," + trimmed)
            let thousands = whole + fraction
            return CommandRefusal(
                code: "R05",
                message: "\(text) iki türlü okunabilir. \(decimal) mm için \(decimal) yazın; \(thousands) mm için \(thousands) yazın.",
                suggestions: [replace(token.lo, token.hi, with: decimal), replace(token.lo, token.hi, with: thousands)])
        }

        func quantities(_ items: [Item]) -> [Quantity] {
            items.compactMap { item -> Quantity? in
                if case .qty(let q) = item { return q }
                return nil
            }
        }

        func unitQuestion(_ shown: String, _ quantities: [Quantity]) -> CommandRefusal? {
            guard let last = quantities.last else { return nil }
            let unitless = quantities.allSatisfy { $0.factor == nil }
            let small = quantities.allSatisfy { $0.raw < 30 }
            guard unitless, small else { return nil }
            return CommandRefusal(
                code: "R06",
                message: "\(shown): milimetre mi, santimetre mi?",
                suggestions: [insert(" mm", at: last.end), insert(" cm", at: last.end)])
        }

        func inRange(_ q: Quantity) -> Bool {
            q.mm >= CommandParser.minSize && q.mm <= CommandParser.maxSize
        }

        // MARK: Which job

        /// Things the iPhone does elsewhere or not at all. Returns nil when the sentence is a job
        /// the command field does.
        static func elsewhere(words: [String], wordSet: Set<String>) -> CommandRefusal? {
            func has(_ list: [String]) -> Bool { list.contains { wordSet.contains($0) } }
            let saving = wordSet.contains("kaydet")

            let mouseTool = has(["arac", "araci", "fareyle", "kavis", "bezier", "egri", "yay", "kontur"])
                || words.filter { $0 == "nokta" }.count >= 2 || wordSet.contains("cizimi")
            if mouseTool {
                return CommandParser.elsewhere("Fare araçları yalnız Windows programında var. iPhone'da parçaya dokunun; yeni kontur için “Parmakla yeni kontur çiz” düğmesini kullanın.")
            }
            if words.contains(where: { $0.hasPrefix("aynal") }) {
                return CommandParser.windowsOnly("Aynalama")
            }
            if wordSet.contains("dikis") || wordSet.contains("pay") {
                return CommandParser.windowsOnly("Dikiş payı")
            }
            let sizeVerbs = has(["buyut", "kucult", "cikar", "ust", "alt", "uret"])
            if wordSet.contains("serileme") || wordSet.contains("serile")
                || (words.contains(where: { $0.hasPrefix("beden") }) && sizeVerbs) {
                return CommandParser.windowsOnly("Beden serileme")
            }
            if has(["pastal", "yerlestir", "yerlestirme"]) {
                return CommandParser.windowsOnly("Pastal yerleştirme")
            }
            if has(["makine", "simulator", "simulatoru"]) {
                return CommandParser.windowsOnly("Makine dosyası")
            }
            if wordSet.contains("dxf") { return CommandParser.windowsOnly("DXF") }
            if has(["pdf", "plotter", "plotterdan", "kesime", "yazdir", "a4", "cikti"]) {
                return CommandParser.windowsOnly("PDF ve plotter çıktısı")
            }
            if (wordSet.contains("disa") && wordSet.contains("aktar")) || wordSet.contains("paylas") {
                return CommandParser.elsewhere("Dosya vermek için aşağıdaki “Dosyayı hazırla (.tcad)” düğmesini, sonra Paylaş düğmesini kullanın.")
            }
            if has(["iplik", "ipligi"]) { return CommandParser.windowsOnly("İplik yönü") }
            if wordSet.contains("adet") || wordSet.contains("kumas") || wordSet.contains("kumasi")
                || (wordSet.contains("bilgileri") && wordSet.contains("parca")) {
                return CommandParser.windowsOnly("Parça bilgileri")
            }
            if words.contains(where: { $0.hasPrefix("katman") }) { return CommandParser.windowsOnly("Katmanlar") }
            if has(["karsi"]) { return CommandParser.windowsOnly("Karşı parça") }
            if has(["kurtarma", "tema", "hakkinda", "surum", "ayrintili", "uzman", "yaziyi"])
                || (wordSet.contains("elbise") && wordSet.contains("istegi")) {
                return CommandParser.windowsOnly("Bu ayar")
            }
            if (wordSet.contains("parcalari") && has(["tasi", "kaydir"])) || (wordSet.contains("birden") && wordSet.contains("fazla")) {
                return CommandParser.windowsOnly("Birden fazla parçayı taşıma")
            }
            let viewWords = has(["yaklastir", "yaklas", "uzaklastir", "uzaklas", "sigdir", "gorunumu", "gorunum"])
                || (wordSet.contains("goster") && has(["tumunu", "hepsini", "seciliyi"]))
            if viewWords {
                return CommandParser.elsewhere("Görünüm komutla değişmez; çizim ekrana sığdırılır. Parçaları taşımak için: 25 sağa taşı")
            }
            if !saving && has(["ac", "yeni", "bos", "sifirdan", "dunku"]) {
                return CommandParser.elsewhere("Proje açma ve yeni proje ana ekrandan yapılır. Önce “Kapat” düğmesine dokunun.")
            }
            if wordSet == ["kapat"] {
                return CommandParser.elsewhere("Çalışma alanını kapatmak için “Kapat” düğmesine dokunun.")
            }
            return nil
        }

        func dispatch(tokens: [Token], words: [String], wordSet: Set<String>, shown: String) -> CommandOutcome {
            func has(_ list: [String]) -> Bool { list.contains { wordSet.contains($0) } }

            let clear = has(["secimi", "secimini", "hicbirini", "hicbiri"])
            let wantsSelect = has(["sec", "secin", "secmek"])
            let adjacent = has(["sonraki", "onceki", "siradaki"])
            let wantsDelete = has(["sil", "kaldir", "silin"]) && !clear
            let isRect = has(["dikdortgen", "kutu"])
            let isSquare = wordSet.contains("kare")
            let isLine = wordSet.contains("cizgi")
            let create = isRect || isSquare || isLine
            let rotate = has(["dondur", "derece", "saat"])
                || (wordSet.contains("ceyrek") && wordSet.contains("tur"))
                || (wordSet.contains("yarim") && wordSet.contains("tur"))
            let directionWords = words.filter { Self.directions.contains($0) }
            let moveWords = has(["tasi", "kaydir"])
            let positionWords = words.contains { $0.hasPrefix("konum") || $0.hasPrefix("nokta") }
            let move = moveWords || ((!directionWords.isEmpty || positionWords) && !rotate && !create)
            let sizeWords = words.contains { Self.widthWords.contains($0) || Self.heightWords.contains($0) || Self.lengthWords.contains($0) }
            let resize = sizeWords && !create
            let undo = wordSet.contains("geri")
            let redo = has(["yinele", "ileri"]) || (wordSet.contains("tekrar") && wordSet.contains("yap"))
            let savesCopy = has(["farkli", "baska", "yedek", "yedegini", "kopyasini", "kopya"]) || wordSet.contains("yeni")
            let save = has(["kaydet", "sakla"]) || has(["yedek", "yedegini"])
            let duplicate = has(["cogalt", "kopyala", "cogaltin"])
                || ((wordSet.contains("kopya") || wordSet.contains("kopyasini")) && !wordSet.contains("kaydet")
                    && has(["daha", "cikar"]))

            var jobs: [String] = []
            if wantsDelete { jobs.append("delete") }
            if wantsSelect || adjacent { jobs.append("select") }
            if clear { jobs.append("clear") }
            if duplicate { jobs.append("duplicate") }
            if undo { jobs.append("undo") }
            if redo { jobs.append("redo") }
            if save { jobs.append("save") }
            if create { jobs.append("create") }
            if move { jobs.append("move") }
            if rotate { jobs.append("rotate") }
            if resize { jobs.append("resize") }
            if jobs.count > 1 { return .refuse(CommandParser.twoJobs()) }

            guard let job = jobs.first else { return noJob(tokens: tokens, words: words, wordSet: wordSet, shown: shown) }
            switch job {
            case "create":
                return parseCreate(tokens, words: words, wordSet: wordSet, shown: shown,
                                   kind: isRect ? 0 : (isSquare ? 1 : 2))
            case "resize": return parseResize(tokens, words: words, shown: shown)
            case "move":
                if positionWords { return parseMoveTo(tokens, words: words, wordSet: wordSet) }
                return parseMove(tokens, words: words, wordSet: wordSet, explicitVerb: moveWords)
            case "rotate": return parseRotate(tokens, words: words, wordSet: wordSet)
            case "delete": return parsePieceJob(scannedWords: words, tokens: tokens, delete: true)
            case "select":
                if adjacent { return parseAdjacent(words: words, wordSet: wordSet) }
                return parsePieceJob(scannedWords: words, tokens: tokens, delete: false)
            case "clear": return leftover(of: words, allowed: ["secimi", "secimini", "birak", "kaldir", "hicbirini", "hicbiri", "sec", "secme", "sifirla"], then: .command(.clearSelection), tokens: tokens, shown: shown)
            case "duplicate": return parseDuplicate(tokens, words: words, shown: shown)
            case "undo": return parseUndo(tokens, words: words, shown: shown)
            case "redo":
                return leftover(of: words, allowed: ["yinele", "ileri", "al", "tekrar", "yap", "islemi"], then: .command(.redo), tokens: tokens, shown: shown)
            default:
                let copy = savesCopy
                return leftover(of: words,
                                allowed: ["kaydet", "projeyi", "proje", "sakla", "farkli", "baska", "adla", "yeni", "isimle", "yedek", "yedegini", "kopyasini", "kopya", "al", "kopyasi"],
                                then: .command(copy ? .saveCopy : .save), tokens: tokens, shown: shown)
            }
        }

        /// A sentence with no job word: a bare pair of numbers is not enough to act on.
        func noJob(tokens: [Token], words: [String], wordSet: Set<String>, shown: String) -> CommandOutcome {
            let numbers = tokens.filter { $0.isNumber }
            let allowed: Set<String> = Self.softVerbs.union(["yap", "olsun", "dikey", "yatay", "x", "carpi",
                                                             "olcusunu", "olcusu", "olcu"])
            let known = words.allSatisfy { allowed.contains($0) || Self.connectors.contains($0) }
            // "95 x 60 yap": two sizes and a verb.
            if known, wordSet.contains("yap") || wordSet.contains("olsun"), numbers.count == 2 {
                return parseResize(tokens, words: words, shown: shown)
            }
            // "dikey 80", "yatay 150": a line.
            if known, wordSet.contains("dikey") || wordSet.contains("yatay"), numbers.count == 1 {
                return parseCreate(tokens, words: words, wordSet: wordSet, shown: shown, kind: 2)
            }
            return .refuse(typoOrUnclear(tokens: tokens, words: words, shown: shown))
        }

        func leftover(of words: [String], allowed: Set<String>, then outcome: CommandOutcome,
                      tokens: [Token], shown: String) -> CommandOutcome {
            let extra = words.filter {
                !allowed.contains($0) && !Self.softVerbs.contains($0) && !Self.connectors.contains($0) && !Self.pieceWords.contains($0)
            }
            if !extra.isEmpty || tokens.contains(where: { $0.isNumber }) {
                return .refuse(namedOrUnclear(tokens: tokens, extra: extra, shown: shown))
            }
            return outcome
        }

        /// Unknown words: a piece name in front of a verb gets its own message, anything else the
        /// general one.
        func namedOrUnclear(tokens: [Token], extra: [String], shown: String) -> CommandRefusal {
            let query = extra.joined(separator: " ")
            if !query.isEmpty {
                for candidate in nameCandidates(query) {
                    let hits = matches(candidate)
                    if let first = hits.first {
                        let name = CommandText.lower(pieceNames[first])
                        return CommandRefusal(
                            code: "R27",
                            message: "Önce parçayı seçin: \(name) seç. Sonra: 25 sağa taşı",
                            suggestions: ["\(name) seç"])
                    }
                }
            }
            let words = tokens.filter { $0.isWord }.map { $0.folded }
            return typoOrUnclear(tokens: tokens, words: words, shown: shown)
        }

        static let vocabulary: [(folded: String, shown: String)] = [
            ("dikdortgen", "dikdörtgen"), ("dondur", "döndür"), ("kaydir", "kaydır"), ("cogalt", "çoğalt"),
            ("kopyala", "kopyala"), ("yukariya", "yukarıya"), ("asagiya", "aşağıya"), ("genisligi", "genişliği"),
            ("yuksekligi", "yüksekliği"), ("yinele", "yinele"), ("kaydet", "kaydet"), ("santimetre", "santimetre"),
            ("milimetre", "milimetre")
        ]

        func typoOrUnclear(tokens: [Token], words: [String], shown: String) -> CommandRefusal {
            for token in tokens where token.isWord && token.folded.count > 5 {
                for entry in Self.vocabulary where Self.oneEditApart(token.folded, entry.folded) {
                    let fixed = replace(token.lo, token.hi, with: entry.shown)
                    return CommandParser.unclear(shown, suggestions: [fixed],
                                                 prefix: "Şunu mu demek istediniz: \(fixed)? ")
                }
            }
            return CommandParser.unclear(shown)
        }

        /// One substitution, insertion, deletion or swap of neighbours.
        static func oneEditApart(_ a: String, _ b: String) -> Bool {
            guard a != b else { return false }
            let x = Array(a), y = Array(b)
            if abs(x.count - y.count) > 1 { return false }
            if x.count == y.count {
                let diff = (0..<x.count).filter { x[$0] != y[$0] }
                if diff.count == 1 { return true }
                if diff.count == 2, diff[1] == diff[0] + 1, x[diff[0]] == y[diff[1]], x[diff[1]] == y[diff[0]] { return true }
                return false
            }
            let (longer, shorter) = x.count > y.count ? (x, y) : (y, x)
            var skipped = false
            var i = 0, j = 0
            while i < longer.count && j < shorter.count {
                if longer[i] == shorter[j] {
                    i += 1
                    j += 1
                } else if !skipped {
                    skipped = true
                    i += 1
                } else {
                    return false
                }
            }
            return true
        }

        // MARK: Create

        func parseCreate(_ tokens: [Token], words: [String], wordSet: Set<String>, shown: String, kind: Int) -> CommandOutcome {
            let allowed: Set<String> = ["dikdortgen", "kutu", "kare", "cizgi", "yatay", "dikey", "x", "carpi",
                                        "olcusunde", "olcusu", "olcu"]
                .union(Self.widthWords).union(Self.heightWords).union(Self.lengthWords)
            let extra = words.filter { !allowed.contains($0) && !Self.softVerbs.contains($0) && !Self.connectors.contains($0) && !Self.pieceWords.contains($0) }
            if !extra.isEmpty { return .refuse(namedOrUnclear(tokens: tokens, extra: extra, shown: shown)) }
            if wordSet.contains("yatay") && wordSet.contains("dikey") { return .refuse(CommandParser.twoJobs()) }

            let built = items(tokens, global: true)
            guard case .ok(let list, _) = built else {
                if case .refusal(let r) = built { return .refuse(r) }
                return .refuse(CommandParser.unclear(shown))
            }
            let qs = quantities(list)

            if kind == 2 {
                guard qs.count <= 1 else { return .refuse(CommandParser.unclear(shown)) }
                guard let q = qs.first else {
                    return .refuse(CommandParser.missingNumber(example: "150 çizgi"))
                }
                guard inRange(q) else { return .refuse(CommandParser.outOfRange()) }
                if let ask = unitQuestion(shown, qs) { return .ask(ask) }
                return .command(.addLine(length: q.mm, vertical: wordSet.contains("dikey")))
            }

            if kind == 1 {
                guard !qs.isEmpty else { return .refuse(CommandParser.missingNumber(example: "kare 100")) }
                guard qs.count == 1 || (qs.count == 2 && abs(qs[0].mm - qs[1].mm) < 1e-9) else {
                    return .refuse(CommandParser.unclear(shown))
                }
                guard inRange(qs[0]) else { return .refuse(CommandParser.outOfRange()) }
                if let ask = unitQuestion(shown, qs) { return .ask(ask) }
                return .command(.addRectangle(width: qs[0].mm, height: qs[0].mm))
            }

            var width: Quantity?
            var height: Quantity?
            let labelled = list.contains { item in
                if case .word(let w) = item { return Self.widthWords.contains(w) || Self.heightWords.contains(w) }
                return false
            }
            if labelled {
                var pendingLabel: String?
                for item in list {
                    switch item {
                    case .word(let w):
                        if Self.widthWords.contains(w) { pendingLabel = "w" }
                        else if Self.heightWords.contains(w) { pendingLabel = "h" }
                    case .qty(let q):
                        if pendingLabel == "w", width == nil { width = q }
                        else if pendingLabel == "h", height == nil { height = q }
                        else { return .refuse(CommandParser.twoDimensionsNeeded()) }
                        pendingLabel = nil
                    case .sep:
                        break
                    }
                }
                if width == nil || height == nil { return .refuse(CommandParser.twoDimensionsNeeded()) }
            } else {
                guard qs.count == 2 else { return .refuse(CommandParser.twoDimensionsNeeded()) }
                width = qs[0]
                height = qs[1]
            }
            guard let w = width, let h = height else { return .refuse(CommandParser.twoDimensionsNeeded()) }
            guard inRange(w), inRange(h) else { return .refuse(CommandParser.outOfRange()) }
            if let ask = unitQuestion(shown, [w, h]) { return .ask(ask) }
            return .command(.addRectangle(width: w.mm, height: h.mm))
        }

        // MARK: Resize

        func parseResize(_ tokens: [Token], words: [String], shown: String) -> CommandOutcome {
            let allowed: Set<String> = ["x", "carpi", "olcusunu", "olcusu", "olcu", "yap", "olsun"]
                .union(Self.widthWords).union(Self.heightWords).union(Self.lengthWords)
            let extra = words.filter { !allowed.contains($0) && !Self.softVerbs.contains($0) && !Self.connectors.contains($0) && !Self.pieceWords.contains($0) }
            if !extra.isEmpty { return .refuse(namedOrUnclear(tokens: tokens, extra: extra, shown: shown)) }

            let built = items(tokens, global: true)
            guard case .ok(let list, _) = built else {
                if case .refusal(let r) = built { return .refuse(r) }
                return .refuse(CommandParser.unclear(shown))
            }
            let qs = quantities(list)
            var width: Quantity?
            var height: Quantity?
            let labelled = list.contains { item in
                if case .word(let w) = item {
                    return Self.widthWords.contains(w) || Self.heightWords.contains(w) || Self.lengthWords.contains(w)
                }
                return false
            }
            if labelled {
                var pendingLabel: String?
                for item in list {
                    switch item {
                    case .word(let w):
                        if Self.widthWords.contains(w) || Self.lengthWords.contains(w) { pendingLabel = "w" }
                        else if Self.heightWords.contains(w) { pendingLabel = "h" }
                    case .qty(let q):
                        if pendingLabel == "w", width == nil { width = q }
                        else if pendingLabel == "h", height == nil { height = q }
                        else { return .refuse(CommandParser.unclear(shown)) }
                        pendingLabel = nil
                    case .sep:
                        break
                    }
                }
                if width == nil && height == nil {
                    return .refuse(CommandParser.missingNumber(example: "genişliği 95 yap"))
                }
                if pendingLabel != nil || qs.isEmpty {
                    return .refuse(CommandParser.missingNumber(example: "genişliği 95 yap"))
                }
            } else {
                guard qs.count == 2 else { return .refuse(CommandParser.unclear(shown)) }
                width = qs[0]
                height = qs[1]
            }
            let given = [width, height].compactMap { $0 }
            guard given.allSatisfy(inRange) else { return .refuse(CommandParser.outOfRange()) }
            if let ask = unitQuestion(shown, given) { return .ask(ask) }
            return .command(.resize(width: width?.mm, height: height?.mm))
        }

        // MARK: Move

        func parseMove(_ tokens: [Token], words: [String], wordSet: Set<String>, explicitVerb: Bool) -> CommandOutcome {
            let allowed = Self.directions.union(["tasi", "kaydir", "x", "carpi"])
            let extra = words.filter { !allowed.contains($0) && !Self.softVerbs.contains($0) && !Self.connectors.contains($0) && !Self.pieceWords.contains($0) }
            if !extra.isEmpty { return .refuse(namedOrUnclear(tokens: tokens, extra: extra, shown: String(chars))) }

            let built = items(tokens, global: false)
            guard case .ok(let list, _) = built else {
                if case .refusal(let r) = built { return .refuse(r) }
                return .refuse(CommandParser.unclear(String(chars)))
            }
            // Numbers and direction words, in the order written.
            var sequence: [Item] = []
            for item in list {
                switch item {
                case .qty: sequence.append(item)
                case .word(let w): if Self.directions.contains(w) { sequence.append(item) }
                case .sep: break
                }
            }
            if sequence.isEmpty { return .refuse(CommandParser.missingNumber(example: "25 sağa taşı")) }
            let numberCount = sequence.filter { if case .qty = $0 { return true } else { return false } }.count
            if numberCount == 0 { return .refuse(CommandParser.missingNumber(example: "25 sağa taşı")) }

            var right = 0.0
            var down = 0.0
            var usedHorizontal = false
            var usedVertical = false
            var pairs = 0
            var lastRaw = 0.0
            var lastUnitless = false
            var k = 0
            while k < sequence.count {
                var quantity: Quantity?
                var direction: String?
                if case .qty(let q) = sequence[k], k + 1 < sequence.count, case .word(let w) = sequence[k + 1] {
                    quantity = q
                    direction = w
                    k += 2
                } else if case .word(let w) = sequence[k], k + 1 < sequence.count, case .qty(let q) = sequence[k + 1] {
                    quantity = q
                    direction = w
                    k += 2
                } else if case .word = sequence[k] {
                    return .refuse(CommandParser.missingNumber(example: "25 sağa taşı"))
                } else {
                    return .refuse(missingDirection())
                }
                guard let q = quantity, let d = direction else { return .refuse(missingDirection()) }
                guard q.mm > 0, q.mm <= CommandParser.maxSize else {
                    return .refuse(CommandRefusal(
                        code: "R04",
                        message: "Taşıma mesafesi 0'dan büyük, en çok 20000 mm olmalı. Örnek: 25 sağa taşı",
                        suggestions: ["25 sağa taşı"]))
                }
                let horizontal = d.hasPrefix("sa") || d.hasPrefix("so")
                let repeatedAxis = horizontal ? usedHorizontal : usedVertical
                if repeatedAxis { return .refuse(CommandParser.twoJobs()) }
                if horizontal { usedHorizontal = true } else { usedVertical = true }
                let toTheRight = d == "saga" || d == "sag"
                let toTheLeft = d == "sola" || d == "sol"
                let upwards = d.hasPrefix("yukari")
                if toTheRight { right += q.mm }
                else if toTheLeft { right -= q.mm }
                else if upwards { down -= q.mm }
                else { down += q.mm }
                pairs += 1
                lastRaw = q.raw
                lastUnitless = q.factor == nil
            }
            // "90 sağa" is a move of 90 mm or a quarter turn: do not guess.
            if !explicitVerb, pairs == 1, lastUnitless, [90.0, 180.0, 270.0].contains(lastRaw) {
                let n = CommandText.number(lastRaw)
                let side = (right != 0) ? (right > 0 ? "sağa" : "sola") : ""
                if !side.isEmpty {
                    return .ask(CommandRefusal(
                        code: "R16b",
                        message: "“\(n) \(side)” taşıma mı, döndürme mi? Örnek: \(n) mm \(side) taşı · \(n) \(side) döndür",
                        suggestions: ["\(n) mm \(side) taşı", "\(n) \(side) döndür"]))
                }
            }
            return .command(.move(right: right, down: down))
        }

        func missingDirection() -> CommandRefusal {
            CommandRefusal(
                code: "R01b",
                message: "Hangi yöne? Sağa, sola, yukarı ya da aşağı yazın. Örnek: 25 sağa taşı",
                suggestions: ["25 sağa taşı"])
        }

        func parseMoveTo(_ tokens: [Token], words: [String], wordSet: Set<String>) -> CommandOutcome {
            let shown = String(chars)
            if !wordSet.isDisjoint(with: ["yukaridan", "ustten", "asagidan", "alttan"]) {
                return .refuse(CommandParser.elsewhere("iPhone'da Y değeri alttan ölçülür; “yukarıdan” kullanılamaz. X ve Y alanlarındaki gibi yazın. Örnek: x 100 y 50 konumuna taşı"))
            }
            let allowed: Set<String> = ["tasi", "kaydir", "x", "y", "soldan", "carpi"]
            let extra = words.filter {
                !allowed.contains($0) && !Self.softVerbs.contains($0) && !Self.connectors.contains($0)
                    && !Self.pieceWords.contains($0) && !$0.hasPrefix("konum") && !$0.hasPrefix("nokta")
            }
            if !extra.isEmpty { return .refuse(namedOrUnclear(tokens: tokens, extra: extra, shown: shown)) }
            let built = items(tokens, global: false)
            guard case .ok(let list, _) = built else {
                if case .refusal(let r) = built { return .refuse(r) }
                return .refuse(CommandParser.unclear(shown))
            }
            let qs = quantities(list)
            guard qs.count == 2 else { return .refuse(CommandParser.missingNumber(example: "x 100 y 50 konumuna taşı")) }
            var x = qs[0]
            var y = qs[1]
            if wordSet.contains("x") && wordSet.contains("y") {
                var label: String?
                var gotX: Quantity?
                var gotY: Quantity?
                for item in list {
                    switch item {
                    case .word(let w):
                        if w == "x" || w == "y" { label = w }
                    case .qty(let q):
                        if label == "x" { gotX = q } else if label == "y" { gotY = q }
                        label = nil
                    case .sep:
                        break
                    }
                }
                guard let a = gotX, let b = gotY else { return .refuse(CommandParser.unclear(shown)) }
                x = a
                y = b
            }
            guard x.mm >= 0, y.mm >= 0, x.mm <= 100_000, y.mm <= 100_000 else {
                return .refuse(CommandRefusal(
                    code: "R04",
                    message: "Konum 0 ile 100000 mm arasında olmalı. Örnek: x 100 y 50 konumuna taşı",
                    suggestions: ["x 100 y 50 konumuna taşı"]))
            }
            return .command(.moveTo(x: x.mm, y: y.mm))
        }

        // MARK: Rotate

        func parseRotate(_ tokens: [Token], words: [String], wordSet: Set<String>) -> CommandOutcome {
            let shown = String(chars)
            let allowed: Set<String> = ["dondur", "derece", "saat", "yonunde", "yonunun", "yone", "tersine", "tersi", "ters",
                                        "ceyrek", "yarim", "tur", "saga", "sag", "sola", "sol", "bas", "asagi", "yon"]
            let extra = words.filter { !allowed.contains($0) && !Self.softVerbs.contains($0) && !Self.connectors.contains($0) && !Self.pieceWords.contains($0) }
            if !extra.isEmpty { return .refuse(namedOrUnclear(tokens: tokens, extra: extra, shown: shown)) }

            let built = items(tokens, global: false)
            guard case .ok(let list, _) = built else {
                if case .refusal(let r) = built { return .refuse(r) }
                return .refuse(CommandParser.unclear(shown))
            }
            let qs = quantities(list)
            var degrees = 90
            if wordSet.contains("yarim") || (wordSet.contains("bas") && wordSet.contains("asagi")) { degrees = 180 }
            if let q = qs.first {
                guard qs.count == 1, q.factor == nil, q.raw == q.raw.rounded() else { return .refuse(CommandParser.angle()) }
                guard [90.0, 180.0, 270.0].contains(q.raw) else { return .refuse(CommandParser.angle()) }
                degrees = Int(q.raw)
            }
            let counter = wordSet.contains("sola") || wordSet.contains("sol")
                || (wordSet.contains("saat") && (wordSet.contains("tersine") || wordSet.contains("tersi") || wordSet.contains("ters")))
            let clockwise = wordSet.contains("saga") || wordSet.contains("sag")
                || (wordSet.contains("saat") && wordSet.contains("yonunde"))
            if counter && clockwise { return .refuse(CommandParser.unclear(shown)) }
            return .command(.rotate(degrees: degrees, clockwise: !counter))
        }

        // MARK: Pieces by name

        static let nameVocabulary: Set<String> = ["sec", "secin", "secmek", "sil", "silin", "kaldir", "adli"]

        func foldedNames() -> [String] { pieceNames.map { CommandText.fold($0) } }

        func matches(_ query: String) -> [Int] {
            let needle = CommandText.fold(query)
            guard !needle.isEmpty else { return [] }
            return foldedNames().enumerated().filter { $0.element.contains(needle) }.map { $0.offset }
        }

        /// The query as typed, then without the accusative ending ("kolu" → "kol", "bedeni" → "beden").
        func nameCandidates(_ query: String) -> [String] {
            var list = [query]
            let lower = CommandText.lower(query)
            for ending in ["i", "ı", "u", "ü"] where lower.hasSuffix(ending) && lower.count > 1 {
                list.append(String(lower.dropLast()))
            }
            for ending in ["yi", "yı", "yu", "yü"] where lower.hasSuffix(ending) && lower.count > 2 {
                list.append(String(lower.dropLast(2)))
            }
            return list
        }

        func parsePieceJob(scannedWords: [String], tokens: [Token], delete: Bool) -> CommandOutcome {
            // The name is whatever remains once the verb and filler are taken away. Number words
            // stay as written: "ön" must not become the number ten.
            let raw = Self.scan(chars).filter { $0.kind != .separator }
            let nameTokens = raw.filter { token in
                if case .suffix = token.kind { return false }
                let f = token.folded
                return !Self.nameVocabulary.contains(f) && !Self.fillers.contains(f)
                    && !Self.pieceWords.contains(f) && !Self.connectors.contains(f)
                    && !Self.softVerbs.contains(f)
            }
            let shown = String(chars)
            if nameTokens.isEmpty {
                if delete { return .command(.delete(name: nil)) }
                if pieceNames.isEmpty { return .refuse(CommandParser.noPieces()) }
                return .refuse(namedPiecesRefusal(shown: shown))
            }
            if pieceNames.isEmpty { return .refuse(CommandParser.noPieces()) }
            let query = nameTokens.map { $0.text }.joined(separator: " ")
            for candidate in nameCandidates(query) {
                let hits = matches(candidate)
                if hits.isEmpty { continue }
                if hits.count == 1 {
                    return .command(delete ? .delete(name: candidate) : .select(name: candidate))
                }
                let choices = hits.prefix(3).map { index -> CommandRefusal.ExactChoice in
                    let name = pieceNames[index]
                    return CommandRefusal.ExactChoice(
                        label: name,
                        command: delete ? .deleteAt(index: index, name: name) : .selectAt(index: index, name: name))
                }
                return .ask(CommandRefusal(
                    code: "R15b",
                    message: "\(hits.count) parça eşleşti. Hangisi?",
                    suggestions: hits.prefix(3).map { pieceNames[$0] },
                    exactChoices: Array(choices)))
            }
            return .refuse(namedPiecesRefusal(shown: query, notFound: true))
        }

        func namedPiecesRefusal(shown: String, notFound: Bool = false) -> CommandRefusal {
            let sample = pieceNames.prefix(3).map { "\(CommandText.lower($0)) seç" }
            let example = sample.first ?? "ön beden seç"
            let message = notFound
                ? "“\(shown)” adında parça bulunamadı. Örnek: \(example)"
                : "Hangi parça? Örnek: \(example)"
            return CommandRefusal(code: "R15a", message: message, suggestions: Array(sample))
        }

        func parseAdjacent(words: [String], wordSet: Set<String>) -> CommandOutcome {
            let tokens = Self.scan(chars)
            let allowed: Set<String> = ["sonraki", "onceki", "siradaki", "sec", "secin"]
            let extra = words.filter { !allowed.contains($0) && !Self.pieceWords.contains($0) && !Self.softVerbs.contains($0) }
            if !extra.isEmpty || tokens.contains(where: { $0.isNumber }) {
                return .refuse(CommandParser.unclear(String(chars)))
            }
            if wordSet.contains("sonraki") && wordSet.contains("onceki") { return .refuse(CommandParser.twoJobs()) }
            if pieceNames.isEmpty { return .refuse(CommandParser.noPieces()) }
            return .command(.selectAdjacent(next: !wordSet.contains("onceki")))
        }

        // MARK: Rename

        func rename(_ scanned: [Token]) -> CommandOutcome? {
            guard let position = scanned.firstIndex(where: { $0.isWord && Self.renameWords.contains($0.folded) }) else {
                return nil
            }
            let before = scanned[..<position]
            let shown = String(chars)
            for token in before where !(Self.fillers.contains(token.folded) || Self.pieceWords.contains(token.folded)
                                         || Self.connectors.contains(token.folded)) {
                return .refuse(CommandParser.unclear(shown))
            }
            var name = Array(scanned[(position + 1)...])
            let trailing: Set<String> = ["olsun", "yap", "et", "ver", "degistir", "olarak", "yapin", "edin"]
            while let last = name.last, last.isWord, trailing.contains(last.folded) { name.removeLast() }
            guard let first = name.first, let last = name.last else {
                return .refuse(CommandRefusal(code: "R24", message: "Yeni ad boş olamaz. Örnek: adı ön beden olsun",
                                              suggestions: ["adı ön beden olsun"]))
            }
            let text = String(chars[first.lo..<last.hi]).trimmingCharacters(in: .whitespacesAndNewlines)
            if text.isEmpty {
                return .refuse(CommandRefusal(code: "R24", message: "Yeni ad boş olamaz. Örnek: adı ön beden olsun",
                                              suggestions: ["adı ön beden olsun"]))
            }
            if text.count > 128 {
                return .refuse(CommandRefusal(code: "R24", message: "Ad en fazla 128 karakter olabilir. Örnek: adı ön beden olsun",
                                              suggestions: ["adı ön beden olsun"]))
            }
            return .command(.rename(text))
        }

        // MARK: Duplicate, undo

        func parseDuplicate(_ tokens: [Token], words: [String], shown: String) -> CommandOutcome {
            let allowed: Set<String> = ["cogalt", "cogaltin", "kopyala", "kopya", "kopyasini", "daha", "cikar"]
            let extra = words.filter { !allowed.contains($0) && !Self.softVerbs.contains($0) && !Self.connectors.contains($0) && !Self.pieceWords.contains($0) }
            if !extra.isEmpty { return .refuse(namedOrUnclear(tokens: tokens, extra: extra, shown: shown)) }
            // "bir kopya daha": the one is allowed, any other number is not.
            let numbers = tokens.compactMap { token -> Double? in
                if case .number(let v, _, _) = token.kind { return v }
                return nil
            }
            if numbers.contains(where: { $0 != 1 }) || numbers.count > 1 { return .refuse(CommandParser.unclear(shown)) }
            return .command(.duplicate)
        }

        func parseUndo(_ tokens: [Token], words: [String], shown: String) -> CommandOutcome {
            let allowed: Set<String> = ["geri", "al", "adim", "adimi", "son", "islemi", "islem"]
            let extra = words.filter { !allowed.contains($0) && !Self.softVerbs.contains($0) && !Self.connectors.contains($0) }
            if !extra.isEmpty { return .refuse(namedOrUnclear(tokens: tokens, extra: extra, shown: shown)) }
            let numbers = tokens.compactMap { token -> Token? in token.isNumber ? token : nil }
            guard !numbers.isEmpty else { return .command(.undo(steps: 1)) }
            guard numbers.count == 1, case .number(let value, let ambiguous, let negative) = numbers[0].kind else {
                return .refuse(CommandParser.unclear(shown))
            }
            if negative { return .refuse(CommandParser.negative()) }
            if ambiguous { return .refuse(ambiguousNumber(numbers[0])) }
            guard value == value.rounded(), value >= 1, value <= 10 else {
                return .refuse(CommandRefusal(
                    code: "R04",
                    message: "Geri alma adımı 1 ile 10 arasında olmalı. Örnek: 3 adım geri",
                    suggestions: ["3 adım geri"]))
            }
            return .command(.undo(steps: Int(value)))
        }
    }
}
