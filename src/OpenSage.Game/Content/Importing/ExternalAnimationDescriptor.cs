#nullable enable

using System;

namespace OpenSage.Content.Importing;

/// <summary>
/// One discovered animation, with the stable key the C4 animation importer accepts and the
/// name, duration, and channel count the corresponding import produces.
/// </summary>
public sealed record ExternalAnimationDescriptor(
    string Key,
    string Name,
    TimeSpan Duration,
    int ChannelCount);
