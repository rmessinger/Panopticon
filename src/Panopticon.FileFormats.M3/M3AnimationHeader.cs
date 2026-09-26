using System.Numerics;

namespace Panopticon.FileFormats.M3;

public readonly record struct M3AnimationHeader(ushort Interpolation, ushort Flags, uint AnimationId);

public sealed record M3AnimatedVector3(M3AnimationHeader Header, Vector3 DefaultValue);

public sealed record M3AnimatedQuaternion(M3AnimationHeader Header, Quaternion DefaultValue);
