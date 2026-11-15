namespace Panopticon.Benchmarks.Simulation;

/// <summary>
/// Object-state facts observed in one repeat, read from the live editor and loaded package state.
/// Counts are measured from the objects themselves, not derived from the plan.
/// </summary>
public sealed record B4aRepeatObservation(
    int AuthoredObjectCount,
    int BrushCenterElevationDelta,
    int RegionMovedCount,
    int RegionDeletedCount,
    int CountAfterDelete,
    int CountAfterUndo,
    bool RegionUndoRestoredAuthoredState,
    int LoadedObjectCount,
    bool LoadedObjectsMatchSaved,
    int PlayFramesCompleted,
    int MeshMoldUndoEntriesAdded,
    int MeshMoldChangedCellCount,
    bool MeshMoldUndoRestoredPreStroke,
    bool MeshMoldRedoReappliedStroke,
    int SavedPropertyCount,
    bool LoadedTypesMatchPlan,
    bool LoadedHeightsMatchSaved,
    bool LoadedBlendTilesMatchSaved);
