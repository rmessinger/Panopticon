using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Panopticon.Tools.TestHarness;

public sealed class RoutingChokeProgressMonitor
{
    private readonly Vector2[] _anchors;
    private readonly int _timeoutFrames;
    private int _lastProgressFrame;

    public RoutingChokeProgressMonitor(IReadOnlyList<Vector2> positions, int timeoutFrames)
    {
        _anchors = positions.ToArray();
        _timeoutFrames = timeoutFrames;
    }

    public string? Observe(int frame, IReadOnlyList<Vector2> positions, bool hasPaths, bool hasTimedGate)
    {
        for (var i = 0; i < positions.Count; i++)
        {
            // Accumulate slow movement, but ignore sub-unit position jitter.
            if (Vector2.DistanceSquared(positions[i], _anchors[i]) >= 1f)
            {
                _anchors[i] = positions[i];
                _lastProgressFrame = frame;
            }
        }

        if (hasTimedGate)
        {
            return null;
        }

        if (!hasPaths)
        {
            return "PathsExhausted";
        }

        return frame - _lastProgressFrame >= _timeoutFrames ? "NoProgress" : null;
    }
}
