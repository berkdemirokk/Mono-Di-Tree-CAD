import SwiftUI
import UniformTypeIdentifiers
#if canImport(UIKit)
import UIKit
#else
import AppKit
#endif

struct FactoryCatalogView: View {
    let english: Bool
    @Environment(\.dismiss) private var dismiss
    @State private var catalog: FactoryModelCatalog?
    @State private var query = ""
    @State private var importing = false
    @State private var busy = false
    @State private var failure: String?
    private func t(_ tr: String, _ en: String) -> String { english ? en : tr }
    private var matches: [FactoryModelCatalog.Model] { catalog?.search(query) ?? [] }
    private var cache: URL? {
        FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first?
            .appendingPathComponent("FactoryReferences", isDirectory: true).appendingPathComponent("catalog-v1.json")
    }

    var body: some View {
        NavigationStack {
            List {
                Section {
                    Text(t("Kayıtlı beden, malzeme ve dikiş payı bilgileri kullanıcı açıklamalarıdır. Kalıp geometrisi dönüştürülmez; mevcut çizime pay eklenmez.", "Recorded sizes, materials and seam allowances are owner declarations. No contours are converted or allowances added to the current drawing."))
                        .font(.footnote).foregroundStyle(.secondary)
                    Button(t("Kütüphane aç…", "Open catalogue…")) { importing = true }.disabled(busy)
                        .accessibilityIdentifier("factory-catalog-import")
                    if busy { ProgressView(t("Kütüphane okunuyor…", "Reading catalogue…")) }
                    if let failure { Text(failure).foregroundStyle(.red) }
                    if let catalog { Text("\(catalog.name) · \(matches.count) / \(catalog.models.count)\n\(catalog.confirmedOn)").font(.footnote) }
                }
                ForEach(matches) { model in
                    NavigationLink {
                        FactoryModelDetail(model: model, catalog: catalog!, english: english)
                    } label: {
                        VStack(alignment: .leading, spacing: 5) {
                            Text(model.fullName).font(.headline)
                            Text(model.sizes.map(String.init).joined(separator: " / ")).font(.subheadline)
                            if model.status != "unconfirmed" {
                                Text(model.status == "cancelled" ? t("Kullanım dışı", "Cancelled") : t("Henüz denenmemiş", "Not yet trialled")).font(.caption).foregroundStyle(.orange)
                            }
                        }
                    }
                }
                if catalog != nil && matches.isEmpty { Text(t("Eşleşen model yok.", "No matching models.")) }
            }
            .navigationTitle(t("Model kütüphanesi", "Model catalogue"))
            .searchable(text: $query, prompt: t("Model kodu veya beden", "Model code or size"))
            .toolbar { ToolbarItem(placement: .cancellationAction) { Button(t("Kapat", "Close")) { dismiss() } } }
            .fileImporter(isPresented: $importing, allowedContentTypes: [.json], allowsMultipleSelection: false) { result in
                switch result {
                case .success(let urls): if let url = urls.first { load(url, persist: true) }
                case .failure(let error): if !isFilePickerCancellation(error) { failure = t("Dosya seçilemedi.", "Could not select the file.") }
                }
            }
            .onAppear { if catalog == nil, !busy, let cache, FileManager.default.fileExists(atPath: cache.path) { load(cache, persist: false) } }
        }
    }

    private func load(_ url: URL, persist: Bool) {
        guard !busy else { return }
        busy = true; failure = nil
        let destination = cache
        Task {
            do {
                let loaded = try await Task.detached {
                    let bytes = try FactoryModelCatalog.read(url)
                    let value = try FactoryModelCatalog.decode(bytes)
                    if persist, let destination {
                        try FileManager.default.createDirectory(at: destination.deletingLastPathComponent(), withIntermediateDirectories: true)
                        try bytes.write(to: destination, options: .atomic)
                    }
                    return value
                }.value
                catalog = loaded; query = ""
            } catch { failure = t("Kütüphane açılamadı. Önceki kütüphane korunuyor; dosya biçimini ve 16 MB sınırını kontrol edin.", "Could not open catalogue. The previous catalogue is retained; check the format and 16 MB limit.") }
            busy = false
        }
    }
}

private struct FactoryModelDetail: View {
    let model: FactoryModelCatalog.Model
    let catalog: FactoryModelCatalog
    let english: Bool
    private func t(_ tr: String, _ en: String) -> String { english ? en : tr }
    var body: some View {
        List {
            Section {
                if let bytes = model.thumbnailData {
                    #if canImport(UIKit)
                    if let image = UIImage(data: bytes) { Image(uiImage: image).resizable().scaledToFit().frame(maxHeight: 340).accessibilityLabel(model.fullName) }
                    #else
                    if let image = NSImage(data: bytes) { Image(nsImage: image).resizable().scaledToFit().frame(maxHeight: 340).accessibilityLabel(model.fullName) }
                    #endif
                }
                Text(model.fullName).font(.headline)
                Text(t("Bedenler: ", "Sizes: ") + model.sizes.map(String.init).joined(separator: " / "))
                Text(model.status == "cancelled" ? t("Kullanım dışı", "Cancelled") : model.status == "untested" ? t("Henüz denenmemiş", "Not yet trialled") : t("Üretim durumu ayrıca onaylanmadı", "Production status not separately confirmed"))
                Text(model.seamAllowanceIncluded ? t("Dikiş payı dahil · kullanıcı onayı", "Seam allowance included · owner confirmed") : t("Dikiş payı dahil bilgisi yok", "Seam allowance inclusion not confirmed"))
                Text(t("Pay miktarı ve fiziksel ölçüler henüz doğrulanmadı.", "Allowance amount and physical measurements have not been verified.")).font(.footnote).foregroundStyle(.secondary)
                if let code = model.code, catalog.models.filter({ $0.code == code }).count > 1 {
                    Text(t("Aynı kodun farklı sürümleri var; tam model adını kullanın.", "This code has different versions; use the full model name.")).foregroundStyle(.orange)
                }
            }
            Section(t("Malzeme grupları", "Material groups")) {
                ForEach(Array(Set(model.parts.map(\.group))).sorted(), id: \.self) { group in
                    let label = group.isEmpty ? t("Grup belirtilmemiş", "Unassigned group") : group
                    let alias = catalog.materialAliases[group]
                    Text(label + (alias.map { " → " + $0 } ?? "") + ": \(model.parts.filter { $0.group == group }.count)" +
                         (catalog.cuttingGroups.contains(group) ? t(" · kesime dahil (kullanıcı)", " · cut group (owner)") : ""))
                }
                Text(t("Parça sayıları kesim adetleri değildir. Kullanım dışı modelin durumu korunur.", "Part counts are not cutting quantities. Cancelled models remain cancelled.")).font(.footnote).foregroundStyle(.secondary)
            }
            if !model.notes.isEmpty {
                Section(t("Kaynak notları", "Source notes")) { ForEach(Array(model.notes.enumerated()), id: \.offset) { _, note in Text(note) } }
            }
            Section(t("Parça listesi", "Parts list")) {
                ForEach(Array(model.parts.enumerated()), id: \.offset) { _, part in Text(part.name + " · " + part.group) }
            }
        }.navigationTitle(model.code ?? model.fullName)
    }
}
