import SwiftUI

struct PatternBriefStudio: View {
    let openEditor: (DraftProject) -> Void

    @Environment(\.dismiss) private var dismiss
    @FocusState private var writing: Bool
    @State private var brief = ""
    @State private var previous: DraftProject?
    @State private var project: DraftProject?
    @State private var file: URL?
    @State private var problem: String?
    @State private var printStatus: String?
    @StateObject private var printer = PatternPaperPrinter()

    private let markerExample = "önlük için 150 cm kumaşa 2 gövde ve 1 cep yerleştir ve plotter dosyası hazırla"
    private let example = "65 cm eninde 85 cm boyunda cepli bir önlük çiz ve plotterda bastır"

    var body: some View {
        NavigationStack {
            ScrollView {
                VStack(alignment: .leading, spacing: 20) {
                    VStack(alignment: .leading, spacing: 12) {
                        Text(previous == nil ? "Ne hazırlayalım?" : "Neyi değiştirelim?")
                            .font(.title2.bold())
                            .accessibilityAddTraits(.isHeader)
                        HStack(spacing: 8) {
                            TextField("Kalıp tarifin", text: $brief)
                                .textFieldStyle(.plain)
                                .font(.body)
                                .focused($writing)
                                .submitLabel(.go)
                                .onSubmit(submit)
                                .accessibilityIdentifier("brief-field")
                            Button(action: submit) {
                                Image(systemName: "arrow.up.circle.fill")
                                    .font(.title)
                                    .frame(minWidth: 44, minHeight: 44)
                            }
                            .buttonStyle(.plain)
                            .foregroundStyle(AppTheme.accent)
                            .disabled(brief.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
                            .accessibilityLabel("Tarifi uygula")
                            .accessibilityIdentifier("brief-submit")
                        }
                        .padding(.leading, 14)
                        .padding(.trailing, 3)
                        .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 15))
                        .overlay(RoundedRectangle(cornerRadius: 15).strokeBorder(.secondary.opacity(0.25)).allowsHitTesting(false))
                        if brief.isEmpty && previous == nil {
                            Button {
                                brief = example
                                writing = true
                            } label: {
                                Label(example, systemImage: "text.quote")
                                    .font(.subheadline)
                                    .multilineTextAlignment(.leading)
                                    .fixedSize(horizontal: false, vertical: true)
                                    .frame(minHeight: 44)
                            }
                            .buttonStyle(.borderless)
                            .accessibilityHint("Tarifi düzenleyebilirsin.")
                            Button {
                                brief = markerExample
                                writing = true
                            } label: {
                                Label("Pastal örneği · 150 cm kumaş, 2 gövde, 1 cep", systemImage: "square.grid.2x2")
                                    .font(.subheadline)
                                    .frame(minHeight: 44)
                            }
                            .buttonStyle(.borderless)
                            .accessibilityIdentifier("brief-marker-example")
                        } else if previous != nil && brief.isEmpty {
                            Text("“Cebi kaldır” veya “Plotterda bastır” yazabilirsin.")
                                .font(.subheadline)
                                .foregroundStyle(.secondary)
                        }
                    }
                    if let printStatus {
                        Text(printStatus).font(.callout).accessibilityIdentifier("brief-print-status")
                    }
                    if let problem {
                        Label(problem, systemImage: "exclamationmark.circle")
                            .foregroundStyle(AppTheme.danger)
                            .accessibilityIdentifier("brief-error")
                    }
                    if let project {
                        result(project)
                    }
                }
                .padding(20)
                .frame(maxWidth: 640)
                .frame(maxWidth: .infinity)
            }
            .background(AppTheme.page)
            .navigationTitle("Tarifle kalıp")
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Kapat") { dismiss() }
                }
            }
        }
        #if os(iOS)
        .background(PatternPrintAnchor(presenter: printer))
        #endif
        .onChange(of: brief) { _ in
            project = nil
            file = nil
            problem = nil
            printStatus = nil
        }
    }

    private func result(_ pattern: DraftProject) -> some View {
        VStack(alignment: .leading, spacing: 12) {
            StudioPatternPreview(project: pattern)
                .frame(height: 190)
                .frame(maxWidth: .infinity)
                .background(Color(red: 0.06, green: 0.22, blue: 0.20),
                            in: RoundedRectangle(cornerRadius: 18))
                .accessibilityIdentifier("brief-preview")
            HStack(alignment: .firstTextBaseline) {
                VStack(alignment: .leading, spacing: 3) {
                    Text(pattern.name).font(.title2.bold()).accessibilityAddTraits(.isHeader)
                    Text(pattern.pieces.map(\.name).joined(separator: " · "))
                        .font(.subheadline).foregroundStyle(.secondary)
                }
                Spacer()
                Button("Tarifle düzelt") {
                    brief = ""
                    writing = true
                }
                .frame(minHeight: 44)
                .accessibilityIdentifier("brief-correct")
            }
            Button { prepareFile(pattern, as: .plotter) } label: {
                Label("Plotter dosyası hazırla", systemImage: "doc.badge.arrow.up")
                    .frame(maxWidth: .infinity, minHeight: 44)
            }
            .buttonStyle(.borderedProminent)
            .accessibilityIdentifier("brief-plotter")
            Button { prepareFile(pattern, as: .paper) } label: {
                Label("A4 yazdır", systemImage: "printer")
                    .frame(maxWidth: .infinity, minHeight: 44)
            }
            .buttonStyle(.bordered)
            .accessibilityIdentifier("brief-print")
            if let file {
                ShareLink(item: file) {
                    Label("\(file.pathExtension.uppercased()) dosyasını paylaş", systemImage: "square.and.arrow.up")
                        .frame(maxWidth: .infinity, minHeight: 44)
                }
                .buttonStyle(.bordered)
                .accessibilityIdentifier("brief-share")
            } else {
                HStack {
                    Button("A4 PDF hazırla") { prepareFile(pattern, as: .pdf) }
                    Button("TCAD hazırla") { prepareFile(pattern, as: .tcad) }
                }
                .buttonStyle(.bordered)
            }
            Button {
                openEditor(pattern)
                dismiss()
            } label: {
                Label("Çizimde düzenle", systemImage: "pencil")
                    .frame(maxWidth: .infinity, minHeight: 44)
            }
            .buttonStyle(.bordered)
            .accessibilityIdentifier("brief-open-editor")
        }
    }

    private func submit() {
        writing = false
        file = nil
        project = nil
        problem = nil
        printStatus = nil
        do {
            let (pattern, format) = try PatternBrief.read(brief, previous: previous)
            previous = pattern
            project = pattern
            if let format { prepareFile(pattern, as: format) }
        } catch {
            problem = error.localizedDescription
        }
    }

    private func prepareFile(_ pattern: DraftProject, as format: PatternBrief.Output) {
        file = nil
        problem = nil
        printStatus = nil
        do {
            switch format {
            case .plotter:
                file = try PatternPlotterFile.export(pattern)
                printStatus = "PLT hazır · HP-GL, 40 birim/mm. Dosyayı plotter uygulamana aktar."
            case .paper:
                let url = try PatternBriefPDF.export(pattern)
                file = url
                printer.present(url, title: pattern.name) { message in printStatus = message }
            case .pdf:
                file = try PatternBriefPDF.export(pattern)
            case .tcad:
                let bytes = try CADSketchImport.encode(pattern)
                let folder = FileManager.default.temporaryDirectory
                    .appendingPathComponent(UUID().uuidString, isDirectory: true)
                try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
                let url = folder.appendingPathComponent(CADSketchImport.exportFileName(forProjectNamed: pattern.name))
                try bytes.write(to: url, options: .atomic)
                file = url
            }
        } catch {
            problem = "Dosya hazırlanamadı. \(error.localizedDescription)"
        }
    }
}
