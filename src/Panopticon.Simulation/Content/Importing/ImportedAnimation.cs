#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

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

    public ImportedAnimation(
        string name,
        TimeSpan duration,
        IReadOnlyList<ImportedAnimationChannel> channels)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("Animation name must not be null or empty.", nameof(name));
        }

        if (duration < TimeSpan.Zero)
        {
            throw new ArgumentException("Animation duration must not be negative.", nameof(duration));
        }

        if (channels is null)
        {
            throw new ArgumentNullException(nameof(channels));
        }

        for (var i = 0; i < channels.Count; i++)
        {
            if (channels[i] is null)
            {
                throw new ArgumentException($"Channel at index {i} must not be null.", nameof(channels));
            }
        }

        Name = name;
        Duration = duration;
        Channels = channels.ToArray();
    }
}
