import CoreFoundation
import Darwin
import SwiftUI
import UniformTypeIdentifiers

func isFilePickerCancellation(_ error: Error) -> Bool {
    let error = error as NSError
    return error.domain == NSCocoaErrorDomain && error.code == NSUserCancelledError
}

private struct ProjectExportDocument: FileDocument {
    static var readableContentTypes: [UTType] { [.data] }
    let data: Data
    init(data: Data) { self.data = data }
    init(configuration: ReadConfiguration) throws {
        guard let data = configuration.file.regularFileContents else { throw CocoaError(.fileReadCorruptFile) }
        self.data = data
    }
    func fileWrapper(configuration: WriteConfiguration) throws -> FileWrapper {
        FileWrapper(regularFileWithContents: data)
    }
}

@main
struct TekstilCADViewerApp: App {
    var body: some Scene {
        WindowGroup { ContentView() }
    }
}

private struct ContentView: View {
    @State private var showingImporter = false
    @State private var showingPieces = false
    @State private var showingFullScreenDrawing = false
    @State private var showingLargePiecePicker = false
    @State private var showingNotches = false
    @State private var selectedNotch: (piece: Int, point: Int)?
    @State private var showingLibrary = false
    @State private var showingExporter = false
    @State private var showingExportWarning = false
    @State private var exportDocument: ProjectExportDocument?
    @State private var exportName = "Model.tcad"
    @State private var importedData: Data?
    @State private var sourceData: Data?
    @State private var showingNameEditor = false
    @State private var editedName = ""
    @State private var showingPieceNameEditor = false
    #if DEBUG
    private struct PointEditSnapshot: Identifiable {
        let id = UUID()
        let data: Data
        let piece: Drawing.Piece
        let pieceID: UUID
        let index: Int
        let projectName: String
        let revision: UUID
        let loadRequest: UUID
    }
    @State private var pointEditSnapshot: PointEditSnapshot?
    #endif
    @State private var editedPieceName = ""
    @State private var pieceRenameSnapshot: PieceRenameSnapshot?
    @State private var importedName = ""
    @State private var showingLibraryNameEditor = false
    @State private var libraryName = ""
    @State private var librarySaveSnapshot: (data: Data, request: UUID)?
    @State private var isSaving = false
    @State private var saveNotice: String?
    @State private var project: Drawing?
    @State private var error: String?
    @State private var selectedPiece: Int?
    @State private var drawingRevision = UUID()
    @State private var notchSelectionRevision = UUID()
    @State private var modelLoad = ModelLoadState()

    var body: some View {
        ScrollView {
        VStack(spacing: 12) {
            HStack {
                Button(".tcad aç") { showingImporter = true }
                    .accessibilityHint("Önizlemek için bir proje dosyası seçin").disabled(isSaving)
                Button("Modellerim") { showingLibrary = true }.disabled(isSaving)
                Spacer()
                if let project { Text(project.name).font(.headline).lineLimit(1) }
            }
            Text("Proje adını yeni bir kopyada değiştirebilirsiniz. Pastal ve üretim çıktıları desteklenmez.")
                .font(.caption).foregroundStyle(.secondary)
            if modelLoad.isLoading { ProgressView("Proje açılıyor…") }
            if let saveNotice { Text(saveNotice).font(.caption).foregroundStyle(.secondary) }
            if importedData != nil {
                Button(isSaving ? "Kaydediliyor…" : "Bu modeli telefona kaydet") { saveImportedModel() }
                    .disabled(isSaving || modelLoad.isLoading)
                Button("Telefona farklı adla kaydet…") {
                    guard let data = importedData else { return }
                    librarySaveSnapshot = (data, modelLoad.request)
                    libraryName = project?.name ?? ""
                    showingLibraryNameEditor = true
                }
                .disabled(isSaving || modelLoad.isLoading)
            }
            if let project {
                Button("Açık modeli dışa aktar…") {
                    guard let data = sourceData else { return }
                    // Capture the loaded bytes before presenting the system picker.
                    exportDocument = ProjectExportDocument(data: data)
                    exportName = exportFileName(project.name)
                    saveNotice = nil
                    showingExportWarning = true
                }.disabled(isSaving || modelLoad.isLoading || sourceData == nil)
                Button("Proje adını değiştir ve kopya kaydet") {
                    showingNameEditor = true
                }.disabled(isSaving || modelLoad.isLoading || sourceData == nil || project.formatVersion == 6 || (sourceData?.count ?? 0) > LosslessProjectName.maximumEditBytes)
                if project.formatVersion == 6 {
                    Text("Sürüm 6 projelerinde proje adını değiştirip kopya kaydetme henüz desteklenmiyor. Açık modeli dışa aktarabilirsiniz.")
                        .font(.caption).foregroundStyle(.secondary)
                }
                if let sourceData, sourceData.count > LosslessProjectName.maximumEditBytes {
                    Text("Proje adını değiştirmek için dosya 8 MB veya daha küçük olmalı.")
                        .font(.caption).foregroundStyle(.secondary)
                }
                if project.pieces.contains(where: { !$0.hasMinimumContourPoints }) {
                    Text("\(project.pieces.filter { !$0.hasMinimumContourPoints }.count) parça asgari nokta sayısını karşılamıyor. Bu parçalar çizilmez veya ölçülmez; dosya masaüstünde açılamayabilir. Diğer dosya koşulları doğrulanmadı.")
                        .font(.caption).foregroundStyle(.secondary)
                }
                if project.pieces.contains(where: { ContourMeasure.isCollapsed($0.points) }) {
                    Text("Uyarı: \(project.pieces.filter { ContourMeasure.isCollapsed($0.points) }.count) parçanın tüm noktaları üst üste geliyor. Bu konturların geometrisini masaüstünde kontrol edin.")
                        .font(.caption).foregroundStyle(.orange)
                }
                if project.pieces.contains(where: { !$0.notches.isEmpty }) {
                    Text("Köşe işaretleri halkayla gösterilir; kesim derinliği veya yönü belirtilmez. Önizleme üretim çıktısı değildir.")
                        .font(.caption).foregroundStyle(.secondary)
                    Text("Sürüm 6 için yalnız UTF-8 ve en fazla 8 MB desteklenir. İşaretler ve konturlar önizlenir; tüm masaüstü proje kuralları doğrulanmaz.")
                        .font(.caption).foregroundStyle(.secondary)
                    Button("Köşe işaretlerini ara ve seç…") { showingNotches = true }
                        .disabled(modelLoad.isLoading)
                        .sheet(isPresented: $showingNotches) {
                            NotchPicker(pieces: project.pieces) { piece, point in
                                selectedPiece = piece
                                selectedNotch = (piece, point)
                                notchSelectionRevision = UUID()
                                showingNotches = false
                            }
                        }
                    if let selectedNotch, selectedNotch.piece == selectedPiece {
                        let piece = project.pieces[selectedNotch.piece]
                        let point = piece.points[selectedNotch.point]
                        let coordinates = NotchSearch.label(index: selectedNotch.point, x: point.x.formatted(), y: point.y.formatted())
                        Text(piece.hasMinimumContourPoints
                             ? "Seçili köşe işareti: \(coordinates). Önizlemede sarı halka ile gösterilir."
                             : "Seçili köşe işareti: \(coordinates). Konturda yeterli nokta olmadığı için önizleme gösterilemiyor.")
                            .font(.caption).foregroundStyle(.secondary)
                    }
                    if (selectedPiece.map { project.pieces[$0].notches.count } ?? project.pieces.reduce(0) { $0 + $1.notches.count }) > DrawingView.maximumVisibleNotches {
                        Text("Görünümde yalnız ilk 500 köşe işareti çiziliyor. Tüm işaretler kaynak dosyada korunur; parçaları ayrı seçerek inceleyin.")
                            .font(.caption).foregroundStyle(.secondary)
                    }
                }
                if !project.pieces.isEmpty {
                    Button { showingPieces = true } label: {
                        HStack {
                            VStack(alignment: .leading, spacing: 3) {
                                Text("Gösterilen parça").font(.caption).foregroundStyle(.secondary)
                                Text(selectedPiece.map { project.pieces[$0].displayName(index: $0) } ?? "Tüm parçalar")
                                    .font(.headline).lineLimit(1)
                            }
                            Spacer()
                            Image(systemName: "chevron.up.chevron.down").foregroundStyle(.secondary)
                        }
                        .frame(maxWidth: .infinity, minHeight: 52, alignment: .leading)
                        .contentShape(Rectangle())
                    }
                    .buttonStyle(.plain)
                    .disabled(modelLoad.isLoading)
                    .accessibilityHint("Parçaları aramak veya seçmek için açın")
                    .sheet(isPresented: $showingPieces) {
                        PiecePicker(pieces: project.pieces, selection: Binding(
                            get: { selectedPiece },
                            set: { newSelection in
                                if newSelection != selectedPiece { selectedNotch = nil }
                                selectedPiece = newSelection
                            }
                        ))
                    }
                    HStack {
                        Button("Önceki parça") {
                            guard let current = selectedPiece, current > 0 else { return }
                            selectedNotch = nil
                            selectedPiece = current - 1
                        }
                        .disabled(modelLoad.isLoading || selectedPiece == nil || selectedPiece == 0)
                        Spacer()
                        Text(selectedPiece.map { "\($0 + 1) / \(project.pieces.count)" } ?? "Tüm parçalar")
                            .font(.caption).foregroundStyle(.secondary)
                            .accessibilityLabel(selectedPiece.map { "Parça \($0 + 1), toplam \(project.pieces.count)" } ?? "Tüm parçalar gösteriliyor")
                        Spacer()
                        Button("Sonraki parça") {
                            let next = selectedPiece.map { $0 + 1 } ?? 0
                            guard next < project.pieces.count else { return }
                            selectedNotch = nil
                            selectedPiece = next
                        }
                        .disabled(modelLoad.isLoading || selectedPiece == project.pieces.count - 1)
                    }
                    .buttonStyle(.bordered)
                    if let selectedPiece, !project.pieces[selectedPiece].notches.isEmpty {
                        let notches = project.pieces[selectedPiece].notches
                        let current = selectedNotch?.piece == selectedPiece ? selectedNotch?.point : nil
                        HStack {
                            Button("Önceki işaret") {
                                if let point = NotchSearch.adjacent(in: notches, current: current, forward: false) {
                                    selectedNotch = (selectedPiece, point)
                                    notchSelectionRevision = UUID()
                                }
                            }
                            .disabled(modelLoad.isLoading || NotchSearch.adjacent(in: notches, current: current, forward: false) == nil)
                            Spacer()
                            Button("Sonraki işaret") {
                                if let point = NotchSearch.adjacent(in: notches, current: current, forward: true) {
                                    selectedNotch = (selectedPiece, point)
                                    notchSelectionRevision = UUID()
                                }
                            }
                            .disabled(modelLoad.isLoading || NotchSearch.adjacent(in: notches, current: current, forward: true) == nil)
                        }
                        .buttonStyle(.bordered)
                        .accessibilityHint("Seçili parçanın işaretleri arasında dosya sırasıyla gezinin")
                    }
                    if let selectedPiece {
                        #if DEBUG
                        Button("Deneysel nokta düzenle…") {
                            guard let data = sourceData, let id = project.pieces[selectedPiece].id else { return }
                            pointEditSnapshot = PointEditSnapshot(data: data, piece: project.pieces[selectedPiece], pieceID: id,
                                index: selectedPiece, projectName: project.name, revision: drawingRevision, loadRequest: modelLoad.request)
                        }
                            .disabled(isSaving || modelLoad.isLoading || sourceData == nil || project.pieces[selectedPiece].id == nil || project.pieces[selectedPiece].points.isEmpty || project.pieces[selectedPiece].points.count > 4096 || (sourceData?.count ?? 0) > 8 * 1024 * 1024)
                            .sheet(item: $pointEditSnapshot) { snapshot in
                                ExperimentalPointEditor(data: snapshot.data, piece: snapshot.piece, pieceID: snapshot.pieceID, projectName: snapshot.projectName,
                                    isCurrent: { sourceData == snapshot.data && self.selectedPiece == snapshot.index && drawingRevision == snapshot.revision && modelLoad.request == snapshot.loadRequest && !modelLoad.isLoading },
                                    invalidateForTesting: { modelLoad.invalidate() })
                            }
                        Text("Deneysel nokta düzenleme yalnız seçili, kimlikli ve en fazla 4096 noktalı parçada kullanılabilir; desteklenmeyen proje alanları reddedilir.")
                            .font(.caption).foregroundStyle(.secondary)
                        Button("Parça adını değiştir ve kopya kaydet") {
                            guard let data = sourceData, let name = project.pieces[selectedPiece].name else { return }
                            // Keep a failed edit available for retry on the same snapshot.
                            if pieceRenameSnapshot?.data != data || pieceRenameSnapshot?.index != selectedPiece {
                                editedPieceName = name
                            }
                            pieceRenameSnapshot = PieceRenameSnapshot(data: data, index: selectedPiece, name: name, id: project.pieces[selectedPiece].id, projectName: project.name)
                            showingPieceNameEditor = true
                        }
                        .disabled(isSaving || modelLoad.isLoading || sourceData == nil || (sourceData?.count ?? 0) > LosslessProjectName.maximumEditBytes || !project.pieceRenameIdentitiesValid)
                        if !project.pieceRenameIdentitiesValid {
                            Text("Parça adını değiştirmek için tüm parçaların adı ve varsa geçerli, benzersiz kimliği olmalı.")
                                .font(.caption).foregroundStyle(.secondary)
                        }
                        #endif
                        if project.pieces[selectedPiece].hasMinimumContourPoints {
                            PieceDetails(piece: project.pieces[selectedPiece])
                        }
                    }
                    if let selectedPiece, !project.pieces[selectedPiece].hasMinimumContourPoints {
                        Text("Bu parça asgari nokta sayısını karşılamıyor; kontur çizimi ve ölçüsü gösterilemiyor.")
                            .foregroundStyle(.secondary).frame(maxWidth: .infinity, maxHeight: .infinity)
                    } else if project.pieces.contains(where: { $0.hasMinimumContourPoints }) {
                        Button("Konturu büyük görünümde incele") { showingFullScreenDrawing = true }
                            .disabled(modelLoad.isLoading)
                        DrawingView(
                        pieces: selectedPiece.map { [project.pieces[$0]] } ?? project.pieces.filter { $0.hasMinimumContourPoints },
                        label: selectedPiece.map { "\(project.pieces[$0].displayName(index: $0)) kontur önizlemesi" } ?? "\(project.pieces.filter { $0.hasMinimumContourPoints }.count) parçalı kontur önizlemesi",
                        highlightedNotch: selectedNotch?.piece == selectedPiece ? selectedNotch?.point : nil,
                        selectionRevision: notchSelectionRevision
                        )
                        .id("\(drawingRevision)-\(selectedPiece.map(String.init) ?? "all")")
                        .background(Color.secondary.opacity(0.08))
                    } else {
                        Text("Bu projede görüntülenebilir kontur yok.")
                            .foregroundStyle(.secondary).frame(maxWidth: .infinity, maxHeight: .infinity)
                    }
                } else {
                    Text("Bu projede görüntülenebilir kontur yok.")
                        .foregroundStyle(.secondary)
                        .multilineTextAlignment(.center)
                        .frame(maxWidth: .infinity, maxHeight: .infinity)
                        .background(Color.secondary.opacity(0.08))
                }
                Text("\(project.pieces.count) parça • Ölçekli üretim çıktısı değildir")
                    .font(.caption).foregroundStyle(.secondary)
            } else {
                Spacer()
                Text("Önizlemek için bir .tcad dosyası açın.").foregroundStyle(.secondary)
                Spacer()
            }
        }
        .padding()
        }
        #if DEBUG
        .onChange(of: selectedPiece) { _ in pieceRenameSnapshot = nil }
        #endif
        .frame(minWidth: 300, minHeight: 300)
        .sheet(isPresented: $showingFullScreenDrawing) {
            if let project {
                NavigationStack {
                    GeometryReader { geometry in
                        ScrollView {
                            HStack {
                                Button("Önceki parça") {
                                    guard let current = selectedPiece, current > 0 else { return }
                                    selectedNotch = nil
                                    selectedPiece = current - 1
                                }
                                .disabled(modelLoad.isLoading || selectedPiece == nil || selectedPiece == 0)
                                Spacer()
                                Button { showingLargePiecePicker = true } label: {
                                    HStack(spacing: 4) {
                                        Text(selectedPiece.map { project.pieces[$0].displayName(index: $0) } ?? "Tüm parçalar")
                                            .font(.headline).lineLimit(2)
                                        Image(systemName: "chevron.up.chevron.down")
                                    }
                                }
                                .disabled(modelLoad.isLoading)
                                .accessibilityLabel("Büyük görünümde parça seç")
                                .accessibilityValue(selectedPiece.map { project.pieces[$0].displayName(index: $0) } ?? "Tüm parçalar")
                                Spacer()
                                Button("Sonraki parça") {
                                    let next = selectedPiece.map { $0 + 1 } ?? 0
                                    guard next < project.pieces.count else { return }
                                    selectedNotch = nil
                                    selectedPiece = next
                                }
                                .disabled(modelLoad.isLoading || selectedPiece == project.pieces.count - 1)
                            }
                            .buttonStyle(.bordered)
                            .padding(.horizontal)
                            if let selectedPiece {
                                Text(project.pieces[selectedPiece].selectionDetails(index: selectedPiece))
                                    .font(.caption)
                                    .foregroundStyle(.secondary)
                                    .frame(maxWidth: .infinity, alignment: .leading)
                                    .padding(.horizontal)
                                    .accessibilityIdentifier("largePreviewPieceDetails")
                            }
                            if let selectedPiece, !project.pieces[selectedPiece].notches.isEmpty {
                                let piece = project.pieces[selectedPiece]
                                let current = selectedNotch?.piece == selectedPiece ? selectedNotch?.point : nil
                                HStack {
                                    Button("Önceki işaret") {
                                        if let point = NotchSearch.adjacent(in: piece.notches, current: current, forward: false) {
                                            selectedNotch = (selectedPiece, point)
                                            notchSelectionRevision = UUID()
                                        }
                                    }
                                    .disabled(modelLoad.isLoading || NotchSearch.adjacent(in: piece.notches, current: current, forward: false) == nil)
                                    Spacer()
                                    Button("Sonraki işaret") {
                                        if let point = NotchSearch.adjacent(in: piece.notches, current: current, forward: true) {
                                            selectedNotch = (selectedPiece, point)
                                            notchSelectionRevision = UUID()
                                        }
                                    }
                                    .disabled(modelLoad.isLoading || NotchSearch.adjacent(in: piece.notches, current: current, forward: true) == nil)
                                }
                                .buttonStyle(.bordered)
                                .padding(.horizontal)
                                if let current {
                                    let point = piece.points[current]
                                    Text("Seçili işaret: \(NotchSearch.label(index: current, x: point.x.formatted(), y: point.y.formatted()))")
                                        .font(.caption).foregroundStyle(.secondary)
                                        .frame(maxWidth: .infinity, alignment: .leading)
                                        .padding(.horizontal)
                                        .accessibilityIdentifier("largePreviewNotchLabel")
                                }
                            }
                            if let selectedPiece, !project.pieces[selectedPiece].hasMinimumContourPoints {
                                Text("Bu parça asgari nokta sayısını karşılamıyor; kontur önizlemesi gösterilemiyor.")
                                    .foregroundStyle(.secondary).padding()
                            } else {
                            DrawingView(
                                pieces: selectedPiece.map { [project.pieces[$0]] } ?? project.pieces.filter { $0.hasMinimumContourPoints },
                                label: selectedPiece.map { "\(project.pieces[$0].displayName(index: $0)) kontur önizlemesi" } ?? "\(project.pieces.filter { $0.hasMinimumContourPoints }.count) parçalı kontur önizlemesi",
                                highlightedNotch: selectedNotch?.piece == selectedPiece ? selectedNotch?.point : nil,
                                selectionRevision: notchSelectionRevision,
                                drawingHeight: max(320, geometry.size.height - 180)
                            )
                            .id("\(drawingRevision)-\(selectedPiece.map(String.init) ?? "all")")
                            .padding()
                            }
                        }
                    }
                    .navigationTitle("Kontur inceleme")
                    .toolbar { ToolbarItem(placement: .cancellationAction) { Button("Kapat") { showingFullScreenDrawing = false } } }
                    .sheet(isPresented: $showingLargePiecePicker) {
                        PiecePicker(pieces: project.pieces, selection: Binding(
                            get: { selectedPiece },
                            set: { selection in
                                if selection != selectedPiece { selectedNotch = nil }
                                selectedPiece = selection
                            }
                        ))
                    }
                }
            }
        }
        .onChange(of: showingFullScreenDrawing) { presented in
            if !presented { showingLargePiecePicker = false }
        }
        .fileImporter(isPresented: $showingImporter, allowedContentTypes: [.item]) { result in
            do {
                let url = try result.get()
                guard url.pathExtension.lowercased() == "tcad" else {
                    modelLoad.invalidate()
                    throw ViewerError.message("Lütfen bir .tcad dosyası seçin.")
                }
                openModel(url, imported: true)
            } catch {
                if modelLoad.rejectPickerFailure(error) { self.error = error.localizedDescription }
            }
        }
        .fileExporter(isPresented: $showingExporter, document: exportDocument, contentType: .data, defaultFilename: exportName) { result in
            exportDocument = nil
            switch result {
            case .success: saveNotice = "Model seçilen hedefe dışa aktarıldı."
            case .failure(let failure):
                if !isFilePickerCancellation(failure) { error = failure.localizedDescription }
            }
        }
        .confirmationDialog("Yüklenmiş kopyayı dışa aktar", isPresented: $showingExportWarning, titleVisibility: .visible) {
            Button("Hedef seç") { showingExporter = true }
            Button("Vazgeç", role: .cancel) { exportDocument = nil }
        } message: {
            Text("Dışa aktarılan veri, dosyanın açıldığı andaki kopyasıdır. Kaynak başka yerde değişmiş olabilir. Kaynağı korumak için farklı bir dosya adı seçin; aynı hedef seçilirse sonraki değişiklikler üzerine yazılabilir.")
        }
        .onChange(of: showingExporter) { presented in
            // The system may dismiss without a completion callback on cancel.
            if !presented { exportDocument = nil }
        }
        .alert("Kayıtlı model adı", isPresented: $showingLibraryNameEditor) {
            TextField("Model adı", text: $libraryName)
            Button("İptal", role: .cancel) { librarySaveSnapshot = nil }
            Button("Telefona kaydet") { saveImportedModelWithName() }
                .disabled(libraryName.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty || libraryName.utf16.count > 1024)
        } message: {
            Text("Bu ad Modellerim listesinde görünür; dosyanın içindeki proje adı değişmez. En fazla 1024 UTF-16 birimi kullanın.")
        }
        .alert("Proje adını değiştir", isPresented: $showingNameEditor) {
            TextField("Proje adı", text: $editedName)
            Button("İptal", role: .cancel) { editedName = project?.name ?? "" }
            Button("Yeni kopyayı kaydet") { saveRenamedCopy() }
                .disabled(editedName.utf16.count > 1024)
        } message: { Text("Yeni bir model kaydedilir; açık dosya değişmez. Proje adı en fazla 1024 UTF-16 birimi olabilir.") }
        #if DEBUG
        .alert("Parça adını değiştir", isPresented: $showingPieceNameEditor) {
            TextField("Parça adı", text: $editedPieceName)
            Button("İptal", role: .cancel) { pieceRenameSnapshot = nil }
            Button("Yeni kopyayı kaydet") { saveRenamedPieceCopy() }
                .disabled(editedPieceName.utf16.count > 1024)
        } message: { Text("Yeni bir model kopyası kaydedilir; açık dosya değişmez. Kayıtlı pastal ve içe aktarma geçmişi eski parça adını veya önceki durumu gösterebilir. Parça adı en fazla 1024 UTF-16 birimi olabilir.") }
        #endif
        .sheet(isPresented: $showingLibrary) {
            ModelLibrary { entry in
                showingLibrary = false
                openModel(ModelStore.payloadURL(entry.url), imported: false, listedEntry: entry)
            }
        }
        #if DEBUG && targetEnvironment(simulator)
        .onAppear {
            guard (ProcessInfo.processInfo.arguments.contains("--ui-test-fixture") ||
                   ProcessInfo.processInfo.arguments.contains("--ui-test-library-fixture")),
                  let encoded = ProcessInfo.processInfo.environment["TCAD_UI_TEST_FIXTURE"],
                  let data = Data(base64Encoded: encoded), data.count <= 64 * 1024 else { return }
            let url = FileManager.default.temporaryDirectory
                .appendingPathComponent("ui-test-\(UUID().uuidString).tcad")
            defer { try? FileManager.default.removeItem(at: url) }
            do {
                try data.write(to: url, options: .atomic)
                let drawing = try Drawing.load(url)
                if ProcessInfo.processInfo.arguments.contains("--ui-test-oversized-metadata") {
                    let copy = try ModelStore.directory().appendingPathComponent("recovery.tcadcopy", isDirectory: true)
                    try FileManager.default.createDirectory(at: copy, withIntermediateDirectories: true)
                    try data.write(to: copy.appendingPathComponent("project.tcad"))
                    try Data(repeating: 65, count: 65_537).write(to: copy.appendingPathComponent("metadata.json"))
                }
                if ProcessInfo.processInfo.arguments.contains("--ui-test-corrupt-library-model") {
                    let corrupt = try ModelStore.directory().appendingPathComponent("Bozuk.tcad")
                    try Data([0xff, 0x00, 0x91]).write(to: corrupt)
                }
                selectedPiece = nil
                selectedNotch = nil
                drawingRevision = UUID()
                project = drawing
                editedName = drawing.name
                sourceData = data
                if ProcessInfo.processInfo.arguments.contains("--ui-test-library-fixture") {
                    importedData = data
                    importedName = ProcessInfo.processInfo.environment["TCAD_UI_TEST_FILENAME"] ?? "Deneme Modeli.tcad"
                }
            } catch { self.error = error.localizedDescription }
        }
        #endif
        .alert("İşlem tamamlanamadı", isPresented: Binding(get: { error != nil }, set: { if !$0 { error = nil } })) {
            Button("Tamam", role: .cancel) { error = nil }
        } message: { Text(error ?? "") }
    }

    private func exportFileName(_ name: String) -> String {
        let base = name.lowercased().hasSuffix(".tcad") ? String(name.dropLast(5)) : name
        var stem = ""
        for character in base {
            let part = character == "/" || character == ":" || character.unicodeScalars.contains(where: { CharacterSet.controlCharacters.contains($0) }) ? "_" : String(character)
            if (stem + part).utf8.count > 100 { break }
            stem += part
        }
        stem = stem.trimmingCharacters(in: .whitespacesAndNewlines)
        while stem.first == "." { stem.removeFirst() }
        return (stem.isEmpty ? "Model" : stem) + " - kopya.tcad"
    }

    private func openModel(_ url: URL, imported: Bool, listedEntry: ModelStore.Entry? = nil) {
        saveNotice = nil
        let request = modelLoad.begin()
        // Acquire the picker grant while still handling its result. Keep it
        // alive until the background coordinated read has finished.
        let access = imported && url.startAccessingSecurityScopedResource()
        DispatchQueue.global(qos: .userInitiated).async {
            defer { if access { url.stopAccessingSecurityScopedResource() } }
            #if DEBUG && targetEnvironment(simulator)
            if ProcessInfo.processInfo.arguments.contains("--ui-test-open-delay") { Thread.sleep(forTimeInterval: 3) }
            #endif
            let loaded = Result { () -> (Drawing, Data) in
                let data: Data
                if let listedEntry { data = try ModelStore.readListedPayload(listedEntry) }
                else { data = try Drawing.readData(url) }
                return (try Drawing.load(data), data)
            }
            DispatchQueue.main.async {
                guard modelLoad.finish(request) else { return }
                switch loaded {
                case .success(let (drawing, data)):
                    #if DEBUG
                    pieceRenameSnapshot = nil
                    #endif
                    showingPieces = false
                    showingNotches = false
                    selectedPiece = nil
                    selectedNotch = nil
                    drawingRevision = UUID()
                    project = drawing
                    editedName = drawing.name
                    sourceData = data
                    importedData = imported ? data : nil
                    importedName = url.lastPathComponent
                case .failure(let failure):
                    error = failure.localizedDescription
                }
            }
        }
    }

    private func saveRenamedCopy() {
        guard let data = sourceData, let currentProject = project, currentProject.formatVersion != 6 else { return }
        let name = editedName
        saveNotice = nil
        isSaving = true
        DispatchQueue.global(qos: .userInitiated).async {
            let result = Result {
                let copy = try LosslessProjectName.replacing(in: data, with: name)
                try ModelStore.saveRenamed(copy, projectName: name)
            }
            DispatchQueue.main.async {
                isSaving = false
                switch result {
                case .success:
                    editedName = project?.name ?? ""
                    saveNotice = "Yeni kopya Modellerim bölümüne kaydedildi."
                case .failure(let failure): error = failure.localizedDescription
                }
            }
        }
    }
    private struct PieceRenameSnapshot {
        let data: Data
        let index: Int
        let name: String
        let id: UUID?
        let projectName: String
    }

    private func saveRenamedPieceCopy() {
        guard let snapshot = pieceRenameSnapshot, sourceData == snapshot.data, selectedPiece == snapshot.index else {
            error = "Açık model veya seçili parça değişti. Parçayı yeniden seçin."
            return
        }
        let name = editedPieceName
        saveNotice = nil
        isSaving = true
        DispatchQueue.global(qos: .userInitiated).async {
            let result = Result {
                let copy: Data
                if let id = snapshot.id {
                    copy = try ExperimentalPieceRename.replacing(in: snapshot.data, snapshot: snapshot.data, pieceID: id, with: name)
                } else {
                    copy = try ExperimentalPieceRename.replacingLegacy(in: snapshot.data, snapshot: snapshot.data, pieceIndex: snapshot.index, expectedName: snapshot.name, with: name)
                }
                try ModelStore.saveRenamed(copy, projectName: "\(snapshot.projectName) – \(name)")
            }
            DispatchQueue.main.async {
                isSaving = false
                switch result {
                case .success:
                    pieceRenameSnapshot = nil
                    saveNotice = "Parça adı değiştirilmiş yeni kopya Modellerim bölümüne kaydedildi."
                case .failure:
                    error = "Parça adı güvenli biçimde değiştirilemedi. Dosya biçimini ve parça kimliklerini kontrol edin."
                }
            }
        }
    }

    private func saveImportedModelWithName() {
        guard let snapshot = librarySaveSnapshot else { return }
        librarySaveSnapshot = nil
        guard !modelLoad.isLoading, modelLoad.request == snapshot.request,
              importedData == snapshot.data else {
            error = "Açık model değişti. Kaydetmek istediğiniz modeli yeniden seçin."
            return
        }
        let name = libraryName.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !name.isEmpty, name.utf16.count <= 1024 else { return }
        saveNotice = nil
        isSaving = true
        DispatchQueue.global(qos: .userInitiated).async {
            let result = Result { try ModelStore.saveRenamed(snapshot.data, projectName: name) }
            DispatchQueue.main.async {
                isSaving = false
                switch result {
                case .success:
                    if modelLoad.request == snapshot.request && importedData == snapshot.data {
                        importedData = nil
                        saveNotice = "Model telefona kaydedildi."
                    }
                case .failure(let failure): error = failure.localizedDescription
                }
            }
        }
    }

    private func saveImportedModel() {
        guard let data = importedData else { return }
        let name = importedName
        let request = modelLoad.request
        saveNotice = nil
        isSaving = true
        DispatchQueue.global(qos: .userInitiated).async {
            let result = Result { try ModelStore.save(data, name: name) }
            DispatchQueue.main.async {
                isSaving = false
                switch result {
                case .success:
                    if modelLoad.request == request && importedData == data {
                        importedData = nil
                        saveNotice = "Model telefona kaydedildi."
                    }
                case .failure(let failure): error = failure.localizedDescription
                }
            }
        }
    }
}

enum ModelStore {
    struct FileIdentity: Equatable {
        let device: Int64, inode: UInt64, size: Int64, mode: UInt32
        let modifiedSeconds: Int64, modifiedNanoseconds: Int64
        let changedSeconds: Int64, changedNanoseconds: Int64
    }
    struct EntryIdentity: Equatable {
        let entry: FileIdentity
        let payload: FileIdentity?
        let metadata: FileIdentity?
    }

    struct Entry: Identifiable {
        let url: URL
        let name: String
        let date: Date?
        let canOpen: Bool
        let requiresRecovery: Bool
        let identity: EntryIdentity?
        var id: URL { url }
        // The complete directory entry name distinguishes records in this
        // library; actions continue to use the full URL, never a display ID.
        var displayIdentity: String { url.lastPathComponent }
        var readableIdentity: String {
            let stem = url.deletingPathExtension().lastPathComponent
            let candidate = stem.range(of: " - ", options: .backwards).map { String(stem[$0.upperBound...]) } ?? stem
            guard let uuid = UUID(uuidString: candidate) else { return displayIdentity }
            return "\(url.pathExtension.lowercased()) · \(uuid.uuidString)"
        }
    }
    private struct CopyMetadata: Codable {
        let version: Int
        let name: String
    }

    private static let copyExtension = "tcadcopy"
    private static let payloadName = "project.tcad"
    private static let metadataName = "metadata.json"
    #if DEBUG && targetEnvironment(simulator)
    private static let testDirectory: URL = {
        let url = FileManager.default.temporaryDirectory
            .appendingPathComponent("ui-test-library-fixture", isDirectory: true)
        if ProcessInfo.processInfo.arguments.contains("--ui-test-library-reset") {
            try? FileManager.default.removeItem(at: url)
        }
        return url
    }()
    #endif

    static func directory() throws -> URL {
        #if DEBUG && targetEnvironment(simulator)
        if ProcessInfo.processInfo.arguments.contains("--ui-test-library-fixture") {
            try FileManager.default.createDirectory(at: testDirectory, withIntermediateDirectories: true)
            return testDirectory
        }
        #endif
        let base = try FileManager.default.url(for: .applicationSupportDirectory, in: .userDomainMask,
                                               appropriateFor: nil, create: true)
        let directory = base.appendingPathComponent("Models", isDirectory: true)
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        return directory
    }

    static func save(_ data: Data, name: String) throws {
        guard data.count <= 64 * 1024 * 1024 else { throw ViewerError.message("Dosya 64 MB sınırını aşıyor.") }
        let stem = URL(fileURLWithPath: name).deletingPathExtension().lastPathComponent
        var safeStem = ""
        for character in stem.decomposedStringWithCanonicalMapping {
            let next = safeStem + String(character)
            if next.utf8.count > 100 { break }
            safeStem = next
        }
        while safeStem.first == "." { safeStem.removeFirst() }
        if safeStem.isEmpty { safeStem = "Model" }
        let directory = try directory()
        let destination = directory.appendingPathComponent("\(safeStem) - \(UUID().uuidString).tcad")
        let staging = directory.appendingPathComponent(".\(UUID().uuidString).partial")
        try ModelFilePublisher.publish(data, staging: staging, destination: destination)
    }

    static func saveRenamed(_ data: Data, projectName: String) throws {
        guard data.count <= 64 * 1024 * 1024 else { throw ViewerError.message("Dosya 64 MB sınırını aşıyor.") }
        let directory = try directory()
        let id = UUID().uuidString
        let staging = directory.appendingPathComponent(".\(id).partial", isDirectory: true)
        let destination = directory.appendingPathComponent("\(id).\(copyExtension)", isDirectory: true)
        // mkdir fails if the path already exists; createDirectory can silently
        // accept an existing directory that does not belong to this save.
        guard staging.path.withCString({ mkdir($0, 0o700) }) == 0 else {
            throw POSIXError(POSIXErrorCode(rawValue: errno) ?? .EIO)
        }
        // Do not recursively delete a potentially replaced path. A failed save
        // may leave its nonempty partial directory for inspection.
        defer { _ = staging.path.withCString { rmdir($0) } }
        try data.write(to: staging.appendingPathComponent(payloadName), options: .withoutOverwriting)
        let metadata = try JSONEncoder().encode(CopyMetadata(version: 1, name: projectName))
        try metadata.write(to: staging.appendingPathComponent(metadataName), options: .withoutOverwriting)
        try FileManager.default.moveItem(at: staging, to: destination)
    }

    #if DEBUG
    struct PreparedPointCopy {
        let staging: URL
        let destination: URL
    }
    static func preparePointCopy(_ data: Data, projectName: String) throws -> PreparedPointCopy {
        guard data.count <= 8 * 1024 * 1024 else { throw ViewerError.message("Dosya 8 MB sınırını aşıyor.") }
        let directory = try directory(), id = UUID().uuidString
        let staging = directory.appendingPathComponent(".\(id).partial", isDirectory: true)
        let destination = directory.appendingPathComponent("\(id).\(copyExtension)", isDirectory: true)
        guard staging.path.withCString({ mkdir($0, 0o700) }) == 0 else { throw POSIXError(POSIXErrorCode(rawValue: errno) ?? .EIO) }
        do {
            try data.write(to: staging.appendingPathComponent(payloadName), options: .withoutOverwriting)
            let metadata = try JSONEncoder().encode(CopyMetadata(version: 1, name: projectName))
            try metadata.write(to: staging.appendingPathComponent(metadataName), options: .withoutOverwriting)
            return PreparedPointCopy(staging: staging, destination: destination)
        } catch {
            _ = staging.path.withCString { rmdir($0) }
            throw error
        }
    }
    static func finishPointCopy(_ prepared: PreparedPointCopy) throws {
        try FileManager.default.moveItem(at: prepared.staging, to: prepared.destination)
    }
    static func discardPointCopy(_ prepared: PreparedPointCopy) throws {
        // The path may no longer refer to the directory we created. Retain the
        // unpublished partial rather than delete anyone else's files.
    }
    #endif

    static func payloadURL(_ url: URL) -> URL {
        url.pathExtension.lowercased() == copyExtension ? url.appendingPathComponent(payloadName) : url
    }

    private enum MetadataResult {
        case valid(CopyMetadata), oversized, invalid
    }

    private static func metadata(_ url: URL) -> MetadataResult {
        let limit = 64 * 1024
        guard url.pathExtension.lowercased() == copyExtension else { return .invalid }
        let metadataURL = url.appendingPathComponent(metadataName)
        // A damaged or substituted metadata path might be a FIFO. Opening it
        // normally could wait indefinitely before we even reach the size cap.
        let descriptor = metadataURL.path.withCString { open($0, O_RDONLY | O_NONBLOCK | O_NOFOLLOW) }
        guard descriptor >= 0 else { return .invalid }
        var info = stat()
        guard fstat(descriptor, &info) == 0, (info.st_mode & S_IFMT) == S_IFREG else {
            close(descriptor)
            return .invalid
        }
        let file = FileHandle(fileDescriptor: descriptor, closeOnDealloc: true)
        defer { try? file.close() }
        do {
            var data = Data()
            while data.count <= limit {
                let chunk = try file.read(upToCount: min(8192, limit + 1 - data.count)) ?? Data()
                if chunk.isEmpty { break }
                data.append(chunk)
            }
            if data.count > limit { return .oversized }
            try LosslessProjectName.validateAuxiliaryJSONKeys(data, limit: limit)
            guard let object = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
                  Set(object.keys) == Set(["version", "name"]),
                  let metadata = try? JSONDecoder().decode(CopyMetadata.self, from: data), metadata.version == 1 else { return .invalid }
            return .valid(metadata)
        } catch { return .invalid }
    }

    static func models() throws -> [Entry] {
        try models(in: directory())
    }

    static func models(in directory: URL) throws -> [Entry] {
        try FileManager.default.contentsOfDirectory(at: directory, includingPropertiesForKeys: nil,
                                                    options: [])
            .compactMap { url in
                let initialIdentity = identity(url)
                let name: String
                let canOpen: Bool
                let requiresRecovery: Bool
                if url.pathExtension.lowercased() == "tcad" {
                    let regular = initialIdentity.map { ($0.entry.mode & UInt32(S_IFMT)) == UInt32(S_IFREG) } ?? false
                    name = regular ? displayName(url) : "Dosya değil (\(url.lastPathComponent))"
                    canOpen = regular
                    requiresRecovery = false
                } else if url.pathExtension.lowercased() == copyExtension {
                    let directory = initialIdentity.map { ($0.entry.mode & UInt32(S_IFMT)) == UInt32(S_IFDIR) } ?? false
                    let hasPayload = directory && FileManager.default.fileExists(atPath: payloadURL(url).path)
                    switch (directory ? metadata(url) : .invalid, hasPayload) {
                    case (.valid(let metadata), true):
                        name = metadata.name.isEmpty ? "Model" : metadata.name; canOpen = true; requiresRecovery = false
                    case (.oversized, true), (.invalid, true):
                        var info = stat()
                        let payload = payloadURL(url)
                        let regular = payload.path.withCString { lstat($0, &info) } == 0 && (info.st_mode & S_IFMT) == S_IFREG
                        name = regular ? "Kurtarma gerekli (\(url.deletingPathExtension().lastPathComponent))" : "Okunamayan model (\(url.deletingPathExtension().lastPathComponent))"
                        canOpen = false; requiresRecovery = regular
                    default:
                        name = "Okunamayan model (\(url.deletingPathExtension().lastPathComponent))"
                        canOpen = false
                        requiresRecovery = false
                    }
                } else {
                    return nil
                }
                let finalIdentity = identity(url)
                guard let initialIdentity, initialIdentity == finalIdentity else {
                    return Entry(url: url, name: "Değişen kayıt (\(url.lastPathComponent))", date: nil,
                                 canOpen: false, requiresRecovery: false, identity: nil)
                }
                return Entry(url: url, name: name, date: savedDate(url), canOpen: canOpen, requiresRecovery: requiresRecovery, identity: initialIdentity)
            }
            .sorted { $0.name.localizedStandardCompare($1.name) == .orderedAscending }
    }

    static func savedDate(_ url: URL) -> Date? {
        try? url.resourceValues(forKeys: [.creationDateKey]).creationDate
    }

    static func filteredModels(_ entries: [Entry], query: String, newest: Bool,
                               cancelled: () -> Bool) -> [Entry]? {
        let words = query.split(whereSeparator: { $0.isWhitespace }).map(String.init)
        var matches: [Entry] = []
        for entry in entries {
            if cancelled() { return nil }
            let fields = [entry.name, entry.displayIdentity, entry.readableIdentity]
            if words.allSatisfy({ word in fields.contains { $0.localizedStandardContains(word) } }) {
                matches.append(entry)
            }
        }
        guard !cancelled() else { return nil }
        matches.sort { first, second in
            if newest {
                switch (first.date, second.date) {
                case let (left?, right?) where left != right: return left > right
                case (_?, nil): return true
                case (nil, _?): return false
                default: break
                }
            }
            let comparison = first.name.localizedStandardCompare(second.name)
            if comparison != .orderedSame { return comparison == .orderedAscending }
            return first.displayIdentity < second.displayIdentity
        }
        return cancelled() ? nil : matches
    }

    private static func fileIdentity(_ url: URL) -> FileIdentity? {
        var info = stat()
        guard url.path.withCString({ lstat($0, &info) }) == 0 else { return nil }
        return fileIdentity(info)
    }

    private static func fileIdentity(_ info: stat) -> FileIdentity {
        return FileIdentity(device: Int64(info.st_dev), inode: UInt64(info.st_ino), size: Int64(info.st_size), mode: UInt32(info.st_mode),
                            modifiedSeconds: Int64(info.st_mtimespec.tv_sec), modifiedNanoseconds: Int64(info.st_mtimespec.tv_nsec),
                            changedSeconds: Int64(info.st_ctimespec.tv_sec), changedNanoseconds: Int64(info.st_ctimespec.tv_nsec))
    }

    private static func identity(_ url: URL) -> EntryIdentity? {
        guard let entry = fileIdentity(url) else { return nil }
        let isCopy = url.pathExtension.lowercased() == copyExtension
        return EntryIdentity(entry: entry,
                             payload: isCopy ? fileIdentity(url.appendingPathComponent(payloadName)) : nil,
                             metadata: isCopy ? fileIdentity(url.appendingPathComponent(metadataName)) : nil)
    }

    static func requireCurrent(_ entry: Entry) throws {
        guard let expected = entry.identity, identity(entry.url) == expected else {
            throw ViewerError.message("Kayıt değişti veya kaldırıldı; model listesini yenileyip tekrar deneyin.")
        }
    }

    // Reject a row that was replaced or modified before or during the read.
    // Filesystem identity checks are not atomic with another writer.
    static func readListedPayload(_ entry: Entry, afterRead: (() throws -> Void)? = nil) throws -> Data {
        try requireCurrent(entry)
        let data = try Drawing.readData(payloadURL(entry.url))
        try afterRead?()
        try requireCurrent(entry)
        return data
    }

    static func delete(_ entry: Entry, in library: URL) throws {
        try requireCurrent(entry)
        try delete(entry.url, in: library)
    }

    static func delete(_ entry: Entry) throws {
        try delete(entry, in: directory())
    }

    static func delete(_ url: URL) throws {
        try delete(url, in: directory())
    }

    static func delete(_ url: URL, in library: URL, afterValidation: (() throws -> Void)? = nil) throws {
        let directory = library.standardizedFileURL
        guard url.standardizedFileURL.deletingLastPathComponent() == directory,
              ["tcad", copyExtension].contains(url.pathExtension.lowercased()) else {
            throw ViewerError.message("Model silinemedi.")
        }
        var rootInfo = stat()
        guard url.path.withCString({ lstat($0, &rootInfo) }) == 0 else {
            throw ViewerError.message("Kayıt bulunamadı; model listesini yenileyin.")
        }
        let expectedType = url.pathExtension.lowercased() == copyExtension ? S_IFDIR : S_IFREG
        guard (rootInfo.st_mode & S_IFMT) == expectedType else {
            throw ViewerError.message("Kayıt beklenen dosya türünde değil; içeriği korumak için silinmedi.")
        }
        if url.pathExtension.lowercased() == "tcad" {
            try afterValidation?()
            // Unlike recursive removal, unlink cannot recursively remove a
            // directory substituted after the type check.
            guard url.path.withCString({ Darwin.unlink($0) }) == 0 else {
                throw POSIXError(POSIXErrorCode(rawValue: errno) ?? .EIO)
            }
            return
        }
        if url.pathExtension.lowercased() == copyExtension {
            let names = try FileManager.default.contentsOfDirectory(atPath: url.path)
            guard Set(names).isSubset(of: [payloadName, metadataName]) else {
                throw ViewerError.message("Bu kayıtta tanınmayan dosyalar var; kayıp olmaması için silinmedi.")
            }
            var children: [String: FileIdentity] = [:]
            for name in names {
                let child = url.appendingPathComponent(name)
                guard let identity = fileIdentity(child),
                      (identity.mode & UInt32(S_IFMT)) == UInt32(S_IFREG) else {
                    throw ViewerError.message("Bu kaydın içeriği güvenle silinemedi.")
                }
                children[name] = identity
            }
            let descriptor = url.path.withCString { Darwin.open($0, O_RDONLY | O_DIRECTORY | O_NOFOLLOW) }
            guard descriptor >= 0 else { throw POSIXError(POSIXErrorCode(rawValue: errno) ?? .EIO) }
            defer { close(descriptor) }
            var opened = stat()
            guard fstat(descriptor, &opened) == 0,
                  opened.st_dev == rootInfo.st_dev, opened.st_ino == rootInfo.st_ino else {
                throw ViewerError.message("Kayıt değişti; listeyi yenileyin.")
            }
            try afterValidation?()
            // Recheck before deleting anything. This is not an atomic lock,
            // so the removal below is deliberately never recursive.
            var currentRoot = stat()
            guard url.path.withCString({ lstat($0, &currentRoot) }) == 0,
                  currentRoot.st_dev == opened.st_dev, currentRoot.st_ino == opened.st_ino,
                  Set(try FileManager.default.contentsOfDirectory(atPath: url.path)) == Set(names) else {
                throw ViewerError.message("Kayıt içeriği değişti; hiçbir dosya silinmedi.")
            }
            for name in names {
                var current = stat()
                guard name.withCString({ fstatat(descriptor, $0, &current, AT_SYMLINK_NOFOLLOW) }) == 0,
                      fileIdentity(current) == children[name] else {
                    throw ViewerError.message("Kayıt içeriği değişti; hiçbir dosya silinmedi.")
                }
            }
            // Remove only the two known regular-file names from the opened
            // directory. Unknown/new entries are never traversed or removed.
            for name in [metadataName, payloadName] where children[name] != nil {
                guard name.withCString({ unlinkat(descriptor, $0, 0) }) == 0 else {
                    throw ViewerError.message("Kayıt tamamen silinemedi; bazı kayıt dosyaları silinmiş olabilir.")
                }
            }
            guard url.path.withCString({ lstat($0, &currentRoot) }) == 0,
                  currentRoot.st_dev == opened.st_dev, currentRoot.st_ino == opened.st_ino,
                  url.path.withCString({ rmdir($0) }) == 0 else {
                throw ViewerError.message("Kayıt klasörü silinemedi; bazı kayıt dosyaları silinmiş olabilir.")
            }
            return
        }
    }

    static func displayName(_ url: URL) -> String {
        let stem = url.deletingPathExtension().lastPathComponent
        guard let separator = stem.range(of: " - ", options: .backwards),
              UUID(uuidString: String(stem[separator.upperBound...])) != nil else { return stem }
        return String(stem[..<separator.lowerBound])
    }
}

private struct ModelLibrary: View {
    let open: (ModelStore.Entry) -> Void
    @Environment(\.dismiss) private var dismiss
    @State private var models: [ModelStore.Entry] = []
    @State private var modelsRevision = UUID()
    @State private var search = ""
    @State private var sortOrder: SortOrder = .name
    @State private var error: String?
    @State private var modelToDelete: ModelStore.Entry?
    @State private var modelToRecover: ModelStore.Entry?
    @State private var recoveryDocument: ProjectExportDocument?
    @State private var showingRecoveryExporter = false
    @State private var isRecovering = false
    @State private var recoveryID = UUID()
    @State private var isLoading = true
    @State private var loadFailed = false
    @State private var loadID = UUID()
    #if DEBUG && targetEnvironment(simulator)
    @State private var didSimulateLoadFailure = false
    #endif

    private enum SortOrder: String, CaseIterable, Identifiable {
        case name = "Ada göre"
        case newest = "En yeni önce"
        var id: String { rawValue }
    }

    private struct FilterKey: Hashable {
        let query: String
        let newest: Bool
        let revision: UUID
    }
    private struct FilterResult { let key: FilterKey; let entries: [ModelStore.Entry] }
    @State private var filterResult: FilterResult?
    private var filterKey: FilterKey { FilterKey(query: search, newest: sortOrder == .newest, revision: modelsRevision) }

    var body: some View {
        NavigationStack {
            let current = filterResult?.key == filterKey ? filterResult : nil
            let matchingModels = current?.entries ?? []
            List {
                if isLoading {
                    ProgressView("Modeller yükleniyor…")
                } else if isRecovering {
                    ProgressView("Ham kopya hazırlanıyor…")
                } else if loadFailed {
                    VStack(alignment: .leading, spacing: 8) {
                        Text("Kayıtlı modeller yüklenemedi.").foregroundStyle(.secondary)
                        Button("Tekrar dene") { loadModels() }
                    }
                } else if models.isEmpty {
                    Text("Henüz kayıtlı model yok. Bir .tcad açıp telefona kaydedebilirsiniz.")
                        .foregroundStyle(.secondary)
                } else if current == nil {
                    ProgressView("Modeller aranıyor…")
                } else if matchingModels.isEmpty {
                    VStack(alignment: .leading, spacing: 8) {
                        Text("Eşleşen model bulunamadı.").foregroundStyle(.secondary)
                        Button("Aramayı temizle") { search = "" }
                    }
                }
                ForEach(matchingModels) { model in
                    HStack {
                    Button {
                        if model.canOpen { open(model) }
                        else if model.requiresRecovery { modelToRecover = model }
                        else if model.identity == nil { error = "Kayıt listelenirken değişti. Listeyi kapatıp yeniden açın." }
                        else { error = "Bu kayıt açılamıyor. Kayıtlı dosyayı kontrol edin." }
                    } label: {
                        VStack(alignment: .leading) {
                            Text(model.name)
                            Text("Kayıt: \(model.readableIdentity)")
                                .font(.caption).foregroundStyle(.secondary).lineLimit(2)
                            if let date = model.date {
                                Text("Kaydedildi: \(date.formatted(date: .abbreviated, time: .standard))")
                                    .font(.caption).foregroundStyle(.secondary)
                            }
                        }
                        .frame(maxWidth: .infinity, minHeight: 44, alignment: .leading)
                    }
                    .accessibilityElement(children: .combine)
                    .accessibilityLabel(model.name)
                    .accessibilityValue("Kayıt kimliği: \(model.displayIdentity)" + (model.date.map { ", Kaydedildi: \($0.formatted(date: .abbreviated, time: .standard))" } ?? ""))
                    .buttonStyle(.plain)
                    .disabled(isRecovering)
                    Menu {
                        if model.canOpen || model.requiresRecovery {
                            Button("Ham kopyayı dışa aktar…") { modelToRecover = model }
                        }
                        Button("Kayıtlı modeli sil", role: .destructive) { modelToDelete = model }
                    } label: {
                        Image(systemName: "ellipsis.circle")
                            .frame(width: 44, height: 44)
                    }
                    .accessibilityLabel("\(model.name) model işlemleri")
                    .accessibilityValue("Kayıt kimliği: \(model.displayIdentity)")
                    .disabled(isRecovering)
                    }
                    .swipeActions {
                        Button("Sil", role: .destructive) { modelToDelete = model }.disabled(isRecovering)
                    }
                }
            }
            .searchable(text: $search, prompt: "Model ara")
            .navigationTitle("Modellerim")
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("Kapat") { dismiss() } }
                ToolbarItem(placement: .primaryAction) {
                    Menu {
                        Picker("Sıralama", selection: $sortOrder) {
                            ForEach(SortOrder.allCases) { order in
                                Text(order.rawValue).tag(order)
                            }
                        }
                    } label: {
                        Label("Sırala", systemImage: "arrow.up.arrow.down")
                    }
                    .accessibilityLabel("Modelleri sırala")
                }
            }
            .onAppear { loadModels() }
            .onDisappear { loadID = UUID(); recoveryID = UUID(); isRecovering = false }
            .alert("Ham kopyayı kurtar?", isPresented: Binding(get: { modelToRecover != nil }, set: { if !$0 { modelToRecover = nil } })) {
                Button("İptal", role: .cancel) { modelToRecover = nil }
                Button("Ham kopyayı dışa aktar") {
                    guard let model = modelToRecover else { return }
                    modelToRecover = nil
                    prepareRecovery(model)
                }
            } message: {
                if modelToRecover?.requiresRecovery == true {
                    Text("Kayıt bilgisi eksik, bozuk, çok büyük veya desteklenmeyen sürümde; biçimi ve sürümü doğrulanamadı. Proje dosyası açılmadan veya değiştirilmeden dışa aktarılır; ham veri bozuk olabilir ve geçerli bir proje olduğu doğrulanmaz. Kayıt bilgisi kopyaya dahil edilmez. En fazla 64 MB desteklenir.")
                } else {
                    Text("Dosya içeriği açılmadan veya değiştirilmeden dışa aktarılır. Ham veri bozuk olabilir ve geçerli bir proje olduğu doğrulanmaz. Kayıt bilgisi varsa kopyaya dahil edilmez. En fazla 64 MB desteklenir.")
                }
            }
            .fileExporter(isPresented: $showingRecoveryExporter, document: recoveryDocument, contentType: .data, defaultFilename: "Kurtarılan-model.tcad") { result in
                recoveryDocument = nil
                if case .failure(let failure) = result, !isFilePickerCancellation(failure) { error = failure.localizedDescription }
            }
            .onChange(of: showingRecoveryExporter) { presented in
                if !presented { recoveryDocument = nil }
            }
            .alert("Modeli sil?", isPresented: Binding(get: { modelToDelete != nil }, set: { if !$0 { modelToDelete = nil } })) {
                Button("İptal", role: .cancel) { modelToDelete = nil }
                Button("Sil", role: .destructive) {
                    guard let model = modelToDelete else { return }
                    do {
                        try ModelStore.delete(model)
                        models.removeAll { $0.id == model.id }
                        modelsRevision = UUID()
                    } catch { self.error = error.localizedDescription }
                    modelToDelete = nil
                }
            } message: {
                Text("\(modelToDelete?.name ?? "Model")\nKayıt: \(modelToDelete?.readableIdentity ?? "")\nTam kayıt kimliği: \(modelToDelete?.displayIdentity ?? "")\nTelefonunuzdaki kayıtlı modellerden silinecek. Orijinal dosya etkilenmez.")
            }
            .alert("İşlem tamamlanamadı", isPresented: Binding(get: { error != nil }, set: { if !$0 { error = nil } })) {
                Button("Tamam", role: .cancel) { error = nil }
            } message: { Text(error ?? "") }
            .task(id: filterKey) {
                let request = filterKey
                let snapshot = models
                if !request.query.isEmpty {
                    do { try await Task.sleep(nanoseconds: 150_000_000) } catch { return }
                }
                guard !Task.isCancelled else { return }
                let worker = Task.detached(priority: .userInitiated) {
                    ModelStore.filteredModels(snapshot, query: request.query, newest: request.newest, cancelled: { Task.isCancelled })
                }
                let entries = await withTaskCancellationHandler(operation: { await worker.value }, onCancel: { worker.cancel() })
                guard !Task.isCancelled, filterKey == request, let entries else { return }
                filterResult = FilterResult(key: request, entries: entries)
            }
        }
    }

    private func prepareRecovery(_ model: ModelStore.Entry) {
        let request = UUID()
        recoveryID = request
        isRecovering = true
        DispatchQueue.global(qos: .userInitiated).async {
            let result = Result { try ModelStore.readListedPayload(model) }
            DispatchQueue.main.async {
                guard recoveryID == request else { return }
                isRecovering = false
                switch result {
                case .success(let data):
                    recoveryDocument = ProjectExportDocument(data: data)
                    showingRecoveryExporter = true
                case .failure(let failure): error = failure.localizedDescription
                }
            }
        }
    }

    private func loadModels() {
        let requestID = UUID()
        loadID = requestID
        isLoading = true
        loadFailed = false
        models = []
        modelsRevision = UUID()
        #if DEBUG && targetEnvironment(simulator)
        var simulateFailure = false
        if ProcessInfo.processInfo.arguments.contains("--ui-test-library-fixture"),
           ProcessInfo.processInfo.arguments.contains("--ui-test-library-fail-once"),
           !didSimulateLoadFailure {
            didSimulateLoadFailure = true
            simulateFailure = true
        }
        let shouldSimulateFailure = simulateFailure
        #endif
        DispatchQueue.global(qos: .userInitiated).async {
            let result = Result {
                #if DEBUG && targetEnvironment(simulator)
                if shouldSimulateFailure { throw ViewerError.message("Test yükleme hatası.") }
                #endif
                return try ModelStore.models()
            }
            DispatchQueue.main.async {
                guard loadID == requestID else { return }
                isLoading = false
                switch result {
                case .success(let entries): models = entries; modelsRevision = UUID()
                case .failure(let failure):
                    loadFailed = true
                    error = failure.localizedDescription
                }
            }
        }
    }
}

private enum ViewerError: LocalizedError {
    case message(String)
    var errorDescription: String? { if case let .message(text) = self { return text }; return nil }
}

private struct Drawing {
    typealias Point = ContourMeasure.Point
    struct Grain { let start: Point; let end: Point; let needsReview: Bool }
    struct Piece {
        let name: String?
        let id: UUID?
        let points: [Point]
        let closed: Bool
        let contourLength: Double?
        let role: String?
        let size: String?
        let material: String?
        let quantity: Int?
        let grainStatus: String
        let grain: Grain?
        let notches: [Int]
        var hasMinimumContourPoints: Bool { points.count >= (closed ? 3 : 2) }

        func displayName(index: Int) -> String {
            guard let name, !name.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
                return "Parça \(index + 1)"
            }
            return name
        }

        func selectionDetails(index: Int) -> String {
            let fields = [("Beden", size), ("Tür", role), ("Malzeme", material)].compactMap { label, value -> String? in
                guard let value = value?.trimmingCharacters(in: .whitespacesAndNewlines), !value.isEmpty else { return nil }
                return "\(label): \(value)"
            }
            let warning = hasMinimumContourPoints ? [] : ["Kontur için nokta sayısı yetersiz: \(points.count)"]
            return (["Parça \(index + 1)"] + fields + warning + (notches.isEmpty ? [] : ["\(notches.count) köşe işareti"])).joined(separator: " · ")
        }
    }
    let name: String
    let pieces: [Piece]
    let pieceRenameIdentitiesValid: Bool
    let formatVersion: Int

    // JSONSerialization bridges both JSON booleans and numbers through NSNumber.
    // Check the underlying type before conversion so malformed files cannot turn
    // true/false into coordinates or numeric values into contour flags.
    private static func jsonNumber(_ value: Any?) -> Double? {
        guard let number = value as? NSNumber,
              CFGetTypeID(number) != CFBooleanGetTypeID() else { return nil }
        return number.doubleValue
    }

    private static func jsonBoolean(_ value: Any?) -> Bool? {
        guard let number = value as? NSNumber,
              CFGetTypeID(number) == CFBooleanGetTypeID() else { return nil }
        return number.boolValue
    }

    // JSONSerialization does not promise to preserve a number's spelling.
    // Examine the first array element in the UTF-8 source before interpreting it.
    private static func envelopeVersion(_ data: Data) -> Int? {
        // Desktop accepts BOM-marked UTF-8, UTF-16 and UTF-32 project files.
        // Inspect decoded text only; retain the original bytes for saving.
        let encoding: String.Encoding
        if data.starts(with: [0xff, 0xfe, 0x00, 0x00]) { encoding = .utf32LittleEndian }
        else if data.starts(with: [0x00, 0x00, 0xfe, 0xff]) { encoding = .utf32BigEndian }
        else if data.starts(with: [0xff, 0xfe]) { encoding = .utf16LittleEndian }
        else if data.starts(with: [0xfe, 0xff]) { encoding = .utf16BigEndian }
        else { encoding = .utf8 }
        guard var text = String(data: data, encoding: encoding) else { return nil }
        if text.first == "\u{FEFF}" { text.removeFirst() }
        var bytes = text.utf8.makeIterator()
        func nextNonWhitespace() -> UInt8? {
            while let byte = bytes.next() {
                if byte != 0x20 && byte != 0x09 && byte != 0x0a && byte != 0x0d { return byte }
            }
            return nil
        }
        guard nextNonWhitespace() == 0x5b, let digit = nextNonWhitespace(),
              (0x31...0x36).contains(digit), nextNonWhitespace() == 0x2c else { return nil }
        return Int(digit - 0x30)
    }
    static func load(_ url: URL) throws -> Drawing { try load(readData(url)) }

    static func readData(_ url: URL) throws -> Data {
        // File providers can replace or materialize a document while it is
        // being read. Capture one coordinated snapshot before parsing it.
        let coordinator = NSFileCoordinator(filePresenter: nil)
        var coordinationError: NSError?
        var snapshot: Result<Data, Error>?
        coordinator.coordinate(readingItemAt: url, options: [], error: &coordinationError) { coordinatedURL in
            snapshot = Result { try readCoordinatedData(coordinatedURL) }
        }
        if let coordinationError { throw coordinationError }
        guard let snapshot else { throw ViewerError.message("Dosya okuması tamamlanamadı.") }
        return try snapshot.get()
    }

    private static func readCoordinatedData(_ url: URL) throws -> Data {
        let limit = 64 * 1024 * 1024
        // A FIFO named .tcad can block indefinitely when opened normally.
        // Open without waiting and validate the opened object before reading.
        let descriptor = url.path.withCString { Darwin.open($0, O_RDONLY | O_NONBLOCK) }
        guard descriptor >= 0 else { throw POSIXError(POSIXErrorCode(rawValue: errno) ?? .EIO) }
        var info = stat()
        guard fstat(descriptor, &info) == 0, (info.st_mode & S_IFMT) == S_IFREG else {
            close(descriptor)
            throw ViewerError.message("Yalnızca normal proje dosyaları açılabilir.")
        }
        let file = FileHandle(fileDescriptor: descriptor, closeOnDealloc: true)
        defer { try? file.close() }
        var data = Data()
        while true {
            let remaining = limit + 1 - data.count
            let chunk = try file.read(upToCount: min(64 * 1024, remaining)) ?? Data()
            if chunk.isEmpty { break }
            data.append(chunk)
            guard data.count <= limit else { throw ViewerError.message("Dosya 64 MB sınırını aşıyor.") }
        }
        return data
    }

    static func load(_ data: Data) throws -> Drawing {
        guard data.count <= 64 * 1024 * 1024 else { throw ViewerError.message("Dosya 64 MB sınırını aşıyor.") }
        let root = try JSONSerialization.jsonObject(with: data)
        let object: [String: Any]
        let formatVersion: Int
        if let array = root as? [Any], array.count == 2,
           let version = envelopeVersion(data), let body = array[1] as? [String: Any] {
            object = body; formatVersion = version
        } else if let body = root as? [String: Any] { object = body; formatVersion = 0 }
        else { throw ViewerError.message("Desteklenmeyen proje biçimi.") }
        if formatVersion == 6 {
            guard data.count <= LosslessProjectName.maximumEditBytes else { throw ViewerError.message("Sürüm 6 önizlemesi için dosya en fazla 8 MB olmalı.") }
            do { try LosslessProjectName.validateUniqueKeys(data) }
            catch { throw ViewerError.message("Sürüm 6 dosyası güvenli biçimde okunamadı; alanları ve UTF-8 kodlamasını kontrol edin.") }
        } else {
            // JSONSerialization keeps only one value for a repeated key, while
            // desktop rejects duplicates. Scan decoded bytes but keep the
            // original snapshot untouched for saving and export.
            let encoding: String.Encoding
            if data.starts(with: [0xff, 0xfe, 0x00, 0x00]) { encoding = .utf32LittleEndian }
            else if data.starts(with: [0x00, 0x00, 0xfe, 0xff]) { encoding = .utf32BigEndian }
            else if data.starts(with: [0xff, 0xfe]) { encoding = .utf16LittleEndian }
            else if data.starts(with: [0xfe, 0xff]) { encoding = .utf16BigEndian }
            else { encoding = .utf8 }
            guard let decoded = String(data: data, encoding: encoding) else { throw ViewerError.message("Proje metni okunamadı.") }
            do { try LosslessProjectName.validateUniqueKeys(Data(decoded.utf8), strictV6: false) }
            catch { throw ViewerError.message("Proje alanları yinelenmiş veya güvenli biçimde okunamadı.") }
        }
        guard let name = object["Name"] as? String, let rawPieces = object["Pieces"] as? [[String: Any]], rawPieces.count <= 10_000 else {
            throw ViewerError.message("Proje adı veya parça listesi eksik ya da sınır dışında.")
        }
        var total = 0
        var totalNotches = 0
        var pieces: [Piece] = []
        var pieceRenameIdentitiesValid = true
        var pieceIDs = Set<UUID>()
        for raw in rawPieces {
            guard let rawPoints = raw["Points"] as? [[String: Any]], rawPoints.count <= 10_000,
                  let closed = jsonBoolean(raw["Closed"]) else { throw ViewerError.message("Parça konturu eksik veya sınır dışında.") }
            total += rawPoints.count
            guard total <= 200_000 else { throw ViewerError.message("Toplam nokta sınırı aşıldı.") }
            let points: [Point] = try rawPoints.map { point in
                guard let x = jsonNumber(point["X"]), let y = jsonNumber(point["Y"]),
                      x.isFinite, y.isFinite, abs(x) < 1e100, abs(y) < 1e100,
                      (point["IsFinite"] == nil || jsonBoolean(point["IsFinite"]) == true) else {
                    throw ViewerError.message("Geçersiz veya gösterilemeyen koordinat.")
                }
                return Point(x: x, y: y)
            }
            var notchIndices: [Int] = []
            if let value = raw["Notches"] {
                guard formatVersion == 6, let marks = value as? [[String: Any]], !marks.isEmpty, marks.count <= 10_000 else { throw ViewerError.message("Köşe işareti listesi veya sürümü geçersiz.") }
                totalNotches += marks.count
                guard totalNotches <= 200_000 else { throw ViewerError.message("Köşe işareti sayısı sınırı aşıldı.") }
                var seen = Set<Int>()
                for mark in marks {
                    guard let n = mark["VertexIndex"] as? NSNumber, CFGetTypeID(n) != CFBooleanGetTypeID(),
                          !["f", "d"].contains(String(cString: n.objCType)), n.doubleValue >= 0, n.doubleValue < Double(points.count),
                          let position = mark["Position"] as? [String: Any], let x = jsonNumber(position["X"]), let y = jsonNumber(position["Y"]),
                          x.isFinite, y.isFinite, abs(x) <= 100_000_000, abs(y) <= 100_000_000,
                          (position["IsFinite"] == nil || jsonBoolean(position["IsFinite"]) == true) else { throw ViewerError.message("Köşe işareti konumu veya indeksi geçersiz.") }
                    let index = n.intValue
                    guard seen.insert(index).inserted, points[index].x == x, points[index].y == y,
                          !(closed && points.count > 1 && points.first!.x == points.last!.x && points.first!.y == points.last!.y && index == points.count - 1) else { throw ViewerError.message("Köşe işareti konturla eşleşmiyor veya yinelenmiş.") }
                    notchIndices.append(index)
                }
            }
            let metadata = raw["Metadata"] as? [String: Any]
            let quantityNumber = metadata?["Quantity"] as? NSNumber
            let quantity: Int? = quantityNumber.flatMap { number in
                guard CFGetTypeID(number) != CFBooleanGetTypeID(), number.doubleValue.isFinite,
                      number.doubleValue.rounded() == number.doubleValue,
                      number.doubleValue >= 0, number.doubleValue <= 1_000_000 else { return nil }
                return number.intValue
            }
            let grainStatus: String
            var parsedGrain: Grain?
            if let grain = raw["Grainline"] as? [String: Any],
               let start = grain["Start"] as? [String: Any], let end = grain["End"] as? [String: Any],
               let sx = jsonNumber(start["X"]), let sy = jsonNumber(start["Y"]),
               let ex = jsonNumber(end["X"]), let ey = jsonNumber(end["Y"]),
               sx.isFinite, sy.isFinite, ex.isFinite, ey.isFinite,
               (start["IsFinite"] == nil || jsonBoolean(start["IsFinite"]) == true),
               (end["IsFinite"] == nil || jsonBoolean(end["IsFinite"]) == true),
               [sx, sy, ex, ey].allSatisfy({ abs($0) < 1e100 }),
               (sx != ex || sy != ey), let review = jsonBoolean(grain["NeedsReview"]) {
                grainStatus = review ? "İplik yönü: Kontrol bekliyor" : "İplik yönü: Kayıtlı"
                parsedGrain = Grain(start: Point(x: sx, y: sy), end: Point(x: ex, y: ey), needsReview: review)
            } else if raw["Grainline"] == nil || raw["Grainline"] is NSNull {
                grainStatus = "İplik yönü: Belirtilmemiş"
            } else {
                grainStatus = "İplik yönü: Okunamadı"
            }
            let id = (raw["Id"] as? String).flatMap { UUID(uuidString: $0) }
            if !(raw["Name"] is String) { pieceRenameIdentitiesValid = false }
            if raw["Id"] != nil {
                if let id, id.uuidString != "00000000-0000-0000-0000-000000000000", pieceIDs.insert(id).inserted { }
                else { pieceRenameIdentitiesValid = false }
            }
            pieces.append(Piece(name: raw["Name"] as? String, id: id, points: points, closed: closed,
                                contourLength: points.count >= (closed ? 3 : 2) ? ContourMeasure.length(points, closed: closed) : nil,
                                role: metadata?["Role"] as? String, size: metadata?["Size"] as? String,
                                material: metadata?["Material"] as? String, quantity: quantity,
                                grainStatus: grainStatus, grain: parsedGrain, notches: notchIndices))
        }
        if formatVersion == 6 && totalNotches == 0 { throw ViewerError.message("Sürüm 6 dosyasında köşe işareti bulunamadı.") }
        return Drawing(name: name, pieces: pieces, pieceRenameIdentitiesValid: pieceRenameIdentitiesValid, formatVersion: formatVersion)
    }
}

#if DEBUG
private struct ExperimentalPointEditor: View {
    let data: Data
    let piece: Drawing.Piece
    let pieceID: UUID
    let projectName: String
    let isCurrent: () -> Bool
    let invalidateForTesting: () -> Void
    @Environment(\.dismiss) private var dismiss
    @State private var pointIndex = 0
    @State private var x = ""
    @State private var y = ""
    @State private var generation = UUID()
    @State private var candidate: Data?
    @State private var preview: Drawing.Piece?
    @State private var message = ""
    @State private var working = false
    @State private var saving = false
    @State private var saveGeneration = UUID()
    @State private var saveActiveID: UUID?
    @State private var cancellingSave = false

    private func invalidate() { generation = UUID(); candidate = nil; preview = nil; message = "" }
    private func updatePoint() {
        let point = piece.points[pointIndex]
        x = String(point.x); y = String(point.y)
        invalidate()
    }
    private func coordinate(_ text: String) -> Double? {
        let token = text.trimmingCharacters(in: .whitespacesAndNewlines).replacingOccurrences(of: ",", with: ".")
        guard let value = Double(token), value.isFinite else { return nil }
        let mantissa = token.prefix { $0 != "e" && $0 != "E" }
        guard value != 0 || !mantissa.contains(where: { ("1"..."9").contains(String($0)) }) else { return nil }
        return value
    }
    private func makePreview() {
        guard isCurrent(), let px = coordinate(x), let py = coordinate(y) else {
            message = "Geçerli X/Y girin ve açık parçanın değişmediğini doğrulayın."; return
        }
        let request = UUID(); generation = request; candidate = nil; preview = nil; working = true
        let selected = pointIndex
        DispatchQueue.global(qos: .userInitiated).async {
            let result = Result { () -> (Data, Drawing.Piece) in
                let edited = try ExperimentalPointMoveJSON.moving(data, snapshot: data, pieceID: pieceID, index: selected, to: .init(x: px, y: py))
                guard edited != data else { throw ViewerError.message("Nokta konumu değişmedi.") }
                let drawing = try Drawing.load(edited)
                guard let editedPiece = drawing.pieces.first(where: { $0.id == pieceID }) else { throw ViewerError.message("Düzenlenen parça okunamadı.") }
                return (edited, editedPiece)
            }
            DispatchQueue.main.async {
                working = false
                guard generation == request else { return }
                guard isCurrent() else { message = "Açık model veya seçim değişti; yeniden başlayın."; return }
                switch result {
                case .success(let (bytes, edited)): candidate = bytes; preview = edited; message = "Önizleme hazır; kopya henüz kaydedilmedi."
                case .failure: message = "Düzenleme güvenle uygulanamadı. Koordinatları, konturu ve desteklenmeyen proje alanlarını kontrol edin."
                }
            }
        }
    }
    private func saveCopy() {
        guard !saving, isCurrent(), let bytes = candidate else {
            message = "Açık model veya seçim değişti; kopya kaydedilmedi."; return
        }
        let request = UUID(); saveGeneration = request; saveActiveID = request; saving = true; cancellingSave = false
        message = "Kopya hazırlanıyor…"
        DispatchQueue.global(qos: .userInitiated).async {
            let result = Result { try ModelStore.preparePointCopy(bytes, projectName: projectName + " · nokta denemesi") }
            #if targetEnvironment(simulator)
            if ProcessInfo.processInfo.arguments.contains("--ui-test-point-save-delay") { Thread.sleep(forTimeInterval: 2) }
            #endif
            DispatchQueue.main.async {
                guard saveGeneration == request, saving, isCurrent(), candidate == bytes else {
                    let wasCancelled = saveGeneration != request
                    if case .success(let prepared) = result {
                        DispatchQueue.global(qos: .utility).async {
                            let cleanup = Result { try ModelStore.discardPointCopy(prepared) }
                            DispatchQueue.main.async {
                                guard saveActiveID == request else { return }
                                saveActiveID = nil; saving = false; cancellingSave = false
                                if case .failure = cleanup { message = "Kopya kaydedilmedi; geçici kayıt temizlenemedi." }
                                else { message = wasCancelled ? "Kayıt iptal edildi; kopya model listesine kaydedilmedi. Geçici veri cihazda kalabilir." : "Açık model veya seçim değişti; kopya model listesine kaydedilmedi. Geçici veri cihazda kalabilir." }
                            }
                        }
                    } else if saveActiveID == request {
                        saveActiveID = nil; saving = false; cancellingSave = false
                        message = wasCancelled ? "Kayıt iptal edildi; kopya model listesine kaydedilmedi. Geçici veri cihazda kalabilir." : "Açık model veya seçim değişti; kopya model listesine kaydedilmedi. Geçici veri cihazda kalabilir."
                    }
                    return
                }
                saveActiveID = nil; saving = false
                switch result {
                case .failure(let failure): message = "Kopya kaydedilemedi: \(failure.localizedDescription)"
                case .success(let prepared):
                    do {
                        // Only the final same-directory rename runs on the main
                        // actor, immediately after the snapshot check.
                        try ModelStore.finishPointCopy(prepared)
                        message = "Deneysel kopya Modellerim bölümüne kaydedildi. Orijinal korunuyor."
                        candidate = nil
                    } catch {
                        DispatchQueue.global(qos: .utility).async { try? ModelStore.discardPointCopy(prepared) }
                        message = "Kopya kaydedilemedi: \(error.localizedDescription)"
                    }
                }
            }
        }
    }
    var body: some View {
        NavigationStack {
            Form {
                Text("DENEYSEL DEBUG · Üretim için doğrulanmadı. Orijinal değiştirilmez; yalnız yeni kopya kaydedilir. Uygun v6 köşe işaretleri noktayla taşınır; konumları kontrol edin. Serileme kuralı varsa eski kaynak geometrisiyle aynen korunur; değişen konturla eşleşmeyebilir ve masaüstünde yeniden incelenmelidir. Pastal ve geçmiş desteklenmez. İplik yönü varsa yeniden incelenmelidir.")
                Picker("Nokta", selection: Binding(get: { pointIndex }, set: { pointIndex = $0; updatePoint() })) {
                    ForEach(piece.points.indices, id: \.self) { index in Text("Nokta \(index)").tag(index) }
                }
                .disabled(saving)
                TextField("X · mm", text: Binding(get: { x }, set: { x = $0; invalidate() }))
                    .disabled(saving)
                TextField("Y · mm", text: Binding(get: { y }, set: { y = $0; invalidate() }))
                    .disabled(saving)
                Button(working ? "Önizleme hazırlanıyor…" : "Önizlemeyi hesapla") { makePreview() }.disabled(working || saving)
                if saving {
                    Button("Kaydı iptal et") {
                        saveGeneration = UUID(); cancellingSave = true
                        message = "Kayıt iptal ediliyor; işlem tamamlanıyor…"
                    }
                    .disabled(cancellingSave)
                }
                #if targetEnvironment(simulator)
                if ProcessInfo.processInfo.arguments.contains("--ui-test-point-source-change") {
                    Button("Test: kaynak değiştir") { invalidateForTesting() }
                }
                #endif
                if !message.isEmpty { Text(message).foregroundStyle(.secondary) }
                if let preview {
                    DrawingView(pieces: [preview], label: "Düzenlenmiş kontur önizlemesi", highlightedNotch: nil, selectionRevision: nil)
                        .id(generation)
                        .frame(height: 240)
                    Button("Yeni kopyayı kaydet") { saveCopy() }
                    .disabled(candidate == nil || working || saving)
                }
            }
            .navigationTitle("Deneysel nokta")
            .toolbar { ToolbarItem(placement: .cancellationAction) { Button("Kapat") { dismiss() } } }
            .onAppear { updatePoint() }
            .onDisappear { generation = UUID(); saveGeneration = UUID(); saveActiveID = nil; saving = false; cancellingSave = false }
        }
    }
}
#endif

private struct NotchPicker: View {
    let pieces: [Drawing.Piece]
    let select: (Int, Int) -> Void
    @Environment(\.dismiss) private var dismiss
    @State private var search = ""
    private struct SearchResult {
        let query: String
        let groups: [[Int]]
        let hasMore: Bool
    }
    @State private var result: SearchResult?

    var body: some View {
        NavigationStack {
            let current = result?.query == search ? result : nil
            List {
                if current == nil { ProgressView("İşaretler aranıyor…") }
                if current?.hasMore == true {
                    Text("İlk 500 eşleşme gösteriliyor. Diğer işaretleri bulmak için aramayı daraltın.")
                        .font(.caption).foregroundStyle(.secondary)
                }
                ForEach(current.map { found in found.groups.indices.filter { !found.groups[$0].isEmpty } } ?? [], id: \.self) { pieceIndex in
                    let piece = pieces[pieceIndex]
                    let indices = current?.groups[pieceIndex] ?? []
                    if !indices.isEmpty {
                        Section {
                            ForEach(indices, id: \.self) { pointIndex in
                                let point = piece.points[pointIndex]
                                Button(NotchSearch.label(index: pointIndex, x: point.x.formatted(), y: point.y.formatted())) {
                                    select(pieceIndex, pointIndex)
                                }
                            }
                        } header: {
                            VStack(alignment: .leading) {
                                Text(piece.displayName(index: pieceIndex))
                                Text(piece.selectionDetails(index: pieceIndex)).font(.caption)
                            }
                        }
                    }
                }
                if let current, current.groups.allSatisfy({ $0.isEmpty }) {
                    Text("Eşleşen köşe işareti bulunamadı.").foregroundStyle(.secondary)
                }
            }
            .searchable(text: $search, prompt: "Parça, beden, nokta veya koordinat ara")
            .navigationTitle("Köşe işaretleri")
            .toolbar { ToolbarItem(placement: .cancellationAction) { Button("Kapat") { dismiss() } } }
            .task(id: search) {
                let request = search
                // Let rapid typing settle without delaying the first unfiltered list.
                if !request.isEmpty {
                    do { try await Task.sleep(nanoseconds: 150_000_000) } catch { return }
                }
                guard !Task.isCancelled else { return }
                let snapshot = pieces
                let worker = Task.detached(priority: .userInitiated) { () -> SearchResult? in
                    #if DEBUG && targetEnvironment(simulator)
                    if ProcessInfo.processInfo.arguments.contains("--ui-test-delay-old-notch-search"), request == "old" {
                        Thread.sleep(forTimeInterval: 1.5)
                    }
                    #endif
                    let query = NotchSearch.Query(request)
                    let names = snapshot.indices.map { snapshot[$0].displayName(index: $0) }
                    let details = snapshot.indices.map { snapshot[$0].selectionDetails(index: $0) }
                    guard let found = NotchSearch.boundedMatchesCancellable(groups: snapshot.map(\.notches), limit: 500, cancelled: { Task.isCancelled }, matches: { pieceIndex, pointIndex in
                        if query.isEmpty { return true }
                        guard query.allows(index: pointIndex) else { return false }
                        let point = snapshot[pieceIndex].points[pointIndex]
                        return query.matches(pieceName: names[pieceIndex], details: details[pieceIndex], index: pointIndex,
                                             x: point.x.formatted(), y: point.y.formatted())
                    }) else { return nil }
                    return SearchResult(query: request, groups: found.groups, hasMore: found.hasMore)
                }
                let found = await withTaskCancellationHandler(operation: { await worker.value }, onCancel: { worker.cancel() })
                guard !Task.isCancelled, search == request, let found else { return }
                result = found
            }
        }
    }
}

private struct PiecePicker: View {
    let pieces: [Drawing.Piece]
    @Binding var selection: Int?
    @Environment(\.dismiss) private var dismiss
    @State private var search = ""
    private struct SearchResult { let query: String; let indices: [Int] }
    @State private var result: SearchResult?

    var body: some View {
        NavigationStack {
            let current = result?.query == search ? result : nil
            let matches = current?.indices ?? []
            List {
                selectionRow(title: "Tüm parçalar", index: nil)
                if current == nil { ProgressView("Parçalar aranıyor…") }
                ForEach(matches, id: \.self) { index in
                    selectionRow(title: pieces[index].displayName(index: index), index: index,
                                 details: pieces[index].selectionDetails(index: index))
                }
                if current != nil && matches.isEmpty && !search.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
                    Text("Eşleşen parça bulunamadı.")
                        .foregroundStyle(.secondary)
                }
            }
            .searchable(text: $search, prompt: "Parça ara")
            .navigationTitle("Parça seç")
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Kapat") { dismiss() }
                }
            }
            .task(id: search) {
                let request = search
                let snapshot = pieces
                if !request.isEmpty {
                    do { try await Task.sleep(nanoseconds: 150_000_000) } catch { return }
                }
                guard !Task.isCancelled else { return }
                let worker = Task.detached(priority: .userInitiated) { () -> SearchResult? in
                    #if DEBUG && targetEnvironment(simulator)
                    if ProcessInfo.processInfo.arguments.contains("--ui-test-delay-old-piece-search"), request == "old" {
                        Thread.sleep(forTimeInterval: 1.5)
                    }
                    #endif
                    guard let indices = ModelSearch.matchingIndices(count: snapshot.count, query: request, cancelled: { Task.isCancelled }, fields: { index in
                        (snapshot[index].displayName(index: index), snapshot[index].selectionDetails(index: index))
                    }) else { return nil }
                    return SearchResult(query: request, indices: indices)
                }
                let found = await withTaskCancellationHandler(operation: { await worker.value }, onCancel: { worker.cancel() })
                guard !Task.isCancelled, search == request, let found else { return }
                result = found
            }
        }
    }

    private func selectionRow(title: String, index: Int?, details: String? = nil) -> some View {
        Button {
            selection = index
            dismiss()
        } label: {
            HStack {
                VStack(alignment: .leading, spacing: 3) {
                    Text(title).foregroundStyle(.primary)
                    if let details {
                        Text(details).font(.caption).foregroundStyle(.secondary)
                    }
                }
                Spacer()
                if selection == index {
                    Image(systemName: "checkmark").foregroundStyle(Color.accentColor)
                }
            }
            .frame(maxWidth: .infinity, minHeight: 52, alignment: .leading)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .accessibilityElement(children: .combine)
        .accessibilityLabel(title)
        .accessibilityValue(details ?? "")
        .accessibilityAddTraits(selection == index ? .isSelected : [])
    }
}
private struct PieceDetails: View {
    let piece: Drawing.Piece

    private var dimensions: (width: Double, height: Double)? {
        guard let first = piece.points.first else { return nil }
        var minX = first.x, maxX = first.x, minY = first.y, maxY = first.y
        for point in piece.points.dropFirst() {
            minX = min(minX, point.x); maxX = max(maxX, point.x)
            minY = min(minY, point.y); maxY = max(maxY, point.y)
        }
        return (maxX - minX, maxY - minY)
    }

    var body: some View {
        if let dimensions {
            VStack(alignment: .leading, spacing: 3) {
                Text("Kontur bilgisi").font(.subheadline.bold())
                Text("\(piece.points.count) nokta • \(piece.closed ? "Kapalı" : "Açık") kontur")
                if let role = piece.role, !role.isEmpty { Text("Rol: \(role)") }
                if let size = piece.size, !size.isEmpty { Text("Beden: \(size)") }
                if let material = piece.material, !material.isEmpty { Text("Malzeme: \(material)") }
                if let quantity = piece.quantity { Text("Adet: \(quantity)") }
                Text(piece.grainStatus)
                if let width = MeasurementDisplay.format(dimensions.width), let height = MeasurementDisplay.format(dimensions.height) {
                    Text("Yaklaşık en: \(width) mm • boy: \(height) mm")
                } else {
                    Text("Boyutlar hesaplanamadı.")
                }
                if let length = piece.contourLength, let formatted = MeasurementDisplay.format(length) {
                    Text("Yaklaşık kontur uzunluğu: \(formatted) mm")
                } else {
                    Text("Kontur uzunluğu hesaplanamadı.")
                }
                Text("Uzunluk dosyadaki düz çizgi parçalarının toplamıdır; üretim ölçüsü değildir.")
                    .foregroundStyle(.secondary)
                Text("Boyutlar ve uzunluk milimetre cinsindendir.")
                    .foregroundStyle(.secondary)
            }
            .font(.caption)
            .frame(maxWidth: .infinity, alignment: .leading)
            .padding(8)
            .background(Color.secondary.opacity(0.08))
            .accessibilityElement(children: .combine)
        }
    }
}
private struct DrawingView: View {
    static let maximumVisibleNotches = 500
    let pieces: [Drawing.Piece]
    let label: String
    let highlightedNotch: Int?
    let selectionRevision: UUID?
    var drawingHeight: CGFloat = 320
    @State private var zoom: CGFloat = 1
    @State private var offset: CGSize = .zero
    @State private var showGrain = true
    @State private var panEnabled = false
    @State private var canvasSize: CGSize = .zero
    @State private var cachedBounds: ContourBounds?
    @GestureState private var pinch: CGFloat = 1
    @GestureState private var drag: CGSize = .zero

    private func limitedZoom(_ value: CGFloat) -> CGFloat {
        min(max(value.isFinite ? value : 1, 0.5), 10)
    }

    private func limitedOffset(_ value: CGSize, size: CGSize, scale: CGFloat) -> CGSize {
        let limitX = size.width * max(scale, 1)
        let limitY = size.height * max(scale, 1)
        return CGSize(width: min(max(value.width, -limitX), limitX),
                      height: min(max(value.height, -limitY), limitY))
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
        GeometryReader { geometry in
            let currentZoom = limitedZoom(zoom * pinch)
            let currentOffset = limitedOffset(
                CGSize(width: offset.width + drag.width, height: offset.height + drag.height),
                size: geometry.size, scale: currentZoom
            )
            ZStack(alignment: .topTrailing) {
                drawing
                    .scaleEffect(currentZoom)
                    .offset(currentOffset)
                    .frame(width: geometry.size.width, height: geometry.size.height)
                    .accessibilityLabel(label)
                    .accessibilityHint("Yakınlaştırmak için iki parmağınızı kullanın. Çizimi sürüklemek için taşıma modunu açın; yön düğmeleriyle de taşıyabilirsiniz.")
                    .accessibilityValue("Yüzde \(Int(currentZoom * 100))")
                    .accessibilityAdjustableAction { direction in
                        switch direction {
                        case .increment: zoom = limitedZoom(zoom * 1.25)
                        case .decrement: zoom = limitedZoom(zoom / 1.25)
                        @unknown default: break
                        }
                        offset = limitedOffset(offset, size: geometry.size, scale: zoom)
                    }

            }
            .frame(width: geometry.size.width, height: geometry.size.height)
            .contentShape(Rectangle())
            .clipped()
            .gesture(panEnabled ? DragGesture()
                .updating($drag) { value, state, _ in state = value.translation }
                .onEnded { value in
                    offset = limitedOffset(
                        CGSize(width: offset.width + value.translation.width, height: offset.height + value.translation.height),
                        size: geometry.size, scale: zoom
                    )
                } : nil)
            .simultaneousGesture(
                MagnificationGesture()
                    .updating($pinch) { value, state, _ in state = value }
                    .onEnded { value in
                        zoom = limitedZoom(zoom * value)
                        offset = limitedOffset(offset, size: geometry.size, scale: zoom)
                    }
            )
            .onAppear { canvasSize = geometry.size; refreshBounds(size: geometry.size) }
            .onChange(of: geometry.size) { canvasSize = $0; revealSelectedNotch(size: $0) }
            .onChange(of: selectionRevision) { _ in revealSelectedNotch(size: geometry.size) }
            .onChange(of: showGrain) { _ in refreshBounds(size: geometry.size) }
        }
        .frame(height: drawingHeight)
                VStack(alignment: .leading, spacing: 8) {
                    Button(panEnabled ? "Taşımayı bitir" : "Çizimi taşı") { panEnabled.toggle() }
                        .accessibilityHint(panEnabled ? "Sayfayı kaydırmaya dön" : "Çizimi sürüklemek için taşıma modunu aç")
                    if panEnabled {
                        HStack {
                            Button("Sol") { moveDrawing(width: -40, height: 0, size: canvasSize) }
                            Button("Sağ") { moveDrawing(width: 40, height: 0, size: canvasSize) }
                            Button("Yukarı") { moveDrawing(width: 0, height: -40, size: canvasSize) }
                            Button("Aşağı") { moveDrawing(width: 0, height: 40, size: canvasSize) }
                        }
                    }
                    HStack {
                        Button("Uzaklaştır") { changeZoom(by: 1 / 1.25, size: canvasSize) }
                            .disabled(zoom <= 0.5)
                        Button("Yakınlaştır") { changeZoom(by: 1.25, size: canvasSize) }
                            .disabled(zoom >= 10)
                    }
                    Button("Görünümü sıfırla") { zoom = 1; offset = .zero }
                    if pieces.contains(where: { $0.grain != nil }) {
                        Button(showGrain ? "İplik yönlerini gizle" : "İplik yönlerini göster") { showGrain.toggle() }
                    }
                }
                .font(.caption)

        }
    }

    private func changeZoom(by factor: CGFloat, size: CGSize) {
        zoom = limitedZoom(zoom * factor)
        offset = limitedOffset(offset, size: size, scale: zoom)
    }

    private func moveDrawing(width: CGFloat, height: CGFloat, size: CGSize) {
        offset = limitedOffset(CGSize(width: offset.width + width, height: offset.height + height), size: size, scale: zoom)
    }

    private var viewportPoints: [Drawing.Point] {
        var points = pieces.filter { $0.points.count > 1 }.flatMap(\.points)
        if showGrain {
            for piece in pieces where !piece.points.isEmpty {
                if let grain = piece.grain { points.append(contentsOf: [grain.start, grain.end]) }
            }
        }
        return points
    }

    private func refreshBounds(size: CGSize) {
        cachedBounds = ContourBounds(points: viewportPoints)
        revealSelectedNotch(size: size)
    }

    private func revealSelectedNotch(size: CGSize) {
        guard pieces.count == 1, let index = highlightedNotch,
              pieces[0].points.indices.contains(index), pieces[0].notches.contains(index),
              let cachedBounds,
              let viewport = ContourViewport(bounds: cachedBounds, width: Double(size.width), height: Double(size.height)),
              let result = ContourViewport.visibleOffset(point: viewport.position(pieces[0].points[index]), size: size, zoom: zoom, offset: offset) else { return }
        offset = limitedOffset(result, size: size, scale: zoom)
    }

    private var drawing: some View {
        Canvas { context, size in
            guard let cachedBounds,
                  let viewport = ContourViewport(bounds: cachedBounds, width: Double(size.width), height: Double(size.height)) else { return }
            func position(_ p: Drawing.Point) -> CGPoint { viewport.position(p) }
            var remainingNotches = Self.maximumVisibleNotches
            for piece in pieces {
                guard let first = piece.points.first else { continue }
                var path = Path()
                path.move(to: position(first))
                for point in piece.points.dropFirst() { path.addLine(to: position(point)) }
                if piece.closed && piece.points.count > 1 { path.closeSubpath() }
                context.stroke(path, with: .color(.accentColor), lineWidth: 1.5)
                // A path whose vertices all coincide has no visible stroke.
                if ContourMeasure.isCollapsed(piece.points) {
                    let p = position(first)
                    context.fill(Path(ellipseIn: CGRect(x: p.x - 3, y: p.y - 3, width: 6, height: 6)), with: .color(.accentColor))
                }
                for index in piece.notches.prefix(remainingNotches) {
                    let p = position(piece.points[index])
                    let mark = Path(ellipseIn: CGRect(x: p.x - 5, y: p.y - 5, width: 10, height: 10))
                    context.stroke(mark, with: .color(.pink), lineWidth: 2)
                    remainingNotches -= 1
                }
                if let index = highlightedNotch, pieces.count == 1, piece.notches.contains(index) {
                    let p = position(piece.points[index])
                    context.stroke(Path(ellipseIn: CGRect(x: p.x - 8, y: p.y - 8, width: 16, height: 16)), with: .color(.yellow), lineWidth: 3)
                }
                if showGrain, let grain = piece.grain {
                    let start = position(grain.start), end = position(grain.end)
                    let dx = end.x - start.x, dy = end.y - start.y
                    let length = hypot(dx, dy)
                    guard length.isFinite && length > 0.1 else { continue }
                    let ux = dx / length, uy = dy / length
                    let arrow = min(CGFloat(9), length / 3)
                    var grainPath = Path()
                    grainPath.move(to: start); grainPath.addLine(to: end)
                    grainPath.move(to: CGPoint(x: end.x - ux * arrow - uy * arrow * 0.5, y: end.y - uy * arrow + ux * arrow * 0.5))
                    grainPath.addLine(to: end)
                    grainPath.addLine(to: CGPoint(x: end.x - ux * arrow + uy * arrow * 0.5, y: end.y - uy * arrow - ux * arrow * 0.5))
                    context.stroke(grainPath, with: .color(grain.needsReview ? .orange : .primary),
                                   style: StrokeStyle(lineWidth: 1.5, dash: grain.needsReview ? [4, 3] : []))
                }
            }
        }
    }
}
