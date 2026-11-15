namespace Panopticon.Benchmarks.Simulation;

public sealed record B4aConsumerPlan(int ObjectCount = 10_000, int PlayFrames = 300, int Repeats = 3)
{
    public static B4aConsumerPlan FullScale { get; } = new();

    public static B4aConsumerPlan SmokeScale { get; } = new(ObjectCount: 200, PlayFrames: 10);
}
