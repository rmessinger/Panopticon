#nullable enable

using System;
using Panopticon.Content.Importing;

namespace Panopticon.Tools.ReplaySketch.Model;

public enum ExternalAnimationSourceDocument
{
    SameDocument,

    SeparateDocument,
}

/// <summary>
/// The selected animation pairing: which document owns the clip, the source display name,
/// and the descriptor with its stable C4 asset key. Selection is by key, never by display
/// name, so duplicate names stay distinct.
/// </summary>
public sealed record ExternalAnimationPairing(
    ExternalAnimationSourceDocument Document,
    string SourceDisplayName,
    ExternalAnimationDescriptor Descriptor)
{
    public string SelectedKey => Descriptor.Key;
}
