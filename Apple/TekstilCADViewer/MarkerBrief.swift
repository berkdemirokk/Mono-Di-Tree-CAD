import Foundation

/// Bounded, deterministic Turkish marker request parser. Placement remains in MarkerLayout.
enum MarkerBrief {
    struct Request: Equatable {
        let sourceText: String
        let widthMM: Double
        let gapMM: Double
        let marginMM: Double
        let quantities: [UUID: Int]
        let output: PatternBrief.Output?
    }

    private struct Problem: LocalizedError {
        let message: String
        var errorDescription: String? { message }
    }

    static func parse(_ text: String, source: DraftProject) throws -> Request? {
        guard text.utf16.count <= 512 else { throw Problem(message: "Tarifi 512 karakter içinde yazın.") }
        let folded = TurkishText.fold(TurkishText.normalize(text))
            .replacingOccurrences(of: #"(?<=[0-9])[,](?=[0-9])"#, with: ".", options: .regularExpression)
            .replacingOccurrences(of: ",", with: " , ")
        let words = folded.split { $0.isWhitespace || ";:".contains($0) }.map(String.init)
        guard words.contains("yerlestir") else { return nil }

        let marker = words.firstIndex(of: "yerlestir")!
        guard words.lastIndex(of: "yerlestir") == marker,
              let fabric = words.firstIndex(of: "kumasa"), fabric >= 2, fabric < marker,
              let width = Double(words[fabric - 2]), width.isFinite, width > 0,
              ["cm", "santim", "santimetre", "mm", "milimetre"].contains(words[fabric - 1]) else {
            throw Problem(message: "Kumaş enini “150 cm kumaşa” biçiminde, birimiyle yazın.")
        }
        let prefix = Array(words[..<(fabric - 2)])
        guard prefix.isEmpty || prefix == ["onluk", "icin"] || prefix == ["onlugu", "icin"] ||
                prefix == ["etek", "icin"] || prefix == ["etegi", "icin"] ||
                prefix == ["bez", "canta", "icin"] || prefix == ["bez", "cantasi", "icin"] else {
            throw Problem(message: "Kumaş eninden önce yalnızca “önlük için”, “etek için” veya “bez çanta için” yazın.")
        }
        let widthMM = width * (["cm", "santim", "santimetre"].contains(words[fabric - 1]) ? 10 : 1)
        guard widthMM.isFinite, widthMM > 0, widthMM <= 100_000 else {
            throw Problem(message: "Kumaş eni geçerli aralıkta olmalı.")
        }

        var gapMM = 10.0, marginMM = 10.0
        var end = marker
        var output: PatternBrief.Output?
        if marker + 1 < words.count {
            guard Array(words[(marker + 1)...]) == ["ve", "plotter", "dosyasi", "hazirla"] ||
                    Array(words[(marker + 1)...]) == ["ve", "plt", "dosyasi", "hazirla"] else {
                throw Problem(message: "Yerleşimden sonra yalnızca “ve plotter dosyası hazırla” isteği destekleniyor.")
            }
            output = .plotter
        }

        // Optional explicit spacing clauses immediately before the placement verb.
        var requestEnd = end
        if requestEnd >= 4, words[requestEnd - 1] == "aralik", words[requestEnd - 2] == "mm",
           let value = Double(words[requestEnd - 3]), value.isFinite, value >= 0 {
            gapMM = value
            requestEnd -= 3
        }
        if requestEnd >= 4, words[requestEnd - 1] == "payi", words[requestEnd - 2] == "kenar",
           words[requestEnd - 3] == "mm", let value = Double(words[requestEnd - 4]), value.isFinite, value >= 0 {
            marginMM = value
            requestEnd -= 4
        }
        end = requestEnd
        guard gapMM <= 100_000, marginMM <= 100_000 else { throw Problem(message: "Aralık ve kenar payı geçerli aralıkta olmalı.") }

        let counts: [String: Int] = ["bir": 1, "iki": 2, "uc": 3, "dort": 4, "bes": 5]
        let normalizedNames = source.pieces.map {
            TurkishText.fold(TurkishText.normalize($0.name)).split(separator: " ").map(String.init)
        }
        let aliases: [(String, (String) -> Bool)] = [
            ("on", { $0 == "on" || $0.hasPrefix("on ") }),
            ("arka", { $0 == "arka" || $0.hasPrefix("arka ") }),
            ("govde", { $0.contains("govde") }),
            ("cep", { $0 == "cep" || $0.hasPrefix("cep ") })
        ]
        var quantities: [UUID: Int] = [:]
        var consumed = Set<Int>()
        var cursor = fabric + 1
        while cursor < end {
            guard let quantity = Int(words[cursor]) ?? counts[words[cursor]], quantity > 0, quantity <= 100 else {
                throw Problem(message: "Her parça için 1–100 arasında adet yazın.")
            }
            let quantityIndex = cursor
            cursor += 1
            if cursor < end, words[cursor] == "adet" { cursor += 1 }
            guard cursor < end else { throw Problem(message: "Adetten sonra parça adını yazın.") }

            let exact = source.pieces.indices.filter { index in
                let name = normalizedNames[index]
                return !name.isEmpty && cursor + name.count <= end && Array(words[cursor..<(cursor + name.count)]) == name
            }
            let chosen: [Int]
            let matchedNameCount: Int
            if !exact.isEmpty {
                let longest = exact.map { normalizedNames[$0].count }.max()!
                chosen = exact.filter { normalizedNames[$0].count == longest }
                matchedNameCount = longest
            } else if let alias = aliases.first(where: { $0.0 == words[cursor] }) {
                chosen = source.pieces.indices.filter { alias.1(TurkishText.fold(TurkishText.normalize(source.pieces[$0].name))) }
                matchedNameCount = 1
            } else {
                throw Problem(message: "“\(words[cursor])” adlı parça kaynak kalıpta yok.")
            }
            guard chosen.count == 1 else { throw Problem(message: "Parça adı belirsiz; kaynak kalıptaki tam adını yazın.") }
            let pieceIndex = chosen[0]
            let piece = source.pieces[pieceIndex]
            guard quantities[piece.id] == nil else { throw Problem(message: "Aynı parça için adet bilgisini bir kez yazın.") }
            quantities[piece.id] = quantity
            consumed.insert(quantityIndex)
            if cursor > quantityIndex + 1 { consumed.insert(quantityIndex + 1) }
            consumed.formUnion(cursor..<(cursor + matchedNameCount))
            cursor += matchedNameCount
            if cursor < end {
                guard words[cursor] == "ve" || words[cursor] == "," else {
                    throw Problem(message: "Parça adetlerini virgül veya “ve” ile ayırın.")
                }
                cursor += 1
                guard cursor < end else { throw Problem(message: "“ve” sonrasında parça adedi eksik.") }
            }
        }
        guard !quantities.isEmpty else { throw Problem(message: "Yerleştirilecek parça ve adetleri belirtin.") }
        return Request(sourceText: text, widthMM: widthMM, gapMM: gapMM, marginMM: marginMM,
                       quantities: quantities, output: output)
    }
}
