#nullable enable

using System.Numerics;

namespace Panopticon.Content.Importing.Maps;

/// <summary>One directional light specification with ambient, diffuse color, and direction.</summary>
public sealed record MapGlobalLightRecord(Vector3 Ambient, Vector3 Color, Vector3 Direction);
