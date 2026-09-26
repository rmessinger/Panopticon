namespace Panopticon.FileFormats.M3;

public sealed record M3Batch(
    ushort Flags,
    ushort Priority,
    ushort Region,
    ushort Bounds,
    ushort Color,
    ushort MaterialReference,
    short Bone);
