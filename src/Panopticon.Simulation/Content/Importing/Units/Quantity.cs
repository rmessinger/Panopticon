namespace Panopticon.Content.Importing.Units;

/// <summary>A source value in its declared unit. Conversion happens at materialization and is recorded there.</summary>
public readonly record struct Quantity(double Value, QuantityUnit Unit)
{
    public override string ToString() => $"{Value} {Unit}";

    public static Quantity FromMeters(double value) => new(value, QuantityUnit.Meters);
    public static Quantity FromMetersPerSecond(double value) => new(value, QuantityUnit.MetersPerSecond);
    public static Quantity FromMetersPerSecondSquared(double value) => new(value, QuantityUnit.MetersPerSecondSquared);
    public static Quantity FromGameDistance(double value) => new(value, QuantityUnit.GameDistance);
    public static Quantity FromSourceDistance(double value) => new(value, QuantityUnit.SourceDistance);
    public static Quantity FromSourceDistancePerSecond(double value) => new(value, QuantityUnit.SourceDistancePerSecond);
    public static Quantity FromSeconds(double value) => new(value, QuantityUnit.Seconds);
    public static Quantity FromLogicFrames(double value) => new(value, QuantityUnit.LogicFrames);
    public static Quantity FromDegreesPerSecond(double value) => new(value, QuantityUnit.DegreesPerSecond);

    public bool IsMetricDistance => Unit is QuantityUnit.Meters;
    public bool IsMetricSpeed => Unit is QuantityUnit.MetersPerSecond;
    public bool IsMetricAcceleration => Unit is QuantityUnit.MetersPerSecondSquared;
    public bool IsMetric => Unit is QuantityUnit.Meters or QuantityUnit.MetersPerSecond or QuantityUnit.MetersPerSecondSquared;
}
