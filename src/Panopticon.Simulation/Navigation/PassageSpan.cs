using System.Collections.Generic;
using System.Numerics;

namespace Panopticon.Navigation;

/// <summary>
/// One narrow passage a route crosses (WI-N11-01). ThroatCell and Corridor use grid cell
/// coordinates; Direction is the unit travel direction of the route through the span.
/// </summary>
internal sealed record PassageSpan(
    int EntryIndex,
    int ExitIndex,
    (int X, int Y) ThroatCell,
    int WidthCells,
    int Lanes,
    Vector2 Direction,
    IReadOnlyCollection<(int X, int Y)> Corridor);
