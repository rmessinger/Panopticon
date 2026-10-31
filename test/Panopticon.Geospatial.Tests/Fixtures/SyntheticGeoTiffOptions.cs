using Panopticon.Geospatial.Interchange;

namespace Panopticon.Geospatial.Tests.Fixtures;

/// <summary>Coordinate reference system written into a synthetic fixture's GeoKey directory.</summary>
public enum SyntheticGeoTiffCrs
{
    /// <summary>EPSG:4326 geographic; keys 1024 = 2 and 2048 = 4326.</summary>
    Wgs84Geographic = 1,

    /// <summary>EPSG:32631, WGS84 UTM zone 31N; keys 1024 = 1 and 3072 = 32631.</summary>
    Wgs84Utm31North = 2,

    /// <summary>EPSG:27700 British National Grid; written as-is and unsupported by the import contract.</summary>
    Epsg27700 = 3,
}

/// <summary>Strip or tile storage of the pixel data.</summary>
public enum SyntheticGeoTiffLayout
{
    /// <summary>Rows stored in strips of <see cref="SyntheticGeoTiffWriter.RowsPerStrip"/> rows.</summary>
    Stripped = 1,

    /// <summary>Stored as <see cref="SyntheticGeoTiffWriter.TileWidth"/> by <see cref="SyntheticGeoTiffWriter.TileHeight"/> tiles.</summary>
    Tiled = 2,
}

/// <summary>Deliberate tag defect injected into a fixture.</summary>
public enum SyntheticGeoTiffMalformation
{
    /// <summary>Well-formed tags.</summary>
    None = 0,

    /// <summary>Tag 33550 carries two doubles instead of three.</summary>
    PixelScaleTooShort = 1,

    /// <summary>Tag 33922 carries four doubles, not a multiple of six.</summary>
    TiepointTooShort = 2,

    /// <summary>Tag 34735 header count claims one key more than the directory contains.</summary>
    TruncatedGeoKeys = 3,

    /// <summary>Tag 33550 has a zero X scale, a degenerate affine.</summary>
    ZeroPixelScale = 4,
}

/// <summary>
/// Shape of a synthetic GeoTIFF. Defaults describe a small north-up, area-convention,
/// single-band float32 DEM in EPSG:4326 written with ModelPixelScale plus ModelTiepoint.
/// </summary>
public sealed record SyntheticGeoTiffOptions
{
    public int Width { get; init; } = 8;

    public int Height { get; init; } = 6;

    /// <summary>UInt8, Int16, UInt16, Int32, or Float32. Multi-band rasters must be UInt8.</summary>
    public RasterSampleFormat SampleFormat { get; init; } = RasterSampleFormat.Float32;

    /// <summary>1 for numeric rasters, 3 (RGB) or 4 (RGBA) for imagery.</summary>
    public int BandCount { get; init; } = 1;

    public SyntheticGeoTiffCrs Crs { get; init; } = SyntheticGeoTiffCrs.Wgs84Geographic;

    public SyntheticGeoTiffLayout Layout { get; init; } = SyntheticGeoTiffLayout.Stripped;

    /// <summary>Writes tag 34264 instead of tags 33550 plus 33922.</summary>
    public bool UseModelTransformation { get; init; }

    /// <summary>Positive row scale; row 0 is the southern edge, exposing vertical mirroring.</summary>
    public bool SouthUp { get; init; }

    /// <summary>Geokey 1025 = 2, tiepoint at the pixel center; readers must shift to area convention.</summary>
    public bool PixelIsPoint { get; init; }

    /// <summary>GDAL_NODATA (tag 42113) ASCII value, or null to omit the tag.</summary>
    public string NoData { get; init; }

    /// <summary>Vertical CRS written as geokey 4096, for example 5773 for EGM96 height.</summary>
    public int? VerticalCrsEpsg { get; init; }

    /// <summary>World X of the row 0, column 0 reference; null selects a CRS-specific default.</summary>
    public double? OriginX { get; init; }

    /// <summary>World Y of the row 0, column 0 reference; null selects a CRS-specific default.</summary>
    public double? OriginY { get; init; }

    /// <summary>Absolute column scale; null selects a CRS-specific default.</summary>
    public double? PixelSizeX { get; init; }

    /// <summary>Absolute row scale; null selects a CRS-specific default. Sign derives from <see cref="SouthUp"/>.</summary>
    public double? PixelSizeY { get; init; }

    public SyntheticGeoTiffMalformation Malformation { get; init; } = SyntheticGeoTiffMalformation.None;
}
