#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Panopticon.Content.Importing.Units.Materialization;
using Panopticon.Logic.Object;

namespace Panopticon.Content.Importing.Units.Packages;

/// <summary>
/// Handle representing a loaded staged pack consisting of one or more published packages.
/// Unloading unloads published packages in reverse publication order.
/// </summary>
public sealed class StagedPackPublication
{
    private readonly List<PublishedUnitContent> _publishedPackages;

    public string PackRoot { get; }

    public IReadOnlyList<PublishedUnitContent> PublishedPackages => _publishedPackages;

    public bool IsUnloaded { get; private set; }

    public StagedPackPublication(string packRoot, IEnumerable<PublishedUnitContent> publishedPackages)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packRoot);
        ArgumentNullException.ThrowIfNull(publishedPackages);
        PackRoot = packRoot;
        _publishedPackages = publishedPackages.ToList();
    }

    /// <summary>
    /// Unloads all published packages in reverse publication order.
    /// </summary>
    public void Unload(UnitContentPublisher publisher, IEnumerable<ObjectDefinition>? liveDefinitions = null)
    {
        ArgumentNullException.ThrowIfNull(publisher);
        if (IsUnloaded)
        {
            throw new InvalidOperationException("This pack publication was already unloaded.");
        }

        for (var i = _publishedPackages.Count - 1; i >= 0; i--)
        {
            publisher.Unload(_publishedPackages[i], liveDefinitions);
        }

        IsUnloaded = true;
    }
}
