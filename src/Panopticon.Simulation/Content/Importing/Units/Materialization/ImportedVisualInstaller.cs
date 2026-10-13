#nullable enable

using System;
using System.Collections.Generic;
using Panopticon;
using Panopticon.Content.Loaders;

namespace Panopticon.Content.Importing.Units.Materialization;

/// <summary>
/// Installs visual assets through existing Track C importers against the source file system, which
/// is separate from runtime content names. Animations are CPU-only and always install; models are
/// materialized only when a graphics load context is supplied, keeping headless logic GPU-free.
/// </summary>
internal sealed class ImportedVisualInstaller : IUnitVisualInstaller
{
    private readonly IModelImporter _modelImporter;
    private readonly IAnimationImporter _animationImporter;
    private readonly AssetImportContext _sourceContext;
    private readonly AssetLoadContext? _graphicsContext;
    private readonly float _modelScale;
    private readonly Dictionary<string, ImportedModel> _models = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ImportedAnimation> _animations = new(StringComparer.Ordinal);

    /// <param name="modelScale">
    /// Card WI-M3-01: game units per source model unit, baked once into each installed model's
    /// root bone binds. 1 leaves source units as-is.
    /// </param>
    public ImportedVisualInstaller(
        IModelImporter modelImporter,
        IAnimationImporter animationImporter,
        AssetImportContext sourceContext,
        AssetLoadContext? graphicsContext = null,
        float modelScale = 1f)
    {
        ArgumentNullException.ThrowIfNull(modelImporter);
        ArgumentNullException.ThrowIfNull(animationImporter);
        ArgumentNullException.ThrowIfNull(sourceContext);
        Ensure.FinitePositive(modelScale, "Model scale must be finite and positive.");

        _modelImporter = modelImporter;
        _animationImporter = animationImporter;
        _sourceContext = sourceContext;
        _graphicsContext = graphicsContext;
        _modelScale = modelScale;
    }

    public IReadOnlyList<ImportDiagnostic> Verify(IReadOnlyList<VisualAssetRequest> requests)
    {
        var diagnostics = new List<ImportDiagnostic>();

        foreach (var request in requests)
        {
            try
            {
                var found = request.Kind == VisualAssetKind.Model
                    ? ImportModel(request.SourceKey) is not null
                    : ImportAnimation(request.SourceKey) is not null;

                if (!found)
                {
                    diagnostics.Add(Missing(request, "was not found in the source file system"));
                }
            }
            catch (Exception exception) when (exception is System.IO.InvalidDataException or NotSupportedException or ArgumentException)
            {
                diagnostics.Add(Missing(request, $"failed to import: {exception.Message}"));
            }
        }

        return diagnostics;
    }

    public void Install(IReadOnlyList<VisualAssetRequest> requests, AssetStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        foreach (var request in requests)
        {
            if (request.Kind == VisualAssetKind.Animation)
            {
                var animation = ImportedAnimationMaterializer.Materialize(ImportAnimation(request.SourceKey)!);
                animation.AssignImportedName("ModelAnimation", request.RuntimeName);
                store.ModelAnimations.Add(animation);
            }
            else if (_graphicsContext is not null)
            {
                var model = ImportedModelMaterializer.MaterializeModel(ImportModel(request.SourceKey)!, _graphicsContext, _modelScale);
                model.AssignImportedName("W3DContainer", request.RuntimeName);
                store.Models.Add(model);
            }
        }
    }

    private ImportedModel? ImportModel(string key)
    {
        if (!_models.TryGetValue(key, out var model))
        {
            model = _modelImporter.ImportModel(key, _sourceContext);
            if (model is not null)
            {
                _models[key] = model;
            }
        }

        return model;
    }

    private ImportedAnimation? ImportAnimation(string key)
    {
        if (!_animations.TryGetValue(key, out var animation))
        {
            animation = _animationImporter.ImportAnimation(key, _sourceContext);
            if (animation is not null)
            {
                _animations[key] = animation;
            }
        }

        return animation;
    }

    private static ImportDiagnostic Missing(VisualAssetRequest request, string reason)
    {
        return new ImportDiagnostic(
            ImportDiagnosticSeverity.Error,
            ImportStage.Materialize,
            DiagnosticCodes.MissingVisualState,
            $"{request.Subject}: {request.Kind} '{request.SourceKey}' {reason}.",
            request.Subject);
    }
}
