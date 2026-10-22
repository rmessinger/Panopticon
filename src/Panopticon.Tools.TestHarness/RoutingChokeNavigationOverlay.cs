using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Panopticon.Logic.Object;

namespace Panopticon.Tools.TestHarness;

public sealed record RoutingChokeNavigationOverlay(
    float CellSize,
    IReadOnlyList<RoutingChokePartition> Cells,
    IReadOnlyList<RoutingChokeUnitOverlay> Units)
{
    public static RoutingChokeNavigationOverlay Capture(Game game, IReadOnlyList<GameObject> units)
    {
        RoutingChokeScreenPoint Project(Vector3 point)
        {
            var screen = game.Scene3D.Camera.WorldToScreenPoint(point);
            return new RoutingChokeScreenPoint(screen.X, screen.Y);
        }

        RoutingChokeScreenPoint Ground(float x, float y) =>
            Project(new Vector3(x, y, game.Scene3D.Terrain.GetGroundHeight(new Vector2(x, y)) ?? 0));

        var manager = game.PartitionCellManager;
        var cells = new List<RoutingChokePartition>();
        for (var y = 0; y < manager.NumCellsY; y++)
        {
            for (var x = 0; x < manager.NumCellsX; x++)
            {
                var cell = manager[x, y];
                var bounds = cell.WorldSpaceBounds;
                if (bounds.Right < 450 || bounds.Left > 1550 || bounds.Bottom < 350 || bounds.Top > 1500)
                {
                    continue;
                }

                var members = cell.Objects.Select(member => member.GameObject).ToArray();
                cells.Add(new RoutingChokePartition(x, y,
                    [Ground(bounds.Left, bounds.Top), Ground(bounds.Right, bounds.Top),
                        Ground(bounds.Right, bounds.Bottom), Ground(bounds.Left, bounds.Bottom)],
                    members.Select(member => member.Id.Index).ToArray(),
                    members.Count(member => member.IsStructure)));
            }
        }

        var overlays = new List<RoutingChokeUnitOverlay>();
        foreach (var unit in units)
        {
            var radius = unit.Geometry.BoundingCircleRadius;
            var footprint = new List<RoutingChokeScreenPoint>();
            for (var step = 0; step < 32; step++)
            {
                var angle = step * System.MathF.Tau / 32;
                footprint.Add(Ground(unit.Translation.X + System.MathF.Cos(angle) * radius,
                    unit.Translation.Y + System.MathF.Sin(angle) * radius));
            }

            var path = new List<RoutingChokeScreenPoint> { Project(unit.Translation) };
            if (unit.AIUpdate?.TargetPoints != null)
            {
                path.AddRange(unit.AIUpdate.TargetPoints.Select(Project));
            }

            overlays.Add(new RoutingChokeUnitOverlay(unit.Id.Index, radius, footprint, path));
        }

        return new RoutingChokeNavigationOverlay(manager.PartitionCellSize, cells, overlays);
    }
}

public sealed record RoutingChokeScreenPoint(float X, float Y);

public sealed record RoutingChokePartition(
    int X, int Y, IReadOnlyList<RoutingChokeScreenPoint> Corners,
    IReadOnlyList<uint> ObjectIds, int Structures);

public sealed record RoutingChokeUnitOverlay(
    uint Id, float Radius, IReadOnlyList<RoutingChokeScreenPoint> Footprint,
    IReadOnlyList<RoutingChokeScreenPoint> Path);
