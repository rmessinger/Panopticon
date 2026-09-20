#nullable enable

namespace OpenSage.Tools.ReplaySketch.Model;

public enum ExternalModelSessionState
{
    Empty,

    Inspecting,

    Ready,

    Rendering,

    Failed,

    Disposed,
}
