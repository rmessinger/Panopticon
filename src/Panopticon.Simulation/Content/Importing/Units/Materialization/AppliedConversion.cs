namespace Panopticon.Content.Importing.Units.Materialization;

/// <summary>One source value and the runtime value it became, kept for inspection and evidence.</summary>
public sealed record AppliedConversion(ContentId Subject, string Field, Quantity Source, double Runtime, string RuntimeUnit);
