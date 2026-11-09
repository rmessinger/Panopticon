#nullable enable

using System.Collections.Generic;

namespace Panopticon.Content.Importing.Maps;

/// <summary>Typed interchange home for NamedCameras.</summary>
public sealed record MapNamedCamerasRecord(
    ushort Version,
    IReadOnlyList<MapNamedCameraRecord> Cameras);
