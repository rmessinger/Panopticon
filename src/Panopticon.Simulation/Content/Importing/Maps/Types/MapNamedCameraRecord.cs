#nullable enable

using System.Numerics;

namespace Panopticon.Content.Importing.Maps;

/// <summary>One authored named camera position and orientation.</summary>
public sealed record MapNamedCameraRecord(
    Vector3 LookAtPoint,
    string Name,
    float Pitch,
    float Roll,
    float Yaw,
    float Zoom,
    float FieldOfView,
    float Unknown);
