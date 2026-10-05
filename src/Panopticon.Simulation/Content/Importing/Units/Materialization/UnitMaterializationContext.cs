#nullable enable

using System;
using System.Collections.Generic;

namespace Panopticon.Content.Importing.Units.Materialization;

/// <summary>
/// Shared state for one materialization pass: staged runtime assets, the lookup for already
/// published content, conversions applied, and diagnostics. References bind lazily by content id,
/// so staged assets can reference each other before publication.
/// </summary>
public sealed class UnitMaterializationContext
{
    private readonly Dictionary<ContentId, BaseAsset> _staged = new();
    private readonly Func<ContentId, BaseAsset?> _published;

    public UnitConversionPolicy Policy { get; }

    public ILegacyNativeTable? LegacyNatives { get; }

    public List<AppliedConversion> Conversions { get; } = [];

    public List<ImportDiagnostic> Diagnostics { get; } = [];

    public IReadOnlyDictionary<ContentId, BaseAsset> Staged => _staged;

    /// <param name="published">Lookup for content outside this import; returns null when absent.</param>
    public UnitMaterializationContext(UnitConversionPolicy policy, Func<ContentId, BaseAsset?>? published = null, ILegacyNativeTable? legacyNatives = null)
    {
        ArgumentNullException.ThrowIfNull(policy);

        Policy = policy;
        _published = published ?? (_ => null);
        LegacyNatives = legacyNatives;
    }

    internal void Stage(ContentId id, BaseAsset asset) => _staged[id] = asset;

    internal T? Find<T>(ContentId id)
        where T : BaseAsset
    {
        return (_staged.TryGetValue(id, out var staged) ? staged : _published(id)) as T;
    }

    internal LazyAssetReference<T> Reference<T>(ContentId id)
        where T : BaseAsset
    {
        return new LazyAssetReference<T>(id.Name, () => Find<T>(id)!);
    }

    internal T? GetLegacy<T>(ContentId subject, LegacyBinding binding)
        where T : class
    {
        if (LegacyNatives is null || !LegacyNatives.Owner.Equals(binding.Owner))
        {
            Error(subject, $"No legacy table from {binding.Owner} is available for handle {binding.Handle}.");
            return null;
        }

        if (LegacyNatives.Get(binding.Handle) is not T value)
        {
            Error(subject, $"Legacy handle {binding.Handle} does not hold a {typeof(T).Name}.");
            return null;
        }

        return value;
    }

    internal double Convert(ContentId subject, string field, Quantity source, double runtime, string runtimeUnit)
    {
        Conversions.Add(new AppliedConversion(subject, field, source, runtime, runtimeUnit));
        return runtime;
    }

    internal void Error(ContentId subject, string message)
    {
        Diagnostics.Add(new ImportDiagnostic(ImportDiagnosticSeverity.Error, ImportStage.Materialize, DiagnosticCodes.MaterializationFailed, $"{subject}: {message}", subject));
    }
}
