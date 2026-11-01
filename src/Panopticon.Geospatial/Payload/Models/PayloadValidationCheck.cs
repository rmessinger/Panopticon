using System;

namespace Panopticon.Geospatial.Payload.Models;

/// <summary>Outcome of one staged validation check.</summary>
public enum PayloadValidationStatus
{
    Passed,

    Failed,

    NotEvaluated,
}

/// <summary>
/// One generic validation check record. RGB comparisons use one check per channel. Any
/// <see cref="PayloadValidationStatus.Failed"/> check blocks commit, even when the
/// validator adds no error diagnostic.
/// </summary>
public sealed record PayloadValidationCheck
{
    public PayloadValidationCheck(
        string category,
        string name,
        PayloadValidationStatus status,
        double? expected = null,
        double? actual = null,
        double? tolerance = null,
        string? units = null,
        string? message = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(category);
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "Undefined check status.");
        }

        ValidateFinite(expected, nameof(expected));
        ValidateFinite(actual, nameof(actual));
        ValidateFinite(tolerance, nameof(tolerance));

        if (tolerance is { } allowed && allowed < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(tolerance), allowed, "Tolerance must be non-negative.");
        }

        Category = category;
        Name = name;
        Status = status;
        Expected = expected;
        Actual = actual;
        Tolerance = tolerance;
        Units = units;
        Message = message;
    }

    /// <summary>Check grouping, for example <c>control-point</c> or <c>payload</c>.</summary>
    public string Category { get; }

    /// <summary>Check name, unique within its category and run.</summary>
    public string Name { get; }

    public PayloadValidationStatus Status { get; }

    /// <summary>Expected value, when the check compares numbers.</summary>
    public double? Expected { get; }

    /// <summary>Observed value, when the check compares numbers.</summary>
    public double? Actual { get; }

    /// <summary>Allowed deviation, when the check compares numbers.</summary>
    public double? Tolerance { get; }

    /// <summary>Units of the expected, actual, and tolerance numbers, for example <c>meters</c>.</summary>
    public string? Units { get; }

    public string? Message { get; }

    private static void ValidateFinite(double? value, string paramName)
    {
        if (value is { } number && !double.IsFinite(number))
        {
            throw new ArgumentOutOfRangeException(paramName, number, "Check numbers must be finite.");
        }
    }
}
