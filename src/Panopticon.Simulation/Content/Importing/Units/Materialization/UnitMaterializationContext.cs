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
    private readonly Dictionary<ContentId, string> _impactEffectDamageTypes;
    private readonly Dictionary<ContentId, DamageType> _validatedImpactEffectDamageTypes = new();

    public UnitConversionPolicy Policy { get; }

    /// <summary>
    /// Card WI-M3-01: declared game units per source model unit (EE2 decimeters), applied to the
    /// imported draw's world matrix so model extents, bones, and bounds follow. Null means the
    /// source's model units already are game units (no scaling).
    /// </summary>
    public double? GameUnitsPerSourceModelUnit { get; }

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
        Func<string, DamageFX?>? damageEffectLookup = null,
        IReadOnlyDictionary<ContentId, string>? impactEffectDamageTypes = null,
        double? gameUnitsPerSourceModelUnit = null)
    {
        ArgumentNullException.ThrowIfNull(policy);

        if (gameUnitsPerSourceModelUnit is { } modelScale && (!double.IsFinite(modelScale) || modelScale <= 0))
        {
            throw new ArgumentOutOfRangeException(nameof(gameUnitsPerSourceModelUnit), modelScale, "Game units per source model unit must be finite and positive.");
        }

        Policy = policy;
        GameUnitsPerSourceModelUnit = gameUnitsPerSourceModelUnit;
        _published = published ?? (_ => null);
        LegacyNatives = legacyNatives;
        _visualStore = visualStore;
        _damageEffects = damageEffects?.ToDictionary() ?? new();
        _damageEffectLookup = damageEffectLookup;
        _impactEffectDamageTypes = impactEffectDamageTypes?.ToDictionary() ?? new();
    }

    internal bool ValidateDamageEffects(UnitImportResolution resolution)
    {
        foreach (var (subject, name) in _damageEffects)
        {
            var unit = resolution.Closure.OfType<UnitRecord>().FirstOrDefault(record => record.Id.Equals(subject));
            if (unit is null || unit.Legacy is not null ||
                !unit.KindOf.Resolved.Contains("VEHICLE", StringComparer.OrdinalIgnoreCase))
            {
                Error(subject, $"DamageFX '{name}' requires a typed vehicle in this import.");
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

        ValidateImpactEffectDamageTypes(resolution);
        return !Diagnostics.Any(diagnostic => diagnostic.IsError);
    }

    private void ValidateImpactEffectDamageTypes(UnitImportResolution resolution)
    {
        foreach (var (subject, name) in _impactEffectDamageTypes)
        {
            var unit = resolution.Closure.OfType<UnitRecord>().FirstOrDefault(record => record.Id.Equals(subject));
            IReadOnlyCollection<ContentId> weapons = unit is null || unit.Legacy is not null ? [] : UnitCombatMaterializer.WeaponIds(unit, resolution);
            if (weapons.Count == 0)
            {
                Error(subject, $"Impact effect damage type '{name}' requires a typed unit with a weapon in this import.");
                continue;
            }

            // Unresistable is the engine's "no override" sentinel, so it cannot be requested.
            if (!EngineEnumNames.TryParse<DamageType>(name, out var damageType) || damageType == DamageType.Unresistable)
            {
                Error(subject, $"Impact effect damage type '{name}' is not a selectable damage type.");
                continue;
            }

            var shared = resolution.Closure.OfType<UnitRecord>()
                .Where(other => !other.Id.Equals(subject) && other.Legacy is null)
                .SelectMany(other => UnitCombatMaterializer.WeaponIds(other, resolution))
                .Intersect(weapons)
                .ToArray();
            if (shared.Length > 0)
            {
                Error(subject, $"Impact effect damage type '{name}' cannot apply to weapon(s) shared with other units: {string.Join(", ", shared.Select(id => id.Name))}.");
                continue;
            }

            _validatedImpactEffectDamageTypes[subject] = damageType;
        }
    }

    internal DamageType? ImpactEffectDamageType(ContentId subject)
    {
        return _validatedImpactEffectDamageTypes.TryGetValue(subject, out var damageType) ? damageType : null;
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
