import CoreFoundation
import CryptoKit
import Darwin
import SwiftUI
import UniformTypeIdentifiers
#if os(iOS)
import UIKit
#endif

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

// Binds a result to a SHA-256 digest of the bytes that were searched.
struct ModelContentMatch {
    let index: Int
    let detail: String
    private let digest: Data
    init(index: Int, detail: String, data: Data) {
        self.index = index; self.detail = detail; self.digest = Data(SHA256.hash(data: data))
    }
    func matches(data: Data, pieceCount: Int) -> Bool {
        index >= 0 && index < pieceCount && digest == Data(SHA256.hash(data: data))
    }
}
private enum ModelLibrarySortOrder: String, CaseIterable, Identifiable {
    case name = "Ada göre"
    case newest = "En yeni önce"
    var id: String { rawValue }
}
#if DEBUG
struct PointEditSnapshot: Identifiable {
    let id = UUID()
    let data: Data
    let piece: Drawing.Piece
    let pieceID: UUID
    let index: Int
    let projectName: String
    let identity: LoadedModelState<Drawing>.EditIdentity
}
#endif
private struct ContentView: View {
    @State private var showingDesignBrief = false
    @State private var showingImporter = false
    @State private var showingPendingRawNotice = false
    @State private var showingPieces = false
    @State private var showingPieceComparison = false
    @State private var showingPointDistance = false
    @State private var showingFullScreenDrawing = false
    @State private var showingLargePiecePicker = false
    @State private var showingNotches = false
    private var selectedNotch: (piece: Int, point: Int)? {
        get { loadedModel.selectedNotch }
        nonmutating set { loadedModel.selectedNotch = newValue }
    }
    @State private var showingLibrary = false
    @State private var librarySearch = ""
    @State private var librarySortOrder: ModelLibrarySortOrder = .name
    @State private var libraryFilter: ModelStore.LibraryFilter = .all
    @State private var librarySearchContents = false
    @State private var showingExporter = false
    @State private var showingExportWarning = false
    @State private var exportDocument: ProjectExportDocument?
    @State private var exportName = "Model.tcad"
    @State private var importedData: Data?
    @State private var loadedModel = LoadedModelState<Drawing>()
    private var sourceData: Data? { loadedModel.sourceData }
    private var project: Drawing? { loadedModel.model }
    private var drawingRevision: UUID { loadedModel.revision }
    private enum RawImportReason { case sizeLimit, invalidPreview(String) }
    private struct RawImport { let data: Data; let name: String; let request: UUID; let reason: RawImportReason }
    private enum OpenedModel { case preview(Drawing, Data, Int?); case raw(Data, RawImportReason) }
    @State private var rawImport: RawImport?
    @State private var showingRawImport = false
    @State private var rawSaveNeedsRetry = false
    @State private var rawExportDocument: ProjectExportDocument?
    @State private var rawExportName = "Kurtarılan-ham-model.tcad"
    @State private var showingRawExportWarning = false
    @State private var showingRawExporter = false
    #if DEBUG && targetEnvironment(simulator)
    @State private var didSimulateRawSaveFailure = false
    #endif
    @State private var showingNameEditor = false
    @State private var editedName = ""
    @State private var showingPieceNameEditor = false

    @State private var editedPieceName = ""
    @State private var pieceRenameSnapshot: PieceRenameSnapshot?
    @State private var importedName = ""
    @State private var showingLibraryNameEditor = false
    @State private var libraryName = ""
    @State private var librarySaveSnapshot: (data: Data, request: UUID)?
    @State private var isSaving = false
    @State private var saveNotice: String?
    @State private var error: String?
    private var selectedPiece: Int? {
        get { loadedModel.selectedPiece }
        nonmutating set { loadedModel.selectedPiece = newValue }
    }
    @State private var showGrain = true
    @State private var notchSelectionRevision = UUID()
    @State private var modelReadCancellation: ModelReadCancellation?

    var body: some View {
        ScrollView {
        VStack(spacing: 12) {
            VStack(alignment: .leading, spacing: 12) {
                HStack {
                    VStack(alignment: .leading, spacing: 3) {
                        Text("MONO DI TREE").font(.caption.weight(.semibold)).tracking(1.2)
                        Text("CAD Atölyesi").font(.title2).fontDesign(.serif)
                    }
                    Spacer()
                    if let project { Text(ModelStore.safeLabel(project.name)).font(.caption).lineLimit(2) }
                }
                Text("Modelini seç, isteğini hazırla.").font(.subheadline).foregroundStyle(.secondary)
                Button { showingDesignBrief = true } label: {
                    Label("Elbise isteği oluştur", systemImage: "plus")
                        .frame(maxWidth: .infinity, minHeight: 48)
                }
                .buttonStyle(.borderedProminent).tint(Color(red: 0.29, green: 0.08, blue: 0.14))
                .disabled(isSaving || loadedModel.load.isLoading)
                .sheet(isPresented: $showingDesignBrief) { ExperimentalDesignBriefView() }
                HStack {
                    Button(".tcad aç") {
                        if rawSaveNeedsRetry { showingPendingRawNotice = true } else { showingImporter = true }
                    }.accessibilityHint("Önizlemek için bir proje dosyası seçin").disabled(isSaving)
                    Spacer()
                    Button("Modellerim") { showingLibrary = true }.disabled(isSaving)
                }.frame(minHeight: 44)
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            .padding(16)
            .background(Color(red: 0.29, green: 0.08, blue: 0.14).opacity(0.06))
            .clipShape(RoundedRectangle(cornerRadius: 14))
            Text("Proje adını yeni bir kopyada değiştirebilirsiniz. Pastal ve üretim çıktıları desteklenmez.")
                .font(.caption).foregroundStyle(.secondary)
            if loadedModel.load.isLoading {
                ProgressView("Proje açılıyor…")
                Button("Açmayı iptal et") {
                    modelReadCancellation?.cancel()
                    loadedModel.load.invalidate()
                    saveNotice = "Model açma iptal edildi."
                }
                .accessibilityHint("Önceki açık modeli korur")
            }
            if let saveNotice { Text(saveNotice).font(.caption).foregroundStyle(.secondary) }
            if rawSaveNeedsRetry, let rawImport {
                Text("Kaydedilemeyen ham kopya: \(ModelStore.safeLabel(rawImport.name))")
                    .font(.caption).foregroundStyle(.secondary).lineLimit(2)
                Text("Kopya geçici olarak bellekte tutuluyor. Başka bir dosya açmadan önce kopyayı kaydedin veya ham kopyadan vazgeçin; uygulama kapanırsa yeniden seçmeniz gerekir.")
                    .font(.caption).foregroundStyle(.secondary)
                Button("Ham kopyayı yeniden kaydet…") { showingRawImport = true }
                    .disabled(isSaving || loadedModel.load.isLoading)
                Button("Ham kopyayı dışa aktar…") {
                    guard let rawImport else { return }
                    rawExportDocument = ProjectExportDocument(data: rawImport.data)
                    rawExportName = ModelStore.newRawExportName(source: rawImport.name)
                    showingRawExportWarning = true
                }
                .disabled(isSaving || loadedModel.load.isLoading)
                #if DEBUG && targetEnvironment(simulator)
                if ProcessInfo.processInfo.arguments.contains("--ui-test-raw-integrity"),
                   let encoded = ProcessInfo.processInfo.environment["TCAD_UI_TEST_EXPECTED_RAW"],
                   let expected = Data(base64Encoded: encoded) {
                    Text(rawImport.data == expected ? "Test: ham baytlar aynı" : "Test: ham baytlar değişti")
                        .accessibilityIdentifier("raw-integrity-status")
                }
                if ProcessInfo.processInfo.arguments.contains("--ui-test-raw-picker-lifecycle") {
                    Button("Test: seçim iptal") { handlePickerFailure(CocoaError(.userCancelled)) }
                    Button("Test: geçersiz seçim") { handlePickerFailure(ViewerError.message("Test: geçersiz seçim.")) }
                    Button("Test: okunamayan dosya") { openModel(FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString), imported: true) }
                    Button("Test: ikinci bozuk dosya") {
                        let file = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString + ".tcad")
                        do { try Data("[6,{".utf8).write(to: file); openModel(file, imported: true) }
                        catch { error = error.localizedDescription }
                    }
                }
                #endif
            }
            if importedData != nil {
                Button(isSaving ? "Kaydediliyor…" : "Bu modeli telefona kaydet") { saveImportedModel() }
                    .disabled(isSaving || loadedModel.load.isLoading)
                Button("Telefona farklı adla kaydet…") {
                    guard let data = importedData else { return }
                    librarySaveSnapshot = (data, loadedModel.load.request)
                    libraryName = project?.name ?? ""
                    showingLibraryNameEditor = true
                }
                .disabled(isSaving || loadedModel.load.isLoading)
            }
            if let project {
                if project.formatVersion == 7 {
                    Text("Sürüm 7: Yalnız görünür konturlar ve işaretler önizlenir. Döndürme geçmişi, eşleşmeler ve üretime uygunluk bu telefonda doğrulanmaz; masaüstünde kontrol edin. Özgün dosya baytları korunur.")
                        .font(.caption).foregroundStyle(.secondary)
                    if project.historyPieceIDsDiffer {
                        Text("Pastal geçmişindeki parça kimlikleri görünür parçalarla eşleşmiyor; geçmiş güncel yerleşimi temsil etmiyor. Masaüstünde yeniden kontrol edin.")
                            .font(.caption).foregroundStyle(.orange)
                    }
                }
                Button("Açık modeli dışa aktar…") {
                    guard let data = sourceData else { return }
                    // Capture the loaded bytes before presenting the system picker.
                    exportDocument = ProjectExportDocument(data: data)
                    exportName = exportFileName(project.name)
                    saveNotice = nil
                    showingExportWarning = true
                }.disabled(isSaving || loadedModel.load.isLoading || sourceData == nil)
                Button("Proje adını değiştir ve kopya kaydet") {
                    showingNameEditor = true
                }.disabled(isSaving || loadedModel.load.isLoading || sourceData == nil || project.formatVersion >= 6 || project.hasNonUTF8BOM || (sourceData?.count ?? 0) > LosslessProjectName.maximumEditBytes)
                if project.formatVersion >= 6 {
                    Text("Sürüm \(project.formatVersion) projelerinde proje adını değiştirip kopya kaydetme henüz desteklenmiyor. Açık modeli dışa aktarabilirsiniz.")
                        .font(.caption).foregroundStyle(.secondary)
                }
                if project.formatVersion < 6 && project.hasNonUTF8BOM {
                    Text("UTF-16 veya UTF-32 dosyalarında proje adını değiştirme henüz desteklenmiyor. Açık modeli özgün baytlarıyla dışa aktarabilirsiniz.")
                        .font(.caption).foregroundStyle(.secondary)
                }
                if let sourceData, sourceData.count > LosslessProjectName.maximumEditBytes {
                    Text("Proje adını değiştirmek için dosya 8 MB veya daha küçük olmalı.")
                        .font(.caption).foregroundStyle(.secondary)
                }
                if project.insufficientContourCount > 0 {
                    Text("\(project.insufficientContourCount) parça asgari nokta sayısını karşılamıyor. Kapalı kontur için üç farklı köşe gerekir; tekrarlanan noktalar ayrı sayılmaz. Bu parçalar çizilmez veya ölçülmez; dosya masaüstünde açılamayabilir. Diğer dosya koşulları doğrulanmadı.")
                        .font(.caption).foregroundStyle(.secondary)
                }
                if project.collapsedContourCount > 0 {
                    Text("Uyarı: \(project.collapsedContourCount) parçanın tüm noktaları üst üste geliyor. Bu konturların geometrisini masaüstünde kontrol edin.")
                        .font(.caption).foregroundStyle(.orange)
                }
                if project.pieces.contains(where: { !$0.notches.isEmpty }) {
                    Text("Köşe işaretleri halkayla gösterilir; kesim derinliği veya yönü belirtilmez. Önizleme üretim çıktısı değildir.")
                        .font(.caption).foregroundStyle(.secondary)
                    Text("İşaret koordinatları yaklaşık gösterilir; kesin değerler kaynak dosyada korunur.")
                        .font(.caption).foregroundStyle(.secondary)
                    Text("Sürüm \(project.formatVersion) önizlemesinde dosya ve doğrulama metni en fazla 8 MB olabilir. UTF-8 ve BOM'lu UTF-16/UTF-32 desteklenir; tüm masaüstü proje kuralları doğrulanmaz.")
                        .font(.caption).foregroundStyle(.secondary)
                    Button("Köşe işaretlerini ara ve seç…") { showingNotches = true }
                        .disabled(loadedModel.load.isLoading)
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
                        let coordinates = NotchSearch.label(index: selectedNotch.point, x: NotchSearch.coordinate(point.x), y: NotchSearch.coordinate(point.y))
                        Text(piece.hasMinimumContourPoints
                             ? "Seçili köşe işareti: \(coordinates). Önizlemede sarı halka ile gösterilir."
                             : "Seçili köşe işareti: \(coordinates). Konturda yeterli nokta olmadığı için önizleme gösterilemiyor.")
                            .font(.caption).foregroundStyle(.secondary)
                    }
                    if NotchSearch.exceedsDrawingLimit(project.pieces.enumerated().map { index, piece in
                        (visible: piece.hasMinimumContourPoints && (selectedPiece == nil || selectedPiece == index), count: piece.notches.count)
                    }, limit: DrawingView.maximumVisibleNotches) {
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
                    .disabled(loadedModel.load.isLoading)
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
                    Button("İki parçayı karşılaştır…") { showingPieceComparison = true }
                        .disabled(loadedModel.load.isLoading || project.pieces.count < 2)
                        .sheet(isPresented: $showingPieceComparison) {
                            PieceComparisonView(pieces: project.pieces, projectName: project.name, initialIndex: selectedPiece)
                        }
                    HStack {
                        Button("Önceki parça") {
                            guard let current = selectedPiece, current > 0 else { return }
                            selectedNotch = nil
                            selectedPiece = current - 1
                        }
                        .disabled(loadedModel.load.isLoading || selectedPiece == nil || selectedPiece == 0)
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
                        .disabled(loadedModel.load.isLoading || selectedPiece == project.pieces.count - 1)
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
                            .disabled(loadedModel.load.isLoading || NotchSearch.adjacent(in: notches, current: current, forward: false) == nil)
                            Spacer()
                            Button("Sonraki işaret") {
                                if let point = NotchSearch.adjacent(in: notches, current: current, forward: true) {
                                    selectedNotch = (selectedPiece, point)
                                    notchSelectionRevision = UUID()
                                }
                            }
                            .disabled(loadedModel.load.isLoading || NotchSearch.adjacent(in: notches, current: current, forward: true) == nil)
                        }
                        .buttonStyle(.bordered)
                        .accessibilityHint("Seçili parçanın işaretleri arasında dosya sırasıyla gezinin")
                    }
                    if let selectedPiece {
                        Button("İki nokta arasını ölç…") { showingPointDistance = true }
                            .disabled(loadedModel.load.isLoading || project.pieces[selectedPiece].points.count < 2)
                            .sheet(isPresented: $showingPointDistance) {
                                PointDistanceView(piece: project.pieces[selectedPiece], name: project.pieces[selectedPiece].displayName(index: selectedPiece))
                            }
                        #if DEBUG
                        Button("Deneysel nokta düzenle…") {
                            guard let data = sourceData, let id = project.pieces[selectedPiece].id,
                                  let identity = loadedModel.editIdentity() else { return }
                            loadedModel.pointEditSnapshot = PointEditSnapshot(data: data, piece: project.pieces[selectedPiece], pieceID: id,
                                index: selectedPiece, projectName: project.name, identity: identity)
                        }
                            .disabled(isSaving || loadedModel.load.isLoading || sourceData == nil || project.formatVersion == 7 || project.hasNonUTF8BOM || project.pieces[selectedPiece].id == nil || project.pieces[selectedPiece].points.isEmpty || project.pieces[selectedPiece].points.count > 4096 || (sourceData?.count ?? 0) > 8 * 1024 * 1024)
                            .sheet(item: Binding(get: { loadedModel.pointEditSnapshot }, set: { _ in })) { snapshot in
                                ExperimentalPointEditor(data: snapshot.data, piece: snapshot.piece, pieceID: snapshot.pieceID, projectName: snapshot.projectName,
                                    isCurrent: { loadedModel.isCurrent(snapshot.identity, data: snapshot.data) },
                                    invalidateForTesting: { loadedModel.load.invalidate() },
                                    onSessionClosed: { loadedModel.closePointEditSession(snapshot.id) })
                                    .id(snapshot.id)
                            }
                        Text("Deneysel nokta düzenleme yalnız seçili, kimlikli ve en fazla 4096 noktalı parçada kullanılabilir; desteklenmeyen proje alanları reddedilir.")
                            .font(.caption).foregroundStyle(.secondary)
                        if project.formatVersion == 7 {
                            Text("Sürüm 7 dosyalarında nokta ve parça adı düzenleme desteklenmiyor; özgün dosya dışa aktarılabilir.")
                                .font(.caption).foregroundStyle(.secondary)
                        }
                        if project.hasNonUTF8BOM {
                            Text("UTF-16 veya UTF-32 dosyalarında nokta düzenleme henüz desteklenmiyor. Açık modeli özgün baytlarıyla dışa aktarabilirsiniz.")
                                .font(.caption).foregroundStyle(.secondary)
                        }
                        Button("Parça adını değiştir ve kopya kaydet") {
                            guard let data = sourceData, let name = project.pieces[selectedPiece].name else { return }
                            // Keep a failed edit available for retry on the same snapshot.
                            if pieceRenameSnapshot?.data != data || pieceRenameSnapshot?.index != selectedPiece {
                                editedPieceName = name
                            }
                            pieceRenameSnapshot = PieceRenameSnapshot(data: data, index: selectedPiece, name: name, id: project.pieces[selectedPiece].id, projectName: project.name)
                            showingPieceNameEditor = true
                        }
                        .disabled(isSaving || loadedModel.load.isLoading || sourceData == nil || project.formatVersion == 7 || project.hasNonUTF8BOM || (sourceData?.count ?? 0) > LosslessProjectName.maximumEditBytes || !project.pieceRenameIdentitiesValid)
                        if project.hasNonUTF8BOM {
                            Text("UTF-16 veya UTF-32 dosyalarında parça adını değiştirme henüz desteklenmiyor. Açık modeli özgün baytlarıyla dışa aktarabilirsiniz.")
                                .font(.caption).foregroundStyle(.secondary)
                        }
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
                            .disabled(loadedModel.load.isLoading)
                        DrawingView(
                        pieces: selectedPiece.map { [project.pieces[$0]] } ?? project.pieces.filter { $0.hasMinimumContourPoints },
                        label: selectedPiece.map { "\(project.pieces[$0].displayName(index: $0)) kontur önizlemesi" } ?? "\(project.pieces.filter { $0.hasMinimumContourPoints }.count) parçalı kontur önizlemesi",
                        highlightedNotch: selectedNotch?.piece == selectedPiece ? selectedNotch?.point : nil,
                        selectionRevision: notchSelectionRevision, showGrain: $showGrain
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
                                .disabled(loadedModel.load.isLoading || selectedPiece == nil || selectedPiece == 0)
                                Spacer()
                                Button { showingLargePiecePicker = true } label: {
                                    HStack(spacing: 4) {
                                        Text(selectedPiece.map { project.pieces[$0].displayName(index: $0) } ?? "Tüm parçalar")
                                            .font(.headline).lineLimit(2)
                                        Image(systemName: "chevron.up.chevron.down")
                                    }
                                }
                                .disabled(loadedModel.load.isLoading)
                                .accessibilityLabel("Büyük görünümde parça seç")
                                .accessibilityValue(selectedPiece.map { project.pieces[$0].displayName(index: $0) } ?? "Tüm parçalar")
                                Spacer()
                                Button("Sonraki parça") {
                                    let next = selectedPiece.map { $0 + 1 } ?? 0
                                    guard next < project.pieces.count else { return }
                                    selectedNotch = nil
                                    selectedPiece = next
                                }
                                .disabled(loadedModel.load.isLoading || selectedPiece == project.pieces.count - 1)
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
                                    .disabled(loadedModel.load.isLoading || NotchSearch.adjacent(in: piece.notches, current: current, forward: false) == nil)
                                    Spacer()
                                    Button("Sonraki işaret") {
                                        if let point = NotchSearch.adjacent(in: piece.notches, current: current, forward: true) {
                                            selectedNotch = (selectedPiece, point)
                                            notchSelectionRevision = UUID()
                                        }
                                    }
                                    .disabled(loadedModel.load.isLoading || NotchSearch.adjacent(in: piece.notches, current: current, forward: true) == nil)
                                }
                                .buttonStyle(.bordered)
                                .padding(.horizontal)
                                if let current {
                                    let point = piece.points[current]
                                    Text("Seçili işaret: \(NotchSearch.label(index: current, x: NotchSearch.coordinate(point.x), y: NotchSearch.coordinate(point.y)))")
                                        .font(.caption).foregroundStyle(.secondary)
                                        .frame(maxWidth: .infinity, alignment: .leading)
                                        .padding(.horizontal)
                                        .accessibilityIdentifier("largePreviewNotchLabel")
                                    Text("Koordinatlar yaklaşık gösterilir; kesin değerler kaynak dosyada korunur.")
                                        .font(.caption).foregroundStyle(.secondary).padding(.horizontal)
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
                                drawingHeight: max(320, geometry.size.height - 180), showGrain: $showGrain
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
                    throw ViewerError.message("Lütfen bir .tcad dosyası seçin.")
                }
                openModel(url, imported: true)
            } catch {
                handlePickerFailure(error)
            }
        }
        .alert("Ham kurtarma bekliyor", isPresented: $showingPendingRawNotice) {
            Button("Tamam", role: .cancel) { }
        } message: {
            Text("Yeni dosya seçmeden önce ham kopyayı yeniden kaydedin veya ham kopyadan açıkça vazgeçin. Dışa aktarırsanız da devam etmek için bellekteki kopyadan ayrıca vazgeçmeniz gerekir. Bekleyen kopya bellekte korunuyor.")
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
        .confirmationDialog("Ham kopyayı dışa aktar", isPresented: $showingRawExportWarning, titleVisibility: .visible) {
            Button("Farklı hedef seçeceğim") { showingRawExporter = true }
            Button("Vazgeç", role: .cancel) { rawExportDocument = nil }
        } message: {
            Text("Bu ham kopya doğrulanmadı ve bozuk olabilir. Açılış anındaki eski baytlar dışa aktarılır. Kaynağın sonraki değişiklikleri üzerine yazılabilir ve kaybolabilir. Farklı bir dosya adı ve konumu seçin. Dosya sağlayıcısındaki farklı adlar aynı dosyayı gösterebilir; uygulama hedefi yazmadan önce doğrulayamaz.")
        }
        .fileExporter(isPresented: $showingRawExporter, document: rawExportDocument, contentType: .data, defaultFilename: rawExportName) { result in
            rawExportDocument = nil
            switch result {
            case .success: saveNotice = "Ham kopya dışa aktarıldı; içerik doğrulanmadı."
            case .failure(let failure):
                if !isFilePickerCancellation(failure) { error = failure.localizedDescription }
            }
        }
        .onChange(of: showingRawExporter) { presented in
            if !presented { rawExportDocument = nil }
        }
        .alert("Kayıtlı model adı", isPresented: $showingLibraryNameEditor) {
            TextField("Model adı", text: $libraryName)
            Button("İptal", role: .cancel) { librarySaveSnapshot = nil }
            Button("Telefona kaydet") { saveImportedModelWithName() }
                .disabled(libraryName.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty || libraryName.utf16.count > 1024)
        } message: {
            Text("Bu ad Modellerim listesinde görünür; dosyanın içindeki proje adı değişmez. En fazla 1024 UTF-16 birimi kullanın.")
        }
        .alert("Dosya önizlenemedi", isPresented: $showingRawImport) {
            Button(rawSaveNeedsRetry ? "Ham kopyadan vazgeç" : "Vazgeç", role: .cancel) { rawImport = nil; rawSaveNeedsRetry = false }
            Button("Ham kopyayı telefona kaydet") { saveRawImport() }
        } message: {
            if rawSaveNeedsRetry { Text("Ham kopyadan vazgeçerseniz bellekteki kaydedilmemiş kopya silinir; kaynak dosya değiştirilmez.") }
            if case .sizeLimit? = rawImport?.reason {
                Text("Sürüm 6 veya 7 dosyası veya doğrulama için çözülen metni 8 MB sınırını aşıyor. Dosya doğrulanmadı ve model olarak açılmadı. İsterseniz en fazla 64 MB'lık ham kopyayı kurtarma kaydı olarak saklayabilirsiniz; bu kopya uygulamada önizlenemez.")
            } else if case .invalidPreview(let reason)? = rawImport?.reason {
                Text("\(reason) Dosya doğrulanmadı ve model olarak açılmadı. İsterseniz en fazla 64 MB'lık ham kopyayı kurtarma kaydı olarak saklayabilirsiniz; bu kopya uygulamada önizlenemez.")
            } else {
                Text("Dosya güvenle önizlenemedi; bozuk veya desteklenmeyen bir biçimde olabilir. Dosya doğrulanmadı ve model olarak açılmadı. İsterseniz en fazla 64 MB'lık ham kopyayı kurtarma kaydı olarak saklayabilirsiniz; bu kopya uygulamada önizlenemez.")
            }
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
            ModelLibrary(search: $librarySearch, sortOrder: $librarySortOrder,
                         libraryFilter: $libraryFilter, searchContents: $librarySearchContents,
                         pendingRawRecovery: rawSaveNeedsRetry) { entry, match in
                showingLibrary = false
                openModel(ModelStore.payloadURL(entry.url), imported: false, listedEntry: entry, matchedPiece: match)
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
                selectedNotch = nil
                loadedModel.install(drawing, data: data)
                editedName = drawing.name
                if ProcessInfo.processInfo.arguments.contains("--ui-test-library-fixture") {
                    importedData = data
                    importedName = ProcessInfo.processInfo.environment["TCAD_UI_TEST_FILENAME"] ?? "Deneme Modeli.tcad"
                }
                if ProcessInfo.processInfo.arguments.contains("--ui-test-large-v6-import") {
                    let largeURL = FileManager.default.temporaryDirectory.appendingPathComponent("Büyük-\(UUID().uuidString).tcad")
                    let utf16 = ProcessInfo.processInfo.arguments.contains("--ui-test-large-v6-utf16be")
                    let expansion = ProcessInfo.processInfo.arguments.contains("--ui-test-v6-utf16-expansion")
                    let leading = ProcessInfo.processInfo.arguments.contains("--ui-test-v6-long-header") ? String(repeating: " ", count: 5000) : ""
                    let width = utf16 ? 2 : 1
                    let text: String
                    if expansion {
                        let name = String(repeating: "€", count: 1000)
                        let pieces = (0..<2800).map { index in
                            let id = String(format: "00000000-0000-0000-0000-%012d", index + 1)
                            let mark = index == 0 ? #", "Notches":[{"VertexIndex":0,"Position":{"X":0,"Y":0}}]"# : ""
                            return #"{"Id":"\#(id)","Name":"\#(name)","Closed":false,"Points":[{"X":0,"Y":0},{"X":1,"Y":1}]\#(mark)}"#
                        }.joined(separator: ",")
                        text = leading + #"[6,{"Name":"Büyük","Pieces":[\#(pieces)],"GradingRules":[]}]"#
                    } else {
                        text = leading + "[6," + String(repeating: " ", count: LosslessProjectName.maximumEditBytes / width)
                            + #"{"Name":"Büyük","Pieces":[{"Name":"Kol","Closed":false,"Points":[{"X":0,"Y":0},{"X":1,"Y":1}],"Notches":[{"VertexIndex":0,"Position":{"X":0,"Y":0}}]}],"GradingRules":[]}]"#
                    }
                    var large = utf16 ? Data([0xfe, 0xff]) : Data()
                    guard let encoded = text.data(using: utf16 ? .utf16BigEndian : .utf8) else { throw ViewerError.message("Test verisi kodlanamadı.") }
                    large.append(encoded)
                    try large.write(to: largeURL)
                    openModel(largeURL, imported: true)
                }
                if ProcessInfo.processInfo.arguments.contains("--ui-test-invalid-v6-import") {
                    let invalidURL = FileManager.default.temporaryDirectory.appendingPathComponent("Eksik-\(UUID().uuidString).tcad")
                    try Data("[6,{\"Name\":\"kesik\"".utf8).write(to: invalidURL)
                    openModel(invalidURL, imported: true)
                }
                if ProcessInfo.processInfo.arguments.contains("--ui-test-unknown-v6-notch") {
                    let unknownURL = FileManager.default.temporaryDirectory.appendingPathComponent("İşaret-\(UUID().uuidString).tcad")
                    let unknown = #"[6,{"Name":"İşaretli","Pieces":[{"Name":"Kol","Closed":false,"Points":[{"X":0,"Y":0},{"X":1,"Y":1}],"Notches":[{"VertexIndex":0,"Position":{"X":0,"Y":0},"Depth":2}]}],"GradingRules":[]}]"#
                    try Data(unknown.utf8).write(to: unknownURL)
                    openModel(unknownURL, imported: true)
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

    private func handlePickerFailure(_ failure: Error) {
        guard loadedModel.load.rejectPickerFailure(failure) else { return }
        modelReadCancellation?.cancel()
        // A rejected picker result did not open a replacement file. Retain a
        // failed raw-save snapshot explicitly under the new request generation.
        if rawSaveNeedsRetry, let retained = rawImport {
            rawImport = RawImport(data: retained.data, name: retained.name, request: loadedModel.load.request, reason: retained.reason)
        }
        error = failure.localizedDescription
    }

    private func openModel(_ url: URL, imported: Bool, listedEntry: ModelStore.Entry? = nil, matchedPiece: ModelContentMatch? = nil) {
        guard !rawSaveNeedsRetry else {
            error = "Önce kaydedilemeyen ham kopyayı yeniden kaydedin veya ham kopyadan açıkça vazgeçin. Önceki kopya bellekte korunuyor."
            return
        }
        saveNotice = nil
        // Retain a failed raw save until a replacement has actually opened.
        modelReadCancellation?.cancel()
        let cancellation = ModelReadCancellation()
        modelReadCancellation = cancellation
        let request = loadedModel.load.begin()
        // Acquire the picker grant while still handling its result. Keep it
        // alive until the background coordinated read has finished.
        let access = imported && url.startAccessingSecurityScopedResource()
        DispatchQueue.global(qos: .userInitiated).async {
            defer { if access { url.stopAccessingSecurityScopedResource() } }
            #if DEBUG && targetEnvironment(simulator)
            if ProcessInfo.processInfo.arguments.contains("--ui-test-open-delay") { Thread.sleep(forTimeInterval: 3) }
            #endif
            let loaded = Result { () -> OpenedModel in
                let data: Data
                if let listedEntry { data = try ModelStore.readListedPayload(listedEntry, cancelled: { cancellation.isCancelled }) }
                else { data = try Drawing.readData(url, cancelled: { cancellation.isCancelled }) }
                if cancellation.isCancelled { throw CancellationError() }
                if imported && (LosslessProjectName.requiresVersionSixRawRecovery(data) || LosslessProjectName.requiresVersionSixRawRecovery(data, version: 7)) { return .raw(data, .sizeLimit) }
                if cancellation.isCancelled { throw CancellationError() }
                do {
                    let drawing = try Drawing.load(data, cancelled: { cancellation.isCancelled })
                    if let matchedPiece, !matchedPiece.matches(data: data, pieceCount: drawing.pieces.count) {
                        throw ViewerError.message("Kayıt aramadan sonra değişti; model listesini yenileyip tekrar arayın.")
                    }
                    return .preview(drawing, data, matchedPiece?.index)
                }
                catch {
                    if error is CancellationError || cancellation.isCancelled { throw CancellationError() }
                    if imported { return .raw(data, .invalidPreview(error.localizedDescription)) }
                    throw error
                }
            }
            DispatchQueue.main.async {
                switch loaded {
                case .success(.preview(let drawing, let data, let matchedIndex)):
                    guard loadedModel.completePreview(request, model: drawing, data: data, selectedPiece: matchedIndex) else { return }
                    rawImport = nil
                    rawSaveNeedsRetry = false
                    #if DEBUG
                    pieceRenameSnapshot = nil
                    #endif
                    showingPieces = false
                    showingNotches = false
                    selectedNotch = nil
                    editedName = drawing.name
                    importedData = imported ? data : nil
                    importedName = url.lastPathComponent
                case .success(.raw(let data, let reason)):
                    guard loadedModel.load.finish(request) else { return }
                    rawSaveNeedsRetry = false
                    rawImport = RawImport(data: data, name: url.lastPathComponent, request: request, reason: reason)
                    showingRawImport = true
                case .failure(let failure):
                    guard loadedModel.load.finish(request) else { return }
                    if rawSaveNeedsRetry, let retained = rawImport {
                        rawImport = RawImport(data: retained.data, name: retained.name, request: request, reason: retained.reason)
                    }
                    error = failure.localizedDescription
                }
            }
        }
    }

    private func saveRawImport() {
        guard let candidate = rawImport, candidate.request == loadedModel.load.request, !loadedModel.load.isLoading else {
            error = "Seçim değişti veya yükleme sürüyor; dosyayı yeniden seçin."
            return
        }
        saveNotice = nil
        isSaving = true
        #if DEBUG && targetEnvironment(simulator)
        let simulateFailure = ProcessInfo.processInfo.arguments.contains("--ui-test-raw-save-fail-once") && !didSimulateRawSaveFailure
        if simulateFailure { didSimulateRawSaveFailure = true }
        #endif
        DispatchQueue.global(qos: .userInitiated).async {
            let result = Result {
                #if DEBUG && targetEnvironment(simulator)
                if simulateFailure { throw ViewerError.message("Test: ham kopya kaydedilemedi.") }
                #endif
                let savedURL = try ModelStore.saveRaw(candidate.data, name: candidate.name)
                #if DEBUG && targetEnvironment(simulator)
                if ProcessInfo.processInfo.arguments.contains("--ui-test-raw-integrity") {
                    let entries = try ModelStore.models()
                    guard let saved = entries.first(where: { $0.url == savedURL }),
                          saved.url.pathExtension.lowercased() == "tcadraw",
                          try ModelStore.readListedPayload(saved) == candidate.data else {
                        throw ViewerError.message("Test: bu kaydın ham baytları eşleşmedi.")
                    }
                }
                #else
                _ = savedURL
                #endif
            }
            DispatchQueue.main.async {
                isSaving = false
                switch result {
                case .success:
                    if rawImport?.request == candidate.request { rawImport = nil; rawSaveNeedsRetry = false }
                    saveNotice = "Ham kopya Modellerim bölümüne kaydedildi; önizlenemiyor."
                case .failure(let failure):
                    if rawImport?.request == candidate.request { rawSaveNeedsRetry = true }
                    error = failure.localizedDescription
                }
            }
        }
    }

    private func saveRenamedCopy() {
        guard let data = sourceData, let currentProject = project, currentProject.formatVersion < 6, !currentProject.hasNonUTF8BOM else { return }
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
        guard let snapshot = pieceRenameSnapshot, sourceData == snapshot.data, selectedPiece == snapshot.index,
              !LosslessProjectName.hasNonUTF8BOM(snapshot.data) else {
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
        guard !loadedModel.load.isLoading, loadedModel.load.request == snapshot.request,
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
                    if loadedModel.load.request == snapshot.request && importedData == snapshot.data {
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
        let request = loadedModel.load.request
        saveNotice = nil
        isSaving = true
        DispatchQueue.global(qos: .userInitiated).async {
            let result = Result { try ModelStore.save(data, name: name) }
            DispatchQueue.main.async {
                isSaving = false
                switch result {
                case .success:
                    if loadedModel.load.request == request && importedData == data {
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
    // Display only: make line breaks and directional formatting explicit.
    // The stored name, path, and payload remain untouched.
    static func newRawExportName(source: String) -> String {
        var name: String
        repeat { name = "Kurtarılan-ham-\(UUID().uuidString).tcad" }
        while name.caseInsensitiveCompare(source) == .orderedSame
        return name
    }
    static func safeLabel(_ value: String) -> String {
        var result = ""
        for scalar in value.unicodeScalars {
            if scalar.value == 0x5c {
                result += "\\\\"
                continue
            }
            // These format characters are needed for ordinary script shaping
            // and emoji sequences; unlike bidi controls they do not reorder text.
            if scalar.value == 0x200c || scalar.value == 0x200d {
                result.unicodeScalars.append(scalar)
                continue
            }
            switch scalar.properties.generalCategory {
            case .control, .format, .lineSeparator, .paragraphSeparator:
                result += String(format: "\\u{%X}", scalar.value)
            default:
                result.unicodeScalars.append(scalar)
            }
        }
        return result
    }

    struct FileIdentity: Equatable {
        let device: Int64, inode: UInt64, size: Int64, mode: UInt32
        let modifiedSeconds: Int64, modifiedNanoseconds: Int64
        let changedSeconds: Int64, changedNanoseconds: Int64
    }
    struct EntryIdentity: Equatable {
        let entry: FileIdentity
        let payload: FileIdentity?
        let metadata: FileIdentity?
        let partialPayloadName: String?
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
        var safeName: String { ModelStore.safeLabel(name) }
        var selectionStatus: String? {
            if canOpen { return nil }
            if url.pathExtension.lowercased() == "partial" { return "Tamamlanmamış kayıt · Yalnız ham kurtarma" }
            return requiresRecovery ? "Önizlenemiyor · Ham kopya kurtarma denenebilir" : "Açılamıyor · Kayıt kontrol edilmeli"
        }
        var readableIdentity: String {
            let stem = url.deletingPathExtension().lastPathComponent
            let candidate = stem.range(of: " - ", options: .backwards).map { String(stem[$0.upperBound...]) } ?? stem
            guard let uuid = UUID(uuidString: candidate) else { return displayIdentity }
            return "\(url.pathExtension.lowercased()) · \(uuid.uuidString)"
        }
    }
    struct CopyMetadata: Codable {
        let version: Int
        let name: String
    }

    private static let copyExtension = "tcadcopy"
    private static let rawExtension = "tcadraw"
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
        _ = try save(data, name: name, fileExtension: "tcad")
    }

    @discardableResult static func saveRaw(_ data: Data, name: String) throws -> URL {
        try save(data, name: name, fileExtension: rawExtension)
    }

    private static func save(_ data: Data, name: String, fileExtension: String) throws -> URL {
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
        let destination = directory.appendingPathComponent("\(safeStem) - \(UUID().uuidString).\(fileExtension)")
        let staging = directory.appendingPathComponent(".\(UUID().uuidString).partial")
        try ModelFilePublisher.publish(data, staging: staging, destination: destination)
        return destination
    }

    static func saveRenamed(_ data: Data, projectName: String) throws {
        guard data.count <= 64 * 1024 * 1024 else { throw ViewerError.message("Dosya 64 MB sınırını aşıyor.") }
        let directory = try directory()
        let id = UUID().uuidString
        let staging = directory.appendingPathComponent(".\(id).partial", isDirectory: true)
        let destination = directory.appendingPathComponent("\(id).\(copyExtension)", isDirectory: true)
        let metadata = try JSONEncoder().encode(CopyMetadata(version: 1, name: projectName))
        try ModelFilePublisher.writeCopyFiles(data, metadata: metadata, staging: staging)
        try FileManager.default.moveItem(at: staging, to: destination)
    }

    #if DEBUG
    struct PreparedPointCopy {
        let staging: URL
        let destination: URL
        let identity: stat
    }
    static func preparePointCopy(_ data: Data, projectName: String,
                                 directoryOverride: URL? = nil, id: UUID = UUID(),
                                 beforeVerification: (() throws -> Void)? = nil,
                                 afterOpen: (() throws -> Void)? = nil) throws -> PreparedPointCopy {
        guard data.count <= 8 * 1024 * 1024 else { throw ViewerError.message("Dosya 8 MB sınırını aşıyor.") }
        let directory: URL
        if let directoryOverride { directory = directoryOverride } else { directory = try self.directory() }
        let staging = directory.appendingPathComponent(".\(id.uuidString).partial", isDirectory: true)
        let destination = directory.appendingPathComponent("\(id.uuidString).\(copyExtension)", isDirectory: true)
        let metadata = try JSONEncoder().encode(CopyMetadata(version: 1, name: projectName))
        let identity = try ModelFilePublisher.writeCopyFiles(data, metadata: metadata, staging: staging,
                                                              beforeVerification: beforeVerification, afterOpen: afterOpen)
        return PreparedPointCopy(staging: staging, destination: destination, identity: identity)
    }
    static func finishPointCopy(_ prepared: PreparedPointCopy) throws {
        try ModelFilePublisher.verifyStagingIdentity(prepared.staging, expected: prepared.identity)
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

    enum MetadataResult {
        case valid(CopyMetadata), oversized, invalid
    }

    static func metadata(_ url: URL, expected: FileIdentity?, afterFirstChunk: (() throws -> Void)? = nil) -> MetadataResult {
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
            guard let expected, fileIdentity(info) == expected else { return .invalid }
            var data = Data(), didRead = false
            while data.count <= limit {
                let chunk = try file.read(upToCount: min(8192, limit + 1 - data.count)) ?? Data()
                if chunk.isEmpty { break }
                data.append(chunk)
                if !didRead { didRead = true; try afterFirstChunk?() }
            }
            var final = stat()
            guard fstat(descriptor, &final) == 0, fileIdentity(final) == expected else { return .invalid }
            if data.count > limit { return .oversized }
            guard Int64(data.count) == Int64(final.st_size) else { return .invalid }
            try LosslessProjectName.validateAuxiliaryJSONKeys(data, limit: limit)
            guard let object = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
                  Set(object.keys) == Set(["version", "name"]),
                  let metadata = try? JSONDecoder().decode(CopyMetadata.self, from: data), metadata.version == 1 else { return .invalid }
            return .valid(metadata)
        } catch { return .invalid }
    }

    static func models(cancelled: () -> Bool = { false }) throws -> [Entry] {
        try models(in: directory(), cancelled: cancelled)
    }

    static func models(in directory: URL, cancelled: () -> Bool = { false }) throws -> [Entry] {
        if cancelled() { throw CancellationError() }
        let urls = try FileManager.default.contentsOfDirectory(at: directory, includingPropertiesForKeys: nil, options: [])
        let entries: [Entry] = try urls.compactMap { url in
                if cancelled() { throw CancellationError() }
                let initialIdentity = identity(url)
                let name: String
                let canOpen: Bool
                let requiresRecovery: Bool
                if url.pathExtension.lowercased() == "tcad" {
                    let regular = initialIdentity.map { ($0.entry.mode & UInt32(S_IFMT)) == UInt32(S_IFREG) } ?? false
                    name = regular ? displayName(url) : "Dosya değil (\(url.lastPathComponent))"
                    canOpen = regular
                    requiresRecovery = false
                } else if url.pathExtension.lowercased() == rawExtension {
                    let regular = initialIdentity.map { ($0.entry.mode & UInt32(S_IFMT)) == UInt32(S_IFREG) } ?? false
                    name = regular ? "Ham kayıt: \(displayName(url))" : "Dosya değil (\(url.lastPathComponent))"
                    canOpen = false
                    requiresRecovery = regular
                } else if url.pathExtension.lowercased() == copyExtension {
                    let directory = initialIdentity.map { ($0.entry.mode & UInt32(S_IFMT)) == UInt32(S_IFDIR) } ?? false
                    let hasPayload = directory && (initialIdentity?.payload.map { ($0.mode & UInt32(S_IFMT)) == UInt32(S_IFREG) } ?? false)
                    switch (directory ? metadata(url, expected: initialIdentity?.metadata) : .invalid, hasPayload) {
                    case (.valid(let metadata), true):
                        name = metadata.name.isEmpty ? "Model" : metadata.name; canOpen = true; requiresRecovery = false
                    case (.oversized, true), (.invalid, true):
                        name = "Kurtarma gerekli (\(url.deletingPathExtension().lastPathComponent))"
                        canOpen = false; requiresRecovery = true
                    default:
                        name = "Okunamayan model (\(url.deletingPathExtension().lastPathComponent))"
                        canOpen = false
                        requiresRecovery = false
                    }
                } else if url.pathExtension.lowercased() == "partial" {
                    let stem = url.deletingPathExtension().lastPathComponent
                    guard stem.first == ".", UUID(uuidString: String(stem.dropFirst())) != nil,
                          let current = initialIdentity,
                          (current.entry.mode & UInt32(S_IFMT)) == UInt32(S_IFDIR),
                          let payload = current.payload, (payload.mode & UInt32(S_IFMT)) == UInt32(S_IFREG),
                          current.partialPayloadName != nil else { return nil }
                    name = "Tamamlanmamış kayıt (\(stem.dropFirst()))"
                    canOpen = false; requiresRecovery = true
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
        if cancelled() { throw CancellationError() }
        let sorted = entries.sorted { $0.name.localizedStandardCompare($1.name) == .orderedAscending }
        if cancelled() { throw CancellationError() }
        return sorted
    }

    static func savedDate(_ url: URL) -> Date? {
        try? url.resourceValues(forKeys: [.creationDateKey]).creationDate
    }

    enum LibraryFilter: String, CaseIterable, Identifiable {
        case all = "Tüm kayıtlar"
        case normal = "Açma seçeneği olanlar"
        case attention = "Kontrol gerekenler"
        var id: String { rawValue }
    }

    static func filteredModels(_ entries: [Entry], query: String, newest: Bool, filter: LibraryFilter = .all,
                               cancelled: () -> Bool) -> [Entry]? {
        let words = query.split(whereSeparator: { $0.isWhitespace }).map(String.init)
        var matches: [Entry] = []
        for entry in entries {
            if cancelled() { return nil }
            if filter == .normal && !entry.canOpen { continue }
            if filter == .attention && entry.canOpen { continue }
            if words.isEmpty { matches.append(entry); continue }
            let fields = [entry.name, entry.safeName, entry.displayIdentity, entry.readableIdentity,
                          safeLabel(entry.displayIdentity), safeLabel(entry.readableIdentity)]
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

    struct ContentSearchBudget {
        let maximumFiles: Int
        let maximumBytes: Int64
        let deadline: TimeInterval
        private(set) var files = 0
        private(set) var bytes: Int64 = 0
        init(maximumFiles: Int = 100, maximumBytes: Int64 = 32 * 1024 * 1024,
             start: TimeInterval, duration: TimeInterval = 5) {
            self.maximumFiles = maximumFiles
            self.maximumBytes = maximumBytes
            self.deadline = start + duration
        }
        mutating func reserve(bytes size: Int64, now: TimeInterval) -> Bool {
            guard size >= 0, files < maximumFiles, now < deadline,
                  size <= maximumBytes - bytes else { return false }
            files += 1
            bytes += size
            return true
        }
    }

    struct ContentPageNavigation {
        private(set) var offset = 0
        private(set) var history: [Int] = []
        mutating func advance(to next: Int) {
            guard next > offset else { return }
            history.append(offset)
            offset = next
        }
        mutating func previous() {
            guard let prior = history.popLast() else { return }
            offset = prior
        }
        mutating func reset() { offset = 0; history.removeAll() }
    }
    struct ContentPageGate {
        let start: Int
        private(set) var next: Int?
        private(set) var stalled = false
        init(start: Int) { self.start = start }
        func includes(_ index: Int) -> Bool { index >= start && next == nil && !stalled }
        mutating func shouldScan(index: Int, bytes: Int64, now: TimeInterval, budget: inout ContentSearchBudget) -> Bool {
            guard includes(index) else { return false }
            guard budget.reserve(bytes: bytes, now: now) else {
                if index == start { stalled = true } else { next = index }
                return false
            }
            return true
        }
    }
    static func acceptsSearchResult<Key: Equatable>(request: Key, current: Key, cancelled: Bool) -> Bool {
        !cancelled && request == current
    }
    static func contentSearchMatches(query: String, fields: [String]) -> Bool {
        let words = query.split(whereSeparator: { $0.isWhitespace }).map(String.init)
        return !words.isEmpty && words.allSatisfy { word in
            fields.contains { $0.localizedStandardContains(word) || safeLabel($0).localizedStandardContains(word) }
        }
    }

    static func searchablePieceFields(name: String?, size: String?, material: String?, index: Int) -> [String] {
        let displayName = name.flatMap { $0.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty ? nil : $0 } ?? "Parça \(index + 1)"
        return [displayName, size, material].compactMap { $0 }
    }
    // All piece terms must match one piece; model terms may match its record.
    static func pieceSearchMatches(query: String, modelFields: [String], pieceFields: [String]) -> Bool {
        contentSearchMatches(query: query, fields: modelFields + pieceFields)
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

    // Detect common concurrent in-place changes while reading an imported file.
    // Metadata checks are not an atomic snapshot against uncoordinated writers.
    static func readFileSnapshot(_ url: URL, expectedIdentity: FileIdentity? = nil, cancelled: () -> Bool = { false }, afterFirstChunk: (() throws -> Void)? = nil) throws -> Data {
        let limit = 64 * 1024 * 1024
        if cancelled() { throw CancellationError() }
        let descriptor = url.path.withCString { Darwin.open($0, O_RDONLY | O_NONBLOCK) }
        guard descriptor >= 0 else { throw POSIXError(POSIXErrorCode(rawValue: errno) ?? .EIO) }
        var initial = stat()
        guard fstat(descriptor, &initial) == 0, (initial.st_mode & S_IFMT) == S_IFREG else {
            close(descriptor)
            throw ViewerError.message("Yalnızca normal proje dosyaları açılabilir.")
        }
        let file = FileHandle(fileDescriptor: descriptor, closeOnDealloc: true)
        defer { try? file.close() }
        if let expectedIdentity, fileIdentity(initial) != expectedIdentity {
            throw ViewerError.message("Kayıt açılırken değişti; model listesini yenileyip tekrar deneyin.")
        }
        guard initial.st_size <= Int64(limit) else { throw ViewerError.message("Dosya 64 MB sınırını aşıyor.") }
        var data = Data(), didRead = false
        while true {
            if cancelled() { throw CancellationError() }
            let remaining = limit + 1 - data.count
            let chunk = try file.read(upToCount: min(64 * 1024, remaining)) ?? Data()
            if chunk.isEmpty { break }
            data.append(chunk)
            guard data.count <= limit else { throw ViewerError.message("Dosya 64 MB sınırını aşıyor.") }
            if !didRead { didRead = true; try afterFirstChunk?() }
        }
        var final = stat()
        if cancelled() { throw CancellationError() }
        guard fstat(descriptor, &final) == 0, fileIdentity(initial) == fileIdentity(final),
              Int64(data.count) == Int64(final.st_size) else {
            throw ViewerError.message("Dosya okunurken değişti; yeniden seçin.")
        }
        return data
    }

    private static func identity(_ url: URL) -> EntryIdentity? {
        guard let entry = fileIdentity(url) else { return nil }
        let isCopy = url.pathExtension.lowercased() == copyExtension
        let isPartial = url.pathExtension.lowercased() == "partial"
        var partialName: String?
        if isPartial && (entry.mode & UInt32(S_IFMT)) == UInt32(S_IFDIR) {
            let candidates = ["project.tcad", "model.tcad"].filter { fileIdentity(url.appendingPathComponent($0)) != nil }
            guard candidates.count == 1 else { return nil }
            partialName = candidates[0]
        }
        return EntryIdentity(entry: entry,
                             payload: isCopy ? fileIdentity(url.appendingPathComponent(payloadName)) : partialName.flatMap { fileIdentity(url.appendingPathComponent($0)) },
                             metadata: isCopy || isPartial ? fileIdentity(url.appendingPathComponent(metadataName)) : nil,
                             partialPayloadName: partialName)
    }

    static func requireCurrent(_ entry: Entry) throws {
        guard let expected = entry.identity, identity(entry.url) == expected else {
            throw ViewerError.message("Kayıt değişti veya kaldırıldı; model listesini yenileyip tekrar deneyin.")
        }
    }

    // Reject a row that was replaced or modified before or during the read.
    // Filesystem identity checks are not atomic with another writer.
    static func readListedPayload(_ entry: Entry, cancelled: @escaping () -> Bool = { false }, afterRead: (() throws -> Void)? = nil) throws -> Data {
        try requireCurrent(entry)
        let isPartial = entry.url.pathExtension.lowercased() == "partial"
        let payload = entry.url.pathExtension.lowercased() == copyExtension || isPartial ? entry.identity?.payload : entry.identity?.entry
        guard let payload, (payload.mode & UInt32(S_IFMT)) == UInt32(S_IFREG) else {
            throw ViewerError.message("Kayıt içeriği normal bir dosya değil; açılamadı.")
        }
        let url: URL
        if isPartial {
            guard let name = entry.identity?.partialPayloadName else { throw ViewerError.message("Geçici kayıt içeriği belirlenemedi.") }
            url = entry.url.appendingPathComponent(name)
        } else { url = payloadURL(entry.url) }
        let data = try Drawing.readData(url, expectedIdentity: payload, cancelled: cancelled)
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
              ["tcad", rawExtension, copyExtension].contains(url.pathExtension.lowercased()) else {
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
        if url.pathExtension.lowercased() != copyExtension {
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
    @Binding var search: String
    @Binding var sortOrder: ModelLibrarySortOrder
    @Binding var libraryFilter: ModelStore.LibraryFilter
    @Binding var searchContents: Bool
    let pendingRawRecovery: Bool
    let open: (ModelStore.Entry, ModelContentMatch?) -> Void
    @Environment(\.dismiss) private var dismiss
    @State private var models: [ModelStore.Entry] = []
    @State private var modelsRevision = UUID()

    @State private var contentPage = ModelStore.ContentPageNavigation()
    @State private var contentRetry = UUID()
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
    @State private var loadCancellation: ModelReadCancellation?
    #if DEBUG && targetEnvironment(simulator)
    @State private var didSimulateLoadFailure = false
    #endif


    private struct FilterKey: Hashable {
        let query: String
        let newest: Bool
        let filter: ModelStore.LibraryFilter
        let revision: UUID
        let searchContents: Bool
        let contentOffset: Int
        let retry: UUID
    }
    private struct FilterResult { let key: FilterKey; let entries: [ModelStore.Entry]; let skipped: Int; let next: Int?; let stalled: Bool; let evidence: [URL: ModelContentMatch] }
    @State private var filterResult: FilterResult?
    private var filterKey: FilterKey { FilterKey(query: search, newest: sortOrder == .newest, filter: libraryFilter, revision: modelsRevision, searchContents: searchContents, contentOffset: contentPage.offset, retry: contentRetry) }

    var body: some View {
        NavigationStack {
            let current = filterResult?.key == filterKey ? filterResult : nil
            let matchingModels = current?.entries ?? []
            List {
                if !isLoading && !isRecovering && !loadFailed {
                    Section("Arama kapsamı ve filtre") {
                        Text(searchContents ? "Model adı, kimliği ve parça bilgileri aranıyor" : "Model adı ve kayıt kimliği aranıyor")
                            .font(.caption).foregroundStyle(.secondary)
                        Toggle("Parça adı, beden ve malzemede ara", isOn: $searchContents)
                        if searchContents {
                            Text("İçerik araması gruplar hâlinde çalışır; okunamayan veya 8 MB üzerindeki kayıtlar atlanabilir.")
                                .font(.caption).foregroundStyle(.secondary)
                        }
                        Picker("Kayıt durumu", selection: $libraryFilter) {
                            ForEach(ModelStore.LibraryFilter.allCases) { filter in
                                Text(filter.rawValue).tag(filter)
                            }
                        }.pickerStyle(.menu)
                    }
                }
                if !isLoading && !isRecovering && !loadFailed && !models.isEmpty, let current {
                    Text("\(current.entries.count) / \(models.count) kayıt gösteriliyor")
                        .font(.caption).foregroundStyle(.secondary)
                        .accessibilityLabel("\(models.count) kayıttan \(current.entries.count) tanesi gösteriliyor")

                    if current.next != nil || current.stalled || current.key.contentOffset > 0 {
                        Text("İçerik sonuçları yalnızca bu kayıt grubunu kapsar; diğer gruplarda eşleşmeler olabilir. Ad araması seçili kayıtların tamamında yapıldı.")
                            .font(.caption).foregroundStyle(.secondary)
                    }
                    if current.stalled {
                        Text("İçerik araması bu gruba başlayamadı; sonuçlar eksik. Tekrar deneyebilirsiniz.")
                            .font(.caption).foregroundStyle(.secondary)
                        Button("İçerik aramasını tekrar dene") { contentRetry = UUID() }
                    }
                    if let next = current.next {
                        Button("Sonraki kayıt grubunda ara") { contentPage.advance(to: next) }
                    }
                    if current.key.contentOffset > 0 {
                        Button("Önceki kayıt grubuna dön") { contentPage.previous() }
                        Button("İlk kayıt grubuna dön") { contentPage.reset() }
                    }
                    if current.skipped > 0 {
                        Text("İçerik aramasında \(current.skipped) kayıt okunamadı veya 8 MB sınırını aşıyor; ad araması devam eder.")
                            .font(.caption).foregroundStyle(.secondary)
                    }
                    if libraryFilter != .all {
                        Text("Filtre: \(libraryFilter.rawValue)")
                            .font(.caption).foregroundStyle(.secondary)
                    }
                    if !search.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty || libraryFilter != .all {
                        Button("Aramayı ve filtreyi temizle") {
                            search = ""
                            libraryFilter = .all
                        }
                    }
                }
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
                        if !search.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
                            Button("Aramayı temizle") { search = "" }
                        }
                        if libraryFilter != .all {
                            Button("Tüm kayıtları göster") { libraryFilter = .all }
                        }
                    }
                }
                ForEach(matchingModels) { model in
                    HStack {
                    Button {
                        if model.canOpen {
                            if pendingRawRecovery { error = "Önce bekleyen ham kopyayı ana ekranda yeniden kaydedin veya açıkça vazgeçin. Dışa aktarmadan sonra da bellekteki kopyadan ayrıca vazgeçmeniz gerekir. Arama sonuçları korunuyor." }
                            else { open(model, current?.evidence[model.id]) }
                        }
                        else if model.requiresRecovery { modelToRecover = model }
                        else if model.identity == nil { error = "Kayıt listelenirken değişti. Model listesini yenileyip tekrar deneyin." }
                        else { error = "Bu kayıt açılamıyor. Kayıtlı dosyayı kontrol edin." }
                    } label: {
                        VStack(alignment: .leading) {
                            Text(model.safeName)
                            if let match = current?.evidence[model.id] {
                                Text("Eşleşen parça: \(match.detail)")
                                    .font(.caption).foregroundStyle(.secondary).lineLimit(3)
                            }
                            if let status = model.selectionStatus {
                                Text(status).font(.caption).foregroundStyle(.secondary)
                            }
                            Text("Kayıt: \(ModelStore.safeLabel(model.readableIdentity))")
                                .font(.caption).foregroundStyle(.secondary).lineLimit(2)
                            if let date = model.date {
                                Text("Kaydedildi: \(date.formatted(date: .abbreviated, time: .standard))")
                                    .font(.caption).foregroundStyle(.secondary)
                            }
                        }
                        .frame(maxWidth: .infinity, minHeight: 44, alignment: .leading)
                    }
                    .accessibilityElement(children: .combine)
                    .accessibilityLabel(model.safeName)
                    .accessibilityValue((current?.evidence[model.id].map { "Eşleşen parça: \($0.detail), " } ?? "") + (model.selectionStatus.map { "\($0), " } ?? "") + "Kayıt kimliği: \(ModelStore.safeLabel(model.displayIdentity))" + (model.date.map { ", Kaydedildi: \($0.formatted(date: .abbreviated, time: .standard))" } ?? ""))
                    .buttonStyle(.plain)
                    .disabled(isRecovering)
                    Menu {
                        if model.canOpen || model.requiresRecovery {
                            Button("Ham kopyayı dışa aktar…") { modelToRecover = model }
                        }
                        if model.url.pathExtension.lowercased() != "partial" {
                            Button("Kayıtlı modeli sil", role: .destructive) { modelToDelete = model }
                        }
                    } label: {
                        Image(systemName: "ellipsis.circle")
                            .frame(width: 44, height: 44)
                    }
                    .accessibilityLabel("\(model.safeName) model işlemleri")
                    .accessibilityValue("Kayıt kimliği: \(ModelStore.safeLabel(model.displayIdentity))")
                    .disabled(isRecovering)
                    }
                    .swipeActions {
                        if model.url.pathExtension.lowercased() != "partial" {
                            Button("Sil", role: .destructive) { modelToDelete = model }.disabled(isRecovering)
                        }
                    }
                }
            }
            .searchable(text: $search, prompt: Text(searchContents ? "Model veya parça bilgisi ara" : "Model adı veya kimliği ara"))
            .navigationTitle("Modellerim")
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("Kapat") { dismiss() } }
                ToolbarItem(placement: .primaryAction) {
                    Menu {
                        Button { loadModels() } label: { Label("Listeyi yenile", systemImage: "arrow.clockwise") }
                            .disabled(isLoading || isRecovering)

                        Picker("Sıralama", selection: $sortOrder) {
                            ForEach(ModelLibrarySortOrder.allCases) { order in
                                Text(order.rawValue).tag(order)
                            }
                        }
                    } label: {
                        Label("Liste seçenekleri", systemImage: "line.3.horizontal.decrease.circle")
                    }
                    .accessibilityLabel("Model listesi seçenekleri")
                }
            }
            .onChange(of: search) { _ in contentPage.reset() }
            .onChange(of: sortOrder) { _ in contentPage.reset() }
            .onChange(of: libraryFilter) { _ in contentPage.reset() }
            .onChange(of: searchContents) { _ in contentPage.reset() }
            .onChange(of: modelsRevision) { _ in contentPage.reset() }
            .onAppear { loadModels() }
            .onDisappear { loadCancellation?.cancel(); loadID = UUID(); recoveryID = UUID(); isRecovering = false }
            .alert("Ham kopyayı kurtar?", isPresented: Binding(get: { modelToRecover != nil }, set: { if !$0 { modelToRecover = nil } })) {
                Button("İptal", role: .cancel) { modelToRecover = nil }
                Button("Ham kopyayı dışa aktar") {
                    guard let model = modelToRecover else { return }
                    modelToRecover = nil
                    prepareRecovery(model)
                }
            } message: {
                if modelToRecover?.url.pathExtension.lowercased() == "partial" {
                    Text("Bu, tamamlanmamış veya başarısız bir kayıt olabilir; yazma işlemi hâlâ sürüyor olabilir. Ham dosya eksik ya da bozuk olabilir ve geçerli proje olduğu doğrulanmaz. Dosya açılmadan veya değiştirilmeden dışa aktarılır; geçici kayıt silinmez. En fazla 64 MB desteklenir.")
                } else if modelToRecover?.url.pathExtension.lowercased() == "tcadraw" {
                    Text("Bu ham kayıt önizlenmedi veya proje olarak doğrulanmadı. Dosya değiştirilmeden dışa aktarılır; bozuk olabilir. En fazla 64 MB desteklenir.")
                } else if modelToRecover?.requiresRecovery == true {
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
                Text("\(modelToDelete?.safeName ?? "Model")\nKayıt: \(ModelStore.safeLabel(modelToDelete?.readableIdentity ?? ""))\nTam kayıt kimliği: \(ModelStore.safeLabel(modelToDelete?.displayIdentity ?? ""))\nTelefonunuzdaki kayıtlı modellerden silinecek. Orijinal dosya etkilenmez.")
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
                let worker = Task.detached(priority: .userInitiated) { () -> ([ModelStore.Entry], Int, Int?, Bool, [URL: ModelContentMatch])? in
                    let cancelled = { Task.isCancelled }
                    guard let names = ModelStore.filteredModels(snapshot, query: request.query, newest: request.newest, filter: request.filter, cancelled: cancelled) else { return nil }
                    guard request.searchContents, !request.query.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else { return (names, 0, nil, false, [:]) }
                    guard let candidates = ModelStore.filteredModels(snapshot, query: "", newest: request.newest, filter: request.filter, cancelled: cancelled) else { return nil }
                    let named = Set(names.map(\.id))
                    var selected: [ModelStore.Entry] = []
                    var evidence: [URL: ModelContentMatch] = [:]
                    var skipped = 0
                    var page = ModelStore.ContentPageGate(start: request.contentOffset)
                    var budget = ModelStore.ContentSearchBudget(start: ProcessInfo.processInfo.systemUptime)
                    for (candidateIndex, entry) in candidates.enumerated() {
                        if cancelled() { return nil }
                        if named.contains(entry.id) { selected.append(entry); continue }
                        if !page.includes(candidateIndex) { continue }
                        let identity = entry.url.pathExtension.lowercased() == "tcadcopy" ? entry.identity?.payload : entry.identity?.entry
                        guard entry.canOpen, let identity, identity.size <= 8 * 1024 * 1024 else { skipped += 1; continue }
                        guard page.shouldScan(index: candidateIndex, bytes: identity.size,
                                              now: ProcessInfo.processInfo.systemUptime, budget: &budget) else { continue }
                        do {
                            let bytes = try ModelStore.readListedPayload(entry, cancelled: cancelled)
                            let drawing = try Drawing.load(bytes, cancelled: cancelled)
                            let modelFields = [entry.name, entry.displayIdentity, entry.readableIdentity]
                            for (index, piece) in drawing.pieces.enumerated() {
                                if cancelled() { return nil }
                                let values = ModelStore.searchablePieceFields(name: piece.name, size: piece.size, material: piece.material, index: index)
                                if ModelStore.pieceSearchMatches(query: request.query, modelFields: modelFields, pieceFields: values) {
                                    func excerpt(_ value: String) -> String {
                                        let prefix = value.prefix(120)
                                        return ModelStore.safeLabel(String(prefix)) + (prefix.endIndex == value.endIndex ? "" : "…")
                                    }
                                    let name = piece.name.flatMap { $0.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty ? nil : excerpt($0) } ?? "Parça \(index + 1)"
                                    let details = [name, piece.size.map { "Beden: \(excerpt($0))" }, piece.material.map { "Malzeme: \(excerpt($0))" }].compactMap { $0 }
                                    evidence[entry.id] = ModelContentMatch(index: index, detail: details.joined(separator: " · "), data: bytes)
                                    selected.append(entry)
                                    break
                                }
                            }
                        } catch {
                            if cancelled() { return nil }
                            skipped += 1
                        }
                    }
                    return (selected, skipped, page.next, page.stalled, evidence)
                }
                let result = await withTaskCancellationHandler(operation: { await worker.value }, onCancel: { worker.cancel() })
                guard ModelStore.acceptsSearchResult(request: request, current: filterKey, cancelled: Task.isCancelled), let result else { return }
                filterResult = FilterResult(key: request, entries: result.0, skipped: result.1, next: result.2, stalled: result.3, evidence: result.4)
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
        loadCancellation?.cancel()
        let cancellation = ModelReadCancellation()
        loadCancellation = cancellation
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
                return try ModelStore.models(cancelled: { cancellation.isCancelled })
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

struct Drawing {
    typealias Point = ContourMeasure.Point
    struct Grain { let start: Point; let end: Point; let needsReview: Bool }
    struct Piece {
        let name: String?
        let id: UUID?
        let points: [Point]
        let closed: Bool
        let contourLength: Double?
        let isCollapsed: Bool
        let contourBounds: ContourBounds?
        let hasMinimumContourPoints: Bool
        let role: String?
        let roleUnreadable: Bool
        let size: String?
        let material: String?
        let textMetadataUnreadable: Bool
        let quantity: Int?
        let quantityUnreadable: Bool
        let metadataUnreadable: Bool
        let metadataEmpty: Bool
        let grainStatus: String
        let grain: Grain?
        let notches: [Int]
        func displayName(index: Int) -> String {
            guard let name, !name.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
                return "Parça \(index + 1)"
            }
            return ModelStore.safeLabel(name)
        }

        func selectionDetails(index: Int) -> String {
            let fields = [("Beden", size), ("Tür", role), ("Malzeme", material)].compactMap { label, value -> String? in
                guard let value = value?.trimmingCharacters(in: .whitespacesAndNewlines), !value.isEmpty else { return nil }
                return "\(label): \(ModelStore.safeLabel(value))"
            }
            let dimensions: [String]
            if let bounds = contourBounds,
               let width = MeasurementDisplay.format(bounds.maxX - bounds.minX),
               let height = MeasurementDisplay.format(bounds.maxY - bounds.minY) {
                dimensions = ["Yaklaşık en: \(width) mm", "boy: \(height) mm"]
            } else {
                dimensions = []
            }
            let warning = (hasMinimumContourPoints ? [] : ["Kontur için nokta sayısı yetersiz: \(points.count)"]) + (quantityUnreadable ? ["Adet okunamadı veya geçersiz"] : []) + (metadataUnreadable ? ["Üretim bilgileri okunamadı"] : []) + (metadataEmpty ? ["Üretim bilgileri boş"] : []) + (roleUnreadable ? ["Parça türü okunamadı veya geçersiz"] : []) + (textMetadataUnreadable ? ["Beden veya malzeme okunamadı veya geçersiz"] : [])
            return (["Parça \(index + 1)"] + (id.map { ["Kimlik: \($0.uuidString)"] } ?? []) + [closed ? "Kapalı kontur" : "Açık kontur", "\(points.count) nokta"] + fields + dimensions + warning + (notches.isEmpty ? [] : ["\(notches.count) köşe işareti"])).joined(separator: " · ")
        }

        var rawSearchText: String {
            [name, size, role, material].compactMap { $0 }.joined(separator: " ")
        }
    }
    let name: String
    let pieces: [Piece]
    let pieceRenameIdentitiesValid: Bool
    let formatVersion: Int
    let hasNonUTF8BOM: Bool
    let historyPieceIDsDiffer: Bool
    let insufficientContourCount: Int
    let collapsedContourCount: Int

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

    // Structural screening only. The desktop must verify geometry, fingerprints,
    // copy mappings and the relationship between this history and the layout.
    private static func hasKnownRotationTrace(_ value: Any?, cancelled: () -> Bool) throws -> Bool {
        if cancelled() { throw CancellationError() }
        guard let trace = value as? [String: Any],
              Set(trace.keys) == Set(["Schema", "Algorithm", "TransformConvention", "SourceFingerprint", "LayoutFingerprint", "SourcePieces", "LayoutPieces", "Selections", "Copies", "FabricWidth", "Gap", "EdgeMargin", "UsedLength", "MaxLength", "GrainDirection", "ToleranceDegrees"]),
              trace["Schema"] as? String == "per-copy-orthogonal-trace-v1",
              trace["Algorithm"] as? String == "explicit-per-copy-shelf-v1",
              trace["TransformConvention"] as? String == "origin-quarter-turn-then-translate-v1",
              let source = trace["SourcePieces"] as? [[String: Any]], !source.isEmpty, source.count <= 10_000,
              let layout = trace["LayoutPieces"] as? [[String: Any]], !layout.isEmpty, layout.count <= 10_000,
              let selections = trace["Selections"] as? [String], !selections.isEmpty, selections.count <= 10_000,
              selections.allSatisfy({ UUID(uuidString: $0) != nil }),
              let copies = trace["Copies"] as? [[String: Any]], !copies.isEmpty, copies.count <= 10_000,
              let direction = trace["GrainDirection"] as? String, ["OneWay", "TwoWay"].contains(direction) else { return false }
        for key in ["SourceFingerprint", "LayoutFingerprint"] {
            guard let hash = trace[key] as? String, hash.utf8.count == 64,
                  hash.utf8.allSatisfy({ (48...57).contains($0) || (65...70).contains($0) }) else { return false }
        }
        for key in ["FabricWidth", "Gap", "EdgeMargin", "UsedLength", "ToleranceDegrees"] {
            guard let n = jsonNumber(trace[key]), n.isFinite else { return false }
        }
        if !(trace["MaxLength"] is NSNull) {
            guard let n = jsonNumber(trace["MaxLength"]), n.isFinite else { return false }
        }
        func pointValid(_ point: [String: Any]) -> Bool {
            guard Set(point.keys).isSubset(of: ["X", "Y", "IsFinite"]),
                  let x = jsonNumber(point["X"]), let y = jsonNumber(point["Y"]), x.isFinite, y.isFinite,
                  point["IsFinite"] == nil || jsonBoolean(point["IsFinite"]) == true else { return false }
            return true
        }
        for piece in source + layout {
            if cancelled() { throw CancellationError() }
            guard Set(piece.keys).isSubset(of: ["Id", "Name", "Points", "Closed", "Metadata", "Grainline", "Notches"]),
                  let id = piece["Id"] as? String, UUID(uuidString: id) != nil,
                  piece["Name"] is String, jsonBoolean(piece["Closed"]) != nil,
                  let points = piece["Points"] as? [[String: Any]], !points.isEmpty, points.count <= 10_000,
                  let metadata = piece["Metadata"] as? [String: Any],
                  Set(metadata.keys) == Set(["Role", "Size", "Material", "Quantity"]),
                  let grain = piece["Grainline"] as? [String: Any],
                  Set(grain.keys) == Set(["Start", "End", "NeedsReview"]),
                  let start = grain["Start"] as? [String: Any], pointValid(start),
                  let end = grain["End"] as? [String: Any], pointValid(end),
                  jsonBoolean(grain["NeedsReview"]) != nil else { return false }
            for key in ["Role", "Size", "Material"] {
                if !(metadata[key] is NSNull) && !(metadata[key] is String) { return false }
            }
            if !(metadata["Quantity"] is NSNull) {
                guard let quantity = jsonNumber(metadata["Quantity"]), quantity.rounded() == quantity,
                      quantity >= 1, quantity <= 100_000 else { return false }
            }
            for (offset, point) in points.enumerated() {
                if offset % 256 == 0 && cancelled() { throw CancellationError() }
                if !pointValid(point) { return false }
            }
            if let value = piece["Notches"] {
                guard let marks = value as? [[String: Any]], !marks.isEmpty, marks.count <= points.count else { return false }
                for (offset, mark) in marks.enumerated() {
                    if offset % 256 == 0 && cancelled() { throw CancellationError() }
                    guard Set(mark.keys) == Set(["VertexIndex", "Position"]),
                          let index = jsonNumber(mark["VertexIndex"]), index.rounded() == index, index >= 0, index < Double(points.count),
                          let position = mark["Position"] as? [String: Any], pointValid(position) else { return false }
                }
            }
        }
        for copy in copies {
            if cancelled() { throw CancellationError() }
            guard Set(copy.keys) == Set(["SourcePieceId", "CopyNumber", "PlacedPieceId", "Degrees"]),
                  let sourceID = copy["SourcePieceId"] as? String, UUID(uuidString: sourceID) != nil,
                  let placedID = copy["PlacedPieceId"] as? String, UUID(uuidString: placedID) != nil,
                  let number = jsonNumber(copy["CopyNumber"]), number.rounded() == number, number >= 1, number <= 100_000,
                  let degrees = jsonNumber(copy["Degrees"]), [0.0, 90.0, 180.0, 270.0].contains(degrees) else { return false }
        }
        return true
    }

    private static func hasV7ProjectFields(_ object: [String: Any], cancelled: () -> Bool) throws -> Bool {
        if cancelled() { throw CancellationError() }
        guard let rules = object["GradingRules"] as? [[String: Any]], rules.count <= 10_000,
              let snapshot = object["NestingSnapshot"] as? [String: Any],
              Set(snapshot.keys) == Set(["FabricWidth", "UsedLength", "Gap", "EdgeMargin", "Algorithm", "LayoutFingerprint", "Placements", "RotationTrace"]),
              snapshot["Algorithm"] as? String == "deterministic-shelf-v1",
              let fingerprint = snapshot["LayoutFingerprint"] as? String, fingerprint.utf8.count == 64,
              fingerprint.utf8.allSatisfy({ (48...57).contains($0) || (65...70).contains($0) }),
              let placements = snapshot["Placements"] as? [[String: Any]], !placements.isEmpty, placements.count <= 10_000 else { return false }
        for key in ["FabricWidth", "UsedLength", "Gap", "EdgeMargin"] {
            guard let value = jsonNumber(snapshot[key]), value.isFinite else { return false }
        }
        for placement in placements {
            if cancelled() { throw CancellationError() }
            guard Set(placement.keys) == Set(["SourcePieceId", "CopyNumber", "PlacedPieceId"]),
                  let source = placement["SourcePieceId"] as? String, UUID(uuidString: source) != nil,
                  let placed = placement["PlacedPieceId"] as? String, UUID(uuidString: placed) != nil,
                  let number = jsonNumber(placement["CopyNumber"]), number.rounded() == number,
                  number >= 1, number <= 100_000 else { return false }
        }
        for rule in rules {
            if cancelled() { throw CancellationError() }
            guard Set(rule.keys) == Set(["SourcePieceId", "SourceClosed", "SourcePoints", "Deltas"]),
                  let id = rule["SourcePieceId"] as? String, UUID(uuidString: id) != nil,
                  jsonBoolean(rule["SourceClosed"]) != nil,
                  let points = rule["SourcePoints"] as? [[String: Any]], points.count <= 10_000,
                  let deltas = rule["Deltas"] as? [[String: Any]], deltas.count == points.count else { return false }
            for (offset, point) in (points + deltas).enumerated() {
                if offset % 256 == 0 && cancelled() { throw CancellationError() }
                guard Set(point.keys).isSubset(of: ["X", "Y", "IsFinite"]),
                      let x = jsonNumber(point["X"]), let y = jsonNumber(point["Y"]), x.isFinite, y.isFinite,
                      point["IsFinite"] == nil || jsonBoolean(point["IsFinite"]) == true else { return false }
            }
        }
        if let value = object["ImportProvenance"] {
            guard let provenance = value as? [String: Any],
                  Set(provenance.keys) == Set(["SourceFileName", "SourceSha256", "SourceByteLength", "MillimetersPerUnit", "Warnings", "PieceIds"]),
                  provenance["SourceFileName"] is String, provenance["SourceSha256"] is String,
                  let length = jsonNumber(provenance["SourceByteLength"]), length.isFinite, length.rounded() == length,
                  let scale = jsonNumber(provenance["MillimetersPerUnit"]), scale.isFinite,
                  provenance["Warnings"] is [String],
                  let ids = provenance["PieceIds"] as? [String], ids.allSatisfy({ UUID(uuidString: $0) != nil }) else { return false }
        }
        return true
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
              (0x31...0x37).contains(digit), nextNonWhitespace() == 0x2c else { return nil }
        return Int(digit - 0x30)
    }
    static func load(_ url: URL) throws -> Drawing { try load(readData(url)) }

    static func readData(_ url: URL, expectedIdentity: ModelStore.FileIdentity? = nil, cancelled: @escaping () -> Bool = { false }) throws -> Data {
        // File providers can replace or materialize a document while it is
        // being read. Capture one coordinated snapshot before parsing it.
        let coordinator = NSFileCoordinator(filePresenter: nil)
        var coordinationError: NSError?
        var snapshot: Result<Data, Error>?
        coordinator.coordinate(readingItemAt: url, options: [], error: &coordinationError) { coordinatedURL in
            snapshot = Result { try readCoordinatedData(coordinatedURL, expectedIdentity: expectedIdentity, cancelled: cancelled) }
        }
        if let coordinationError { throw coordinationError }
        guard let snapshot else { throw ViewerError.message("Dosya okuması tamamlanamadı.") }
        return try snapshot.get()
    }

    private static func readCoordinatedData(_ url: URL, expectedIdentity: ModelStore.FileIdentity?, cancelled: () -> Bool) throws -> Data {
        try ModelStore.readFileSnapshot(url, expectedIdentity: expectedIdentity, cancelled: cancelled)
    }

    static func load(_ data: Data, cancelled: @escaping () -> Bool = { false }) throws -> Drawing {
        if cancelled() { throw CancellationError() }
        guard data.count <= 64 * 1024 * 1024 else { throw ViewerError.message("Dosya 64 MB sınırını aşıyor.") }
        if LosslessProjectName.isOversizedVersionSix(data) || LosslessProjectName.isOversizedVersionSix(data, version: 7) {
            throw ViewerError.message("Sürüm 6 veya 7 önizlemesi için dosya en fazla 8 MB olmalı.")
        }
        let root = try JSONSerialization.jsonObject(with: data)
        if cancelled() { throw CancellationError() }
        let object: [String: Any]
        let formatVersion: Int
        if let array = root as? [Any], array.count == 2,
           let version = envelopeVersion(data), let body = array[1] as? [String: Any] {
            object = body; formatVersion = version
        } else if let body = root as? [String: Any] { object = body; formatVersion = 0 }
        else { throw ViewerError.message("Desteklenmeyen proje biçimi.") }
        if formatVersion == 6 || formatVersion == 7 {
            guard data.count <= LosslessProjectName.maximumEditBytes else { throw ViewerError.message("Sürüm 6 veya 7 önizlemesi için dosya en fazla 8 MB olmalı.") }
            do {
                let validationData = try LosslessProjectName.versionSixValidationData(data)
                try LosslessProjectName.validateUniqueKeys(validationData, strictVersion: formatVersion, cancelled: cancelled)
            } catch is CancellationError { throw CancellationError() }
            catch { throw ViewerError.message("Sürüm \(formatVersion) dosyası güvenli biçimde okunamadı; alanları, kodlamayı ve 8 MB doğrulama sınırını kontrol edin.") }
            guard Set(object.keys).isSubset(of: ["Name", "Pieces", "GradingRules", "NestingSnapshot", "ImportProvenance"]) else {
                throw ViewerError.message("Sürüm \(formatVersion) projede tanınmayan alanlar var; dosya önizlenmedi.")
            }
            if formatVersion == 7 {
                guard try hasV7ProjectFields(object, cancelled: cancelled), let snapshot = object["NestingSnapshot"] as? [String: Any],
                      try hasKnownRotationTrace(snapshot["RotationTrace"], cancelled: cancelled) else {
                    throw ViewerError.message("Sürüm 7 döndürme geçmişi eksik veya desteklenmiyor; dosya önizlenmedi.")
                }
            }
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
            do { try LosslessProjectName.validateUniqueKeys(Data(decoded.utf8), strictV6: false, strictVersion: formatVersion, cancelled: cancelled) }
            catch is CancellationError { throw CancellationError() }
            catch { throw ViewerError.message("Proje alanları yinelenmiş veya güvenli biçimde okunamadı.") }
        }
        if cancelled() { throw CancellationError() }
        guard let name = object["Name"] as? String, let rawPieces = object["Pieces"] as? [[String: Any]], rawPieces.count <= 10_000 else {
            throw ViewerError.message("Proje adı veya parça listesi eksik ya da sınır dışında.")
        }
        var total = 0
        var totalNotches = 0
        var pieces: [Piece] = []
        var pieceRenameIdentitiesValid = true
        var pieceIDs = Set<UUID>()
        for raw in rawPieces {
            if cancelled() { throw CancellationError() }
            if formatVersion == 7 {
                guard let id = raw["Id"] as? String, UUID(uuidString: id) != nil, raw["Name"] is String else {
                    throw ViewerError.message("Sürüm 7 parça kimliği veya adı eksik; dosya önizlenmedi.")
                }
            }
            if formatVersion >= 6 && !Set(raw.keys).isSubset(of: ["Id", "Name", "Points", "Closed", "Metadata", "Grainline", "Notches"]) {
                throw ViewerError.message("Sürüm \(formatVersion) parçada tanınmayan alanlar var; kontur önizlenmedi.")
            }
            guard let rawPoints = raw["Points"] as? [[String: Any]], rawPoints.count <= 10_000,
                  let closed = jsonBoolean(raw["Closed"]) else { throw ViewerError.message("Parça konturu eksik veya sınır dışında.") }
            total += rawPoints.count
            guard total <= 200_000 else { throw ViewerError.message("Toplam nokta sınırı aşıldı.") }
            let points: [Point] = try rawPoints.enumerated().map { offset, point in
                if offset % 256 == 0 && cancelled() { throw CancellationError() }
                if formatVersion >= 6 && !Set(point.keys).isSubset(of: ["X", "Y", "IsFinite"]) {
                    throw ViewerError.message("Kontur noktasında tanınmayan alan var; önizleme güvenli değil.")
                }
                guard let x = jsonNumber(point["X"]), let y = jsonNumber(point["Y"]),
                      x.isFinite, y.isFinite, abs(x) < 1e100, abs(y) < 1e100,
                      (formatVersion < 6 || (abs(x) <= 100_000_000 && abs(y) <= 100_000_000)),
                      (point["IsFinite"] == nil || jsonBoolean(point["IsFinite"]) == true) else {
                    throw ViewerError.message("Geçersiz veya gösterilemeyen koordinat.")
                }
                return Point(x: x, y: y)
            }
            var notchIndices: [Int] = []
            if let value = raw["Notches"] {
                guard formatVersion >= 6, let marks = value as? [[String: Any]], !marks.isEmpty, marks.count <= 10_000 else { throw ViewerError.message("Köşe işareti listesi veya sürümü geçersiz.") }
                totalNotches += marks.count
                guard totalNotches <= 200_000 else { throw ViewerError.message("Köşe işareti sayısı sınırı aşıldı.") }
                var seen = Set<Int>()
                for (offset, mark) in marks.enumerated() {
                    if offset % 256 == 0 && cancelled() { throw CancellationError() }
                    guard Set(mark.keys) == Set(["VertexIndex", "Position"]),
                          let n = mark["VertexIndex"] as? NSNumber, CFGetTypeID(n) != CFBooleanGetTypeID(),
                          !["f", "d"].contains(String(cString: n.objCType)), n.doubleValue >= 0, n.doubleValue < Double(points.count),
                          let position = mark["Position"] as? [String: Any], Set(position.keys).isSubset(of: ["X", "Y", "IsFinite"]),
                          let x = jsonNumber(position["X"]), let y = jsonNumber(position["Y"]),
                          x.isFinite, y.isFinite, abs(x) <= 100_000_000, abs(y) <= 100_000_000,
                          (position["IsFinite"] == nil || jsonBoolean(position["IsFinite"]) == true) else { throw ViewerError.message("Köşe işareti konumu veya indeksi geçersiz.") }
                    let index = n.intValue
                    guard seen.insert(index).inserted, points[index].x == x, points[index].y == y,
                          !(closed && points.count > 1 && points.first!.x == points.last!.x && points.first!.y == points.last!.y && index == points.count - 1) else { throw ViewerError.message("Köşe işareti konturla eşleşmiyor veya yinelenmiş.") }
                    notchIndices.append(index)
                }
            }
            let metadata = raw["Metadata"] as? [String: Any]
            let rawMetadata = raw["Metadata"]
            let metadataUnreadable = rawMetadata != nil && !(rawMetadata is NSNull) && metadata == nil
            let metadataEmpty = metadata.map { fields in
                ["Role", "Size", "Material", "Quantity"].allSatisfy { fields[$0] == nil || fields[$0] is NSNull }
            } ?? false
            if formatVersion >= 6, let metadata, !Set(metadata.keys).isSubset(of: ["Role", "Size", "Material", "Quantity"]) {
                throw ViewerError.message("Parça bilgisinde tanınmayan alan var; dosya önizlenmedi.")
            }
            let quantityNumber = metadata?["Quantity"] as? NSNumber
            let quantity: Int? = quantityNumber.flatMap { number in
                guard CFGetTypeID(number) != CFBooleanGetTypeID(), number.doubleValue.isFinite,
                      number.doubleValue.rounded() == number.doubleValue,
                      number.doubleValue >= 1, number.doubleValue <= 100_000 else { return nil }
                return number.intValue
            }
            let rawQuantity = metadata?["Quantity"]
            let quantityUnreadable = rawQuantity != nil && !(rawQuantity is NSNull) && quantity == nil
            let rawRole = metadata?["Role"]
            let roleText = rawRole as? String
            let role = roleText == "Kesim" || roleText == "Yardımcı" ? roleText : nil
            let roleUnreadable = rawRole != nil && !(rawRole is NSNull) && role == nil
            func productionText(_ value: Any?) -> String? {
                guard let text = value as? String, !text.isEmpty, text.utf16.count <= 128,
                      text == text.trimmingCharacters(in: .whitespacesAndNewlines),
                      text.rangeOfCharacter(from: .controlCharacters) == nil else { return nil }
                return text
            }
            let rawSize = metadata?["Size"], rawMaterial = metadata?["Material"]
            let size = productionText(rawSize), material = productionText(rawMaterial)
            let textMetadataUnreadable = (rawSize != nil && !(rawSize is NSNull) && size == nil) ||
                (rawMaterial != nil && !(rawMaterial is NSNull) && material == nil)
            let grainStatus: String
            var parsedGrain: Grain?
            if formatVersion >= 6, let grain = raw["Grainline"] as? [String: Any] {
                guard Set(grain.keys).isSubset(of: ["Start", "End", "NeedsReview"]) else {
                    throw ViewerError.message("İplik yönünde tanınmayan alan var; dosya önizlenmedi.")
                }
                for key in ["Start", "End"] {
                    if let point = grain[key] as? [String: Any], !Set(point.keys).isSubset(of: ["X", "Y", "IsFinite"]) {
                        throw ViewerError.message("İplik yönü noktasında tanınmayan alan var; dosya önizlenmedi.")
                    }
                }
            }
            if let grain = raw["Grainline"] as? [String: Any],
               let start = grain["Start"] as? [String: Any], let end = grain["End"] as? [String: Any],
               let sx = jsonNumber(start["X"]), let sy = jsonNumber(start["Y"]),
               let ex = jsonNumber(end["X"]), let ey = jsonNumber(end["Y"]),
               sx.isFinite, sy.isFinite, ex.isFinite, ey.isFinite,
               (start["IsFinite"] == nil || jsonBoolean(start["IsFinite"]) == true),
               (end["IsFinite"] == nil || jsonBoolean(end["IsFinite"]) == true),
               [sx, sy, ex, ey].allSatisfy({ abs($0) < 1e100 }),
               (formatVersion < 6 || [sx, sy, ex, ey].allSatisfy({ abs($0) <= 100_000_000 })),
               (sx != ex || sy != ey), let review = jsonBoolean(grain["NeedsReview"]) {
                grainStatus = review ? "İplik yönü: Kontrol bekliyor" : "İplik yönü: Kayıtlı"
                parsedGrain = Grain(start: Point(x: sx, y: sy), end: Point(x: ex, y: ey), needsReview: review)
            } else if raw["Grainline"] == nil || raw["Grainline"] is NSNull {
                grainStatus = "İplik yönü: Belirtilmemiş"
            } else {
                grainStatus = "İplik yönü: Okunamadı"
            }
            if formatVersion == 7 && (metadataUnreadable || metadataEmpty || roleUnreadable || textMetadataUnreadable || quantityUnreadable || grainStatus == "İplik yönü: Okunamadı") {
                throw ViewerError.message("Sürüm 7 parça bilgisi veya iplik yönü geçersiz; dosya önizlenmedi.")
            }
            let id = (raw["Id"] as? String).flatMap { UUID(uuidString: $0) }
            if !(raw["Name"] is String) { pieceRenameIdentitiesValid = false }
            if raw["Id"] != nil {
                if let id, id.uuidString != "00000000-0000-0000-0000-000000000000", pieceIDs.insert(id).inserted { }
                else { pieceRenameIdentitiesValid = false }
            }
            if formatVersion == 7 && !pieceRenameIdentitiesValid {
                throw ViewerError.message("Sürüm 7 parça kimliği yinelenmiş veya geçersiz; dosya önizlenmedi.")
            }
            let hasMinimum = ContourMeasure.hasMinimumContourPoints(points, closed: closed)
            pieces.append(Piece(name: raw["Name"] as? String, id: id, points: points, closed: closed,
                                contourLength: hasMinimum ? ContourMeasure.length(points, closed: closed) : nil,
                                isCollapsed: ContourMeasure.isCollapsed(points),
                                contourBounds: points.count > 1 ? ContourBounds(points: points) : nil,
                                hasMinimumContourPoints: hasMinimum,
                                role: role, roleUnreadable: roleUnreadable, size: size,
                                material: material, textMetadataUnreadable: textMetadataUnreadable, quantity: quantity, quantityUnreadable: quantityUnreadable, metadataUnreadable: metadataUnreadable, metadataEmpty: metadataEmpty,
                                grainStatus: grainStatus, grain: parsedGrain, notches: notchIndices))
        }
        if cancelled() { throw CancellationError() }
        if formatVersion == 6 && totalNotches == 0 { throw ViewerError.message("Sürüm 6 dosyasında köşe işareti bulunamadı.") }
        var historyPieceIDsDiffer = false
        if formatVersion == 7, let snapshot = object["NestingSnapshot"] as? [String: Any],
           let placements = snapshot["Placements"] as? [[String: Any]] {
            var placedIDs = Set<UUID>()
            for placement in placements {
                if cancelled() { throw CancellationError() }
                guard let text = placement["PlacedPieceId"] as? String, let id = UUID(uuidString: text) else {
                    throw ViewerError.message("Pastal eşleme kimliği geçersiz.")
                }
                placedIDs.insert(id)
            }
            historyPieceIDsDiffer = placements.count != pieces.count || placedIDs.count != placements.count || placedIDs != pieceIDs
        }
        return Drawing(name: name, pieces: pieces, pieceRenameIdentitiesValid: pieceRenameIdentitiesValid,
                       formatVersion: formatVersion, hasNonUTF8BOM: LosslessProjectName.hasNonUTF8BOM(data), historyPieceIDsDiffer: historyPieceIDsDiffer,
                       insufficientContourCount: pieces.filter { !$0.hasMinimumContourPoints }.count,
                       collapsedContourCount: pieces.filter { $0.isCollapsed }.count)
    }
}

/// Illustrative icons only; they do not describe generated pattern geometry.
private struct DressStyleSketch: Shape {
    let style: ExperimentalDesignBrief.DressStyle
    func path(in rect: CGRect) -> Path {
        func p(_ x: CGFloat, _ y: CGFloat) -> CGPoint { CGPoint(x: rect.minX + x * rect.width, y: rect.minY + y * rect.height) }
        var path = Path()
        // Reference-inspired technical silhouettes are illustrative, not pattern geometry.
        if style == .corset || style == .sheath {
            let narrow = style == .sheath
            let top: CGFloat = narrow ? 0.14 : 0.22
            path.move(to: p(0.36, top)); path.addQuadCurve(to: p(0.64, top), control: p(0.5, top + 0.04))
            path.addLine(to: p(0.68, 0.43)); path.addLine(to: p(narrow ? 0.72 : 0.76, 0.94))
            path.addLine(to: p(narrow ? 0.28 : 0.24, 0.94)); path.addLine(to: p(0.32, 0.43))
            path.closeSubpath()
            path.move(to: p(0.42, top + 0.03)); path.addLine(to: p(0.42, 0.46))
            path.move(to: p(0.58, top + 0.03)); path.addLine(to: p(0.58, 0.46))
            path.move(to: p(0.33, 0.46)); path.addQuadCurve(to: p(0.67, 0.46), control: p(0.5, 0.50))
            return path
        }
        if style == .wrap || style == .aline {
            let hem: CGFloat = style == .aline ? 0.08 : 0.14
            path.move(to: p(0.40, 0.13)); path.addQuadCurve(to: p(0.60, 0.13), control: p(0.50, 0.21))
            path.addLines([p(0.73, 0.17), p(0.78, 0.27), p(0.70, 0.30), p(0.67, 0.23), p(0.65, 0.42), p(1 - hem, 0.94), p(hem, 0.94), p(0.35, 0.42)])
            // Return via the opposite seam; draw the outline in a separate contour.
            path.move(to: p(0.40, 0.13)); path.addLines([p(0.27, 0.17), p(0.22, 0.27), p(0.30, 0.30), p(0.33, 0.23), p(0.35, 0.42), p(hem, 0.94)])
            path.move(to: p(0.35, 0.44)); path.addQuadCurve(to: p(0.65, 0.44), control: p(0.5, 0.47))
            if style == .wrap {
                path.move(to: p(0.60, 0.14)); path.addQuadCurve(to: p(0.36, 0.45), control: p(0.48, 0.33))
                path.move(to: p(0.41, 0.46)); path.addQuadCurve(to: p(0.19, 0.92), control: p(0.35, 0.74))
                path.move(to: p(0.59, 0.46)); path.addQuadCurve(to: p(0.81, 0.92), control: p(0.65, 0.74))
            } else {
                path.move(to: p(0.43, 0.35)); path.addLine(to: p(0.43, 0.45))
                path.move(to: p(0.57, 0.35)); path.addLine(to: p(0.57, 0.45))
            }
            return path
        }
        let flare: CGFloat = style == .aline ? 0.06 : (style == .wrap ? 0.12 : (style == .sheath ? 0.30 : 0.18))
        path.move(to: p(0.32, 0.07))
        path.addLine(to: p(0.15, 0.16)); path.addLine(to: p(0.23, 0.32))
        path.addLine(to: p(0.31, 0.27)); path.addLine(to: p(0.31, 0.46))
        path.addLine(to: p(flare, 0.94)); path.addLine(to: p(1 - flare, 0.94))
        path.addLine(to: p(0.69, 0.46)); path.addLine(to: p(0.69, 0.27))
        path.addLine(to: p(0.77, 0.32)); path.addLine(to: p(0.85, 0.16))
        path.addLine(to: p(0.68, 0.07)); path.addQuadCurve(to: p(0.32, 0.07), control: p(0.5, 0.25))
        path.closeSubpath()
        if style == .corset || style == .sheath {
            path.move(to: p(0.42, 0.18)); path.addLine(to: p(0.42, 0.47))
            path.move(to: p(0.58, 0.18)); path.addLine(to: p(0.58, 0.47))
            path.move(to: p(0.32, 0.47)); path.addQuadCurve(to: p(0.68, 0.47), control: p(0.50, 0.51))
        }
        if style == .wrap {
            path.move(to: p(0.68, 0.08)); path.addLine(to: p(0.34, 0.45)); path.addLine(to: p(0.68, 0.45))
        } else if style == .shirt {
            path.move(to: p(0.5, 0.13)); path.addLine(to: p(0.5, 0.91))
            path.move(to: p(0.36, 0.42)); path.addLine(to: p(0.64, 0.42))
        }
        return path
    }
}
/// A distinct view captures one attempt even if a previous system callback is late.
private struct DesignBriefExportBridge: View {
    let attempt: ExperimentalDesignBrief.ExportAttempt
    @Binding var isPresented: Bool
    let completed: (UUID, Result<URL, Error>) -> Void
    var body: some View {
        Color.clear.frame(width: 0, height: 0)
            .fileExporter(isPresented: $isPresented, document: ProjectExportDocument(data: attempt.bytes), contentType: .json,
                          defaultFilename: "kalip-istegi.tcad-request.json") { result in
                completed(attempt.id, result)
            }
    }
}
/// Main-thread ledger: stores identifiers only, never garment or measurement data.
@MainActor
enum DraftPendingLedger {
    static let key = "dressDraftPendingIDsV1"
    static var active = Set<String>()
    static func decode(_ raw: String) -> Set<String>? {
        guard let data = raw.data(using: .utf8), let ids = try? JSONDecoder().decode([String].self, from: data) else { return nil }
        return Set(ids)
    }
    static func read() -> Set<String>? {
        guard let value = UserDefaults.standard.object(forKey: key) else { return [] }
        guard let raw = value as? String else { return nil }
        return decode(raw)
    }
    @discardableResult static func add(_ id: String) -> Bool {
        active.insert(id)
        guard var ids = read() else { return false }
        ids.insert(id); return write(ids)
    }
    static func complete(_ ids: Set<String>) {
        active.subtract(ids)
        guard var existing = read() else { return }
        existing.subtract(ids); _ = write(existing)
    }
    static func acknowledge(_ displayed: Set<String>) {
        guard let current = read() else { return }
        _ = write(remaining(current: current, displayed: displayed, active: active))
    }
    static func remaining(current: Set<String>, displayed: Set<String>, active: Set<String>) -> Set<String> {
        current.subtracting(displayed.subtracting(active))
    }
    static func abandon(_ ids: Set<String>) { active.subtract(ids) }
    /// Preserves the exact property-list value, including unexpected older value types.
    static func corruptSnapshot() -> Data? {
        guard read() == nil, let value = UserDefaults.standard.object(forKey: key), active.isEmpty,
              let data = try? PropertyListSerialization.data(fromPropertyList: ["value": value], format: .binary, options: 0),
              data.count <= 262_144 else { return nil }
        return data
    }
    static func mayReset(expected: Data, current: Data?, hasActiveWrites: Bool) -> Bool {
        !hasActiveWrites && current == expected
    }
    static func resetCorrupt(expected: Data) -> Bool {
        guard let current = corruptSnapshot(), mayReset(expected: expected, current: current, hasActiveWrites: !active.isEmpty) else { return false }
        let backupKey = "dressDraftPendingBackupV1." + UUID().uuidString
        UserDefaults.standard.set(current, forKey: backupKey)
        guard UserDefaults.standard.data(forKey: backupKey) == current else { return false }
        UserDefaults.standard.set("[]", forKey: key)
        return true
    }
    private static func write(_ ids: Set<String>) -> Bool {
        guard let bytes = try? JSONEncoder().encode(ids.sorted()), let raw = String(data: bytes, encoding: .utf8) else { return false }
        UserDefaults.standard.set(raw, forKey: key); return true
    }
}
/// One assertion per screen, shared by its coalesced writes; UIKit may deny or expire it.
private final class DraftBackgroundLease {
    #if os(iOS)
    private var identifier: UIBackgroundTaskIdentifier = .invalid
    private var onExpiration: (() -> Void)?
    func begin(onExpiration: @escaping () -> Void) -> Bool {
        if identifier != .invalid { return true }
        self.onExpiration = onExpiration
        identifier = UIApplication.shared.beginBackgroundTask(withName: "Save dress draft") { [weak self] in
            guard let self else { return }
            let notify = self.onExpiration
            self.finish()
            notify?()
        }
        if identifier == .invalid { self.onExpiration = nil; return false }
        return true
    }
    func finish() {
        let old = identifier; identifier = .invalid; onExpiration = nil
        if old != .invalid { UIApplication.shared.endBackgroundTask(old) }
    }
    #else
    func begin(onExpiration: @escaping () -> Void) -> Bool { true }
    func finish() { }
    #endif
}
private struct ExperimentalDesignBriefView: View {
    @Environment(\.colorScheme) private var colorScheme
    @Environment(\.scenePhase) private var scenePhase
    @Environment(\.dynamicTypeSize) private var typeSize
    private var plum: Color { colorScheme == .dark ? Color(red: 1.0, green: 0.72, blue: 0.78) : Color(red: 0.29, green: 0.08, blue: 0.14) }
    private var ivory: Color { colorScheme == .dark ? Color(red: 0.11, green: 0.08, blue: 0.09) : Color(red: 0.988, green: 0.976, blue: 0.969) }
    @Environment(\.dismiss) private var dismiss
    private enum BriefInput: Hashable { case description, bust, waist, hips, length, notes }
    @FocusState private var focusedInput: BriefInput?
    @State private var style = ExperimentalDesignBrief.DressStyle.describe
    @State private var modelExplicitlySelected = false
    @State private var conflictingModelNotice = false
    @State private var importedPortable = false
    @State private var candidateIsPortable = false
    @State private var step = 0
    @State private var confirmClose = false
    @State private var description = ""
    @State private var bust = ""
    @State private var waist = ""
    @State private var hips = ""
    @State private var length = ""
    @State private var otherMeasurements = ""
    @State private var measurementProposal: MeasurementMessageParser.Proposal?
    @State private var messageMeasurementSnapshot: ExperimentalDesignBrief.MessageMeasurementSuggestion?
    @State private var descriptionMeasurementSuggestion: ExperimentalDesignBrief.DescriptionMeasurementSuggestion?
    @State private var showDescriptionMeasurements = false
    @State private var modelSuggestion: ExperimentalDesignBrief.ModelSuggestion?
    @State private var showModelSuggestion = false
    @State private var modelSuggestionRevision = UUID()
    @State private var photoObservationRequest: PhotoObservation.Request?
    @State private var modelSuggestionStatus = ""
    @State private var showMeasurementProposal = false
    @State private var measurementMessageStatus = ""
    @State private var showDesktopMeasurementWarning = false
    @State private var desktopWarningSnapshot: ExperimentalDesignBrief.DesktopWarningSnapshot?
    @State private var confirmingExportDestination = false
    @State private var pendingExportAttempt: ExperimentalDesignBrief.ExportAttempt?
    @State private var choosingRequestVersion = false
    @State private var requestExportVersion = 1
    @State private var image: Data?
    @State private var imageName: String?
    @State private var selectingImage = false
    @State private var referenceTask: Task<ExperimentalDesignBrief.ReferenceSelection, Error>?
    @State private var referenceLoadID: UUID?
    @State private var referenceLoading = false
    @State private var pendingReferenceChange: ExperimentalDesignBrief.ReferenceChange?
    @State private var confirmReferenceChange = false
    @State private var showingBodyMeasurements = false
    @State private var aLineGuideRequest: ALineMeasurementGuide.Request?
    @State private var aLineGuideInput = ALineMeasurementGuide.Input()
    @State private var confirmDiscardALineInput = false
    @State private var discardALineInputSnapshot: ALineMeasurementGuide.Input?
    @State private var measurementForm: ExperimentalDesignBrief.MeasurementForm?
    @State private var offerMeasurementForm = false
    @State private var exportPresentation = ExperimentalDesignBrief.ExportPresentation()
    @State private var exportAttempt: ExperimentalDesignBrief.ExportAttempt?
    @State private var savedDraft: ExperimentalDesignBrief.Draft?
    @State private var message = ""

    @State private var restoreCandidate: ExperimentalDesignBrief.Draft?
    @State private var draftRevision: Data?
    @State private var draftWriter = ExperimentalDesignBrief.DraftWriter()
    @State private var backgroundLease = DraftBackgroundLease()
    @AppStorage("dressDraftPendingIDsV1") private var pendingLedger = "[]"
    @AppStorage("dressDraftBackgroundUncertain") private var legacyUncertain = false
    @State private var ownedPending = Set<String>()
    @State private var markerError = false
    @State private var corruptLedgerSnapshot: Data?
    @State private var confirmLedgerReset = false
    @State private var saveRequest = UUID()
    @State private var offerRestore = false
    @State private var draftStorageReady = false
    @State private var storageChecked = false
    @State private var draftRetryAvailable = false
    @State private var recoveryCopy: ProjectExportDocument?
    @State private var showingDraftCopyExport = false
    @State private var showingDraftCopyImport = false
    @State private var showingPortableRequestImport = false
    @State private var copyCandidate: ExperimentalDesignBrief.Draft?
    @State private var confirmCopyRestore = false
    @State private var draftTransition = false
    @State private var localStatus = "Taslak kontrol ediliyor."
    private var draftURL: URL {
        FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0].appendingPathComponent("dress-brief-draft.json")
    }
    private var draftNeedsAttention: Bool {
        draftRetryAvailable || (storageChecked && !draftStorageReady) || markerError || legacyUncertain || pendingLedger != "[]"
    }
    private func measurementTile(_ title: String, value: String) -> some View {
        VStack(alignment: .leading, spacing: 5) {
            Text(title).font(.caption2.weight(.semibold)).foregroundStyle(.secondary)
            Text(value).font(.subheadline.weight(.semibold)).fixedSize(horizontal: false, vertical: true)
        }
        .frame(maxWidth: .infinity, minHeight: 55, alignment: .topLeading)
        .padding(8)
        .background(plum.opacity(colorScheme == .dark ? 0.12 : 0.05), in: RoundedRectangle(cornerRadius: 8))
        .accessibilityElement(children: .combine)
    }
    private func styleCaption(_ option: ExperimentalDesignBrief.DressStyle) -> String {
        switch option {
        case .wrap: return "Kruvaze görünüm · örnek"
        case .aline: return "Geniş etek · örnek"
        case .corset: return "Yapısal gövde · örnek"
        case .sheath: return "Düz hat · örnek"
        case .straight: return "Düz siluet · örnek"
        case .shirt: return "Gömlek yakalı · örnek"
        case .describe: return "Kendi sözlerinle"
        }
    }
    private func checkLocalDraft() {
        guard !storageChecked else { return }
        storageChecked = true
        do {
            try FileManager.default.createDirectory(at: draftURL.deletingLastPathComponent(), withIntermediateDirectories: true)
            if let previous = try ExperimentalDesignBrief.DraftStore.load(draftURL) {
                draftRevision = previous.revision
                if !previous.draft.hasInput { draftWriter.configure(revision: draftRevision); draftStorageReady = true; localStatus = "Boş taslak bu cihazda korunuyor."; return }
                restoreCandidate = previous.draft; offerRestore = true
                localStatus = "Önceki taslak bulundu; seçim yapılana kadar otomatik kayıt kapalı."
            } else { draftWriter.configure(revision: nil); draftStorageReady = true; localStatus = "Taslak bu cihazda saklanacak." }
        } catch { draftRetryAvailable = true; localStatus = "Yerel taslak okunamadı. Dosya korundu; yeniden deneyin veya isteği dosyaya kaydedin." }
    }
    private func beginDraftLease() -> Bool {
        backgroundLease.begin {
            markerError = true
            localStatus = "Arka plan kayıt süresi doldu; son değişikliklerin kaydedildiğini doğrulayın."
        }
    }
    private func saveLocalDraft(_ value: ExperimentalDesignBrief.Draft, immediately: Bool = false) {
        guard draftStorageReady else { return }
        let request = UUID(); saveRequest = request
        let marker = request.uuidString; ownedPending.insert(marker)
        if !DraftPendingLedger.add(marker) { markerError = true }
        let coveredMarkers = ownedPending
        if !beginDraftLease() { markerError = true }
        localStatus = "Taslak kaydı bekleniyor; tamamlanmadan uygulama kapanırsa son değişiklikler kaybolabilir."
        draftWriter.submit(value, to: draftURL, immediately: immediately) { result in
            if case .failure(let error) = result, error is ExperimentalDesignBrief.DraftStore.Conflict {
                guard !draftTransition else { return }
                draftStorageReady = false; draftRetryAvailable = false
                let stoppedMarkers = ownedPending
                draftWriter.pause { DraftPendingLedger.abandon(stoppedMarkers); backgroundLease.finish() }
                localStatus = "Taslak başka bir pencerede değişti. İçeriği ayrı kurtarma kopyasına kaydedin; diğer kayıt korunuyor."; return
            }
            guard saveRequest == request else { return }
            backgroundLease.finish()
            switch result {
            case .success:
                DraftPendingLedger.complete(coveredMarkers); ownedPending.subtract(coveredMarkers); markerError = false
                draftRetryAvailable = false; localStatus = "Taslak bu cihazda kaydedildi."
            case .failure(let error):
                DraftPendingLedger.abandon(coveredMarkers)
                if error is ExperimentalDesignBrief.DraftStore.Conflict {
                    draftStorageReady = false; draftRetryAvailable = false
                    localStatus = "Taslak başka bir pencerede değişti. İçeriği ayrı kurtarma kopyasına kaydedin; diğer kayıt korunuyor."
                } else {
                    draftRetryAvailable = true
                    localStatus = "Taslak kaydedilemedi. Alan ve saklama sınırını kontrol edip yeniden deneyin; önceki kayıt korundu."
                }
            }
        }
    }
    private func restore(_ value: ExperimentalDesignBrief.Draft) {
        cancelReferenceLoad()
        style = value.style; modelExplicitlySelected = value.style != .describe; importedPortable = false
        description = value.description; bust = value.bust; waist = value.waist
        hips = value.hips; length = value.length; otherMeasurements = value.otherMeasurements
        image = value.image; imageName = value.imageName; measurementForm = value.measurementForm
        aLineGuideInput = value.aLineGuideInput ?? .init()
        draftWriter.configure(revision: draftRevision)
        draftStorageReady = true
        localStatus = "Önceki taslak geri yüklendi; değişiklikler bu cihazda saklanır."
    }
    private var referenceDecisionPending: Bool {
        ExperimentalDesignBrief.referenceDecisionPending(loading: referenceLoading, awaitingConfirmation: confirmReferenceChange, hasCandidate: pendingReferenceChange != nil)
    }
    private var referenceBlocksClose: Bool {
        ExperimentalDesignBrief.referenceBlocksClose(selecting: selectingImage, loading: referenceLoading,
            awaitingConfirmation: confirmReferenceChange, hasCandidate: pendingReferenceChange != nil)
    }
    private var currentReference: ExperimentalDesignBrief.ReferenceSelection? {
        guard let image, let imageName else { return nil }
        return .init(data: image, name: imageName)
    }
    private var aLineInputPending: Bool { !aLineGuideInput.unit.isEmpty || aLineGuideInput.values.contains { !$0.isEmpty } }
    private func openALineGuide() {
        guard style == .aline, modelExplicitlySelected, !draftTransition, !referenceBlocksClose, measurementForm?.hasValues != true else {
            message = "Önce A kesim modelini seçin ve bekleyen görsel veya ölçü formu işlemini tamamlayın."
            return
        }
        aLineGuideRequest = .init(draft: aLineSourceDraft, explicitSelection: modelExplicitlySelected, importedPortable: importedPortable,
                                  version: requestExportVersion, revision: modelSuggestionRevision)
    }
    private func applyALineGuide(_ request: ALineMeasurementGuide.Request, input: ALineMeasurementGuide.Input, notes: String) -> String? {
        guard request.matches(aLineSourceDraft, explicitSelection: modelExplicitlySelected, importedPortable: importedPortable,
                              version: requestExportVersion, revision: modelSuggestionRevision, activeID: aLineGuideRequest?.id,
                              pending: draftTransition || referenceBlocksClose || measurementForm?.hasValues == true) else {
            return "İstek, seçim veya görsel değişti; ölçüler eklenmedi. Ekranı kapatıp yeniden inceleyin."
        }
        do {
            guard try ALineMeasurementGuide.prepare(input, draft: aLineSourceDraft) == notes else {
                return "Ölçüler veya kaynak değişti; hiçbir değer eklenmedi."
            }
            otherMeasurements = notes
            aLineGuideInput = .init()
            message = "Onaylanan A kesim değerleri notlara eklendi. Bu bir kalıp veya üretim onayı değildir."
            return nil
        } catch { return "Mevcut ölçü, çelişki veya sınır nedeniyle değerler eklenmedi; girişler ve önceki notlar korundu." }
    }
    private func cancelReferenceLoad() {
        referenceLoadID = nil; referenceTask?.cancel(); referenceTask = nil; referenceLoading = false
    }
    private func startReferenceLoad(_ url: URL) {
        guard !draftTransition, !referenceDecisionPending else {
            message = "Önce devam eden taslak veya görsel işlemini tamamlayın."; return
        }
        cancelReferenceLoad()
        let ticket = ExperimentalDesignBrief.ReferenceLoadTicket(original: currentReference)
        referenceLoadID = ticket.id; referenceLoading = true; message = "Görsel yükleniyor; mevcut görsel korunuyor."
        let worker = Task.detached(priority: .userInitiated) { () throws -> ExperimentalDesignBrief.ReferenceSelection in
            let data = try ExperimentalDesignBrief.readImage(url)
            try Task.checkCancellation()
            _ = try ExperimentalDesignBrief.make(prompt: "kontrol", measurements: "", image: data, imageName: url.lastPathComponent)
            try Task.checkCancellation()
            return .init(data: data, name: url.lastPathComponent)
        }
        referenceTask = worker
        Task { @MainActor in
            do {
                let candidate = try await worker.value
                guard ticket.id == referenceLoadID else { return }
                referenceLoading = false; referenceTask = nil; referenceLoadID = nil
                guard ticket.matches(id: ticket.id, current: currentReference) else { message = "Seçili görsel değişti; yüklenen görsel uygulanmadı."; return }
                pendingReferenceChange = .init(original: ticket.original, replacement: candidate)
                if currentReference == nil { applyReferenceChange() } else { confirmReferenceChange = true }
            } catch {
                guard ticket.id == referenceLoadID else { return }
                referenceLoading = false; referenceTask = nil; referenceLoadID = nil
                if !(error is CancellationError) { message = "Görsel okunamadı veya desteklenmiyor; mevcut görsel korundu." }
            }
        }
    }
    private func applyReferenceChange() {
        guard let change = pendingReferenceChange else { return }
        defer { pendingReferenceChange = nil }
        do {
            let result = try change.apply(to: currentReference)
            image = result?.data; imageName = result?.name
            message = result == nil ? "Görsel kaldırıldı." : "Görsel eklendi."
        } catch { message = "Seçili görsel değişti; işlem uygulanmadı. Tekrar deneyin." }
    }
    private func openPhotoObservation() {
        guard !draftTransition, !referenceBlocksClose, currentReference != nil else {
            message = "Önce görsel seçimini tamamlayın; ardından fotoğrafı inceleyebilirsiniz."
            return
        }
        photoObservationRequest = .init(draft: draft, explicitSelection: modelExplicitlySelected,
            importedPortable: importedPortable, version: requestExportVersion, revision: modelSuggestionRevision)
    }
    private func applyPhotoObservation(_ request: PhotoObservation.Request, selection: PhotoObservation.Selection) -> String? {
        guard request.matches(draft, explicitSelection: modelExplicitlySelected, importedPortable: importedPortable,
                              version: requestExportVersion, revision: modelSuggestionRevision,
                              activeID: photoObservationRequest?.id, referencePending: referenceBlocksClose, transition: draftTransition) else {
            return "İstek, model veya görsel değişti; önceki gözlem eklenmedi. Ekranı kapatıp tekrar inceleyin."
        }
        do {
            let updated = try PhotoObservation.append(selection, description: request.draft.description, notes: request.draft.otherMeasurements)
            guard updated != request.draft.description else {
                return "Seçilen gözlemler zaten mevcut; hiçbir değişiklik yapılmadı."
            }
            description = updated
            message = "Kendi fotoğraf gözlemleriniz tarife eklendi. Model ve tarifle çelişkileri kendiniz kontrol edin; otomatik karşılaştırma yapılmadı."
            return nil
        } catch PhotoObservation.Failure.duplicate {
            return "Bu özellik için farklı bir fotoğraf gözlemi var. Hiçbir gözlem eklenmedi; seçiminizi veya önceki metni kontrol edin."
        } catch PhotoObservation.Failure.tooLong {
            return "Tarif sınırı aşılacağı için gözlemler eklenmedi; mevcut metin ve görsel korundu."
        } catch { return "Gözlem eklenemedi; mevcut istek ve görsel korundu." }
    }
    private func editPhotoObservation(_ request: PhotoObservation.Request, plan: PhotoObservation.EditPlan) -> String? {
        guard request.matches(draft, explicitSelection: modelExplicitlySelected, importedPortable: importedPortable,
                              version: requestExportVersion, revision: modelSuggestionRevision,
                              activeID: photoObservationRequest?.id, referencePending: referenceBlocksClose, transition: draftTransition) else {
            return "İstek, model veya görsel değişti; önceki gözlem değiştirilmedi. Ekranı kapatıp tekrar inceleyin."
        }
        do {
            let current = try PhotoObservation.editPlan(plan.action, category: plan.category, selection: plan.selection,
                description: draft.description, notes: draft.otherMeasurements)
            guard current == plan else { return "Gözlem değişti; hiçbir işlem yapılmadı. Yeniden inceleyin." }
            description = current.updatedDescription
            message = plan.action == .remove ? "Seçili fotoğraf gözlemi kaldırıldı; diğer metin ve görsel korundu." : "Fotoğraf gözlemi değiştirildi. Model ve tarifle olası çelişkileri kendiniz kontrol edin."
            return nil
        } catch { return "Gözlem tekil ve tam olarak doğrulanamadı veya metin sınırı aşıldı; hiçbir işlem yapılmadı." }
    }
    private var draft: ExperimentalDesignBrief.Draft {
        .init(style: style, description: description, bust: bust, waist: waist, hips: hips, length: length,
              otherMeasurements: otherMeasurements, image: image, imageName: imageName, measurementForm: measurementForm,
              aLineGuideInput: aLineInputPending ? aLineGuideInput : nil)
    }
    private var aLineSourceDraft: ExperimentalDesignBrief.Draft {
        var source = draft; source.aLineGuideInput = nil; return source
    }
    private var hasUnsavedInput: Bool { draft.requiresDiscardConfirmation(saved: savedDraft) }
    private var requestPrompt: String { draft.prompt }
    private var requestMeasurements: String { draft.measurements }
    private func closeAfterSaving() {
        guard !aLineInputPending else {
            message = "A kesim rehberinde notlara eklenmemiş girişler var. Rehberi tamamlayın veya girişleri açıkça temizleyin."
            step = 1; return
        }
        guard !referenceBlocksClose else {
            message = "Görsel seçimini tamamlayın veya yüklemeyi açıkça iptal edin; ardından kapatabilirsiniz."
            return
        }
        guard !draftTransition else { return }
        guard draftStorageReady else { confirmClose = true; return }
        draftTransition = true; saveRequest = UUID()
        let marker = saveRequest.uuidString; ownedPending.insert(marker)
        if !DraftPendingLedger.add(marker) { markerError = true }
        let coveredMarkers = ownedPending
        if !beginDraftLease() { markerError = true }
        localStatus = "Son taslak kaydediliyor; lütfen bekleyin."
        let closingDraft = draft
        draftWriter.submit(closingDraft, to: draftURL, immediately: true) { result in
            draftTransition = false; backgroundLease.finish()
            switch result {
            case .success:
                DraftPendingLedger.complete(coveredMarkers); ownedPending.subtract(coveredMarkers); markerError = false
                guard ExperimentalDesignBrief.canFinishDraftClose(saved: closingDraft, current: draft, referencePending: referenceBlocksClose) else {
                    localStatus = "Kayıt sırasında taslak veya görsel seçimi değişti; ekran açık. Son içeriği kontrol edip yeniden kapatın."
                    return
                }
                dismiss()
            case .failure(let error):
                DraftPendingLedger.abandon(coveredMarkers)
                if error is ExperimentalDesignBrief.DraftStore.Conflict { draftStorageReady = false }
                draftRetryAvailable = draftStorageReady
                localStatus = "Son taslak kaydedilemedi; ekran açık. Ayrı kurtarma kopyası kaydedin veya yeniden deneyin."
            }
        }
    }
    private func proposeModelSuggestion() {
        guard !draftTransition, !referenceDecisionPending else { modelSuggestionStatus = "Önce devam eden işlemi tamamlayın."; return }
        let current = draft
        guard let suggestion = ExperimentalDesignBrief.suggestedModel(current) else {
            modelSuggestionStatus = "Model önerilemedi. Tek bir kısa model adı yazabilir veya kartlardan seçebilirsiniz; metniniz korundu."
            return
        }
        modelSuggestion = .init(draft: current, suggested: suggestion, explicitSelection: modelExplicitlySelected, importedPortable: importedPortable, revision: modelSuggestionRevision)
        modelSuggestionStatus = ""; showModelSuggestion = true
    }
    private func applyModelSuggestion() {
        guard let proposal = modelSuggestion else { return }
        modelSuggestion = nil
        guard !draftTransition, !referenceDecisionPending,
              proposal.matches(draft, explicitSelection: modelExplicitlySelected, importedPortable: importedPortable, revision: modelSuggestionRevision) else {
            modelSuggestionStatus = "Taslak veya model seçimi değişti; öneriyi yeniden inceleyin."
            return
        }
        style = proposal.suggested; modelExplicitlySelected = true; importedPortable = false
        modelSuggestionStatus = "Model seçildi; özgün metin ve görsel korundu. Tarif ve modelin uyumunu kontrol edin."
    }
    private func parseMeasurementMessage() throws -> MeasurementMessageParser.Proposal {
        try ExperimentalDesignBrief.measurementMessageProposal(draft)
    }
    private func proposeDescriptionMeasurements() {
        guard !draftTransition, !referenceDecisionPending,
              let proposal = ExperimentalDesignBrief.descriptionMeasurements(draft) else {
            measurementMessageStatus = "Tarifteki ölçüler güvenle önerilemedi. Modeli, ölçüleri ve birimleri gözden geçirin; metin ve alanlar korunuyor."
            return
        }
        descriptionMeasurementSuggestion = .init(draft: draft, proposal: proposal, explicitSelection: modelExplicitlySelected,
            importedPortable: importedPortable, version: requestExportVersion, revision: modelSuggestionRevision)
        measurementMessageStatus = ""; showDescriptionMeasurements = true
    }
    private func applyDescriptionMeasurements() {
        guard let suggestion = descriptionMeasurementSuggestion else { return }
        descriptionMeasurementSuggestion = nil
        guard !draftTransition, !referenceDecisionPending,
              suggestion.matches(draft, explicitSelection: modelExplicitlySelected, importedPortable: importedPortable,
                                 version: requestExportVersion, revision: modelSuggestionRevision),
              ExperimentalDesignBrief.descriptionMeasurements(draft) == suggestion.proposal else {
            measurementMessageStatus = "Taslak veya seçim değişti; hiçbir ölçü alanı değiştirilmedi. Tarifi yeniden inceleyin."
            return
        }
        let values = suggestion.proposal.values
        if let value = values["göğüs çevresi"] { bust = value }
        if let value = values["bel çevresi"] { waist = value }
        if let value = values["kalça çevresi"] { hips = value }
        if let value = values["elbise boyu"] { length = value }
        measurementMessageStatus = "Onaylanan ölçüler alanlara eklendi; özgün tarif de korundu. Çelişki olup olmadığını gözden geçirin."
    }
    private func proposeMeasurementMessage() {
        do {
            guard !draftTransition, !referenceDecisionPending else { throw MeasurementMessageParser.Failure.descriptionUncertain }
            let proposal = try parseMeasurementMessage()
            measurementProposal = proposal
            messageMeasurementSnapshot = .init(draft: draft, proposal: proposal, explicitSelection: modelExplicitlySelected,
                importedPortable: importedPortable, version: requestExportVersion, revision: modelSuggestionRevision)
            measurementMessageStatus = ""; showMeasurementProposal = true
        } catch MeasurementMessageParser.Failure.duplicate {
            measurementMessageStatus = "Aynı ölçü mesajda birden fazla kez geçiyor. Çelişkiyi önce düzeltin; hiçbir alan değiştirilmedi."
        } catch MeasurementMessageParser.Failure.existing {
            measurementMessageStatus = "Mesajdaki ölçülerden biri ilgili alanda zaten var. Önce değerleri karşılaştırın; mevcut alanlar değiştirilmedi."
        } catch MeasurementMessageParser.Failure.ambiguousNumber {
            measurementMessageStatus = "Sayı hem ondalık hem binlik olarak okunabilir. Örneğin 1,000 yerine neyi kastettiğinize göre 1000 mm veya 1 mm yazın; hiçbir alan değiştirilmedi."
        } catch MeasurementMessageParser.Failure.descriptionConflict {
            measurementMessageStatus = "Tarifteki ve ölçü mesajındaki değerler çelişiyor. İkisini karşılaştırıp düzeltin; hiçbir alan değiştirilmedi."
        } catch MeasurementMessageParser.Failure.descriptionUncertain {
            measurementMessageStatus = "Tarifte ölçüyle çelişen, belirsiz veya koşullu bir ifade olabilir. Tarifi ve ölçü mesajını birlikte inceleyin; hiçbir alan değiştirilmedi."
        } catch {
            measurementMessageStatus = "Mesaj okunamadı. Yalnız açık etiketli pozitif sayıları cm veya mm ile yazın; diğer metinleri ayrı notta saklayın. Hiçbir alan değiştirilmedi."
        }
    }
    private func applyMeasurementProposal() {
        guard let proposal = measurementProposal, let snapshot = messageMeasurementSnapshot else { return }
        messageMeasurementSnapshot = nil
        do {
            guard !draftTransition, !referenceDecisionPending,
                  snapshot.matches(draft, explicitSelection: modelExplicitlySelected, importedPortable: importedPortable,
                                   version: requestExportVersion, revision: modelSuggestionRevision),
                  snapshot.proposal == proposal, proposal.isApplicable, try parseMeasurementMessage() == proposal else {
                throw MeasurementMessageParser.Failure.unrecognized
            }
            if let value = proposal.values["göğüs çevresi"] { bust = value }
            if let value = proposal.values["bel çevresi"] { waist = value }
            if let value = proposal.values["kalça çevresi"] { hips = value }
            if let value = proposal.values["elbise boyu"] { length = value }
            measurementMessageStatus = "Onaylanan ölçüler alanlara eklendi; özgün mesaj da korundu. Dışa aktarımda ikisi de görünür."
            measurementProposal = nil
        } catch { measurementMessageStatus = "Mesaj veya ölçü alanları değişti; hiçbir alan değiştirilmedi. Yeniden inceleyin."; measurementProposal = nil }
    }
    private func prepareRecoveryCopy() {
        guard !referenceDecisionPending else { message = "Önce görsel yüklemesini veya görsel seçimini tamamlayın ya da iptal edin."; return }
        do {
            recoveryCopy = ProjectExportDocument(data: try ExperimentalDesignBrief.DraftStore.recoveryData(draft))
            showingDraftCopyExport = true
        } catch { localStatus = "Kurtarma kopyası hazırlanamadı; alanları ve boyut sınırlarını kontrol edin." }
    }
    private var desktopMeasurementRisk: Bool {
        let fields = [description, draft.measurements]
        return fields.contains { MeasurementMessageParser.mayNeedDesktopReview($0) }
            || MeasurementMessageParser.containsExplicitMeasurement(description)
            || MeasurementMessageParser.hasConditionalMeasurementRisk(fields)
    }
    private var measurementReviewStartStep: Int {
        let fields = [description, draft.measurements]
        return MeasurementMessageParser.mayNeedDesktopReview(description) || MeasurementMessageParser.containsExplicitMeasurement(description)
            || MeasurementMessageParser.hasConditionalMeasurementRisk(fields) ? 0 : 1
    }
    private func prepareExport(confirmedDesktopWarning: Bool = false) {
        guard !aLineInputPending else { message = "A kesim rehberindeki girişler henüz isteğe eklenmedi. Önce onaylayın veya açıkça temizleyin."; step = 1; return }
        guard !referenceDecisionPending else { message = "Önce görsel yüklemesini veya görsel seçimini tamamlayın ya da iptal edin."; return }
        guard measurementForm?.hasValues != true else {
            message = "Ölçü formunda onaylanmamış giriş var; yalnız boşluk içeren alanlar da buna dahildir. Formu inceleyin veya ayrı kurtarma kopyası kullanın."; step = 1; return
        }
        guard requestExportVersion == 2 || !ExperimentalDesignBrief.hasConflictingModelLabels(style: style, description: description) else {
            message = "Açıklamada önceki model satırı var. Metni gözden geçirip düzenleyin veya Tarif edeceğim seçin; mevcut metin korunuyor."
            step = 0; return
        }
        if !confirmedDesktopWarning && desktopMeasurementRisk {
            desktopWarningSnapshot = .init(draft: draft, version: requestExportVersion)
            showDesktopMeasurementWarning = true; return
        }
        do {
            let candidate = draft
            let data: Data
            if requestExportVersion == 2 { data = try ExperimentalDesignBrief.makeV2(candidate) }
            else { data = try ExperimentalDesignBrief.make(prompt: candidate.prompt, measurements: candidate.measurements, image: candidate.image, imageName: candidate.imageName) }
            pendingExportAttempt = ExperimentalDesignBrief.ExportAttempt(draft: candidate, bytes: data)
            confirmingExportDestination = true
        } catch {
            message = "Dosya hazırlanamadı. Tarif ve ölçüler en fazla 16.384 UTF-16 birimi, görsel 8 MiB olabilir. V1 model satırı eklenince sınır aşılabilir; metin kesilmedi ve taslak korundu."
        }
    }
    var body: some View {
        NavigationStack {
            Form {
                if step == 0 {
                Section {
                    let overview = ExperimentalDesignBrief.AtelierOverview(draft)
                    let summary = ExperimentalDesignBrief.RequestSummary(draft)
                    Text("Çalışman özeti").font(.headline).fontDesign(.serif).foregroundStyle(plum).accessibilityAddTraits(.isHeader)
                    Label(summary.model ?? "Ayrı model seçilmedi", systemImage: summary.model == nil ? "circle" : "checkmark.circle.fill")
                        .font(.subheadline.weight(.semibold))
                    if ExperimentalDesignBrief.hasConflictingModelLabels(style: style, description: description) {
                        Text("Seçilen model ile tarifteki model bilgisini kontrol edin.").font(.caption).foregroundStyle(plum)
                    }
                    Text(summary.description.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty ? "Henüz tarif yazılmadı." : summary.description)
                        .font(.subheadline).lineLimit(3).accessibilityLabel("Tarif: \(summary.description.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty ? "Henüz tarif yazılmadı" : summary.description)")
                    HStack(alignment: .top, spacing: 8) {
                        measurementTile("GÖĞÜS", value: overview.bust)
                        measurementTile("BEL", value: overview.waist)
                        measurementTile("KALÇA", value: overview.hips)
                    }
                    if overview.hasOtherMeasurements { Text("Ek ölçü veya not girildi.").font(.caption) }
                    if !otherMeasurements.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
                        Text("Kutulardaki ölçüler, ölçü mesajı veya ek notlarla karşılaştırılmadı. Çelişki olup olmadığını tüm metni inceleyerek kontrol edin.")
                            .font(.caption).foregroundStyle(plum)
                    }
                    if overview.hasPendingMeasurements { Text("Onaylanmamış ölçüler özete dahil değil.").font(.caption).foregroundStyle(plum) }
                }
                Section {
                    Text("ATÖLYE PROJESİ · YENİ ÇALIŞMA").font(.caption.weight(.semibold)).foregroundStyle(plum).padding(.horizontal, 10).padding(.vertical, 5).background(plum.opacity(0.10), in: Capsule())
                    Text("Modelini seç").font(.largeTitle).fontDesign(.serif).foregroundStyle(plum).accessibilityAddTraits(.isHeader)
                    Text("Özel dikim için temel elbise siluetini belirleyin veya kendi tarifinizi yazın.").font(.subheadline).foregroundStyle(.secondary)
                    Text("TEMEL SİLUETLER · ÖRNEK TERCİHLER").font(.caption.weight(.semibold)).foregroundStyle(.secondary)
                    ScrollView(.horizontal) {
                    HStack(alignment: .top, spacing: 12) {
                        ForEach([ExperimentalDesignBrief.DressStyle.wrap, .aline, .corset, .sheath, .straight, .shirt, .describe]) { option in
                            Button {
                                if option != .describe && ExperimentalDesignBrief.hasEmbeddedModelLine(description) {
                                    conflictingModelNotice = true
                                } else { style = option; modelExplicitlySelected = true; importedPortable = false }
                            } label: {
                                VStack(alignment: .leading, spacing: 8) {
                                    ZStack(alignment: .topTrailing) {
                                    if option == .describe {
                                        Image(systemName: "text.bubble")
                                            .font(.system(size: 38)).frame(height: 160)
                                    } else {
                                        DressStyleSketch(style: option)
                                            .stroke((modelExplicitlySelected && style == option) ? plum : Color.secondary,
                                                    style: StrokeStyle(lineWidth: 2, lineCap: .round, lineJoin: .round))
                                            .frame(width: 112, height: 160)
                                    }
                                    }
                                    .frame(maxWidth: .infinity, minHeight: 176)
                                    .background(Color.secondary.opacity(0.06))
                                    .clipShape(RoundedRectangle(cornerRadius: 10))
                                    .overlay(alignment: .topTrailing) {
                                        if modelExplicitlySelected && style == option {
                                            Image(systemName: "checkmark.circle.fill").foregroundStyle(plum).padding(8).accessibilityHidden(true)
                                        }
                                    }
                                    Text(option.rawValue).font(.headline).fontDesign(.serif)
                                        .multilineTextAlignment(.leading).fixedSize(horizontal: false, vertical: true)
                                    Text(styleCaption(option))
                                        .font(.caption).foregroundStyle(.secondary)
                                    Text((modelExplicitlySelected && style == option) ? "✓ Seçili" : "Seç")
                                        .font(.caption.weight(.semibold)).foregroundStyle(plum)
                                }
                                .frame(width: typeSize.isAccessibilitySize ? 230 : 174)
                                .frame(minHeight: 250)
                                .padding(8)
                                .background(colorScheme == .dark ? Color(red: 0.18, green: 0.13, blue: 0.15) : Color.white)
                                .overlay(RoundedRectangle(cornerRadius: 14).stroke((modelExplicitlySelected && style == option) ? plum : Color.secondary.opacity(0.25), lineWidth: (modelExplicitlySelected && style == option) ? 2 : 1))
                                .clipShape(RoundedRectangle(cornerRadius: 14))
                            }
                            .buttonStyle(.plain)
                            .accessibilityLabel(option.rawValue)
                            .accessibilityHint("Model tercihini belirler; kalıp oluşturmaz.")
                            .accessibilityValue((modelExplicitlySelected && style == option) ? "Seçili" : "Seçili değil")
                            .accessibilityAddTraits((modelExplicitlySelected && style == option) ? .isSelected : [])
                        }
                    }
                    }
                    Text("Seçim bir model tarifidir; hazır kalıp veya görsel değildir. İsterseniz ayrıntıları kendi sözlerinizle yazın.").font(.caption)
                    if importedPortable { Text("İçe aktarılan açıklama aşağıdadır. Dosyada ayrı bir model kimliği bulunmuyor; model seçilmedi. İsterseniz bir kart seçin.").font(.caption) }
                    Text("Nasıl bir elbise istiyorsun?").font(.title3).fontDesign(.serif).foregroundStyle(plum).accessibilityAddTraits(.isHeader)
                    Text("Kumaş, yaka, boy ve stil ayrıntılarını yazın.").font(.subheadline).foregroundStyle(.secondary)
                    TextEditor(text: $description).frame(minHeight: 100).accessibilityLabel("Elbise tarifiniz").focused($focusedInput, equals: .description)
                    Button("Yazdığım model adını incele") { proposeModelSuggestion() }.frame(minHeight: 44)
                    Button("Tarifteki ölçüleri incele") { proposeDescriptionMeasurements() }.frame(minHeight: 44)
                    Text("Yalnız tamamı tanınan açık bir tarifteki ölçüler önerilir. Onaylamadan alanlar değişmez; tarif aynen kalır.").font(.caption)
                    if !measurementMessageStatus.isEmpty { Text(measurementMessageStatus).font(.caption) }
                    Text("Tek bir model adı veya ‘A kesim elbise istiyorum, uzun kollu’ gibi açık bir ifade aranır. Öneriyi onaylamadan tüm ifadenizi kontrol edin.").font(.caption)
                    if !modelSuggestionStatus.isEmpty { Text(modelSuggestionStatus).font(.caption) }
                    ScrollView(.horizontal) {
                        HStack { ForEach(["Midi boy", "Balon kol", "Gizli pens", "V yaka", "Astar payı dahil"], id: \.self) { tag in
                            Button("+ " + tag) {
                                let addition = (description.isEmpty ? "" : "\n") + tag
                                if description.utf16.count + addition.utf16.count <= ExperimentalDesignBrief.maxTextUnits { description += addition }
                                else { message = "Tarif uzunluk sınırına ulaştı; önceki metin korundu." }
                            }.buttonStyle(.bordered).controlSize(.large)
                        } }
                    }
                }
                }
                if step == 0 {
                Section {
                    let overview = ExperimentalDesignBrief.AtelierOverview(draft)
                    Text("Ölçülerini ekle").font(.title3).fontDesign(.serif).foregroundStyle(plum).accessibilityAddTraits(.isHeader)
                    HStack(alignment: .top, spacing: 8) {
                        measurementTile("GÖĞÜS", value: overview.bust)
                        measurementTile("BEL", value: overview.waist)
                        measurementTile("KALÇA", value: overview.hips)
                    }
                    if overview.hasOtherMeasurements { Text("Ek ölçü veya not girildi.").font(.caption) }
                    if !otherMeasurements.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
                        Text("Kutulardaki ölçüler, ölçü mesajı veya ek notlarla karşılaştırılmadı. Çelişki olup olmadığını tüm metni inceleyerek kontrol edin.")
                            .font(.caption).foregroundStyle(plum)
                    }
                    if overview.hasPendingMeasurements { Text("Onaylanmamış ölçü formu var.").font(.caption) }
                    Button("Ölçüleri gir veya düzenle") { focusedInput = nil; step = 1 }.frame(minHeight: 44)
                    Text("Örnek görsel").font(.headline).accessibilityAddTraits(.isHeader)
                    if let name = overview.referenceName { Text("Eklendi: \(ModelStore.safeLabel(name))") }
                    else { Text("Görsel eklenmedi.").foregroundStyle(.secondary) }
                    Button("Örnek görseli seç veya değiştir") { focusedInput = nil; step = 2 }.frame(minHeight: 44)
                    Label("Kalıp henüz oluşturulmadı", systemImage: "doc.text")
                        .font(.subheadline.weight(.semibold)).foregroundStyle(plum)
                    Text("Bu ekran yalnız istek hazırlar; kalıp veya baskı oluşturmaz.").font(.caption).foregroundStyle(.secondary)
                }
                }
                if step == 0 || step == 1 {
                Section("Bildiğiniz ölçüler neler?") {
                    VStack(alignment: .leading, spacing: 8) {
                        Label("Ölçülerinizi mesaj gibi yazın", systemImage: "text.bubble")
                            .font(.headline).accessibilityAddTraits(.isHeader)
                        Text("Örneğin: Göğüs çevresi 92 cm, bel çevresi 72 cm. Bildiklerinizi birimleriyle yazın; bilmediklerinizi boş bırakın.")
                            .font(.subheadline).foregroundStyle(.secondary)
                        TextEditor(text: $otherMeasurements).frame(minHeight: 100)
                            .accessibilityLabel("Ölçü mesajınız")
                            .accessibilityHint("Bildiğiniz ölçüleri ve birimlerini yazın; metin aynen not olarak saklanır.")
                            .focused($focusedInput, equals: .notes)
                        Text("İsterseniz açık etiketli göğüs, bel, kalça çevresi ve elbise boyunu cm veya mm ile denetleyin. İfadeleri virgül, nokta, noktalı virgül, satır veya “ve” ile ayırın; diğer ifadeler reddedilir. Metin korunur.")
                            .font(.caption).foregroundStyle(.secondary)
                        Button("Mesajdaki ölçüleri incele") { proposeMeasurementMessage() }.frame(minHeight: 44)
                        Button("Mesajdaki model adını incele") { proposeModelSuggestion() }.frame(minHeight: 44)
                        if !modelSuggestionStatus.isEmpty { Text(modelSuggestionStatus).font(.caption) }
                        if !measurementMessageStatus.isEmpty { Text(measurementMessageStatus).font(.caption) }
                    }
                    Button("15 beden ölçüsünü gir…") { if measurementForm?.hasValues == true { offerMeasurementForm = true } else { showingBodyMeasurements = true } }.frame(minHeight: 44)
                    if style == .aline && modelExplicitlySelected {
                        Button("A kesim için 11 ölçüyü gir…") { openALineGuide() }.frame(minHeight: 44)
                        Text("Ön/arka beden yayları, ayrı bolluklar ve etek ölçülerini elle girin; değerler türetilmez.").font(.caption)
                    }
                    if aLineInputPending {
                        Text("A kesim rehberinde notlara eklenmemiş girişler var. Bunlar onaylanana kadar yerel taslağa kaydedilir; ayrı kurtarma kopyasına da dahil edilir. Kayıt durumunu kontrol edin.").font(.caption)
                        Button("Bekleyen A kesim girişlerini temizle…") { discardALineInputSnapshot = aLineGuideInput; confirmDiscardALineInput = true }.frame(minHeight: 44)
                    }
                    Text("Bilmediğiniz alanları boş bırakın. Değerle birlikte cm veya mm yazın; modele göre daha fazla ölçü istenebilir.").font(.caption)
                    TextField("Göğüs çevresi · ör. 92 cm", text: $bust).accessibilityLabel("Göğüs çevresi, birimiyle").focused($focusedInput, equals: .bust)
                    TextField("Bel çevresi · ör. 72 cm", text: $waist).accessibilityLabel("Bel çevresi, birimiyle").focused($focusedInput, equals: .waist)
                    TextField("Kalça çevresi · ör. 98 cm", text: $hips).accessibilityLabel("Kalça çevresi, birimiyle").focused($focusedInput, equals: .hips)
                    TextField("Elbise boyu · ör. 110 cm", text: $length).accessibilityLabel("Elbise boyu, birimiyle").focused($focusedInput, equals: .length)

                }
                }
                Section {
                    HStack {
                        Text("ELBİSE İSTEĞİ").font(.caption.weight(.semibold)).foregroundStyle(.secondary)
                        Spacer()
                        Text(step == 1 ? "Ölçü düzenleme · 1 / 3" : "\(step < 2 ? 1 : step) / 3").font(.caption.weight(.semibold)).accessibilityLabel(step == 1 ? "Ölçü düzenleme, birinci adım, toplam üç adım" : "Adım \(step < 2 ? 1 : step), toplam 3")
                    }
                    ProgressView(value: Double(step < 2 ? 1 : step), total: 3).accessibilityHidden(true)
                    Text(localStatus).font(.caption).foregroundStyle(.secondary)
                    if pendingLedger != "[]" || legacyUncertain || markerError {
                        Text("Bir kayıt sürüyor veya önceki kayıt tamamlanmamış olabilir. Son değişiklikleri ve kurtarma kopyasını kontrol edin.").font(.caption)
                        Button("Eski uyarıları onayla") {
                            if let visible = DraftPendingLedger.read() { DraftPendingLedger.acknowledge(visible); markerError = false }
                            legacyUncertain = false
                        }
                    }
                    if DraftPendingLedger.read() == nil {
                        Text("Kayıt uyarısı verisi okunamıyor. Taslak dosyası silinmez; ham uyarı verisi bu cihazda yedeklenir. Yedek, bozuk verinin içerdiği beklenmeyen bilgileri de taşıyabilir.").font(.caption)
                        Button("Bozuk uyarı kaydını onar…") {
                            if let snapshot = DraftPendingLedger.corruptSnapshot() { corruptLedgerSnapshot = snapshot; confirmLedgerReset = true }
                            else { localStatus = "Onarım yapılamıyor: başka kayıt etkin veya veri yedeklenemiyor. Taslağı ayrı kurtarma kopyasına kaydedin." }
                        }
                    }
                    DisclosureGroup("İstek ve kurtarma dosyaları") {
                    Button("Bu taslağın ayrı kurtarma kopyasını kaydet…") { prepareRecoveryCopy() }.disabled(referenceDecisionPending)
                    Button("Kurtarma kopyası aç…") { showingDraftCopyImport = true }.disabled(referenceDecisionPending || aLineInputPending)
                    Button("İstek dosyası aç…") { showingPortableRequestImport = true }.disabled(referenceDecisionPending || aLineInputPending)
                    Text("İstek metni Unicode olarak korunur. PNG/JPEG görselinin iPhone tarafından okunabilir olması gerekir; uyumsuz dosya mevcut taslağı değiştirmez.").font(.caption)
                    Text("Kopya ölçüleri ve varsa görselin tümünü içerir. Alan başına 1.000.000 UTF-16 birimi, toplam 32 MiB ve görsel için 8 MiB sınırı vardır. Güvenli konum ve yeni dosya adı seçin. Yerel önceki taslak değiştirilmez.").font(.caption)
                    }
                    if draftRetryAvailable { Button("Taslağı yeniden dene") {
                        if draftStorageReady { saveLocalDraft(draft) }
                        else if !draft.hasInput { storageChecked = false; draftRetryAvailable = false; checkLocalDraft() }
                        else { localStatus = "Ekrandaki içeriği önce ayrı kurtarma kopyasına kaydedin; önceki taslağı görmek için pencereyi yeniden açın." }
                    } }
                }
                if step == 2 {
                Section("Örnek görsel · isteğe bağlı") {
                    Button("Dosyalardan görsel seç") { selectingImage = true }.disabled(referenceLoading)
                    if referenceLoading {
                        ProgressView("Görsel yükleniyor…")
                        Button("Görsel yüklemeyi iptal et") { cancelReferenceLoad(); message = "Yükleme iptal edildi; mevcut görsel korundu." }
                    }
                    if let imageName {
                        Text("Seçilen: \(ModelStore.safeLabel(imageName))")
                        Button("Fotoğrafa bakarak özellikleri belirt…") { openPhotoObservation() }
                            .disabled(draftTransition || referenceBlocksClose)
                        Button("Görseli kaldır…") {
                            pendingReferenceChange = .init(original: currentReference, replacement: nil); confirmReferenceChange = true
                        }.disabled(referenceLoading)
                    }
                    Text("Seçilen görsel özgün baytlarıyla, içerebileceği konum ve diğer bilgiler dahil dosyaya eklenir. PNG veya JPEG, en fazla 8 MiB.").font(.caption)
                }
                }
                if step == 3 {
                Section("İsteğinizi gözden geçirin") {
                    let summary = ExperimentalDesignBrief.RequestSummary(draft)
                    Text("Model ve açıklamayı birlikte kontrol edin. Serbest metindeki her olası çelişki otomatik belirlenemez.").font(.caption).foregroundStyle(.secondary)
                    Text("Model tercihi").font(.headline).accessibilityAddTraits(.isHeader)
                    Text(summary.model ?? "Ayrı model seçilmedi; açıklamayı kontrol edin.")
                    Text("Tarif").font(.headline).accessibilityAddTraits(.isHeader)
                    if summary.descriptionIsWhitespace {
                        Text("Tarif yalnız boşluk içeriyor; ham metin korunuyor.").foregroundStyle(.secondary)
                    } else {
                        Text(summary.description.isEmpty ? "Tarif yazılmadı." : summary.description).textSelection(.enabled)
                    }
                    Button("Modeli veya tarifi düzenle") { step = 0 }.frame(minHeight: 44)
                    DisclosureGroup("V1 dosyası · açıklama") {
                        Text("V1'de model seçimi varsa açıklamanın başına model satırı eklenir. Kayıt için diğer kontroller de geçmelidir.").font(.caption)
                        if summary.v1Prompt.isEmpty { Text("Açıklama boş.") }
                        else {
                            if ExperimentalDesignBrief.RequestSummary.isOnlyWhitespace(summary.v1Prompt) { Text("Açıklama yalnız boşluk içeriyor; ham metin korunur.").font(.caption) }
                            Text(verbatim: summary.v1Prompt).textSelection(.enabled)
                        }
                    }
                    DisclosureGroup("V2 dosyası · ham tarif ve model") {
                        Text("V2 tarifi olduğu gibi yazar; model seçimi açıklamaya eklenmez, ayrı kaydedilir. Kayıt için diğer kontroller de geçmelidir.").font(.caption)
                        if let id = summary.v2ModelID, let model = summary.model { Text(verbatim: "Ayrı model: \(model) (\(id))") }
                        else { Text("Ayrı model seçilmedi.") }
                        if summary.v2Prompt.isEmpty { Text("Ham tarif boş; dosyaya boş metin yazılır.") }
                        else {
                            if ExperimentalDesignBrief.RequestSummary.isOnlyWhitespace(summary.v2Prompt) { Text("Ham tarif yalnız boşluk içeriyor; boşluklar aynen yazılır.").font(.caption) }
                            Text(verbatim: summary.v2Prompt).textSelection(.enabled)
                        }
                    }
                    Text("Ölçüler ve girilen birimler").font(.headline).accessibilityAddTraits(.isHeader)
                    if !otherMeasurements.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
                        Text("Alanlar ile ölçü mesajı veya ek notlar arasındaki çelişkiler otomatik olarak doğrulanmadı. Göndermeden önce tüm ölçüleri karşılaştırın.")
                            .font(.caption).foregroundStyle(plum)
                    }
                    if desktopMeasurementRisk {
                        Text("Tarifteki ölçüler ile soru, olumsuzluk veya koşul içeren, sayı gösterimi belirsiz ya da cümleyle birleştirilmiş ölçüleri kontrol edin. Özgün metin otomatik değiştirilmez; masaüstündeki yorumu ayrıca doğrulayın.").font(.caption).foregroundStyle(.secondary)
                    }
                    if !summary.whitespaceMeasurementLabels.isEmpty {
                        Text("Yalnız boşluk içeren alanlar: \(summary.whitespaceMeasurementLabels.joined(separator: ", ")). Ölçüleri düzenleyin; ham metin korunuyor.").foregroundStyle(.secondary)
                    }
                    if summary.measurements.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
                        Text("Ölçü alanı veya notu girilmedi; tarifteki ölçüler bu özete otomatik aktarılmaz.")
                    } else { Text(summary.measurements).textSelection(.enabled) }
                    if ExperimentalDesignBrief.descriptionMeasurements(draft) != nil {
                        Text("Tarifte incelenebilecek açık ölçüler bulundu; alanlara eklenmeleri için onay gerekir.").font(.caption)
                        Button("Tarifteki ölçüleri incele") { proposeDescriptionMeasurements() }.frame(minHeight: 44)
                    }
                    if summary.hasPendingMeasurements {
                        Text("Ölçü formunda henüz onaylanmamış giriş var; bu girişler yukarıdaki ölçülere dahil değil.").foregroundStyle(.secondary)
                    }
                    Button("Tarif ve ölçüleri gözden geçir") { step = measurementReviewStartStep }.frame(minHeight: 44)
                    Text("Referans görseli").font(.headline).accessibilityAddTraits(.isHeader)
                    if let name = summary.referenceName { Text("Eklendi: \(ModelStore.safeLabel(name))") }
                    else { Text("Görsel eklenmedi.").foregroundStyle(.secondary) }
                    Button("Görseli düzenle") { step = 2 }.frame(minHeight: 44)
                }
                Text("Aktarılacak içerik: tarif, ölçüler ve varsa seçilen görsel. Dosyayı yalnız güvendiğiniz kişilerle paylaşın ve kayıt için yeni bir dosya adı seçin.").font(.caption)
                Text("Bu istek dosyası kalıp üretmez, baskıya göndermez veya otomatik gönderilmez.").font(.caption).foregroundStyle(.secondary)
                }
                if !message.isEmpty { Text(message).foregroundStyle(.secondary) }
            }
            .disabled(draftTransition)
            .scrollContentBackground(.hidden)
            .background(ivory)
            .tint(plum)
            .toolbar { ToolbarItemGroup(placement: .keyboard) { Spacer(); Button("Klavyeyi kapat") { focusedInput = nil } } }
            .safeAreaInset(edge: .top) {
                if draftNeedsAttention {
                    VStack(alignment: .leading, spacing: 4) {
                        Label("Taslak kaydını kontrol edin", systemImage: "exclamationmark.triangle.fill")
                            .font(.subheadline.weight(.semibold))
                        Text(localStatus).font(.caption)
                        if pendingLedger != "[]" || legacyUncertain || markerError {
                            Text("Bir kayıt sürüyor veya önceki kayıt tamamlanmamış olabilir.").font(.caption)
                        }
                        Button("Kurtarma kopyası kaydet…") { prepareRecoveryCopy() }
                            .frame(minHeight: 44, alignment: .leading)
                            .disabled(referenceDecisionPending)
                    }
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .padding(.horizontal).padding(.vertical, 8)
                    .foregroundStyle(plum)
                    .background(ivory)
                    .accessibilityElement(children: .contain)
                }
            }
            .safeAreaInset(edge: .bottom) {
                HStack(spacing: 12) {
                    if step > 0 {
                        Button("Geri") { step = step == 2 ? 0 : step - 1; message = "" }
                            .buttonStyle(.bordered).controlSize(.large)
                    }
                    if step < 3 {
                        Button(step < 2 ? "Görsele devam" : "İsteği gözden geçir") { step = step < 2 ? 2 : 3; message = ""; focusedInput = nil }
                            .buttonStyle(.borderedProminent).controlSize(.large)
                            .frame(maxWidth: .infinity, minHeight: 48)
                    } else {
                        Button("İstek dosyasını kaydet…") { choosingRequestVersion = true }
                            .buttonStyle(.borderedProminent).controlSize(.large)
                            .frame(maxWidth: .infinity, alignment: .trailing)
                    }
                }
                .disabled(draftTransition || referenceDecisionPending)
                .padding(.horizontal).padding(.vertical, 10)
                .background(ivory)
            }
            .fileExporter(isPresented: $showingDraftCopyExport, document: recoveryCopy, contentType: .json, defaultFilename: "elbise-taslak-kurtarma.json") { result in
                switch result {
                case .success: localStatus = "Kurtarma kopyası kaydedildi. Cihazdaki önceki taslak değiştirilmedi."
                case .failure(let error):
                    if !isFilePickerCancellation(error) { localStatus = "Kurtarma kopyası kaydedilemedi; ekrandaki bilgiler korundu." }
                }
            }
            .sheet(isPresented: $showingBodyMeasurements) { BodyMeasurementNotesView(notes: $otherMeasurements, savedForm: $measurementForm, measurementsWithNotes: { ExperimentalDesignBrief.measurements(bust: bust, waist: waist, hips: hips, length: length, other: $0) }, localSaveStatus: { localStatus }) }
            .sheet(item: $aLineGuideRequest) { request in
                ALineMeasurementGuideView(request: request, input: $aLineGuideInput) { request, input, notes in applyALineGuide(request, input: input, notes: notes) }
            }
            .sheet(item: $photoObservationRequest) { request in
                PhotoObservationView(request: request, apply: { request, selection in applyPhotoObservation(request, selection: selection) },
                                     edit: { request, plan in editPhotoObservation(request, plan: plan) })
            }
            .fileImporter(isPresented: $showingPortableRequestImport, allowedContentTypes: [.json]) { result in
                do { copyCandidate = try ExperimentalDesignBrief.readPortable(result.get()); candidateIsPortable = true; confirmCopyRestore = true }
                catch { if !isFilePickerCancellation(error) { localStatus = "İstek okunamadı veya desteklenmeyen görsel içeriyor; mevcut girişler ve yerel taslak korundu." } }
            }
            .fileImporter(isPresented: $showingDraftCopyImport, allowedContentTypes: [.json]) { result in
                do { copyCandidate = try ExperimentalDesignBrief.DraftStore.readRecovery(result.get()); candidateIsPortable = false; confirmCopyRestore = true }
                catch { if !isFilePickerCancellation(error) { localStatus = "Kurtarma kopyası okunamadı; ekrandaki bilgiler ve önceki yerel kayıt korundu." } }
            }
            .confirmationDialog("İstek veya kurtarma kopyası açılsın mı?", isPresented: $confirmCopyRestore, titleVisibility: .visible) {
                Button("Kopyayı aç", role: .destructive) {
                    guard !aLineInputPending else { localStatus = "A kesim rehberindeki girişler bekliyor; önce tamamlayın veya açıkça temizleyin."; return }
                    if let candidate = copyCandidate {
                        cancelReferenceLoad()
                        draftTransition = true; saveRequest = UUID(); draftStorageReady = false
                        let stoppedMarkers = ownedPending
                        let isPortable = candidateIsPortable
                        draftWriter.pause {
                        draftTransition = false; backgroundLease.finish(); DraftPendingLedger.abandon(stoppedMarkers)
                        importedPortable = isPortable && candidate.style == .describe; modelExplicitlySelected = candidate.style != .describe
                        style = candidate.style; description = candidate.description; bust = candidate.bust; waist = candidate.waist
                        hips = candidate.hips; length = candidate.length; otherMeasurements = candidate.otherMeasurements
                        image = candidate.image; imageName = candidate.imageName; measurementForm = candidate.measurementForm
                        aLineGuideInput = candidate.aLineGuideInput ?? .init()
                        draftRetryAvailable = false
                        localStatus = "Kopya açıldı. Önceki yazılar tamamlandı; otomatik kayıt kapalı. Değişiklikleri ayrı kopyaya kaydedin."
                        }
                    }
                    copyCandidate = nil
                }
                Button("Vazgeç", role: .cancel) { copyCandidate = nil }
            } message: { Text("Ekrandaki mevcut girişler değiştirilecek. Önce onları ayrı kurtarma kopyasına kaydedin; yerel önceki taslak değiştirilmez.") }
            .confirmationDialog("Bozuk uyarı kaydı onarılsın mı?", isPresented: $confirmLedgerReset, titleVisibility: .visible) {
                Button("Yedekle ve onar") {
                    if let snapshot = corruptLedgerSnapshot, DraftPendingLedger.resetCorrupt(expected: snapshot) {
                        markerError = false
                        localStatus = "Ham uyarı kaydı cihazda yedeklendi ve sıfırlandı. Taslak dosyası değiştirilmedi; son içeriği kontrol edin."
                    } else { localStatus = "Uyarı kaydı değişti veya başka kayıt etkin; onarım yapılmadı. Taslak dosyası korundu." }
                    corruptLedgerSnapshot = nil
                }
                Button("Vazgeç", role: .cancel) { corruptLedgerSnapshot = nil }
            } message: { Text("Bu yalnız kayıt uyarısı verisini sıfırlar; mevcut taslağı silmez veya bir kaydın başarılı olduğunu kanıtlamaz. Yedek cihazda tutulur.") }
            .confirmationDialog("Açıklamada model satırı var", isPresented: $conflictingModelNotice, titleVisibility: .visible) {
                Button("Tarif olarak koru") { style = .describe; modelExplicitlySelected = true }
                Button("Açıklamayı gözden geçir", role: .cancel) { }
            } message: { Text("Dosyadaki önceki model açıklaması silinmez. Yeni bir model seçmek için önce açıklamadaki model satırını kendiniz düzenleyin; aksi halde tarif olarak koruyun.") }
            .confirmationDialog("Model önerisini inceleyin", isPresented: $showModelSuggestion, titleVisibility: .visible) {
                Button("Önerilen modeli seç") { applyModelSuggestion() }
                Button("Vazgeç", role: .cancel) { modelSuggestion = nil }
            } message: {
                Text("Öneri: \(modelSuggestion?.suggested.rawValue ?? ""). Mevcut model seçiminiz değişebilir. Özgün metin ve görsel korunur; bu seçim kalıp oluşturmaz.")
            }
            .confirmationDialog("Kaydedilmiş ölçü formu var", isPresented: $offerMeasurementForm, titleVisibility: .visible) {
                Button("Formu geri yükle") { showingBodyMeasurements = true }
                Button("Şimdilik açma", role: .cancel) { }
            } message: { Text("Önceki girilen değerler ve birim notlara henüz eklenmedi. Açıp inceleyebilir veya daha sonra devam edebilirsiniz.") }
            .confirmationDialog("Bekleyen A kesim girişleri temizlensin mi?", isPresented: $confirmDiscardALineInput, titleVisibility: .visible) {
                Button("Girişleri temizle", role: .destructive) {
                    guard let snapshot = discardALineInputSnapshot, snapshot == aLineGuideInput else {
                        discardALineInputSnapshot = nil; message = "Girişler değişti; hiçbir değer temizlenmedi."; return
                    }
                    aLineGuideInput = .init(); discardALineInputSnapshot = nil
                }
                Button("Koru", role: .cancel) { discardALineInputSnapshot = nil }
            } message: { Text("Yalnız rehbere girilen ve henüz notlara eklenmemiş değerler temizlenir; mevcut tarif ve ölçüler değişmez.") }
            .confirmationDialog("Seçili görsel değiştirilsin mi?", isPresented: $confirmReferenceChange, titleVisibility: .visible) {
                Button(pendingReferenceChange?.replacement == nil ? "Görseli kaldır" : "Görseli değiştir", role: .destructive) { applyReferenceChange() }
                Button("Mevcut görseli koru", role: .cancel) { pendingReferenceChange = nil }
            } message: { Text("Geçerli istekteki önceki görsel değişir. Vazgeçerseniz mevcut görsel korunur; kaynak dosyalar değiştirilmez.") }
            .confirmationDialog("Bu ölçüler alanlara eklensin mi?", isPresented: $showMeasurementProposal, titleVisibility: .visible) {
                Button("Ölçüleri alanlara ekle") { applyMeasurementProposal() }
                Button("Vazgeç", role: .cancel) { measurementProposal = nil; messageMeasurementSnapshot = nil }
            } message: {
                Text(measurementProposal?.preview ?? "Ölçü bulunamadı.")
                Text("Özgün mesaj korunur. Aynı bilgiler alanlarda ve mesajda görünebilir; eklenmeyen ölçüleri kontrol edin.")
            }
            .confirmationDialog("Tarifteki ölçüler alanlara eklensin mi?", isPresented: $showDescriptionMeasurements, titleVisibility: .visible) {
                Button("Ölçüleri alanlara ekle") { applyDescriptionMeasurements() }
                Button("Vazgeç", role: .cancel) { descriptionMeasurementSuggestion = nil }
            } message: {
                Text(descriptionMeasurementSuggestion?.proposal.preview ?? "Ölçü bulunamadı.")
                Text("Özgün tarif korunur. Seçili modeli, ölçüleri ve birimleri kontrol edin; bu işlem kalıp oluşturmaz.")
            }
            .confirmationDialog("Dosya hedefini dikkatle seçin", isPresented: $confirmingExportDestination, titleVisibility: .visible) {
                Button("Anladım, dosya konumunu seç") {
                    guard let attempt = pendingExportAttempt, attempt.matchesCurrentDraft(draft), !referenceDecisionPending else {
                        pendingExportAttempt = nil
                        message = "Taslak değişti; içeriği kontrol edip kaydetmeyi yeniden başlatın."
                        return
                    }
                    pendingExportAttempt = nil
                    exportAttempt = attempt
                    exportPresentation.begin(attempt.id)
                }
                Button("İptal", role: .cancel) { pendingExportAttempt = nil }
            } message: {
                Text("Yeni ve kullanılmamış bir dosya adı seçin. Aynı dosyanın farklı bir yolu veya sonradan oluşturulan bir hedef uygulama tarafından kesin olarak tespit edilmez; mevcut dosyanın üzerine yazılmasını engelleyemiyoruz. Emin değilseniz iptal edin.")
            }
            .confirmationDialog("İstek dosyası biçimi", isPresented: $choosingRequestVersion, titleVisibility: .visible) {
                Button("V1: Windows ile uyumlu kopya") { requestExportVersion = 1; prepareExport() }
                Button("V2: model kimliğini koru (deneysel)") { requestExportVersion = 2; prepareExport() }
                Button("Vazgeç", role: .cancel) { }
            } message: {
                Text("V1 model kimliğini ayrı saklamaz; model satırı açıklamaya eklenir. V2 ayrı kimlik saklar; bu güncellemeden önceki Windows ve iPhone sürümleri tarifi veya görseli olmayan model isteklerini reddeder. Windows ve iPhone arasında gerçek dosya alışverişi henüz doğrulanmadı. Model ile tarif arasındaki tüm çelişkiler otomatik saptanmaz; ikisini kontrol edin. Mevcut dosyanın üzerine yazılması uygulama tarafından engellenmez.")
            }
            .confirmationDialog("Masaüstünde ölçüleri kontrol edin", isPresented: $showDesktopMeasurementWarning, titleVisibility: .visible) {
                Button("Olduğu gibi kaydet") {
                    let snapshot = desktopWarningSnapshot
                    desktopWarningSnapshot = nil
                    guard let snapshot, snapshot.matches(draft, version: requestExportVersion) else {
                        message = "Taslak veya dosya biçimi değişti; ölçüleri tekrar inceleyip kaydetmeyi yeniden başlatın."
                        return
                    }
                    prepareExport(confirmedDesktopWarning: true)
                }
                Button("Tarif ve ölçüleri gözden geçir") { desktopWarningSnapshot = nil; step = measurementReviewStartStep }
                Button("Vazgeç", role: .cancel) { desktopWarningSnapshot = nil }
            } message: { Text("Tarifteki ölçüler ile soru, olumsuzluk veya koşul içeren, sayı gösterimi belirsiz ya da cümleyle birleştirilmiş ölçüler masaüstünde farklı değerlendirilebilir. Anlamı ve birimi açıkça kontrol edin; metniniz değiştirilmez. Masaüstündeki sonucu da doğrulayın.") }
            .onAppear { checkLocalDraft() }
            .onChange(of: confirmReferenceChange) { shown in if !shown { pendingReferenceChange = nil } }
            .onChange(of: draft) { value in if !draftTransition { saveLocalDraft(value) } }
            .onChange(of: aLineSourceDraft) { _ in modelSuggestionRevision = UUID() }
            .onChange(of: modelExplicitlySelected) { _ in modelSuggestionRevision = UUID() }
            .onChange(of: importedPortable) { _ in modelSuggestionRevision = UUID() }
            .onChange(of: scenePhase) { phase in if phase != .active && !draftTransition { saveLocalDraft(draft, immediately: true) } }
            .confirmationDialog("Önceki taslak bulundu", isPresented: $offerRestore, titleVisibility: .visible) {
                Button("Taslağı geri yükle") { if let previous = restoreCandidate { restore(previous) } }
                Button("Şimdi geri yükleme", role: .cancel) {
                    localStatus = "Önceki taslak korundu; otomatik kayıt kapalı. Bu isteği dosyaya kaydedin."
                }
            } message: { Text("Bu cihazda saklanan tarif, ölçüler ve varsa görsel geri yüklenecek. Şimdi yüklemezseniz önceki kayıt korunur.") }
            .navigationTitle(step == 1 ? "Ölçü düzenleme" : "Kalıp isteği")
            .interactiveDismissDisabled(true)
            .toolbar { ToolbarItem(placement: .cancellationAction) {
                Button("Kapat") { closeAfterSaving() }.disabled(draftTransition || referenceBlocksClose)
            } }
            .confirmationDialog("İstek kapatılsın mı?", isPresented: $confirmClose, titleVisibility: .visible) {
                Button("Kaydetmeden kapat", role: .destructive) {
                    guard !aLineInputPending else {
                        message = "A kesim rehberindeki girişler bekliyor; önce tamamlayın veya açıkça temizleyin."; step = 1
                        return
                    }
                    guard !referenceBlocksClose else {
                        message = "Görsel seçimini tamamlayın veya yüklemeyi açıkça iptal edin; ardından kapatabilirsiniz."
                        return
                    }
                    dismiss()
                }
                Button("Düzenlemeye devam et", role: .cancel) { }
            } message: { Text("Ekrandaki kaydedilmemiş değişiklikler kaybolabilir. Varsa cihazdaki önceki taslak korunur.") }
            .fileImporter(isPresented: $selectingImage, allowedContentTypes: [.image]) { result in
                do { startReferenceLoad(try result.get()) }
                catch { if !isFilePickerCancellation(error) { message = "Görsel seçilemedi; mevcut görsel korundu." } }
            }
            .background {
                if let attempt = exportAttempt {
                    DesignBriefExportBridge(attempt: attempt, isPresented: Binding(
                        get: { exportPresentation.isPresented(for: attempt.id) },
                        set: { exportPresentation.setPresented($0, for: attempt.id) })) { id, result in
                        guard let current = exportAttempt, current.matches(id) else { return }
                        exportAttempt = nil
                        switch result {
                        case .success:
                            savedDraft = current.draft
                            message = current.draft == draft
                                ? "İstek dosyası kaydedildi; masaüstünde ayrıca incelenmesi gerekir."
                                : "Önceki içerik kaydedildi; sonraki değişiklikler henüz kaydedilmedi."
                        case .failure(let error):
                            if !isFilePickerCancellation(error) { message = "İstek dosyası kaydedilemedi; içerik burada korunuyor." }
                        }
                    }
                    .id(attempt.id)
                }
            }
        }
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
    let onSessionClosed: () -> Void
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
    @State private var showPreviewGrain = true

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
                    DrawingView(pieces: [preview], label: "Düzenlenmiş kontur önizlemesi", highlightedNotch: nil, selectionRevision: nil, showGrain: $showPreviewGrain)
                        .id(generation)
                        .frame(height: 240)
                    Button("Yeni kopyayı kaydet") { saveCopy() }
                    .disabled(candidate == nil || working || saving)
                }
            }
            .navigationTitle("Deneysel nokta")
            .toolbar { ToolbarItem(placement: .cancellationAction) { Button("Kapat") { dismiss() } } }
            .onAppear { updatePoint() }
            .onDisappear { generation = UUID(); saveGeneration = UUID(); saveActiveID = nil; saving = false; cancellingSave = false; onSessionClosed() }
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
                Text("Koordinatlar yaklaşık gösterilir; kesin değerler kaynak dosyada korunur.")
                    .font(.caption).foregroundStyle(.secondary)
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
                                Button(NotchSearch.label(index: pointIndex, x: NotchSearch.coordinate(point.x), y: NotchSearch.coordinate(point.y))) {
                                    select(pieceIndex, pointIndex)
                                }
                                .accessibilityValue("\(piece.displayName(index: pieceIndex)) · \(piece.selectionDetails(index: pieceIndex))")
                                .accessibilityHint(piece.hasMinimumContourPoints
                                    ? "İşareti seç ve parçanın konturunda göster."
                                    : "İşareti seç. Parçada yeterli nokta olmadığından kontur gösterilemez.")
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
                    VStack(alignment: .leading, spacing: 8) {
                        Text("Eşleşen köşe işareti bulunamadı.").foregroundStyle(.secondary)
                        if !search.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
                            Button("Aramayı temizle") { search = "" }
                        }
                    }
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
                    var names: [String] = [], details: [String] = []
                    if !query.isEmpty {
                        names.reserveCapacity(snapshot.count); details.reserveCapacity(snapshot.count)
                        for index in snapshot.indices {
                            guard !Task.isCancelled else { return nil }
                            names.append(snapshot[index].displayName(index: index))
                            details.append(snapshot[index].selectionDetails(index: index) + " " + snapshot[index].rawSearchText)
                        }
                    }
                    guard let found = NotchSearch.boundedMatchesCancellable(groups: snapshot.map(\.notches), limit: 500, cancelled: { Task.isCancelled }, matches: { pieceIndex, pointIndex in
                        if query.isEmpty { return true }
                        guard query.allows(index: pointIndex) else { return false }
                        let point = snapshot[pieceIndex].points[pointIndex]
                        return query.matches(pieceName: names[pieceIndex], details: details[pieceIndex], index: pointIndex,
                                             x: NotchSearch.coordinate(point.x), y: NotchSearch.coordinate(point.y))
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
                if let current {
                    Text("\(current.indices.count) / \(pieces.count) parça gösteriliyor")
                        .font(.caption).foregroundStyle(.secondary)
                        .accessibilityLabel("\(pieces.count) parçadan \(current.indices.count) tanesi gösteriliyor")
                }
                if current == nil { ProgressView("Parçalar aranıyor…") }
                ForEach(matches, id: \.self) { index in
                    selectionRow(title: pieces[index].displayName(index: index), index: index,
                                 details: pieces[index].selectionDetails(index: index))
                }
                if current != nil && matches.isEmpty && !search.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
                    VStack(alignment: .leading, spacing: 8) {
                        Text("Eşleşen parça bulunamadı.").foregroundStyle(.secondary)
                        Button("Aramayı temizle") { search = "" }
                    }
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
                        (snapshot[index].displayName(index: index), snapshot[index].selectionDetails(index: index) + " " + snapshot[index].rawSearchText)
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
        guard let bounds = piece.contourBounds else { return nil }
        return (bounds.maxX - bounds.minX, bounds.maxY - bounds.minY)
    }

    var body: some View {
        if let dimensions {
            VStack(alignment: .leading, spacing: 3) {
                Text("Kontur bilgisi").font(.subheadline.bold())
                Text("\(piece.points.count) nokta • \(piece.closed ? "Kapalı" : "Açık") kontur")
                if let role = piece.role, !role.isEmpty { Text("Rol: \(ModelStore.safeLabel(role))") }
                if piece.roleUnreadable { Text("Parça türü okunamadı veya geçersiz; kaynak dosyayı kontrol edin.") }
                if let size = piece.size, !size.isEmpty { Text("Beden: \(ModelStore.safeLabel(size))") }
                if let material = piece.material, !material.isEmpty { Text("Malzeme: \(ModelStore.safeLabel(material))") }
                if piece.textMetadataUnreadable { Text("Beden veya malzeme okunamadı veya geçersiz; kaynak dosyayı kontrol edin.") }
                if let quantity = piece.quantity { Text("Adet: \(quantity)") }
                if piece.quantityUnreadable { Text("Adet okunamadı veya geçersiz; kaynak dosyayı kontrol edin.") }
                if piece.metadataUnreadable { Text("Üretim bilgileri okunamadı; kaynak dosyayı kontrol edin.") }
                if piece.metadataEmpty { Text("Üretim bilgileri boş; dosya masaüstünde açılamayabilir. Kaynak dosyayı kontrol edin.") }
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
// Plain text only: this is not a project file or a production approval.
enum PieceComparisonReport {
    static func make(pieces: [Drawing.Piece], projectName: String, first: Int, second: Int,
                     firstSpan: ContourMeasure.SpanSelection? = nil, secondSpan: ContourMeasure.SpanSelection? = nil,
                     locale: Locale = .current) -> String? {
        guard pieces.indices.contains(first), pieces.indices.contains(second) else { return nil }
        func format(_ number: Double?) -> String {
            guard let number, let value = MeasurementDisplay.format(number, locale: locale) else { return "hesaplanamadı" }
            return "\(value) mm"
        }
        func width(_ piece: Drawing.Piece) -> Double? { piece.contourBounds.map { $0.maxX - $0.minX } }
        func height(_ piece: Drawing.Piece) -> Double? { piece.contourBounds.map { $0.maxY - $0.minY } }
        func pieceLines(_ index: Int, _ title: String) -> [String] {
            let piece = pieces[index]
            let rawName = piece.name.flatMap { $0.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty ? nil : $0 } ?? "Parça \(index + 1)"
            let prefix = rawName.prefix(256)
            let name = ModelStore.safeLabel(String(prefix)) + (prefix.endIndex == rawName.endIndex ? "" : "…")
            return ["\(title): \(name)", "Dosya sırası: \(index + 1) / \(pieces.count)",
                    "Parça kimliği: \(piece.id?.uuidString ?? "yok")", "Kontur: \(piece.closed ? "kapalı" : "açık")",
                    "Yaklaşık en: \(format(width(piece)))", "Yaklaşık boy: \(format(height(piece)))",
                    "Yaklaşık toplam kontur uzunluğu: \(format(piece.contourLength))"]
        }
        let a = pieces[first], b = pieces[second]
        let projectPrefix = projectName.prefix(256)
        let projectLabel = ModelStore.safeLabel(String(projectPrefix)) + (projectPrefix.endIndex == projectName.endIndex ? "" : "…")
        var lines = ["PARÇA KARŞILAŞTIRMASI · YALNIZCA BİLGİ", "Proje: \(projectLabel)", ""] + pieceLines(first, "Birinci") + [""] + pieceLines(second, "İkinci") + ["",
            first == second ? "Uyarı: Aynı parça iki kez seçildi." : "İki farklı dosya sırası karşılaştırıldı.",
            "Yaklaşık farklar (ikinci − birinci):",
            "En: \(format(ContourMeasure.difference(first: width(a), second: width(b))))",
            "Boy: \(format(ContourMeasure.difference(first: height(a), second: height(b))))",
            "Toplam kontur uzunluğu: \(format(ContourMeasure.difference(first: a.contourLength, second: b.contourLength)))", "",
            "Değerler dosyadaki düz çizgilerden hesaplanır ve en fazla 6 anlamlı basamağa yuvarlanır. Açık ve kapalı konturların toplamları farklı anlam taşır. Bu metin proje dosyası, dikiş uyumu veya üretime uygunluk doğrulaması değildir."]
        if firstSpan != nil || secondSpan != nil {
            func describe(_ span: ContourMeasure.SpanSelection?, piece: Drawing.Piece, index: Int, title: String) -> (String, Double?) {
                guard let span, let start = Int(span.start), let end = Int(span.end),
                      let length = span.length(points: piece.points, closed: piece.closed, pieceIndex: index) else {
                    return ("\(title): seçilmedi veya hesaplanamadı", nil)
                }
                let direction = piece.closed ? (span.forward ? "ileri" : "geri") : "açık kontur"
                return ("\(title): \(start) → \(end) · \(direction) · yaklaşık \(format(length))", length)
            }
            let left = describe(firstSpan, piece: a, index: first, title: "Birinci aralık")
            let right = describe(secondSpan, piece: b, index: second, title: "İkinci aralık")
            lines.insert(contentsOf: ["Seçili kontur aralıkları:", left.0, right.0,
                                     "Aralık farkı (ikinci − birinci): \(format(ContourMeasure.difference(first: left.1, second: right.1)))", ""], at: lines.count - 1)
        }
        return lines.joined(separator: "\n")
    }
}
// A read-only comparison of the currently loaded snapshot, not a production check.
private struct PieceComparisonView: View {
    let pieces: [Drawing.Piece]
    let projectName: String
    @Environment(\.dismiss) private var dismiss
    @State private var first: Int?
    @State private var second: Int?
    @State private var choosingFirst = false
    @State private var choosingSecond = false
    @State private var showingOverlay = false
    @State private var firstSpan = ContourMeasure.SpanSelection()
    @State private var secondSpan = ContourMeasure.SpanSelection()
    private struct ReportPreview: Identifiable { let id = UUID(); let text: String }
    @State private var reportPreview: ReportPreview?

    init(pieces: [Drawing.Piece], projectName: String, initialIndex: Int?) {
        self.pieces = pieces
        self.projectName = projectName
        let selected = initialIndex.flatMap { pieces.indices.contains($0) ? $0 : nil } ?? 0
        _first = State(initialValue: pieces.indices.contains(selected) ? selected : nil)
        _second = State(initialValue: pieces.count > 1 ? (selected == 0 ? 1 : 0) : nil)
    }

    private func difference(_ a: Double?, _ b: Double?) -> String {
        guard let value = ContourMeasure.difference(first: a, second: b), let text = MeasurementDisplay.format(value) else { return "Hesaplanamadı" }
        return "\(text) mm"
    }

    var body: some View {
        NavigationStack {
            List {
                Section("Birinci parça") {
                    Button(first.map { "\($0 + 1). \(pieces[$0].displayName(index: $0))" } ?? "Parça seç") { choosingFirst = true }
                    if let first {
                        Text("Dosya sırası: \(first + 1) / \(pieces.count)")
                        if let id = pieces[first].id { Text("Parça kimliği: \(id.uuidString)").textSelection(.enabled) }
                        PieceDetails(piece: pieces[first])
                    }
                }
                Section("İkinci parça") {
                    Button(second.map { "\($0 + 1). \(pieces[$0].displayName(index: $0))" } ?? "Parça seç") { choosingSecond = true }
                    if let second {
                        Text("Dosya sırası: \(second + 1) / \(pieces.count)")
                        if let id = pieces[second].id { Text("Parça kimliği: \(id.uuidString)").textSelection(.enabled) }
                        PieceDetails(piece: pieces[second])
                    }
                }
                if let first, let second {
                    let a = pieces[first], b = pieces[second]
                    if first == second {
                        Text("Aynı parça iki kez seçildi; farklar sıfır olabilir.").foregroundStyle(.secondary)
                    }

                    if a.hasMinimumContourPoints, b.hasMinimumContourPoints,
                       let firstBounds = a.contourBounds, let secondBounds = b.contourBounds,
                       ContourOverlay.bounds(firstBounds, secondBounds) != nil,
                       ContourOverlay.errorBound(first: a.points, firstBounds: firstBounds,
                                                 second: b.points, secondBounds: secondBounds) != nil {

                        Button("Parçaları üst üste göster…") { showingOverlay = true }
                            .sheet(isPresented: $showingOverlay) {
                                PieceOverlayView(first: a, firstIndex: first, second: b, secondIndex: second)
                            }
                    } else {
                        Text("Bu konturlar sayı hassasiyeti veya geometri nedeniyle güvenle üst üste gösterilemiyor.").foregroundStyle(.secondary)
                    }
                    if let report = PieceComparisonReport.make(pieces: pieces, projectName: projectName, first: first, second: second, firstSpan: firstSpan, secondSpan: secondSpan) {
                        Button("Karşılaştırma özetini önizle…") { reportPreview = ReportPreview(text: report) }
                    }
                    Section("Seçilen kontur aralıkları") {
                        let firstLength = firstSpan.length(points: a.points, closed: a.closed, pieceIndex: first)
                        let secondLength = secondSpan.length(points: b.points, closed: b.closed, pieceIndex: second)
                        NavigationLink("Birinci parçanın aralığını seç…") {
                            PieceSpanSelectionView(piece: a, pieceIndex: first, selection: $firstSpan)
                        }
                        NavigationLink("İkinci parçanın aralığını seç…") {
                            PieceSpanSelectionView(piece: b, pieceIndex: second, selection: $secondSpan)
                        }
                        if let value = firstLength.flatMap({ MeasurementDisplay.format($0) }) {
                            Text("Birinci aralık: \(firstSpan.start) → \(firstSpan.end)\(a.closed ? (firstSpan.forward ? " · ileri" : " · geri") : "") · yaklaşık \(value) mm")
                        } else { Text("Birinci aralık seçilmedi veya hesaplanamadı.") }
                        if let value = secondLength.flatMap({ MeasurementDisplay.format($0) }) {
                            Text("İkinci aralık: \(secondSpan.start) → \(secondSpan.end)\(b.closed ? (secondSpan.forward ? " · ileri" : " · geri") : "") · yaklaşık \(value) mm")
                        } else { Text("İkinci aralık seçilmedi veya hesaplanamadı.") }
                        Text("Aralık farkı (ikinci − birinci): \(difference(firstLength, secondLength))")
                        Text("Aralıklar dosyadaki köşeler arasında düz çizgileri izler; dikiş uyumu doğrulanmaz. Paylaşım özeti seçili aralıkları ve uyarıları da içerir.")
                            .font(.caption).foregroundStyle(.secondary)
                    }
                    Section("Yaklaşık fark: ikinci − birinci") {
                        Text("En: \(difference(a.contourBounds.map { $0.maxX - $0.minX }, b.contourBounds.map { $0.maxX - $0.minX }))")
                        Text("Boy: \(difference(a.contourBounds.map { $0.maxY - $0.minY }, b.contourBounds.map { $0.maxY - $0.minY }))")
                        Text("Toplam kontur uzunluğu: \(difference(a.contourLength, b.contourLength))")
                    }
                } else {
                    Text("Karşılaştırmak için iki parça seçin.").foregroundStyle(.secondary)
                }
                Section {
                    Text("Değerler dosyadaki doğrusal konturlardan hesaplanır ve 6 anlamlı basamağa yuvarlanır. Açık ve kapalı konturların toplamları farklı anlam taşır. Bu karşılaştırma dikiş uyumu veya üretime uygunluk doğrulaması değildir.")
                }
            }
            .navigationTitle("Parçaları karşılaştır")
            .toolbar { ToolbarItem(placement: .cancellationAction) { Button("Kapat") { dismiss() } } }
            .sheet(isPresented: $choosingFirst) { PiecePicker(pieces: pieces, selection: $first) }
            .sheet(isPresented: $choosingSecond) { PiecePicker(pieces: pieces, selection: $second) }
            .sheet(item: $reportPreview) { preview in
                NavigationStack {
                    ScrollView {
                        Text(preview.text)
                            .frame(maxWidth: .infinity, alignment: .leading)
                            .textSelection(.enabled)
                            .accessibilityIdentifier("comparison-report-preview")
                            .padding()
                    }
                    .navigationTitle("Paylaşım önizlemesi")
                    .toolbar { ToolbarItem(placement: .cancellationAction) { Button("Kapat") { reportPreview = nil } } }
                    .safeAreaInset(edge: .bottom) {
                        ShareLink(item: preview.text) { Label("Özeti paylaş…", systemImage: "square.and.arrow.up") }
                            .padding().frame(maxWidth: .infinity).background(.regularMaterial)
                    }
                }
            }
        }
    }
}
private struct PieceOverlayView: View {
    let first: Drawing.Piece
    let firstIndex: Int
    let second: Drawing.Piece
    let secondIndex: Int
    @Environment(\.dismiss) private var dismiss
    @State private var showFirst = true
    @State private var showSecond = true
    @State private var shiftX = 0.0
    @State private var shiftY = 0.0
    @State private var shiftStep = 1.0
    @State private var shiftError: String?
    @State private var shiftCheck: Task<Void, Never>?
    @State private var shiftRequest = UUID()
    @State private var isCheckingShift = false
    @State private var showingNumericShift = false
    @State private var showingAnchorSelection = false
    @State private var firstAnchor = ""
    @State private var secondAnchor = ""
    @State private var shiftXText = "0"
    @State private var shiftYText = "0"
    @State private var zoom: CGFloat = 1
    @State private var offset: CGSize = .zero
    @State private var panEnabled = false
    @State private var canvasSize: CGSize = .zero
    @GestureState private var pinch: CGFloat = 1
    @GestureState private var drag: CGSize = .zero

    private func boundedZoom(_ value: CGFloat) -> CGFloat { min(max(value.isFinite ? value : 1, 0.5), 10) }
    private func boundedOffset(_ value: CGSize, size: CGSize, zoom: CGFloat) -> CGSize {
        let x = size.width * max(zoom, 1), y = size.height * max(zoom, 1)
        return CGSize(width: min(max(value.width, -x), x), height: min(max(value.height, -y), y))
    }
    private func pan(x: CGFloat, y: CGFloat) {
        offset = boundedOffset(CGSize(width: offset.width + x, height: offset.height + y), size: canvasSize, zoom: zoom)
    }
    private enum ShiftDestination { case overlay, numeric, anchor }
    private func cancelShift() {
        shiftRequest = UUID(); shiftCheck?.cancel(); shiftCheck = nil; isCheckingShift = false
    }
    private func shift(dx: Double, dy: Double) {
        applyShift(x: shiftX + dx, y: shiftY + dy, destination: .overlay)
    }
    private func applyShift(x: Double, y: Double, destination: ShiftDestination) {
        cancelShift()
        guard x.isFinite, y.isFinite, abs(x) <= 10_000, abs(y) <= 10_000,
              let firstBounds = first.contourBounds, let secondBounds = second.contourBounds else {
            shiftError = "Bu görsel kaydırma sayı hassasiyeti veya 10.000 mm sınırı nedeniyle uygulanamadı."
            return
        }
        let inputSnapshot: ContourOverlay.ShiftInputSnapshot?
        switch destination {
        case .overlay: inputSnapshot = nil
        case .numeric: inputSnapshot = .init(first: shiftXText, second: shiftYText)
        case .anchor: inputSnapshot = .init(first: firstAnchor, second: secondAnchor)
        }
        let request = UUID(), firstPoints = first.points, secondPoints = second.points
        shiftRequest = request; isCheckingShift = true; shiftError = nil
        shiftCheck = Task {
            let worker = Task.detached(priority: .userInitiated) {
                #if DEBUG && targetEnvironment(simulator)
                if ProcessInfo.processInfo.arguments.contains("--ui-test-delay-overlay-check") { Thread.sleep(forTimeInterval: 1.5) }
                #endif
                guard ContourOverlay.bounds(firstBounds, secondBounds, dx: x, dy: y) != nil else { return false }
                return ContourOverlay.errorBound(first: firstPoints, firstBounds: firstBounds,
                                                 second: secondPoints, secondBounds: secondBounds,
                                                 dx: x, dy: y, cancelled: { Task.isCancelled }) != nil
            }
            let valid = await withTaskCancellationHandler(operation: { await worker.value }, onCancel: { worker.cancel() })
            guard !Task.isCancelled, shiftRequest == request else { return }
            isCheckingShift = false; shiftCheck = nil
            let inputCurrent: Bool
            switch destination {
            case .overlay: inputCurrent = true
            case .numeric: inputCurrent = inputSnapshot?.matches(shiftXText, shiftYText) == true
            case .anchor: inputCurrent = inputSnapshot?.matches(firstAnchor, secondAnchor) == true
            }
            guard inputCurrent else {
                shiftError = "Girdi değişti; hizalama uygulanmadı. Güncel değerlerle yeniden deneyin."
                return
            }
            guard valid else {
                shiftError = "Bu görsel kaydırma sayı hassasiyeti veya 10.000 mm sınırı nedeniyle uygulanamadı."
                return
            }
            shiftX = x; shiftY = y
            switch destination {
            case .overlay: break
            case .numeric: showingNumericShift = false
            case .anchor: showingAnchorSelection = false
            }
        }
    }
    var body: some View {
        NavigationStack {
            ScrollView {
            VStack(alignment: .leading, spacing: 12) {
                Text("Başlangıçta parçaların sınır kutusu sol üstleri hizalanır; ikisi aynı ölçektedir. Kaynak konumları değiştirilmez.")
                Text("Hizalama ve kaydırmada tüm noktalardaki toplam sayısal yuvarlama hatası en fazla 0,001 mm ile sınırlandırılır. Bu sınır kaynak verisinin sayıya dönüşümünü ve ekrandaki piksel yuvarlamasını içermez. Yalnızca görsel karşılaştırmadır; dikiş uyumu, çakışma veya üretime uygunluk doğrulaması değildir.")
                    .font(.caption).foregroundStyle(.secondary)
                Toggle("Birinci: \(firstIndex + 1). \(first.displayName(index: firstIndex)) · düz çizgi", isOn: $showFirst)
                Toggle("İkinci: \(secondIndex + 1). \(second.displayName(index: secondIndex)) · kesik çizgi", isOn: $showSecond)
                Text("İkinci parçanın görsel kaydırması: X \(MeasurementDisplay.format(shiftX) ?? "—"), Y \(MeasurementDisplay.format(shiftY) ?? "—") mm")
                    .font(.caption)
                Picker("Görsel kaydırma adımı", selection: $shiftStep) {
                    Text("0,5 mm").tag(0.5)
                    Text("1 mm").tag(1.0)
                    Text("5 mm").tag(5.0)
                    Text("10 mm").tag(10.0)
                }
                LazyVGrid(columns: [GridItem(.flexible()), GridItem(.flexible())]) {
                    Button("İkinciyi sola") { shift(dx: -shiftStep, dy: 0) }.disabled(isCheckingShift)
                    Button("İkinciyi sağa") { shift(dx: shiftStep, dy: 0) }.disabled(isCheckingShift)
                    Button("İkinciyi yukarı") { shift(dx: 0, dy: -shiftStep) }.disabled(isCheckingShift)
                    Button("İkinciyi aşağı") { shift(dx: 0, dy: shiftStep) }.disabled(isCheckingShift)
                }.font(.caption)
                Button("Seçilen köşeleri hizala…") { shiftError = nil; showingAnchorSelection = true }
                    .font(.caption).disabled(isCheckingShift)
                Button("Kaydırmayı sayısal gir…") {
                    shiftXText = String(shiftX); shiftYText = String(shiftY)
                    shiftError = nil; showingNumericShift = true
                }
                .font(.caption).disabled(isCheckingShift)
                Button("Görsel hizalamayı sıfırla") { cancelShift(); shiftX = 0; shiftY = 0; shiftError = nil }
                    .font(.caption)
                Text("Bu yalnızca görsel hizalamadır; parça konumuna veya paylaşılan özete yazılmaz.")
                    .font(.caption).foregroundStyle(.secondary)
                if isCheckingShift { ProgressView("Hizalama doğrulanıyor…") }
                if let shiftError { Text(shiftError).font(.caption).foregroundStyle(.secondary) }
                LazyVGrid(columns: [GridItem(.flexible()), GridItem(.flexible())]) {
                    Button("Görünümü uzaklaştır") { zoom = boundedZoom(zoom / 1.25) }.disabled(zoom <= 0.5)
                    Button("Görünümü yakınlaştır") { zoom = boundedZoom(zoom * 1.25) }.disabled(zoom >= 10)
                    Button("Görünümü sıfırla") { zoom = 1; offset = .zero }
                }.font(.caption)
                Button(panEnabled ? "Görünümü taşımayı bitir" : "Görünümü taşı") { panEnabled.toggle() }.font(.caption)
                if panEnabled {
                    LazyVGrid(columns: [GridItem(.flexible()), GridItem(.flexible())]) {
                        Button("Görünümü sola taşı") { pan(x: -40, y: 0) }
                        Button("Görünümü sağa taşı") { pan(x: 40, y: 0) }
                        Button("Görünümü yukarı taşı") { pan(x: 0, y: -40) }
                        Button("Görünümü aşağı taşı") { pan(x: 0, y: 40) }
                    }.font(.caption)
                }
                GeometryReader { geometry in
                    let activeZoom = boundedZoom(zoom * pinch)
                    let activeOffset = boundedOffset(CGSize(width: offset.width + drag.width, height: offset.height + drag.height), size: geometry.size, zoom: activeZoom)
                    overlayDrawing.scaleEffect(activeZoom).offset(activeOffset)
                        .frame(width: geometry.size.width, height: geometry.size.height)
                        .contentShape(Rectangle()).clipped()
                        .gesture(panEnabled ? DragGesture()
                            .updating($drag) { value, state, _ in state = value.translation }
                            .onEnded { value in offset = boundedOffset(CGSize(width: offset.width + value.translation.width, height: offset.height + value.translation.height), size: geometry.size, zoom: zoom) } : nil)
                        .simultaneousGesture(MagnificationGesture()
                            .updating($pinch) { value, state, _ in state = value }
                            .onEnded { value in zoom = boundedZoom(zoom * value); offset = boundedOffset(offset, size: geometry.size, zoom: zoom) })
                        .accessibilityLabel("Aynı ölçekte üst üste parça görünümü")
                        .accessibilityValue("Birinci parça \(firstIndex + 1), ikinci parça \(secondIndex + 1). Görsel kaydırma X \(MeasurementDisplay.format(shiftX) ?? "—"), Y \(MeasurementDisplay.format(shiftY) ?? "—") milimetre. Yakınlaştırma yüzde \(Int(activeZoom * 100)).")
                        .onAppear { canvasSize = geometry.size }
                        .onChange(of: geometry.size) { canvasSize = $0 }
                }.frame(height: 240)
            }
            .padding()
            }
            .onChange(of: zoom) { offset = boundedOffset(offset, size: canvasSize, zoom: $0) }
            .sheet(isPresented: $showingAnchorSelection) {
                NavigationStack {
                    Form {
                        Text("İki parçadan birer köşe seçin. Yalnız görsel hizalama değişir; kaynak dosyalar ve karşılaştırma özeti değişmez. Bu işlem dikiş uyumunu doğrulamaz.")
                        Text("Birinci parça: \(firstIndex + 1). \(first.displayName(index: firstIndex))")
                        TextField("Birinci parça köşe indeksi", text: $firstAnchor).keyboardType(.numberPad).disabled(isCheckingShift)
                        NavigationLink("Birinci köşeyi seç") { PointIndexPicker(points: first.points, selected: $firstAnchor) }.disabled(isCheckingShift)
                        Text("İkinci parça: \(secondIndex + 1). \(second.displayName(index: secondIndex))")
                        TextField("İkinci parça köşe indeksi", text: $secondAnchor).keyboardType(.numberPad).disabled(isCheckingShift)
                        NavigationLink("İkinci köşeyi seç") { PointIndexPicker(points: second.points, selected: $secondAnchor) }.disabled(isCheckingShift)
                        if isCheckingShift { ProgressView("Hizalama doğrulanıyor…") }
                        if let shiftError { Text(shiftError).foregroundStyle(.secondary) }
                        Button("Köşeleri görsel olarak hizala") {
                            guard let a = Int(firstAnchor), let b = Int(secondAnchor),
                                  first.points.indices.contains(a), second.points.indices.contains(b),
                                  let firstBounds = first.contourBounds, let secondBounds = second.contourBounds,
                                  let target = ContourOverlay.anchorOffset(first: first.points[a], firstBounds: firstBounds,
                                                                          second: second.points[b], secondBounds: secondBounds) else {
                                shiftError = "Geçerli köşeleri seçin. Tam ve yarım mm ile güvenli hizalanamayan değerler desteklenmez."
                                return
                            }
                            applyShift(x: target.x, y: target.y, destination: .anchor)
                        }.disabled(isCheckingShift)
                    }
                    .navigationTitle("Köşeleri hizala")
                    .toolbar { ToolbarItem(placement: .cancellationAction) { Button("İptal") { cancelShift(); showingAnchorSelection = false; shiftError = nil } } }
                }
            }
            .sheet(isPresented: $showingNumericShift) {
                NavigationStack {
                    Form {
                        Text("İkinci parçanın yalnızca görsel kaydırmasını tam veya yarım milimetre olarak girin (−10.000 ile 10.000). Ondalık için nokta veya virgül kullanın. Kaynak dosya değişmez.")
                        TextField("X kaydırması (mm)", text: $shiftXText).keyboardType(.numbersAndPunctuation).disabled(isCheckingShift)
                        TextField("Y kaydırması (mm)", text: $shiftYText).keyboardType(.numbersAndPunctuation).disabled(isCheckingShift)
                        if isCheckingShift { ProgressView("Hizalama doğrulanıyor…") }
                        if let shiftError { Text(shiftError).foregroundStyle(.secondary) }
                        Button("Görsel kaydırmayı uygula") {
                            guard let x = ContourOverlay.halfMillimeterShift(shiftXText), let y = ContourOverlay.halfMillimeterShift(shiftYText) else {
                                shiftError = "Her iki eksen için −10.000 ile 10.000 arasında tam veya yarım milimetre girin."
                                return
                            }
                            applyShift(x: x, y: y, destination: .numeric)
                        }.disabled(isCheckingShift)
                    }
                    .navigationTitle("Görsel kaydırma")
                    .toolbar { ToolbarItem(placement: .cancellationAction) { Button("İptal") { cancelShift(); showingNumericShift = false; shiftError = nil } } }
                }
            }
            .onChange(of: showingAnchorSelection) { if !$0 && isCheckingShift { cancelShift() } }
            .onChange(of: showingNumericShift) { if !$0 && isCheckingShift { cancelShift() } }
            .onDisappear { cancelShift() }
            .navigationTitle("Parçaları üst üste göster")
            .toolbar { ToolbarItem(placement: .cancellationAction) { Button("Kapat") { dismiss() } } }
        }
    }
    private var overlayDrawing: some View {
        Canvas { context, size in
            guard let aBounds = first.contourBounds, let bBounds = second.contourBounds,
                  let bounds = ContourOverlay.bounds(aBounds, bBounds, dx: shiftX, dy: shiftY),
                  let viewport = ContourViewport(bounds: bounds, width: Double(size.width), height: Double(size.height)) else { return }
            func path(_ piece: Drawing.Piece, _ pieceBounds: ContourBounds, dx: Double = 0, dy: Double = 0) -> Path? {
                guard let firstPoint = piece.points.first.flatMap({ ContourOverlay.aligned($0, bounds: pieceBounds, dx: dx, dy: dy) }) else { return nil }
                var path = Path()
                path.move(to: viewport.position(firstPoint))
                for point in piece.points.dropFirst() {
                    guard let aligned = ContourOverlay.aligned(point, bounds: pieceBounds, dx: dx, dy: dy) else { return nil }
                    path.addLine(to: viewport.position(aligned))
                }
                if piece.closed { path.closeSubpath() }
                return path
            }
            if showFirst, let path = path(first, aBounds) { context.stroke(path, with: .color(.orange), lineWidth: 2) }
            if showSecond, let path = path(second, bBounds, dx: shiftX, dy: shiftY) { context.stroke(path, with: .color(.purple), style: StrokeStyle(lineWidth: 2, dash: [6, 4])) }
        }
        .background(Color.secondary.opacity(0.08))
    }

}
private struct PieceSpanSelectionView: View {
    let piece: Drawing.Piece
    let pieceIndex: Int
    @Binding var selection: ContourMeasure.SpanSelection
    @State private var selectingStart = true
    @State private var selectionRevision = UUID()

    private var startBinding: Binding<String> {
        Binding(get: { selection.start }, set: { selection.start = $0; selectionRevision = UUID() })
    }
    private var endBinding: Binding<String> {
        Binding(get: { selection.end }, set: { selection.end = $0; selectionRevision = UUID() })
    }
    private func validIndex(_ text: String) -> Int? {
        guard selection.pieceIndex == pieceIndex, let index = Int(text), piece.points.indices.contains(index) else { return nil }
        return index
    }

    var body: some View {
        Form {
            Section("Parça") { Text("\(pieceIndex + 1). \(piece.displayName(index: pieceIndex))") }
            Section("Dosya nokta indeksleri") {
                TextField("Başlangıç indeksi", text: startBinding).keyboardType(.numberPad)
                NavigationLink("Başlangıç noktasını seç") { PointIndexPicker(points: piece.points, selected: startBinding) }
                TextField("Bitiş indeksi", text: endBinding).keyboardType(.numberPad)
                NavigationLink("Bitiş noktasını seç") { PointIndexPicker(points: piece.points, selected: endBinding) }
                if piece.closed {
                    Picker("Yol yönü", selection: $selection.forward) {
                        Text("İleri").tag(true)
                        Text("Geri").tag(false)
                    }.pickerStyle(.segmented)
                }
                Picker("Dokunarak seçilecek nokta", selection: $selectingStart) {
                    Text("Başlangıç A").tag(true)
                    Text("Bitiş B").tag(false)
                }.pickerStyle(.segmented)
                if piece.hasMinimumContourPoints, piece.contourBounds != nil {
                    PointDistancePreview(piece: piece, first: validIndex(selection.start), second: validIndex(selection.end),
                                         pathForward: selection.forward, selectionRevision: selectionRevision) { index in
                        if selection.pieceIndex != pieceIndex { selection = ContourMeasure.SpanSelection(pieceIndex: pieceIndex) }
                        if selectingStart { startBinding.wrappedValue = String(index) }
                        else { endBinding.wrappedValue = String(index) }
                    }
                    Text("A başlangıcı, B bitişi; yeşil çizgi seçili kontur yolunu gösterir. Kesik çizgi doğrusal mesafedir.")
                        .font(.caption).foregroundStyle(.secondary)
                }
                if let value = selection.length(points: piece.points, closed: piece.closed, pieceIndex: pieceIndex).flatMap({ MeasurementDisplay.format($0) }) {
                    Text("Aralık: yaklaşık \(value) mm")
                } else { Text("Geçerli başlangıç ve bitiş noktalarını seçin.").foregroundStyle(.secondary) }
            }
            Section {
                Text("İleri yön dosya sırasını izler; kapalı konturda geri yön diğer yolu seçer. Aynı nokta sıfır uzunluktur, tam tur değildir. Bu sonuç dikiş uyumu veya üretim onayı değildir.")
            }
        }
        .navigationTitle("Kontur aralığı")
        .onAppear {
            if selection.pieceIndex != pieceIndex { selection = ContourMeasure.SpanSelection(pieceIndex: pieceIndex) }
        }
    }
}
private struct PointDistanceView: View {
    let piece: Drawing.Piece
    let name: String
    @Environment(\.dismiss) private var dismiss
    @State private var first = "0"
    @State private var second = "1"
    @State private var pathForward = true
    @State private var selectingFirst = true
    @State private var selectionRevision = UUID()

    private var firstBinding: Binding<String> {
        Binding(get: { first }, set: { first = $0; selectionRevision = UUID() })
    }
    private var secondBinding: Binding<String> {
        Binding(get: { second }, set: { second = $0; selectionRevision = UUID() })
    }
    private func validIndex(_ text: String) -> Int? {
        guard let value = Int(text), piece.points.indices.contains(value) else { return nil }
        return value
    }
    private var indices: (Int, Int)? {
        guard let a = Int(first), let b = Int(second),
              piece.points.indices.contains(a), piece.points.indices.contains(b) else { return nil }
        return (a, b)
    }

    var body: some View {
        NavigationStack {
            Form {
                Section("Parça") { Text(name) }
                Section("Nokta indeksleri") {
                    Text("Dosya sırası: 0–\(piece.points.count - 1)")
                    TextField("İlk nokta indeksi", text: firstBinding).keyboardType(.numberPad)
                    NavigationLink("İlk noktayı listeden seç") { PointIndexPicker(points: piece.points, selected: firstBinding) }
                    TextField("İkinci nokta indeksi", text: secondBinding).keyboardType(.numberPad)
                    NavigationLink("İkinci noktayı listeden seç") { PointIndexPicker(points: piece.points, selected: secondBinding) }
                    if piece.closed {
                        Picker("Çizimde gösterilen kontur yolu", selection: $pathForward) {
                            Text("İleri").tag(true)
                            Text("Geri").tag(false)
                        }.pickerStyle(.segmented)
                    }
                    Picker("Dokunarak seçilecek nokta", selection: $selectingFirst) {
                        Text("İlk nokta A").tag(true)
                        Text("İkinci nokta B").tag(false)
                    }.pickerStyle(.segmented)
                    if piece.hasMinimumContourPoints, piece.contourBounds != nil {
                        PointDistancePreview(piece: piece, first: validIndex(first), second: validIndex(second), pathForward: pathForward, selectionRevision: selectionRevision) { index in
                            if selectingFirst { first = String(index) } else { second = String(index) }
                        }
                        Text("Çizimde bir köşeye dokunarak seçin. A: ilk nokta · B: ikinci nokta · yeşil: seçili kontur yolu · kesik çizgi: doğrusal mesafe")
                            .font(.caption).foregroundStyle(.secondary)
                    }
                    if let (a, b) = indices {
                        let firstPoint = piece.points[a], secondPoint = piece.points[b]
                        if let ax = MeasurementDisplay.format(firstPoint.x), let ay = MeasurementDisplay.format(firstPoint.y),
                           let bx = MeasurementDisplay.format(secondPoint.x), let by = MeasurementDisplay.format(secondPoint.y) {
                            Text("İlk nokta: X \(ax), Y \(ay) mm")
                            Text("İkinci nokta: X \(bx), Y \(by) mm")
                        }
                        if let distance = ContourMeasure.distance(piece.points, from: a, to: b),
                           let value = MeasurementDisplay.format(distance) {
                            Text("Yaklaşık doğrusal mesafe: \(value) mm").font(.headline)
                        } else { Text("Mesafe hesaplanamadı.") }
                        if let length = ContourMeasure.pathLength(piece.points, from: a, to: b, closed: piece.closed),
                           let value = MeasurementDisplay.format(length) {
                            Text(piece.closed ? "Kontur boyunca ileri: yaklaşık \(value) mm" : "Kontur boyunca: yaklaşık \(value) mm")
                        } else { Text("Kontur boyunca uzunluk hesaplanamadı.") }
                        if piece.closed,
                           let length = ContourMeasure.pathLength(piece.points, from: a, to: b, closed: true, forward: false),
                           let value = MeasurementDisplay.format(length) {
                            Text("Kontur boyunca geri: yaklaşık \(value) mm")
                        }
                    } else {
                        Text("Geçerli iki nokta indeksi girin.").foregroundStyle(.secondary)
                    }
                }
                Section {
                    Text("Koordinatlar ve mesafe en fazla 6 anlamlı basamağa yuvarlanır. Düz mesafe iki nokta arasındadır. Kontur uzunlukları dosyadaki düz çizgileri izler; kapalı konturda ileri dosya sırası, geri ters yöndür. Aynı nokta için uzunluk sıfırdır; tam tur değildir. Bunlar üretim ölçüsü değildir.")
                }
            }
            .navigationTitle("İki nokta mesafesi")
            .toolbar { ToolbarItem(placement: .cancellationAction) { Button("Kapat") { dismiss() } } }
        }
    }
}
// Display only: the line connects the two chosen file vertices and never
// changes their coordinates or the source document.
private struct PointDistancePreview: View {
    let piece: Drawing.Piece
    let first: Int?
    let second: Int?
    let pathForward: Bool
    let selectionRevision: UUID
    let onSelect: (Int) -> Void

    @State private var zoom: CGFloat = 1
    @State private var offset: CGSize = .zero
    @State private var panEnabled = false
    @State private var hitMessage: String?
    @State private var canvasSize: CGSize = .zero
    @GestureState private var pinch: CGFloat = 1
    @GestureState private var drag: CGSize = .zero

    private func boundedZoom(_ value: CGFloat) -> CGFloat { min(max(value.isFinite ? value : 1, 0.5), 10) }
    private func boundedOffset(_ value: CGSize, size: CGSize, zoom: CGFloat) -> CGSize {
        let x = size.width * max(zoom, 1), y = size.height * max(zoom, 1)
        return CGSize(width: min(max(value.width, -x), x), height: min(max(value.height, -y), y))
    }

    var body: some View {
        VStack(alignment: .leading) {
            GeometryReader { geometry in
                let activeZoom = boundedZoom(zoom * pinch)
                let activeOffset = boundedOffset(CGSize(width: offset.width + drag.width, height: offset.height + drag.height), size: geometry.size, zoom: activeZoom)
                drawing
                    .scaleEffect(activeZoom).offset(activeOffset)
                    .frame(width: geometry.size.width, height: geometry.size.height)
                    .contentShape(Rectangle()).clipped()
                    .gesture(panEnabled ? DragGesture()
                        .updating($drag) { value, state, _ in state = value.translation }
                        .onEnded { value in offset = boundedOffset(CGSize(width: offset.width + value.translation.width, height: offset.height + value.translation.height), size: geometry.size, zoom: zoom) } : nil)
                    .simultaneousGesture(MagnificationGesture()
                        .updating($pinch) { value, state, _ in state = value }
                        .onEnded { value in zoom = boundedZoom(zoom * value); offset = boundedOffset(offset, size: geometry.size, zoom: zoom) })
                    .simultaneousGesture(SpatialTapGesture().onEnded { value in
                        guard !panEnabled, let bounds = piece.contourBounds,
                              let viewport = ContourViewport(bounds: bounds, width: Double(geometry.size.width), height: Double(geometry.size.height)) else { return }
                        let hit = viewport.vertexHit(points: piece.points, closed: piece.closed, tap: value.location,
                                                     size: geometry.size, zoom: activeZoom, offset: activeOffset)
                        hitMessage = hit.selectionNotice
                        if case .vertex(let index) = hit { onSelect(index) }
                    })
                    .accessibilityLabel("Ölçüm önizlemesi")
                    .accessibilityValue("İlk nokta \(first.map { String($0) } ?? "seçilmedi"), ikinci nokta \(second.map { String($0) } ?? "seçilmedi"). Gösterilen kontur yolu: \(piece.closed ? (pathForward ? "ileri" : "geri") : "açık yol"). Yakınlaştırma yüzde \(Int(activeZoom * 100)).")
                    .onAppear { canvasSize = geometry.size }
                    .onChange(of: geometry.size) { canvasSize = $0 }
            }.frame(height: 180)
            if let hitMessage { Text(hitMessage).font(.caption).foregroundStyle(.secondary) }
            HStack {
                Button("Ölçüm çizimini uzaklaştır") { zoom = boundedZoom(zoom / 1.25) }.disabled(zoom <= 0.5)
                Button("Ölçüm çizimini yakınlaştır") { zoom = boundedZoom(zoom * 1.25) }.disabled(zoom >= 10)
            }.font(.caption)
            HStack {
                Button(panEnabled ? "Çizim taşımayı bitir" : "Ölçüm çizimini taşı") { panEnabled.toggle() }
                Button("Ölçüm çizimini sıfırla") { zoom = 1; offset = .zero }
            }.font(.caption)
            HStack {
                Button("İlk noktayı görünür yap") { if let first { reveal(first) } }.disabled(first == nil)
                Button("İkinci noktayı görünür yap") { if let second { reveal(second) } }.disabled(second == nil)
            }.font(.caption)
            if panEnabled {
                HStack {
                    Button("Sola taşı") { move(x: -40, y: 0) }
                    Button("Sağa taşı") { move(x: 40, y: 0) }
                    Button("Yukarı taşı") { move(x: 0, y: -40) }
                    Button("Aşağı taşı") { move(x: 0, y: 40) }
                }.font(.caption)
            }
        }
        .onChange(of: zoom) { offset = boundedOffset(offset, size: canvasSize, zoom: $0) }
        .onChange(of: first) { _ in hitMessage = nil }
        .onChange(of: second) { _ in hitMessage = nil }
        .onChange(of: selectionRevision) { _ in hitMessage = nil }
    }

    private func reveal(_ index: Int) {
        guard piece.points.indices.contains(index), let bounds = piece.contourBounds,
              let viewport = ContourViewport(bounds: bounds, width: Double(canvasSize.width), height: Double(canvasSize.height)),
              let next = ContourViewport.visibleOffset(point: viewport.position(piece.points[index]), size: canvasSize, zoom: zoom, offset: offset) else { return }
        offset = boundedOffset(next, size: canvasSize, zoom: zoom)
    }
    private func move(x: CGFloat, y: CGFloat) {
        offset = boundedOffset(CGSize(width: offset.width + x, height: offset.height + y), size: canvasSize, zoom: zoom)
    }

    private var drawing: some View {
        Canvas { context, size in
            guard let bounds = piece.contourBounds,
                  let viewport = ContourViewport(bounds: bounds, width: Double(size.width), height: Double(size.height)),
                  let beginning = piece.points.first else { return }
            var contour = Path()
            contour.move(to: viewport.position(beginning))
            for point in piece.points.dropFirst() { contour.addLine(to: viewport.position(point)) }
            if piece.closed { contour.closeSubpath() }
            context.stroke(contour, with: .color(.accentColor), lineWidth: 1.5)
            if let first, let second,
               ContourMeasure.pathLength(piece.points, from: first, to: second, closed: piece.closed, forward: pathForward) != nil,
               let indices = ContourMeasure.pathIndices(piece.points, from: first, to: second, closed: piece.closed, forward: pathForward),
               let firstIndex = indices.first {
                var selectedPath = Path()
                selectedPath.move(to: viewport.position(piece.points[firstIndex]))
                for index in indices.dropFirst() { selectedPath.addLine(to: viewport.position(piece.points[index])) }
                context.stroke(selectedPath, with: .color(.green), lineWidth: 4)
            }
            let a = first.map { viewport.position(piece.points[$0]) }
            let b = second.map { viewport.position(piece.points[$0]) }
            if let a, let b {
                var segment = Path()
                segment.move(to: a); segment.addLine(to: b)
                context.stroke(segment, with: .color(.orange), style: StrokeStyle(lineWidth: 2, dash: [5, 3]))
            }
            if let a {
                context.draw(Text("A").font(.caption.bold()).foregroundColor(.orange), at: CGPoint(x: a.x + 10, y: a.y - 10))
                context.stroke(Path(ellipseIn: CGRect(x: a.x - 7, y: a.y - 7, width: 14, height: 14)), with: .color(.orange), lineWidth: 3)
            }
            if let b {
                context.draw(Text("B").font(.caption.bold()).foregroundColor(.purple), at: CGPoint(x: b.x + 10, y: b.y + 10))
                context.stroke(Path(ellipseIn: CGRect(x: b.x - 4, y: b.y - 4, width: 8, height: 8)), with: .color(.purple), lineWidth: 3)
            }
        }
        .background(Color.secondary.opacity(0.08))

    }
}
// A lazy list keeps large contours navigable without creating a picker with
// thousands of eager rows. File indices and rounded coordinates are displayed.
private struct PointIndexPicker: View {
    let points: [ContourMeasure.Point]
    @Binding var selected: String
    @Environment(\.dismiss) private var dismiss
    @State private var search = ""

    var body: some View {
        List {
            if search.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
                ForEach(points.indices, id: \.self) { index in row(index) }
            } else if let index = Int(search.trimmingCharacters(in: .whitespacesAndNewlines)), points.indices.contains(index) {
                row(index)
            } else {
                Text("Bu indekste nokta yok.").foregroundStyle(.secondary)
            }
        }
        .searchable(text: $search, prompt: "Nokta indeksi ara")
        .navigationTitle("Nokta seç")
    }

    private func row(_ index: Int) -> some View {
        let point = points[index]
        let x = MeasurementDisplay.format(point.x) ?? "—"
        let y = MeasurementDisplay.format(point.y) ?? "—"
        return Button {
            selected = String(index)
            dismiss()
        } label: {
            VStack(alignment: .leading) {
                Text("Nokta \(index)")
                Text("X \(x), Y \(y) mm · yaklaşık").font(.caption).foregroundStyle(.secondary)
            }
        }
        .accessibilityElement(children: .combine)
        .accessibilityLabel("Nokta \(index)")
        .accessibilityValue("Yaklaşık X \(x), Y \(y) milimetre")
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
    @Binding var showGrain: Bool
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

    private var viewportBounds: ContourBounds? {
        var bounds = pieces.compactMap(\.contourBounds)
        if showGrain {
            for piece in pieces where !piece.points.isEmpty {
                if let grain = piece.grain, let grainBounds = ContourBounds(points: [grain.start, grain.end]) {
                    bounds.append(grainBounds)
                }
            }
        }
        return ContourBounds(combining: bounds)
    }

    private func refreshBounds(size: CGSize) {
        cachedBounds = viewportBounds
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
                if piece.isCollapsed {
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
