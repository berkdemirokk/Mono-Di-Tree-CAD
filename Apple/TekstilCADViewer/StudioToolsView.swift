import SwiftUI

struct StudioToolsView: View {
    let drawing: Drawing?
    let sourceData: Data?
    @Binding var english: Bool
    @State private var showingMarker = false
    @State private var showingMeasurements = false
    @State private var showingHistory = false
    @State private var showingCatalog = false

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Picker("Language / Dil", selection: $english) {
                Text("Türkçe").tag(false)
                Text("English").tag(true)
            }.pickerStyle(.segmented).accessibilityIdentifier("studio-language")
            Button(english ? "Model catalogue" : "Model kütüphanesi") { showingCatalog = true }
                .frame(minHeight: 44).accessibilityIdentifier("factory-catalog")
                .sheet(isPresented: $showingCatalog) { FactoryCatalogView(english: english) }
            #if os(iOS)
            Button { showingMarker = true } label: {
                Label(english ? "Marker workshop · A4 PDF" : "Pastal atölyesi · A4 PDF", systemImage: "scissors")
                    .frame(maxWidth: .infinity, minHeight: 44)
            }.buttonStyle(.borderedProminent)
                .accessibilityIdentifier("marker-workspace")
                .sheet(isPresented: $showingMarker) { MarkerWorkspaceView(drawing: drawing, sourceData: sourceData, english: english) }
            #endif
            Button(english ? "Reference measurements · 36–52" : "Referans ölçüler · 36–52") { showingMeasurements = true }
                .frame(minHeight: 44)
                .sheet(isPresented: $showingMeasurements) { ReferenceMeasurementsView(english: english) }
            if let history = drawing?.markerHistory {
                Button(english ? "Saved marker request" : "Kayıtlı pastal talebi") { showingHistory = true }
                    .frame(minHeight: 44)
                    .sheet(isPresented: $showingHistory) { MarkerHistoryView(history: history, english: english) }
                Text(english ? "Saved marker: read-only preview. Original file is preserved." : "Kayıtlı pastal: salt okunur önizleme. Özgün dosya korunur.")
                    .font(.caption).foregroundStyle(.secondary)
            }
        }
    }
}

private struct MarkerHistoryView: View {
    let history: MarkerHistoryPreview
    let english: Bool
    @Environment(\.dismiss) private var dismiss
    var body: some View {
        NavigationStack {
            List {
                Section(english ? "Model" : "Model") {
                    Text(history.modelCode)
                    Text("\(history.fabricWidthMM / 10, specifier: "%.1f") cm")
                    Text(english ? "\(history.totalCopies) pieces" : "\(history.totalCopies) parça")
                }
                Section(english ? "Sizes and sets" : "Beden ve takım") {
                    ForEach(history.sizeQuantities, id: \.size) { row in
                        Text("\(row.size) × \(row.sets)")
                    }
                }
                Section(english ? "Original request" : "Özgün talep") {
                    Text(history.sourceText).textSelection(.enabled)
                }
                Section(english ? "Status" : "Durum") {
                    Text(history.layoutMatchesHistory
                         ? (english ? "The visible layout matches the saved fingerprint." : "Görünür yerleşim kayıtlı parmak iziyle eşleşiyor.")
                         : (english ? "The visible layout has changed; this request is historical." : "Görünür yerleşim değişmiş; bu talep geçmiş kaydıdır."))
                    Text(english ? "This does not approve geometry, grain direction or production. Source paths are not opened." : "Bu bilgi geometri, iplik yönü veya üretim onayı değildir. Kaynak dosya yolları açılmaz.")
                }
            }
            .navigationTitle(english ? "Marker history" : "Pastal geçmişi")
            .toolbar { ToolbarItem(placement: .cancellationAction) { Button(english ? "Close" : "Kapat") { dismiss() } } }
        }
    }
}
