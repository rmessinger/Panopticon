#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Panopticon;

namespace Panopticon.Content.Importing;

/// <summary>
/// Immutable CPU description of one imported legacy particle effect. See
/// docs/roadmap/deep-dives/c4h-ee2-particle-effects.md. Importers emit interchange
/// values only; materialization onto the FX particle runtime is WI-C4h-04.
/// </summary>
public sealed class ImportedParticleEffect
{
    /// <summary>Source asset name, e.g. "fx_explosion_1".</summary>
    public string Name { get; }

    public IReadOnlyList<ImportedParticleSystem> Systems { get; }

    /// <summary>Names of non-particle sub-objects carried by the same NIF, in block order.</summary>
    public IReadOnlyList<string> HelperSubObjectNames { get; }

    public IReadOnlyList<ImportedParticleAttachment> Attachments { get; }

    public ImportedParticleEffect(
        string name,
        IReadOnlyList<ImportedParticleSystem> systems,
        IReadOnlyList<string> helperSubObjectNames,
        IReadOnlyList<ImportedParticleAttachment>? attachments = null)
    {
        Ensure.NotNullOrEmpty(name, "Effect name must not be empty.");

        Ensure.NotEmpty(systems, "Effects require at least one particle system.");

        if (systems.Any(s => s is null))
            throw new ArgumentException("Particle systems must not contain null entries.", nameof(systems));

        var duplicate = systems.GroupBy(s => s.Name, StringComparer.Ordinal)
            .FirstOrDefault(g => g.Count() > 1);

        if (duplicate is not null)
            throw new ArgumentException($"Particle system name '{duplicate.Key}' is not unique.", nameof(systems));

        ArgumentNullException.ThrowIfNull(helperSubObjectNames);

        if (helperSubObjectNames.Any(n => string.IsNullOrEmpty(n)))
            throw new ArgumentException("Helper sub-object names must not be empty.", nameof(helperSubObjectNames));

        Name = name;
        Systems = systems.ToArray();
        HelperSubObjectNames = helperSubObjectNames.ToArray();
        Attachments = (attachments ?? Array.Empty<ImportedParticleAttachment>()).ToArray();
    }
}
