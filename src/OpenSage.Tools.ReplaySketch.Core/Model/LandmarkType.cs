namespace OpenSage.Tools.ReplaySketch.Model;

public enum LandmarkType
{
    /// <summary>Raw 0-1 map-fraction coordinates — no landmark anchor.</summary>
    MapNormalized,

    /// <summary>The player's own Player_N_Start waypoint world position.</summary>
    OwnBase,

    /// <summary>The opponent's Player_N_Start waypoint world position.</summary>
    EnemyBase,

    /// <summary>Centre of the map: (ExtentMin + ExtentMax) / 2.</summary>
    MapCenter,
}
