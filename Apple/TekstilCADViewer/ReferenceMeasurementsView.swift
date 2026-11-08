import Foundation
import SwiftUI

/// Transcription of the first supplied chart, in centimetres. Never applies values to a project.
struct ReferenceMeasurements {
    struct Row: Identifiable {
        let id: String
        let turkish: String
        let english: String
        let values: [Double]

        func label(english useEnglish: Bool) -> String { useEnglish ? english : turkish }
    }

    static let sizes = [36, 38, 40, 42, 44, 46, 48, 50, 52]
    static let rows: [Row] = [
        .init(id: "bust", turkish: "Göğüs", english: "Bust", values: [86,90,94,98,102,106,110,114,118]),
        .init(id: "waist", turkish: "Bel", english: "Waist", values: [66,70,74,78,82,86,90,94,98]),
        .init(id: "hips", turkish: "Basen", english: "Hips", values: [92,96,100,104,108,112,116,120,124]),
        .init(id: "total_dress_length", turkish: "Toplam elbise boyu", english: "Total dress length", values: [110,110,110,110,110,110,110,110,110]),
        .init(id: "bust_height", turkish: "Göğüs yüksekliği", english: "Bust height", values: [25,26,27,27.5,28,28,28,28,28]),
        .init(id: "front_bodice_length", turkish: "Beden ön uzunluk", english: "Front bodice length", values: [48,49,50,50,50,50,50,50,50]),
        .init(id: "back_bodice_length", turkish: "Beden arka uzunluk", english: "Back bodice length", values: [48,49,50,50,50,50,50,50,50]),
        .init(id: "shoulder", turkish: "Omuz", english: "Shoulder", values: [37,38,40,41,41.5,42,42.5,43,43.5]),
        .init(id: "arm_length", turkish: "Kol boyu", english: "Arm length", values: [60,61,62,62,62,62,62,62,62]),
        .init(id: "upper_arm", turkish: "Pazı", english: "Upper arm", values: [31,32,33,34,34.5,35,35.5,36,36.5]),
        .init(id: "neck", turkish: "Boyun", english: "Neck", values: [39,40,41,42,42.5,43,43.5,44,44.5]),
        .init(id: "wrist", turkish: "Bilek", english: "Wrist", values: [19,20,21,22,22.5,23,23.5,24,24.5])
    ]

    static func formatted(_ value: Double, english: Bool) -> String {
        value.formatted(.number.locale(Locale(identifier: english ? "en_US" : "tr_TR"))
            .precision(.fractionLength(0...1)).grouping(.never))
    }
}

struct ReferenceMeasurementsView: View {
    let english: Bool
    @Environment(\.dismiss) private var dismiss
    @ScaledMetric(relativeTo: .body) private var labelWidth = 180.0
    @ScaledMetric(relativeTo: .body) private var valueWidth = 64.0

    private func text(_ turkish: String, _ englishText: String) -> String {
        english ? englishText : turkish
    }

    var body: some View {
        NavigationStack {
            ScrollView(.vertical) {
                VStack(alignment: .leading, spacing: 16) {
                    Text(text("Beden ölçüleri · cm", "Size measurements · cm"))
                        .font(.title2.bold())
                        .accessibilityAddTraits(.isHeader)
                    Text(text(
                        "Kaynak: paylaşılan ilk BEDEN ÖLÇÜLERİ fotoğrafı. Tüm değerler santimetredir. 110 cm, elbisenin toplam boyudur; belden etek boyu değildir.",
                        "Source: the first shared BEDEN ÖLÇÜLERİ size chart photo. All values are in centimetres. 110 cm is the total dress length, not the length from waist to hem."))
                    Text(text(
                        "38 beden şeması ayrı bir referanstır; bu tabloya karıştırılmamıştır. Kol boyu değerleri kısa kolun bitmiş boyu olarak kullanılmamalıdır.",
                        "The size 38 diagram is a separate reference and has not been merged into this chart. Arm length values should not be used as the finished length of a short sleeve."))
                        .foregroundStyle(.secondary)
                    Text(text("Diğer bedenleri görmek için tabloyu yatay kaydırın.",
                              "Scroll the chart horizontally to see other sizes."))
                        .font(.callout)
                        .foregroundStyle(.secondary)
                    ScrollView(.horizontal) {
                        VStack(spacing: 0) {
                            HStack(spacing: 0) {
                                Text(text("Ölçü · cm", "Measurement · cm"))
                                    .frame(width: labelWidth, alignment: .leading)
                                ForEach(ReferenceMeasurements.sizes, id: \.self) { size in
                                    Text(String(size))
                                        .frame(width: valueWidth, alignment: .trailing)
                                        .accessibilityLabel(text("Beden \(size)", "Size \(size)"))
                                }
                            }
                            .font(.body.bold())
                            .padding(10)
                            .background(Color.secondary.opacity(0.12))
                            .accessibilityAddTraits(.isHeader)
                            ForEach(Array(ReferenceMeasurements.rows.enumerated()), id: \.element.id) { index, row in
                                HStack(spacing: 0) {
                                    Text(row.label(english: english))
                                        .frame(width: labelWidth, alignment: .leading)
                                        .fixedSize(horizontal: false, vertical: true)
                                    ForEach(ReferenceMeasurements.sizes.indices, id: \.self) { column in
                                        Text(ReferenceMeasurements.formatted(row.values[column], english: english))
                                            .monospacedDigit()
                                            .frame(width: valueWidth, alignment: .trailing)
                                            .accessibilityLabel("\(row.label(english: english)), \(text("beden", "size")) \(ReferenceMeasurements.sizes[column])")
                                            .accessibilityValue("\(ReferenceMeasurements.formatted(row.values[column], english: english)) \(text("santimetre", "centimetres"))")
                                    }
                                }
                                .padding(10)
                                .background(Color.secondary.opacity(index.isMultiple(of: 2) ? 0.04 : 0.09))
                            }
                        }
                        .font(.body)
                    }
                    .accessibilityIdentifier("referenceMeasurementsChart")
                    Text(text(
                        "Salt okunur referanstır; ölçü, prova veya üretim onayı değildir. Proje verileri değiştirilmez.",
                        "Read-only reference; not measurement, fit or production approval. Project data is unchanged."))
                        .font(.callout)
                        .foregroundStyle(.secondary)
                }
                .padding()
            }
            .navigationTitle(text("Referans beden ölçüleri", "Reference size measurements"))
            .toolbar {
                ToolbarItem(placement: .confirmationAction) {
                    Button(text("Kapat", "Close")) { dismiss() }
                }
            }
        }
    }
}
