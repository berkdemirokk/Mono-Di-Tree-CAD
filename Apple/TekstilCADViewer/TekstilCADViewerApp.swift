import CoreFoundation
import Darwin
import SwiftUI
import UniformTypeIdentifiers

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
    @State private var isSaving = false
    @State private var saveNotice: String?
    @State private var project: Drawing?
    @State private var error: String?
    @State private var selectedPiece: Int?
    @State private var drawingRevision = UUID()
    @State private var loadRequest = UUID()
    @State private var isLoading = false

    var body: some View {
        ScrollView {
        VStack(spacing: 12) {
            HStack {
                Button(".tcad aç") { loadRequest = UUID(); isLoading = false; showingImporter = true }
                    .accessibilityHint("Önizlemek için bir proje dosyası seçin").disabled(isSaving)
                Button("Modellerim") { showingLibrary = true }.disabled(isSaving)
                Spacer()
                if let project { Text(project.name).font(.headline).lineLimit(1) }
            }
            Text("Proje adını yeni bir kopyada değiştirebilirsiniz. Pastal ve üretim çıktıları desteklenmez.")
                .font(.caption).foregroundStyle(.secondary)
            if isLoading { ProgressView("Proje açılıyor…") }
            if let saveNotice { Text(saveNotice).font(.caption).foregroundStyle(.secondary) }
            if importedData != nil {
                Button(isSaving ? "Kaydediliyor…" : "Bu modeli telefona kaydet") { saveImportedModel() }
                    .disabled(isSaving || isLoading)
            }
            if let project {
                Button("Açık modeli dışa aktar…") {
                    guard let data = sourceData else { return }
                    // Capture the loaded bytes before presenting the system picker.
                    exportDocument = ProjectExportDocument(data: data)
                    exportName = exportFileName(project.name)
                    saveNotice = nil
                    showingExportWarning = true
                }.disabled(isSaving || isLoading || sourceData == nil)
                Button("Proje adını değiştir ve kopya kaydet") {
                    showingNameEditor = true
                }.disabled(isSaving || isLoading || sourceData == nil || (sourceData?.count ?? 0) > LosslessProjectName.maximumEditBytes)
                if let sourceData, sourceData.count > LosslessProjectName.maximumEditBytes {
                    Text("Proje adını değiştirmek için dosya 8 MB veya daha küçük olmalı.")
                        .font(.caption).foregroundStyle(.secondary)
                }
                if project.pieces.contains(where: { !$0.hasMinimumContourPoints }) {
                    Text("\(project.pieces.filter { !$0.hasMinimumContourPoints }.count) parça asgari nokta sayısını karşılamıyor. Bu parçalar çizilmez veya ölçülmez; dosya masaüstünde açılamayabilir. Diğer dosya koşulları doğrulanmadı.")
                        .font(.caption).foregroundStyle(.secondary)
                }
                if project.pieces.contains(where: { !$0.notches.isEmpty }) {
                    Text("Köşe işaretleri halkayla gösterilir; kesim derinliği veya yönü belirtilmez. Önizleme üretim çıktısı değildir.")
                        .font(.caption).foregroundStyle(.secondary)
                    Text("Sürüm 6 için yalnız UTF-8 ve en fazla 8 MB desteklenir. İşaretler ve konturlar önizlenir; tüm masaüstü proje kuralları doğrulanmaz.")
                        .font(.caption).foregroundStyle(.secondary)
                    Button("Köşe işaretlerini ara ve seç…") { showingNotches = true }
                        .sheet(isPresented: $showingNotches) {
                            NotchPicker(pieces: project.pieces) { piece, point in
                                selectedPiece = piece
                                selectedNotch = (piece, point)
                                showingNotches = false
                            }
                        }
                    if let selectedNotch, selectedNotch.piece == selectedPiece {
                        Text(project.pieces[selectedNotch.piece].hasMinimumContourPoints
                             ? "Seçili köşe işareti: nokta \(selectedNotch.point). Önizlemede sarı halka ile gösterilir."
                             : "Seçili köşe işareti: nokta \(selectedNotch.point). Konturda yeterli nokta olmadığı için önizleme gösterilemiyor.")
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
                    .accessibilityHint("Parçaları aramak veya seçmek için açın")
                    .sheet(isPresented: $showingPieces) {
                        PiecePicker(pieces: project.pieces, selection: $selectedPiece)
                    }
                    if let selectedPiece {
                        #if DEBUG
                        Button("Deneysel nokta düzenle…") {
                            guard let data = sourceData, let id = project.pieces[selectedPiece].id else { return }
                            pointEditSnapshot = PointEditSnapshot(data: data, piece: project.pieces[selectedPiece], pieceID: id,
                                index: selectedPiece, projectName: project.name, revision: drawingRevision, loadRequest: loadRequest)
                        }
                            .disabled(isSaving || isLoading || sourceData == nil || project.pieces[selectedPiece].id == nil || project.pieces[selectedPiece].points.isEmpty || project.pieces[selectedPiece].points.count > 4096 || (sourceData?.count ?? 0) > 8 * 1024 * 1024)
                            .sheet(item: $pointEditSnapshot) { snapshot in
                                ExperimentalPointEditor(data: snapshot.data, piece: snapshot.piece, pieceID: snapshot.pieceID, projectName: snapshot.projectName,
                                    isCurrent: { sourceData == snapshot.data && self.selectedPiece == snapshot.index && drawingRevision == snapshot.revision && loadRequest == snapshot.loadRequest && !isLoading },
                                    invalidateForTesting: { loadRequest = UUID() })
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
                        .disabled(isSaving || isLoading || sourceData == nil || (sourceData?.count ?? 0) > LosslessProjectName.maximumEditBytes || !project.pieceRenameIdentitiesValid)
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
                        DrawingView(
                        pieces: selectedPiece.map { [project.pieces[$0]] } ?? project.pieces.filter { $0.hasMinimumContourPoints },
                        label: selectedPiece.map { "\(project.pieces[$0].displayName(index: $0)) kontur önizlemesi" } ?? "\(project.pieces.filter { $0.hasMinimumContourPoints }.count) parçalı kontur önizlemesi",
                        highlightedNotch: selectedNotch?.piece == selectedPiece ? selectedNotch?.point : nil
                        )
                        .frame(height: 320)
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
        .fileImporter(isPresented: $showingImporter, allowedContentTypes: [.item]) { result in
            do {
                let url = try result.get()
                guard url.pathExtension.lowercased() == "tcad" else { throw ViewerError.message("Lütfen bir .tcad dosyası seçin.") }
                openModel(url, imported: true)
            } catch { self.error = error.localizedDescription }
        }
        .fileExporter(isPresented: $showingExporter, document: exportDocument, contentType: .data, defaultFilename: exportName) { result in
            exportDocument = nil
            switch result {
            case .success: saveNotice = "Model seçilen hedefe dışa aktarıldı."
            case .failure(let failure): error = failure.localizedDescription
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
            ModelLibrary { url in
                showingLibrary = false
                openModel(ModelStore.payloadURL(url), imported: false)
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

    private func openModel(_ url: URL, imported: Bool) {
        saveNotice = nil
        let request = UUID()
        loadRequest = request
        isLoading = true
        DispatchQueue.global(qos: .userInitiated).async {
            let access = imported && url.startAccessingSecurityScopedResource()
            defer { if access { url.stopAccessingSecurityScopedResource() } }
            let loaded = Result { () -> (Drawing, Data) in
                let data = try Drawing.readData(url)
                return (try Drawing.load(data), data)
            }
            DispatchQueue.main.async {
                guard loadRequest == request else { return }
                isLoading = false
                switch loaded {
                case .success(let (drawing, data)):
                    #if DEBUG
                    pieceRenameSnapshot = nil
                    #endif
                    showingPieces = false
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
        guard let data = sourceData else { return }
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

    private func saveImportedModel() {
        guard let data = importedData else { return }
        let name = importedName
        saveNotice = nil
        isSaving = true
        DispatchQueue.global(qos: .userInitiated).async {
            let result = Result { try ModelStore.save(data, name: name) }
            DispatchQueue.main.async {
                isSaving = false
                switch result {
                case .success: importedData = nil; saveNotice = "Model telefona kaydedildi."
                case .failure(let failure): error = failure.localizedDescription
                }
            }
        }
    }
}

enum ModelStore {
    struct Entry: Identifiable {
        let url: URL
        let name: String
        let date: Date?
        let canOpen: Bool
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

    private static func metadata(_ url: URL) -> CopyMetadata? {
        guard url.pathExtension.lowercased() == copyExtension,
              let data = try? Data(contentsOf: url.appendingPathComponent(metadataName)),
              let metadata = try? JSONDecoder().decode(CopyMetadata.self, from: data),
              metadata.version == 1 else { return nil }
        return metadata
    }

    static func models() throws -> [Entry] {
        try models(in: directory())
    }

    static func models(in directory: URL) throws -> [Entry] {
        try FileManager.default.contentsOfDirectory(at: directory, includingPropertiesForKeys: nil,
                                                    options: [])
            .compactMap { url in
                let name: String
                let canOpen: Bool
                if url.pathExtension.lowercased() == "tcad" {
                    name = displayName(url)
                    canOpen = true
                } else if url.pathExtension.lowercased() == copyExtension {
                    if let metadata = metadata(url), FileManager.default.fileExists(atPath: payloadURL(url).path) {
                        name = metadata.name.isEmpty ? "Model" : metadata.name
                        canOpen = true
                    } else {
                        name = "Okunamayan model (\(url.deletingPathExtension().lastPathComponent))"
                        canOpen = false
                    }
                } else {
                    return nil
                }
                return Entry(url: url, name: name, date: savedDate(url), canOpen: canOpen)
            }
            .sorted { $0.name.localizedStandardCompare($1.name) == .orderedAscending }
    }

    static func savedDate(_ url: URL) -> Date? {
        try? url.resourceValues(forKeys: [.creationDateKey]).creationDate
    }

    static func delete(_ url: URL) throws {
        let directory = try directory().standardizedFileURL
        guard url.standardizedFileURL.deletingLastPathComponent() == directory,
              ["tcad", copyExtension].contains(url.pathExtension.lowercased()) else {
            throw ViewerError.message("Model silinemedi.")
        }
        try FileManager.default.removeItem(at: url)
    }

    static func displayName(_ url: URL) -> String {
        let stem = url.deletingPathExtension().lastPathComponent
        guard let separator = stem.range(of: " - ", options: .backwards),
              UUID(uuidString: String(stem[separator.upperBound...])) != nil else { return stem }
        return String(stem[..<separator.lowerBound])
    }
}

private struct ModelLibrary: View {
    let open: (URL) -> Void
    @Environment(\.dismiss) private var dismiss
    @State private var models: [ModelStore.Entry] = []
    @State private var search = ""
    @State private var sortOrder: SortOrder = .name
    @State private var error: String?
    @State private var modelToDelete: ModelStore.Entry?
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

    private var matches: [ModelStore.Entry] {
        let query = search.trimmingCharacters(in: .whitespacesAndNewlines)
        return models.filter {
            query.isEmpty || $0.name.localizedStandardContains(query) ||
                $0.displayIdentity.localizedCaseInsensitiveContains(query) ||
                $0.readableIdentity.localizedCaseInsensitiveContains(query)
        }.sorted { first, second in
            if sortOrder == .newest {
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
    }

    var body: some View {
        NavigationStack {
            List {
                if isLoading {
                    ProgressView("Modeller yükleniyor…")
                } else if loadFailed {
                    VStack(alignment: .leading, spacing: 8) {
                        Text("Kayıtlı modeller yüklenemedi.").foregroundStyle(.secondary)
                        Button("Tekrar dene") { loadModels() }
                    }
                } else if models.isEmpty {
                    Text("Henüz kayıtlı model yok. Bir .tcad açıp telefona kaydedebilirsiniz.")
                        .foregroundStyle(.secondary)
                } else if matches.isEmpty {
                    VStack(alignment: .leading, spacing: 8) {
                        Text("Eşleşen model bulunamadı.").foregroundStyle(.secondary)
                        Button("Aramayı temizle") { search = "" }
                    }
                }
                ForEach(matches) { model in
                    Button {
                        if model.canOpen { open(model.url) }
                        else { error = "Bu kayıt açılamıyor. İsterseniz kayıtlı kopyayı silebilirsiniz." }
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
                    .swipeActions {
                        Button("Sil", role: .destructive) { modelToDelete = model }
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
            .onDisappear { loadID = UUID() }
            .alert("Modeli sil?", isPresented: Binding(get: { modelToDelete != nil }, set: { if !$0 { modelToDelete = nil } })) {
                Button("İptal", role: .cancel) { modelToDelete = nil }
                Button("Sil", role: .destructive) {
                    guard let model = modelToDelete else { return }
                    do {
                        try ModelStore.delete(model.url)
                        models.removeAll { $0.id == model.id }
                    } catch { self.error = error.localizedDescription }
                    modelToDelete = nil
                }
            } message: {
                Text("\(modelToDelete?.name ?? "Model")\nKayıt: \(modelToDelete?.readableIdentity ?? "")\nTam kayıt kimliği: \(modelToDelete?.displayIdentity ?? "")\nTelefonunuzdaki kayıtlı modellerden silinecek. Orijinal dosya etkilenmez.")
            }
            .alert("İşlem tamamlanamadı", isPresented: Binding(get: { error != nil }, set: { if !$0 { error = nil } })) {
                Button("Tamam", role: .cancel) { error = nil }
            } message: { Text(error ?? "") }
        }
    }

    private func loadModels() {
        let requestID = UUID()
        loadID = requestID
        isLoading = true
        loadFailed = false
        models = []
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
                case .success(let entries): models = entries
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
        let file = try FileHandle(forReadingFrom: url)
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
        let root = try JSONSerialization.jsonObject(with: data)
        let object: [String: Any]
        let formatVersion: Int
        if let array = root as? [Any], array.count == 2,
           let version = array[0] as? Int, (1...6).contains(version), let body = array[1] as? [String: Any] {
            object = body; formatVersion = version
        } else if let body = root as? [String: Any] { object = body; formatVersion = 0 }
        else { throw ViewerError.message("Desteklenmeyen proje biçimi.") }
        if formatVersion == 6 {
            guard data.count <= LosslessProjectName.maximumEditBytes else { throw ViewerError.message("Sürüm 6 önizlemesi için dosya en fazla 8 MB olmalı.") }
            do { try LosslessProjectName.validateUniqueKeys(data) }
            catch { throw ViewerError.message("Sürüm 6 dosyası güvenli biçimde okunamadı; alanları ve UTF-8 kodlamasını kontrol edin.") }
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
                      x.isFinite, y.isFinite, abs(x) < 1e100, abs(y) < 1e100 else {
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
        return Drawing(name: name, pieces: pieces, pieceRenameIdentitiesValid: pieceRenameIdentitiesValid)
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
                    DrawingView(pieces: [preview], label: "Düzenlenmiş kontur önizlemesi", highlightedNotch: nil)
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

    private func matching(_ piece: Drawing.Piece, index: Int) -> [Int] {
        let query = search.trimmingCharacters(in: .whitespacesAndNewlines)
        if query.isEmpty || piece.displayName(index: index).localizedCaseInsensitiveContains(query) { return piece.notches }
        return piece.notches.filter { point in
            let p = piece.points[point]
            return "\(point) X \(p.x.formatted()) Y \(p.y.formatted())".localizedCaseInsensitiveContains(query)
        }
    }

    var body: some View {
        NavigationStack {
            List {
                ForEach(pieces.indices, id: \.self) { pieceIndex in
                    let piece = pieces[pieceIndex]
                    let indices = matching(piece, index: pieceIndex)
                    if !indices.isEmpty {
                        Section(piece.displayName(index: pieceIndex)) {
                            ForEach(indices, id: \.self) { pointIndex in
                                let point = piece.points[pointIndex]
                                Button("Nokta \(pointIndex) · X \(point.x.formatted()) · Y \(point.y.formatted()) mm") {
                                    select(pieceIndex, pointIndex)
                                }
                            }
                        }
                    }
                }
                if pieces.indices.allSatisfy({ matching(pieces[$0], index: $0).isEmpty }) {
                    Text("Eşleşen köşe işareti bulunamadı.").foregroundStyle(.secondary)
                }
            }
            .searchable(text: $search, prompt: "Parça, nokta veya koordinat ara")
            .navigationTitle("Köşe işaretleri")
            .toolbar { ToolbarItem(placement: .cancellationAction) { Button("Kapat") { dismiss() } } }
        }
    }
}

private struct PiecePicker: View {
    let pieces: [Drawing.Piece]
    @Binding var selection: Int?
    @Environment(\.dismiss) private var dismiss
    @State private var search = ""

    private var matchingIndices: [Int] {
        let query = search.trimmingCharacters(in: .whitespacesAndNewlines)
        return pieces.indices.filter { index in
            (query.isEmpty || pieces[index].displayName(index: index).localizedCaseInsensitiveContains(query) ||
                pieces[index].selectionDetails(index: index).localizedCaseInsensitiveContains(query))
        }
    }

    var body: some View {
        NavigationStack {
            List {
                selectionRow(title: "Tüm parçalar", index: nil)
                ForEach(matchingIndices, id: \.self) { index in
                    selectionRow(title: pieces[index].displayName(index: index), index: index,
                                 details: pieces[index].selectionDetails(index: index))
                }
                if matchingIndices.isEmpty && !search.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
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
                    Text("Yaklaşık en: \(width) • boy: \(height)")
                } else {
                    Text("Boyutlar hesaplanamadı.")
                }
                if let length = piece.contourLength, let formatted = MeasurementDisplay.format(length) {
                    Text("Yaklaşık kontur uzunluğu: \(formatted)")
                } else {
                    Text("Kontur uzunluğu hesaplanamadı.")
                }
                Text("Uzunluk dosyadaki düz çizgi parçalarının toplamıdır; üretim ölçüsü değildir.")
                    .foregroundStyle(.secondary)
                Text("Boyutlar ve uzunluk dosyadaki koordinat birimindedir.")
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
    @State private var zoom: CGFloat = 1
    @State private var offset: CGSize = .zero
    @State private var showGrain = true
    @State private var panEnabled = false
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
                VStack(alignment: .trailing, spacing: 8) {
                    Button(panEnabled ? "Taşımayı bitir" : "Çizimi taşı") { panEnabled.toggle() }
                        .accessibilityHint(panEnabled ? "Sayfayı kaydırmaya dön" : "Çizimi sürüklemek için taşıma modunu aç")
                    if panEnabled {
                        HStack {
                            Button("Sol") { moveDrawing(width: -40, height: 0, size: geometry.size) }
                            Button("Sağ") { moveDrawing(width: 40, height: 0, size: geometry.size) }
                            Button("Yukarı") { moveDrawing(width: 0, height: -40, size: geometry.size) }
                            Button("Aşağı") { moveDrawing(width: 0, height: 40, size: geometry.size) }
                        }
                    }
                    Button("Görünümü sıfırla") { zoom = 1; offset = .zero }
                    if pieces.contains(where: { $0.grain != nil }) {
                        Button(showGrain ? "İplik yönlerini gizle" : "İplik yönlerini göster") { showGrain.toggle() }
                    }
                }
                .font(.caption)
                .padding(8)
                .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 8))
                .padding(8)
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
        }
    }

    private func moveDrawing(width: CGFloat, height: CGFloat, size: CGSize) {
        offset = limitedOffset(CGSize(width: offset.width + width, height: offset.height + height), size: size, scale: zoom)
    }

    private var drawing: some View {
        Canvas { context, size in
            var points = pieces.filter { $0.points.count > 1 }.flatMap(\.points)
            if showGrain {
                for piece in pieces where !piece.points.isEmpty {
                    if let grain = piece.grain { points.append(contentsOf: [grain.start, grain.end]) }
                }
            }
            guard let first = points.first else { return }
            var minX = first.x, maxX = first.x, minY = first.y, maxY = first.y
            for p in points { minX = min(minX, p.x); maxX = max(maxX, p.x); minY = min(minY, p.y); maxY = max(maxY, p.y) }
            let width = maxX - minX, height = maxY - minY
            guard width > 0 || height > 0 else { return }
            let horizontalScale = width > 0 ? max(Double(size.width) - 32, 1) / max(width, 1e-100) : .infinity
            let verticalScale = height > 0 ? max(Double(size.height) - 32, 1) / max(height, 1e-100) : .infinity
            let scale = min(horizontalScale, verticalScale)
            let offsetX = (Double(size.width) - width * scale) / 2
            let offsetY = (Double(size.height) - height * scale) / 2
            func position(_ p: Drawing.Point) -> CGPoint {
                CGPoint(x: offsetX + (p.x - minX) * scale, y: Double(size.height) - offsetY - (p.y - minY) * scale)
            }
            var remainingNotches = Self.maximumVisibleNotches
            for piece in pieces {
                guard let first = piece.points.first else { continue }
                var path = Path()
                path.move(to: position(first))
                for point in piece.points.dropFirst() { path.addLine(to: position(point)) }
                if piece.closed && piece.points.count > 1 { path.closeSubpath() }
                context.stroke(path, with: .color(.accentColor), lineWidth: 1.5)
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
