using System;

namespace Panopticon.Recording;

/// <summary>
/// Boundary predicates (WI-A1-19): a frame is either an operational boundary (sampled in
/// <see cref="RecordingMode.Operational"/>) or a checkpoint boundary (engine checkpoint taken).
/// </summary>
public sealed record RecordingCadence
{
    private readonly Func<uint, bool> _isOperationalBoundary;
    private readonly Func<uint, bool> _isCheckpointBoundary;

    public RecordingCadence(Func<uint, bool> isOperationalBoundary, Func<uint, bool> isCheckpointBoundary)
    {
        ArgumentNullException.ThrowIfNull(isOperationalBoundary);
        ArgumentNullException.ThrowIfNull(isCheckpointBoundary);

        _isOperationalBoundary = isOperationalBoundary;
        _isCheckpointBoundary = isCheckpointBoundary;
    }

    public bool IsOperationalBoundary(uint frame) => _isOperationalBoundary(frame);

    public bool IsCheckpointBoundary(uint frame) => _isCheckpointBoundary(frame);
}

/// <summary>
/// Options for operational recording (WI-A1-19). The default cadence of 1,800 operational and
/// 108,000 checkpoint frames (one minute and one hour at 30 Hz) applies when neither the domain
/// nor the caller supplies one.
/// </summary>
public sealed record RecordingOptions
{
    public const uint DefaultOperationalInterval = 1_800;

    public const uint DefaultCheckpointInterval = 108_000;

    public RecordingMode Mode { get; init; } = RecordingMode.Full;

    /// <summary>Bundle directory for the operational stream and checkpoints (A1-21).</summary>
    public string BundleDirectory { get; init; } = "";

    /// <summary>Cadence to use when the domain does not supply one; the 1800/108000 default when null.</summary>
    public RecordingCadence? DefaultCadence { get; init; }

    /// <summary>Whether to record T1 signature trees into the operational stream (A5-4 tolerance plan).</summary>
    public bool RecordT1 { get; init; } = true;

    public static RecordingCadence CreateDefaultCadence() => new(
        frame => frame % DefaultOperationalInterval == 0,
        frame => frame % DefaultCheckpointInterval == 0);
}
