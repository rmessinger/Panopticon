#nullable enable

using System;

namespace Panopticon.Content.Importing.Maps;

/// <summary>Retained-opaque legacy section. Bytes round-trip byte-exact and are accepted only for a known section name and version.</summary>
public sealed record RetainedSection(string SectionName, int SectionVersion, ReadOnlyMemory<byte> Bytes, string Sha256);
