import SwiftUI
#if canImport(UIKit)
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

/// One light, calm look for the whole app: a pale page, white cards, a single blue accent and
/// large rounded buttons. The colours are fixed (not system-dependent) so the contrast that was
/// checked here is the contrast people get.
enum AppTheme {
    static let page = Color(red: 0.965, green: 0.972, blue: 0.988)
    static let card = Color.white
    static let accent = Color(red: 0.14, green: 0.38, blue: 0.93)
    static let accentSoft = Color(red: 0.14, green: 0.38, blue: 0.93).opacity(0.10)
    static let ink = Color(red: 0.08, green: 0.10, blue: 0.15)
    static let muted = Color(red: 0.36, green: 0.40, blue: 0.48)
    static let line = Color(red: 0.87, green: 0.89, blue: 0.93)
    static let danger = Color(red: 0.78, green: 0.15, blue: 0.15)
    static let radius: CGFloat = 16
}

extension View {
    /// A white rounded panel with a hairline border, used to group one task.
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
            .foregroundStyle(Color.white)
            .padding(.horizontal, 16)
            .frame(minHeight: 48)
            .background(AppTheme.accent.opacity(configuration.isPressed ? 0.82 : 1),
                        in: RoundedRectangle(cornerRadius: AppTheme.radius))
            .opacity(isEnabled ? 1 : 0.45)
            .contentShape(RoundedRectangle(cornerRadius: AppTheme.radius))
    }
}

/// Every other action: outlined, so it never competes with the main one.
struct AppSecondaryButtonStyle: ButtonStyle {
    @Environment(\.isEnabled) private var isEnabled

    func makeBody(configuration: Configuration) -> some View {
        let tint = configuration.role == .destructive ? AppTheme.danger : AppTheme.accent
        configuration.label
            .font(.body.weight(.medium))
            .foregroundStyle(tint)
            .padding(.horizontal, 14)
            .frame(minHeight: 44)
            .background(configuration.isPressed ? AppTheme.accentSoft : AppTheme.card,
                        in: RoundedRectangle(cornerRadius: 12))
            .overlay(RoundedRectangle(cornerRadius: 12).stroke(tint.opacity(0.35), lineWidth: 1))
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
                .foregroundStyle(Color.white)
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
