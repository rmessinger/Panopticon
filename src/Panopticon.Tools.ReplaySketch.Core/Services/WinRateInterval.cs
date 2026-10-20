using System;

namespace Panopticon.Tools.ReplaySketch.Services;

/// <summary>
/// Wilson score interval for a binomial proportion (WI-M5-06): the per-side win-rate interval
/// for batch battles. Unlike the Wald interval it stays inside [0, 1] at 0 and n successes.
/// </summary>
public static class WinRateInterval
{
    // Two-sided normal critical values for the supported confidence levels; the batch reports
    // author one level per aggregate (no fitting).
    private static double ZFor(double confidenceLevel) => confidenceLevel switch
    {
        0.90 => 1.644854,
        0.95 => 1.959964,
        0.99 => 2.575829,
        _ => throw new ArgumentOutOfRangeException(
            nameof(confidenceLevel), confidenceLevel, "Supported confidence levels: 0.90, 0.95, 0.99."),
    };

    /// <summary>The Wilson score interval for <paramref name="successes"/> in
    /// <paramref name="trials"/> trials. Zero trials return the empty interval [0, 0].</summary>
    public static (double Low, double High) Wilson(int successes, int trials, double confidenceLevel = 0.95)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(successes);
        ArgumentOutOfRangeException.ThrowIfLessThan(trials, 0);
        if (successes > trials)
        {
            throw new ArgumentOutOfRangeException(nameof(successes), successes, "Successes cannot exceed trials.");
        }

        if (trials == 0)
        {
            return (0, 0);
        }

        var z = ZFor(confidenceLevel);
        var z2 = z * z;
        var pHat = (double)successes / trials;
        var denominator = 1 + z2 / trials;
        var center = (pHat + z2 / (2 * trials)) / denominator;
        var half = z / denominator * Math.Sqrt(pHat * (1 - pHat) / trials + z2 / (4 * (double)trials * trials));

        // The interval touches 0 at zero successes and 1 at all successes; sqrt rounding can
        // leave an epsilon residue otherwise.
        var low = pHat == 0 ? 0 : Math.Max(0, center - half);
        var high = pHat == 1 ? 1 : Math.Min(1, center + half);
        return (low, high);
    }
}
