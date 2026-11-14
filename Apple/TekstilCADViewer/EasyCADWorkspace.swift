import SwiftUI

// A deliberately small, genuinely editable workspace. It creates new contour-only
// .tcad documents, never rewrites an imported factory project or strips metadata.
private struct DraftPoint: Equatable {
    var x: Double
    var y: Double
}

private struct DraftPiece: Identifiable, Equatable {
    var id = UUID()
    var name: String
    var closed: Bool
    var points: [DraftPoint]

    var minX: Double { points.map(\.x).min() ?? 0 }
    var minY: Double { points.map(\.y).min() ?? 0 }
    var maxX: Double { points.map(\.x).max() ?? 0 }
    var maxY: Double { points.map(\.y).max() ?? 0 }
}

private struct DraftProject: Equatable {
    var name: String
    var pieces: [DraftPiece]

    static let blank = DraftProject(name: "Yeni kalıp", pieces: [])
    static let example = DraftProject(name: "Örnek kalıp", pieces: [
        DraftPiece(name: "Ön parça", closed: true, points: [
            DraftPoint(x: 0, y: 0), DraftPoint(x: 100, y: 0),
            DraftPoint(x: 100, y: 160), DraftPoint(x: 0, y: 160)
        ]),
        DraftPiece(name: "Çizgi", closed: false, points: [
            DraftPoint(x: 125, y: 0), DraftPoint(x: 225, y: 100)
        ])
    ])
}

struct EasyCADWorkspace: View {
    @Environment(\.dismiss) private var dismiss
    @State private var document: DraftProject
    @State private var undoStack: [DraftProject] = []
    @State private var redoStack: [DraftProject] = []
    @State private var selectedID: UUID?
    @State private var projectName = ""
    @State private var pieceName = ""
    @State private var pieceX = ""
    @State private var pieceY = ""
    @State private var problem: String?
    @State private var success: String?
    @State private var exportURL: URL?

    init(example: Bool) {
        let initial = example ? DraftProject.example : DraftProject.blank
        _document = State(initialValue: initial)
        _projectName = State(initialValue: initial.name)
        _selectedID = State(initialValue: initial.pieces.first?.id)
        if let first = initial.pieces.first {
            _pieceName = State(initialValue: first.name)
            _pieceX = State(initialValue: Self.number(first.minX))
            _pieceY = State(initialValue: Self.number(first.minY))
        }
    }

    private var selectedIndex: Int? {
        document.pieces.firstIndex(where: { $0.id == selectedID })
    }

    private static func number(_ value: Double) -> String {
        value.formatted(.number.precision(.fractionLength(0...3)))
    }

    private func numericInput(_ text: String) -> Double? {
        guard let value = Double(text.trimmingCharacters(in: .whitespacesAndNewlines)
            .replacingOccurrences(of: ",", with: ".")),
              value.isFinite, abs(value) <= 100_000_000 else { return nil }
        return value
    }

    private func select(_ piece: DraftPiece?) {
        selectedID = piece?.id
        pieceName = piece?.name ?? ""
        pieceX = piece.map { Self.number($0.minX) } ?? ""
        pieceY = piece.map { Self.number($0.minY) } ?? ""
    }

    private func commit(_ updated: DraftProject, notice: String) {
        guard updated != document else { return }
        undoStack.append(document)
        if undoStack.count > 50 { undoStack.removeFirst() }
        redoStack.removeAll()
        document = updated
        exportURL = nil
        success = notice
    }

    private func addRectangle() {
        guard document.pieces.count < 10_000 else { problem = "Parça sınırına ulaşıldı."; return }
        var updated = document
        let start = Double(document.pieces.count) * 20
        let piece = DraftPiece(name: "Dikdörtgen \(document.pieces.count + 1)", closed: true, points: [
            DraftPoint(x: start, y: start), DraftPoint(x: start + 100, y: start),
            DraftPoint(x: start + 100, y: start + 150), DraftPoint(x: start, y: start + 150)
        ])
        updated.pieces.append(piece)
        commit(updated, notice: "Yeni dikdörtgen eklendi. Ölçüleri milimetre cinsindendir.")
        select(piece)
    }

    private func addLine() {
        guard document.pieces.count < 10_000 else { problem = "Parça sınırına ulaşıldı."; return }
        var updated = document
        let start = Double(document.pieces.count) * 20
        let piece = DraftPiece(name: "Çizgi \(document.pieces.count + 1)", closed: false, points: [
            DraftPoint(x: start, y: start), DraftPoint(x: start + 100, y: start)
        ])
        updated.pieces.append(piece)
        commit(updated, notice: "100 mm uzunluğunda bir çizgi eklendi.")
        select(piece)
    }

    private func saveDetails() {
        let name = projectName.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !name.isEmpty, name.count <= 128 else {
            problem = "Proje adı 1–128 karakter arasında olmalı."; return
        }
        var updated = document
        updated.name = name
        if let index = selectedIndex {
            let selectedName = pieceName.trimmingCharacters(in: .whitespacesAndNewlines)
            guard !selectedName.isEmpty, selectedName.count <= 128,
                  let x = numericInput(pieceX), let y = numericInput(pieceY) else {
                problem = "Parça adını ve X/Y konumlarını kontrol edin. Ölçü birimi mm."; return
            }
            let piece = updated.pieces[index]
            let dx = x - piece.minX, dy = y - piece.minY
            let translated = piece.points.map { DraftPoint(x: $0.x + dx, y: $0.y + dy) }
            guard translated.allSatisfy({ $0.x.isFinite && $0.y.isFinite && abs($0.x) <= 100_000_000 && abs($0.y) <= 100_000_000 }) else {
                problem = "Kalıp çalışma alanı sınırını aşıyor."; return
            }
            updated.pieces[index].name = selectedName
            updated.pieces[index].points = translated
        }
        commit(updated, notice: "Değişiklikler uygulandı.")
        if let index = selectedIndex { select(document.pieces[index]) }
    }

    private func move(dx: Double, dy: Double) {
        guard let index = selectedIndex else { return }
        var updated = document
        let points = updated.pieces[index].points.map { DraftPoint(x: $0.x + dx, y: $0.y + dy) }
        guard points.allSatisfy({ abs($0.x) <= 100_000_000 && abs($0.y) <= 100_000_000 }) else {
            problem = "Kalıp çalışma alanı sınırını aşıyor."; return
        }
        updated.pieces[index].points = points
        commit(updated, notice: "Parça 10 mm taşındı.")
        select(document.pieces[index])
    }

    private func rotateSelected() {
        guard let index = selectedIndex else { return }
        var updated = document
        let piece = updated.pieces[index]
        let cx = (piece.minX + piece.maxX) / 2
        let cy = (piece.minY + piece.maxY) / 2
        let points = piece.points.map { DraftPoint(x: cx - ($0.y - cy), y: cy + ($0.x - cx)) }
        guard points.allSatisfy({ abs($0.x) <= 100_000_000 && abs($0.y) <= 100_000_000 }) else {
            problem = "Döndürülmüş parça çalışma alanı sınırını aşıyor."; return
        }
        updated.pieces[index].points = points
        commit(updated, notice: "Parça 90° döndürüldü.")
        select(document.pieces[index])
    }

    private func duplicateSelected() {
        guard let index = selectedIndex, document.pieces.count < 10_000 else { return }
        var updated = document
        var copy = updated.pieces[index]
        copy.id = UUID()
        copy.name += " kopya"
        copy.points = copy.points.map { DraftPoint(x: $0.x + 20, y: $0.y + 20) }
        guard copy.points.allSatisfy({ abs($0.x) <= 100_000_000 && abs($0.y) <= 100_000_000 }) else {
            problem = "Kopya çalışma alanı sınırını aşıyor."; return
        }
        updated.pieces.append(copy)
        commit(updated, notice: "Bağımsız bir parça kopyası oluşturuldu.")
        select(copy)
    }

    private func deleteSelected() {
        guard let index = selectedIndex else { return }
        var updated = document
        updated.pieces.remove(at: index)
        commit(updated, notice: "Parça silindi. Geri Al ile geri getirebilirsiniz.")
        select(updated.pieces.first)
    }

    private func undo() {
        guard let previous = undoStack.popLast() else { return }
        redoStack.append(document)
        document = previous
        exportURL = nil
        select(document.pieces.first(where: { $0.id == selectedID }) ?? document.pieces.first)
        projectName = document.name
        success = "İşlem geri alındı."
    }

    private func redo() {
        guard let next = redoStack.popLast() else { return }
        undoStack.append(document)
        document = next
        exportURL = nil
        select(document.pieces.first(where: { $0.id == selectedID }) ?? document.pieces.first)
        projectName = document.name
        success = "İşlem yeniden uygulandı."
    }

    private func prepareExport() {
        guard !document.pieces.isEmpty else { problem = "Önce en az bir parça ekleyin."; return }
        // Export v1 contour-only data, not an unsupported production metadata format.
        // IDs are mandatory for the Windows loader; grading is intentionally empty.
        let body: [String: Any] = [
            "Name": document.name,
            "GradingRules": [],
            "Pieces": document.pieces.map { piece in
                [
                    "Id": piece.id.uuidString, "Name": piece.name, "Closed": piece.closed,
                    "Points": piece.points.map { ["X": $0.x, "Y": $0.y] }
                ] as [String: Any]
            }
        ]
        do {
            let bytes = try JSONSerialization.data(withJSONObject: body, options: [.prettyPrinted, .sortedKeys])
            guard bytes.count < 64 * 1024 * 1024 else {
                problem = "Dosya 64 MB sınırını aşıyor."; return
            }
            let url = FileManager.default.temporaryDirectory
                .appendingPathComponent("MonoDiTree-\(UUID().uuidString).tcad")
            try bytes.write(to: url, options: .atomic)
            exportURL = url
            success = "Dosya hazır. Paylaş düğmesinden Dosyalara Kaydet'i seçin."
        } catch {
            problem = "Dosya hazırlanamadı: \(error.localizedDescription)"
        }
    }

    var body: some View {
        NavigationStack {
            ScrollView {
                VStack(alignment: .leading, spacing: 18) {
                    VStack(alignment: .leading, spacing: 5) {
                        Text("Kalıbını oluştur").font(.title2.bold())
                        Text("Ölçüler milimetre (mm). Önce bir şekil ekle, sonra aşağıdaki araçlarla düzenle.")
                            .foregroundStyle(.secondary)
                        Text("Yeni çizim atölyesi • Üretim/kesim onayı değildir.")
                            .font(.caption).foregroundStyle(.secondary)
                    }

                    VStack(alignment: .leading, spacing: 8) {
                        Label("1 · Çizimin", systemImage: "pencil.tip").font(.headline)
                        preview
                        if document.pieces.isEmpty {
                            Text("Çalışma alanın boş. Aşağıdan dikdörtgen veya çizgi ekle.")
                                .foregroundStyle(.secondary)
                        } else {
                            Text("\(document.pieces.count) parça • Önizleme ölçüye göre ekrana sığdırılır")
                                .font(.caption).foregroundStyle(.secondary)
                        }
                    }

                    VStack(alignment: .leading, spacing: 10) {
                        Label("2 · Parça ekle", systemImage: "plus.square.on.square").font(.headline)
                        HStack(spacing: 10) {
                            Button { addRectangle() } label: {
                                Label("Dikdörtgen ekle", systemImage: "square")
                                    .frame(maxWidth: .infinity, minHeight: 48)
                            }
                            .buttonStyle(.borderedProminent)
                            Button { addLine() } label: {
                                Label("Çizgi ekle", systemImage: "line.diagonal")
                                    .frame(maxWidth: .infinity, minHeight: 48)
                            }
                            .buttonStyle(.bordered)
                        }
                    }

                    if !document.pieces.isEmpty {
                        VStack(alignment: .leading, spacing: 10) {
                            Label("3 · Düzenle", systemImage: "slider.horizontal.3").font(.headline)
                            Picker("Parça seç", selection: $selectedID) {
                                ForEach(document.pieces) { piece in
                                    Text(piece.name).tag(Optional(piece.id))
                                }
                            }
                            .onChange(of: selectedID) { newValue in
                                select(document.pieces.first(where: { $0.id == newValue }))
                            }
                            .pickerStyle(.menu)
                            if selectedIndex != nil {
                                TextField("Parça adı", text: $pieceName)
                                    .textFieldStyle(.roundedBorder)
                                    .accessibilityLabel("Seçili parçanın adı")
                                HStack {
                                    VStack(alignment: .leading) {
                                        Text("X (mm)").font(.caption)
                                        TextField("X", text: $pieceX).textFieldStyle(.roundedBorder)
                                    }
                                    VStack(alignment: .leading) {
                                        Text("Y (mm)").font(.caption)
                                        TextField("Y", text: $pieceY).textFieldStyle(.roundedBorder)
                                    }
                                }
                                Button("Konumu ve adı uygula") { saveDetails() }
                                    .buttonStyle(.borderedProminent)
                                HStack {
                                    Button("← 10 mm") { move(dx: -10, dy: 0) }
                                    Button("10 mm →") { move(dx: 10, dy: 0) }
                                    Button("90° döndür") { rotateSelected() }
                                }
                                .buttonStyle(.bordered)
                                .font(.caption)
                                HStack {
                                    Button("Kopyala", action: duplicateSelected)
                                    Button("Sil", role: .destructive, action: deleteSelected)
                                }
                                .buttonStyle(.bordered)
                            }
                        }
                    }

                    VStack(alignment: .leading, spacing: 10) {
                        Label("4 · Dosyanı kaydet", systemImage: "square.and.arrow.up").font(.headline)
                        TextField("Proje adı", text: $projectName)
                            .textFieldStyle(.roundedBorder)
                        Button("Proje adını uygula") { saveDetails() }
                            .buttonStyle(.bordered)
                        HStack {
                            Button("Geri al", action: undo).disabled(undoStack.isEmpty)
                            Button("İleri al", action: redo).disabled(redoStack.isEmpty)
                        }
                        .buttonStyle(.bordered)
                        Button { saveDetails(); if problem == nil { prepareExport() } } label: {
                            Label("Dosyayı hazırla (.tcad)", systemImage: "doc.badge.plus")
                                .frame(maxWidth: .infinity, minHeight: 48)
                        }
                        .buttonStyle(.borderedProminent)
                        if let exportURL {
                            ShareLink(item: exportURL, preview: SharePreview("Mono Di Tree CAD çizimi")) {
                                Label("Paylaş veya Dosyalara kaydet", systemImage: "square.and.arrow.up")
                                    .frame(maxWidth: .infinity, minHeight: 48)
                            }
                            .buttonStyle(.borderedProminent)
                        }
                        Text("Yalnız yeni çizim parçaları dışa aktarılır. Bu araç, içe alınmış fabrika dosyalarını değiştirmez.")
                            .font(.caption).foregroundStyle(.secondary)
                    }
                    if let success { Text(success).font(.footnote).foregroundStyle(.secondary) }
                }
                .padding()
            }
            .navigationTitle("Kalıp atölyesi")
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Kapat") { dismiss() }
                }
            }
        }
        .alert("İşlem tamamlanamadı", isPresented: Binding(
            get: { problem != nil },
            set: { if !$0 { problem = nil } }
        )) {
            Button("Tamam", role: .cancel) { problem = nil }
        } message: {
            Text(problem ?? "")
        }
    }

    private var preview: some View {
        Canvas { context, size in
            let points = document.pieces.flatMap(\.points)
            guard let first = points.first else { return }
            var minX = first.x, maxX = first.x, minY = first.y, maxY = first.y
            for point in points {
                minX = min(minX, point.x); maxX = max(maxX, point.x)
                minY = min(minY, point.y); maxY = max(maxY, point.y)
            }
            let width = max(maxX - minX, 1)
            let height = max(maxY - minY, 1)
            let scale = min(max((Double(size.width) - 32) / width, 0.000001),
                            max((Double(size.height) - 32) / height, 0.000001))
            let offsetX = (Double(size.width) - (maxX - minX) * scale) / 2
            let offsetY = (Double(size.height) - (maxY - minY) * scale) / 2
            func position(_ point: DraftPoint) -> CGPoint {
                CGPoint(x: offsetX + (point.x - minX) * scale,
                        y: Double(size.height) - offsetY - (point.y - minY) * scale)
            }
            for piece in document.pieces {
                guard let first = piece.points.first else { continue }
                var path = Path()
                path.move(to: position(first))
                for point in piece.points.dropFirst() { path.addLine(to: position(point)) }
                if piece.closed { path.closeSubpath() }
                context.stroke(path,
                               with: .color(piece.id == selectedID ? .accentColor : .secondary),
                               lineWidth: piece.id == selectedID ? 3 : 1.8)
            }
        }
        .frame(height: 260)
        .frame(maxWidth: .infinity)
        .background(Color.secondary.opacity(0.08), in: RoundedRectangle(cornerRadius: 14))
        .accessibilityLabel("Kalıp çiziminin ölçekli olmayan önizlemesi")
        .accessibilityHint("Düzenlemek için Parça seç menüsünü ve aşağıdaki araçları kullanın.")
    }
}
