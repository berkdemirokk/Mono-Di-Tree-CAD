#if canImport(UIKit)
import SwiftUI
import UniformTypeIdentifiers

private struct MarkerExportDocument: FileDocument {
    static var readableContentTypes: [UTType] { [.data, .json, .pdf] }
    let data: Data
    init(data: Data) { self.data = data }
    init(configuration: ReadConfiguration) throws {
        guard let bytes = configuration.file.regularFileContents else { throw CocoaError(.fileReadCorruptFile) }
        data = bytes
    }
    func fileWrapper(configuration: WriteConfiguration) throws -> FileWrapper { FileWrapper(regularFileWithContents: data) }
}

struct MarkerWorkspaceView: View {
    let drawing: Drawing?
    var sourceData: Data? = nil
    let english: Bool
    @Environment(\.dismiss) private var dismiss
    @State private var source = ""
    @State private var order: MarkerWorkOrder?
    @State private var confirmed = false
    @State private var layout: MarkerLayout?
    @State private var busy = false
    @State private var cancellation: ModelReadCancellation?
    @State private var operation = UUID()
    @State private var notice: String?
    @State private var exportDocument: MarkerExportDocument?
    @State private var exportType = UTType.json
    @State private var exportName = ""
    @State private var exporting = false
    private func l(_ tr: String, _ en: String) -> String { english ? en : tr }

    var body: some View {
        NavigationStack {
            ScrollView {
                VStack(alignment: .leading, spacing: 16) {
                    introduction
                    requestEditor
                    if let order { MarkerOrderSummary(order: order, english: english) }
                    confirmation
                    actions
                    if busy { ProgressView(l("Hazırlanıyor…", "Preparing…")); Button(l("İptal", "Cancel")) { cancel() } }
                    if let notice { Text(notice).font(.callout).foregroundStyle(.secondary).accessibilityIdentifier("marker-notice") }
                    if let layout { MarkerResultView(layout: layout, english: english) }
                }.padding()
            }
            .navigationTitle(l("Pastal iş emri", "Marker work order"))
            .toolbar { ToolbarItem(placement: .confirmationAction) { Button(l("Kapat", "Close")) { dismiss() } } }
            .onChange(of: source) { _ in invalidate() }
            .onChange(of: confirmed) { _ in layout = nil }
            .onDisappear { cancel(showNotice: false) }
            .fileExporter(isPresented: $exporting, document: exportDocument, contentType: exportType, defaultFilename: exportName) { result in
                switch result {
                case .success: notice = l("Yeni dosya dışa aktarıldı.", "New file exported.")
                case .failure(let error):
                    if !isFilePickerCancellation(error) { notice = error.localizedDescription }
                }
            }
        }
    }
    private var introduction: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text(l("Gerçek kalıplardan adetli yerleşim ve A4 PDF", "Quantity layout and A4 PDF from real patterns")).font(.headline)
            Text(l("Kalıp üretmez. Döndürmesiz, kutu sınırlarıyla raf yerleşimi yapar; optimizasyon değildir. İplik yönü kontrol edilmez. Sonuç inceleme içindir; üretim onayı değildir.", "Does not create patterns. Uses a conservative shelf layout of bounding boxes without rotation; this is not optimization. Grain direction is unchecked. Results are for inspection, not production approval.")).font(.caption)
            Text(l("Kaynak: ", "Source: ") + (drawing?.name ?? l("Açık model yok — iş emri taslağı kaydedilebilir.", "No loaded model — a request draft can still be saved."))).font(.subheadline)
            Text(l("v5/v6 kaynak, açık Kesim/beden/malzeme/adet bilgileri gerekir. Geçmiş/serileme içeren kaynaklar desteklenmez. En fazla 500 kopya. A4 PDF: 190 × 257 mm birleştirme çerçevesi, 50 mm ölçek çizgisi; fiziksel yazdırma yapmaz. .tcad pastal kaydı oluşturmaz.", "Requires a v5/v6 source with explicit cut/size/material/quantity metadata. Sources with history/grading rules are unsupported. Up to 500 copies. A4 PDF: 190 × 257 mm joining frames and a 50 mm scale check; does not physically print or save a .tcad layout.")).font(.caption).foregroundStyle(.secondary)
        }
    }
    private var requestEditor: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text(l("İsteğinizi yazın", "Write your request")).font(.headline)
            Text(l("Örnek: 1-2-3 model, 36/38 beden, her bedenden 2 takım, tela, en 90 cm, 30 saniye", "Example: model 1-2-3, sizes 36/38, 2 sets per size, interfacing, width 90 cm, 30 seconds")).font(.caption).textSelection(.enabled)
            TextEditor(text: $source).frame(minHeight: 130).padding(4).overlay(RoundedRectangle(cornerRadius: 6).stroke(.gray.opacity(0.5))).accessibilityLabel(l("İş emri metni", "Work order text"))
            Text("\(source.utf16.count) / 12.000").font(.caption)
            Button(l("İsteği çözümle", "Interpret request")) {
                cancel(showNotice: false); order = MarkerWorkOrderParser.parse(source); confirmed = false; layout = nil; notice = nil
            }.disabled(busy || source.isEmpty)
        }
    }
    private var confirmation: some View {
        Group {
            if let order, order.complete, let drawing {
                Toggle(isOn: $confirmed) {
                    Text(l("Açık “\(drawing.name)” modelinin \(order.model ?? "") koduna ait olduğunu kontrol ettim.", "I checked that loaded model “\(drawing.name)” belongs to code \(order.model ?? "")."))
                }.disabled(busy).accessibilityIdentifier("marker-confirm-model")
                Text(l("Metin değişirse bu onay ve sonuç geçersizleşir.", "Editing the request invalidates this confirmation and the result.")).font(.caption)
            }
        }
    }
    private var actions: some View {
        VStack(alignment: .leading, spacing: 10) {
            Button(l("İş emri taslağını kaydet…", "Save request draft…")) {
                do {
                    exportDocument = MarkerExportDocument(data: try MarkerRequestStorage.data(source: source))
                    exportType = .json; exportName = "Is-emri-\(UUID().uuidString.prefix(8)).tcad-marker.json"; exporting = true
                } catch { show(error) }
            }.disabled(busy || source.isEmpty)
            Button(l("Adetli yerleşim hesapla", "Calculate quantity layout")) { calculate() }
                .buttonStyle(.borderedProminent).disabled(busy || !confirmed || drawing == nil || order?.complete != true)
                .accessibilityIdentifier("marker-calculate")
            if layout != nil {
                Button(l("A4 PDF oluştur ve dışa aktar…", "Create and export A4 PDF…")) { exportPDF() }.disabled(busy)
            }
        }
    }
    private func invalidate() {
        cancel(showNotice: false); confirmed = false; order = nil; layout = nil; exportDocument = nil; notice = nil
    }
    private func cancel(showNotice: Bool = true) {
        cancellation?.cancel(); cancellation = nil; operation = UUID(); busy = false
        if showNotice { notice = l("İşlem iptal edildi.", "Operation cancelled.") }
    }
    private func show(_ error: Error) {
        notice = (error as? MarkerFailure)?.message(english: english) ?? error.localizedDescription
    }
    private func calculate() {
        guard let order else { return }
        let token = ModelReadCancellation(), request = UUID(), sourceDrawing = drawing, originalData = sourceData, sourceConfirmed = confirmed
        cancellation = token; operation = request; busy = true; notice = nil; layout = nil
        DispatchQueue.global(qos: .userInitiated).async {
            let result = Result { try MarkerLayoutEngine.calculate(drawing: sourceDrawing, sourceData: originalData, order: order, confirmed: sourceConfirmed, cancelled: { token.isCancelled }) }
            DispatchQueue.main.async {
                guard operation == request else { return }
                busy = false; cancellation = nil
                switch result { case .success(let value): layout = value; case .failure(let error): show(error) }
            }
        }
    }
    private func exportPDF() {
        guard let layout else { return }
        let token = ModelReadCancellation(), request = UUID(), language = english
        cancellation = token; operation = request; busy = true; notice = nil
        DispatchQueue.global(qos: .userInitiated).async {
            let result = Result { try MarkerPDFExporter.data(layout: layout, english: language, cancelled: { token.isCancelled }) }
            DispatchQueue.main.async {
                guard operation == request else { return }
                busy = false; cancellation = nil
                switch result {
                case .success(let bytes):
                    exportDocument = MarkerExportDocument(data: bytes); exportType = .pdf
                    exportName = "Pastal-inceleme-\(UUID().uuidString.prefix(8)).pdf"; exporting = true
                case .failure(let error): show(error)
                }
            }
        }
    }
}

private struct MarkerOrderSummary: View {
    let order: MarkerWorkOrder
    let english: Bool
    var body: some View {
        VStack(alignment: .leading, spacing: 5) {
            Text(english ? "Interpreted request" : "Çözümlenen istek").font(.headline)
            Text("\(order.model ?? "—") · \(order.sizes.map(String.init).joined(separator: "/")) · \(order.material ?? "—")")
            Text("\(order.width.map { String($0) } ?? "—") mm · \(order.seconds.map { String($0) } ?? "—") s")
            if let sets = order.sets { Text(english ? "\(sets) sets per size × recorded cut quantity" : "Her beden \(sets) takım × kayıtlı kesim adedi") }
            ForEach(order.sizeQuantities.keys.sorted(), id: \.self) { size in
                Text(english ? "Size \(size): \(order.sizeQuantities[size]!) sets" : "\(size) beden: \(order.sizeQuantities[size]!) takım")
            }
            ForEach(order.issues.indices, id: \.self) { index in Text(order.issues[index].message(english: english)).foregroundStyle(.red) }
        }.font(.subheadline)
    }
}

private struct MarkerResultView: View {
    let layout: MarkerLayout
    let english: Bool
    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text(english ? "\(layout.placements.count) copies · \(layout.width) × \(layout.length) mm" : "\(layout.placements.count) kopya · \(layout.width) × \(layout.length) mm").font(.headline)
            Text(english ? "Contour preview; PDF also includes grainlines and marks." : "Kontur önizlemesi; PDF ayrıca iplik yönlerini ve işaretleri içerir.").font(.caption)
            Canvas { context, size in
                let scale = min(size.width / layout.width, size.height / layout.length)
                let fabric = CGRect(x: 0, y: 0, width: layout.width*scale, height: layout.length*scale)
                context.stroke(Path(fabric), with: .color(.gray), lineWidth: 1)
                for p in layout.placements {
                    var path = Path()
                    for (i, point) in p.piece.points.enumerated() {
                        let position = CGPoint(x: (point.x+p.dx)*scale, y: (point.y+p.dy)*scale)
                        if i == 0 { path.move(to: position) } else { path.addLine(to: position) }
                    }
                    path.closeSubpath(); context.stroke(path, with: .color(.primary), lineWidth: 0.7)
                }
            }.frame(height: 280).accessibilityLabel(english ? "Quantity layout contour preview" : "Adetli yerleşim kontur önizlemesi")
        }
    }
}
#endif
