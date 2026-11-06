#nullable enable

using System.Collections.Generic;

namespace Panopticon.Content.Importing.Maps;

/// <summary>A generated feature with a stable ID and string attributes.</summary>
public sealed record GeneratedFeature(string StableId, string Kind, IReadOnlyDictionary<string, string> Attributes);
