using Panopticon.FX;

namespace Panopticon.Tools.ReplaySketch.Model.Capture;

public sealed class CaptureTerrainScorch
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Radius { get; set; } = 40;
    public TerrainScorchType Type { get; set; } = TerrainScorchType.Scorch1;
}
