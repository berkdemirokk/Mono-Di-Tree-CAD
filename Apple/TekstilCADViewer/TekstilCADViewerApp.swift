import CoreFoundation
import SwiftUI
import UniformTypeIdentifiers

@main
struct TekstilCADViewerApp: App {
    var body: some Scene {
        #if os(macOS)
        Window("Mono Di Tree CAD", id: "workspace") {
            ContentView().appAppearance().tint(AppTheme.accent).environment(\.locale, AppTheme.locale)
        }
        #else
        WindowGroup {
            ContentView().appAppearance().tint(AppTheme.accent).environment(\.locale, AppTheme.locale)
        }
        #endif
    }
}

private struct ContentView: View {
    private struct EditorRoute: Identifiable {
        let id = UUID()
        let example: Bool
        let document: DraftProject?

        init(example: Bool = false, document: DraftProject? = nil) {
            self.example = example
            self.document = document
        }
    }

    /// Every alert and confirmation of the home and viewer screens goes through this one value,
    /// so two of them can never be asked for at the same time. A follow-up (for example "Ad
    /// değiştirilemedi" after the rename prompt) waits in `queuedAlerts` until the previous alert
    /// has finished closing.
    private struct HomeAlert: Identifiable {
        enum Kind {
            case notice(title: String, message: String)
            case confirmDelete(CADProjectLibrary.Entry)
            case rename(CADProjectLibrary.Entry)
            case unreadableDraft(EditorRoute)
        }

        let id = UUID()
        let kind: Kind

        var title: String {
            switch kind {
            case .notice(let title, _): return title
            case .confirmDelete(let entry): return "\(entry.name) silinsin mi?"
            case .rename: return "Adı değiştir"
            case .unreadableDraft: return "Önceki taslak okunamıyor"
            }
        }
    }

    /// What the home screen says about the autosaved drawing.
    private struct DraftSummary {
        let name: String
        let pieceCount: Int
        let modified: Date?
    }

    @State private var showingImporter = false
    @State private var showingPieces = false
    @State private var editorRoute: EditorRoute?
    @State private var localDraft: DraftSummary?
    @State private var recentProjects: [CADProjectLibrary.Entry] = []
    @State private var didStart = false
    @State private var recentRefresh = UUID()
    @State private var showAllRecent = false
    @State private var renameText = ""
    @State private var alert: HomeAlert?
    @State private var queuedAlerts: [HomeAlert] = []
    @State private var alertClosing = false
    @State private var archiveNotice: String?
    @State private var announceArchiveOnReturn = false

    private static let recentLimit = 10
    // Long enough for the closing alert's animation to end before the next one is presented.
    private static let alertGap: TimeInterval = 0.45

    private static let relativeFormatter: RelativeDateTimeFormatter = {
        let formatter = RelativeDateTimeFormatter()
        formatter.locale = Locale(identifier: "tr_TR")
        formatter.unitsStyle = .full
        formatter.dateTimeStyle = .named
        return formatter
    }()

    private var hasLocalDraft: Bool { localDraft != nil }

    private var visibleRecentProjects: [CADProjectLibrary.Entry] {
        showAllRecent ? recentProjects : Array(recentProjects.prefix(Self.recentLimit))
    }

    // MARK: Alerts

    private func present(_ kind: HomeAlert.Kind) {
        let next = HomeAlert(kind: kind)
        if alert == nil && !alertClosing && queuedAlerts.isEmpty {
            alert = next
        } else {
            queuedAlerts.append(next)
        }
    }

    private func showNotice(_ title: String, _ message: String) {
        present(.notice(title: title, message: message))
    }

    /// SwiftUI may clear the binding before or after it runs the tapped button's action. Only the
    /// alert that was on screen is cleared, and the next one is shown after a short gap.
    private var alertBinding: Binding<Bool> {
        let shownID = alert?.id
        return Binding(
            get: { alert != nil },
            set: { shown in
                guard !shown, let shownID, alert?.id == shownID else { return }
                alert = nil
                alertClosing = true
                DispatchQueue.main.asyncAfter(deadline: .now() + Self.alertGap) { showNextAlert() }
            })
    }

    private func showNextAlert() {
        alertClosing = false
        guard alert == nil, !queuedAlerts.isEmpty else { return }
        alert = queuedAlerts.removeFirst()
    }

    @ViewBuilder
    private func alertActions(_ shown: HomeAlert) -> some View {
        switch shown.kind {
        case .notice:
            Button("Tamam", role: .cancel) { }
        case .confirmDelete(let entry):
            Button("Sil", role: .destructive) { deleteProject(entry) }
            Button("Vazgeç", role: .cancel) { }
        case .rename(let entry):
            TextField("Yeni ad", text: $renameText)
            Button("Kaydet") { renameProject(entry, to: renameText) }
            Button("Vazgeç", role: .cancel) { }
        case .unreadableDraft(let route):
            Button("Taslağı ayır ve devam et") { setAsideUnreadableDraftAndContinue(route) }
            Button("Vazgeç", role: .cancel) { }
        }
    }

    private func alertMessage(_ shown: HomeAlert) -> Text {
        switch shown.kind {
        case .notice(_, let message):
            return Text(message)
        case .confirmDelete:
            return Text("Bu işlem geri alınamaz. Bu projede kaldığın son çizim de silinir.")
        case .rename:
            return Text("Ad 1 ile 120 karakter arasında olmalı.")
        case .unreadableDraft:
            return Text("Cihazdaki otomatik taslak bozuk ya da bu sürümle uyumsuz. Dosya silinmez; ayrı bir klasöre taşınır ve yeni çalışma açılır.")
        }
    }

    private func announce(_ text: String) {
        #if os(iOS)
        UIAccessibility.post(notification: .announcement, argument: text)
        #endif
    }

    // MARK: Projects and the autosaved drawing

    private func deleteProject(_ entry: CADProjectLibrary.Entry) {
        do {
            try CADProjectLibrary.delete(entry.id)
            recentProjects.removeAll { $0.id == entry.id }
            announce("\(entry.name) silindi.")
        } catch {
            showNotice("Proje silinemedi", error.localizedDescription)
        }
        refreshProjects()
    }

    private func renameProject(_ entry: CADProjectLibrary.Entry, to name: String) {
        do {
            try CADProjectLibrary.rename(entry.id, to: name)
            if let newName = CADProjectLibrary.validatedName(name) { announce("Yeni ad: \(newName).") }
        } catch {
            showNotice("Ad değiştirilemedi", error.localizedDescription)
        }
        refreshProjects()
    }

    private func refreshProjects() {
        do {
            localDraft = try CADLocalDraftStore.restore().map { draft in
                DraftSummary(name: draft.name, pieceCount: draft.pieces.count,
                             modified: CADLocalDraftStore.currentModificationDate())
            }
        } catch {
            localDraft = nil
            showNotice("Son çizim açılamadı",
                       "Taslak okunamadı. \(error.localizedDescription) Yeni bir çalışma başlatırken bu taslağı ayırabilirsin.")
        }
        recentRefresh = UUID()
    }

    @MainActor
    private func readRecentProjects() async {
        let result = await Task.detached(priority: .userInitiated) {
            Result { try CADProjectLibrary.recent() }
        }.value
        guard !Task.isCancelled else { return }
        switch result {
        case .success(let entries): recentProjects = entries
        case .failure(let failure): showNotice("Son projeler okunamadı", failure.localizedDescription)
        }
    }

    private func resumeEditor() {
        do {
            guard let draft = try CADLocalDraftStore.restore() else {
                throw ViewerError.message("Kaydedilmiş taslak bulunamadı.")
            }
            editorRoute = EditorRoute(document: draft)
        } catch { showNotice("Son çizim açılamadı", error.localizedDescription) }
    }

    private func openProject(_ entry: CADProjectLibrary.Entry) {
        do { openEditor(EditorRoute(document: try CADProjectLibrary.open(entry.id))) }
        catch {
            showNotice("Proje açılamadı", error.localizedDescription)
            refreshProjects()
        }
    }

    private func noteArchived(_ name: String?) {
        archiveNotice = name.map { "Önceki çizimin «\($0)» Son projeler'e kaydedildi." }
        announceArchiveOnReturn = archiveNotice != nil
    }

    private func openEditor(_ route: EditorRoute) {
        do {
            let archived = try CADLocalDraftStore.archiveBeforeSwitch()
            noteArchived(archived)
            editorRoute = route
        } catch {
            if CADLocalDraftStore.hasUnreadableDraft() {
                // A damaged or incompatible draft must not block every new drawing.
                // Ask before moving it aside; nothing is deleted.
                present(.unreadableDraft(route))
            } else {
                showNotice("Yeni çalışma açılmadı",
                           "Önceki taslak korunamadığı için yeni çalışma açılmadı. \(error.localizedDescription)")
            }
        }
    }

    // The route is passed in by the alert, so this does not depend on whether SwiftUI clears
    // the alert before or after it runs the button action.
    private func setAsideUnreadableDraftAndContinue(_ route: EditorRoute) {
        do {
            _ = try CADLocalDraftStore.setAsideUnreadableDraft()
            let archived = try CADLocalDraftStore.archiveBeforeSwitch()
            noteArchived(archived)
            localDraft = nil
            editorRoute = route
        } catch {
            showNotice("Yeni çalışma açılmadı",
                       "Okunamayan taslak ayrılamadı; yeni çalışma açılmadı. \(error.localizedDescription)")
        }
    }

    private func editorClosed() {
        refreshProjects()
        if announceArchiveOnReturn, let archiveNotice { announce(archiveNotice) }
        announceArchiveOnReturn = false
    }

    private func closeViewer() {
        loadRequest = UUID()
        isLoading = false
        showingPieces = false
        project = nil
        selectedPiece = nil
    }

    /// "«Örnek kalıp» • 2 parça • 5 dakika önce değiştirildi"
    private func draftDetail(_ draft: DraftSummary, now: Date) -> String {
        var parts = ["«\(draft.name)»", draft.pieceCount == 0 ? "henüz parça yok" : "\(draft.pieceCount) parça"]
        if let modified = draft.modified {
            let moment = min(modified, now)
            let when = now.timeIntervalSince(moment) < 60
                ? "az önce"
                : Self.relativeFormatter.localizedString(for: moment, relativeTo: now)
            parts.append("\(when) değiştirildi")
        }
        return parts.joined(separator: " • ")
    }

    @State private var project: Drawing?
    @State private var selectedPiece: Int?
    @State private var drawingRevision = UUID()
    @State private var loadRequest = UUID()
    @State private var isLoading = false

    var body: some View {
        VStack(spacing: 12) {
            if isLoading { ProgressView("Dosya açılıyor…") }
            if let project {
                viewer(project)
            } else {
                home.disabled(isLoading)
            }
        }
        .padding()
        .frame(minWidth: 300, minHeight: 300)
        .background(AppTheme.page.ignoresSafeArea())
        .sheet(item: $editorRoute, onDismiss: editorClosed) { route in
            EasyCADWorkspace(example: route.example, imported: route.document)
        }
        .fileImporter(isPresented: $showingImporter, allowedContentTypes: [.item]) { result in
            do {
                let url = try result.get()
                guard url.pathExtension.lowercased() == "tcad" else { throw ViewerError.message("Bir .tcad dosyası seç.") }
                let request = UUID()
                loadRequest = request
                isLoading = true
                DispatchQueue.global(qos: .userInitiated).async {
                    let access = url.startAccessingSecurityScopedResource()
                    let loaded = Result { try Drawing.load(url) }
                    if access { url.stopAccessingSecurityScopedResource() }
                    DispatchQueue.main.async {
                        guard loadRequest == request else { return }
                        isLoading = false
                        switch loaded {
                        case .success(let drawing):
                            showingPieces = false
                            selectedPiece = nil
                            drawingRevision = UUID()
                            project = drawing
                        case .failure(let failure):
                            showNotice("Dosya açılamadı", failure.localizedDescription)
                        }
                    }
                }
            } catch let failure as NSError where failure.domain == NSCocoaErrorDomain && failure.code == NSUserCancelledError {
                // The Files picker was dismissed without selecting a project.
            } catch { showNotice("Dosya açılamadı", error.localizedDescription) }
        }
        .onAppear {
            guard !didStart else { return }
            didStart = true
            #if DEBUG && targetEnvironment(simulator)
            let arguments = ProcessInfo.processInfo.arguments
            if arguments.contains("--ui-test-reset-draft") {
                CADLocalDraftStore.discard()
                CADProjectLibrary.resetTestLibrary()
                // One-time hints must not depend on which test ran before.
                UserDefaults.standard.removeObject(forKey: "workshop.panHintSeen")
            }
            if arguments.contains("--ui-test-unreadable-draft") {
                CADLocalDraftStore.writeUnreadableDraftForTesting()
            }
            if arguments.contains("--ui-test-seed-projects") {
                CADProjectLibrary.seedProjectsForTesting(count: 12)
            }
            if arguments.contains("--ui-test-unreadable-project") {
                CADProjectLibrary.writeUnreadableProjectForTesting()
            }
            #endif
            refreshProjects()
            #if DEBUG && targetEnvironment(simulator)
            guard ProcessInfo.processInfo.arguments.contains("--ui-test-fixture"),
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
            } catch { showNotice("Dosya açılamadı", error.localizedDescription) }
            #endif
        }
        .task(id: recentRefresh) {
            if didStart { await readRecentProjects() }
        }
        .alert(alert?.title ?? "", isPresented: alertBinding, presenting: alert) { shown in
            alertActions(shown)
        } message: { shown in
            alertMessage(shown)
        }
    }

    // MARK: Home

    @ViewBuilder
    private func recentActions(_ entry: CADProjectLibrary.Entry) -> some View {
        if entry.readable {
            Button {
                renameText = entry.name
                present(.rename(entry))
            } label: {
                Label("Adı değiştir", systemImage: "pencil")
            }
        }
        Button(role: .destructive) {
            present(.confirmDelete(entry))
        } label: {
            Label("Sil", systemImage: "trash")
        }
    }

    private var resumeButton: some View {
        Button(action: resumeEditor) {
            HStack(spacing: 12) {
                Image(systemName: "clock.arrow.circlepath")
                    .frame(width: 28)
                    .accessibilityHidden(true)
                VStack(alignment: .leading, spacing: 2) {
                    Text("Son çizime devam et").font(.headline)
                    if let localDraft {
                        TimelineView(.everyMinute) { context in
                            Text(draftDetail(localDraft, now: context.date))
                                .font(.subheadline)
                                .foregroundStyle(AppTheme.muted)
                                .lineLimit(3)
                                .multilineTextAlignment(.leading)
                        }
                    }
                }
                Spacer(minLength: 0)
                Image(systemName: "chevron.forward").font(.caption)
                    .foregroundStyle(AppTheme.muted)
                    .accessibilityHidden(true)
            }
            .padding(.vertical, 6)
            .frame(maxWidth: .infinity, minHeight: 48, alignment: .leading)
        }
        .buttonStyle(.appSecondary)
        // The label stays the short action name; which drawing it is goes into the value.
        .accessibilityLabel("Son çizime devam et")
        .accessibilityValue(localDraft.map { draftDetail($0, now: Date()) } ?? "")
        .accessibilityHint("Cihazda otomatik kaydedilmiş çizimi açar.")
    }

    @ViewBuilder
    private var archiveNoticeView: some View {
        if let archiveNotice {
            HStack(alignment: .center, spacing: 10) {
                Image(systemName: "checkmark.circle.fill")
                    .foregroundStyle(AppTheme.accent)
                    .accessibilityHidden(true)
                Text(archiveNotice)
                    .font(.subheadline)
                    .foregroundStyle(AppTheme.ink)
                    .fixedSize(horizontal: false, vertical: true)
                    .accessibilityIdentifier("archive-notice")
                Spacer(minLength: 0)
                Button { self.archiveNotice = nil } label: {
                    Image(systemName: "xmark")
                        .font(.footnote.weight(.semibold))
                        .foregroundStyle(AppTheme.muted)
                        .frame(width: 44, height: 44)
                        .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
                .accessibilityLabel("Bildirimi kapat")
            }
            .padding(.leading, 12)
            .background(AppTheme.accentSoft, in: RoundedRectangle(cornerRadius: 12))
        }
    }

    @ViewBuilder
    private func recentRow(_ entry: CADProjectLibrary.Entry) -> some View {
        HStack(spacing: 4) {
            if entry.readable {
                Button { openProject(entry) } label: {
                    HStack(spacing: 12) {
                        Image(systemName: "doc.text")
                            .foregroundStyle(AppTheme.accent)
                            .frame(width: 36, height: 36)
                            .background(AppTheme.accentSoft, in: RoundedRectangle(cornerRadius: 10))
                        VStack(alignment: .leading, spacing: 2) {
                            Text(entry.name).foregroundStyle(AppTheme.ink)
                            Text(entry.modified, format: .dateTime.day().month().year().hour().minute())
                                .font(.caption).foregroundStyle(AppTheme.muted)
                        }
                        Spacer()
                        Image(systemName: "chevron.forward").font(.caption)
                            .foregroundStyle(AppTheme.muted)
                    }
                    .frame(minHeight: 48)
                    .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
                .accessibilityLabel(entry.name)
                .accessibilityValue(entry.modified.formatted(.dateTime.day().month().year().hour().minute().locale(AppTheme.locale)))
                .accessibilityHint("Projeyi açar.")
                .accessibilityIdentifier("recent-\(entry.id.uuidString)")
            } else {
                // A file that cannot be read is listed so it can be removed, but it never tries to open.
                HStack(spacing: 12) {
                    Image(systemName: "exclamationmark.triangle")
                        .foregroundStyle(AppTheme.muted)
                        .frame(width: 36, height: 36)
                        .background(AppTheme.line.opacity(0.5), in: RoundedRectangle(cornerRadius: 10))
                    VStack(alignment: .leading, spacing: 2) {
                        Text(entry.name).foregroundStyle(AppTheme.muted)
                        Text("Okunamıyor").font(.caption).foregroundStyle(AppTheme.muted)
                    }
                    Spacer()
                }
                .frame(minHeight: 48)
                .accessibilityElement(children: .combine)
                .accessibilityHint("Bu proje açılamaz. Silmek için işlemler düğmesini kullan.")
                .accessibilityIdentifier("unreadable-\(entry.id.uuidString)")
            }
            Menu {
                recentActions(entry)
            } label: {
                Image(systemName: "ellipsis.circle")
                    .font(.title3)
                    .foregroundStyle(AppTheme.muted)
                    .frame(width: 44, height: 44)
                    .contentShape(Rectangle())
            }
            .accessibilityLabel("\(entry.name) için işlemler")
            .accessibilityIdentifier("project-actions-\(entry.id.uuidString)")
        }
        .contextMenu { recentActions(entry) }
    }

    @ViewBuilder
    private var homeFileActions: some View {
        Button { showingImporter = true } label: {
            Label("Dosya aç (.tcad)", systemImage: "folder")
                .fixedSize(horizontal: false, vertical: true)
                .frame(maxWidth: .infinity, minHeight: 44)
        }
        .buttonStyle(.appSecondary)
        .accessibilityHint("Dosyayı önizlemek için açar.")
        Button { openEditor(EditorRoute(example: true)) } label: {
            Label("Örnekle dene", systemImage: "square.on.square")
                .fixedSize(horizontal: false, vertical: true)
                .frame(maxWidth: .infinity, minHeight: 44)
        }
        .buttonStyle(.appSecondary)
        .accessibilityHint("İki hazır parçayla uygulamayı denersin.")
    }

    private var home: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 20) {
                HStack(spacing: 12) {
                    Image("StudioMark")
                        .resizable()
                        .frame(width: 44, height: 44)
                        .clipShape(RoundedRectangle(cornerRadius: 10))
                        .accessibilityHidden(true)
                    Text("Mono Di Tree CAD")
                        .font(.headline.weight(.semibold))
                        .foregroundStyle(AppTheme.ink)
                    Spacer(minLength: 0)
                }

                VStack(alignment: .leading, spacing: 12) {
                    Text("Kalıbını kolayca oluştur")
                        .font(.system(.largeTitle, design: .serif).weight(.semibold))
                        .foregroundStyle(AppTheme.ink)
                        .accessibilityAddTraits(.isHeader)

                    Button { openEditor(EditorRoute()) } label: {
                        Label("Yeni proje oluştur", systemImage: "plus")
                            .frame(maxWidth: .infinity, minHeight: 50)
                    }
                    .buttonStyle(.appPrimary)
                    ViewThatFits(in: .horizontal) {
                        HStack(spacing: 10) { homeFileActions }
                        VStack(spacing: 6) { homeFileActions }
                    }
                }

                VStack(alignment: .leading, spacing: 10) {
                    Text("Son projeler").font(.headline).foregroundStyle(AppTheme.ink)
                        .accessibilityAddTraits(.isHeader)
                    archiveNoticeView
                    if hasLocalDraft { resumeButton }
                    if recentProjects.isEmpty && !hasLocalDraft {
                        Text("Kaydettiğin projeler burada görünür.")
                            .font(.subheadline).foregroundStyle(AppTheme.muted)
                    }
                    ForEach(visibleRecentProjects) { entry in
                        recentRow(entry)
                    }
                    if recentProjects.count > Self.recentLimit {
                        Button { showAllRecent.toggle() } label: {
                            Text(showAllRecent ? "Daha az göster" : "Tümünü göster (\(recentProjects.count))")
                                .font(.subheadline.weight(.semibold))
                                .foregroundStyle(AppTheme.accent)
                                .frame(maxWidth: .infinity, minHeight: 44)
                                .contentShape(Rectangle())
                        }
                        .buttonStyle(.plain)
                        .accessibilityIdentifier("show-all-projects")
                    }
                }
                .appCard()

                StudioPatternShelf { openEditor(EditorRoute(document: $0)) }

                AppearancePicker()
            }
            // A readable column on iPad and wide windows; full width on a phone.
            .frame(maxWidth: 640, alignment: .leading)
            .frame(maxWidth: .infinity)
        }
    }

    // MARK: File viewer

    private func viewer(_ project: Drawing) -> some View {
        GeometryReader { outer in
            ScrollView {
                VStack(alignment: .leading, spacing: 14) {
                    Button(action: closeViewer) {
                        Label("Ana ekrana dön", systemImage: "chevron.backward")
                    }
                    .buttonStyle(.appSecondary)
                    .accessibilityHint("Önizlemeyi kapatır ve ana ekrana döner.")
                    .accessibilityIdentifier("viewer-back")

                    VStack(alignment: .leading, spacing: 4) {
                        Text("Dosya önizlemesi").font(.subheadline).foregroundStyle(AppTheme.muted)
                        Text(project.name).font(.title2.bold()).foregroundStyle(AppTheme.ink)
                            .fixedSize(horizontal: false, vertical: true)
                            .accessibilityAddTraits(.isHeader)
                    }

                    archiveNoticeView
                    if hasLocalDraft { resumeButton }

                    viewerActions(project)

                    if let notice = project.formatNotice {
                        HStack(alignment: .top, spacing: 10) {
                            Image(systemName: "exclamationmark.triangle.fill")
                                .foregroundStyle(Self.warning)
                                .accessibilityHidden(true)
                            Text(notice)
                                .font(.subheadline)
                                .foregroundStyle(Self.warning)
                                .fixedSize(horizontal: false, vertical: true)
                                .accessibilityAddTraits(.isStaticText)
                        }
                        .appCard()
                    }

                    if project.pieces.contains(where: { $0.points.count > 1 }) {
                        piecePickerButton(project)
                        VStack(alignment: .leading, spacing: 10) {
                            DrawingView(
                                pieces: selectedPiece.map { [project.pieces[$0]] } ?? project.pieces,
                                bounds: selectedPiece.flatMap { project.pieces[$0].bounds } ?? project.bounds,
                                label: selectedPiece.map { "\(project.pieces[$0].displayName(index: $0)) önizlemesi" }
                                    ?? "\(project.pieces.count) parçanın önizlemesi"
                            )
                            .id("\(drawingRevision)-\(selectedPiece.map(String.init) ?? "all")")
                            // The drawing keeps its old height; the 52 pt are the button row below it.
                            .frame(height: max(260, min(outer.size.height * 0.55, 560)) + 52)
                            .background(AppTheme.page)
                            .clipShape(RoundedRectangle(cornerRadius: 12))
                            if project.pieces.contains(where: { $0.grain != nil }) {
                                Text("Oklar kumaşın yönünü gösterir. Kesikli turuncu ok, yönün kontrol edilmesi gerektiği anlamına gelir.")
                                    .font(.caption).foregroundStyle(AppTheme.muted)
                                    .fixedSize(horizontal: false, vertical: true)
                            }
                            viewerFooter(project)
                        }
                        .appCard()
                        if let selectedPiece {
                            PieceDetails(piece: project.pieces[selectedPiece])
                        }
                    } else {
                        VStack(alignment: .leading, spacing: 8) {
                            Text("Bu dosyada gösterilecek parça yok.")
                                .foregroundStyle(AppTheme.muted)
                            viewerFooter(project)
                        }
                        .appCard()
                    }
                }
                // A readable column on iPad and wide windows; full width on a phone.
                .frame(maxWidth: 640, alignment: .leading)
                .frame(maxWidth: .infinity)
            }
        }
    }

    private static let warning = AppTheme.warning

    private func viewerFooter(_ project: Drawing) -> some View {
        Text("\(project.pieces.count) parça • Ekrana sığdırıldı; kesim için kullanmayın")
            .font(.caption).foregroundStyle(AppTheme.muted)
            .fixedSize(horizontal: false, vertical: true)
    }

    @ViewBuilder
    private func viewerActions(_ project: Drawing) -> some View {
        VStack(alignment: .leading, spacing: 10) {
            if let editableDraft = project.editableDraft {
                Text("Bu dosyayı değiştirmeden bir kopyasını açıp düzenleyebilirsin.")
                    .font(.subheadline).foregroundStyle(AppTheme.muted)
                    .fixedSize(horizontal: false, vertical: true)
                Button {
                    var copy = editableDraft
                    copy.id = UUID()
                    openEditor(EditorRoute(document: copy))
                } label: {
                    Label("Kopyasını düzenle", systemImage: "square.on.square")
                        .frame(maxWidth: .infinity, minHeight: 48)
                }
                .buttonStyle(.appPrimary)
                .accessibilityHint("Bu basit çizimin yeni düzenlenebilir bir kopyasını açar. Kaynak dosyayı değiştirmez.")
            } else {
                HStack(alignment: .top, spacing: 10) {
                    Image(systemName: "eye")
                        .foregroundStyle(AppTheme.accent)
                        .accessibilityHidden(true)
                    Text("Bu dosya burada yalnızca görüntülenebilir; düzenlemek için bilgisayardaki programı kullan.")
                        .font(.subheadline).foregroundStyle(AppTheme.ink)
                        .fixedSize(horizontal: false, vertical: true)
                }
                .accessibilityElement(children: .combine)
            }
        }
        .appCard()
    }

    private func piecePickerButton(_ project: Drawing) -> some View {
        Button { showingPieces = true } label: {
            HStack {
                VStack(alignment: .leading, spacing: 3) {
                    Text("Gösterilen parça").font(.caption).foregroundStyle(AppTheme.muted)
                    Text(selectedPiece.map { project.pieces[$0].displayName(index: $0) } ?? "Tüm parçalar")
                        .font(.headline).foregroundStyle(AppTheme.ink).lineLimit(2)
                }
                Spacer()
                Image(systemName: "chevron.up.chevron.down").foregroundStyle(AppTheme.muted)
            }
            .frame(maxWidth: .infinity, minHeight: 44, alignment: .leading)
            .appCard()
            .contentShape(RoundedRectangle(cornerRadius: AppTheme.radius))
        }
        .buttonStyle(.plain)
        .accessibilityHint("Parçaları aramak veya tek bir parçayı seçmek için açın")
        .sheet(isPresented: $showingPieces) {
            PiecePicker(pieces: project.pieces, selection: $selectedPiece)
        }
    }
}

private enum ViewerError: LocalizedError {
    case message(String)
    var errorDescription: String? { if case let .message(text) = self { return text }; return nil }
}

private struct Drawing {
    struct Point { let x: Double; let y: Double }
    struct Bounds {
        var minX: Double
        var maxX: Double
        var minY: Double
        var maxY: Double

        init(_ point: Point) {
            minX = point.x
            maxX = point.x
            minY = point.y
            maxY = point.y
        }

        mutating func include(_ point: Point) {
            minX = min(minX, point.x)
            maxX = max(maxX, point.x)
            minY = min(minY, point.y)
            maxY = max(maxY, point.y)
        }

        mutating func include(_ other: Bounds) {
            minX = min(minX, other.minX)
            maxX = max(maxX, other.maxX)
            minY = min(minY, other.minY)
            maxY = max(maxY, other.maxY)
        }
    }
    struct Grain { let start: Point; let end: Point; let needsReview: Bool }
    struct Piece {
        let name: String?
        let points: [Point]
        let bounds: Bounds?
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
    }
    let name: String
    let pieces: [Piece]
    let bounds: Bounds?
    let formatNotice: String?
    let editableDraft: DraftProject?

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
    static func load(_ url: URL) throws -> Drawing {
        let values = try url.resourceValues(forKeys: [.fileSizeKey])
        if let size = values.fileSize, size > 64 * 1024 * 1024 { throw ViewerError.message("Dosya 64 MB sınırını aşıyor.") }
        let data = try CADFileIO.read(url, limit: 64 * 1024 * 1024)
        let root: Any
        do {
            root = try JSONSerialization.jsonObject(with: data)
        } catch {
            throw ViewerError.message("Desteklenmeyen proje biçimi.")
        }
        let object: [String: Any]
        var schemaVersion = 0
        if let array = root as? [Any], array.count == 2,
           let number = array[0] as? NSNumber,
           CFGetTypeID(number) != CFBooleanGetTypeID(),
           number.doubleValue.isFinite, number.doubleValue.rounded() == number.doubleValue,
           (1...9).contains(number.intValue), let body = array[1] as? [String: Any] {
            schemaVersion = number.intValue
            object = body
        } else if let body = root as? [String: Any] { object = body }
        else { throw ViewerError.message("Desteklenmeyen proje biçimi.") }
        // Inspect only contours, not the full Windows production schema. Guard a few
        // version-required envelopes to avoid silently accepting obviously broken files.
        if schemaVersion == 6 {
            let rawPieces = object["Pieces"] as? [[String: Any]] ?? []
            guard rawPieces.contains(where: { $0["Notches"] != nil }) else {
                throw ViewerError.message("Sürüm 6 çentik işareti içermiyor.")
            }
        }
        if schemaVersion == 7 {
            guard let nesting = object["NestingSnapshot"] as? [String: Any],
                  nesting["RotationTrace"] is [String: Any] else {
                throw ViewerError.message("Sürüm 7 döndürme izi eksik.")
            }
        }
        if schemaVersion == 8 {
            guard object["Layers"] is [[String: Any]] else {
                throw ViewerError.message("Sürüm 8 katman listesi eksik.")
            }
        }
        if schemaVersion == 9 {
            let rawPieces = object["Pieces"] as? [[String: Any]] ?? []
            var foundControls = false
            for piece in rawPieces {
                guard let curves = piece["Curves"] else { continue }
                foundControls = true
                guard jsonBoolean(piece["Closed"]) == false,
                      let elements = curves as? [[String: Any]],
                      !elements.isEmpty, elements.count <= 2_000 else {
                    throw ViewerError.message("Sürüm 9 parametrik konturu geçersiz.")
                }
                for curve in elements {
                    guard let kind = curve["$type"] as? String else {
                        throw ViewerError.message("Sürüm 9 parametrik konturu geçersiz.")
                    }
                    let expected: Set<String>
                    switch kind {
                    case "line": expected = ["$type", "Start", "End"]
                    case "cubicBezier": expected = ["$type", "P0", "P1", "P2", "P3"]
                    case "arc": expected = ["$type", "Center", "Radius", "StartAngle", "SweepAngle"]
                    default: throw ViewerError.message("Bilinmeyen parametrik kontur türü.")
                    }
                    guard Set(curve.keys) == expected else {
                        throw ViewerError.message("Sürüm 9 parametrik konturu geçersiz.")
                    }
                    for key in expected where key != "$type" {
                        if ["Radius", "StartAngle", "SweepAngle"].contains(key) {
                            guard let number = jsonNumber(curve[key]),
                                  number.isFinite,
                                  (key != "Radius" || number > 0) else {
                                throw ViewerError.message("Sürüm 9 parametrik konturu geçersiz.")
                            }
                        } else {
                            guard let point = curve[key] as? [String: Any],
                                  Set(point.keys).isSubset(of: ["X", "Y", "IsFinite"]),
                                  let x = jsonNumber(point["X"]), let y = jsonNumber(point["Y"]),
                                  x.isFinite, y.isFinite,
                                  abs(x) <= 100_000_000, abs(y) <= 100_000_000,
                                  (point["IsFinite"] == nil || jsonBoolean(point["IsFinite"]) == true) else {
                                throw ViewerError.message("Sürüm 9 parametrik konturu geçersiz.")
                            }
                        }
                    }
                }
            }
            guard foundControls else {
                throw ViewerError.message("Sürüm 9 parametrik kontur içermiyor.")
            }
        } else if let pieces = object["Pieces"] as? [[String: Any]],
                  pieces.contains(where: { $0["Curves"] != nil }) {
            throw ViewerError.message("Parametrik kontur için sürüm 9 gerekir.")
        }
        guard let name = object["Name"] as? String, let rawPieces = object["Pieces"] as? [[String: Any]], rawPieces.count <= 10_000 else {
            throw ViewerError.message("Proje adı veya parça listesi eksik ya da sınır dışında.")
        }
        var total = 0
        var pieces: [Piece] = []
        var bounds: Bounds?
        for raw in rawPieces {
            guard let rawPoints = raw["Points"] as? [[String: Any]], rawPoints.count <= 10_000,
                  let closed = jsonBoolean(raw["Closed"]) else { throw ViewerError.message("Parça konturu eksik veya sınır dışında.") }
            total += rawPoints.count
            guard total <= 200_000 else { throw ViewerError.message("Toplam nokta sınırı aşıldı.") }
            var pieceBounds: Bounds?
            let points: [Point] = try rawPoints.map { point in
                guard let x = jsonNumber(point["X"]), let y = jsonNumber(point["Y"]),
                      x.isFinite, y.isFinite, abs(x) < 1e100, abs(y) < 1e100 else {
                    throw ViewerError.message("Geçersiz veya gösterilemeyen koordinat.")
                }
                let coordinate = Point(x: x, y: y)
                if pieceBounds == nil { pieceBounds = Bounds(coordinate) }
                else { pieceBounds?.include(coordinate) }
                return coordinate
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
                grainStatus = review ? "Kumaş yönü: Kontrol edilmeli" : "Kumaş yönü: Dosyada var"
                parsedGrain = Grain(start: Point(x: sx, y: sy), end: Point(x: ex, y: ey), needsReview: review)
            } else if raw["Grainline"] == nil || raw["Grainline"] is NSNull {
                grainStatus = "Kumaş yönü: Dosyada yok"
            } else {
                grainStatus = "Kumaş yönü: Okunamadı"
            }
            if points.count > 1, let pieceBounds {
                if bounds == nil { bounds = pieceBounds }
                else { bounds?.include(pieceBounds) }
            }
            pieces.append(Piece(name: raw["Name"] as? String, points: points, bounds: pieceBounds, closed: closed,
                                role: metadata?["Role"] as? String, size: metadata?["Size"] as? String,
                                material: metadata?["Material"] as? String, quantity: quantity,
                                grainStatus: grainStatus, grain: parsedGrain))
        }
        let formatNotice = schemaVersion == 9
            ? "Bu dosya bilgisayardaki programın yeni biçimiyle (sürüm 9) kaydedilmiş. Burada yalnızca parçaların çizgileri gösterilir; eğriler düzenlenemez ve denetlenemez. Kesmeden önce dosyayı bilgisayarda açıp kontrol et."
            : schemaVersion >= 6
            ? "Bu dosya bilgisayardaki programın yeni biçimiyle (sürüm \(schemaVersion)) kaydedilmiş. Burada yalnızca parçaların çizgileri gösterilir; çentikler, kesim yerleşimi ve katmanlar gösterilmez. Kesmeden önce dosyayı bilgisayarda açıp kontrol et."
            : nil
        let editableDraft = CADSketchImport.loadEditableCopy(from: data, jsonRoot: root)
        return Drawing(name: name, pieces: pieces, bounds: bounds, formatNotice: formatNotice,
                       editableDraft: editableDraft)
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
            (query.isEmpty || pieces[index].displayName(index: index).localizedCaseInsensitiveContains(query))
        }
    }

    var body: some View {
        NavigationStack {
            List {
                selectionRow(title: "Tüm parçalar", index: nil)
                ForEach(matchingIndices, id: \.self) { index in
                    selectionRow(title: pieces[index].displayName(index: index), index: index)
                }
                if matchingIndices.isEmpty && !search.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
                    Text("Bu adla bir parça yok.")
                        .foregroundStyle(AppTheme.muted)
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
        .tint(AppTheme.accent)
    }

    private func selectionRow(title: String, index: Int?) -> some View {
        Button {
            selection = index
            dismiss()
        } label: {
            HStack {
                Text(title).foregroundStyle(AppTheme.ink)
                Spacer()
                if selection == index {
                    Image(systemName: "checkmark").foregroundStyle(AppTheme.accent)
                }
            }
            .frame(maxWidth: .infinity, minHeight: 52, alignment: .leading)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .accessibilityAddTraits(selection == index ? .isSelected : [])
    }
}
private struct PieceDetails: View {
    let piece: Drawing.Piece

    var body: some View {
        if let bounds = piece.bounds {
            VStack(alignment: .leading, spacing: 4) {
                Text("Parça bilgisi").font(.headline).foregroundStyle(AppTheme.ink)
                Text("\(piece.points.count) nokta • \(piece.closed ? "Çevresi kapalı parça" : "Açık çizgi (uçlar birleşmiyor)")")
                if let role = piece.role, !role.isEmpty { Text("Rol: \(role)") }
                if let size = piece.size, !size.isEmpty { Text("Beden: \(size)") }
                if let material = piece.material, !material.isEmpty { Text("Malzeme: \(material)") }
                if let quantity = piece.quantity { Text("Adet: \(quantity)") }
                Text(piece.grainStatus)
                Text("En: \((bounds.maxX - bounds.minX).formatted(.number.precision(.fractionLength(0...2)))) • Boy: \((bounds.maxY - bounds.minY).formatted(.number.precision(.fractionLength(0...2))))")
                Text("En ve boy dosyadaki sayılarla gösterilir; birimi (mm, cm) dosyayı hazırlayan programa bağlıdır.")
                    .foregroundStyle(AppTheme.muted)
                    .fixedSize(horizontal: false, vertical: true)
            }
            .font(.subheadline)
            .foregroundStyle(AppTheme.ink)
            .appCard()
            .accessibilityElement(children: .combine)
        }
    }
}
private struct DrawingView: View {
    let pieces: [Drawing.Piece]
    let bounds: Drawing.Bounds?
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
                        .accessibilityHint("Yakınlaştırmak için iki parmağını aç; çizimi taşımak için sürükle.")
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
            // Controls sit below the drawing so they never hide pattern lines.
            ViewThatFits(in: .horizontal) {
                HStack(spacing: 8) { viewerButtons }
                VStack(alignment: .leading, spacing: 8) { viewerButtons }
            }
            .buttonStyle(.appSecondary)
        }
    }

    @ViewBuilder
    private var viewerButtons: some View {
        Button("Görünümü sıfırla") { zoom = 1; offset = .zero }
        if pieces.contains(where: { $0.grain != nil }) {
            Button(showGrain ? "Kumaş yönünü gizle" : "Kumaş yönünü göster") { showGrain.toggle() }
        }
    }

    private var drawing: some View {
        Canvas { context, size in
            guard let bounds else { return }
            let width = bounds.maxX - bounds.minX, height = bounds.maxY - bounds.minY
            guard width > 0 || height > 0 else { return }
            let horizontalScale = width > 0 ? max(Double(size.width) - 32, 1) / max(width, 1e-100) : .infinity
            let verticalScale = height > 0 ? max(Double(size.height) - 32, 1) / max(height, 1e-100) : .infinity
            let scale = min(horizontalScale, verticalScale)
            let offsetX = (Double(size.width) - width * scale) / 2
            let offsetY = (Double(size.height) - height * scale) / 2
            func position(_ p: Drawing.Point) -> CGPoint {
                CGPoint(x: offsetX + (p.x - bounds.minX) * scale, y: Double(size.height) - offsetY - (p.y - bounds.minY) * scale)
            }
            for piece in pieces {
                guard let first = piece.points.first else { continue }
                var path = Path()
                path.move(to: position(first))
                for point in piece.points.dropFirst() { path.addLine(to: position(point)) }
                if piece.closed && piece.points.count > 1 { path.closeSubpath() }
                context.stroke(path, with: .color(AppTheme.accent), lineWidth: 1.5)
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
                    context.stroke(grainPath, with: .color(grain.needsReview ? .orange : AppTheme.ink),
                                   style: StrokeStyle(lineWidth: 1.5, dash: grain.needsReview ? [4, 3] : []))
                }
            }
        }
    }
}
