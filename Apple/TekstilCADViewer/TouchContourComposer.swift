import SwiftUI
#if canImport(UIKit)
import UIKit
#endif

/// Sizes of the square drawing area and what happens to existing points when it changes.
/// Kept free of views so it can be unit-tested.
enum ComposerArea {
    /// Side lengths offered to the user, in mm.
    static let sizes: [Double] = [100, 300, 1000]
    static let defaultSize = 300.0

    static func contains(_ p: DraftPoint, extent: Double) -> Bool {
        p.x >= 0 && p.y >= 0 && p.x <= extent && p.y <= extent
    }

    static func outsideCount(_ points: [DraftPoint], extent: Double) -> Int {
        points.reduce(0) { $0 + (contains($1, extent: extent) ? 0 : 1) }
    }

    /// Drops the points outside the area. Neighbours that end up on the same spot are merged.
    static func removingOutside(_ points: [DraftPoint], extent: Double) -> [DraftPoint] {
        mergingRepeats(points.filter { contains($0, extent: extent) })
    }

    /// Moves the points outside the area onto its nearest edge. Neighbours that end up on the
    /// same spot are merged, so the result never has two equal points in a row.
    static func clamped(_ points: [DraftPoint], extent: Double) -> [DraftPoint] {
        mergingRepeats(points.map {
            DraftPoint(x: min(extent, max(0, $0.x)), y: min(extent, max(0, $0.y)))
        })
    }

    static func mergingRepeats(_ points: [DraftPoint]) -> [DraftPoint] {
        var result: [DraftPoint] = []
        result.reserveCapacity(points.count)
        for p in points where result.last != p {
            result.append(p)
        }
        return result
    }

    /// Spacing of the light grid lines, in mm.
    static func gridStep(extent: Double) -> Double {
        switch extent {
        case ...150: return 10
        case ...500: return 25
        default: return 100
        }
    }

    /// Spacing of the numbers on the edges (and of the slightly darker grid lines), in mm.
    static func labelStep(extent: Double) -> Double {
        switch extent {
        case ...150: return 25
        case ...500: return 100
        default: return 250
        }
    }

    /// "300 × 300 mm".
    static func name(_ extent: Double) -> String {
        let side = Int(extent.rounded())
        return "\(side) × \(side) mm"
    }
}

/// Creates independent draft contours in a square workspace of 100, 300 or 1000 mm.
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
    @State private var confirmingClear = false
    @State private var tappedOutside = false
    @State private var extent = ComposerArea.defaultSize
    /// An area size the user picked while some points would not fit in it. The area changes
    /// only after they say what to do with those points.
    @State private var pendingExtent: Double?

    /// Room around the square for the millimetre numbers on its edges.
    private let inset = 34.0
    private let canvasHeight: CGFloat = 340

    private func transform(_ size: CGSize) -> DraftCanvasMap? {
        DraftCanvasMap(size: size, minX: 0, maxX: extent, minY: 0, maxY: extent, inset: inset)
    }

    private func announce(_ message: String) {
        #if os(iOS)
        UIAccessibility.post(notification: .announcement, argument: message)
        #endif
    }

    private func appendPoint(at location: CGPoint, size: CGSize) {
        guard points.count < DraftContourGeometry.pointLimit else {
            error = "Tek bir şekle en fazla 250 nokta ekleyebilirsiniz."
            return
        }
        guard let p = transform(size)?.sketchPoint(at: location, extent: extent, snapToFive: snapToFive) else {
            tappedOutside = true
            announce("Dokunuş çizim alanının dışında kaldı.")
            return
        }
        tappedOutside = false
        if points.last == p {
            error = "Bu nokta bir önceki noktayla aynı. Farklı bir yere dokunun."
            return
        }
        points.append(p)
        Haptics.selection()
        announce("\(points.count). nokta: X \(p.x.formatted()), Y \(p.y.formatted()) mm")
    }

    private func removeLastPoint() {
        guard !points.isEmpty else { return }
        points.removeLast()
        announce("Son nokta silindi. \(points.count) nokta kaldı.")
    }

    private func clearAll() {
        let count = points.count
        points.removeAll()
        tappedOutside = false
        announce("\(count) nokta silindi. Çizim alanı boş.")
    }

    private func requestArea(_ size: Double) {
        guard size != extent else { return }
        if ComposerArea.outsideCount(points, extent: size) == 0 {
            applyArea(size, points: points, note: nil)
        } else {
            // Never drop or move points silently: ask first.
            pendingExtent = size
        }
    }

    private func applyArea(_ size: Double, points newPoints: [DraftPoint], note: String?) {
        points = newPoints
        extent = size
        tappedOutside = false
        var message = "Çizim alanı \(ComposerArea.name(size)) oldu."
        if let note { message += " " + note }
        announce(message)
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

    // MARK: Drawing

    private func label(_ text: String) -> Text {
        Text(text).font(.caption2.monospacedDigit()).foregroundColor(AppTheme.muted)
    }

    private func draw(in context: inout GraphicsContext, size: CGSize) {
        guard let t = transform(size) else { return }
        let origin = t.screen(DraftPoint(x: 0, y: 0))
        let far = t.screen(DraftPoint(x: extent, y: extent))
        let area = CGRect(x: origin.x, y: far.y, width: far.x - origin.x, height: origin.y - far.y)
        context.fill(Path(area), with: .color(AppTheme.card))

        let minor = ComposerArea.gridStep(extent: extent)
        let major = ComposerArea.labelStep(extent: extent)
        var minorLines = Path()
        var majorLines = Path()
        for mm in stride(from: minor, to: extent, by: minor) {
            let x = t.screen(DraftPoint(x: mm, y: 0)).x
            let y = t.screen(DraftPoint(x: 0, y: mm)).y
            let isMajor = abs((mm / major).rounded() * major - mm) < 1e-6
            if isMajor {
                majorLines.move(to: CGPoint(x: x, y: area.minY))
                majorLines.addLine(to: CGPoint(x: x, y: area.maxY))
                majorLines.move(to: CGPoint(x: area.minX, y: y))
                majorLines.addLine(to: CGPoint(x: area.maxX, y: y))
            } else {
                minorLines.move(to: CGPoint(x: x, y: area.minY))
                minorLines.addLine(to: CGPoint(x: x, y: area.maxY))
                minorLines.move(to: CGPoint(x: area.minX, y: y))
                minorLines.addLine(to: CGPoint(x: area.maxX, y: y))
            }
        }
        context.stroke(minorLines, with: .color(AppTheme.line), lineWidth: 0.75)
        context.stroke(majorLines, with: .color(AppTheme.muted.opacity(0.35)), lineWidth: 0.75)
        context.stroke(Path(area), with: .color(AppTheme.muted.opacity(0.8)), lineWidth: 1)

        // Millimetre numbers: X along the bottom edge, Y along the left edge.
        for mm in stride(from: major, through: extent, by: major) {
            let x = t.screen(DraftPoint(x: mm, y: 0)).x
            context.draw(label("\(Int(mm.rounded()))"), at: CGPoint(x: x, y: area.maxY + 3), anchor: .top)
            let y = t.screen(DraftPoint(x: 0, y: mm)).y
            context.draw(label("\(Int(mm.rounded()))"), at: CGPoint(x: area.minX - 4, y: y), anchor: .trailing)
        }

        // The origin really is the bottom-left corner: X grows to the right, Y upwards.
        context.draw(label("0"), at: CGPoint(x: area.minX - 4, y: area.maxY + 3), anchor: .topTrailing)
        let originDot = Path(ellipseIn: CGRect(x: origin.x - 4.5, y: origin.y - 4.5, width: 9, height: 9))
        context.fill(originDot, with: .color(AppTheme.danger))
        context.draw(label("X →"), at: CGPoint(x: origin.x + 9, y: origin.y - 3), anchor: .bottomLeading)
        context.draw(label("↑ Y"), at: CGPoint(x: origin.x + 4, y: origin.y - 20), anchor: .bottomLeading)

        if let first = points.first {
            var contour = Path()
            contour.move(to: t.screen(first))
            for p in points.dropFirst() {
                contour.addLine(to: t.screen(p))
            }
            if closed && points.count > 2 { contour.closeSubpath() }
            context.stroke(contour, with: .color(AppTheme.accent),
                           style: StrokeStyle(lineWidth: 2.5, lineJoin: .round))
        }
        for p in points {
            let at = t.screen(p)
            let dot = Path(ellipseIn: CGRect(x: at.x - 4.5, y: at.y - 4.5, width: 9, height: 9))
            context.fill(dot, with: .color(AppTheme.accent))
        }
        if let last = points.last {
            let at = t.screen(last)
            let ring = Path(ellipseIn: CGRect(x: at.x - 8, y: at.y - 8, width: 16, height: 16))
            context.stroke(ring, with: .color(AppTheme.accent), lineWidth: 1.5)
        }
    }

    private var canvasValue: String {
        var value = "\(points.count) nokta. Alan \(ComposerArea.name(extent)). Sol alt köşe 0,0 noktasıdır."
        if let last = points.last {
            value += " Son nokta X \(last.x.formatted()), Y \(last.y.formatted()) mm."
        }
        return value
    }

    // MARK: Controls

    private var areaPicker: some View {
        VStack(alignment: .leading, spacing: 6) {
            Text("Çizim alanının boyutu")
                .font(.subheadline.weight(.semibold))
                .accessibilityAddTraits(.isHeader)
            ViewThatFits(in: .horizontal) {
                HStack(spacing: 8) { areaButtons }
                VStack(spacing: 8) { areaButtons }
            }
        }
        .confirmationDialog("Bazı noktalar yeni alana sığmıyor",
                            isPresented: Binding(get: { pendingExtent != nil },
                                                 set: { if !$0 { pendingExtent = nil } }),
                            titleVisibility: .visible,
                            presenting: pendingExtent) { size in
            let outside = ComposerArea.outsideCount(points, extent: size)
            Button("Dışarıdaki \(outside) noktayı sil", role: .destructive) {
                let kept = ComposerArea.removingOutside(points, extent: size)
                applyArea(size, points: kept, note: "\(points.count - kept.count) nokta silindi.")
            }
            Button("Noktaları alanın kenarına taşı") {
                let moved = ComposerArea.clamped(points, extent: size)
                let merged = points.count - moved.count
                applyArea(size, points: moved,
                          note: merged > 0 ? "Noktalar kenara taşındı, üst üste gelen \(merged) nokta birleşti."
                                           : "Dışarıdaki noktalar kenara taşındı.")
            }
            Button("Alanı değiştirme", role: .cancel) {}
        } message: { size in
            Text("\(ComposerArea.outsideCount(points, extent: size)) nokta \(ComposerArea.name(size)) alanın dışında kalıyor. Alan değişmeden önce bu noktalara ne olacağını seç.")
        }
    }

    @ViewBuilder private var areaButtons: some View {
        ForEach(ComposerArea.sizes, id: \.self) { size in
            areaButton(size)
        }
    }

    private func areaButton(_ size: Double) -> some View {
        let selected = size == extent
        return Button {
            requestArea(size)
        } label: {
            HStack(spacing: 4) {
                if selected {
                    Image(systemName: "checkmark").accessibilityHidden(true)
                }
                Text("\(Int(size.rounded())) mm")
            }
            .frame(maxWidth: .infinity, minHeight: 44)
        }
        .buttonStyle(AreaChoiceButtonStyle(selected: selected))
        .accessibilityLabel("Çizim alanı \(ComposerArea.name(size))")
        .accessibilityAddTraits(selected ? AccessibilityTraits.isSelected : [])
        .accessibilityIdentifier("composer-area-\(Int(size.rounded()))")
    }

    private var undoButton: some View {
        Button(action: removeLastPoint) {
            Label("Son noktayı sil", systemImage: "arrow.uturn.backward")
                .frame(maxWidth: .infinity)
        }
        .disabled(points.isEmpty)
        .buttonStyle(.appSecondary)
    }

    private var clearButton: some View {
        Button(role: .destructive) {
            confirmingClear = true
        } label: {
            Label("Tümünü temizle", systemImage: "trash")
                .frame(maxWidth: .infinity)
        }
        .disabled(points.isEmpty)
        .buttonStyle(.appSecondary)
    }

    var body: some View {
        NavigationStack {
            ScrollView {
                VStack(alignment: .leading, spacing: 14) {
                    Text("Şeklin köşelerine sırayla dokun. Her dokunuş bir köşe noktası ekler.")
                        .font(.subheadline)

                    TextField("Şekil adı", text: $name)
                        .textFieldStyle(.roundedBorder)
                        .accessibilityLabel("Yeni şeklin adı")
                    Toggle("Köşeleri birleştir (kapalı şekil)", isOn: $closed)
                    Toggle("5 mm'lik adımlarla yerleştir", isOn: $snapToFive)

                    areaPicker

                    GeometryReader { geometry in
                        Canvas { context, size in
                            draw(in: &context, size: size)
                        }
                        // The edge numbers must fit in the margin around the square.
                        .dynamicTypeSize(...DynamicTypeSize.large)
                        .contentShape(Rectangle())
                        .gesture(SpatialTapGesture().onEnded { value in
                            appendPoint(at: value.location, size: geometry.size)
                        })
                    }
                    .frame(height: canvasHeight)
                    .background(AppTheme.page, in: RoundedRectangle(cornerRadius: 14))
                    .accessibilityElement(children: .ignore)
                    .accessibilityLabel("Parmakla nokta ekleme tuvali")
                    .accessibilityValue(canvasValue)
                    .accessibilityHint("Karenin içine dokunarak köşe noktası ekle. Parçayı çizime ekledikten sonra noktaların konumunu sayıyla düzenleyebilirsin.")
                    .accessibilityAddTraits(.allowsDirectInteraction)
                    .accessibilityIdentifier("composer-canvas")

                    Text("Alan \(ComposerArea.name(extent)). İnce çizgiler \(Int(ComposerArea.gridStep(extent: extent))) mm aralıklı; kenarlardaki sayılar milimetredir. Kırmızı nokta 0,0'dır (sol alt köşe): X sağa, Y yukarı doğru büyür.")
                        .font(.caption).foregroundStyle(AppTheme.muted)
                        .fixedSize(horizontal: false, vertical: true)

                    if tappedOutside {
                        Text("Dokunuşun çizim alanının dışında kaldı. Karenin içine dokun.")
                            .font(.caption).foregroundStyle(AppTheme.danger)
                    }
                    Text("\(points.count) nokta")
                        .font(.subheadline.bold())
                        .accessibilityIdentifier("composer-point-count")
                    ViewThatFits(in: .horizontal) {
                        HStack(spacing: 10) {
                            undoButton
                            clearButton
                        }
                        VStack(spacing: 10) {
                            undoButton
                            clearButton
                        }
                    }
                    .confirmationDialog("Çizdiğin \(points.count) noktanın hepsi silinsin mi?",
                                        isPresented: $confirmingClear, titleVisibility: .visible) {
                        Button("Tüm noktaları sil", role: .destructive) { clearAll() }
                        Button("Noktaları koru", role: .cancel) {}
                    } message: {
                        Text("Silinen noktalar geri getirilemez.")
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
                    Text("Bu bir taslaktır. Çizgileri birbirini kesen ya da üretime uygun olmayan parçaları ayrıca kontrol et.")
                        .font(.caption).foregroundStyle(AppTheme.muted)
                        .fixedSize(horizontal: false, vertical: true)
                }
                .padding()
            }
            .accessibilityIdentifier("composer-scroll")
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

/// One choice of the area-size row: filled when selected, outlined otherwise, at least 44 pt tall.
private struct AreaChoiceButtonStyle: ButtonStyle {
    let selected: Bool

    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .font(.body.weight(selected ? .semibold : .regular))
            .foregroundStyle(selected ? Color.white : AppTheme.accent)
            .padding(.horizontal, 12)
            .frame(minHeight: 44)
            .background(selected ? AppTheme.accent
                                 : (configuration.isPressed ? AppTheme.accentSoft : AppTheme.card),
                        in: RoundedRectangle(cornerRadius: 12))
            .overlay(RoundedRectangle(cornerRadius: 12)
                .stroke(AppTheme.accent.opacity(selected ? 1 : 0.35), lineWidth: 1))
            .contentShape(RoundedRectangle(cornerRadius: 12))
    }
}
