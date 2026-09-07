using System.Diagnostics;
using OpenSage.Logic.AI;
using OpenSage.Mathematics;

namespace OpenSage.Logic;

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

    /// <summary>Which AI decision policy this slot's AI player runs; the default matches
    /// pre-seam skirmish AI behavior.</summary>
    public AIDecisionPolicyType AIPolicy { get; set; } = AIDecisionPolicyType.RuleBased;

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
