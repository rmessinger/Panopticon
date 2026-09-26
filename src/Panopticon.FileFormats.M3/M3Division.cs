using System.Collections.Generic;

namespace Panopticon.FileFormats.M3;

public sealed record M3Division(
    IReadOnlyList<ushort> Faces,
    IReadOnlyList<M3Region> Regions,
    IReadOnlyList<M3Batch> Batches);
