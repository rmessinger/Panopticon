using BitMiracle.LibTiff.Classic;

namespace Panopticon.Geospatial.Readers;

/// <summary>
/// Registers the GeoTIFF custom tags with LibTiff.NET's process-global directory extender
/// exactly once. Production readers and the synthetic fixture writer share this gate so the
/// two never race to install competing extenders; an extender installed before the first
/// registration is preserved in the chain.
/// </summary>
internal static class GeoTiffTagRegistration
{
    private static readonly TiffFieldInfo[] GeoTiffFieldInfos =
    {
        new(TiffTag.GEOTIFF_MODELPIXELSCALETAG, -1, -1, TiffType.DOUBLE, FieldBit.Custom, true, true, "ModelPixelScaleTag"),
        new(TiffTag.GEOTIFF_MODELTIEPOINTTAG, -1, -1, TiffType.DOUBLE, FieldBit.Custom, true, true, "ModelTiepointTag"),
        new(TiffTag.GEOTIFF_MODELTRANSFORMATIONTAG, -1, -1, TiffType.DOUBLE, FieldBit.Custom, true, true, "ModelTransformationTag"),
        new(TiffTag.GEOTIFF_GEOKEYDIRECTORYTAG, -1, -1, TiffType.SHORT, FieldBit.Custom, true, true, "GeoKeyDirectoryTag"),
        new(TiffTag.GEOTIFF_GEODOUBLEPARAMSTAG, -1, -1, TiffType.DOUBLE, FieldBit.Custom, true, true, "GeoDoubleParamsTag"),
        new(TiffTag.GEOTIFF_GEOASCIIPARAMSTAG, -1, -1, TiffType.ASCII, FieldBit.Custom, true, false, "GeoAsciiParamsTag"),
        new(TiffTag.GDAL_NODATA, -1, -1, TiffType.ASCII, FieldBit.Custom, true, false, "GDAL_NODATA"),
    };

    private static readonly object RegistrationGate = new();

    private static bool Registered;

    private static Tiff.TiffExtendProc? PreviousExtender;

    internal static bool IsRegistered
    {
        get
        {
            lock (RegistrationGate)
            {
                return Registered;
            }
        }
    }

    internal static void EnsureRegistered()
    {
        lock (RegistrationGate)
        {
            if (Registered)
            {
                return;
            }

            PreviousExtender = Tiff.SetTagExtender(ExtendDirectoryWithGeoTiffTags);
            Registered = true;
        }
    }

    private static void ExtendDirectoryWithGeoTiffTags(Tiff tiff)
    {
        tiff.MergeFieldInfo(GeoTiffFieldInfos, GeoTiffFieldInfos.Length);
        PreviousExtender?.Invoke(tiff);
    }
}
