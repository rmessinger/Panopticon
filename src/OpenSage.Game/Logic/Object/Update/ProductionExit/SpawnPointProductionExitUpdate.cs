using System;
using System.Numerics;
using OpenSage.Data.Ini;

namespace OpenSage.Logic.Object;

public sealed class SpawnPointProductionExitUpdate : UpdateModule, IProductionExit
{
    private readonly SpawnPointProductionExitUpdateModuleData _moduleData;
    private int _nextIndex;

    internal SpawnPointProductionExitUpdate(GameObject gameObject, IGameEngine gameEngine, SpawnPointProductionExitUpdateModuleData moduleData)
        : base(gameObject, gameEngine)
    {
        _moduleData = moduleData;

        _nextIndex = 1;
    }

    Vector3 IProductionExit.GetUnitCreatePoint()
    {
        if (!TryGetBonePose(_nextIndex, out var pose))
        {
            _nextIndex = 1;

            if (!TryGetBonePose(_nextIndex, out pose))
            {
                throw new InvalidOperationException("Could not find spawn point bone");
            }
        }

        _nextIndex++;

        return pose;
    }

    private bool TryGetBonePose(int index, out Vector3 translation)
    {
        if (GameObject.Client?.TryGetBonePose(_moduleData.SpawnPointBoneName + index.ToString("D2"), out var pose) == true)
        {
            translation = pose.Translation;
            return true;
        }

        translation = default;
        return false;
    }

    Vector3? IProductionExit.GetNaturalRallyPoint() => null;

    public override UpdateSleepTime Update()
    {
        // TODO(Port): Use correct value.
        return UpdateSleepTime.None;
    }
}

public sealed class SpawnPointProductionExitUpdateModuleData : UpdateModuleData
{
    internal static SpawnPointProductionExitUpdateModuleData Parse(IniParser parser) => parser.ParseBlock(FieldParseTable);

    private static readonly IniParseTable<SpawnPointProductionExitUpdateModuleData> FieldParseTable = new IniParseTable<SpawnPointProductionExitUpdateModuleData>
    {
        { "SpawnPointBoneName", (parser, x) => x.SpawnPointBoneName = parser.ParseBoneName() }
    };

    public string SpawnPointBoneName { get; private set; }

    internal override BehaviorModule CreateModule(GameObject gameObject, IGameEngine gameEngine)
    {
        return new SpawnPointProductionExitUpdate(gameObject, gameEngine, this);
    }
}
