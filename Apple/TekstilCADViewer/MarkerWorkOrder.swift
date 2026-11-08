import Foundation
#if canImport(UIKit)

struct MarkerFailure: LocalizedError {
    let tr: String
    let en: String
    var errorDescription: String? { tr }
    func message(english: Bool) -> String { english ? en : tr }
}

struct MarkerWorkOrder {
    var model: String?
    var sizes: [Int] = []
    var sets: Int?
    var sizeQuantities: [Int: Int] = [:]
    var material: String?
    var width: Double?
    var seconds: Int?
    var issues: [MarkerFailure] = []
    var complete: Bool { issues.isEmpty }
}

/// A deliberately bounded vocabulary. This interprets a request; it never approves a model.
enum MarkerWorkOrderParser {
    static let maximumTextLength = 12_000
    private static let number = #"-?\d+(?:[.,]\d+)?"#
    private static func matches(_ pattern: String, _ text: String) -> [NSTextCheckingResult] {
        guard let regex = try? NSRegularExpression(pattern: pattern) else { return [] }
        return regex.matches(in: text, range: NSRange(text.startIndex..., in: text))
    }
    private static func group(_ result: NSTextCheckingResult, _ index: Int, _ text: String) -> String {
        guard let range = Range(result.range(at: index), in: text) else { return "" }
        return String(text[range])
    }
    private static func replace(_ pattern: String, _ text: String, _ replacement: String) -> String {
        guard let regex = try? NSRegularExpression(pattern: pattern) else { return text }
        return regex.stringByReplacingMatches(in: text, range: NSRange(text.startIndex..., in: text), withTemplate: replacement)
    }
    private static func numeric(_ text: String) -> Double? { Double(text.replacingOccurrences(of: ",", with: ".")) }
    static func parse(_ source: String) -> MarkerWorkOrder {
        var order = MarkerWorkOrder()
        func issue(_ tr: String, _ en: String) { order.issues.append(.init(tr: tr, en: en)) }
        guard source.utf16.count <= maximumTextLength else {
            issue("İstek en fazla 12.000 karakter olabilir.", "Request is limited to 12,000 characters."); return order
        }
        var text = source.lowercased().folding(options: .diacriticInsensitive, locale: Locale(identifier: "tr_TR"))
            .replacingOccurrences(of: "ı", with: "i").replacingOccurrences(of: "ş", with: "s")
            .replacingOccurrences(of: "–", with: "-").replacingOccurrences(of: "—", with: "-")
            .replacingOccurrences(of: "’", with: "'")
        text = replace(#"\bfabric\s+width\b"#, text, "eni")
        text = replace(#"\bhalf\s+(?:a\s+)?minute\b"#, text, "0.5 dakika")
        text = replace(#"\byarim\s+dakika\b"#, text, "0.5 dakika")
        text = replace(#"\bone\s+set\s+(?:each|per\s+size)\b"#, text, "her bedenden 1")
        // Protect the explicitly labelled size field before interpreting decimal quantities.
        // In "sizes 36/38,2 sets per size", 38 belongs to the slash-delimited size
        // list. A fractional quantity such as "sizes 36/38,2,5 sets per size"
        // still stays 2,5 and is rejected below; it must never become 5 sets.
        text = replace(#"\b(sizes?\s*[:=]?\s*\d{2}(?:\s*/\s*\d{2})*)\s*,\s*("# + number + #")\s+sets?\s+(?:each|per\s+size)\b"#, text, "$1; her bedenden $2")
        text = replace("(" + number + #")\s+sets?\s+(?:each|per\s+size)\b"#, text, "her bedenden $1")
        text = replace(#"\beach\s+size\s+(\d+)\s+sets?\b"#, text, "her bedenden $1")
        text = replace(#"\bsize\s+(\d+)\s+(\d+)\s+sets?\b"#, text, "$1 beden $2 takim")
        let words = ["sizes":"beden", "size":"beden", "sets":"takim", "set":"takim", "interfacing":"tela", "lining":"astar", "fabric":"kumas", "width":"eni", "wide":"eninde", "seconds":"saniye", "second":"saniye", "secs":"sn", "minutes":"dakika", "minute":"dakika", "mins":"dk", "and":"ve", "centimeters":"cm", "centimetres":"cm", "millimeters":"mm", "millimetres":"mm", "meters":"m", "metres":"m"]
        for (from, to) in words { text = replace("\\b" + from + "\\b", text, to) }
        if !matches(#"\b(?:degil|haric|iptal|yerine|not|never|except|excluding|without|cancel|instead)\b"#, text).isEmpty {
            issue("Düzeltme/dışlama belirsiz; yalnız son istediğiniz değerleri yazın.", "Corrections/exclusions are ambiguous; write only the final requested values.")
        }
        func unique<T: Hashable>(_ values: [T], _ tr: String, _ en: String) -> T? {
            let values = Set(values)
            guard values.count == 1 else { issue("\(tr) eksik veya çelişkili.", "\(en) is missing or conflicting."); return nil }
            return values.first
        }
        let modelPattern = #"(?<![a-z0-9-])\d+\s*-\s*\d+\s*-\s*\d+(?![a-z0-9-])"#
        order.model = unique(matches(modelPattern, text).map { replace(#"\s"#, group($0, 0, text), "") }, "Model kodu", "Model code")
        text = replace(modelPattern, text, " ")
        let widthPattern = #"\b(?:en|eni|eninde)\s*[:=]?\s*("# + number + #")\s*(mm|cm|m|milimetre|santim|santimetre|metre)\b"#
        let reverseWidth = "(" + number + #")\s*(mm|cm|m|milimetre|santim|santimetre|metre)\s*(?:eninde|en|ende)\b"#
        var widths: [Double] = []
        for pattern in [widthPattern, reverseWidth] {
            for match in matches(pattern, text) {
                if let n = numeric(group(match, 1, text)) {
                    let u = group(match, 2, text)
                    widths.append(n * (["m", "metre"].contains(u) ? 1000 : ["mm", "milimetre"].contains(u) ? 1 : 10))
                }
            }
            text = replace(pattern, text, " ")
        }
        order.width = unique(widths, "Kumaş eni", "Fabric width")
        if let w = order.width, !w.isFinite || w <= 0 || w > 10_000 { issue("En 0–10.000 mm arasında olmalı.", "Width must be greater than zero and at most 10,000 mm.") }
        let timePattern = "(" + number + #")\s*(saniye|sn|dakika|dk)\b"#
        let times = matches(timePattern, text).compactMap { match -> Double? in
            guard let n = numeric(group(match, 1, text)) else { return nil }
            return n * (["dakika", "dk"].contains(group(match, 2, text)) ? 60 : 1)
        }
        if let t = unique(times, "Süre", "Time limit") {
            if t.isFinite && t >= 1 && t <= 3600 && t.rounded() == t { order.seconds = Int(t) }
            else { issue("Süre 1–3.600 tam saniye olmalı.", "Time limit must be 1–3,600 whole seconds.") }
        }
        text = replace(timePattern, text, " ")
        let materials = matches(#"\b(tela(?:lari|lar|nin|si)?|astar|kumas)\b"#, text).map { match -> String in
            let value = group(match, 1, text)
            return value.hasPrefix("tela") ? "Tela" : value == "astar" ? "Astar" : "Kumaş"
        }
        order.material = unique(materials, "Malzeme", "Material")
        let individual = #"(?:^|[,;]|\bve\b)\s*("# + number + #")\s*beden\s+("# + number + #")\s*(?:adet|takim)\b"#
        for match in matches(individual, text) {
            guard let s = numeric(group(match, 1, text)), let q = numeric(group(match, 2, text)), s >= 30, s <= 64, s.rounded() == s, q >= 1, q <= 1000, q.rounded() == q else {
                issue("Beden 30–64, takım adedi 1–1.000 tam sayı olmalı.", "Size must be a whole number from 30–64; sets from 1–1,000."); continue
            }
            if order.sizeQuantities[Int(s)] != nil { issue("Aynı beden için tekrarlı adet var.", "A size has repeated quantities.") }
            order.sizeQuantities[Int(s)] = Int(q)
        }
        text = replace(individual, text, "; ")
        text = replace(#"\bbirer\b"#, text, "her bedenden 1")
        text = replace("(" + number + #")\s*'?\s*er\b"#, text, "her bedenden $1")
        text = replace(#"\bbeden(?:lerden|den)?\s+("# + number + #")\s*(?:adet|takim)\b"#, text, "beden her bedenden $1")
        let quantityPattern = #"\bher\s+bedenden\s+("# + number + #")(?![0-9.]|,\d)"#
        let quantities = matches(quantityPattern, text).compactMap { numeric(group($0, 1, text)) }
        if !order.sizeQuantities.isEmpty && !quantities.isEmpty { issue("Genel ve beden bazlı adet birlikte kullanılamaz.", "Use either per-size quantities or a common quantity, not both.") }
        if order.sizeQuantities.isEmpty || !quantities.isEmpty, let q = unique(quantities, "Takım adedi", "Set quantity") {
            if q >= 1 && q <= 1000 && q.rounded() == q { order.sets = Int(q) }
            else { issue("Takım adedi 1–1.000 tam sayı olmalı.", "Set quantity must be a whole number from 1–1,000.") }
        }
        text = replace(quantityPattern, text, " ")
        // A comma followed by a field separator is punctuation; a decimal suffix is not.
        text = replace(#"\bbeden(?:ler|leri)?\s*[:=]?\s*(\d{2}(?:\s*[/,]\s*\d{2})*)(?![0-9.]|,\d)"#, text, "$1 beden")
        let sizePattern = #"(?<![0-9.-])(?<![0-9],)(\d{2}(?:\s*(?:[/,]|\s)\s*\d{2})*)\s*beden\b"#
        var lists: [[Int]] = []
        for match in matches(sizePattern, text) {
            let raw = group(match, 1, text)
            let list = matches(#"\d+"#, raw).compactMap { Int(group($0, 0, raw)) }.sorted()
            if list.contains(where: { $0 < 30 || $0 > 64 }) || Set(list).count != list.count { issue("Beden listesi geçersiz veya tekrarlı.", "Size list is invalid or repeated.") }
            lists.append(list)
        }
        order.sizes = lists.first ?? order.sizeQuantities.keys.sorted()
        if order.sizes.isEmpty || lists.contains(where: { $0 != order.sizes }) || (!order.sizeQuantities.isEmpty && order.sizes != order.sizeQuantities.keys.sorted()) {
            issue("Beden listesi eksik veya çelişkili; 36/38/40 beden biçimini kullanın.", "Sizes are missing or conflicting; use the form 36/38/40 sizes.")
        }
        text = replace(sizePattern, text, " ")
        // Unconsumed digits indicate an unsupported/partial field; never silently accept 38.5 as 38.
        if !matches(#"\d"#, text).isEmpty { issue("Çözümlenemeyen sayı var; örnek biçimi kullanın.", "An unrecognized number remains; use the example format.") }
        return order
    }
}

enum MarkerRequestStorage {
    static func data(source: String) throws -> Data {
        guard !source.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty, source.utf16.count <= MarkerWorkOrderParser.maximumTextLength else {
            throw MarkerFailure(tr: "İş emri boş veya çok uzun.", en: "Request is empty or too long.")
        }
        let data = try JSONSerialization.data(withJSONObject: ["format": "tekstilcad.marker-request", "version": 1, "source": source], options: [.sortedKeys])
        guard data.count <= 131_072 else { throw MarkerFailure(tr: "İş emri dosyası çok büyük.", en: "Request file is too large.") }
        return data
    }
}

struct MarkerPlacement {
    let piece: Drawing.Piece
    let copy: Int
    let dx: Double
    let dy: Double
    let bounds: CGRect
}
struct MarkerLayout {
    let sourceName: String
    let order: MarkerWorkOrder
    let placements: [MarkerPlacement]
    let width: Double
    let length: Double
}

enum MarkerSourceValidation {
    /// Inspect the original bytes because the preview intentionally omits unknown v5 fields.
    static func validate(_ data: Data, drawing: Drawing, cancelled: @escaping () -> Bool) throws {
        func reject() -> MarkerFailure { .init(tr: "Kaynak alanları/önizleme eşleşmiyor veya desteklenmeyen geçmiş/işaret içeriyor.", en: "Source fields do not match the preview or include unsupported history/annotations.") }
        guard data.count <= 8_388_608 else { throw reject() }
        let reread = try Drawing.load(data, cancelled: cancelled)
        guard reread.formatVersion == drawing.formatVersion, reread.name == drawing.name,
              reread.pieces.count == drawing.pieces.count else { throw reject() }
        for (a, b) in zip(reread.pieces, drawing.pieces) {
            if cancelled() { throw CancellationError() }
            guard a.id == b.id, a.name == b.name, a.closed == b.closed, a.role == b.role,
                  a.size == b.size, a.material == b.material, a.quantity == b.quantity,
                  a.notches == b.notches, a.grainStatus == b.grainStatus, a.points.count == b.points.count,
                  a.grain?.start.x == b.grain?.start.x, a.grain?.start.y == b.grain?.start.y,
                  a.grain?.end.x == b.grain?.end.x, a.grain?.end.y == b.grain?.end.y,
                  a.grain?.needsReview == b.grain?.needsReview,
                  zip(a.points, b.points).allSatisfy({ pair in pair.0.x == pair.1.x && pair.0.y == pair.1.y }) else { throw reject() }
        }
        guard let envelope = try JSONSerialization.jsonObject(with: data) as? [Any], envelope.count == 2,
              let root = envelope[1] as? [String: Any],
              Set(root.keys).isSubset(of: ["Name", "Pieces", "GradingRules"]),
              let pieces = root["Pieces"] as? [[String: Any]] else { throw reject() }
        if let rules = root["GradingRules"] {
            guard (rules as? [Any])?.isEmpty == true || (rules as? [String: Any])?.isEmpty == true else { throw reject() }
        }
        for piece in pieces {
            if cancelled() { throw CancellationError() }
            guard Set(piece.keys).isSubset(of: ["Id", "Name", "Points", "Closed", "Metadata", "Grainline", "Notches"]),
                  let points = piece["Points"] as? [[String: Any]],
                  points.allSatisfy({ Set($0.keys).isSubset(of: ["X", "Y", "IsFinite"]) }) else { throw reject() }
            if let metadata = piece["Metadata"] as? [String: Any], !Set(metadata.keys).isSubset(of: ["Role", "Size", "Material", "Quantity"]) { throw reject() }
            if let grain = piece["Grainline"] as? [String: Any] {
                guard Set(grain.keys).isSubset(of: ["Start", "End", "NeedsReview"]) else { throw reject() }
                for field in ["Start", "End"] {
                    if let point = grain[field] as? [String: Any], !Set(point.keys).isSubset(of: ["X", "Y", "IsFinite"]) { throw reject() }
                }
            }
        }
    }
}

enum MarkerLayoutEngine {
    static func calculate(drawing: Drawing?, sourceData: Data? = nil, order: MarkerWorkOrder, confirmed: Bool, cancelled: @escaping () -> Bool = { false }) throws -> MarkerLayout {
        func fail(_ tr: String, _ en: String) -> MarkerFailure { .init(tr: tr, en: en) }
        guard let drawing else { throw fail("Önce gerçek .tcad modeli açın; istek tek başına kalıp oluşturmaz.", "Open a real .tcad model first; a request alone cannot create patterns.") }
        guard confirmed else { throw fail("Açık modelin istenen modele ait olduğunu onaylayın.", "Confirm that the loaded model matches the requested model.") }
        guard order.complete, order.model != nil, let material = order.material, ["Tela", "Astar", "Kumaş"].contains(material), let width = order.width, width.isFinite, width > 0, width <= 10_000, let seconds = order.seconds, (1...3600).contains(seconds), !order.sizes.isEmpty, Set(order.sizes).count == order.sizes.count, order.sizes.allSatisfy({ (30...64).contains($0) }) else { throw fail("İş emri eksik/geçersiz.", "The work order is incomplete or invalid.") }
        if order.sizeQuantities.isEmpty {
            guard let sets = order.sets, (1...1000).contains(sets) else { throw fail("Takım adedi geçersiz.", "Invalid set quantity.") }
        } else {
            guard order.sets == nil, order.sizeQuantities.keys.sorted() == order.sizes.sorted(), order.sizeQuantities.values.allSatisfy({ (1...1000).contains($0) }) else { throw fail("Beden adetleri geçersiz.", "Invalid per-size quantities.") }
        }
        guard [5, 6].contains(drawing.formatVersion), !drawing.pieces.isEmpty else { throw fail("Bu kaynak sürümünde tüm alanlar doğrulanamıyor; v5/v6 kaynak kullanın.", "All source fields cannot be verified for this version; use a v5/v6 source.") }
        guard drawing.name.utf16.count <= 1024, drawing.pieces.allSatisfy({ ($0.name?.utf16.count ?? 0) <= 1024 }) else { throw fail("Model/parça adı 1.024 karakter sınırını aşıyor.", "A model/piece name exceeds 1,024 characters.") }
        let ids = drawing.pieces.compactMap(\.id)
        guard ids.count == drawing.pieces.count, Set(ids).count == ids.count, !ids.contains(UUID(uuidString: "00000000-0000-0000-0000-000000000000")!) else { throw fail("Parça kimlikleri eksik veya tekrarlı.", "Piece IDs are missing or duplicated.") }
        let deadline = ProcessInfo.processInfo.systemUptime + Double(seconds)
        func check() throws {
            if cancelled() { throw fail("İşlem iptal edildi; sonuç üretilmedi.", "Cancelled; no result was produced.") }
            if ProcessInfo.processInfo.systemUptime >= deadline { throw fail("Süre sınırı doldu; sonuç üretilmedi.", "Time limit reached; no result was produced.") }
        }
        if let sourceData {
            do { try MarkerSourceValidation.validate(sourceData, drawing: drawing, cancelled: { cancelled() || ProcessInfo.processInfo.systemUptime >= deadline }) }
            catch { try check(); throw error }
        } else if drawing.formatVersion == 5 {
            throw fail("v5 alanlarını doğrulamak için özgün kaynak dosyasını yeniden açın.", "Reopen the original source file to verify v5 fields.")
        }
        var selected: [(Drawing.Piece, Int)] = []
        var copies = 0, points = 0, work = 0
        for piece in drawing.pieces {
            try check()
            let size = piece.size?.trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
            let mat = piece.material?.trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
            let otherSize = !size.isEmpty && !order.sizes.map(String.init).contains(size)
            let otherMaterial = !mat.isEmpty && mat.caseInsensitiveCompare(material) != .orderedSame
            if !otherSize && !otherMaterial && (size.isEmpty || mat.isEmpty) { throw fail("Takıma ait olabilecek parçanın beden/malzeme bilgisi eksik.", "A piece that may belong to the set has missing size/material metadata.") }
        }
        for size in order.sizes {
            let candidates = drawing.pieces.filter { $0.size?.trimmingCharacters(in: .whitespacesAndNewlines) == String(size) && $0.material?.trimmingCharacters(in: .whitespacesAndNewlines).caseInsensitiveCompare(material) == .orderedSame }
            guard !candidates.isEmpty else { throw fail("\(size) beden için \(material) parçası yok.", "No \(material) pieces for size \(size).") }
            for piece in candidates {
                try check()
                guard piece.size == String(size), piece.material == material, piece.role == "Kesim", !piece.roleUnreadable, !piece.metadataUnreadable, !piece.metadataEmpty, !piece.textMetadataUnreadable, !piece.quantityUnreadable, !piece.experimentalALine, let quantity = piece.quantity, (1...100_000).contains(quantity) else { throw fail("Parça bilgileri tam ve kesin olmalı: Kesim, beden, malzeme ve kesim adedi.", "Pieces need exact cut role, size, material, and cut quantity metadata.") }
                let sets = order.sizeQuantities[size] ?? order.sets!
                let count = quantity * sets
                copies += count
                guard piece.points.count <= 2048 else { throw fail("Parça nokta sınırı 2.048.", "A piece is limited to 2,048 points.") }
                points += count * piece.points.count
                work += piece.points.count * piece.points.count
                guard copies <= 500, points <= 100_000, work <= 4_000_000 else { throw fail("500 kopya veya nokta/kenar iş sınırı aşıldı.", "The 500-copy or point/edge work limit was exceeded.") }
                guard piece.closed, piece.grainStatus != "İplik yönü: Okunamadı" else { throw fail("Kapalı kontur ve okunabilir işaretler gerekli.", "Closed contours and readable annotations are required.") }
                do { try ExperimentalContourValidation.validate(piece.points.map { .init(x: $0.x, y: $0.y) }, closed: true) }
                catch { throw fail("Kontur bozuk, kesişiyor veya sonlu değil.", "A contour is invalid, self-intersecting, or non-finite.") }
                try check()
                guard piece.notches.allSatisfy({ piece.points.indices.contains($0) }) else { throw fail("Köşe işaretleri geçersiz.", "Invalid notch annotations.") }
                if let g = piece.grain {
                    guard [g.start.x, g.start.y, g.end.x, g.end.y].allSatisfy(\.isFinite), hypot(g.end.x-g.start.x, g.end.y-g.start.y) > 0.001 else { throw fail("İplik yönü gösterilemiyor.", "The grainline cannot be represented reliably.") }
                }
                selected.append((piece, count))
            }
        }
        var placements: [MarkerPlacement] = []
        var x = 0.0, y = 0.0, shelf = 0.0
        for (piece, count) in selected {
            var all = piece.points
            if let grain = piece.grain { all += [grain.start, grain.end] }
            let minX = all.map(\.x).min()! - 2, minY = all.map(\.y).min()! - 2
            let w = all.map(\.x).max()! - minX + 2, h = all.map(\.y).max()! - minY + 2
            guard w.isFinite, h.isFinite, w <= width, h <= 10_000 else { throw fail("Parça/işaret sınırları kumaşa sığmıyor.", "Piece/annotation bounds do not fit the fabric.") }
            for copy in 1...count {
                try check()
                if x + w > width { x = 0; y += shelf; shelf = 0 }
                guard y + h <= 10_000 else { throw fail("Yerleşim boyu 10.000 mm sınırını aşıyor.", "Layout exceeds the 10,000 mm length limit.") }
                placements.append(.init(piece: piece, copy: copy, dx: x-minX, dy: y-minY, bounds: CGRect(x: x, y: y, width: w, height: h)))
                x += w; shelf = max(shelf, h)
            }
        }
        try check()
        return MarkerLayout(sourceName: drawing.name, order: order, placements: placements, width: width, length: y+shelf)
    }
}
#endif
