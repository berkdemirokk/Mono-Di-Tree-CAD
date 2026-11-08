import Foundation
import SwiftUI
#if os(iOS)
import UIKit
#endif

private extension View {
    @ViewBuilder func measurementKeyboard(isAngle: Bool) -> some View {
        #if os(iOS)
        self.keyboardType(isAngle ? .numbersAndPunctuation : .decimalPad)
        #else
        self
        #endif
    }
}

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
    // Mirrors the local BreannaMeasurementGate definitions; these descriptions
    // are research guidance and have not been independently verified by a tailor.
    static let instructions: [String] = [
        "Üst kolun en geniş yerinden çevresini ölçün.",
        "Göğsün en dolgun yerinden, bir yan dikiş noktasından diğerine ön taraftan ölçün.",
        "İki göğüs noktası arasındaki mesafeyi ölçün.",
        "Göğsün üstünden geçen üst göğüs çevresini ölçün.",
        "Üst göğüs hattında yan dikiş noktaları arasında ön kısmı ölçün.",
        "Boyun ile omzun birleştiği omuz üst noktasından (HPS), göğüs noktalarından geçen göğüs çevresi hizasına doğru düz aşağı ölçün.",
        "Boyun dibindeki omuz üst noktasından sırt üzerinden bele ölçün.",
        "Boyun dibindeki omuz üst noktasından ön taraftan bele ölçün.",
        "Bel çevresini ölçün.",
        "Bedenin yan tarafında bel hattından kalça kemiğinin üst noktasına doğru aşağı ölçün.",
        "Boyun çevresini ölçün.",
        "Bir omuz ucundan sırt üzerinden diğer omuz ucuna ölçün.",
        "Omuz çizgisinin yataya göre aşağı eğimini dereceyle belirtin.",
        "Kol doğal şekilde hafif bükülü dururken omuz ucundan kol boyunca bileğe kadar ölçün.",
        "Bilek çevresini ölçün."
    ]
    struct FieldGroup {
        let title: String
        let indices: Range<Int>
    }
    static let groups: [FieldGroup] = [
        .init(title: "Kol ve göğüs", indices: 0..<5),
        .init(title: "Gövde ve bel", indices: 5..<10),
        .init(title: "Boyun, omuz ve kol", indices: 10..<15)
    ]
    struct FieldJumpLifetime {
        private(set) var token = UUID()
        private(set) var active = false
        mutating func appear() { token = UUID(); active = true }
        mutating func disappear() { active = false; token = UUID() }
        func permits(_ captured: UUID) -> Bool { active && token == captured }
    }
    static func fieldScrollID(_ index: Int) -> String { "measurement-field-\(index)" }
    static func groupIndex(forField index: Int) -> Int? {
        groups.firstIndex { $0.indices.contains(index) }
    }
    static func missingIndices(_ values: [String]) -> [Int] {
        fields.indices.filter { $0 >= values.count || values[$0].trimmingCharacters(in: .whitespacesAndNewlines).isEmpty }
    }
    enum ValidationError: Error { case empty, whitespace(String), invalid(String), duplicate(String), ambiguous(String), tooLong }
    struct AdditionReview {
        let form: ExperimentalDesignBrief.MeasurementForm
        let revision: UUID
        let notes: String
        let existingNotes: String
        let savedForm: ExperimentalDesignBrief.MeasurementForm?
        func matches(form: ExperimentalDesignBrief.MeasurementForm, revision: UUID, notes: String, existingNotes: String, savedForm: ExperimentalDesignBrief.MeasurementForm?) -> Bool {
            self.form == form && self.revision == revision && self.notes == notes && self.existingNotes == existingNotes && self.savedForm == savedForm
        }
    }
    struct UnitChange {
        let before: ExperimentalDesignBrief.MeasurementForm
        let after: ExperimentalDesignBrief.MeasurementForm
        let revision: UUID
        let notes: String
        let existingNotes: String
        func matches(form: ExperimentalDesignBrief.MeasurementForm, revision: UUID, notes: String, existingNotes: String) -> Bool {
            before == form && self.revision == revision && self.notes == notes && self.existingNotes == existingNotes
        }
    }
    private static func numericValue(_ raw: String, angle: Bool, unit: String) -> Decimal? {
        let text = raw.trimmingCharacters(in: .whitespacesAndNewlines)
        let pattern = angle ? #"\A-?[0-9]{1,5}(?:[.,][0-9]{1,8})?\z"# : #"\A[0-9]{1,5}(?:[.,][0-9]{1,8})?\z"#
        guard text.range(of: pattern, options: .regularExpression) != nil,
              text.range(of: #"\A-?[0-9]+[.,][0-9]{3}\z"#, options: .regularExpression) == nil,
              let value = Decimal(string: text.replacingOccurrences(of: ",", with: "."), locale: Locale(identifier: "en_US_POSIX")) else { return nil }
        if angle { return value > -90 && value < 90 ? value : nil }
        let mm = unit == "cm" ? value * 10 : value
        return mm > 0 && mm <= 10_000 ? mm : nil
    }
    static func convertUnit(_ form: ExperimentalDesignBrief.MeasurementForm, to target: String) throws -> ExperimentalDesignBrief.MeasurementForm {
        guard form.values.count == fields.count, ["cm", "mm"].contains(form.unit), ["cm", "mm"].contains(target) else { throw ValidationError.tooLong }
        if form.unit == target { return form }
        var values = form.values
        for index in fields.indices {
            let raw = form.values[index]
            if raw.isEmpty { continue }
            let field = fields[index]
            guard let original = numericValue(raw, angle: field.angle, unit: form.unit) else { throw ValidationError.invalid(field.label) }
            if field.angle { continue }
            let converted = target == "cm" ? original / 10 : original
            var text = NSDecimalNumber(decimal: converted).stringValue
            if let dot = text.firstIndex(of: "."), text.distance(from: text.index(after: dot), to: text.endIndex) == 3 { text += "0" }
            guard numericValue(text, angle: false, unit: target) == original else { throw ValidationError.invalid(field.label) }
            values[index] = text
        }
        return .init(values: values, unit: target)
    }
    static func whitespaceOnlyIndices(_ values: [String]) -> [Int] {
        values.indices.filter { !values[$0].isEmpty && values[$0].trimmingCharacters(in: .whitespacesAndNewlines).isEmpty }
    }
    static func clearingWhitespaceOnly(_ values: [String]) -> [String] {
        var cleaned = values
        for index in whitespaceOnlyIndices(values) { cleaned[index] = "" }
        return cleaned
    }
    static func append(values: [String], unit: String, current: String, existingNotes: String, allowAmbiguous: Bool = false) throws -> String {
        guard values.count == fields.count, unit == "cm" || unit == "mm",
              existingNotes.utf16.count <= ExperimentalDesignBrief.maxTextUnits,
              current.utf16.count <= ExperimentalDesignBrief.maxTextUnits else { throw ValidationError.tooLong }
        let separators = CharacterSet.newlines.union(CharacterSet(charactersIn: ";"))
        let segments = existingNotes.components(separatedBy: separators)
        let knownLabels = fields.map(\.label) + ["göğüs çevresi", "kalça çevresi", "elbise boyu"]
        let labelsPattern = knownLabels.map { NSRegularExpression.escapedPattern(for: $0) }.joined(separator: "|")
        guard let heading = try? NSRegularExpression(pattern: #"\A[ \t]*(?:"# + labelsPattern + #")(?![\p{L}\p{N}_])"#, options: .caseInsensitive),
              let exact = try? NSRegularExpression(pattern: #"\A[ \t]*(?:"# + labelsPattern + #")[ \t]*(?::|=)?[ \t]*[0-9]+(?:[.,][0-9]+)?[ \t]*(?:cm|mm|derece)[ \t]*[.!]?[ \t]*\z"#, options: .caseInsensitive),
              let conditional = try? NSRegularExpression(pattern: #"[?？]|(?<![\p{L}\p{N}_])(?:eğer|şayet|ise|olursa|olma\p{L}*|değil\p{L}*|belki|varsay\p{L}*)(?![\p{L}\p{N}_])"#, options: .caseInsensitive) else { throw ValidationError.tooLong }
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
                    for next in segments.dropFirst(index + 1) {
                        if next.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty { continue }
                        let range = NSRange(next.startIndex..., in: next)
                        let hasQuestionOrCondition = conditional.firstMatch(in: next, range: range) != nil
                        let hasKnownLabel = heading.firstMatch(in: next, range: range) != nil
                        let hasKnownExplicitMeasurement = exact.firstMatch(in: next, range: range) != nil
                        let hasNumber = next.contains { $0.isNumber }
                        if !allowAmbiguous && (hasQuestionOrCondition || (hasNumber && !hasKnownExplicitMeasurement)) {
                            throw ValidationError.ambiguous(field.label)
                        }
                        if hasKnownLabel { break }
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
    @AccessibilityFocusState private var accessibilityField: Int?
    @State private var pendingFieldJump: Int?
    @State private var fieldJumpRevision = UUID()
    @State private var fieldJumpLifetime = BodyMeasurementNotes.FieldJumpLifetime()
    @State private var unit = "cm"
    @State private var groupIndex = 0
    @State private var revision = UUID()
    @State private var unitChange: BodyMeasurementNotes.UnitChange?
    @State private var confirmUnit = false
    @State private var statusText = ""
    @State private var statusFieldIndex: Int?
    private var status: String {
        get { statusText }
        nonmutating set { statusText = newValue; statusFieldIndex = nil; pendingFieldJump = nil; fieldJumpRevision = UUID() }
    }
    @State private var confirm = false
    @State private var pendingAddition: BodyMeasurementNotes.AdditionReview?
    @State private var discard = false
    @State private var ambiguousConfirm = false
    @State private var confirmWhitespaceCleanup = false
    @State private var ambiguousLabel = ""
    @State private var initialNotes: String?
    @State private var readyToSaveForm = false
    @ViewBuilder private var groupActions: some View {
        if groupIndex > 0 {
            Button("Önceki") { focusedIndex = nil; pendingFieldJump = nil; fieldJumpRevision = UUID(); groupIndex -= 1 }.frame(minHeight: 44)
        }
        if groupIndex < BodyMeasurementNotes.groups.count - 1 {
            Button("Sonraki ölçüler") { focusedIndex = nil; pendingFieldJump = nil; fieldJumpRevision = UUID(); groupIndex += 1 }.frame(minHeight: 44)
        }
    }
    private var measurementGroupSection: some View {
                Section {
                    Text("Adım \(groupIndex + 1) / \(BodyMeasurementNotes.groups.count): \(BodyMeasurementNotes.groups[groupIndex].title)")
                        .font(.headline).accessibilityAddTraits(.isHeader).id("measurement-group-heading")
                    Text("\(BodyMeasurementNotes.fields.count - BodyMeasurementNotes.missingIndices(values).count) / \(BodyMeasurementNotes.fields.count) alan dolduruldu. Bilmediklerinizi boş bırakabilirsiniz.")
                        .font(.caption).foregroundStyle(.secondary)
                    ForEach(BodyMeasurementNotes.groups[groupIndex].indices, id: \.self) { index in
                        let field = BodyMeasurementNotes.fields[index]
                        VStack(alignment: .leading) {
                            Text("\(field.label) (\(field.angle ? "derece" : unit))")
                            if statusFieldIndex == index { Text("Bu alanı kontrol edin").font(.caption.weight(.semibold)).foregroundStyle(.red) }
                            Text(BodyMeasurementNotes.instructions[index]).font(.caption).foregroundStyle(.secondary)
                            TextField("Bilinmiyorsa boş bırakın", text: Binding(get: { values[index] }, set: { value in
                                if values[index] != value { values[index] = value; revision = UUID(); if statusFieldIndex == index { status = "" } }
                            }))
                                .measurementKeyboard(isAngle: field.angle)
                                .accessibilityLabel("\(field.label), \(field.angle ? "derece" : unit)")
                                .accessibilityHint(BodyMeasurementNotes.instructions[index] + " Bilinmiyorsa boş bırakın.")
                                .accessibilityFocused($accessibilityField, equals: index)
                                .focused($focusedIndex, equals: index).frame(minHeight: 44)
                        }.id(BodyMeasurementNotes.fieldScrollID(index))
                    }
                }
    }
    var body: some View {
        NavigationStack {
            ScrollViewReader { proxy in
            Form {
                Section {
                    Text("Bildiğiniz ölçüleri girin; bilinmeyenleri boş bırakın. Uzunluklarda cm veya mm seçin; omuz eğimi derecedir. Bu form kalıp oluşturmaz. Girişler yalnız onayla notlara eklenir. Henüz eklenmemiş değerler yerel taslağa kaydedilmeye çalışılır; ani kapanışta son değişiklikler kaybolabilir.")
                    Text("Göğüs çevresi, kalça çevresi ve elbise boyu ana istek ekranında ayrıca girilir; bu form onları hesaplamaz.").font(.caption)
                    Text("Ölçüm açıklamaları araştırma kaynağından alınmıştır; bağımsız kalıp uzmanı tarafından doğrulanmadı. Emin olmadığınız değerleri boş bırakın.").font(.caption)
                    Text("Bu formdaki belden kalçaya ölçü kalça kemiğinin üst noktasına yöneliktir. A kesim rehberindeki belden kalça hattına düşüklük ve ayrı ön/arka yaylar farklı ölçülerdir; aynı referans noktası olduğu varsayılmaz.").font(.caption)
                    Text("Bu bilgiler iPhone’dan Windows’a istek olarak aktarılır; kalıp üretilmez.").font(.caption)
                    Text(localSaveStatus()).font(.caption).foregroundStyle(.secondary)
                    Picker("Uzunluk birimi", selection: Binding(get: { unit }, set: { requestUnit($0) })) { Text("cm").tag("cm"); Text("mm").tag("mm") }.pickerStyle(.segmented)
                    Text("Dolu alanların birimi yalnız açıkça onaylanan kayıpsız dönüşümle değişir. Omuz eğimi derece olarak kalır.").font(.caption)
                }
                measurementGroupSection
                Section {
                    DisclosureGroup("Boş veya tamamlanmamış alanlar: \(BodyMeasurementNotes.missingIndices(values).count)") {
                        if BodyMeasurementNotes.missingIndices(values).isEmpty { Text("Tüm alanlarda giriş var; değerlerin doğruluğunu ayrıca kontrol edin.") }
                        ForEach(BodyMeasurementNotes.missingIndices(values), id: \.self) { index in
                            let field = BodyMeasurementNotes.fields[index]
                            Text("\(field.label) · \(field.angle ? "derece" : unit)")
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
            }
            .safeAreaInset(edge: .bottom) {
                VStack(spacing: 6) {
                    if !status.isEmpty {
                        Text(status).font(.caption).frame(maxWidth: .infinity, alignment: .leading).accessibilityLabel("Ölçü uyarısı: " + status)
                        if let index = statusFieldIndex, let group = BodyMeasurementNotes.groupIndex(forField: index) {
                            Button("İlgili ölçü alanına git") {
                                focusedIndex = nil; fieldJumpRevision = UUID()
                                if groupIndex == group { scrollToField(index, group: group, proxy: proxy) }
                                else { pendingFieldJump = index; groupIndex = group }
                            }
                            .frame(minHeight: 44)
                            .accessibilityHint(BodyMeasurementNotes.fields[index].label + " alanını gösterir.")
                        }
                    }
                    ViewThatFits(in: .horizontal) {
                        HStack(spacing: 16) { groupActions }.fixedSize(horizontal: true, vertical: false)
                        VStack(spacing: 4) { groupActions }
                    }.frame(maxWidth: .infinity)
                    Button("Girilenleri gözden geçir ve ekle") {
                        focusedIndex = nil; rememberForm()
                        pendingAddition = .init(form: .init(values: values, unit: unit), revision: revision,
                                                notes: notes, existingNotes: measurementsWithNotes(notes), savedForm: savedForm)
                        confirm = true
                    }
                        .frame(maxWidth: .infinity, minHeight: 44).buttonStyle(.borderedProminent)
                }.padding(8).background(.regularMaterial)
            }
            .tint(colorScheme == .dark ? Color(red: 1, green: 0.72, blue: 0.78) : Color(red: 0.29, green: 0.08, blue: 0.14))
            .toolbar { ToolbarItemGroup(placement: .keyboard) { Spacer(); Button("Klavyeyi kapat") { focusedIndex = nil } } }
            .navigationTitle("Beden ölçüleri")
            .interactiveDismissDisabled(true)
            .toolbar { ToolbarItem(placement: .cancellationAction) { Button("Kapat") { if values.contains(where: { !$0.isEmpty }) { discard = true } else { dismiss() } } } }
            .onAppear {
                fieldJumpLifetime.appear()
                initialNotes = notes
                if let form = savedForm, form.values.count == BodyMeasurementNotes.fields.count { values = form.values; unit = form.unit }
                readyToSaveForm = true
            }
            .onDisappear {
                fieldJumpLifetime.disappear(); fieldJumpRevision = UUID(); pendingFieldJump = nil; accessibilityField = nil
                rememberForm()
            }
            .onChange(of: groupIndex) { index in
                guard fieldJumpLifetime.active else { return }
                if let target = pendingFieldJump, BodyMeasurementNotes.groupIndex(forField: target) == index {
                    pendingFieldJump = nil
                    scrollToField(target, group: index, proxy: proxy)
                } else {
                    proxy.scrollTo("measurement-group-heading", anchor: .top)
                    #if os(iOS)
                    UIAccessibility.post(notification: .announcement, argument: "Adım \(index + 1), \(BodyMeasurementNotes.groups[index].title)")
                    #endif
                }
            }
            .onChange(of: values) { _ in revision = UUID(); rememberForm() }
            .onChange(of: unit) { _ in revision = UUID(); rememberForm() }
            .confirmationDialog("Uzunluk birimi değiştirilsin mi?", isPresented: $confirmUnit, titleVisibility: .visible) {
                Button("Değerleri dönüştür") {
                    guard let change = unitChange, change.matches(form: .init(values: values, unit: unit), revision: revision,
                                                                   notes: notes, existingNotes: measurementsWithNotes(notes)) else {
                        unitChange = nil; status = "Girişler veya kaynak değişti; birim değiştirilmedi."; return
                    }
                    values = change.after.values; unit = change.after.unit; revision = UUID(); unitChange = nil
                    rememberForm(); status = "Uzunluklar dönüştürüldü. Değerleri tekrar gözden geçirin; açı değişmedi."
                }
                Button("Vazgeç", role: .cancel) { unitChange = nil }
            } message: {
                if let change = unitChange {
                    Text(BodyMeasurementNotes.fields.indices.compactMap { index in
                        let field = BodyMeasurementNotes.fields[index]
                        guard !field.angle, !change.before.values[index].isEmpty else { return nil }
                        return "\(field.label): \(change.before.values[index]) \(change.before.unit) → \(change.after.values[index]) \(change.after.unit)"
                    }.joined(separator: "\n"))
                    Text("Boş alanlar ve derece cinsinden omuz eğimi korunur.")
                }
            }
            .confirmationDialog("Girilen ölçüler notlara eklensin mi?", isPresented: $confirm, titleVisibility: .visible) {
                Button("Ölçüleri ekle") { apply() }
                Button("Düzenlemeye devam et", role: .cancel) { pendingAddition = nil }
            } message: {
                if let review = pendingAddition {
                    Text((try? BodyMeasurementNotes.append(values: review.form.values, unit: review.form.unit, current: "", existingNotes: "")) ?? "Geçersiz veya boş giriş var; değerleri kontrol edin.")
                }
                Text("Önceki notlar korunur. Aynı ölçü daha önce varsa çelişkiyi önce gözden geçirin.")
            }
            .confirmationDialog("Yalnız boşluk içeren alanlar temizlensin mi?", isPresented: $confirmWhitespaceCleanup, titleVisibility: .visible) {
                Button("Boşlukları temizle", role: .destructive) { values = BodyMeasurementNotes.clearingWhitespaceOnly(values) }
                Button("Vazgeç", role: .cancel) { }
            } message: { Text("Yalnız tamamen boşluk içeren alanlar temizlenir. Diğer ham ölçüler ve önceki notlar değiştirilmez.") }
            .confirmationDialog("Önceki not belirsiz", isPresented: $ambiguousConfirm, titleVisibility: .visible) {
                Button("Önceki notu koruyarak ekle") { apply(allowAmbiguous: true) }
                Button("Notu kontrol edeceğim", role: .cancel) {
                    pendingAddition = nil
                    showSourceIssue("Önceki not formun dışındadır. Formu taslakta tutup ana istek ekranındaki notu kontrol edin.")
                }
            } message: { Text("\(ambiguousLabel) önceki notlarda geçiyor. Çelişki olabilir; mevcut not silinmez. Devam etmeden önce kontrol edin.") }
            .confirmationDialog("Formdan çıkılsın mı?", isPresented: $discard, titleVisibility: .visible) {
                Button("Formu taslakta tut ve çık") { rememberForm(); dismiss() }
                Button("Formu sil ve çık", role: .destructive) { savedForm = nil; readyToSaveForm = false; dismiss() }
                Button("Düzenlemeye devam et", role: .cancel) { }
            }
            }
        }
    }
    private func scrollToField(_ index: Int, group: Int, proxy: ScrollViewProxy) {
        let revision = fieldJumpRevision, lifetime = fieldJumpLifetime.token
        DispatchQueue.main.async {
            guard fieldJumpLifetime.permits(lifetime), fieldJumpRevision == revision, groupIndex == group, statusFieldIndex == index else { return }
            proxy.scrollTo(BodyMeasurementNotes.fieldScrollID(index), anchor: .center)
            DispatchQueue.main.async {
                guard fieldJumpLifetime.permits(lifetime), fieldJumpRevision == revision, groupIndex == group, statusFieldIndex == index else { return }
                accessibilityField = index
            }
        }
    }
    private func showSourceIssue(_ text: String) {
        status = text
        #if os(iOS)
        UIAccessibility.post(notification: .announcement, argument: text)
        #endif
    }
    private func showFieldIssue(_ label: String, _ text: String) {
        status = text
        statusFieldIndex = BodyMeasurementNotes.fields.firstIndex { $0.label == label }
        #if os(iOS)
        UIAccessibility.post(notification: .announcement, argument: text)
        #endif
    }
    private func requestUnit(_ target: String) {
        guard target != unit else { return }
        focusedIndex = nil
        let before = ExperimentalDesignBrief.MeasurementForm(values: values, unit: unit)
        if !before.hasValues { unit = target; revision = UUID(); return }
        do {
            let after = try BodyMeasurementNotes.convertUnit(before, to: target)
            unitChange = .init(before: before, after: after, revision: revision, notes: notes, existingNotes: measurementsWithNotes(notes))
            confirmUnit = true
        } catch BodyMeasurementNotes.ValidationError.invalid(let label) {
            showFieldIssue(label, "\(label) belirsiz veya geçersiz; birim değiştirilmedi. Alanı kontrol edin.")
        } catch { status = "Birim dönüştürülemedi; girişler korundu." }
    }
    private func rememberForm() {
        guard readyToSaveForm else { return }
        savedForm = ExperimentalDesignBrief.MeasurementForm.pending(values: values, unit: unit)
    }
    private func apply(allowAmbiguous: Bool = false) {
        guard let review = pendingAddition,
              review.matches(form: .init(values: values, unit: unit), revision: revision,
                             notes: notes, existingNotes: measurementsWithNotes(notes), savedForm: savedForm), notes == initialNotes else {
            pendingAddition = nil; status = "Girişler veya kaynak değişti. Hiçbir ölçü eklenmedi; yeniden gözden geçirin."; return
        }
        do {
            let updated = try BodyMeasurementNotes.append(values: review.form.values, unit: review.form.unit, current: review.notes, existingNotes: review.existingNotes, allowAmbiguous: allowAmbiguous)
            guard measurementsWithNotes(updated).utf16.count <= ExperimentalDesignBrief.maxTextUnits else { throw BodyMeasurementNotes.ValidationError.tooLong }
            guard review.matches(form: .init(values: values, unit: unit), revision: revision,
                                 notes: notes, existingNotes: measurementsWithNotes(notes), savedForm: savedForm) else {
                pendingAddition = nil; status = "Girişler veya kaynak değişti. Hiçbir ölçü eklenmedi; yeniden gözden geçirin."; return
            }
            readyToSaveForm = false; pendingAddition = nil; notes = updated; savedForm = nil; dismiss()
        } catch BodyMeasurementNotes.ValidationError.empty { status = "En az bir ölçü girin." }
        catch BodyMeasurementNotes.ValidationError.whitespace(let label) { showFieldIssue(label, "\(label) yalnız boşluk içeriyor. Alanı inceleyin veya onayla temizleyin.") }
        catch BodyMeasurementNotes.ValidationError.invalid(let label) { showFieldIssue(label, "\(label): Pozitif uzunluk veya -90 ile 90 arasında (uçlar hariç) omuz açısı girin.") }
        catch BodyMeasurementNotes.ValidationError.ambiguous(let label) { ambiguousLabel = label; ambiguousConfirm = true }
        catch BodyMeasurementNotes.ValidationError.duplicate(let label) { showSourceIssue("\(label) önceki notlarda olabilir. Formu taslakta tutup ana istek ekranındaki notu kontrol edin; önceki not değiştirilmedi.") }
        catch { status = "Ölçüler eklenemedi; metin sınırını kontrol edin. Önceki notlar korundu." }
    }
}
