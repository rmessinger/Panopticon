namespace OpenSage.Tools.ReplaySketch.Assertions;

public sealed class AssertionResult
{
    public string AssertionId { get; init; } = string.Empty;
    public bool Passed { get; init; }
    public string? ActualValue { get; init; }
    public uint? FailFrame { get; init; }
    public string? Message { get; init; }
}
