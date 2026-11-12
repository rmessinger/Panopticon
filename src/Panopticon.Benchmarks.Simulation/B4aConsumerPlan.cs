namespace Panopticon.Benchmarks.Simulation;

public sealed record B4aConsumerPlan(int ObjectCount = 10_000, int PlayFrames = 300, int Repeats = 3, int PlayableSize = 500)
{
    public const int BorderWidth = 6;

    public static B4aConsumerPlan FullScale { get; } = new(ObjectCount: 10_000, PlayFrames: 300, Repeats: 3, PlayableSize: 4096);

    public static B4aConsumerPlan SmokeScale { get; } = new(ObjectCount: 200, PlayFrames: 10);

    public int LatticeSize => PlayableSize + (2 * BorderWidth) + 1;
}
