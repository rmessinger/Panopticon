#nullable enable

namespace Panopticon.Content.Importing.Units.Packages;

/// <summary>
/// One retained INI block: exact source bytes with dialect identity and the provenance of the
/// originating block. Bytes are stored verbatim; the writer never re-serializes records into INI.
/// </summary>
public sealed record UnitPackageRetainedBlock(string Dialect, int DialectVersion, byte[] Bytes, UnitPackageProvenance? Provenance = null);
