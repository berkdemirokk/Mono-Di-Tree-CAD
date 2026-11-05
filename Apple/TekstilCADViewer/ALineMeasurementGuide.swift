import Foundation
import SwiftUI

/// User-entered values for the desktop's experimental waist-down panel only.
enum ALineMeasurementGuide {
    static let labels = ["Ön bel yayı", "Arka bel yayı", "Ön kalça yayı", "Arka kalça yayı",
                         "Ön bel yayı bolluğu", "Arka bel yayı bolluğu", "Ön kalça yayı bolluğu", "Arka kalça yayı bolluğu",
                         "Belden kalçaya düşüklük", "Belden etek boyu", "Bitmiş etek çevresi (bolluk dahil)"]
    enum Failure: Error { case incomplete, invalid(Int), existing, uncertain, conflict, tooLong }
    struct Input: Equatable { var values = Array(repeating: "", count: 11); var unit = "" }
    struct Request: Identifiable {
        let id = UUID()
        let draft: ExperimentalDesignBrief.Draft
        let explicitSelection: Bool
        let importedPortable: Bool
        let version: Int
        let revision: UUID
        func matches(_ current: ExperimentalDesignBrief.Draft, explicitSelection: Bool, importedPortable: Bool,
                     version: Int, revision: UUID, activeID: UUID?, pending: Bool) -> Bool {
            id == activeID && !pending && draft == current && self.explicitSelection == explicitSelection
                && self.importedPortable == importedPortable && self.version == version && self.revision == revision
        }
    }
    private static func fold(_ text: String) -> String {
        text.precomposedStringWithCanonicalMapping.replacingOccurrences(of: "İ", with: "i").lowercased().replacingOccurrences(of: "ı", with: "i")
    }
    private static func number(_ raw: String, unit: String, zeroAllowed: Bool) -> Decimal? {
        let value = raw.trimmingCharacters(in: .whitespacesAndNewlines)
        guard value.range(of: #"\A[0-9]{1,5}(?:[.,][0-9]{1,8})?\z"#, options: .regularExpression) != nil,
              value.range(of: #"\A[0-9]+[.,][0-9]{3}\z"#, options: .regularExpression) == nil,
              let parsed = Decimal(string: value.replacingOccurrences(of: ",", with: "."), locale: Locale(identifier: "en_US_POSIX")) else { return nil }
        let millimeters = unit == "cm" ? parsed * 10 : parsed
        guard millimeters <= 10_000, zeroAllowed ? millimeters >= 0 : millimeters > 0 else { return nil }
        return millimeters
    }
    static func prepare(_ input: Input, draft: ExperimentalDesignBrief.Draft) throws -> String {
        guard draft.style == .aline else { throw Failure.uncertain }
        guard input.values.count == labels.count, input.unit == "cm" || input.unit == "mm" else { throw Failure.incomplete }
        guard input.values.allSatisfy({ !$0.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty }) else { throw Failure.incomplete }
        var mm: [Decimal] = []
        for (i, value) in input.values.enumerated() {
            guard let parsed = number(value, unit: input.unit, zeroAllowed: (4...7).contains(i)) else { throw Failure.invalid(i) }
            mm.append(parsed)
        }
        let sources = [draft.description, draft.otherMeasurements]
        guard sources.allSatisfy({ $0.utf16.count <= ExperimentalDesignBrief.maxTextUnits }) else { throw Failure.tooLong }
        let guarded = #"(?<![\p{L}\p{M}\p{N}_])(?:eğer|şayet|gerekirse|koşuluyla|şartiyla|takdirde|ise|olursa|olma[\p{L}\p{M}]*|değil[\p{L}\p{M}]*|bunlar[\p{L}\p{M}]*|şunlar[\p{L}\p{M}]*|onlar(?:i|in|a|da|dan|la)?|bunun[\p{L}\p{M}]*|şunun[\p{L}\p{M}]*|onun(?:la|ki)?|bunu|buna|bunda|bundan|şunu|şuna|şunda|şundan|onu|ona|onda|ondan|hepsi[\p{L}\p{M}]*|tümü[\p{L}\p{M}]*|ölçü[\p{L}\p{M}]*)(?![\p{L}\p{M}\p{N}_])|[?？]"#
        guard sources.allSatisfy({ fold($0).range(of: guarded, options: .regularExpression) == nil }) else { throw Failure.uncertain }
        for source in sources {
            let normalized = fold(source)
            for label in labels {
                let labelPattern = fold(label).split(separator: " ").map { NSRegularExpression.escapedPattern(for: String($0)) }.joined(separator: #"[ \t]+"#)
                let pattern = #"(?<![\p{L}\p{M}\p{N}_])"# + labelPattern + #"(?![\p{L}\p{M}\p{N}_])"#
                if normalized.range(of: pattern, options: .regularExpression) != nil { throw Failure.existing }
            }
        }
        var totals: [String: Decimal] = [:]
        func record(_ label: String, value: String) throws {
            let proposal = try MeasurementMessageParser.parse(label + ": " + value, existing: [:])
            guard let raw = proposal.values[label], let value = MeasurementMessageParser.millimeters(raw) else { throw Failure.uncertain }
            if let prior = totals[label], prior != value { throw Failure.conflict }
            totals[label] = value
        }
        if !draft.waist.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty { try record("bel çevresi", value: draft.waist) }
        if !draft.hips.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty { try record("kalça çevresi", value: draft.hips) }
        for source in sources {
            for segment in source.components(separatedBy: CharacterSet.newlines.union(CharacterSet(charactersIn: ";"))) {
                let text = fold(segment)
                guard text.contains("bel çevresi") || text.contains("kalça çevresi") else { continue }
                guard let proposal = try? MeasurementMessageParser.parse(segment, existing: [:]) else { throw Failure.uncertain }
                for label in ["bel çevresi", "kalça çevresi"] {
                    if let raw = proposal.values[label] { try record(label, value: raw) }
                }
            }
        }
        if let waist = totals["bel çevresi"], mm[0] + mm[1] != waist { throw Failure.conflict }
        if let hips = totals["kalça çevresi"], mm[2] + mm[3] != hips { throw Failure.conflict }
        let lines = zip(labels, input.values).map { "\($0.0): \($0.1.trimmingCharacters(in: .whitespacesAndNewlines)) \(input.unit)" }.joined(separator: "\n")
        let previous = draft.otherMeasurements
        let updated = previous + (previous.isEmpty || previous.hasSuffix("\n") ? "" : "\n") + lines
        let whole = ExperimentalDesignBrief.measurements(bust: draft.bust, waist: draft.waist, hips: draft.hips, length: draft.length, other: updated)
        guard whole.utf16.count <= ExperimentalDesignBrief.maxTextUnits else { throw Failure.tooLong }
        return updated
    }
}

struct ALineMeasurementGuideView: View {
    let request: ALineMeasurementGuide.Request
    @Binding var input: ALineMeasurementGuide.Input
    let apply: (ALineMeasurementGuide.Request, ALineMeasurementGuide.Input, String) -> String?
    @Environment(\.dismiss) private var dismiss
    @State private var revision = UUID()
    @State private var pendingInput: ALineMeasurementGuide.Input?
    @State private var pendingNotes: String?
    @State private var pendingRevision: UUID?
    @State private var confirming = false
    @State private var status = ""
    var body: some View {
        NavigationStack {
            Form {
                Section {
                    Text("A kesim bel-altı önizlemesi için ön ve arka yayları beden üzerinde ayrı ölçün; yaylara bolluk eklemeyin. Her bolluğu ayrı girin. Etek çevresi bitmiş giysi ölçüsüdür ve bolluk dahildir. Bu rehber kalıp veya üretim onayı değildir.")
                    Text("Eksik değerler hesaplanmaz; sıfır yalnız bollukta kabul edilir. Sağ/sol asimetri bu önizlemede desteklenmez. Girişler bu ekran açık kaldığı sürece korunur.").font(.caption)
                    Picker("Tüm değerlerin birimi", selection: Binding(get: { input.unit }, set: { if input.unit != $0 { input.unit = $0; revision = UUID() } })) {
                        Text("Birim seçin").tag(""); Text("cm").tag("cm"); Text("mm").tag("mm")
                    }
                    Text("Birim değiştirmek sayıları dönüştürmez; tüm değerleri yeniden kontrol edin.").font(.caption)
                }
                Section("11 ayrı değer") {
                    ForEach(ALineMeasurementGuide.labels.indices, id: \.self) { index in
                        VStack(alignment: .leading) {
                            Text(ALineMeasurementGuide.labels[index])
                            TextField("Değeri girin", text: Binding(get: { input.values[index] }, set: { if input.values[index] != $0 { input.values[index] = $0; revision = UUID() } }))
                                .keyboardType(.decimalPad).accessibilityLabel(ALineMeasurementGuide.labels[index] + ", " + (input.unit.isEmpty ? "birim seçilmedi" : input.unit))
                        }
                    }
                }
                if !status.isEmpty { Text(status).foregroundStyle(.secondary) }
                Button("11 değeri gözden geçir") {
                    do {
                        pendingNotes = try ALineMeasurementGuide.prepare(input, draft: request.draft)
                        pendingInput = input; pendingRevision = revision; status = ""; confirming = true
                    } catch { status = "Değerler eklenemedi. 11 alanı ve birimi kontrol edin; mevcut ölçü satırları veya çelişkiler varsa önce bunları inceleyin. Girişler korunuyor." }
                }
            }
            .navigationTitle("A kesim ölçüleri")
            .interactiveDismissDisabled(true)
            .toolbar { ToolbarItem(placement: .cancellationAction) { Button("Vazgeç") { dismiss() } } }
            .confirmationDialog("Bu ölçüler isteğe eklensin mi?", isPresented: $confirming, titleVisibility: .visible) {
                Button("Ölçüleri notlara ekle") {
                    guard let saved = pendingInput, let notes = pendingNotes, pendingRevision == revision, saved == input else {
                        status = "Girişler değişti; hiçbir değer eklenmedi. Yeniden gözden geçirin."; return
                    }
                    if let issue = apply(request, saved, notes) { status = issue }
                    else { dismiss() }
                }
                Button("Düzenlemeye devam et", role: .cancel) { pendingInput = nil; pendingNotes = nil; pendingRevision = nil }
            } message: {
                if let saved = pendingInput { Text(zip(ALineMeasurementGuide.labels, saved.values).map { "\($0.0): \($0.1) \(saved.unit)" }.joined(separator: "\n")) }
                Text("Değerler sizin girişlerinizdir. Sağ/sol simetri varsayılır; asimetri desteklenmez. Mevcut tarif ve ölçüler korunur; bu işlem kalıp veya üretim onayı değildir.")
            }
        }
    }
}
