using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Panopticon;

/// <summary>
/// Single-line argument validation for guards that carry a custom message. BCL
/// <c>ThrowIf</c> helpers cover the message-less cases; see
/// docs/roadmap/plans/argument-validation.md.
/// </summary>
public static class Ensure
{
    public static void NotNullOrEmpty(
        [NotNull] string? value,
        string? message,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        ArgumentNullException.ThrowIfNull(value, paramName);

        if (value.Length == 0)
            throw new ArgumentException(message ?? $"{paramName} must not be null or empty.", paramName);
    }

    public static void NotNullOrWhiteSpace(
        [NotNull] string? value,
        string? message,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        ArgumentNullException.ThrowIfNull(value, paramName);

        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException(message ?? $"{paramName} must not be null or whitespace.", paramName);
    }

    public static void NotEmpty<T>(
        [NotNull] IReadOnlyCollection<T>? items,
        string? message,
        [CallerArgumentExpression(nameof(items))] string? paramName = null)
    {
        ArgumentNullException.ThrowIfNull(items, paramName);

        if (items.Count == 0)
            throw new ArgumentException(message ?? $"{paramName} must contain at least one element.", paramName);
    }

    public static void NoNullElements<T>(
        [NotNull] IReadOnlyList<T?>? items,
        string elementName,
        [CallerArgumentExpression(nameof(items))] string? paramName = null)
    {
        ArgumentNullException.ThrowIfNull(items, paramName);

        for (var i = 0; i < items.Count; i++)
        {
            if (items[i] is null)
                throw new ArgumentException($"{elementName} at index {i} must not be null.", paramName);
        }
    }

    public static void AtLeast<T>(
        T value,
        T min,
        string? message,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
        where T : IComparable<T>
    {
        if (value.CompareTo(min) < 0)
            throw new ArgumentOutOfRangeException(paramName, value, message ?? $"{paramName} must be at least {min}.");
    }

    public static void GreaterThan<T>(
        T value,
        T bound,
        string? message,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
        where T : IComparable<T>
    {
        if (value.CompareTo(bound) <= 0)
            throw new ArgumentOutOfRangeException(paramName, value, message ?? $"{paramName} must be greater than {bound}.");
    }

    public static void InRange<T>(
        T value,
        T min,
        T max,
        string? message,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
        where T : IComparable<T>
    {
        if (value.CompareTo(min) < 0 || value.CompareTo(max) > 0)
            throw new ArgumentOutOfRangeException(paramName, value, message ?? $"{paramName} must be between {min} and {max}.");
    }

    public static void Finite(
        float value,
        string? message,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (!float.IsFinite(value))
            throw new ArgumentOutOfRangeException(paramName, value, message ?? $"{paramName} must be a finite number.");
    }

    public static void Finite(Vector3 value, string? message, [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z))
            throw new ArgumentException(message ?? $"{paramName} must contain only finite components.", paramName);
    }

    public static void Finite(Matrix4x4 value, string? message, [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (!float.IsFinite(value.M11) || !float.IsFinite(value.M12) || !float.IsFinite(value.M13) || !float.IsFinite(value.M14)
            || !float.IsFinite(value.M21) || !float.IsFinite(value.M22) || !float.IsFinite(value.M23) || !float.IsFinite(value.M24)
            || !float.IsFinite(value.M31) || !float.IsFinite(value.M32) || !float.IsFinite(value.M33) || !float.IsFinite(value.M34)
            || !float.IsFinite(value.M41) || !float.IsFinite(value.M42) || !float.IsFinite(value.M43) || !float.IsFinite(value.M44))
        {
            throw new ArgumentException(message ?? $"{paramName} must contain only finite components.", paramName);
        }
    }

    public static void FiniteNonNegative(
        float value,
        string? message,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (!float.IsFinite(value) || value < 0)
            throw new ArgumentOutOfRangeException(paramName, value, message ?? $"{paramName} must be a finite, non-negative number.");
    }

    public static void FinitePositive(
        float value,
        string? message,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (!float.IsFinite(value) || value <= 0)
            throw new ArgumentOutOfRangeException(paramName, value, message ?? $"{paramName} must be a finite, positive number.");
    }
}
