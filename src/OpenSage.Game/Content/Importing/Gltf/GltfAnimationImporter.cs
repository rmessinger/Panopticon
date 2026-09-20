#nullable enable

using System;
using System.IO;
using OpenSage.IO;
using SharpGLTF.Schema2;

namespace OpenSage.Content.Importing.Gltf;

/// <summary>
/// glTF animation importer: owns the `<model>#animation=<index>` key form, loads and strictly
/// parses the source document independently, and converts one indexed animation. Returns null
/// for an invalid key or missing root.
/// </summary>
internal sealed class GltfAnimationImporter : IAnimationImporter
{
    private readonly GltfImportOptions _options;
    private readonly GltfImportDiagnostics? _diagnostics;

    public string Id => "GLTF";

    internal GltfAnimationImporter(GltfImportOptions options, GltfImportDiagnostics? diagnostics = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _diagnostics = diagnostics;
    }

    public ImportedAnimation? ImportAnimation(string key, AssetImportContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (!GltfAssetKey.TryParseAnimation(key, out var animationKey))
        {
            return null;
        }

        var resolver = new GltfResourceResolver(animationKey.ModelPath, context.FileSystem, _options);
        var document = GltfDocumentLoader.TryLoadDocument(animationKey.ModelPath, context, _options, resolver);
        if (document is null)
        {
            return null;
        }

        GltfModelImporter.ValidateExtensionPolicy(document.Root, _diagnostics);
        EnforceAnimationLimits(animationKey.ModelPath, document.Root, animationKey.AnimationIndex);

        var plan = GltfSceneConverter.CreatePlan(
            animationKey.ModelPath,
            document.Root,
            _options,
            document.HasDeclaredDefaultScene);

        return GltfAnimationConverter.Convert(
            animationKey.AnimationIndex,
            animationKey.ModelPath,
            document,
            plan,
            _options);
    }

    private void EnforceAnimationLimits(string modelPath, ModelRoot root, int animationIndex)
    {
        var animations = root.LogicalAnimations;
        if (animationIndex < 0 || animationIndex >= animations.Count)
        {
            throw new InvalidDataException(
                $"Animation index {animationIndex} is outside document '{modelPath}', which has {animations.Count} animations.");
        }

        var animation = animations[animationIndex];
        if (animation.Channels.Count > _options.MaxChannelCount)
        {
            throw new InvalidDataException(
                $"Animation '{animation.Name}' in '{modelPath}' has {animation.Channels.Count} channels; " +
                $"the limit is {_options.MaxChannelCount}.");
        }

        // Keyframe limits are enforced in GltfAnimationConverter while key lists are
        // materialized, before any per-key allocation downstream.
    }
}
