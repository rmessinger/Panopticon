using System.Collections.Generic;

namespace Panopticon.Tools.TestHarness;

/// <summary>Routing-choke run report: arrival statistics for one group move through the gap.</summary>
public sealed record RoutingChokeReport(
    string Scenario,
    string MapPath,
    int UnitCount,
    int GapWidth,
    int FramesRun,
    int FrameLimit,
    int Arrived,
    int? FirstArrivalFrame,
    int? MedianArrivalFrame,
    int? LastArrivalFrame,
    IReadOnlyList<int> StuckUnitIndexes,
    long BlockedFollowerDelta,
    long PathRequestDelta,
    IReadOnlyList<RoutingChokeUnitRecord> Units);

/// <summary>Per-unit outcome: start and end positions plus the arrival frame, null when stuck.</summary>
public sealed record RoutingChokeUnitRecord(
    int Id,
    float StartX,
    float StartY,
    float EndX,
    float EndY,
    float Displacement,
    float DistanceToDestination,
    int? ArrivalFrame);

/// <summary>One captured PNG: sequence index, logic frame, file name, unit screen positions.</summary>
public sealed record RoutingChokeCaptureFrame(
    int Index,
    int AbsoluteLogicFrame,
    string File,
    IReadOnlyList<RoutingChokeManifestActor> Actors,
    IReadOnlyList<string> Projectiles,
    RoutingChokeNavigationOverlay Navigation);

/// <summary>Minimal actor entry for the GIF tool's content-bounds crop.</summary>
public sealed record RoutingChokeManifestActor(
    string Role,
    string Label,
    float X,
    float Y,
    float RadiusPixels);

/// <summary>Minimal capture manifest consumed by scripts/annotate_capture_gif.py.</summary>
public sealed record RoutingChokeCaptureManifest(
    int ViewportWidth,
    int ViewportHeight,
    IReadOnlyList<RoutingChokeCaptureFrame> Frames);
