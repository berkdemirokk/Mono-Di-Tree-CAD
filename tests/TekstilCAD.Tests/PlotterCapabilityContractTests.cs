using TekstilCAD.MachineGateway;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;
using System.Security.Cryptography;
using System.Text;

namespace TekstilCAD.Tests;

public sealed class PlotterCapabilityContractTests
{
    private static PlotterCapabilityProfile Profile() => new("Vendor", "Model", "1", "1", "1", "evidence",
        PlotterPlatform.Windows, PlotterTransport.WindowsSpooler, 40, 1000, 1000, 1024,
        CapabilitySupport.Supported, CapabilitySupport.Unknown)
        { SupportedCommands = PlotterHpglCommands.CurrentExporter, LineBreaks = CapabilitySupport.Supported, MaximumJobBytes = 100_000, ProtocolIdentifier = "hpgl-simulation-v1", MaximumXPlotUnits = int.MaxValue, MaximumYPlotUnits = int.MaxValue };

    [Fact]
    public void UnknownHpglAndDirectIPhoneUsbAreRejected()
    {
        Assert.False((Profile() with { Hpgl = CapabilitySupport.Unknown }).IsComplete);
        Assert.False((Profile() with { Platform = PlotterPlatform.IPhoneOS, Transport = PlotterTransport.IPadDriverKit }).IsComplete);
        var ipad = Profile() with { Platform = PlotterPlatform.IPadOS, Transport = PlotterTransport.IPadDriverKit };
        Assert.False(ipad.IsComplete);
        Assert.True((ipad with { IPadDriverKitPrerequisites = ApplePlotterEligibility.Decision.PrerequisitesMet }).IsComplete);
        var mobileGateway = Profile() with { Platform = PlotterPlatform.IPhoneOS, Transport = PlotterTransport.NetworkGateway };
        Assert.False(mobileGateway.IsComplete);
        Assert.True((mobileGateway with { GatewayAuthentication = CapabilitySupport.Supported, GatewayAuthorization = CapabilitySupport.Supported }).IsComplete);
        var macUsb = Profile() with { Platform = PlotterPlatform.MacOS, Transport = PlotterTransport.MacUsb };
        Assert.False(macUsb.IsComplete);
        Assert.True((macUsb with { MacUsbEntitlement = CapabilitySupport.Supported }).IsComplete);
        var accessory = Profile() with { Platform = PlotterPlatform.IPhoneOS, Transport = PlotterTransport.ExternalAccessory };
        Assert.False(accessory.IsComplete);
        Assert.True((accessory with { ExternalAccessoryAuthorization = CapabilitySupport.Supported }).IsComplete);
    }

    [Fact]
    public void SerialProfileRequiresExplicitCommunicationSettings()
    {
        var serial = Profile() with { Transport = PlotterTransport.Serial };
        Assert.False(serial.IsComplete);
        var settings = new PlotterSerialSettings(9600, 8, SerialParity.None, SerialStopBits.One, SerialFlowControl.Hardware, 1000);
        Assert.True((serial with { SerialSettings = settings }).IsComplete);
        Assert.False((serial with { SerialSettings = settings with { FlowControl = SerialFlowControl.Unknown } }).IsComplete);
        Assert.False((serial with { SerialSettings = settings with { BaudRate = 0 } }).IsComplete);
        Assert.False((serial with { SerialSettings = settings, ProtocolIdentifier = null }).IsComplete);
    }

    [Fact]
    public void SerialSettingsAreBoundToTheReview()
    {
        var serial = Profile() with { Transport = PlotterTransport.Serial,
            SerialSettings = new PlotterSerialSettings(9600, 8, SerialParity.None, SerialStopBits.One, SerialFlowControl.Hardware, 1000) };
        var output = Prepared(); var hash = PlotterHpglPreflight.Inspect(output, serial).Sha256;
        var review = PlotterSimulationReview.Capture(serial, output, "revision-1", "device-1", hash);
        var changed = serial with { SerialSettings = serial.SerialSettings! with { BaudRate = 19200 } };
        Assert.Throws<InvalidOperationException>(() => new PlotterTransferSimulation().Submit(Guid.NewGuid(), changed, output, review, "revision-1", "device-1", false));
    }

    [Fact]
    public void InterruptedSimulationDoesNotPermitAutomaticRetry()
    {
        var simulation = new PlotterTransferSimulation();
        var output = Prepared(); var review = Review(output);
        simulation.Submit(Guid.NewGuid(), Profile(), output, review, "revision-1", "device-1", interrupted: true);
        Assert.Equal(SimulatedTransferState.OutcomeUnknown, simulation.State);
        Assert.Throws<InvalidOperationException>(() => simulation.Submit(Guid.NewGuid(), Profile(), output, review, "revision-1", "device-1", false));
    }

    [Fact]
    public void ChangedSourceCannotBeSubmitted()
    {
        var simulation = new PlotterTransferSimulation();
        var output = Prepared();
        Assert.Throws<InvalidOperationException>(() => simulation.Submit(Guid.NewGuid(), Profile(), output, Review(output), "revision-2", "device-1", false));
        Assert.Equal(SimulatedTransferState.Prepared, simulation.State);
    }

    private static HpglPreparedOutput Prepared(double size = 10) => HpglFormat.Prepare(new ProjectDocument
    {
        Name = "Preflight",
        Pieces = [new PatternPiece { Name = "square", Closed = true,
            Points = [new(0, 0), new(size, 0), new(size, size), new(0, size)] }]
    }, 40);

    private static PlotterSimulationReview Review(HpglPreparedOutput output) =>
        PlotterSimulationReview.Capture(Profile(), output, "revision-1", "device-1", PlotterHpglPreflight.Inspect(output, Profile()).Sha256);

    private static readonly DateTimeOffset SimulationTime = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);
    private static PlotterQualificationEvidence Qualification() => new("device-1", Profile(), QualificationStatus.Active,
        SimulationTime.AddDays(-1), SimulationTime.AddDays(1), "lab-record-1");

    [Fact]
    public void PreflightHashesTheExactPreparedBytes()
    {
        var output = Prepared();
        var result = PlotterHpglPreflight.Inspect(output, Profile());
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(output.Text))), result.Sha256);
        Assert.Equal(Encoding.UTF8.GetByteCount(output.Text), result.ByteCount);
    }

    [Fact]
    public void PreflightMatchesTheExistingReviewedQueueFile()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        if (root is null) throw new InvalidOperationException("Repository root missing.");
        var queue = new SimulationQueue(Path.Combine(root.FullName, "work", "plotter-preflight-tests", Guid.NewGuid().ToString("N")));
        var source = new ProjectDocument { Name = "preflight queue", Pieces =
            [new PatternPiece { Name = "square", Closed = true,
                Points = [new(0, 0), new(10, 0), new(10, 10), new(0, 10)] }] };
        var machine = new MachineProfile("sim", UnitsPerMillimeter: 40);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => PlotterPreparedSimulation.Prepare(queue, source, machine, Profile(), cancelled.Token));
        Assert.Empty(queue.Jobs);
        var prepared = PlotterPreparedSimulation.Prepare(queue, source, machine, Profile());
        var preflight = prepared.Preflight;
        Assert.ThrowsAny<OperationCanceledException>(() => PlotterHpglPreflight.Inspect(prepared.Output, Profile(), cancelled.Token));
        var approval = prepared.Approve("revision-1", "device-1", preflight.Sha256);
        var unrelated = PlotterPreparedSimulation.Prepare(queue, source, machine, Profile());
        var unrelatedApproval = unrelated.Approve("revision-1", "device-1", unrelated.Preflight.Sha256);
        Assert.Throws<InvalidOperationException>(() => prepared.Commit(unrelatedApproval, source));
        var changed = source.Clone(); changed.Pieces[0].Points[1] = new(11, 0);
        Assert.Throws<InvalidOperationException>(() => prepared.Commit(approval, changed));
        Assert.ThrowsAny<OperationCanceledException>(() => prepared.Commit(approval, source, cancelled.Token));
        Assert.Empty(queue.Jobs);
        var job = prepared.Commit(approval, source);
        var bytes = File.ReadAllBytes(job.OutputPath);
        Assert.Equal(preflight.ByteCount, bytes.Length);
        Assert.Equal(preflight.Sha256, Convert.ToHexString(SHA256.HashData(bytes)));
        Assert.Equal(preflight.Sha256, job.Sha256);
        Assert.Equal(JobStatus.Prepared, job.Status);
        Assert.True(job.SourceSnapshotUnconfirmed);
        Assert.Throws<InvalidOperationException>(() => prepared.Commit(approval, source));
        var simulation = new PlotterSimulationLedger();
        var attempt = simulation.Start(Guid.NewGuid(), "device-1", Profile(), prepared.Output, approval.Review,
            "revision-1", false, Qualification(), SimulationTime);
        Assert.Equal(preflight.Sha256, attempt.OutputSha256);
    }

    [Fact]
    public void PreflightRejectsDifferentUnitsSmallBedAndLongCommands()
    {
        var output = Prepared();
        Assert.Throws<InvalidOperationException>(() => PlotterHpglPreflight.Inspect(output, Profile() with { UnitsPerMillimeter = 20 }));
        Assert.Throws<InvalidDataException>(() => PlotterHpglPreflight.Inspect(output, Profile() with { WidthMillimeters = 9 }));
        Assert.Throws<InvalidDataException>(() => PlotterHpglPreflight.Inspect(output, Profile() with { MaximumCommandBytes = 4 }));
        Assert.Throws<InvalidDataException>(() => PlotterHpglPreflight.Inspect(output, Profile() with { MaximumJobBytes = output.Text.Length - 1 }));
        Assert.Throws<InvalidOperationException>(() => PlotterHpglPreflight.Inspect(output, Profile() with { SupportedCommands = PlotterHpglCommands.PenUp }));
        Assert.Throws<InvalidOperationException>(() => PlotterHpglPreflight.Inspect(output, Profile() with { SupportedCommands = PlotterHpglCommands.CurrentExporter & ~PlotterHpglCommands.DeselectPenZero }));
        Assert.Throws<InvalidOperationException>(() => PlotterHpglPreflight.Inspect(output, Profile() with { LineBreaks = CapabilitySupport.Unknown }));
    }

    [Fact]
    public void PreflightDoesNotRoundAnOutsideCoordinateOntoTheBedEdge()
    {
        var source = new ProjectDocument { Name = "edge", Pieces =
            [new PatternPiece { Name = "square", Closed = true,
                Points = [new(0, 0), new(.3, 0), new(.3, .3), new(0, .3)] }] };
        var output = HpglFormat.Prepare(source, 10);
        var profile = Profile() with { UnitsPerMillimeter = 10, WidthMillimeters = .3, HeightMillimeters = 1 };
        // Binary double 0.3 is slightly less than 3/10, even though 3 / 10d rounds to 0.3.
        Assert.Throws<InvalidDataException>(() => PlotterHpglPreflight.Inspect(output, profile));
        Assert.NotNull(PlotterHpglPreflight.Inspect(output, profile with { WidthMillimeters = Math.BitIncrement(.3) }));
    }

    [Fact]
    public void PreflightHandlesExtremeBoundsWithoutFloatingPointOverflow()
    {
        var output = Prepared();
        Assert.NotNull(PlotterHpglPreflight.Inspect(output, Profile() with { WidthMillimeters = double.MaxValue, HeightMillimeters = double.MaxValue }));
        Assert.Throws<InvalidDataException>(() => PlotterHpglPreflight.Inspect(output, Profile() with { WidthMillimeters = double.Epsilon }));
        Assert.Throws<InvalidOperationException>(() => PlotterHpglPreflight.Inspect(output, Profile() with { WidthMillimeters = double.PositiveInfinity }));
    }

    [Fact]
    public void PreflightRespectsProtocolCoordinateLimitsSeparatelyFromBedSize()
    {
        var output = Prepared(); // 10 mm at 40 units/mm reaches 400 on each axis.
        Assert.False((Profile() with { MaximumXPlotUnits = 0 }).IsComplete);
        Assert.False((Profile() with { MaximumYPlotUnits = 0 }).IsComplete);
        Assert.NotNull(PlotterHpglPreflight.Inspect(output, Profile() with { MaximumXPlotUnits = 400, MaximumYPlotUnits = 400 }));
        Assert.Throws<InvalidDataException>(() => PlotterHpglPreflight.Inspect(output, Profile() with { MaximumXPlotUnits = 399 }));
        Assert.Throws<InvalidDataException>(() => PlotterHpglPreflight.Inspect(output, Profile() with { MaximumYPlotUnits = 399 }));
    }

    [Fact]
    public void PreflightRejectsNegativeMachineCoordinates()
    {
        var source = new ProjectDocument { Name = "negative", Pieces =
            [new PatternPiece { Name = "square", Closed = true,
                Points = [new(-10, 0), new(0, 0), new(0, 10), new(-10, 10)] }] };
        var output = HpglFormat.Prepare(source, 40);
        Assert.Throws<InvalidDataException>(() => PlotterHpglPreflight.Inspect(output, Profile()));
    }

    [Fact]
    public void ConcurrentSimulationSubmissionsAllowOnlyOne()
    {
        var simulation = new PlotterTransferSimulation();
        var output = Prepared(); var review = Review(output);
        var successes = 0;
        Parallel.For(0, 32, _ =>
        {
            try { simulation.Submit(Guid.NewGuid(), Profile(), output, review, "revision-1", "device-1", false); Interlocked.Increment(ref successes); }
            catch (InvalidOperationException) { }
        });
        Assert.Equal(1, successes);
    }

    [Fact]
    public void ChangedApprovalDoesNotConsumeAttempt()
    {
        var simulation = new PlotterTransferSimulation();
        var output = Prepared();
        Assert.Throws<InvalidOperationException>(() => PlotterSimulationReview.Capture(Profile(), output, "revision-1", "device-1", new string('0', 64)));
        Assert.Equal(SimulatedTransferState.Prepared, simulation.State);
    }

    [Fact]
    public void ReviewRejectsDifferentProfileOrPreparedOutput()
    {
        var simulation = new PlotterTransferSimulation();
        var output = Prepared(); var review = Review(output);
        Assert.Throws<InvalidOperationException>(() => simulation.Submit(Guid.NewGuid(), Profile() with { Model = "other" }, output, review, "revision-1", "device-1", false));
        Assert.Throws<InvalidOperationException>(() => simulation.Submit(Guid.NewGuid(), Profile(), Prepared(11), review, "revision-1", "device-1", false));
        Assert.Throws<InvalidOperationException>(() => simulation.Submit(Guid.NewGuid(), Profile(), output, review, "revision-1", "device-2", false));
        Assert.Equal(SimulatedTransferState.Prepared, simulation.State);
    }

    [Fact]
    public void LedgerRequiresExplicitResolutionAndNeverReusesAttemptId()
    {
        var ledger = new PlotterSimulationLedger();
        var output = Prepared(); var review = Review(output); var firstId = Guid.NewGuid();
        var first = ledger.Start(firstId, "device-1", Profile(), output, review, "revision-1", true, Qualification(), SimulationTime);
        Assert.Equal(SimulatedTransferState.OutcomeUnknown, first.TransferState);
        var nextReview = Review(output);
        Assert.Throws<InvalidOperationException>(() => ledger.Start(Guid.NewGuid(), "device-1", Profile(), output, nextReview, "revision-1", false, Qualification(), SimulationTime));
        Assert.Throws<ArgumentOutOfRangeException>(() => ledger.Resolve(firstId, SimulatedResolution.Unresolved, "unknown"));
        Assert.Throws<ArgumentException>(() => ledger.Resolve(firstId, SimulatedResolution.ReportedNotProduced, " "));
        Assert.Equal(SimulatedResolution.Unresolved, ledger.Get(firstId).Resolution);
        ledger.Resolve(firstId, SimulatedResolution.ReportedNotProduced, "Operator reported no output");
        Assert.Throws<InvalidOperationException>(() => ledger.Start(firstId, "device-1", Profile(), output, nextReview, "revision-1", false, Qualification(), SimulationTime));
        Assert.Throws<InvalidOperationException>(() => ledger.Start(Guid.NewGuid(), "device-1", Profile(), output, review, "revision-1", false, Qualification(), SimulationTime));
        Assert.Equal(SimulatedTransferState.Submitted, ledger.Start(Guid.NewGuid(), "device-1", Profile(), output, nextReview, "revision-1", false, Qualification(), SimulationTime).TransferState);
    }

    [Fact]
    public void LedgerSerializesConcurrentAttemptsForOneDevice()
    {
        var ledger = new PlotterSimulationLedger();
        var output = Prepared(); var review = Review(output); var successes = 0;
        Parallel.For(0, 32, _ =>
        {
            try { ledger.Start(Guid.NewGuid(), "device-1", Profile(), output, review, "revision-1", false, Qualification(), SimulationTime); Interlocked.Increment(ref successes); }
            catch (InvalidOperationException) { }
        });
        Assert.Equal(1, successes);
    }

    [Fact]
    public void OneReviewCannotBeConsumedBySeparateLedgers()
    {
        var output = Prepared(); var review = Review(output); var first = new PlotterSimulationLedger(); var second = new PlotterSimulationLedger();
        var successes = 0;
        Parallel.Invoke(
            () => TryStart(first),
            () => TryStart(second));
        Assert.Equal(1, successes);
        void TryStart(PlotterSimulationLedger ledger)
        {
            try { ledger.Start(Guid.NewGuid(), "device-1", Profile(), output, review, "revision-1", false, Qualification(), SimulationTime); Interlocked.Increment(ref successes); }
            catch (InvalidOperationException) { }
        }
    }

    [Fact]
    public void LedgerRejectsRevokedQualificationBeforeReservingDevice()
    {
        var ledger = new PlotterSimulationLedger(); var output = Prepared(); var review = Review(output);
        var revoked = Qualification() with { Status = QualificationStatus.Revoked };
        Assert.Throws<InvalidOperationException>(() => ledger.Start(Guid.NewGuid(), "device-1", Profile(), output, review, "revision-1", false, revoked, SimulationTime));
        Assert.Equal(SimulatedTransferState.Submitted, ledger.Start(Guid.NewGuid(), "device-1", Profile(), output, review, "revision-1", false, Qualification(), SimulationTime).TransferState);
    }

    [Fact]
    public void QualificationSnapshotRejectsRevocationExpiryAndConfigurationChange()
    {
        var now = new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);
        var evidence = new PlotterQualificationEvidence("device-1", Profile(), QualificationStatus.Active,
            now.AddDays(-1), now.AddDays(1), "lab-record-1");
        Assert.Equal(QualificationAssessment.EligibleForSimulation, PlotterQualificationAssessment.Evaluate(evidence, "device-1", Profile(), now));
        Assert.Equal(QualificationAssessment.Expired, PlotterQualificationAssessment.Evaluate(evidence, "device-1", Profile(), evidence.ValidUntil));
        Assert.Equal(QualificationAssessment.Inactive, PlotterQualificationAssessment.Evaluate(evidence with { Status = QualificationStatus.Revoked }, "device-1", Profile(), now));
        Assert.Equal(QualificationAssessment.Mismatch, PlotterQualificationAssessment.Evaluate(evidence, "device-1", Profile() with { Firmware = "2" }, now));
        Assert.Equal(QualificationAssessment.Mismatch, PlotterQualificationAssessment.Evaluate(evidence, "device-2", Profile(), now));
        Assert.Equal(QualificationAssessment.InsufficientEvidence, PlotterQualificationAssessment.Evaluate(evidence with { EvidenceReference = "" }, "device-1", Profile(), now));
    }

    [Fact]
    public void SimulatedTransportAndPhysicalCompletionAreDistinct()
    {
        var ledger = new PlotterSimulationLedger(); var output = Prepared(); var review = Review(output);
        var attempt = ledger.Start(Guid.NewGuid(), "device-1", Profile(), output, review, "revision-1", false, Qualification(), SimulationTime);
        var total = PlotterHpglPreflight.Inspect(output, Profile()).ByteCount;
        var observation = new SimulatedDeliveryObservation(attempt.AttemptId, attempt.DeviceId, attempt.OutputSha256,
            SimulatedObservationKind.SpoolerAccepted, total, total);
        Assert.Equal(SimulatedDeliveryAssessment.SpoolerAccepted, PlotterSimulationObservations.Assess(attempt, Profile(), observation));
        Assert.Equal(SimulatedDeliveryAssessment.OutcomeUnknown, PlotterSimulationObservations.Assess(attempt, Profile(), observation with { Kind = SimulatedObservationKind.DeviceReportsCompletion }));
        Assert.Equal(SimulatedDeliveryAssessment.InvalidEvidence, PlotterSimulationObservations.Assess(attempt, Profile() with { PhysicalCompletionEvidence = CapabilitySupport.Supported }, observation with { Kind = SimulatedObservationKind.DeviceReportsCompletion }));
        Assert.Equal(SimulatedDeliveryAssessment.OutcomeUnknown, PlotterSimulationObservations.Assess(attempt, Profile(), observation with { BytesWritten = total - 1 }));
        Assert.Equal(SimulatedDeliveryAssessment.OutcomeUnknown, PlotterSimulationObservations.Assess(attempt, Profile(), observation with { Kind = SimulatedObservationKind.BeforeSend, BytesWritten = 0 }));
        Assert.Equal(SimulatedDeliveryAssessment.InvalidEvidence, PlotterSimulationObservations.Assess(attempt, Profile(), observation with { AttemptId = Guid.NewGuid() }));
        Assert.Equal(SimulatedResolution.Unresolved, ledger.Get(attempt.AttemptId).Resolution);

        var supported = Profile() with { PhysicalCompletionEvidence = CapabilitySupport.Supported };
        var supportedReview = PlotterSimulationReview.Capture(supported, output, "revision-1", "device-2", observation.OutputSha256);
        var qualification = Qualification() with { DeviceId = "device-2", Profile = supported };
        var second = ledger.Start(Guid.NewGuid(), "device-2", supported, output, supportedReview, "revision-1", false, qualification, SimulationTime);
        var completion = observation with { AttemptId = second.AttemptId, DeviceId = second.DeviceId, Kind = SimulatedObservationKind.DeviceReportsCompletion };
        Assert.Equal(SimulatedDeliveryAssessment.DeviceReportsCompletion, PlotterSimulationObservations.Assess(second, supported, completion));
        Assert.Equal(SimulatedResolution.Unresolved, ledger.Get(second.AttemptId).Resolution);
    }

    [Fact]
    public void IPadDriverKitPrerequisitesDoNotAssumeUnknownFacts()
    {
            Assert.Equal(ApplePlotterEligibility.Decision.Unknown, ApplePlotterEligibility.EvaluateIPadDriverKit(null, ApplePlotterEligibility.IPadProcessor.AppleMSeries, CapabilitySupport.Supported, CapabilitySupport.Supported));
            Assert.Equal(ApplePlotterEligibility.Decision.Unsupported, ApplePlotterEligibility.EvaluateIPadDriverKit(15, ApplePlotterEligibility.IPadProcessor.AppleMSeries, CapabilitySupport.Supported, CapabilitySupport.Supported));
            Assert.Equal(ApplePlotterEligibility.Decision.Unsupported, ApplePlotterEligibility.EvaluateIPadDriverKit(16, ApplePlotterEligibility.IPadProcessor.Other, CapabilitySupport.Supported, CapabilitySupport.Supported));
            Assert.Equal(ApplePlotterEligibility.Decision.Unknown, ApplePlotterEligibility.EvaluateIPadDriverKit(16, ApplePlotterEligibility.IPadProcessor.AppleMSeries, CapabilitySupport.Unknown, CapabilitySupport.Supported));
            Assert.Equal(ApplePlotterEligibility.Decision.PrerequisitesMet, ApplePlotterEligibility.EvaluateIPadDriverKit(16, ApplePlotterEligibility.IPadProcessor.AppleMSeries, CapabilitySupport.Supported, CapabilitySupport.Supported));
    }
}
