#nullable enable

namespace Panopticon.Tools.ReplaySketch.Model;

/// <summary>
/// WI-A5-19: how the recorded-frame view colors its points: by owning player or by the optional
/// fidelity category each projected point may carry.
/// </summary>
public enum FrameColorMode
{
    Owner,
    Fidelity,
}
