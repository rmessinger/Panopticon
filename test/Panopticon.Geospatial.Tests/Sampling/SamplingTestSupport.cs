using System;
using Panopticon.Geospatial.Interchange;
using Panopticon.Geospatial.Sampling;

namespace Panopticon.Geospatial.Tests.Sampling;

internal static class SamplingTestSupport
{
    public static readonly CoordinateSystemId Utm31 = CoordinateSystemId.Utm(31, northernHemisphere: true);

    /// <summary>Metric target CRS distinct from any test source CRS.</summary>
    public static readonly CoordinateSystemId MetricTarget = CoordinateSystemId.TransverseMercator(3d, 1d, 0d, 0d);

    public static readonly CoordinateSystemId Wgs84 = CoordinateSystemId.Wgs84;

    /// <summary>Identity between equal CRS ids; scales degrees to meters between EPSG:4326 and any projected CRS.</summary>
    public static ICoordinateTransformer ScalingTransformer(double metersPerDegreeX, double metersPerDegreeY)
    {
        return new DegreeScalingTransformer(metersPerDegreeX, metersPerDegreeY);
    }

    /// <summary>Quarter-turn between the UTM source CRS and any other projected CRS; identity otherwise.</summary>
    public static ICoordinateTransformer RotatingTransformer()
    {
        return new QuarterTurnTransformer();
    }

    public static ICoordinateTransformer IdentityTransformer()
    {
        return new DegreeScalingTransformer(1d, 1d);
    }

    public static AffineTransform NorthUp(double originX, double originY, double stepX, double stepY)
    {
        return new AffineTransform(originX, stepX, 0d, originY, 0d, -stepY);
    }

    public static AffineTransform SouthUp(double originX, double originY, double stepX, double stepY)
    {
        return new AffineTransform(originX, stepX, 0d, originY, 0d, stepY);
    }

    public static GeospatialRaster<double> Raster(
        int width,
        int height,
        AffineTransform transform,
        CoordinateSystemId coordinateSystem,
        Func<int, int, double> value,
        double noData = 0d,
        bool hasNoData = false,
        int bandCount = 1)
    {
        var samples = new double[width * height * bandCount];

        for (var row = 0; row < height; row++)
        {
            for (var column = 0; column < width; column++)
            {
                for (var band = 0; band < bandCount; band++)
                {
                    samples[(row * width + column) * bandCount + band] = value(column, row);
                }
            }
        }

        return new GeospatialRaster<double>(
            width,
            height,
            bandCount,
            transform,
            coordinateSystem,
            samples,
            noData,
            hasNoData);
    }

    public static SampleFootprint Footprint(
        double centerX,
        double centerY,
        double spacingX,
        double spacingY,
        CoordinateSystemId? target = null)
    {
        return new SampleFootprint(
            new ProjectedBounds(
                centerX - (spacingX / 2d),
                centerY - (spacingY / 2d),
                centerX + (spacingX / 2d),
                centerY + (spacingY / 2d)),
            target ?? Utm31);
    }

    private sealed class QuarterTurnTransformer : ICoordinateTransformer
    {
        public Vector2d Transform(Vector2d point, CoordinateSystemId source, CoordinateSystemId target)
        {
            if (source == target)
            {
                return point;
            }

            var sourceIsUtm = source.EpsgCode == Utm31.EpsgCode;

            return sourceIsUtm
                ? new Vector2d(-point.Y, point.X)
                : new Vector2d(point.Y, -point.X);
        }
    }

    private sealed class DegreeScalingTransformer(double metersPerDegreeX, double metersPerDegreeY) : ICoordinateTransformer
    {
        public Vector2d Transform(Vector2d point, CoordinateSystemId source, CoordinateSystemId target)
        {
            var sourceGeographic = source.EpsgCode == CoordinateSystemId.Wgs84Epsg;
            var targetGeographic = target.EpsgCode == CoordinateSystemId.Wgs84Epsg;

            if (sourceGeographic == targetGeographic)
            {
                if (source == target)
                {
                    return point;
                }

                throw new NotSupportedException($"Unsupported coordinate pair {source} to {target}.");
            }

            return sourceGeographic
                ? new Vector2d(point.X * metersPerDegreeX, point.Y * metersPerDegreeY)
                : new Vector2d(point.X / metersPerDegreeX, point.Y / metersPerDegreeY);
        }
    }
}
