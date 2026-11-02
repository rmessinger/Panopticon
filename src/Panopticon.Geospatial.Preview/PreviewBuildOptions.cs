namespace Panopticon.Geospatial.Preview;

/// <summary>Resident build options of the preview builder.</summary>
public sealed class PreviewBuildOptions
{
    /// <summary>
    /// Default cap on stored height cells: 1,048,576, border padding and the final core
    /// edge included (B4b group 5 decision 5). Checked before any allocation; an oversized
    /// whole map is an error, never auto-cropped.
    /// </summary>
    public const long DefaultMaxStoredHeightCells = 1_048_576;

    /// <summary>Extra index padding per side; zero by default (B4b deep dive section 6.1).</summary>
    public uint BorderWidth { get; set; }

    public long MaxStoredHeightCells { get; set; } = DefaultMaxStoredHeightCells;
}
