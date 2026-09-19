#nullable enable

using System;
using OpenSage.Content.Importing.Gltf;
using OpenSage.IO;

namespace OpenSage.Content.Importing.Gltf;

/// <summary>
/// glTF animation importer: owns the `<model>#animation=<index>` key form, loads and strictly
/// parses the source document independently, and converts one indexed animation. Returns null
/// for an invalid key or missing root.
/// </summary>
internal sealed class GltfAnimationImporter : IAnimationImporter
{
    public string Id => "GLTF";

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

        var options = GltfImportOptions.Default;
        var resolver = new GltfResourceResolver(animationKey.ModelPath, context.FileSystem, options);
        var document = GltfDocumentLoader.TryLoadDocument(animationKey.ModelPath, context, options, resolver);
        if (document is null)
        {
            return null;
        }

        var plan = GltfSceneConverter.CreatePlan(
            animationKey.ModelPath,
            document.Root,
            options,
            document.HasDeclaredDefaultScene);

        return GltfAnimationConverter.Convert(
            animationKey.AnimationIndex,
            animationKey.ModelPath,
            document,
            plan,
            options);
    }
}
