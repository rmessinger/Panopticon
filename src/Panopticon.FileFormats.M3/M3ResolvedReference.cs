using System;

namespace Panopticon.FileFormats.M3;

public sealed record M3ResolvedReference(M3IndexEntry Entry, ReadOnlyMemory<byte> Data);
