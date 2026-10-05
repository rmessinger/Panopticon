#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Panopticon.Logic.Object;

namespace Panopticon.Content.Importing.Units.Materialization;

/// <summary>
/// Card WI-H2a-12: registers a complete materialized closure in one new <see cref="AssetStore"/>
/// scope (decision D3). Every check runs before the scope is pushed; a failure while registering
/// pops the scope, so neither built-ins nor other sources see partial content. Non-Generals sources
/// publish into their own collection set (decision D2).
/// </summary>
public sealed class UnitContentPublisher
{
    private readonly AssetStore _store;

    public UnitContentPublisher(AssetStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <summary>Test seam: invoked after each registration, to inject mid-publication failures.</summary>
    internal Action<ContentId>? AfterRegister { get; set; }

    /// <param name="overrides">Ids allowed to shadow an existing same-source asset in a lower scope.</param>
    public PublicationResult Publish(
        MaterializedUnitContent content,
        IUnitVisualInstaller? visualInstaller = null,
        IReadOnlyCollection<ContentId>? overrides = null)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (!content.Succeeded)
        {
            throw new InvalidOperationException("Only content that materialized without errors can be published.");
        }

        var target = Target(content.Source);
        var diagnostics = new List<ImportDiagnostic>();

        if (content.VisualAssets.Count > 0)
        {
            if (visualInstaller is null)
            {
                diagnostics.Add(Error(DiagnosticCodes.PublicationFailed, "Visual assets were requested but no visual installer was supplied."));
            }
            else
            {
                diagnostics.AddRange(visualInstaller.Verify(content.VisualAssets));
            }
        }

        diagnostics.AddRange(CheckCollisions(content, target, overrides ?? []));

        if (diagnostics.Any(d => d.IsError))
        {
            return new PublicationResult(null, diagnostics);
        }

        _store.PushScope();
        var depth = _store.ScopeDepth;

        try
        {
            foreach (var (id, asset) in content.Assets)
            {
                target(id.Kind)!.Add(asset);
                AfterRegister?.Invoke(id);
            }

            if (content.VisualAssets.Count > 0)
            {
                visualInstaller!.Install(content.VisualAssets, _store);
            }
        }
        catch (Exception exception)
        {
            _store.PopScope();
            diagnostics.Add(Error(DiagnosticCodes.PublicationFailed, $"Registration failed and was rolled back: {exception.Message}"));
            return new PublicationResult(null, diagnostics);
        }

        content.Context.BindToStore(id => Lookup(id));

        return new PublicationResult(new PublishedUnitContent(content.Source, content.Assets.Keys.ToArray(), depth), diagnostics);
    }

    /// <summary>
    /// Publishes Generals content the INI parser already registered in the root scope: succeeds only
    /// when every validated asset is exactly the instance the store resolves for its id. Adopted
    /// content belongs to the root scope and cannot be unloaded.
    /// </summary>
    public PublicationResult Adopt(MaterializedUnitContent content)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (!content.Source.Equals(ContentSource.Generals))
        {
            throw new InvalidOperationException("Only Generals content registered by the INI loader can be adopted.");
        }

        if (!content.Succeeded)
        {
            throw new InvalidOperationException("Only content that materialized without errors can be adopted.");
        }

        var diagnostics = content.Assets
            .Where(pair => !ReferenceEquals(Lookup(pair.Key), pair.Value))
            .Select(pair => Error(DiagnosticCodes.PublicationFailed, $"'{pair.Key}' is not the instance registered in the store.", pair.Key))
            .ToList();

        if (diagnostics.Count > 0)
        {
            return new PublicationResult(null, diagnostics);
        }

        content.Context.BindToStore(Lookup);
        return new PublicationResult(new PublishedUnitContent(content.Source, content.Assets.Keys.ToArray(), scopeDepth: 0), diagnostics);
    }

    /// <summary>
    /// Removes a publication. It must be the most recent scope, and no live object may still use one
    /// of its definitions.
    /// </summary>
    public void Unload(PublishedUnitContent published, IEnumerable<ObjectDefinition>? liveDefinitions = null)
    {
        ArgumentNullException.ThrowIfNull(published);

        if (published.IsUnloaded)
        {
            throw new InvalidOperationException("This publication was already unloaded.");
        }

        if (published.ScopeDepth == 0)
        {
            throw new InvalidOperationException("Adopted root content cannot be unloaded.");
        }

        if (_store.ScopeDepth != published.ScopeDepth)
        {
            throw new InvalidOperationException("Publications unload in reverse order; a later scope is still loaded.");
        }

        var owned = published.Ids
            .Where(id => id.Kind == ContentKind.Unit)
            .Select(Lookup)
            .OfType<ObjectDefinition>()
            .ToHashSet();

        if (liveDefinitions?.Any(owned.Contains) == true)
        {
            throw new InvalidOperationException("Live objects still use definitions from this publication.");
        }

        _store.PopScope();
        published.IsUnloaded = true;
    }

    /// <summary>Resolves a published id through the store: Generals built-ins or the source's own collections.</summary>
    public BaseAsset? Lookup(ContentId id) => Target(id.Source)(id.Kind)?.FindByName(id.Name);

    private Func<ContentKind, IScopedAssetCollection?> Target(ContentSource source)
    {
        if (!source.Equals(ContentSource.Generals))
        {
            var content = _store.GetSourceContent(source);
            return content.For;
        }

        return kind => kind switch
        {
            ContentKind.Unit => _store.ObjectDefinitions,
            ContentKind.Weapon => _store.WeaponTemplates,
            ContentKind.Armor => _store.ArmorTemplates,
            ContentKind.Locomotor => _store.LocomotorTemplates,
            ContentKind.Upgrade => _store.Upgrades,
            _ => null,
        };
    }

    private IEnumerable<ImportDiagnostic> CheckCollisions(
        MaterializedUnitContent content,
        Func<ContentKind, IScopedAssetCollection?> target,
        IReadOnlyCollection<ContentId> overrides)
    {
        var seen = new Dictionary<(ContentKind, uint), ContentId>();

        foreach (var (id, asset) in content.Assets)
        {
            var collection = target(id.Kind);
            if (collection is null)
            {
                yield return Error(DiagnosticCodes.PublicationFailed, $"'{id}' has no runtime collection.", id);
                continue;
            }

            if (!seen.TryAdd((id.Kind, asset.InstanceId), id))
            {
                yield return Error(DiagnosticCodes.NameCollision, $"'{id}' and '{seen[(id.Kind, asset.InstanceId)]}' share a runtime name hash.", id);
            }

            if (collection.FindByName(asset.Name) is not null && !overrides.Contains(id))
            {
                yield return Error(DiagnosticCodes.NameCollision, $"'{id}' collides with existing {id.Kind} '{asset.Name}'.", id);
            }
        }

        foreach (var request in content.VisualAssets.DistinctBy(r => r.RuntimeName))
        {
            var hash = AssetHash.GetHash(request.RuntimeName);
            var existing = request.Kind == VisualAssetKind.Model
                ? (BaseAsset?)_store.Models.GetByInstanceId(hash)
                : _store.ModelAnimations.GetByInstanceId(hash);

            if (existing is not null)
            {
                yield return Error(DiagnosticCodes.NameCollision, $"Visual asset '{request.RuntimeName}' is already registered.", request.Subject);
            }
        }
    }

    private static ImportDiagnostic Error(string code, string message, ContentId? subject = null)
    {
        return new ImportDiagnostic(ImportDiagnosticSeverity.Error, ImportStage.Publish, code, message, subject);
    }
}
