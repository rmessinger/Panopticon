namespace Panopticon.Content.Importing.Units;

public enum QuantityUnit
{
    Scalar,
    HitPoints,
    GameDistance,
    /// <summary>A source game's own distance unit (for example an EE2 tile); converted by a declared scale.</summary>
    SourceDistance,
    SourceDistancePerSecond,
    Meters,
    Seconds,
    Milliseconds,
    LogicFrames,
    DistancePerSecond,
    DistancePerSecondSquared,
    DegreesPerSecond,
    Degrees,
    Radians,
    Currency,
    Ratio,
}
