namespace Panopticon.Content.Importing.Units;

/// <summary>A source value in its declared unit. Conversion happens at materialization and is recorded there.</summary>
public readonly record struct Quantity(double Value, QuantityUnit Unit)
{
    public override string ToString() => $"{Value} {Unit}";
}
