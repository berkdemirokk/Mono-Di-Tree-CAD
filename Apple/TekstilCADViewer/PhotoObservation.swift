import Foundation
import CoreGraphics
import ImageIO
import SwiftUI

/// Explicit user observations only. No analysis, dimensions or pattern are inferred.
enum PhotoObservation {
    enum Silhouette: String, CaseIterable, Identifiable {
        case unknown = "Bilinmiyor / görünmüyor", aline = "A kesim", straight = "Düz", fitted = "Bedene oturan"
        var id: String { rawValue }
    }
    enum Sleeve: String, CaseIterable, Identifiable {
        case unknown = "Bilinmiyor / görünmüyor", sleeveless = "Kolsuz", short = "Kısa kollu", long = "Uzun kollu"
        var id: String { rawValue }
    }
    enum Neck: String, CaseIterable, Identifiable {
        case unknown = "Bilinmiyor / görünmüyor", round = "Yuvarlak yaka", vee = "V yaka", collar = "Yakalı"
        var id: String { rawValue }
    }
    struct Selection: Equatable {
        var silhouette: Silhouette = .unknown
        var sleeve: Sleeve = .unknown
        var neck: Neck = .unknown
        var lines: [String] {
            var result: [String] = []
            if silhouette != .unknown { result.append("Fotoğraf gözlemi · Siluet: \(silhouette.rawValue)") }
            if sleeve != .unknown { result.append("Fotoğraf gözlemi · Kol: \(sleeve.rawValue)") }
            if neck != .unknown { result.append("Fotoğraf gözlemi · Yaka: \(neck.rawValue)") }
            return result
        }
    }
    enum Failure: Error { case empty, duplicate, tooLong, invalidImage, missing, malformed, unchanged }
    enum Category: String, CaseIterable, Identifiable {
        case silhouette = "Siluet", sleeve = "Kol", neck = "Yaka"
        var id: String { rawValue }
        var prefix: String { "Fotoğraf gözlemi · \(rawValue):" }
        var validLines: [String] {
            let values: [String]
            switch self {
            case .silhouette: values = Silhouette.allCases.filter { $0 != .unknown }.map(\.rawValue)
            case .sleeve: values = Sleeve.allCases.filter { $0 != .unknown }.map(\.rawValue)
            case .neck: values = Neck.allCases.filter { $0 != .unknown }.map(\.rawValue)
            }
            return values.map { prefix + " " + $0 }
        }
        func selectedLine(_ selection: Selection) -> String? {
            switch self {
            case .silhouette: return selection.silhouette == .unknown ? nil : prefix + " " + selection.silhouette.rawValue
            case .sleeve: return selection.sleeve == .unknown ? nil : prefix + " " + selection.sleeve.rawValue
            case .neck: return selection.neck == .unknown ? nil : prefix + " " + selection.neck.rawValue
            }
        }
    }
    enum EditAction: Equatable { case replace, remove }
    struct EditPlan: Equatable {
        let category: Category
        let action: EditAction
        let selection: Selection
        let before: String
        let after: String?
        let updatedDescription: String
    }
    struct ChoiceState {
        private(set) var selection = Selection()
        private(set) var category = Category.sleeve
        private(set) var revision = UUID()
        init() { }
        mutating func select(_ value: Selection) {
            guard value != selection else { return }
            selection = value; revision = UUID()
        }
        mutating func selectCategory(_ value: Category) {
            guard value != category else { return }
            category = value; revision = UUID()
        }
    }
    struct EditReview {
        let plan: EditPlan
        let revision: UUID
        func matches(_ current: ChoiceState, action: EditAction, confirmedSelection: Selection?, confirmedRevision: UUID?) -> Bool {
            guard revision == current.revision, plan.action == action, plan.category == current.category,
                  plan.selection == current.selection else { return false }
            return action != .replace || (confirmedSelection == current.selection && confirmedRevision == current.revision)
        }
    }
    /// Only one exact, complete application-generated line in the description
    /// can be changed. Other bytes, including line separators, stay untouched.
    static func editPlan(_ action: EditAction, category: Category, selection: Selection, description: String, notes: String) throws -> EditPlan {
        guard description.utf16.count <= ExperimentalDesignBrief.maxTextUnits, notes.utf16.count <= ExperimentalDesignBrief.maxTextUnits else { throw Failure.tooLong }
        let locale = Locale(identifier: "tr_TR")
        func mentionsTarget(_ text: String) -> Bool {
            let folded = text.folding(options: [.caseInsensitive, .diacriticInsensitive], locale: locale)
            let marker = #"(?<![\p{L}\p{N}_])fotograf[ \t·:.-]+gozlemi(?![\p{L}\p{N}_])"#
            let categoryPattern = #"(?<![\p{L}\p{N}_])"# + NSRegularExpression.escapedPattern(for: category.rawValue.lowercased()) + #"(?![\p{L}\p{N}_])"#
            return folded.range(of: marker, options: .regularExpression) != nil && folded.range(of: categoryPattern, options: .regularExpression) != nil
        }
        guard !notes.components(separatedBy: .newlines).contains(where: mentionsTarget) else { throw Failure.duplicate }
        var found: [(Range<String.Index>, String)] = []
        description.enumerateSubstrings(in: description.startIndex..<description.endIndex, options: [.byLines, .substringNotRequired]) { _, range, _, _ in
            let line = String(description[range])
            if mentionsTarget(line) { found.append((range, line)) }
        }
        guard !found.isEmpty else { throw Failure.missing }
        guard found.count == 1 else { throw Failure.duplicate }
        let (range, before) = found[0]
        guard category.validLines.contains(before) else { throw Failure.malformed }
        let after: String?
        switch action {
        case .replace:
            guard let chosen = category.selectedLine(selection) else { throw Failure.empty }
            guard chosen != before else { throw Failure.unchanged }
            after = chosen
        case .remove: after = nil
        }
        var updated = description
        updated.replaceSubrange(range, with: after ?? "")
        guard updated.utf16.count <= ExperimentalDesignBrief.maxTextUnits else { throw Failure.tooLong }
        return EditPlan(category: category, action: action, selection: selection, before: before, after: after, updatedDescription: updated)
    }
    static func append(_ selection: Selection, description: String, notes: String) throws -> String {
        let lines = selection.lines
        guard !lines.isEmpty else { throw Failure.empty }
        let previous = (description + "\n" + notes).components(separatedBy: CharacterSet.newlines.union(CharacterSet(charactersIn: ";")))
        let locale = Locale(identifier: "tr_TR")
        var additions: [String] = []
        for line in lines {
            guard let colon = line.firstIndex(of: ":") else { throw Failure.duplicate }
            let prefix = String(line[...colon])
            let matches = previous.map { $0.trimmingCharacters(in: .whitespacesAndNewlines) }.filter {
                $0.range(of: prefix, options: [.anchored, .caseInsensitive], range: nil, locale: locale) != nil
            }
            if matches.contains(where: { $0.compare(line, options: [.caseInsensitive], range: nil, locale: locale) != .orderedSame }) {
                throw Failure.duplicate
            }
            if matches.isEmpty { additions.append(line) }
        }
        guard !additions.isEmpty else { return description }
        let joined = description + (description.isEmpty || description.hasSuffix("\n") ? "" : "\n") + additions.joined(separator: "\n")
        guard joined.utf16.count <= ExperimentalDesignBrief.maxTextUnits else { throw Failure.tooLong }
        return joined
    }
    struct Request: Identifiable {
        let id = UUID()
        let draft: ExperimentalDesignBrief.Draft
        let explicitSelection: Bool
        let importedPortable: Bool
        let version: Int
        let revision: UUID
        func matches(_ current: ExperimentalDesignBrief.Draft, explicitSelection: Bool, importedPortable: Bool,
                     version: Int, revision: UUID, activeID: UUID?, referencePending: Bool, transition: Bool) -> Bool {
            id == activeID && !referencePending && !transition && draft == current && self.explicitSelection == explicitSelection
                && self.importedPortable == importedPortable && self.version == version && self.revision == revision
        }
    }
    /// Reuses the V2 validation limits before producing a bounded display-only image.
    static func thumbnail(_ data: Data) throws -> CGImage {
        try Task.checkCancellation()
        try ExperimentalDesignBrief.validateV2Image(data)
        try Task.checkCancellation()
        guard let source = CGImageSourceCreateWithData(data as CFData, nil),
              let type = CGImageSourceGetType(source) as String?, type == "public.png" || type == "public.jpeg",
              let thumbnail = CGImageSourceCreateThumbnailAtIndex(source, 0, [
                kCGImageSourceCreateThumbnailFromImageAlways: true,
                kCGImageSourceCreateThumbnailWithTransform: true,
                kCGImageSourceThumbnailMaxPixelSize: 1024,
                kCGImageSourceShouldCacheImmediately: true
              ] as CFDictionary) else { throw Failure.invalidImage }
        try Task.checkCancellation()
        return thumbnail
    }
}

struct PhotoObservationView: View {
    let request: PhotoObservation.Request
    let apply: (PhotoObservation.Request, PhotoObservation.Selection) -> String?
    let edit: (PhotoObservation.Request, PhotoObservation.EditPlan) -> String?
    @Environment(\.dismiss) private var dismiss
    @State private var choices = PhotoObservation.ChoiceState()
    @State private var confirmedSelection: PhotoObservation.Selection?
    @State private var confirmedRevision: UUID?
    @State private var status = ""
    @State private var pendingEdit: PhotoObservation.EditReview?
    @State private var confirmReplacement = false
    @State private var confirmRemoval = false
    @State private var image: CGImage?
    @State private var failed = false
    @State private var loading = true
    private var selection: PhotoObservation.Selection { choices.selection }
    private var editCategory: PhotoObservation.Category { choices.category }
    private var isConfirmed: Bool { confirmedSelection == selection && confirmedRevision == choices.revision }
    private var silhouetteBinding: Binding<PhotoObservation.Silhouette> {
        Binding(get: { selection.silhouette }, set: { var next = selection; next.silhouette = $0; choices.select(next) })
    }
    private var sleeveBinding: Binding<PhotoObservation.Sleeve> {
        Binding(get: { selection.sleeve }, set: { var next = selection; next.sleeve = $0; choices.select(next) })
    }
    private var neckBinding: Binding<PhotoObservation.Neck> {
        Binding(get: { selection.neck }, set: { var next = selection; next.neck = $0; choices.select(next) })
    }
    var body: some View {
        NavigationStack {
            Form {
                Section {
                    Text("Fotoğrafa kendiniz bakın. Yalnız gördüğünüz özellikleri seçin; bu işlem otomatik analiz, ölçü veya kalıp oluşturmaz.")
                    if loading { ProgressView("Fotoğraf hazırlanıyor…") }
                    else if let image {
                        Image(decorative: image, scale: 1, orientation: .up).resizable().scaledToFit()
                            .frame(maxWidth: .infinity, maxHeight: 360)
                            .accessibilityHidden(false)
                            .accessibilityLabel("Referans fotoğrafı; otomatik yorumlanmaz")
                    } else if failed { Text("Fotoğraf gösterilemiyor; orijinal korunuyor. Gözlem eklenmedi.") }
                }
                Section("Mevcut istek · elle karşılaştırın") {
                    Text("Model: \(request.draft.style == .describe ? "Seçilmedi" : request.draft.style.rawValue)")
                    if request.draft.description.isEmpty { Text("Tarif boş.") }
                    else { Text(verbatim: request.draft.description).textSelection(.enabled) }
                    if !request.draft.otherMeasurements.isEmpty { Text("Ek notlar"); Text(verbatim: request.draft.otherMeasurements).textSelection(.enabled) }
                }
                Section("Gördüğünüz özellikler") {
                    Picker("Siluet", selection: silhouetteBinding) { ForEach(PhotoObservation.Silhouette.allCases) { Text($0.rawValue).tag($0) } }
                    Picker("Kol", selection: sleeveBinding) { ForEach(PhotoObservation.Sleeve.allCases) { Text($0.rawValue).tag($0) } }
                    Picker("Yaka", selection: neckBinding) { ForEach(PhotoObservation.Neck.allCases) { Text($0.rawValue).tag($0) } }
                    Toggle("Bunlar kendi gözlemlerimdir; mevcut tarif ve modelle olası çelişkileri kendim karşılaştırdım.", isOn: Binding(
                        get: { isConfirmed },
                        set: { confirmedSelection = $0 ? selection : nil; confirmedRevision = $0 ? choices.revision : nil }))
                    Text("Serbest metindeki çelişkiler otomatik saptanmaz. Bilinmeyen özellikleri seçmeyin.").font(.caption)
                    if !status.isEmpty { Text(status).foregroundStyle(.secondary) }
                    Button("Seçilen gözlemleri tarife ekle") {
                        guard isConfirmed, !loading, !failed, image != nil, !selection.lines.isEmpty else { return }
                        if let problem = apply(request, selection) { status = problem }
                        else { dismiss() }
                    }
                        .disabled(loading || failed || image == nil || !isConfirmed || selection.lines.isEmpty)
                }
                Section("Önceki gözlemi düzelt veya kaldır") {
                    Picker("Düzenlenecek özellik", selection: Binding(get: { editCategory }, set: { choices.selectCategory($0) })) { ForEach(PhotoObservation.Category.allCases) { Text($0.rawValue).tag($0) } }
                    Text("Yalnız tarifteki tek, tam bir uygulama gözlem satırı değiştirilebilir. Bilinmiyor seçimi hiçbir satırı silmez.").font(.caption)
                    Button("Seçili değere değiştirmeyi incele…") { prepareEdit(.replace) }
                        .disabled(loading || failed || image == nil || !isConfirmed || editCategory.selectedLine(selection) == nil)
                    Button("Önceki gözlemi kaldırmayı incele…") { prepareEdit(.remove) }
                        .disabled(loading || failed || image == nil)
                }
            }
            .navigationTitle("Fotoğrafı incele")
            .toolbar { ToolbarItem(placement: .cancellationAction) { Button("Vazgeç") { dismiss() } } }
            .confirmationDialog("Önceki gözlem değiştirilsin mi?", isPresented: $confirmReplacement, titleVisibility: .visible) {
                Button("Gözlemi değiştir") { confirmEdit(.replace) }
                Button("Vazgeç", role: .cancel) { pendingEdit = nil }
            } message: {
                Text("Önceki: \(pendingEdit?.plan.before ?? "")\nYeni: \(pendingEdit?.plan.after ?? "")")
                Text("Mevcut model, ölçüler ve fotoğraf değişmez. Diğer tarif metni korunur; çelişkileri kendiniz kontrol edin.")
            }
            .confirmationDialog("Önceki gözlem kaldırılsın mı?", isPresented: $confirmRemoval, titleVisibility: .visible) {
                Button("Gözlemi kaldır", role: .destructive) { confirmEdit(.remove) }
                Button("Vazgeç", role: .cancel) { pendingEdit = nil }
            } message: {
                Text("Kaldırılacak: \(pendingEdit?.plan.before ?? "")")
                Text("Yalnız belirtilen satırın içeriği kaldırılır; model, ölçüler, fotoğraf ve diğer metin korunur.")
            }
            .task(id: request.id) {
                guard let bytes = request.draft.image else { loading = false; failed = true; return }
                let worker = Task.detached(priority: .userInitiated) { try PhotoObservation.thumbnail(bytes) }
                do {
                    let result = try await withTaskCancellationHandler(operation: { try await worker.value }, onCancel: { worker.cancel() })
                    try Task.checkCancellation()
                    image = result; loading = false
                } catch {
                    guard !Task.isCancelled else { return }
                    loading = false; failed = true
                }
            }
        }
    }
    private func prepareEdit(_ action: PhotoObservation.EditAction) {
        do {
            let plan = try PhotoObservation.editPlan(action, category: editCategory, selection: selection,
                description: request.draft.description, notes: request.draft.otherMeasurements)
            pendingEdit = .init(plan: plan, revision: choices.revision); status = ""
            if action == .replace { confirmReplacement = true } else { confirmRemoval = true }
        } catch PhotoObservation.Failure.empty { status = "Değiştirmek için bilinmeyen dışında bir değer seçin; bilinmiyor silmez." }
        catch PhotoObservation.Failure.missing { status = "Bu özellik için tam bir önceki gözlem bulunamadı; metin korunuyor." }
        catch PhotoObservation.Failure.unchanged { status = "Seçili değer zaten mevcut; hiçbir değişiklik yapılmadı." }
        catch PhotoObservation.Failure.tooLong { status = "Metin sınırı nedeniyle işlem yapılamadı; mevcut istek korundu." }
        catch { status = "Gözlem birden fazla, bozuk veya ek notlarda olabilir. Metni kontrol edin; hiçbir değişiklik yapılmadı." }
    }
    private func confirmEdit(_ action: PhotoObservation.EditAction) {
        guard let review = pendingEdit,
              review.matches(choices, action: action, confirmedSelection: confirmedSelection, confirmedRevision: confirmedRevision) else {
            pendingEdit = nil; status = "Seçim değişti; gözlem değiştirilmedi. Yeniden inceleyin."; return
        }
        pendingEdit = nil
        if let problem = edit(request, review.plan) { status = problem }
        else { dismiss() }
    }
}
