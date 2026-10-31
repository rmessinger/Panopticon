using System;
using System.Globalization;

namespace Panopticon.Geospatial.Interchange;

/// <summary>
/// Affine mapping from continuous pixel space (X = column, Y = row) to world coordinates,
/// following the GDAL coefficient order. Pixel (0, 0) is the top-left corner of the
/// top-left sample; sample centers lie at half-integer pixel coordinates, so sampling code
/// adds 0.5 to integer column and row before mapping. The world position of pixel
/// (column, row) is (OriginX + column * ScaleX + row * RowOffsetX,
/// OriginY + column * ColumnOffsetY + row * ScaleY).
/// </summary>
public sealed record AffineTransform
{
    public AffineTransform(
        double originX,
        double scaleX,
        double rowOffsetX,
        double originY,
        double columnOffsetY,
        double scaleY)
    {
        if (!double.IsFinite(originX) || !double.IsFinite(scaleX) || !double.IsFinite(rowOffsetX)
            || !double.IsFinite(originY) || !double.IsFinite(columnOffsetY) || !double.IsFinite(scaleY))
        {
            throw new ArgumentException("Affine coefficients must be finite.");
        }

        var determinant = scaleX * scaleY - rowOffsetX * columnOffsetY;
        if (!double.IsFinite(determinant) || determinant == 0d)
        {
            throw new ArgumentException(
                $"Affine determinant must be finite and nonzero, was {determinant.ToString(CultureInfo.InvariantCulture)}.");
        }

        OriginX = originX;
        ScaleX = scaleX;
        RowOffsetX = rowOffsetX;
        OriginY = originY;
        ColumnOffsetY = columnOffsetY;
        ScaleY = scaleY;
    }

    /// <summary>World X of pixel (0, 0).</summary>
    public double OriginX { get; }

    /// <summary>World X per column step.</summary>
    public double ScaleX { get; }

    /// <summary>World X per row step (rotation/shear; zero for axis-aligned rasters).</summary>
    public double RowOffsetX { get; }

    /// <summary>World Y of pixel (0, 0).</summary>
    public double OriginY { get; }

    /// <summary>World Y per column step (rotation/shear; zero for axis-aligned rasters).</summary>
    public double ColumnOffsetY { get; }

    /// <summary>World Y per row step (negative for north-up rasters).</summary>
    public double ScaleY { get; }

    /// <summary>Determinant of the linear part; finite and nonzero by construction.</summary>
    public double Determinant => ScaleX * ScaleY - RowOffsetX * ColumnOffsetY;

    /// <summary>
    /// True when the raster is axis-aligned with positive column scale and negative row scale,
    /// that is row 0 is the northern edge.
    /// </summary>
    public bool IsNorthUp => RowOffsetX == 0d && ColumnOffsetY == 0d && ScaleX > 0d && ScaleY < 0d;

    public Vector2d PixelToWorld(Vector2d pixel)
    {
        return new Vector2d(
            OriginX + pixel.X * ScaleX + pixel.Y * RowOffsetX,
            OriginY + pixel.X * ColumnOffsetY + pixel.Y * ScaleY);
    }

    public Vector2d WorldToPixel(Vector2d world)
    {
        var offsetX = world.X - OriginX;
        var offsetY = world.Y - OriginY;
        var determinant = Determinant;

        return new Vector2d(
            (offsetX * ScaleY - RowOffsetX * offsetY) / determinant,
            (ScaleX * offsetY - offsetX * ColumnOffsetY) / determinant);
    }
}
