namespace Panopticon.FileFormats.M3;

public sealed record M3Bone(
    string Name,
    uint Flags,
    short Parent,
    M3AnimatedVector3 Location,
    M3AnimatedQuaternion Rotation,
    M3AnimatedVector3 Scale);
