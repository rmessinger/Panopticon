#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Panopticon.Logic.Object;

namespace Panopticon.Content.Importing.Units.Materialization;

/// <summary>
/// Shared state for one materialization pass: staged runtime assets, the lookup for already
/// published content, conversions applied, and diagnostics. References bind lazily by content id,
/// so staged assets can reference each other before publication.
/// </summary>
public sealed class UnitMaterializationContext
{
    private readonly Dictionary<ContentId, BaseAsset> _staged = new();
    private Func<ContentId, BaseAsset?> _published;
    private bool _boundToStore;
    private readonly AssetStore? _visualStore;
    private readonly Dictionary<ContentId, string> _damageEffects;
    private readonly Func<string, DamageFX?>? _damageEffectLookup;
    private readonly Dictionary<ContentId, DamageFX> _validatedDamageEffects = new();

    public UnitConversionPolicy Policy { get; }

    public ILegacyNativeTable? LegacyNatives { get; }

    public List<AppliedConversion> Conversions { get; } = [];

    public List<ImportDiagnostic> Diagnostics { get; } = [];

    /// <summary>Track C models and animations the staged draw modules need installed before use.</summary>
    public List<VisualAssetRequest> VisualAssets { get; } = [];

    public IReadOnlyDictionary<ContentId, BaseAsset> Staged => _staged;

    /// <param name="published">Lookup for content outside this import; returns null when absent.</param>
    /// <param name="visualStore">Store whose model and animation collections receive installed visual assets.</param>
    public UnitMaterializationContext(
        UnitConversionPolicy policy,
        Func<ContentId, BaseAsset?>? published = null,
        ILegacyNativeTable? legacyNatives = null,
        AssetStore? visualStore = null,
        IReadOnlyDictionary<ContentId, string>? damageEffects = null,
        Func<string, DamageFX?>? damageEffectLookup = null)
    {
        ArgumentNullException.ThrowIfNull(policy);

        Policy = policy;
        _published = published ?? (_ => null);
        LegacyNatives = legacyNatives;
        _visualStore = visualStore;
        _damageEffects = damageEffects?.ToDictionary() ?? new();
        _damageEffectLookup = damageEffectLookup;
    }

    internal bool ValidateDamageEffects(UnitImportResolution resolution)
    {
        foreach (var (subject, name) in _damageEffects)
        {
            var unit = resolution.Closure.OfType<UnitRecord>().FirstOrDefault(record => record.Id.Equals(subject));
            if (unit is null || unit.Legacy is not null || !unit.Armor.IsPresent ||
                !unit.KindOf.Resolved.Contains("VEHICLE", StringComparer.OrdinalIgnoreCase))
            {
                Error(subject, $"DamageFX '{name}' requires a typed vehicle with armor in this import.");
                continue;
            }

            var effect = string.IsNullOrWhiteSpace(name) ? null : _damageEffectLookup?.Invoke(name);
            if (effect is null)
            {
                Error(subject, $"Requested DamageFX '{name}' was not found.");
                continue;
            }

            foreach (var group in effect.Groups)
            {
                foreach (var reference in new[] { group.MajorFX, group.MinorFX })
                {
                    if (reference is not null && reference.Value is null)
                    {
                        Error(subject, $"DamageFX '{name}' references missing FXList '{reference.Name}'.");
                    }
                }
            }

            _validatedDamageEffects[subject] = effect;
        }

        return !Diagnostics.Any(diagnostic => diagnostic.IsError);
    }

    internal LazyAssetReference<DamageFX>? DamageEffectReference(ContentId subject)
    {
        return _validatedDamageEffects.TryGetValue(subject, out var effect)
            ? new LazyAssetReference<DamageFX>(_damageEffects[subject], () => effect)
            : null;
    }

    internal void Stage(ContentId id, BaseAsset asset) => _staged[id] = asset;

    internal T? Find<T>(ContentId id)
        where T : BaseAsset
    {
        if (!_boundToStore && _staged.TryGetValue(id, out var staged))
        {
            return staged as T;
        }

        return _published(id) as T;
    }

    /// <summary>
    /// After publication, references resolve through the store, so a reference held past unload
    /// fails lookup instead of returning a disposed asset.
    /// </summary>
    internal void BindToStore(Func<ContentId, BaseAsset?> lookup)
    {
        _published = lookup;
        _boundToStore = true;
    }

    internal LazyAssetReference<T> Reference<T>(ContentId id)
        where T : BaseAsset
    {
        return new LazyAssetReference<T>(id.Name, () => Find<T>(id)!);
    }

    /// <summary>Models and animations live in the store's shared collections, so they bind by runtime name rather than content id.</summary>
    internal LazyAssetReference<Graphics.Model> ModelReference(string runtimeName)
    {
        return new LazyAssetReference<Graphics.Model>(runtimeName, () => _visualStore?.Models.GetByInstanceId(AssetHash.GetHash(runtimeName))!);
    }

    internal LazyAssetReference<Graphics.Animation.ModelAnimation> AnimationReference(string runtimeName)
    {
        return new LazyAssetReference<Graphics.Animation.ModelAnimation>(runtimeName, () => _visualStore?.ModelAnimations.GetByInstanceId(AssetHash.GetHash(runtimeName))!);
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
