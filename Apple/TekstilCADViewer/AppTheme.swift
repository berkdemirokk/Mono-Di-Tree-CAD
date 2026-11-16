import SwiftUI
#if os(macOS)
import AppKit
#elseif canImport(UIKit)
import UIKit
#endif

/// Light touch feedback so a finger knows a piece was picked up or put down. No-op off iOS.
enum Haptics {
    static func selection() {
        #if canImport(UIKit) && !os(macOS)
        UISelectionFeedbackGenerator().selectionChanged()
        #endif
    }

    static func drop() {
        #if canImport(UIKit) && !os(macOS)
        UIImpactFeedbackGenerator(style: .light).impactOccurred()
        #endif
    }
}

// MARK: - Appearance choice

/// How the app picks light or dark. Light is the default whatever the phone is set to; dark and
/// "follow the phone" are opt-in. Stored under `AppAppearance.storageKey` with `@AppStorage`, so a
/// UI test can force a value with the launch arguments `-appearance dark` (argument domain).
enum AppAppearance: String, CaseIterable, Identifiable {
    case light
    case dark
    case system

    static let storageKey = "appearance"

    var id: String { rawValue }

    /// Plain Turkish name shown in the picker.
    var title: String {
        switch self {
        case .light: return "Açık"
        case .dark: return "Koyu"
        case .system: return "Telefona uy"
        }
    }

    var systemImage: String {
        switch self {
        case .light: return "sun.max"
        case .dark: return "moon"
        case .system: return "iphone"
        }
    }

    /// `nil` lets the system decide.
    var colorScheme: ColorScheme? {
        switch self {
        case .light: return .light
        case .dark: return .dark
        case .system: return nil
        }
    }

    /// Mirrors the choice onto the windows themselves. `preferredColorScheme(nil)` does not always
    /// undo an earlier explicit choice (the window keeps the old override), so "Telefona uy" would
    /// otherwise stay stuck on the last light/dark value until the app restarts.
    static func applyToWindows(_ choice: AppAppearance) {
        #if os(macOS)
        switch choice {
        case .light: NSApplication.shared.appearance = NSAppearance(named: .aqua)
        case .dark: NSApplication.shared.appearance = NSAppearance(named: .darkAqua)
        case .system: NSApplication.shared.appearance = nil
        }
        #elseif os(iOS)
        let style: UIUserInterfaceStyle
        switch choice {
        case .light: style = .light
        case .dark: style = .dark
        case .system: style = .unspecified
        }
        for case let scene as UIWindowScene in UIApplication.shared.connectedScenes {
            for window in scene.windows where window.overrideUserInterfaceStyle != style {
                window.overrideUserInterfaceStyle = style
            }
        }
        #endif
    }
}

/// Applies the stored appearance choice. Use it on every window root and on sheets that set their
/// own colour scheme; sheets presented without one inherit the window's.
struct AppAppearanceModifier: ViewModifier {
    @AppStorage(AppAppearance.storageKey) private var appearance: AppAppearance = .light

    func body(content: Content) -> some View {
        content
            .preferredColorScheme(appearance.colorScheme)
            .onAppear { AppAppearance.applyToWindows(appearance) }
            .onChange(of: appearance) { AppAppearance.applyToWindows($0) }
    }
}

extension View {
    /// Light by default; dark or system-following when the person chose so in `AppearancePicker`.
    func appAppearance() -> some View {
        modifier(AppAppearanceModifier())
    }
}

/// A small, self-contained control for the appearance choice. It is not placed on any screen by
/// this file; put it wherever settings live.
struct AppearancePicker: View {
    @AppStorage(AppAppearance.storageKey) private var appearance: AppAppearance = .light

    var body: some View {
        Menu {
            Picker("Görünüm", selection: $appearance) {
                ForEach(AppAppearance.allCases) { option in
                    Label(option.title, systemImage: option.systemImage).tag(option)
                }
            }
        } label: {
            Label("Görünüm: \(appearance.title)", systemImage: "circle.lefthalf.filled")
                .font(.body.weight(.medium))
                .foregroundStyle(AppTheme.accent)
                .frame(minHeight: 44)
                .contentShape(Rectangle())
        }
        .accessibilityLabel("Görünüm")
        .accessibilityValue(appearance.title)
        .accessibilityHint("Açık, koyu ya da telefonun ayarına uyan görünümü seçer.")
        .accessibilityIdentifier("appearance-picker")
    }
}

// MARK: - Colours

/// One calm look for the whole app: a pale page, white cards, a single blue accent and large
/// rounded buttons. Light is the default and its values are unchanged from the light-only design.
/// Each colour also has a dark variant and, for Increase Contrast, a stronger light and dark one.
///
/// Contrast (WCAG 2.x, computed from these sRGB values; "card" = white or dark card, "page" = page):
/// - light: ink/page 16.4, muted/page 5.4, muted/card 5.8, accent/card 5.2, accent/page 4.9,
///   accent on accentSoft 4.6, danger/card 5.6, onAccent/accent 5.2, onAccent/accentPressed 7.5.
/// - light + Increase Contrast: muted/card 10.5, accent/card 8.6, danger/card 8.3, line/card 4.8.
/// - dark: ink/card 14.5, muted/card 7.1, accent/card 6.7, accent on accentSoft 5.0,
///   danger/card 6.2, onAccent/accent 7.6, onAccent/accentPressed 9.9.
/// - dark + Increase Contrast: muted/card 12.1, accent/card 9.8, danger/card 9.2, line/card 6.3.
/// `line` and the 35 % button outline are decorative in the standard looks (every control there is
/// identified by its text or fill); with Increase Contrast they reach at least 3:1.
enum AppTheme {
    /// sRGB red, green, blue in 0...1.
    typealias Tone = (Double, Double, Double)

    static let page = adaptive(light: (0.965, 0.972, 0.988),
                               dark: (0.055, 0.067, 0.090), darkHigh: (0.0, 0.0, 0.0))
    static let card = adaptive(light: (1.0, 1.0, 1.0),
                               dark: (0.106, 0.122, 0.157), darkHigh: (0.075, 0.085, 0.110))
    static let accent = adaptive(light: accentLight, lightHigh: accentLightHigh,
                                 dark: accentDark, darkHigh: accentDarkHigh)
    /// Tinted fill behind accent icons and pressed outline buttons.
    static let accentSoft = adaptive(light: accentLight, lightHigh: accentLightHigh,
                                     dark: accentDark, darkHigh: accentDarkHigh,
                                     lightAlpha: 0.10, darkAlpha: 0.16)
    /// The filled main button while a finger is on it: darker in light, lighter in dark, so its
    /// label keeps its contrast (a see-through accent dropped white text to 3.9:1).
    static let accentPressed = adaptive(light: (0.10, 0.29, 0.76), lightHigh: (0.04, 0.18, 0.55),
                                        dark: (0.60, 0.74, 1.0), darkHigh: (0.75, 0.84, 1.0))
    /// Text and icons placed on a filled accent: white in light, near-black on the lighter dark accent.
    static let onAccent = adaptive(light: (1.0, 1.0, 1.0),
                                   dark: (0.04, 0.07, 0.13), darkHigh: (0.0, 0.0, 0.0))
    static let ink = adaptive(light: (0.08, 0.10, 0.15), lightHigh: (0.0, 0.0, 0.0),
                              dark: (0.93, 0.945, 0.965), darkHigh: (1.0, 1.0, 1.0))
    static let muted = adaptive(light: (0.36, 0.40, 0.48), lightHigh: (0.22, 0.25, 0.31),
                                dark: (0.64, 0.67, 0.73), darkHigh: (0.80, 0.83, 0.88))
    static let line = adaptive(light: (0.87, 0.89, 0.93), lightHigh: (0.42, 0.45, 0.52),
                               dark: (0.20, 0.23, 0.28), darkHigh: (0.56, 0.60, 0.67))
    static let danger = adaptive(light: (0.78, 0.15, 0.15), lightHigh: (0.62, 0.06, 0.06),
                                 dark: (1.0, 0.45, 0.45), darkHigh: (1.0, 0.62, 0.62))
    /// Caution text (for example "needs review"). Light value matches the brown used on the viewer
    /// screen (5.8:1 on the page); dark 10.6:1, Increase Contrast 9.0:1 (light) and 14.2:1 (dark).
    static let warning = adaptive(light: (0.60, 0.30, 0.0), lightHigh: (0.45, 0.20, 0.0),
                                  dark: (1.0, 0.70, 0.30), darkHigh: (1.0, 0.80, 0.50))
    static let radius: CGFloat = 16
    /// The interface is Turkish only: dates and numbers use Turkish rules ("1.000", "16 Kas 2026")
    /// even on a phone set to another region, so the screen never mixes two languages.
    static let locale = Locale(identifier: "tr_TR")

    private static let accentLight: Tone = (0.14, 0.38, 0.93)
    private static let accentLightHigh: Tone = (0.06, 0.25, 0.72)
    private static let accentDark: Tone = (0.47, 0.64, 1.0)
    private static let accentDarkHigh: Tone = (0.62, 0.75, 1.0)

    /// A colour that resolves per appearance and per Increase Contrast. `lightHigh` and `darkHigh`
    /// default to the standard variant.
    private static func adaptive(light: Tone, lightHigh: Tone? = nil, dark: Tone, darkHigh: Tone? = nil,
                                 lightAlpha: Double = 1, darkAlpha: Double = 1) -> Color {
        func pick(isDark: Bool, isHigh: Bool) -> (Tone, Double) {
            if isDark { return (isHigh ? (darkHigh ?? dark) : dark, darkAlpha) }
            return (isHigh ? (lightHigh ?? light) : light, lightAlpha)
        }
        #if os(macOS)
        let color = NSColor(name: nil) { appearance in
            let match = appearance.bestMatch(from: [.aqua, .darkAqua,
                                                    .accessibilityHighContrastAqua,
                                                    .accessibilityHighContrastDarkAqua])
            let isDark = match == .darkAqua || match == .accessibilityHighContrastDarkAqua
            let isHigh = match == .accessibilityHighContrastAqua || match == .accessibilityHighContrastDarkAqua
            let (tone, alpha) = pick(isDark: isDark, isHigh: isHigh)
            return NSColor(srgbRed: CGFloat(tone.0), green: CGFloat(tone.1),
                           blue: CGFloat(tone.2), alpha: CGFloat(alpha))
        }
        return Color(nsColor: color)
        #else
        let color = UIColor { traits in
            let (tone, alpha) = pick(isDark: traits.userInterfaceStyle == .dark,
                                     isHigh: traits.accessibilityContrast == .high)
            return UIColor(red: CGFloat(tone.0), green: CGFloat(tone.1),
                           blue: CGFloat(tone.2), alpha: CGFloat(alpha))
        }
        return Color(uiColor: color)
        #endif
    }
}

extension View {
    /// A rounded card panel with a hairline border, used to group one task.
    func appCard() -> some View {
        padding(16)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(AppTheme.card, in: RoundedRectangle(cornerRadius: AppTheme.radius))
            .overlay(RoundedRectangle(cornerRadius: AppTheme.radius).stroke(AppTheme.line, lineWidth: 1))
    }
}

/// The one main action of a screen or section: filled, full width.
struct AppPrimaryButtonStyle: ButtonStyle {
    @Environment(\.isEnabled) private var isEnabled

    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .font(.headline)
            .foregroundStyle(AppTheme.onAccent)
            .padding(.horizontal, 16)
            .frame(minHeight: 48)
            .background(configuration.isPressed ? AppTheme.accentPressed : AppTheme.accent,
                        in: RoundedRectangle(cornerRadius: AppTheme.radius))
            .opacity(isEnabled ? 1 : 0.45)
            .contentShape(RoundedRectangle(cornerRadius: AppTheme.radius))
    }
}

/// Every other action: outlined, so it never competes with the main one.
struct AppSecondaryButtonStyle: ButtonStyle {
    @Environment(\.isEnabled) private var isEnabled
    @Environment(\.colorSchemeContrast) private var contrast

    func makeBody(configuration: Configuration) -> some View {
        let tint = configuration.role == .destructive ? AppTheme.danger : AppTheme.accent
        // With Increase Contrast the outline is drawn at full strength (at least 3:1).
        let outline = contrast == .increased ? 1.0 : 0.35
        configuration.label
            .font(.body.weight(.medium))
            .foregroundStyle(tint)
            .padding(.horizontal, 14)
            .frame(minHeight: 44)
            .background(configuration.isPressed ? AppTheme.accentSoft : AppTheme.card,
                        in: RoundedRectangle(cornerRadius: 12))
            .overlay(RoundedRectangle(cornerRadius: 12).stroke(tint.opacity(outline), lineWidth: 1))
            .opacity(isEnabled ? 1 : 0.45)
            .contentShape(RoundedRectangle(cornerRadius: 12))
    }
}

extension ButtonStyle where Self == AppPrimaryButtonStyle {
    static var appPrimary: AppPrimaryButtonStyle { AppPrimaryButtonStyle() }
}

extension ButtonStyle where Self == AppSecondaryButtonStyle {
    static var appSecondary: AppSecondaryButtonStyle { AppSecondaryButtonStyle() }
}

/// A short, plain-language hint that says what to do next. It never contains a control.
struct CoachTip: View {
    let systemImage: String
    let title: String
    let text: String

    var body: some View {
        HStack(alignment: .top, spacing: 12) {
            Image(systemName: systemImage)
                .font(.title3)
                .foregroundStyle(AppTheme.accent)
                .frame(width: 36, height: 36)
                .background(AppTheme.accentSoft, in: Circle())
                .accessibilityHidden(true)
            VStack(alignment: .leading, spacing: 3) {
                Text(title).font(.headline).foregroundStyle(AppTheme.ink)
                Text(text).font(.subheadline).foregroundStyle(AppTheme.muted)
                    .fixedSize(horizontal: false, vertical: true)
            }
        }
        .accessibilityElement(children: .combine)
        .accessibilityIdentifier("coach-tip")
    }
}

/// One numbered step of the "how it works" strip on the home screen.
struct StepBadge: View {
    let number: Int
    let title: String
    let detail: String

    var body: some View {
        HStack(alignment: .top, spacing: 12) {
            Text("\(number)")
                .font(.subheadline.bold())
                .foregroundStyle(AppTheme.onAccent)
                .frame(width: 28, height: 28)
                .background(AppTheme.accent, in: Circle())
                .accessibilityHidden(true)
            VStack(alignment: .leading, spacing: 2) {
                Text(title).font(.headline).foregroundStyle(AppTheme.ink)
                Text(detail).font(.subheadline).foregroundStyle(AppTheme.muted)
                    .fixedSize(horizontal: false, vertical: true)
            }
        }
        .accessibilityElement(children: .ignore)
        .accessibilityLabel("Adım \(number): \(title). \(detail)")
    }
}
