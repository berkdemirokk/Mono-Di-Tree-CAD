import SwiftUI

/// Creates independent draft contours in a fixed 300 × 300 mm workspace.
/// This is not a pattern-digitization or production-certified tracing tool.
struct TouchContourComposer: View {
    @Environment(\.dismiss) private var dismiss
    let onCreate: (DraftPiece) -> Void

    @State private var name = "Yeni şekil"
    @State private var closed = true
    @State private var snapToFive = true
    @State private var points: [DraftPoint] = []
    @State private var error: String?
    @State private var confirmingDiscard = false
    @State private var tappedOutside = false

    private let extent = 300.0
    private let padding = 14.0

    private func transform(_ size: CGSize) -> DraftCanvasMap? {
        DraftCanvasMap(size: size, minX: 0, maxX: extent, minY: 0, maxY: extent, inset: padding)
    }

    private func appendPoint(at location: CGPoint, size: CGSize) {
        guard points.count < DraftContourGeometry.pointLimit else {
            error = "Tek bir şekle en fazla 250 nokta ekleyebilirsiniz."
            return
        }
        guard let p = transform(size)?.sketchPoint(at: location, extent: extent, snapToFive: snapToFive) else {
            tappedOutside = true
            return
        }
        tappedOutside = false
        if points.last == p {
            error = "Bu nokta bir önceki noktayla aynı. Farklı bir yere dokunun."
            return
        }
        points.append(p)
    }

    private func finish() {
        let clean = name.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !clean.isEmpty, clean.count <= 128 else {
            error = "Şekle 1–128 karakterlik bir ad verin."
            return
        }
        if let message = DraftContourGeometry.validationError(points: points, closed: closed) {
            error = message
            return
        }
        onCreate(DraftPiece(name: clean, closed: closed, points: points))
        dismiss()
    }

    var body: some View {
        NavigationStack {
            ScrollView {
                VStack(alignment: .leading, spacing: 14) {
                    Text("Şeklin köşelerine sırayla dokun. Her dokunuş bir köşe noktası ekler.")
                        .font(.subheadline)
                    Text("Sabit çizim alanı: 300 × 300 mm. Her dokunuş 1 noktadır.")
                        .font(.caption).foregroundStyle(AppTheme.muted)

                    TextField("Şekil adı", text: $name)
                        .textFieldStyle(.roundedBorder)
                        .accessibilityLabel("Yeni şeklin adı")
                    Toggle("Köşeleri birleştir (kapalı şekil)", isOn: $closed)
                    Toggle("5 mm'lik adımlarla yerleştir", isOn: $snapToFive)

                    GeometryReader { geometry in
                        Canvas { context, size in
                            guard let t = transform(size) else { return }
                            let topLeft = t.screen(DraftPoint(x: 0, y: extent))
                            let left = topLeft.x, top = topLeft.y
                            let edge = t.screen(DraftPoint(x: extent, y: 0)).x - left
                            var border = Path()
                            border.addRect(CGRect(x: left, y: top, width: edge, height: edge))
                            context.stroke(border, with: .color(.secondary.opacity(0.6)),
                                           lineWidth: 1)
                            for mm in stride(from: 0.0, through: extent, by: 25.0) {
                                let offset = mm / extent * edge
                                var grid = Path()
                                grid.move(to: CGPoint(x: left + offset, y: top))
                                grid.addLine(to: CGPoint(x: left + offset, y: top + edge))
                                grid.move(to: CGPoint(x: left, y: top + offset))
                                grid.addLine(to: CGPoint(x: left + edge, y: top + offset))
                                context.stroke(grid, with: .color(.secondary.opacity(0.12)),
                                               lineWidth: 0.75)
                            }
                            var contour = Path()
                            if let first = points.first {
                                contour.move(to: t.screen(first))
                                for p in points.dropFirst() {
                                    contour.addLine(to: t.screen(p))
                                }
                                if closed && points.count > 2 { contour.closeSubpath() }
                                context.stroke(contour, with: .color(.accentColor),
                                               style: StrokeStyle(lineWidth: 2.5, lineJoin: .round))
                            }
                            for p in points {
                                let at = t.screen(p)
                                let dot = Path(ellipseIn: CGRect(x: at.x - 4.5, y: at.y - 4.5,
                                                                 width: 9, height: 9))
                                context.fill(dot, with: .color(.accentColor))
                            }
                        }
                        .contentShape(Rectangle())
                        .gesture(SpatialTapGesture().onEnded { value in
                            appendPoint(at: value.location, size: geometry.size)
                        })
                    }
                    .frame(height: 310)
                    .background(Color.secondary.opacity(0.06),
                                in: RoundedRectangle(cornerRadius: 14))
                    .accessibilityLabel("Parmakla nokta ekleme tuvali")
                    .accessibilityHint("Çizim alanına dokunarak yeni köşe ekleyin. Konturu ekledikten sonra noktaların koordinatlarını düzenleyebilirsiniz.")

                    if tappedOutside {
                        Text("Dokunuşun çizim alanının dışında kaldı. Karenin içine dokun.")
                            .font(.caption).foregroundStyle(AppTheme.danger)
                    }
                    HStack {
                        Text("\(points.count) nokta")
                            .font(.subheadline.bold())
                        Spacer()
                        Button("Son noktayı sil") {
                            if !points.isEmpty { points.removeLast() }
                        }
                        .disabled(points.isEmpty)
                        .buttonStyle(.appSecondary)
                    }
                    if let last = points.last {
                        Text("Son nokta: X \(last.x.formatted()) mm · Y \(last.y.formatted()) mm")
                            .font(.caption).foregroundStyle(AppTheme.muted)
                    }

                    Button(action: finish) {
                        Label("Şekli çizime ekle", systemImage: "checkmark.circle.fill")
                            .frame(maxWidth: .infinity, minHeight: 48)
                    }
                    .buttonStyle(.appPrimary)
                    .disabled(points.count < (closed ? 3 : 2))
                    if points.count < (closed ? 3 : 2) {
                        Text("En az \(closed ? 3 : 2) nokta gerekli. Şu an \(points.count) nokta var.")
                            .font(.caption).foregroundStyle(AppTheme.muted)
                    }
                    Text("Yeni şekil taslağıdır. Kendi kendini kesen veya üretime uygun olmayan konturlar ayrıca kontrol edilmelidir.")
                        .font(.caption).foregroundStyle(AppTheme.muted)
                }
                .padding()
            }
            // Swiping the sheet down would also drop the points without asking.
            .interactiveDismissDisabled(!points.isEmpty)
            .confirmationDialog("Çizdiğin \(points.count) nokta silinsin mi?",
                                isPresented: $confirmingDiscard, titleVisibility: .visible) {
                Button("Noktaları sil ve çık", role: .destructive) { dismiss() }
                Button("Çizmeye devam et", role: .cancel) {}
            }
            .navigationTitle("Parmakla çiz")
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Vazgeç") {
                        // Up to 250 points can be lost with one tap, so ask first.
                        if points.isEmpty { dismiss() } else { confirmingDiscard = true }
                    }
                }
            }
        }
        .alert("Şekil eklenemedi", isPresented: Binding(
            get: { error != nil }, set: { if !$0 { error = nil } }
        )) {
            Button("Tamam", role: .cancel) { error = nil }
        } message: {
            Text(error ?? "")
        }
    }
}
