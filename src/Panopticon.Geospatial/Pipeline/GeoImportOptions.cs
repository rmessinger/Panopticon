using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Panopticon.Geospatial.Pipeline;

/// <summary>
/// Options for one import run. Defaults preserve existing outputs (no force) and use the
/// standard <see cref="GeospatialImportLimits"/>. The staged validator list is copied in
/// declared order; validators run in that order during staged validation.
/// </summary>
public sealed record GeoImportOptions
{
    public GeoImportOptions(
        bool force = false,
        GeospatialImportLimits? limits = null,
        IEnumerable<IStagedPayloadValidator>? stagedValidators = null)
    {
        Limits = limits ?? new GeospatialImportLimits();

        List<IStagedPayloadValidator> copiedValidators;
        if (stagedValidators is null)
        {
            copiedValidators = [];
        }
        else
        {
            copiedValidators = new List<IStagedPayloadValidator>();

            foreach (var validator in stagedValidators)
            {
                if (validator is null)
                {
                    throw new ArgumentException("Staged validators must not be null.", nameof(stagedValidators));
                }

                copiedValidators.Add(validator);
            }
        }

        Force = force;
        StagedValidators = new ReadOnlyCollection<IStagedPayloadValidator>(copiedValidators);
    }

    /// <summary>
    /// When true, an existing destination is retained as a unique sibling backup after a
    /// successful replacement. Without force, an existing destination is an error that
    /// leaves the destination unchanged.
    /// </summary>
    public bool Force { get; }

    public GeospatialImportLimits Limits { get; }

    /// <summary>Copied ordered staged validators; empty when none are declared.</summary>
    public IReadOnlyList<IStagedPayloadValidator> StagedValidators { get; }
}
