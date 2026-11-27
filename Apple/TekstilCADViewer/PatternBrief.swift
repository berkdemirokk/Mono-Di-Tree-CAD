import Foundation

/// Parses a single editable starter pattern. Every token must describe a supported
/// model, measurement, output or request phrase before a project is produced.
enum PatternBrief {
    enum Output: Equatable { case pdf, tcad, paper, plotter }

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

    static func read(_ text: String, previous: DraftProject? = nil) throws -> (project: DraftProject, output: Output?) {
        guard text.utf16.count <= 512 else { throw Problem("Tarifi 512 karakter içinde yazın.") }
        let foldedBrief = TurkishText.fold(TurkishText.normalize(text))
        if foldedBrief.contains("yerlestir") {
            let source: DraftProject
            if let previous {
                source = previous
            } else if foldedBrief.contains("onluk") || foldedBrief.contains("onlugu") {
                source = StudioPattern.apron.makeProject()
            } else if foldedBrief.contains("etek") {
                source = StudioPattern.skirt.makeProject()
            } else if foldedBrief.contains("bez canta") || foldedBrief.contains("bez cantasi") {
                source = StudioPattern.tote.makeProject()
            } else {
                throw Problem("Yerleşim için mevcut kalıbı açın veya önlük belirtin.")
            }
            guard let marker = try MarkerBrief.parse(text, source: source) else {
                throw Problem("Yerleşim tarifi anlaşılamadı.")
            }
            return (try MarkerLayout.arrange(source, widthMM: marker.widthMM,
                                             gapMM: marker.gapMM, marginMM: marker.marginMM,
                                             quantities: marker.quantities), marker.output)
        }
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

        let printVerbs: Set<String> = ["bastir", "yazdir"]
        let paperDestinations: Set<String> = ["a4", "yazicida", "yaziciya", "kagida"]
        let plotterDestinations: Set<String> = ["makinada", "makinede", "makinaya", "makineye", "plotterda", "plottera"]
        func select(_ format: Output) throws {
            guard output == nil || output == format else { throw Problem("Bir çıktı seçin: plotter, A4, PDF veya TCAD.") }
            output = format
        }
        while let word = peek() {
            let followedByPrint = peek(1).map(printVerbs.contains) == true
            if (plotterDestinations.contains(word) || paperDestinations.contains(word)) && followedByPrint {
                try select(plotterDestinations.contains(word) ? .plotter : .paper)
                index += 2
                continue
            }
            if printVerbs.contains(word) || ["hpgl", "plt", "plotter"].contains(word) {
                try select(.plotter)
                index += 1
                continue
            }
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
            if ["cebi", "cep"].contains(word), let action = peek(1), ["kaldir", "cikar", "sil", "ekle"].contains(action) {
                guard pocket == nil else { throw Problem("Tek cep seçeneği belirtin.") }
                pocket = action == "ekle"
                index += 2
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
                try select(requested)
            } else if !requestWords.contains(word) {
                throw Problem("Tarifteki “\(words[index])” ifadesi desteklenmiyor.")
            }
            index += 1
        }
        if model == nil, let previous {
            if width == nil, length == nil, pocket == nil {
                guard output != nil else { throw Problem("Değişecek ölçüyü, cebi veya çıktı biçimini yazın.") }
                return (previous, output)
            }
            guard previous.name == StudioPattern.apron.rawValue,
                  let body = previous.pieces.first(where: { $0.name == "Gövde" }) else {
                throw Problem("Bu ölçü ve cep düzeltmeleri önlük için kullanılabilir.")
            }
            var updated = try apron(width: width ?? body.maxX - body.minX,
                                    length: length ?? body.maxY - body.minY,
                                    pocket: pocket ?? previous.pieces.contains { $0.name == "Cep" })
            updated.id = previous.id
            for index in updated.pieces.indices {
                if let original = previous.pieces.first(where: { $0.name == updated.pieces[index].name }) {
                    updated.pieces[index].id = original.id
                }
            }
            return (updated, output)
        }
        guard let model else { throw Problem("Modeli belirtin: önlük, etek veya bez çanta.") }
        guard model == .apron else {
            guard width == nil, length == nil, pocket == nil else {
                throw Problem("Ölçü ve cep seçeneği şimdilik önlükte kullanılabilir.")
            }
            return (model.makeProject(), output)
        }
        return (try apron(width: width ?? 600, length: length ?? 810, pocket: pocket ?? true), output)
    }

    private static func apron(width: Double, length: Double, pocket: Bool) throws -> DraftProject {
        guard var project = StudioPattern.makeApron(width: width, length: length) else {
            throw Problem("Önlük eni 30–120 cm, boyu 35–150 cm aralığında olmalı.")
        }
        if pocket == false { project.pieces.removeAll { $0.name == "Cep" } }
        return project
    }
}
