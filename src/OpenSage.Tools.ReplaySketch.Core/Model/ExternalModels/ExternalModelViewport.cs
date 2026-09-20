#nullable enable

namespace OpenSage.Tools.ReplaySketch.Model;

/// <summary>Deterministic capture viewport; both dimensions positive.</summary>
public sealed record ExternalModelViewport(int Width, int Height);
