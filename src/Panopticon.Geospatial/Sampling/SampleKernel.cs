namespace Panopticon.Geospatial.Sampling;

/// <summary>
/// Resampling kernels. <see cref="Nearest"/> is for categorical rasters;
/// <see cref="Auto"/> selects between continuous kernels from the ground-sample-distance ratio.
/// </summary>
public enum SampleKernel
{
    /// <summary>Bilinear when output spacing does not exceed source GSD on either axis,
    /// area-weighted when either axis decimates.</summary>
    Auto = 0,

    Nearest = 1,

    Bilinear = 2,

    AreaWeighted = 3,
}
