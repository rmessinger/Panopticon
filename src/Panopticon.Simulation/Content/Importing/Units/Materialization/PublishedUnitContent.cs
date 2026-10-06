#nullable enable

using System.Collections.Generic;

namespace Panopticon.Content.Importing.Units.Materialization;

/// <summary>Handle for one successful publication; pass it to <see cref="UnitContentPublisher.Unload"/>.</summary>
public sealed class PublishedUnitContent
{
    internal PublishedUnitContent(ContentSource source, IReadOnlyCollection<ContentId> ids, int scopeDepth)
    {
        Source = source;
        Ids = ids;
        ScopeDepth = scopeDepth;
    }

    public ContentSource Source { get; }

    public IReadOnlyCollection<ContentId> Ids { get; }

    internal int ScopeDepth { get; }

    public bool IsUnloaded { get; internal set; }
}
