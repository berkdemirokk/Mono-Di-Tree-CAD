using TekstilCAD.FileFormats;

namespace TekstilCAD.MachineGateway;

// A description of a qualified combination, not evidence of qualification.
public enum PlotterPlatform { Windows, MacOS, IPadOS, IPhoneOS }
public enum PlotterTransport { WindowsSpooler, Serial, VendorSdk, MacUsb, IPadDriverKit, ExternalAccessory, NetworkGateway }
public enum CapabilitySupport { Unknown, Unsupported, Supported }
public enum SerialParity { Unknown, None, Odd, Even, Mark, Space }
public enum SerialStopBits { Unknown, One, OnePointFive, Two }
public enum SerialFlowControl { Unknown, None, Hardware, XonXoff }

/// <summary>Caller-declared settings; never probes a port or confirms device compatibility.</summary>
public sealed record PlotterSerialSettings(int BaudRate, int DataBits, SerialParity Parity,
    SerialStopBits StopBits, SerialFlowControl FlowControl, int TimeoutMilliseconds)
{
    public bool IsComplete => BaudRate is > 0 and <= 4_000_000 && DataBits is >= 5 and <= 8 &&
        Enum.IsDefined(Parity) && Parity != SerialParity.Unknown &&
        Enum.IsDefined(StopBits) && StopBits != SerialStopBits.Unknown &&
        Enum.IsDefined(FlowControl) && FlowControl != SerialFlowControl.Unknown &&
        TimeoutMilliseconds is > 0 and <= 60_000;
}

[Flags]
public enum PlotterHpglCommands
{
    None = 0, Initialize = 1, SelectPenOne = 2, AbsoluteCoordinates = 4, PenUp = 8, PenDown = 16,
    DeselectPenZero = 32,
    CurrentExporter = Initialize | SelectPenOne | DeselectPenZero | AbsoluteCoordinates | PenUp | PenDown
}

public sealed record PlotterCapabilityProfile(
    string Manufacturer, string Model, string Firmware, string DriverVersion,
    string AdapterVersion, string QualificationId, PlotterPlatform Platform,
    PlotterTransport Transport, double UnitsPerMillimeter, double WidthMillimeters,
    double HeightMillimeters, int MaximumCommandBytes, CapabilitySupport Hpgl,
    CapabilitySupport PhysicalCompletionEvidence)
{
    // Defaults deliberately deny preflight until the exact dialect and byte limit are supplied.
    public PlotterHpglCommands SupportedCommands { get; init; }
    public CapabilitySupport LineBreaks { get; init; }
    public int MaximumJobBytes { get; init; }
    // Explicit protocol limits; zero means unspecified, not unlimited.
    public int MaximumXPlotUnits { get; init; }
    public int MaximumYPlotUnits { get; init; }
    public string? ProtocolIdentifier { get; init; }
    public PlotterSerialSettings? SerialSettings { get; init; }
    // A caller-reported prerequisite result, never device or App Store certification.
    public ApplePlotterEligibility.Decision? IPadDriverKitPrerequisites { get; init; }
    // These are reported capabilities, not runtime verification of permissions or identity.
    public CapabilitySupport MacUsbEntitlement { get; init; }
    public CapabilitySupport ExternalAccessoryAuthorization { get; init; }
    public CapabilitySupport GatewayAuthentication { get; init; }
    public CapabilitySupport GatewayAuthorization { get; init; }

    // This checks completeness only; a separate qualification registry must verify
    // that the evidence is current and matches the connected physical device.
    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(Manufacturer) && !string.IsNullOrWhiteSpace(Model) &&
        !string.IsNullOrWhiteSpace(Firmware) && !string.IsNullOrWhiteSpace(DriverVersion) &&
        !string.IsNullOrWhiteSpace(AdapterVersion) && !string.IsNullOrWhiteSpace(QualificationId) &&
        !string.IsNullOrWhiteSpace(ProtocolIdentifier) && ProtocolIdentifier.Length <= 200 &&
        Enum.IsDefined(Platform) && Enum.IsDefined(Transport) &&
        Enum.IsDefined(Hpgl) && Enum.IsDefined(PhysicalCompletionEvidence) && Enum.IsDefined(LineBreaks) &&
        Enum.IsDefined(MacUsbEntitlement) && Enum.IsDefined(ExternalAccessoryAuthorization) &&
        Enum.IsDefined(GatewayAuthentication) && Enum.IsDefined(GatewayAuthorization) &&
        double.IsFinite(UnitsPerMillimeter) && UnitsPerMillimeter > 0 &&
        double.IsFinite(WidthMillimeters) && WidthMillimeters > 0 &&
        double.IsFinite(HeightMillimeters) && HeightMillimeters > 0 && MaximumCommandBytes > 0 &&
        Hpgl == CapabilitySupport.Supported && LineBreaks == CapabilitySupport.Supported &&
        SupportedCommands == PlotterHpglCommands.CurrentExporter && MaximumJobBytes > 0 &&
        MaximumXPlotUnits > 0 && MaximumYPlotUnits > 0 && PlatformAllowsTransport;

    private bool PlatformAllowsTransport => Transport switch
    {
        PlotterTransport.WindowsSpooler => Platform == PlotterPlatform.Windows,
        PlotterTransport.MacUsb => Platform == PlotterPlatform.MacOS && MacUsbEntitlement == CapabilitySupport.Supported,
        PlotterTransport.IPadDriverKit => Platform == PlotterPlatform.IPadOS &&
            IPadDriverKitPrerequisites == ApplePlotterEligibility.Decision.PrerequisitesMet,
        PlotterTransport.ExternalAccessory => (Platform is PlotterPlatform.IPadOS or PlotterPlatform.IPhoneOS) &&
            ExternalAccessoryAuthorization == CapabilitySupport.Supported,
        PlotterTransport.Serial => (Platform is PlotterPlatform.Windows or PlotterPlatform.MacOS) && SerialSettings is { IsComplete: true },
        PlotterTransport.VendorSdk => Platform is PlotterPlatform.Windows or PlotterPlatform.MacOS,
        PlotterTransport.NetworkGateway => GatewayAuthentication == CapabilitySupport.Supported &&
            GatewayAuthorization == CapabilitySupport.Supported,
        _ => false
    };
}

public enum SimulatedTransferState { Prepared, Submitted, OutcomeUnknown }

/// <summary>
/// A caller-declared simulation review, not an authentication or qualification record.
/// The caller must derive the revision identity from the captured source snapshot and
/// create this object only after the user has reviewed the exact output and profile.
/// </summary>
public sealed class PlotterSimulationReview
{
    private int consumed;
    private PlotterSimulationReview(PlotterCapabilityProfile profile, string sourceRevision, string targetDeviceId, string sha256)
    { Profile = profile; SourceRevision = sourceRevision; TargetDeviceId = targetDeviceId; Sha256 = sha256; }

    public PlotterCapabilityProfile Profile { get; }
    public string SourceRevision { get; }
    public string TargetDeviceId { get; }
    public string Sha256 { get; }

    // Process-local one-use marker only; it is not a durable approval record.
    internal bool TryConsume() => Interlocked.CompareExchange(ref consumed, 1, 0) == 0;

    public static PlotterSimulationReview Capture(PlotterCapabilityProfile profile, HpglPreparedOutput output,
        string sourceRevision, string targetDeviceId, string reviewedSha256)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (string.IsNullOrWhiteSpace(sourceRevision) || sourceRevision.Length > 256)
            throw new ArgumentException("Kaynak revizyonu gerekli ve en fazla 256 karakter olmalıdır.", nameof(sourceRevision));
        ValidateDeviceId(targetDeviceId);
        var actual = PlotterHpglPreflight.Inspect(output, profile);
        if (!string.Equals(actual.Sha256, reviewedSha256, StringComparison.Ordinal))
            throw new InvalidOperationException("İncelenen HPGL özeti eşleşmiyor.");
        return new PlotterSimulationReview(profile, sourceRevision, targetDeviceId, actual.Sha256);
    }

    internal static void ValidateDeviceId(string targetDeviceId)
    {
        if (string.IsNullOrWhiteSpace(targetDeviceId) || targetDeviceId.Length > 256 ||
            !string.Equals(targetDeviceId, targetDeviceId.Trim(), StringComparison.Ordinal))
            throw new ArgumentException("Kanonik cihaz kimliği gerekli ve en fazla 256 karakter olmalıdır.", nameof(targetDeviceId));
    }
}

/// <summary>In-memory model only. No file, network, USB, or physical machine I/O.</summary>
public sealed class PlotterTransferSimulation
{
    private readonly object gate = new();
    private SimulatedTransferState state = SimulatedTransferState.Prepared;
    public SimulatedTransferState State { get { lock (gate) return state; } }

    public void Submit(Guid attemptId, PlotterCapabilityProfile profile, HpglPreparedOutput output,
        PlotterSimulationReview review, string currentSourceRevision, string targetDeviceId, bool interrupted)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(review);
        if (attemptId == Guid.Empty || !profile.IsComplete || profile != review.Profile ||
            !string.Equals(currentSourceRevision, review.SourceRevision, StringComparison.Ordinal) ||
            !string.Equals(targetDeviceId, review.TargetDeviceId, StringComparison.Ordinal))
            throw new InvalidOperationException("Deneme kimliği, incelenen profil veya kaynak revizyonu eşleşmiyor.");
        var inspected = PlotterHpglPreflight.Inspect(output, profile);
        if (!string.Equals(inspected.Sha256, review.Sha256, StringComparison.Ordinal))
            throw new InvalidOperationException("Onaylanan HPGL özeti eşleşmiyor.");
        lock (gate)
        {
            if (state != SimulatedTransferState.Prepared)
                throw new InvalidOperationException("Bu simülasyon yeniden gönderilemez.");
            if (!review.TryConsume())
                throw new InvalidOperationException("Bu simülasyon incelemesi zaten kullanıldı; yeniden inceleyin.");
            state = interrupted ? SimulatedTransferState.OutcomeUnknown : SimulatedTransferState.Submitted;
        }
    }
}
