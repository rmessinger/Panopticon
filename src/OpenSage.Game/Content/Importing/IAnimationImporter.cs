#nullable enable

namespace OpenSage.Content.Importing;

/// <summary>
/// Imports animations from one source format into CPU-only interchange values. Implementations
/// own their key parsing, path resolution, and decoding, and return null for an invalid asset
/// key or missing source file.
/// </summary>
internal interface IAnimationImporter
{
    string Id { get; }

    ImportedAnimation? ImportAnimation(string name, AssetImportContext context);
}
