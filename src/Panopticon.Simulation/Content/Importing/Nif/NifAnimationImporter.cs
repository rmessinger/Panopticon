#nullable enable

using System;
using System.IO;
using System.Linq;
using Panopticon.IO;

namespace Panopticon.Content.Importing.Nif;

/// <summary>
/// NIF 10.1 KF animation importer through the C1 seam: parses a
/// <c>&lt;model-key&gt;#kf=&lt;kf-path&gt;</c> key, resolves the model's
/// skeleton and the archive-relative KF entry through the import file
/// system, and produces rest-relative, CPU-only interchange channels.
/// Returns null for a malformed key or a missing model/KF entry. Not
/// registered in shipped game definitions.
/// </summary>
internal sealed class NifAnimationImporter : IAnimationImporter
{
    public string Id => "NIF";

    public ImportedAnimation? ImportAnimation(string key, AssetImportContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!NifAnimationKey.TryParse(key, out var animationKey))
        {
            return null;
        }

        var modelFile = NifModelImporter.ReadNifFile(animationKey.ModelName, context);
        if (modelFile is null)
        {
            return null;
        }

        var kfEntry = context.FileSystem.GetFile(animationKey.KfPath);
        if (kfEntry is null)
        {
            return null;
        }

        // ZIP entry streams are not seekable; buffer the (small) clip.
        using var stream = kfEntry.Open();
        using var buffered = new MemoryStream();
        stream.CopyTo(buffered);
        buffered.Position = 0;
        var kfFile = FileFormats.Nif.NifFile.Read(buffered);

        if (kfFile.Header.Version != FileFormats.Nif.NifHeader.SupportedVersion &&
            modelFile.Blocks.Any(block => block is FileFormats.Nif.NifTriShape shape && shape.SkinInstanceReference >= 0))
        {
            // C4i proves legacy-header clips on rigid targets only. Current 10.1 clips on skinned
            // targets are C4d's proven path and import normally.
            throw new NotSupportedException(
                $"NIF animation '{key}': legacy clip targets model '{animationKey.ModelName}', which carries skinned shapes; " +
                "legacy skinned animation is outside the C4d seam and is not imported.");
        }

        if (kfFile.Blocks.Count == 0 ||
            kfFile.Blocks[0] is not FileFormats.Nif.NifControllerSequence sequence)
        {
            throw new InvalidDataException(
                $"NIF animation '{key}': block 0 is not a controller sequence.");
        }

        if (kfFile.Roots.Count > 1)
        {
            throw new NotSupportedException(
                $"NIF animation '{key}' holds {kfFile.Roots.Count} root blocks; multi-sequence KF files need a selection rule and are not supported.");
        }

        var graph = NifSceneConverter.ConvertGraph(
            modelFile,
            animationKey.ModelName,
            context,
            convertMeshes: false);

        return NifAnimationConverter.Convert(
            kfFile,
            sequence,
            graph.Skeleton,
            graph.PropertyTargets,
            graph.CollapsedScaleNodes,
            GetAnimationName(animationKey.KfPath),
            key);
    }

    private static string GetAnimationName(string kfPath)
    {
        var file = kfPath;
        var lastSeparator = file.LastIndexOf('/');
        if (lastSeparator >= 0)
        {
            file = file[(lastSeparator + 1)..];
        }

        var extension = file.LastIndexOf('.');
        return extension > 0 ? file[..extension] : file;
    }
}
