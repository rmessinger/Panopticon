#nullable enable

using System.Collections.Generic;

namespace Panopticon.Content.Importing.Units;

/// <summary>
/// Reads one source game's unit content into engine-owned records. Implementations keep source
/// syntax internal and report unreadable input as diagnostics on the returned set.
/// </summary>
public interface IUnitSourceAdapter
{
    ContentSource Source { get; }

    UnitContentSet Read(IReadOnlyCollection<string> rootNames);
}
