namespace Panopticon.FileFormats.Casc;

public readonly record struct CascSpan(int Archive, long Offset, int Size);
