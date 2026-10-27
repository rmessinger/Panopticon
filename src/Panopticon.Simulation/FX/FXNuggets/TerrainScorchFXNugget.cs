using System.Numerics;
using Panopticon.Data.Ini;

namespace Panopticon.FX;

public sealed class TerrainScorchFXNugget : FXNugget
{
    internal static TerrainScorchFXNugget Parse(IniParser parser) => parser.ParseBlock(FieldParseTable);

    private static readonly IniParseTable<TerrainScorchFXNugget> FieldParseTable = FXNuggetFieldParseTable.Concat(new IniParseTable<TerrainScorchFXNugget>
    {
        { "Type", (parser, x) => x.Type = parser.ParseEnum<TerrainScorchType>() },
        { "Radius", (parser, x) => x.Radius = parser.ParseFloat() },
        { "RandomRange", (parser, x) => x.RandomRange = parser.ParseVector2() }
    });

    public TerrainScorchType Type { get; private set; }
    public float Radius { get; private set; }

    [AddedIn(SageGame.Bfme2)]
    public Vector2 RandomRange { get; private set; }

    internal override void Execute(FXListExecutionContext context) =>
        context.GameEngine.GameClient?.AddScorch(context.Position, Radius, Type);
}
