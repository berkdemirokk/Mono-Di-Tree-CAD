import Foundation

/// Parses a single editable starter pattern. Every token must describe a supported
/// model, measurement, output or request phrase before a project is produced.
enum PatternBrief {
    enum Output: Equatable { case pdf, tcad }

    private struct Problem: LocalizedError {
        let message: String
        init(_ message: String) { self.message = message }
        var errorDescription: String? { message }
    }

    static func parse(_ text: String) throws -> DraftProject {
        try read(text).project
    }

    static func output(in text: String) throws -> Output? {
        try read(text).output
    }

    private enum Axis { case width, length }
    private static let widthWords: Set<String> = ["en", "eni", "eninde", "enine", "genislik", "genisligi", "genisliginde"]
    private static let lengthWords: Set<String> = ["boy", "boyu", "boyunda", "boyuna", "uzunluk", "uzunlugu", "uzunlugunda"]
    private static let requestWords: Set<String> = [
        "bir", "bana", "icin", "lutfen", "ve", "sonra", "olarak", "olan", "olsun",
        "kalip", "kalibi", "kalibini", "model", "modeli", "modelini", "dosyasi",
        "ciz", "cizer", "misin", "cizmeni", "istiyorum", "hazirla", "hazirlar",
        "olustur", "olusturur", "uret", "ver", "cikti", "ciktisi", "ciktisini", "kaydet"
    ]

    private static func axis(_ word: String?) -> Axis? {
        guard let word else { return nil }
        if widthWords.contains(word) { return .width }
        if lengthWords.contains(word) { return .length }
        return nil
    }

    private static func factor(_ word: String?) -> Double? {
        switch word {
        case "cm", "santim", "santimetre": return 10
        case "mm", "milimetre": return 1
        default: return nil
        }
    }

    private static func read(_ text: String) throws -> (project: DraftProject, output: Output?) {
        guard text.utf16.count <= 512 else { throw Problem("Tarifi 512 karakter içinde yazın.") }
        let normalized = TurkishText.fold(TurkishText.normalize(text))
            .replacingOccurrences(of: #"(?<=[0-9])[,](?=[0-9])"#, with: ".", options: .regularExpression)
            .replacingOccurrences(of: #"(?<=[0-9])(?=cm|mm|x)"#, with: " ", options: .regularExpression)
            .replacingOccurrences(of: #"(?<=x)(?=[0-9])"#, with: " ", options: .regularExpression)
            .replacingOccurrences(of: "×", with: " x ")
        let words = normalized.split { $0.isWhitespace || ",;:".contains($0) }.map(String.init)
        var index = 0
        func peek(_ offset: Int = 0) -> String? {
            index + offset < words.count ? words[index + offset] : nil
        }
        func number(_ word: String?) -> Double? {
            guard let word, word.range(of: #"^[0-9]+(?:\.[0-9]+)?$"#, options: .regularExpression) != nil,
                  let value = Double(word), value.isFinite else { return nil }
            return value
        }
        let example = "Ölçüyü birimiyle yazın. Örneğin 65 cm eninde, 85 cm boyunda."
        var width: Double?, length: Double?, pocket: Bool?, model: StudioPattern?, output: Output?
        func set(_ axis: Axis, _ value: Double) throws {
            switch axis {
            case .width:
                guard width == nil else { throw Problem("En için tek ölçü yazın.") }
                width = value
            case .length:
                guard length == nil else { throw Problem("Boy için tek ölçü yazın.") }
                length = value
            }
        }

        while let word = peek() {
            if let label = axis(word) {
                guard let value = number(peek(1)), let unit = factor(peek(2)) else { throw Problem(example) }
                try set(label, value * unit)
                index += 3
                continue
            }
            if let first = number(word) {
                index += 1
                let firstUnit = factor(peek())
                if firstUnit != nil { index += 1 }
                if peek() == "x" {
                    guard let second = number(peek(1)) else { throw Problem(example) }
                    index += 2
                    let secondUnit = factor(peek())
                    if secondUnit != nil { index += 1 }
                    guard let widthUnit = firstUnit ?? secondUnit, let lengthUnit = secondUnit ?? firstUnit else { throw Problem(example) }
                    try set(.width, first * widthUnit)
                    try set(.length, second * lengthUnit)
                } else {
                    guard let firstUnit, let label = axis(peek()) else { throw Problem(example) }
                    try set(label, first * firstUnit)
                    index += 1
                }
                continue
            }
            let requestedModel: StudioPattern?
            switch word {
            case "onluk", "onlugu": requestedModel = .apron
            case "etek", "etegi": requestedModel = .skirt
            case "bez" where peek(1) == "canta" || peek(1) == "cantasi":
                requestedModel = .tote
                index += 1
            default: requestedModel = nil
            }
            if let requestedModel {
                guard model == nil else { throw Problem("Her tarifte tek model seçin.") }
                model = requestedModel
            } else if word == "cepli" || word == "cepsiz" {
                guard pocket == nil else { throw Problem("Tek cep seçeneği belirtin.") }
                pocket = word == "cepli"
            } else if word == "pdf" || word == "tcad" {
                let requested: Output = word == "pdf" ? .pdf : .tcad
                guard output == nil || output == requested else { throw Problem("Tek çıktı biçimi seçin: PDF veya TCAD.") }
                output = requested
            } else if !requestWords.contains(word) {
                throw Problem("Tarifteki “\(words[index])” ifadesi desteklenmiyor.")
            }
            index += 1
        }
        guard let model else { throw Problem("Modeli belirtin: önlük, etek veya bez çanta.") }
        guard model == .apron else {
            guard width == nil, length == nil, pocket == nil else {
                throw Problem("Ölçü ve cep seçeneği şimdilik önlükte kullanılabilir.")
            }
            return (model.makeProject(), output)
        }
        guard var project = StudioPattern.makeApron(width: width ?? 600, length: length ?? 810) else {
            throw Problem("Önlük eni 30–120 cm, boyu 35–150 cm aralığında olmalı.")
        }
        if pocket == false { project.pieces.removeAll { $0.name == "Cep" } }
        return (project, output)
    }
}
