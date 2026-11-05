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
    enum Failure: Error { case empty, duplicate, tooLong, invalidImage }
    static func append(_ selection: Selection, description: String, notes: String) throws -> String {
        let lines = selection.lines
        guard !lines.isEmpty else { throw Failure.empty }
        let previous = (description + "\n" + notes).components(separatedBy: CharacterSet.newlines.union(CharacterSet(charactersIn: ";")))
        for line in lines {
            guard let colon = line.firstIndex(of: ":") else { throw Failure.duplicate }
            let prefix = String(line[...colon])
            if previous.contains(where: { $0.trimmingCharacters(in: .whitespacesAndNewlines).range(of: prefix, options: [.anchored, .caseInsensitive]) != nil }) {
                throw Failure.duplicate
            }
        }
        let joined = description + (description.isEmpty || description.hasSuffix("\n") ? "" : "\n") + lines.joined(separator: "\n")
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
    @Environment(\.dismiss) private var dismiss
    @State private var selection = PhotoObservation.Selection()
    @State private var confirmedSelection: PhotoObservation.Selection?
    @State private var status = ""
    @State private var image: CGImage?
    @State private var failed = false
    @State private var loading = true
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
                    Picker("Siluet", selection: $selection.silhouette) { ForEach(PhotoObservation.Silhouette.allCases) { Text($0.rawValue).tag($0) } }
                    Picker("Kol", selection: $selection.sleeve) { ForEach(PhotoObservation.Sleeve.allCases) { Text($0.rawValue).tag($0) } }
                    Picker("Yaka", selection: $selection.neck) { ForEach(PhotoObservation.Neck.allCases) { Text($0.rawValue).tag($0) } }
                    Toggle("Bunlar kendi gözlemlerimdir; mevcut tarif ve modelle olası çelişkileri kendim karşılaştırdım.", isOn: Binding(
                        get: { confirmedSelection == selection },
                        set: { confirmedSelection = $0 ? selection : nil }))
                    Text("Serbest metindeki çelişkiler otomatik saptanmaz. Bilinmeyen özellikleri seçmeyin.").font(.caption)
                    if !status.isEmpty { Text(status).foregroundStyle(.secondary) }
                    Button("Seçilen gözlemleri tarife ekle") {
                        guard confirmedSelection == selection, !loading, !failed, image != nil, !selection.lines.isEmpty else { return }
                        if let problem = apply(request, selection) { status = problem }
                        else { dismiss() }
                    }
                        .disabled(loading || failed || image == nil || confirmedSelection != selection || selection.lines.isEmpty)
                }
            }
            .navigationTitle("Fotoğrafı incele")
            .toolbar { ToolbarItem(placement: .cancellationAction) { Button("Vazgeç") { dismiss() } } }
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
}
