namespace Panopticon.Mathematics;

public readonly struct FloatRange
{
    public readonly float Low;
    public readonly float High;

    public FloatRange(float low, float high)
    {
        Low = low;
        High = high;
    }

    public float GetValue(IRandom random) => random.NextSingle(Low, High);
}
