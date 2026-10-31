namespace Panopticon.Geospatial.Interchange;

/// <summary>
/// Sample layout of a raster in its source format. Readers normalize numeric samples to
/// double but retain this metadata so later imagery layout validation can check the
/// original encoding.
/// </summary>
public enum RasterSampleFormat
{
    Unknown = 0,

    UInt8 = 1,

    Int16 = 2,

    UInt16 = 3,

    Int32 = 4,

    UInt32 = 5,

    Float32 = 6,

    Float64 = 7,
}
