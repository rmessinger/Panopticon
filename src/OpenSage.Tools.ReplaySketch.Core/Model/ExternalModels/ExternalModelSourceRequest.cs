#nullable enable

namespace OpenSage.Tools.ReplaySketch.Model;

/// <summary>
/// One external source in a preview request: the absolute host root directory and the
/// normalized relative virtual key of the selected document. Absolute host paths exist only
/// here and in the request output fields.
/// </summary>
public sealed record ExternalModelSourceRequest(
    string AbsoluteRoot,
    string VirtualKey,
    string FormatId);
