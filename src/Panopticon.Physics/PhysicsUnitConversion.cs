using System.Numerics;

namespace Panopticon.Physics;

/// <summary>
/// Conversions between the SAGE per-logic-frame units used by GameData and
/// per-second units used by real-time backends.
/// </summary>
public static class PhysicsUnitConversion
{
    public static float PerFrameSquaredToPerSecondSquared(float valuePerFrameSquared, float logicFramesPerSecond)
    {
        return valuePerFrameSquared * logicFramesPerSecond * logicFramesPerSecond;
    }

    public static float PerFrameToPerSecond(float valuePerFrame, float logicFramesPerSecond)
    {
        return valuePerFrame * logicFramesPerSecond;
    }

    public static float PerSecondToPerFrame(float valuePerSecond, float logicFramesPerSecond)
    {
        return valuePerSecond / logicFramesPerSecond;
    }
}
