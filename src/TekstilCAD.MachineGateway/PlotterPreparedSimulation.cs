using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.MachineGateway;

/// <summary>
/// Connects the existing file-only queue proposal to the simulation preflight.
/// The caller must keep the source stable while capturing and committing, and
/// serialize all operations on the queue. It does not establish qualification,
/// user identity, production readiness, or any connection to a physical machine.
/// </summary>
public sealed class PlotterPreparedSimulation
{
    private readonly SimulationQueue queue;
    private readonly SimulationQueue.PreparedHpglJob prepared;
    private readonly PlotterCapabilityProfile deviceProfile;

    private PlotterPreparedSimulation(SimulationQueue queue, SimulationQueue.PreparedHpglJob prepared,
        PlotterCapabilityProfile deviceProfile, PlotterHpglPreflight.Result preflight)
    { this.queue = queue; this.prepared = prepared; this.deviceProfile = deviceProfile; Preflight = preflight; }

    public HpglPreparedOutput Output => prepared.Output;
    public PlotterHpglPreflight.Result Preflight { get; }

    public static PlotterPreparedSimulation Prepare(SimulationQueue queue, ProjectDocument source,
        MachineProfile fileProfile, PlotterCapabilityProfile deviceProfile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(fileProfile);
        ArgumentNullException.ThrowIfNull(deviceProfile);
        cancellationToken.ThrowIfCancellationRequested();
        if (!deviceProfile.IsComplete || fileProfile.UnitsPerMillimeter != deviceProfile.UnitsPerMillimeter)
            throw new InvalidOperationException("Profil eksik veya HPGL birimi eşleşmiyor.");
        // PrepareHpglReview makes a detached source capture and performs no file I/O.
        var prepared = queue.PrepareHpglReview(source, fileProfile, cancellationToken);
        var preflight = PlotterHpglPreflight.Inspect(prepared.Output, deviceProfile, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return new PlotterPreparedSimulation(queue, prepared, deviceProfile, preflight);
    }

    public sealed class Approval
    {
        internal Approval(PlotterPreparedSimulation owner, PlotterSimulationReview review) { Owner = owner; Review = review; }
        internal PlotterPreparedSimulation Owner { get; }
        public PlotterSimulationReview Review { get; }
    }

    /// <summary>Call only after the user has reviewed this exact output and profile.</summary>
    public Approval Approve(string sourceRevision, string deviceId, string reviewedSha256)
    {
        var review = PlotterSimulationReview.Capture(deviceProfile, prepared.Output, sourceRevision, deviceId, reviewedSha256);
        return new Approval(this, review);
    }

    /// <summary>
    /// Writes only a local simulation file. SourceSnapshotUnconfirmed remains true:
    /// the caller must separately verify the live document and selection. The queue's
    /// commit is single-attempt; a failed commit may leave local files for inspection.
    /// </summary>
    public SimulationJob Commit(Approval approval, ProjectDocument currentSource, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(approval);
        if (!ReferenceEquals(approval.Owner, this) || approval.Review.Profile != deviceProfile ||
            !string.Equals(approval.Review.Sha256, Preflight.Sha256, StringComparison.Ordinal))
            throw new InvalidOperationException("İnceleme bu hazırlıkla eşleşmiyor.");
        return queue.CommitPreparedHpgl(prepared, currentSource, prepared.Profile,
            sourceSnapshotUnconfirmed: true, cancellationToken: cancellationToken);
    }
}
