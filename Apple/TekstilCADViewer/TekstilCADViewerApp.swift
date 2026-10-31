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
    @State private var showingLibrary = false
    @State private var showingExporter = false
    @State private var exportDocument: ProjectExportDocument?
    @State private var exportName = "Model.tcad"
    @State private var importedData: Data?
    @State private var sourceData: Data?
    @State private var showingNameEditor = false
    @State private var editedName = ""
    @State private var showingPieceNameEditor = false
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
                    showingExporter = true
                }.disabled(isSaving || isLoading || sourceData == nil)
                Button("Proje adını değiştir ve kopya kaydet") {
                    showingNameEditor = true
                }.disabled(isSaving || isLoading || sourceData == nil || (sourceData?.count ?? 0) > LosslessProjectName.maximumEditBytes)
                if let sourceData, sourceData.count > LosslessProjectName.maximumEditBytes {
                    Text("Proje adını değiştirmek için dosya 8 MB veya daha küçük olmalı.")
                        .font(.caption).foregroundStyle(.secondary)
                }
                if project.pieces.contains(where: { $0.points.count > 1 }) {
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
                        PieceDetails(piece: project.pieces[selectedPiece])
                    }
                    DrawingView(
                        pieces: selectedPiece.map { [project.pieces[$0]] } ?? project.pieces,
                        label: selectedPiece.map { "\(project.pieces[$0].displayName(index: $0)) kontur önizlemesi" } ?? "\(project.pieces.count) parçalı kontur önizlemesi"
                    )
                        .id("\(drawingRevision)-\(selectedPiece.map(String.init) ?? "all")")
                        .background(Color.secondary.opacity(0.08))
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
        var stem = ""
        for character in name {
            let part = character == "/" || character == ":" || character.unicodeScalars.contains(where: { CharacterSet.controlCharacters.contains($0) }) ? "_" : String(character)
            if (stem + part).utf8.count > 100 { break }
            stem += part
        }
        stem = stem.trimmingCharacters(in: .whitespacesAndNewlines)
        while stem.first == "." { stem.removeFirst() }
        return (stem.isEmpty ? "Model" : stem) + ".tcad"
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

private enum ModelStore {
    struct Entry: Identifiable {
        let url: URL
        let name: String
        let date: Date?
        let canOpen: Bool
        var id: URL { url }
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
        defer { try? FileManager.default.removeItem(at: staging) }
        try data.write(to: staging, options: .withoutOverwriting)
        try FileManager.default.moveItem(at: staging, to: destination)
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
        defer { try? FileManager.default.removeItem(at: staging) }
        try data.write(to: staging.appendingPathComponent(payloadName), options: .withoutOverwriting)
        let metadata = try JSONEncoder().encode(CopyMetadata(version: 1, name: projectName))
        try metadata.write(to: staging.appendingPathComponent(metadataName), options: .withoutOverwriting)
        try FileManager.default.moveItem(at: staging, to: destination)
    }

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
        try FileManager.default.contentsOfDirectory(at: directory(), includingPropertiesForKeys: nil,
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
    @State private var error: String?
    @State private var modelToDelete: ModelStore.Entry?
    @State private var isLoading = true

    private var matches: [ModelStore.Entry] {
        let query = search.trimmingCharacters(in: .whitespacesAndNewlines)
        return models.filter { query.isEmpty || $0.name.localizedCaseInsensitiveContains(query) }
    }

    var body: some View {
        NavigationStack {
            List {
                if isLoading {
                    ProgressView("Modeller yükleniyor…")
                } else if models.isEmpty {
                    Text("Henüz kayıtlı model yok. Bir .tcad açıp telefona kaydedebilirsiniz.")
                        .foregroundStyle(.secondary)
                } else if matches.isEmpty {
                    Text("Eşleşen model bulunamadı.").foregroundStyle(.secondary)
                }
                ForEach(matches) { model in
                    Button {
                        if model.canOpen { open(model.url) }
                        else { error = "Bu kayıt açılamıyor. İsterseniz kayıtlı kopyayı silebilirsiniz." }
                    } label: {
                        VStack(alignment: .leading) {
                            Text(model.name)
                            if let date = model.date {
                                Text("Kaydedildi: \(date.formatted(date: .abbreviated, time: .standard))")
                                    .font(.caption).foregroundStyle(.secondary)
                            }
                        }
                        .frame(maxWidth: .infinity, minHeight: 44, alignment: .leading)
                    }
                    .swipeActions {
                        Button("Sil", role: .destructive) { modelToDelete = model }
                    }
                }
            }
            .searchable(text: $search, prompt: "Model ara")
            .navigationTitle("Modellerim")
            .toolbar { ToolbarItem(placement: .cancellationAction) { Button("Kapat") { dismiss() } } }
            .onAppear {
                DispatchQueue.global(qos: .userInitiated).async {
                    let result = Result { try ModelStore.models() }
                    DispatchQueue.main.async {
                        isLoading = false
                        switch result {
                        case .success(let entries): models = entries
                        case .failure(let failure): error = failure.localizedDescription
                        }
                    }
                }
            }
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
                Text("\(modelToDelete?.name ?? "Model") telefonunuzdaki kayıtlı modellerden silinecek. Orijinal dosya etkilenmez.")
            }
            .alert("İşlem tamamlanamadı", isPresented: Binding(get: { error != nil }, set: { if !$0 { error = nil } })) {
                Button("Tamam", role: .cancel) { error = nil }
            } message: { Text(error ?? "") }
        }
    }
}

private enum ViewerError: LocalizedError {
    case message(String)
    var errorDescription: String? { if case let .message(text) = self { return text }; return nil }
}

private struct Drawing {
    struct Point { let x: Double; let y: Double }
    struct Grain { let start: Point; let end: Point; let needsReview: Bool }
    struct Piece {
        let name: String?
        let id: UUID?
        let points: [Point]
        let closed: Bool
        let role: String?
        let size: String?
        let material: String?
        let quantity: Int?
        let grainStatus: String
        let grain: Grain?

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
            return (["Parça \(index + 1)"] + fields).joined(separator: " · ")
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
        if let array = root as? [Any], array.count == 2,
           let version = array[0] as? Int, (1...5).contains(version), let body = array[1] as? [String: Any] {
            object = body
        } else if let body = root as? [String: Any] { object = body }
        else { throw ViewerError.message("Desteklenmeyen proje biçimi.") }
        guard let name = object["Name"] as? String, let rawPieces = object["Pieces"] as? [[String: Any]], rawPieces.count <= 10_000 else {
            throw ViewerError.message("Proje adı veya parça listesi eksik ya da sınır dışında.")
        }
        var total = 0
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
                                role: metadata?["Role"] as? String, size: metadata?["Size"] as? String,
                                material: metadata?["Material"] as? String, quantity: quantity,
                                grainStatus: grainStatus, grain: parsedGrain))
        }
        return Drawing(name: name, pieces: pieces, pieceRenameIdentitiesValid: pieceRenameIdentitiesValid)
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
            pieces[index].points.count > 1 &&
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
                Text("En: \(dimensions.width.formatted(.number.precision(.fractionLength(0...2)))) • Boy: \(dimensions.height.formatted(.number.precision(.fractionLength(0...2))))")
                Text("Boyutlar dosyadaki koordinat birimindedir.")
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
    let pieces: [Drawing.Piece]
    let label: String
    @State private var zoom: CGFloat = 1
    @State private var offset: CGSize = .zero
    @State private var showGrain = true
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
                    .accessibilityHint("Yakınlaştırmak için iki parmağınızı kullanın; çizimi taşımak için sürükleyin.")
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
            .gesture(
                MagnificationGesture()
                    .updating($pinch) { value, state, _ in state = value }
                    .onEnded { value in
                        zoom = limitedZoom(zoom * value)
                        offset = limitedOffset(offset, size: geometry.size, scale: zoom)
                    }
                    .simultaneously(with: DragGesture()
                        .updating($drag) { value, state, _ in state = value.translation }
                        .onEnded { value in
                            offset = limitedOffset(
                                CGSize(width: offset.width + value.translation.width, height: offset.height + value.translation.height),
                                size: geometry.size, scale: zoom
                            )
                        })
            )
        }
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
            for piece in pieces {
                guard let first = piece.points.first else { continue }
                var path = Path()
                path.move(to: position(first))
                for point in piece.points.dropFirst() { path.addLine(to: position(point)) }
                if piece.closed && piece.points.count > 1 { path.closeSubpath() }
                context.stroke(path, with: .color(.accentColor), lineWidth: 1.5)
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
