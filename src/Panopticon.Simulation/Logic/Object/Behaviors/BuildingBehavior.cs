using System.Collections.Generic;
using Panopticon.Data.Ini;

namespace Panopticon.Logic.Object;

[AddedIn(SageGame.Bfme)]
public class BuildingBehavior : UpdateModule
{
    private readonly BuildingBehaviorModuleData _moduleData;
    private bool _initial;
    private bool _isNight;
    private bool _fire;
    private bool _isGlowing;

    internal BuildingBehavior(GameObject gameObject, IGameEngine gameEngine, BuildingBehaviorModuleData moduleData)
        : base(gameObject, gameEngine)
    {
        _moduleData = moduleData;

        _initial = true;
        _isNight = false;
        _fire = false;
        _isGlowing = false;
    }

    public override UpdateSleepTime Update()
    {
        // TODO: get proper values for these
        var night = false;
        var fire = false;
        var glowing = false;

        if (night != _isNight || _initial)
        {
            var nightWindow = _moduleData.NightWindowName;
            if (night)
            {
                GameObject.Client?.ShowSubObject(nightWindow);
            }
            else
            {
                GameObject.Client?.HideSubObject(nightWindow);
            }

            _isNight = night;
        }

        if (fire != _fire || _initial)
        {
            var fireWindow = _moduleData.FireWindowName;
            if (fire)
            {
                GameObject.Client?.ShowSubObject(fireWindow);
                foreach (var fireName in _moduleData.FireNames)
                {
                    GameObject.Client?.ShowSubObject(fireName);
                }
            }
            else
            {
                GameObject.Client?.HideSubObject(fireWindow);

                foreach (var fireName in _moduleData.FireNames)
                {
                    GameObject.Client?.HideSubObject(fireName);
                }
            }

            _fire = fire;
        }

        if (glowing != _isGlowing || _initial)
        {
            var glowWindow = _moduleData.GlowWindowName;
            if (glowing)
            {
                GameObject.Client?.ShowSubObject(glowWindow);
            }
            else
            {
                GameObject.Client?.HideSubObject(glowWindow);
            }

            _isGlowing = glowing;
        }

        if (_initial)
        {
            _initial = false;
        }

        // TODO(Port): Use correct value.
        return UpdateSleepTime.None;
    }
}


public sealed class BuildingBehaviorModuleData : BehaviorModuleData
{
    internal static BuildingBehaviorModuleData Parse(IniParser parser) => parser.ParseBlock(FieldParseTable);

    private static readonly IniParseTable<BuildingBehaviorModuleData> FieldParseTable = new IniParseTable<BuildingBehaviorModuleData>
    {
        { "NightWindowName", (parser, x) => x.NightWindowName = parser.ParseString() },
        { "FireWindowName", (parser, x) => x.FireWindowName = parser.ParseString() },
        { "GlowWindowName", (parser, x) => x.GlowWindowName = parser.ParseString() },
        { "FireName", (parser, x) => x.FireNames.Add(parser.ParseString()) },
    };

    public string NightWindowName { get; private set; }
    public string FireWindowName { get; private set; }
    public string GlowWindowName { get; private set; }
    public List<string> FireNames { get; private set; } = new List<string>();

    internal override BehaviorModule CreateModule(GameObject gameObject, IGameEngine gameEngine)
    {
        return new BuildingBehavior(gameObject, gameEngine, this);
    }
}
