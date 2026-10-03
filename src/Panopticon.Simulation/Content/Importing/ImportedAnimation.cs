#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Panopticon;

namespace Panopticon.Content.Importing;

/// <summary>
/// CPU-only animation: asset name, duration, and ordered channels addressing the target
/// skeleton's bone index space.
/// </summary>
public sealed class ImportedAnimation
{
    public string Name { get; }

    public TimeSpan Duration { get; }

    public IReadOnlyList<ImportedAnimationChannel> Channels { get; }

    public IReadOnlyList<ImportedMorphAnimationChannel> MorphChannels { get; }

    public ImportedAnimation(
        string name,
        TimeSpan duration,
        IReadOnlyList<ImportedAnimationChannel> channels,
        IReadOnlyList<ImportedMorphAnimationChannel>? morphChannels = null)
    {
        Ensure.NotNullOrEmpty(name, "Animation name must not be null or empty.");

        if (duration < TimeSpan.Zero)
        {
            throw new ArgumentException("Animation duration must not be negative.", nameof(duration));
        }

        ArgumentNullException.ThrowIfNull(channels);
        Ensure.NoNullElements(channels, "Channel");

        if (morphChannels is not null)
            Ensure.NoNullElements(morphChannels, "Morph channel");

        Name = name;
        Duration = duration;
        Channels = channels.ToArray();
        MorphChannels = morphChannels?.ToArray() ?? Array.Empty<ImportedMorphAnimationChannel>();
    }
}
