#nullable enable

using System.Collections.Generic;

namespace Panopticon.Content.Importing.Maps;

/// <summary>Placed objects of a map.</summary>
public sealed record PlacedObjectSet(IReadOnlyList<PlacedObject> Objects);
