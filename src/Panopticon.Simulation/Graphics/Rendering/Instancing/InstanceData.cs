using System.Numerics;
using System.Runtime.InteropServices;

namespace Panopticon.Graphics.Rendering.Instancing;

/// <summary>
/// One GPU instance payload (B5-2), mirrored in GLSL by <c>Instancing.h</c>. std430 layout:
/// mat4, then three vec4s, 112 bytes.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct InstanceData
{
    public Matrix4x4 World;

    /// <summary>xy texture offset, z baked-scroll scale, w unused.</summary>
    public Vector4 UVOffset;

    /// <summary>rgb house colour, w opacity.</summary>
    public Vector4 HouseColorOpacity;

    /// <summary>rgb tint, w unused.</summary>
    public Vector4 Tint;
}

/// <summary>
/// Per-batch constants for instanced draws: where this batch's instances start inside the
/// frame's <see cref="InstanceData"/> stream. Kept at 16 bytes for the std140 block.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct InstancedItemConstants
{
    public uint InstanceStart;

    public float Padding0;

    public float Padding1;

    public float Padding2;

    public static unsafe implicit operator InstancedItemConstants(uint start) => new() { InstanceStart = start };
}
