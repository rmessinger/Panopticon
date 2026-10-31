namespace Panopticon.Geospatial.Sampling;

/// <summary>
/// Kernel scaling report for one sample. Source GSD is the local source cell step in target
/// meters; output spacing is the footprint mapped onto the source axes (the source-pixel
/// quad's axis extent times the local GSD), so the ratios equal the quad's extent in source
/// cells.
/// </summary>
public sealed record KernelSelection(
    SampleKernel Kernel,
    double SourceGsdXMeters,
    double SourceGsdYMeters,
    double OutputSpacingXMeters,
    double OutputSpacingYMeters)
{
    /// <summary>Output spacing divided by source GSD per axis; the GSD is measured through the affine even on single-cell axes.</summary>
    public double GsdRatioX => OutputSpacingXMeters / SourceGsdXMeters;

    public double GsdRatioY => OutputSpacingYMeters / SourceGsdYMeters;
}
