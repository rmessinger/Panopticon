namespace Panopticon.FileFormats.M3;

public sealed record M3IndexEntry(string Tag, uint Offset, uint Repetitions, uint Version);
