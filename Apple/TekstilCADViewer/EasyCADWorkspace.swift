import SwiftUI

// A deliberately small, genuinely editable workspace. It creates new contour-only
// .tcad documents, never rewrites an imported factory project or strips metadata.
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
    @State private var pieceWidth = ""
    @State private var pieceHeight = ""
    @State private var problem: String?
    @State private var success: String?
    @State private var exportURL: URL?
    @State private var snapToFiveMillimeters = true
    @State private var dragStarted = false
    @State private var draggedPieceID: UUID?
    @State private var currentDrag = CGSize.zero
    @State private var editingPoints: DraftPiece?
    @State private var showingTouchComposer = false
    @State private var shouldStoreImportedSeed = false

    init(example: Bool, imported: DraftProject? = nil) {
        let initial = imported ?? (example ? DraftProject.example : DraftProject.blank)
        _shouldStoreImportedSeed = State(initialValue: imported != nil)
        _document = State(initialValue: initial)
        _projectName = State(initialValue: initial.name)
        _selectedID = State(initialValue: initial.pieces.first?.id)
        if let first = initial.pieces.first {
            _pieceName = State(initialValue: first.name)
            _pieceX = State(initialValue: DraftPiece.display(first.minX))
            _pieceY = State(initialValue: DraftPiece.display(first.minY))
            _pieceWidth = State(initialValue: DraftPiece.display(first.lineLength))
            _pieceHeight = State(initialValue: DraftPiece.display(first.maxY - first.minY))
        }
    }

    private var selectedIndex: Int? {
        document.pieces.firstIndex(where: { $0.id == selectedID })
    }

    private var hasPendingDetails: Bool {
        if projectName != document.name { return true }
        guard let index = selectedIndex else { return false }
        let piece = document.pieces[index]
        return pieceName != piece.name ||
            pieceX != DraftPiece.display(piece.minX) ||
            pieceY != DraftPiece.display(piece.minY) ||
            pieceWidth != DraftPiece.display(piece.lineLength) ||
            pieceHeight != DraftPiece.display(piece.maxY - piece.minY)
    }
    private func select(_ piece: DraftPiece?) {
        selectedID = piece?.id
        pieceName = piece?.name ?? ""
        pieceX = piece.map { DraftPiece.display($0.minX) } ?? ""
        pieceY = piece.map { DraftPiece.display($0.minY) } ?? ""
        pieceWidth = piece.map { DraftPiece.display($0.lineLength) } ?? ""
        pieceHeight = piece.map { DraftPiece.display($0.maxY - $0.minY) } ?? ""
    }

    @discardableResult
    private func commit(_ updated: DraftProject, notice: String) -> Bool {
        guard updated != document else { return true }
        guard CADLocalDraftStore.isValid(updated) else {
            problem = "Değişiklik çizimin parça, nokta veya koordinat sınırlarını aşıyor."
            return false
        }
        guard persistDraft(updated) else { return false }
        undoStack.append(document)
        if undoStack.count > 50 { undoStack.removeFirst() }
        redoStack.removeAll()
        document = updated
        exportURL = nil
        success = notice
        return true
    }

    private func persistDraft(_ project: DraftProject) -> Bool {
        do {
            try CADLocalDraftStore.save(project)
            return true
        } catch {
            problem = "Taslak kaydedilemedi. " + error.localizedDescription
            return false
        }
    }

    private func addRectangle() {
        guard document.pieces.count < 250 else { problem = "Parça sınırına ulaşıldı."; return }
        var updated = document
        let start = Double(document.pieces.count) * 20
        let piece = DraftPiece(name: "Dikdörtgen \(document.pieces.count + 1)", closed: true, points: [
            DraftPoint(x: start, y: start), DraftPoint(x: start + 100, y: start),
            DraftPoint(x: start + 100, y: start + 150), DraftPoint(x: start, y: start + 150)
        ])
        updated.pieces.append(piece)
        guard commit(updated, notice: "Yeni dikdörtgen eklendi. Ölçüleri milimetre cinsindendir.") else { return }
        select(piece)
    }

    private func addTouchContour(_ piece: DraftPiece) {
        guard document.pieces.count < 250,
              document.pieces.reduce(0, { $0 + $1.points.count }) + piece.points.count <= 20_000 else {
            problem = "Çizim sınırı doldu. Yeni kontur eklenemedi."
            return
        }
        var updated = document
        updated.pieces.append(piece)
        guard commit(updated, notice: "Parmakla çizdiğiniz \(piece.points.count) noktalı kontur eklendi.") else { return }
        select(piece)
    }

    private func addLine() {
        guard document.pieces.count < 250 else { problem = "Parça sınırına ulaşıldı."; return }
        var updated = document
        let start = Double(document.pieces.count) * 20
        let piece = DraftPiece(name: "Çizgi \(document.pieces.count + 1)", closed: false, points: [
            DraftPoint(x: start, y: start), DraftPoint(x: start + 100, y: start)
        ])
        updated.pieces.append(piece)
        guard commit(updated, notice: "100 mm uzunluğunda bir çizgi eklendi.") else { return }
        select(piece)
    }

    @discardableResult
    private func saveDetails() -> Bool {
        let name = projectName.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !name.isEmpty, name.count <= 128 else {
            problem = "Proje adı 1–128 karakter arasında olmalı."
            return false
        }
        var updated = document
        updated.name = name
        if let index = selectedIndex {
            let selectedName = pieceName.trimmingCharacters(in: .whitespacesAndNewlines)
            let piece = updated.pieces[index]
            guard !selectedName.isEmpty, selectedName.count <= 128,
                  let resized = piece.applyingDimensions(x: pieceX, y: pieceY,
                                                         width: pieceWidth, height: pieceHeight) else {
                problem = "Parça adını, konumunu ve milimetre ölçülerini kontrol edin."
                return false
            }
            updated.pieces[index].points = resized
            updated.pieces[index].name = selectedName
        }
        guard commit(updated, notice: "Değişiklikler uygulandı.") else { return false }
        projectName = updated.name
        if let index = selectedIndex { select(updated.pieces[index]) }
        return true
    }

    private func move(dx: Double, dy: Double) {
        guard let index = selectedIndex else { return }
        var updated = document
        let points = updated.pieces[index].points.map { DraftPoint(x: $0.x + dx, y: $0.y + dy) }
        guard points.allSatisfy({ abs($0.x) <= 100_000_000 && abs($0.y) <= 100_000_000 }) else {
            problem = "Kalıp çalışma alanı sınırını aşıyor."; return
        }
        updated.pieces[index].points = points
        guard commit(updated, notice: "Parça taşındı.") else { return }
        select(document.pieces[index])
    }

    private func rotateSelected() {
        guard let index = selectedIndex else { return }
        var updated = document
        let piece = updated.pieces[index]
        let points = piece.rotatedQuarterTurn()
        guard points.allSatisfy({ abs($0.x) <= 100_000_000 && abs($0.y) <= 100_000_000 }) else {
            problem = "Döndürülmüş parça çalışma alanı sınırını aşıyor."; return
        }
        updated.pieces[index].points = points
        guard commit(updated, notice: "Parça 90° döndürüldü.") else { return }
        select(document.pieces[index])
    }

    private func duplicateSelected() {
        guard let index = selectedIndex else { return }
        var updated = document
        var copy = updated.pieces[index]
        copy.id = UUID()
        copy.name = String(copy.name.prefix(122)) + " kopya"
        copy.points = copy.points.map { DraftPoint(x: $0.x + 20, y: $0.y + 20) }
        guard copy.points.allSatisfy({ abs($0.x) <= 100_000_000 && abs($0.y) <= 100_000_000 }) else {
            problem = "Kopya çalışma alanı sınırını aşıyor."; return
        }
        updated.pieces.append(copy)
        guard commit(updated, notice: "Bağımsız bir parça kopyası oluşturuldu.") else { return }
        select(copy)
    }

    private func deleteSelected() {
        guard let index = selectedIndex else { return }
        var updated = document
        updated.pieces.remove(at: index)
        guard commit(updated, notice: "Parça silindi. Geri Al ile geri getirebilirsiniz.") else { return }
        select(updated.pieces.first)
    }

    private func undo() {
        guard let previous = undoStack.last, persistDraft(previous) else { return }
        undoStack.removeLast()
        redoStack.append(document)
        document = previous
        exportURL = nil
        select(document.pieces.first(where: { $0.id == selectedID }) ?? document.pieces.first)
        projectName = document.name
        success = "İşlem geri alındı."
    }

    private func redo() {
        guard let next = redoStack.last, persistDraft(next) else { return }
        redoStack.removeLast()
        undoStack.append(document)
        document = next
        exportURL = nil
        select(document.pieces.first(where: { $0.id == selectedID }) ?? document.pieces.first)
        projectName = document.name
        success = "İşlem yeniden uygulandı."
    }

    private func prepareExport() {
        guard !document.pieces.isEmpty else { problem = "Önce en az bir parça ekleyin."; return }
        do {
            let bytes = try CADSketchImport.encode(document)
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
                        Text("Çizimler cihazda otomatik saklanır • Üretim/kesim onayı değildir.")
                            .font(.caption).foregroundStyle(.secondary)
                    }

                    VStack(alignment: .leading, spacing: 10) {
                        Label("1 · Parça ekle", systemImage: "plus.square.on.square").font(.headline)
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
                        Button {
                            showingTouchComposer = true
                        } label: {
                            Label("Parmakla yeni kontur çiz", systemImage: "pencil.tip.crop.circle")
                                .frame(maxWidth: .infinity, minHeight: 50)
                        }
                        .buttonStyle(.bordered)
                    }

                    VStack(alignment: .leading, spacing: 8) {
                        Label("2 · Çizimin", systemImage: "pencil.tip").font(.headline)
                        preview
                        if !document.pieces.isEmpty {
                            Toggle("Sürüklerken 5 mm'ye hizala", isOn: $snapToFiveMillimeters)
                                .font(.caption)
                            Text("Bir parçaya dokunarak seç. Parmağınla sürükleyerek yerini değiştir.")
                                .font(.caption).foregroundStyle(.secondary)
                        }
                        if document.pieces.isEmpty {
                            Text("Çalışma alanın boş. Yukarıdan bir şekil seçerek başla.")
                                .foregroundStyle(.secondary)
                        } else {
                            Text("\(document.pieces.count) parça • Önizleme ölçüye göre ekrana sığdırılır")
                                .font(.caption).foregroundStyle(.secondary)
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
                                if let selectedIndex {
                                    if document.pieces[selectedIndex].closed {
                                        HStack {
                                            VStack(alignment: .leading) {
                                                Text("En (mm)").font(.caption)
                                                TextField("En", text: $pieceWidth).textFieldStyle(.roundedBorder)
                                            }
                                            VStack(alignment: .leading) {
                                                Text("Boy (mm)").font(.caption)
                                                TextField("Boy", text: $pieceHeight).textFieldStyle(.roundedBorder)
                                            }
                                        }
                                    } else {
                                        Text("Toplam çizgi uzunluğu (mm)").font(.caption)
                                        TextField("Uzunluk", text: $pieceWidth)
                                            .textFieldStyle(.roundedBorder)
                                    }
                                }
                                Button("Ölçüleri, konumu ve adı uygula") { _ = saveDetails() }
                                    .buttonStyle(.borderedProminent)
                                if let index = selectedIndex {
                                    if document.pieces[index].points.count <= DraftContourGeometry.pointLimit {
                                        Button("Kontur noktalarını düzenle") { editingPoints = document.pieces[index] }
                                            .buttonStyle(.bordered)
                                    } else {
                                        Text("Nokta düzenleme en fazla 250 noktalı konturlarda kullanılabilir.")
                                            .font(.caption)
                                    }
                                }
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
                        Button("Proje adını uygula") { _ = saveDetails() }
                            .buttonStyle(.bordered)
                        HStack {
                            Button("Geri al", action: undo).disabled(undoStack.isEmpty)
                            Button("İleri al", action: redo).disabled(redoStack.isEmpty)
                        }
                        .buttonStyle(.bordered)
                        Button {
                            guard saveDetails() else { return }
                            do {
                                try CADProjectLibrary.save(document)
                                success = "Proje kaydedildi. Ana ekrandaki Son projeler bölümünden açabilirsiniz."
                            } catch { problem = error.localizedDescription }
                        } label: {
                            Label("Projeyi kaydet", systemImage: "tray.and.arrow.down")
                                .frame(maxWidth: .infinity, minHeight: 48)
                        }
                        .buttonStyle(.borderedProminent)
                        Button { if saveDetails() { prepareExport() } } label: {
                            Label("Dosyayı hazırla (.tcad)", systemImage: "doc.badge.plus")
                                .frame(maxWidth: .infinity, minHeight: 48)
                        }
                        .buttonStyle(.borderedProminent)
                        if let exportURL, !hasPendingDetails {
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
                .controlSize(.large)
            }
            .navigationTitle("Kalıp atölyesi")
            .onAppear {
                if shouldStoreImportedSeed {
                    shouldStoreImportedSeed = false
                    do { try CADLocalDraftStore.save(document) }
                    catch { problem = "Kopya açıldı ancak cihazda saklanamadı. Kapatmadan önce dışa aktarın." }
                }
            }
            .sheet(item: $editingPoints) { piece in
                ContourPointEditor(piece: piece) { edited in
                    guard let index = document.pieces.firstIndex(where: { $0.id == edited.id }) else { return }
                    var updated = document
                    updated.pieces[index] = edited
                    if commit(updated, notice: "Kontur noktaları güncellendi.") { select(edited) }
                }
            }
            .sheet(isPresented: $showingTouchComposer) {
                TouchContourComposer { newPiece in
                    addTouchContour(newPiece)
                }
            }
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Kapat") { if saveDetails() { dismiss() } }
                }
            }
        }
        .interactiveDismissDisabled(hasPendingDetails)
        .alert("İşlem tamamlanamadı", isPresented: Binding(
            get: { problem != nil },
            set: { if !$0 { problem = nil } }
        )) {
            Button("Tamam", role: .cancel) { problem = nil }
        } message: {
            Text(problem ?? "")
        }
    }

    // Drawing, hit testing and finger movement share the same transform.
    private func transform(_ size: CGSize) -> DraftCanvasMap? {
        guard let first = document.pieces.first?.points.first, size.width > 40, size.height > 40 else { return nil }
        var minX = first.x, maxX = first.x, minY = first.y, maxY = first.y
        for piece in document.pieces {
            for p in piece.points {
                minX = min(minX, p.x); maxX = max(maxX, p.x)
                minY = min(minY, p.y); maxY = max(maxY, p.y)
            }
        }
        return DraftCanvasMap(size: size, minX: minX, maxX: maxX, minY: minY, maxY: maxY, inset: 16)
    }

    private func segmentDistance(_ p: CGPoint, _ a: CGPoint, _ b: CGPoint) -> Double {
        let dx = Double(b.x - a.x), dy = Double(b.y - a.y)
        let d2 = dx * dx + dy * dy
        let t = d2 < 1e-12 ? 0 : max(0, min(1,
            (Double(p.x - a.x) * dx + Double(p.y - a.y) * dy) / d2))
        return hypot(Double(p.x - a.x) - t * dx, Double(p.y - a.y) - t * dy)
    }

    private func inside(_ point: CGPoint, polygon: [CGPoint]) -> Bool {
        guard polygon.count >= 3 else { return false }
        var isInside = false
        var j = polygon.count - 1
        for i in polygon.indices {
            let a = polygon[i], b = polygon[j]
            if (a.y > point.y) != (b.y > point.y) {
                let edgeX = Double(b.x - a.x) * Double(point.y - a.y) /
                    Double(b.y - a.y) + Double(a.x)
                if Double(point.x) < edgeX { isInside.toggle() }
            }
            j = i
        }
        return isInside
    }

    private func hitPiece(_ point: CGPoint, size: CGSize) -> DraftPiece? {
        guard let transform = transform(size) else { return nil }
        var closest: (piece: DraftPiece, distance: Double)?
        for piece in document.pieces.reversed() {
            let vertices = piece.points.map(transform.screen)
            if piece.closed && inside(point, polygon: vertices) { return piece }
            guard vertices.count >= 2 else { continue }
            let count = piece.closed ? vertices.count : vertices.count - 1
            for index in 0..<count {
                let d = segmentDistance(point, vertices[index], vertices[(index + 1) % vertices.count])
                if closest == nil || d < closest!.distance { closest = (piece, d) }
            }
        }
        return (closest?.distance ?? Double.infinity) <= 24 ? closest?.piece : nil
    }

    private func finishDrag(_ translation: CGSize, size: CGSize) {
        defer { dragStarted = false; draggedPieceID = nil; currentDrag = .zero }
        guard let index = selectedIndex, document.pieces[index].id == draggedPieceID,
              let transform = transform(size) else { return }
        let delta = transform.translation(translation, for: document.pieces[index], snap: snapToFiveMillimeters)
        let dx = delta.x, dy = delta.y
        guard dx.isFinite, dy.isFinite, abs(dx) <= 100_000_000, abs(dy) <= 100_000_000 else {
            problem = "Sürükleme çalışma alanını aşıyor."
            return
        }
        if dx != 0 || dy != 0 { move(dx: dx, dy: dy) }
    }

    private var preview: some View {
        GeometryReader { geometry in
            Canvas { context, size in
                guard let mapper = transform(size) else { return }
                for piece in document.pieces {
                    guard let first = piece.points.first else { continue }
                    let delta = piece.id == draggedPieceID ? currentDrag : .zero
                    func position(_ p: DraftPoint) -> CGPoint {
                        let q = mapper.screen(p)
                        return CGPoint(x: q.x + delta.width, y: q.y + delta.height)
                    }
                    var path = Path()
                    path.move(to: position(first))
                    for p in piece.points.dropFirst() { path.addLine(to: position(p)) }
                    if piece.closed { path.closeSubpath() }
                    context.stroke(path,
                        with: .color(piece.id == selectedID ? .accentColor : .secondary),
                        lineWidth: piece.id == selectedID ? 3 : 1.8)
                }
            }
            .contentShape(Rectangle())
            .simultaneousGesture(SpatialTapGesture().onEnded { value in
                select(hitPiece(value.location, size: geometry.size))
            })
            .simultaneousGesture(DragGesture(minimumDistance: 8)
                .onChanged { value in
                    if !dragStarted {
                        dragStarted = true
                        let piece = hitPiece(value.startLocation, size: geometry.size)
                        draggedPieceID = piece?.id
                        if let piece { select(piece) }
                    }
                    if draggedPieceID != nil { currentDrag = value.translation }
                }
                .onEnded { value in
                    finishDrag(value.translation, size: geometry.size)
                })
        }
        .frame(height: 260)
        .frame(maxWidth: .infinity)
        .background(Color.secondary.opacity(0.08), in: RoundedRectangle(cornerRadius: 14))
        .accessibilityLabel("Dokunarak parça seçebileceğiniz kalıp önizlemesi")
        .accessibilityHint("Bir parçaya dokunarak seçin ve sürükleyerek taşıyın. Alternatif: Parça seç menüsü.")
    }
}
