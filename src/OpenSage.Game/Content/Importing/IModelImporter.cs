#nullable enable

namespace OpenSage.Content.Importing;

/// <summary>
/// Imports models and skeletons from one source format into CPU-only interchange values.
/// Implementations own their path resolution and parsing rules, and return null for an
/// invalid asset key or missing source file.
/// </summary>
internal interface IModelImporter
{
    string Id { get; }

    ImportedModel? ImportModel(string name, AssetImportContext context);

    ImportedSkeleton? ImportSkeleton(string name, AssetImportContext context);
}
