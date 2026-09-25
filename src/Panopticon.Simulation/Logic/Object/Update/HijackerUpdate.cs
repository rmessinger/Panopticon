using System;
using Panopticon.Data.Ini;

namespace Panopticon.Logic.Object;

public sealed class HijackerUpdate : UpdateModule
{
    // Contact distance for the hijack: the ConvertToHijackedVehicleCrateCollide crate trigger
    // fires on overlap in the original game; the hijacker's footprint is small, so this covers
    // the approach distance the locomotor stops at.
    private const float HijackContactDistance = 20f;

    public HijackerUpdate(GameObject gameObject, IGameEngine gameEngine) : base(gameObject, gameEngine)
    {
    }

    public override UpdateSleepTime Update()
    {
        if (GameObject.IsEffectivelyDead)
        {
            return UpdateSleepTime.Forever;
        }

        // Context-mode ability: no special-power order path. The hijacker captures the first enemy
        // vehicle it comes into contact with (RequiredKindOf = VEHICLE on
        // ConvertToHijackedVehicleCrateCollide): ownership transfers to the hijacker's player and
        // the hijacker is consumed.
        if (GameEngine.Quadtree != null)
        {
            foreach (var candidate in GameEngine.Quadtree.FindNearby(GameObject, GameObject.Transform, HijackContactDistance))
            {
                if (TryHijack(candidate))
                {
                    break;
                }
            }
        }

        return UpdateSleepTime.None;
    }

    private bool TryHijack(GameObject candidate)
    {
        // Hijackable while it belongs to anyone but the hijacker's own side (enemy or civilian
        // vehicles are both valid targets in the original game). Team data is unreliable on
        // sandbox-spawned objects, so the check is owner-based.
        if (candidate == GameObject
            || candidate.IsEffectivelyDead
            || !candidate.IsKindOf(ObjectKinds.Vehicle)
            || candidate.IsKindOf(ObjectKinds.Aircraft)
            || candidate.Owner == GameObject.Owner)
        {
            return false;
        }

        if (GameObject.Owner.DefaultTeam != null)
        {
            candidate.Team = GameObject.Owner.DefaultTeam;
        }

        candidate.Owner = GameObject.Owner;
        GameObject.Kill();

        return true;
    }

    internal override void Load(StatePersister reader)
    {
        reader.PersistVersion(1);

        reader.BeginObject("Base");
        base.Load(reader);
        reader.EndObject();

        reader.SkipUnknownBytes(19);
    }
}

public sealed class HijackerUpdateModuleData : UpdateModuleData
{
    internal static HijackerUpdateModuleData Parse(IniParser parser) => parser.ParseBlock(FieldParseTable);

    private static readonly IniParseTable<HijackerUpdateModuleData> FieldParseTable = new IniParseTable<HijackerUpdateModuleData>
    {
        { "ParachuteName", (parser, x) => x.ParachuteName = parser.ParseAssetReference() }
    };

    public string ParachuteName { get; private set; }

    internal override HijackerUpdate CreateModule(GameObject gameObject, IGameEngine gameEngine)
    {
        return new HijackerUpdate(gameObject, gameEngine);
    }
}
