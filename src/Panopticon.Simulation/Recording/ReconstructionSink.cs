#nullable enable annotations
using System.Collections.Generic;
using Panopticon.Simulation;

namespace Panopticon.Recording;

/// <summary>
/// Receives reconstructed state at operational boundaries (WI-A1-22). Boundaries are delivered
/// in frame order, only after their recorded hashes have been verified; nothing at or after the
/// first diverging boundary is ever delivered.
/// </summary>
public interface IReconstructionSink
{
    /// <summary>
    /// Called once per verified boundary frame with the world summary and, when the domain has a
    /// state projection, the domain state frame captured at that boundary.
    /// </summary>
    void OnBoundary(uint frame, WorldStateSummary summary, DomainStateFrame? domainState);
}

/// <summary>Collects every delivered boundary in memory (the ExportSlice buffer and test surface).</summary>
public sealed class BufferedReconstructionSink : IReconstructionSink
{
    public List<uint> Frames { get; } = new();

    public List<WorldStateSummary> Summaries { get; } = new();

    public List<DomainStateFrame?> DomainStates { get; } = new();

    public void OnBoundary(uint frame, WorldStateSummary summary, DomainStateFrame? domainState)
    {
        Frames.Add(frame);
        Summaries.Add(summary);
        DomainStates.Add(domainState);
    }
}
