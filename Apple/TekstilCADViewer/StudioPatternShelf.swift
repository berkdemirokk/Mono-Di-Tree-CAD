import SwiftUI

enum StudioPattern: String, CaseIterable, Identifiable {
    case apron = "Önlük"
    case skirt = "Etek"
    case tote = "Bez çanta"

    var id: Self { self }

    static let apronWidthMM = 300.0...1200.0
    static let apronLengthMM = 350.0...1500.0

    /// Finished body outline in millimetres, before seam allowances. The separate pocket keeps its size.
    static func makeApron(width: Double, length: Double) -> DraftProject? {
        guard apronWidthMM.contains(width), apronLengthMM.contains(length) else { return nil }
        return apronProject(width: width, length: length)
    }

    private static func apronProject(width: Double, length: Double) -> DraftProject {
        let outline: [(Double, Double)] = [(170,0),(430,0),(452,115),(490,192),(544,246),(600,270),
                                            (600,810),(0,810),(0,270),(56,246),(110,192),(148,115)]
        let body = DraftPiece(name: "Gövde", closed: true, points: outline.map {
            DraftPoint(x: $0.0 * (width / 600), y: $0.1 * (length / 810))
        })
        let pocketOutline: [(Double, Double)] = [(0,0),(240,0),(240,160),(216,184),(24,184),(0,160)]
        let pocket = DraftPiece(name: "Cep", closed: true, points: pocketOutline.map {
            DraftPoint(x: width + 60 + $0.0, y: 430 * (length / 810) + $0.1)
        })
        return DraftProject(name: StudioPattern.apron.rawValue, pieces: [body, pocket])
    }

    /// Starter pieces use millimetres. Each opening creates independent document and piece IDs.
    func makeProject() -> DraftProject {
        func piece(_ name: String, _ vertices: [(Double, Double)]) -> DraftPiece {
            DraftPiece(name: name, closed: true, points: vertices.map { DraftPoint(x: $0.0, y: $0.1) })
        }
        let pieces: [DraftPiece]
        switch self {
        case .apron:
            return Self.apronProject(width: 600, length: 810)
        case .skirt:
            pieces = [
                piece("Ön panel", [(78,0),(258,0),(284,140),(336,580),(0,580),(52,140)]),
                piece("Arka panel", [(462,0),(648,0),(680,140),(728,580),(382,580),(434,140)]),
                piece("Kemer", [(0,640),(728,640),(728,704),(0,704)])
            ]
        case .tote:
            pieces = [
                piece("Ön yüz", [(0,0),(360,0),(360,410),(0,410)]),
                piece("Arka yüz", [(392,0),(752,0),(752,410),(392,410)]),
                piece("Ön sap", [(0,444),(610,444),(610,504),(0,504)]),
                piece("Arka sap", [(0,534),(610,534),(610,594),(0,594)])
            ]
        }
        return DraftProject(name: rawValue, pieces: pieces)
    }
}

struct StudioPatternPreview: View {
    let project: DraftProject

    var body: some View {
        Canvas { context, size in
            let points = project.pieces.flatMap(\.points)
            guard let minX = points.map(\.x).min(), let maxX = points.map(\.x).max(),
                  let minY = points.map(\.y).min(), let maxY = points.map(\.y).max() else { return }
            let scale = min((size.width - 32) / max(1, maxX - minX),
                            (size.height - 20) / max(1, maxY - minY))
            guard scale > 0 else { return }
            let left = (size.width - (maxX - minX) * scale) / 2
            let top = (size.height - (maxY - minY) * scale) / 2
            for piece in project.pieces {
                var outline = Path()
                for (i, point) in piece.points.enumerated() {
                    let p = CGPoint(x: left + (point.x - minX) * scale, y: top + (point.y - minY) * scale)
                    if i == 0 { outline.move(to: p) } else { outline.addLine(to: p) }
                }
                outline.closeSubpath()
                context.fill(outline, with: .color(Color(red: 0.84, green: 0.91, blue: 0.82)))
                context.stroke(outline, with: .color(.white.opacity(0.8)), lineWidth: 1)
            }
        }
        .accessibilityLabel("\(project.name) kalıp önizlemesi")
        .accessibilityValue(project.pieces.map(\.name).joined(separator: ", "))
    }
}

struct StudioPatternShelf: View {
    @State private var selected: StudioPattern = .apron
    @State private var showingPreview = false
    @State private var editingSize = false
    @State private var widthCM = "60"
    @State private var lengthCM = "81"
    @FocusState private var sizeField: Dimension?
    private enum Dimension: Hashable { case width, length }
    let open: (DraftProject) -> Void

    private var selectedProject: DraftProject? {
        guard selected == .apron else { return selected.makeProject() }
        func millimetres(_ cm: String) -> Double? {
            let decimal = cm.trimmingCharacters(in: .whitespacesAndNewlines)
                .replacingOccurrences(of: ",", with: ".")
            return Double(decimal).map { $0 * 10 }
        }
        guard let width = millimetres(widthCM), let length = millimetres(lengthCM) else { return nil }
        return StudioPattern.makeApron(width: width, length: length)
    }

    private func dimensionField(_ label: String, text: Binding<String>, dimension: Dimension,
                                id: String) -> some View {
        VStack(alignment: .leading, spacing: 4) {
            Text(label).font(.subheadline)
            TextField(label, text: text)
                .textFieldStyle(.roundedBorder)
                #if os(iOS)
                .keyboardType(.decimalPad)
                #endif
                .focused($sizeField, equals: dimension)
                .onSubmit { sizeField = nil }
                .accessibilityLabel(label)
                .accessibilityIdentifier(id)
        }
    }

    private var measurements: some View {
        VStack(alignment: .leading, spacing: 10) {
            Button {
                editingSize.toggle()
                if !editingSize { sizeField = nil }
            } label: {
                Label("Bitmiş önlük ölçüleri", systemImage: "ruler")
                    .frame(minHeight: 44)
            }
            .accessibilityIdentifier("studio-apron-measurements")
            .accessibilityValue(editingSize ? "Açık" : "Kapalı")
            if editingSize {
                dimensionField("Gövde eni (cm)", text: $widthCM, dimension: .width, id: "studio-apron-width-cm")
                dimensionField("Göğüsten eteğe boy (cm)", text: $lengthCM, dimension: .length, id: "studio-apron-length-cm")
                Text("Dikiş payı hariç. Cep 24 × 18,4 cm.")
                    .font(.footnote)
            }
        }
        #if os(iOS)
        .toolbar {
            ToolbarItemGroup(placement: .keyboard) {
                if sizeField != nil {
                    Spacer()
                    Button("Bitti") { sizeField = nil }
                        .accessibilityIdentifier("studio-apron-finish-input")
                }
            }
        }
        #endif
    }

    var body: some View {
        let project = selectedProject
        VStack(alignment: .leading, spacing: 12) {
            Text("Hazır modeller").font(.title3.bold()).accessibilityAddTraits(.isHeader)
            Picker("Model", selection: $selected) {
                ForEach(StudioPattern.allCases) { model in Text(model.rawValue).tag(model) }
            }
            .pickerStyle(.segmented)
            if selected == .apron { measurements }
            if let project {
                modelCard(project)
            } else {
                Text("En için 30–120 cm, boy için 35–150 cm arasında bir ölçü girin.")
                    .font(.callout)
                    .foregroundStyle(AppTheme.warning)
            }
        }
        .sheet(isPresented: $showingPreview) {
            if let project { expandedPreview(project) }
        }
    }

    private func modelCard(_ project: DraftProject) -> some View {
        VStack(spacing: 4) {
            Button { showingPreview = true } label: {
                StudioPatternPreview(project: project)
                    .frame(height: 118)
                    .frame(maxWidth: .infinity)
                    .overlay(alignment: .topTrailing) {
                        Image(systemName: "arrow.up.left.and.arrow.down.right")
                            .foregroundStyle(.white)
                            .padding(10)
                    }
                    .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            .accessibilityLabel("\(project.name) kalıbını önizle")
            .accessibilityIdentifier("studio-pattern-preview")
            Button { sizeField = nil; open(project) } label: {
                HStack {
                    VStack(alignment: .leading) {
                        Text(project.name).font(.headline)
                        Text("\(project.pieces.count) parça").font(.caption.monospacedDigit())
                    }
                    Spacer()
                    Image(systemName: "arrow.up.right").font(.headline)
                }
                .frame(minHeight: 44)
                .padding(.horizontal, 14)
                .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            .foregroundStyle(.white)
            .accessibilityLabel("\(project.name) modelini aç")
            .accessibilityIdentifier("studio-pattern-open")
        }
        .padding(.vertical, 8)
        .background(Color(red: 0.06, green: 0.22, blue: 0.20), in: RoundedRectangle(cornerRadius: 14))
    }

    private func expandedPreview(_ project: DraftProject) -> some View {
        VStack(alignment: .leading, spacing: 16) {
            HStack {
                Text(project.name).font(.title.bold())
                Spacer()
                Button("Kapat") { showingPreview = false }
                    .frame(minHeight: 44)
            }
            GarmentTracePreview(project: project)
        }
        .padding()
        .appAppearance()
    }
}
