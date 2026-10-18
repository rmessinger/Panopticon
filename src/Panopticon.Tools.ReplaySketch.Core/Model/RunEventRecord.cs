namespace Panopticon.Tools.ReplaySketch.Model;

/// <summary>
/// WI-A5-09: one structured timeline event in a run bundle's event stream. Frame is the
/// simulation frame at raise time; category names the event family; the remaining fields carry
/// player and entity identity where the event has one, plus an optional action-type detail.
/// </summary>
public sealed record RunEventRecord(
    uint Frame,
    string Category,
    string? ActionType,
    string? PlayerName,
    int? PlayerIndex,
    string? DefinitionName,
    uint? ObjectId);
