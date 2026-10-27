using System.Numerics;
using Panopticon.Data.Ini;
using Panopticon.Terrain.Decals;

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

    internal override void Execute(FXListExecutionContext context)
    {
        var client = context.GameEngine.GameClient;
        var terrain = context.GameEngine.Terrain;
        if (client == null || terrain == null)
        {
            return;
        }

        var type = Type == TerrainScorchType.Random
            ? (TerrainScorchType)client.Random.Next((int)TerrainScorchType.Scorch1, (int)TerrainScorchType.Scorch4)
            : Type;
        terrain.Decals.Limit = context.GameEngine.Game.EffectiveGraphicsSettings.TerrainDecalLimit;
        terrain.Decals.Add(TerrainDecalTemplate.Scorch, context.Position, Radius, (int)type);
    }
}
