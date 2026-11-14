import SwiftUI

/// Creates independent draft contours in a fixed 300 × 300 mm workspace.
/// This is not a pattern-digitization or production-certified tracing tool.
struct TouchContourComposer: View {
    @Environment(\.dismiss) private var dismiss
    let onCreate: (DraftPiece) -> Void

    @State private var name = "Yeni kontur"
    @State private var closed = true
    @State private var snapToFive = true
    @State private var points: [DraftPoint] = []
    @State private var error: String?

    private let extent = 300.0
    private let padding = 14.0
    private let pointLimit = 250

    private func transform(_ size: CGSize) -> (scale: Double, x: Double, y: Double)? {
        let available = min(Double(size.width), Double(size.height)) - 2 * padding
        guard available > 40 else { return nil }
        let scale = available / extent
        let x = (Double(size.width) - extent * scale) / 2
        let y = (Double(size.height) - extent * scale) / 2
        return (scale, x, y)
    }

    private func screenPoint(_ p: DraftPoint, size: CGSize) -> CGPoint {
        guard let t = transform(size) else { return .zero }
        return CGPoint(x: t.x + p.x * t.scale,
                       y: t.y + (extent - p.y) * t.scale)
    }

    private func appendPoint(at location: CGPoint, size: CGSize) {
        guard let t = transform(size), points.count < pointLimit else {
            error = "Tek bir kontura en fazla 250 nokta ekleyebilirsiniz."
            return
        }
        var x = (Double(location.x) - t.x) / t.scale
        var y = extent - (Double(location.y) - t.y) / t.scale
        guard x.isFinite, y.isFinite,
              x >= 0, x <= extent, y >= 0, y <= extent else { return }
        if snapToFive {
            x = (x / 5).rounded() * 5
            y = (y / 5).rounded() * 5
        } else {
            x = (x * 10).rounded() / 10
            y = (y * 10).rounded() / 10
        }
        let p = DraftPoint(x: x, y: y)
        if points.last == p {
            error = "Bu nokta bir önceki noktayla aynı. Farklı bir yere dokunun."
            return
        }
        points.append(p)
    }

    private func finish() {
        let clean = name.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !clean.isEmpty, clean.count <= 128 else {
            error = "Kontura 1–128 karakterlik bir ad verin."
            return
        }
        guard points.count >= (closed ? 3 : 2) else {
            error = closed ? "Kapalı bir kalıp için en az 3 nokta gerekli."
                : "Açık çizgi için en az 2 nokta gerekli."
            return
        }
        if closed {
            // Reject collinear/zero-area closed paths. This is only a basic
            // geometry safety check, not a self-intersection/production test.
            var twiceArea = 0.0
            for i in points.indices {
                let a = points[i], b = points[(i + 1) % points.count]
                twiceArea += a.x * b.y - b.x * a.y
            }
            guard abs(twiceArea) > 1e-6 else {
                error = "Kalıp kapalı bir alan oluşturmuyor. Noktaları gözden geçirin."
                return
            }
        }
        onCreate(DraftPiece(name: clean, closed: closed, points: points))
        dismiss()
    }

    var body: some View {
        NavigationStack {
            ScrollView {
                VStack(alignment: .leading, spacing: 14) {
                    Text("Kalıbın köşelerini çizim alanına sırayla dokunarak yerleştir.")
                        .font(.subheadline)
                    Text("Sabit çizim alanı: 300 × 300 mm. Her dokunuş 1 noktadır.")
                        .font(.caption).foregroundStyle(.secondary)

                    TextField("Kontur adı", text: $name)
                        .textFieldStyle(.roundedBorder)
                        .accessibilityLabel("Yeni konturun adı")
                    Toggle("Kapalı kalıp oluştur", isOn: $closed)
                    Toggle("Noktaları 5 mm'ye hizala", isOn: $snapToFive)

                    GeometryReader { geometry in
                        Canvas { context, size in
                            guard let t = transform(size) else { return }
                            let left = t.x, top = t.y, edge = extent * t.scale
                            var border = Path()
                            border.addRect(CGRect(x: left, y: top, width: edge, height: edge))
                            context.stroke(border, with: .color(.secondary.opacity(0.6)),
                                           lineWidth: 1)
                            for mm in stride(from: 0.0, through: extent, by: 25.0) {
                                let offset = mm * t.scale
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
                                contour.move(to: screenPoint(first, size: size))
                                for p in points.dropFirst() {
                                    contour.addLine(to: screenPoint(p, size: size))
                                }
                                if closed && points.count > 2 { contour.closeSubpath() }
                                context.stroke(contour, with: .color(.accentColor),
                                               style: StrokeStyle(lineWidth: 2.5, lineJoin: .round))
                            }
                            for p in points {
                                let at = screenPoint(p, size: size)
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
                    .accessibilityHint("Çizim alanına dokunarak yeni köşe ekleyin. Sayısal giriş ve gelişmiş CAD hassasiyeti için masaüstü kullanın.")

                    HStack {
                        Text("\(points.count) nokta")
                            .font(.subheadline.bold())
                        Spacer()
                        Button("Son noktayı sil") {
                            if !points.isEmpty { points.removeLast() }
                        }
                        .disabled(points.isEmpty)
                        .buttonStyle(.bordered)
                    }
                    if let last = points.last {
                        Text("Son nokta: X \(last.x.formatted()) mm · Y \(last.y.formatted()) mm")
                            .font(.caption).foregroundStyle(.secondary)
                    }

                    Button(action: finish) {
                        Label("Konturu kalıba ekle", systemImage: "checkmark.circle.fill")
                            .frame(maxWidth: .infinity, minHeight: 48)
                    }
                    .buttonStyle(.borderedProminent)
                    .disabled(points.count < (closed ? 3 : 2))
                    Text("Yeni şekil taslağıdır. Kendi kendini kesen veya üretime uygun olmayan konturlar ayrıca kontrol edilmelidir.")
                        .font(.caption).foregroundStyle(.secondary)
                }
                .padding()
            }
            .navigationTitle("Parmakla çiz")
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Vazgeç") { dismiss() }
                }
            }
        }
        .alert("Kontur eklenemedi", isPresented: Binding(
            get: { error != nil }, set: { if !$0 { error = nil } }
        )) {
            Button("Tamam", role: .cancel) { error = nil }
        } message: {
            Text(error ?? "")
        }
    }
}
