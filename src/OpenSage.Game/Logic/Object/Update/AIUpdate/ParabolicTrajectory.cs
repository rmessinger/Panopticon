using System;
using System.Numerics;

namespace OpenSage.Logic.Object;

/// <summary>
/// Vertical-plane parabola from a launch point to an impact point, computed once at launch.
/// The apex sits at <see cref="ApexHeight"/> =
/// <c>apexHeightFactor * horizontal launch-to-impact distance</c> above the chord midpoint, so
/// the apex height above ground is the same regardless of the endpoints' heights. Guided
/// missiles steer at the curve point a lookahead ahead of their current position, so the flight
/// tracks the parabola instead of merely homing on the impact point.
/// </summary>
internal readonly struct ParabolicTrajectory
{
    private readonly Vector3 _launch;
    private readonly Vector3 _impact;
    private readonly Vector3 _horizontalAxis;
    private readonly float _range;

    public ParabolicTrajectory(Vector3 launch, Vector3 impact, float apexHeightFactor)
    {
        _launch = launch;
        _impact = impact;
        var horizontal = new Vector3(impact.X - launch.X, impact.Y - launch.Y, 0f);
        _range = horizontal.Length();
        _horizontalAxis = _range > 0f ? horizontal / _range : Vector3.UnitX;
        ApexHeight = apexHeightFactor * _range;
    }

    public float ApexHeight { get; }

    /// <summary>Horizontal launch-to-impact distance.</summary>
    public float Range => _range;

    /// <summary>Horizontal progress of a position along the launch-to-impact axis, clamped to [0, range].</summary>
    public float Progress(Vector3 position)
    {
        var toPosition = position - _launch;
        var progress = toPosition.X * _horizontalAxis.X + toPosition.Y * _horizontalAxis.Y;
        return Math.Clamp(progress, 0f, _range);
    }

    /// <summary>The curve point at horizontal distance s from the launch point.</summary>
    public Vector3 PointAt(float s)
    {
        s = Math.Clamp(s, 0f, _range);
        var fraction = _range > 0f ? s / _range : 0f;

        var chordZ = _launch.Z + (_impact.Z - _launch.Z) * fraction;
        var arch = ApexHeight - (_launch.Z + _impact.Z) * 0.5f;
        var z = chordZ + arch * 4f * fraction * (1f - fraction);

        return new Vector3(
            _launch.X + _horizontalAxis.X * s,
            _launch.Y + _horizontalAxis.Y * s,
            z);
    }

    /// <summary>The steering target: the curve point lookahead ahead of the position's progress.</summary>
    public Vector3 SteerPoint(Vector3 position, float lookahead)
    {
        return PointAt(Progress(position) + lookahead);
    }
}
