namespace Panopticon.Content.Loaders;

/// <summary>
/// Optional loader capability: role-keyed variants of the same source asset
/// (e.g. a cutout texture whose mips preserve alpha coverage, WI-G5-02).
/// Loaders that do not implement it keep the single-method contract.
/// </summary>
internal interface IVariantAwareAssetLoader<TAsset> : IOnDemandAssetLoader<TAsset>
    where TAsset : BaseAsset
{
    TAsset Load(string key, string role, AssetLoadContext context);
}
