using System.Numerics;
using Panopticon.Data.Ini;
using Panopticon.Mathematics;

namespace Panopticon.FX;

public sealed class TracerFXNugget : FXNugget
{
    internal static TracerFXNugget Parse(IniParser parser) => parser.ParseBlock(FieldParseTable);

    private static readonly IniParseTable<TracerFXNugget> FieldParseTable = FXNuggetFieldParseTable.Concat(new IniParseTable<TracerFXNugget>
    {
        { "Color", (parser, x) => x.Color = parser.ParseColorRgb() },
        { "DecayAt", (parser, x) => x.DecayAt = parser.ParseFloat() },
        { "Length", (parser, x) => x.Length = parser.ParseFloat() },
        { "Probability", (parser, x) => x.Probability = parser.ParseFloat() },
        { "Speed", (parser, x) => x.Speed = parser.ParseInteger() },
        { "Width", (parser, x) => x.Width = parser.ParseFloat() },
    });

    public ColorRgb Color { get; private set; }

    /// <summary>Fraction of the tracer's flight at which fade-out begins. Data uses 0.5-1.</summary>
    public float DecayAt { get; private set; } = 1.0f;

    public float Length { get; private set; }

    /// <summary>Chance that any given shot spawns a tracer. Absent in data means always.</summary>
    public float Probability { get; private set; } = 1.0f;

    public int Speed { get; private set; }

    public float Width { get; private set; }

    internal override void Execute(FXListExecutionContext context)
    {
        if (Probability < 1.0f &&
            context.GameEngine.GameClient.Random.NextSingle(0, 1) >= Probability)
        {
            return;
        }

        // Fire-bone transforms orient projectiles along local +X (BezierProjectileBehavior),
        // so the same axis is the tracer direction.
        var direction = Vector3.Transform(Vector3.UnitX, context.Rotation);

        context.GameEngine.Scene3D?.TracerManager.Create(
            context.Position,
            direction,
            Color,
            Length,
            Width,
            Speed,
            DecayAt);
    }
}
