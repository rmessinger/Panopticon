using System.Diagnostics;
using Panopticon.Logic.AI;
using Panopticon.Mathematics;

namespace Panopticon.Logic;

public enum PlayerOwner
{
    Player,
    EasyAi,
    MediumAi,
    HardAi,
    None
}


[DebuggerDisplay("[Player:{Name}]")]
public struct PlayerSetting
{
    public ColorRgb Color { get; set; }
    public int? StartPosition { get; set; }
    public string SideName { get; set; }
    public PlayerOwner Owner { get; set; }
    public string Name { get; set; }
    public int Team { get; set; }
    public bool IsLocalForMultiplayer { get; set; }

    /// <summary>Which AI decision policy this slot's AI player runs; null is the default
    /// rule-based behavior.</summary>
    public AIDecisionPolicyPayload AIPolicy { get; set; }

    public PlayerSetting(int? startPosition, string sideName, ColorRgb color, int team, PlayerOwner owner = PlayerOwner.None, string name = "", bool isLocalForMultiplayer = false)
    {
        StartPosition = startPosition;
        SideName = sideName;
        Color = color;
        Owner = owner;
        Name = name;
        Team = team;
        IsLocalForMultiplayer = isLocalForMultiplayer;
    }
}
