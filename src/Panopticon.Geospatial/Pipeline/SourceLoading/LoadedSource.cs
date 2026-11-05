using Panopticon.Geospatial.Interchange;
using Panopticon.Geospatial.Manifest;

namespace Panopticon.Geospatial.Pipeline.SourceLoading;

/// <summary>
/// Provenance of one import source observed while loading: the manifest identity plus the
/// normalized relative path, the SHA-256 of the parsed bytes, the actual CRS the reader
/// reported, and both vertical datums with the effective one resolved from them.
/// <see cref="FullPath"/> is a runtime value and is never serialized. A later importer
/// converts this record into the payload provenance model.
/// </summary>
internal sealed record LoadedSource(
    string SourceId,
    ManifestSourceKind Kind,
    ManifestSourceRole Role,
    string RelativePath,
    string FullPath,
    string Sha256Hex,
    long LengthBytes,
    CoordinateSystemId ActualCoordinateSystem,
    string DeclaredVerticalDatum,
    string? EmbeddedVerticalDatum,
    string EffectiveVerticalDatum,
    string? Label = null,
    string? Attribution = null,
    double? HorizontalAccuracyMeters = null,
    double? VerticalAccuracyMeters = null);
