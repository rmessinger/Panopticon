using System.Numerics;
using Panopticon.Data.Ini;

namespace Panopticon.Logic.Object;

public sealed class CleanupAreaPower : SpecialPowerModule
{
    private readonly CleanupAreaPowerModuleData _moduleData;

    internal CleanupAreaPower(GameObject gameObject, IGameEngine gameEngine, CleanupAreaPowerModuleData moduleData) : base(gameObject, gameEngine, moduleData)
    {
        _moduleData = moduleData;
    }

    /// <summary>
    /// Cleans the area around the location: heals every friendly object inside the power's
    /// <c>RadiusCursorRadius</c>. The original power also clears toxin and radiation hazard fields;
    /// hazard persistence is not modelled yet, so this heals only.
    /// </summary>
    internal new void Activate(Vector3 location)
    {
        base.Activate(location);

        var radius = Template.RadiusCursorRadius;

        foreach (var obj in GameEngine.GameLogic.Objects)
        {
            if (obj == GameObject || obj.IsEffectivelyDead || obj.BodyModule == null)
            {
                continue;
            }

            // Friendlies only: same owner or same team.
            if (obj.Owner != GameObject.Owner && obj.Team != GameObject.Team)
            {
                continue;
            }

            if (Vector3.Distance(obj.Transform.Translation, location) > radius)
            {
                continue;
            }

            obj.AttemptDamage(new DamageInfoInput(GameObject)
            {
                DamageType = DamageType.Healing,
                DeathType = DeathType.None,
                Amount = obj.BodyModule.MaxHealth,
            });
        }
    }

    internal override void Load(StatePersister reader)
    {
        reader.PersistVersion(1);

        reader.BeginObject("Base");
        base.Load(reader);
        reader.EndObject();

        // no idea what these are for
        byte unknown1 = 0;
        reader.PersistByte(ref unknown1);
        byte unknown2 = 0;
        reader.PersistByte(ref unknown2);
        byte unknown3 = 0;
        reader.PersistByte(ref unknown3);

        reader.SkipUnknownBytes(14);
    }
}

public sealed class CleanupAreaPowerModuleData : SpecialPowerModuleData
{
    internal static new CleanupAreaPowerModuleData Parse(IniParser parser) => parser.ParseBlock(FieldParseTable);

    private static new readonly IniParseTable<CleanupAreaPowerModuleData> FieldParseTable = SpecialPowerModuleData.FieldParseTable
        .Concat(new IniParseTable<CleanupAreaPowerModuleData>
        {
            { "MaxMoveDistanceFromLocation", (parser, x) => x.MaxMoveDistanceFromLocation = parser.ParseFloat() }
        });

    public float MaxMoveDistanceFromLocation { get; private set; }

    internal override CleanupAreaPower CreateModule(GameObject gameObject, IGameEngine gameEngine)
    {
        return new CleanupAreaPower(gameObject, gameEngine, this);
    }
}
