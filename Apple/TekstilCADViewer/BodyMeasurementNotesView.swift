import Foundation
import SwiftUI

/// User-provided measurement notes only; no pattern or body model is generated.
enum BodyMeasurementNotes {
    struct Field: Identifiable {
        let label: String
        let angle: Bool
        var id: String { label }
    }
    static let fields: [Field] = [
        .init(label: "üst kol çevresi", angle: false), .init(label: "ön göğüs", angle: false),
        .init(label: "göğüs noktaları arası", angle: false), .init(label: "üst göğüs çevresi", angle: false),
        .init(label: "ön üst göğüs", angle: false), .init(label: "omuz üst noktasından göğse", angle: false),
        .init(label: "omuz üst noktasından arka bele", angle: false), .init(label: "omuz üst noktasından ön bele", angle: false),
        .init(label: "bel çevresi", angle: false), .init(label: "belden kalçaya", angle: false),
        .init(label: "boyun çevresi", angle: false), .init(label: "omuzdan omuza", angle: false),
        .init(label: "omuz eğimi", angle: true), .init(label: "omuzdan bileğe", angle: false),
        .init(label: "bilek çevresi", angle: false)
    ]
    enum ValidationError: Error { case empty, whitespace(String), invalid(String), duplicate(String), ambiguous(String), tooLong }
    static func whitespaceOnlyIndices(_ values: [String]) -> [Int] {
        values.indices.filter { !values[$0].isEmpty && values[$0].trimmingCharacters(in: .whitespacesAndNewlines).isEmpty }
    }
    static func clearingWhitespaceOnly(_ values: [String]) -> [String] {
        var cleaned = values
        for index in whitespaceOnlyIndices(values) { cleaned[index] = "" }
        return cleaned
    }
    static func append(values: [String], unit: String, current: String, existingNotes: String, allowAmbiguous: Bool = false) throws -> String {
        guard values.count == fields.count, unit == "cm" || unit == "mm" else { throw ValidationError.tooLong }
        let separators = CharacterSet.newlines.union(CharacterSet(charactersIn: ";"))
        let segments = existingNotes.components(separatedBy: separators)
        let knownLabels = fields.map(\.label) + ["göğüs çevresi", "kalça çevresi", "elbise boyu"]
        var lines: [String] = []
        for (field, raw) in zip(fields, values) {
            let text = raw.trimmingCharacters(in: .whitespacesAndNewlines)
            if text.isEmpty {
                if !raw.isEmpty { throw ValidationError.whitespace(field.label) }
                continue
            }
            let regex = field.angle ? #"^-?[0-9]{1,5}([.,][0-9]{1,8})?$"# : #"^[0-9]{1,5}([.,][0-9]{1,8})?$"#
            guard text.range(of: regex, options: .regularExpression) != nil,
                  let value = Decimal(string: text.replacingOccurrences(of: ",", with: "."), locale: Locale(identifier: "en_US_POSIX")),
                  (field.angle ? (value > -90 && value < 90) : (value > 0 && value <= (unit == "cm" ? 1000 : 10000))) else { throw ValidationError.invalid(field.label) }
            // A semicolon or newline separates claims. A number in another
            // segment must not be attributed to this label; ambiguous content
            // inside the same segment is still handled conservatively.
            for (index, segment) in segments.enumerated() {
                guard let match = segment.range(of: field.label, options: [.caseInsensitive]) else { continue }
                let prefix = segment[..<match.lowerBound].trimmingCharacters(in: .whitespacesAndNewlines.union(.punctuationCharacters))
                let suffix = segment[match.upperBound...].trimmingCharacters(in: .whitespacesAndNewlines.union(.punctuationCharacters))
                if prefix.isEmpty && suffix.localizedCaseInsensitiveCompare("ölçülmedi") == .orderedSame {
                    if index + 1 < segments.count {
                        let next = segments[index + 1]
                        let numericMeasurement = next.range(of: #"(?i)(?<![\p{L}\p{N}_])[0-9]+(?:[.,][0-9]+)?[ \t]*(?:cm|mm|derece)(?![\p{L}\p{N}_])"#, options: .regularExpression) != nil
                        let hasKnownExplicitMeasurement = knownLabels.contains { label in
                            let pattern = #"(?i)\A[ \t]*"# + NSRegularExpression.escapedPattern(for: label) + #"[ \t]*(?::|=)?[ \t]*[0-9]+(?:[.,][0-9]+)?[ \t]*(?:cm|mm|derece)[ \t]*[.!]?[ \t]*\z"#
                            return next.range(of: pattern, options: .regularExpression) != nil
                        }
                        if numericMeasurement && !hasKnownExplicitMeasurement && !allowAmbiguous { throw ValidationError.ambiguous(field.label) }
                    }
                    continue
                }
                if segment.contains(where: { $0.isNumber }) { throw ValidationError.duplicate(field.label) }
                if !allowAmbiguous { throw ValidationError.ambiguous(field.label) }
            }
            lines.append("\(field.label) \(text) \(field.angle ? "derece" : unit)")
        }
        guard !lines.isEmpty else { throw ValidationError.empty }
        let addition = lines.joined(separator: "\n")
        let result = current + (current.isEmpty || current.hasSuffix("\n") ? "" : "\n") + addition
        guard result.utf16.count <= ExperimentalDesignBrief.maxTextUnits else { throw ValidationError.tooLong }
        return result
    }
}

struct BodyMeasurementNotesView: View {
    @Environment(\.dismiss) private var dismiss
    @Environment(\.colorScheme) private var colorScheme
    @Binding var notes: String
    @Binding var savedForm: ExperimentalDesignBrief.MeasurementForm?
    let measurementsWithNotes: (String) -> String
    let localSaveStatus: () -> String
    @State private var values = Array(repeating: "", count: BodyMeasurementNotes.fields.count)
    @FocusState private var focusedIndex: Int?
    @State private var unit = "cm"
    @State private var status = ""
    @State private var confirm = false
    @State private var discard = false
    @State private var ambiguousConfirm = false
    @State private var confirmWhitespaceCleanup = false
    @State private var ambiguousLabel = ""
    @State private var initialNotes: String?
    @State private var readyToSaveForm = false
    var body: some View {
        NavigationStack {
            Form {
                Section {
                    Text("Bildiğiniz ölçüleri girin; bilinmeyenleri boş bırakın. Uzunluklarda cm veya mm seçin; omuz eğimi derecedir. Bu form kalıp oluşturmaz. Girişler yalnız onayla notlara eklenir. Henüz eklenmemiş değerler yerel taslağa kaydedilmeye çalışılır; ani kapanışta son değişiklikler kaybolabilir.")
                    Text(localSaveStatus()).font(.caption).foregroundStyle(.secondary)
                    Picker("Uzunluk birimi", selection: $unit) { Text("cm").tag("cm"); Text("mm").tag("mm") }.pickerStyle(.segmented)
                    Text("Birim değişirse girdiğiniz sayılar dönüştürülmez; yeniden kontrol edin.").font(.caption)
                }
                Section("Beden ölçüleri") {
                    ForEach(BodyMeasurementNotes.fields.indices, id: \.self) { index in
                        let field = BodyMeasurementNotes.fields[index]
                        VStack(alignment: .leading) {
                            Text("\(field.label) (\(field.angle ? "derece" : unit))")
                            TextField("Bilinmiyorsa boş bırakın", text: $values[index])
                                .accessibilityLabel("\(field.label), \(field.angle ? "derece" : unit)")
                                .accessibilityHint(field.angle ? "Yataya göre açı girin, bilinmiyorsa boş bırakın." : "Pozitif ölçü girin, bilinmiyorsa boş bırakın.")
                                .focused($focusedIndex, equals: index).frame(minHeight: 44)
                        }
                    }
                }
                Section("Eklenecek değerler") {
                    ForEach(BodyMeasurementNotes.fields.indices, id: \.self) { index in
                        let entered = values[index].trimmingCharacters(in: .whitespacesAndNewlines)
                        if !entered.isEmpty {
                            let field = BodyMeasurementNotes.fields[index]
                            Text("\(field.label): \(entered) \(field.angle ? "derece" : unit)")
                        }
                    }
                    if values.allSatisfy({ $0.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty }) { Text("Henüz değer girilmedi.").foregroundStyle(.secondary) }
                }
                if !BodyMeasurementNotes.whitespaceOnlyIndices(values).isEmpty {
                    Text("Bazı alanlarda yalnız görünmeyen boşluk var. Ham giriş korunuyor ve dışa aktarmayı engeller; alanları inceleyin veya yalnız boşlukları temizleyin.")
                    ForEach(BodyMeasurementNotes.whitespaceOnlyIndices(values), id: \.self) { index in
                        Text("\(BodyMeasurementNotes.fields[index].label): yalnız boşluk")
                    }
                    Button("Yalnız boşluk içeren alanları temizle…") { confirmWhitespaceCleanup = true }.frame(minHeight: 44)
                }
                if !status.isEmpty { Text(status).accessibilityLabel("Ölçü uyarısı: \(status)") }
                Button("Girilenleri gözden geçir ve ekle") { confirm = true }.frame(minHeight: 44)
            }
            .tint(colorScheme == .dark ? Color(red: 1, green: 0.72, blue: 0.78) : Color(red: 0.29, green: 0.08, blue: 0.14))
            .toolbar { ToolbarItemGroup(placement: .keyboard) { Spacer(); Button("Klavyeyi kapat") { focusedIndex = nil } } }
            .navigationTitle("Beden ölçüleri")
            .interactiveDismissDisabled(true)
            .toolbar { ToolbarItem(placement: .cancellationAction) { Button("Vazgeç") { if values.contains(where: { !$0.isEmpty }) { discard = true } else { dismiss() } } } }
            .onAppear {
                initialNotes = notes
                if let form = savedForm, form.values.count == BodyMeasurementNotes.fields.count { values = form.values; unit = form.unit }
                readyToSaveForm = true
            }
            .onDisappear { rememberForm() }
            .onChange(of: values) { _ in rememberForm() }
            .onChange(of: unit) { _ in rememberForm() }
            .confirmationDialog("Girilen ölçüler notlara eklensin mi?", isPresented: $confirm, titleVisibility: .visible) {
                Button("Ölçüleri ekle") { apply() }
                Button("Düzenlemeye devam et", role: .cancel) { }
            } message: {
                Text((try? BodyMeasurementNotes.append(values: values, unit: unit, current: "", existingNotes: "")) ?? "Geçersiz veya boş giriş var; değerleri kontrol edin.")
                Text("Önceki notlar korunur. Aynı ölçü daha önce varsa çelişkiyi önce gözden geçirin.")
            }
            .confirmationDialog("Yalnız boşluk içeren alanlar temizlensin mi?", isPresented: $confirmWhitespaceCleanup, titleVisibility: .visible) {
                Button("Boşlukları temizle", role: .destructive) { values = BodyMeasurementNotes.clearingWhitespaceOnly(values) }
                Button("Vazgeç", role: .cancel) { }
            } message: { Text("Yalnız tamamen boşluk içeren alanlar temizlenir. Diğer ham ölçüler ve önceki notlar değiştirilmez.") }
            .confirmationDialog("Önceki not belirsiz", isPresented: $ambiguousConfirm, titleVisibility: .visible) {
                Button("Önceki notu koruyarak ekle") { apply(allowAmbiguous: true) }
                Button("Notu kontrol edeceğim", role: .cancel) { }
            } message: { Text("\(ambiguousLabel) önceki notlarda geçiyor. Çelişki olabilir; mevcut not silinmez. Devam etmeden önce kontrol edin.") }
            .confirmationDialog("Girilenler silinsin mi?", isPresented: $discard, titleVisibility: .visible) {
                Button("Formu taslakta tut ve çık") { rememberForm(); dismiss() }
                Button("Formu sil ve çık", role: .destructive) { savedForm = nil; readyToSaveForm = false; dismiss() }
                Button("Düzenlemeye devam et", role: .cancel) { }
            }
        }
    }
    private func rememberForm() {
        guard readyToSaveForm else { return }
        savedForm = ExperimentalDesignBrief.MeasurementForm.pending(values: values, unit: unit)
    }
    private func apply(allowAmbiguous: Bool = false) {
        guard notes == initialNotes else { status = "Notlar değişti. Önceki girişleri kaybetmemek için ekranı kapatıp yeniden açın."; return }
        do {
            let updated = try BodyMeasurementNotes.append(values: values, unit: unit, current: notes, existingNotes: measurementsWithNotes(notes), allowAmbiguous: allowAmbiguous)
            guard measurementsWithNotes(updated).utf16.count <= ExperimentalDesignBrief.maxTextUnits else { throw BodyMeasurementNotes.ValidationError.tooLong }
            readyToSaveForm = false; notes = updated; savedForm = nil; dismiss()
        } catch BodyMeasurementNotes.ValidationError.empty { status = "En az bir ölçü girin." }
        catch BodyMeasurementNotes.ValidationError.whitespace(let label) { status = "\(label) yalnız boşluk içeriyor. Alanı inceleyin veya onayla temizleyin." }
        catch BodyMeasurementNotes.ValidationError.invalid(let label) { status = "\(label): Pozitif uzunluk veya -90 ile 90 arasında (uçlar hariç) omuz açısı girin." }
        catch BodyMeasurementNotes.ValidationError.ambiguous(let label) { ambiguousLabel = label; ambiguousConfirm = true }
        catch BodyMeasurementNotes.ValidationError.duplicate(let label) { status = "\(label) önceki notlarda olabilir. Çelişkiyi çözmek için mevcut notları kontrol edin." }
        catch { status = "Ölçüler eklenemedi; metin sınırını kontrol edin. Önceki notlar korundu." }
    }
}
