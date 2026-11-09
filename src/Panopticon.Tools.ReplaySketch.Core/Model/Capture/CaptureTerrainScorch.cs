using Panopticon.Content.Importing.Units.Materialization;
using Panopticon.FX;

namespace Panopticon.Tools.ReplaySketch.Model.Capture;

public sealed class CaptureTerrainScorch
{
    public float X { get; set; }
    public float Y { get; set; }
    // Legacy 40 units converted to meters at the model boundary.
    public float Radius { get; set; } = 40f * (float)UnitConversionPolicy.GeneralsFeetToMeters;
    public TerrainScorchType Type { get; set; } = TerrainScorchType.Scorch1;
}
