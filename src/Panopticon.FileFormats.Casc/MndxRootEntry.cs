namespace Panopticon.FileFormats.Casc;

public readonly record struct MndxRootEntry(string Path, CascKey ContentKey, int Size);
