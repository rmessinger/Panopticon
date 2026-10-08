using System.Collections;
using System.Collections.Generic;
using Panopticon.Content.Loaders;

namespace Panopticon.Content;

public sealed class ScopedAssetCollection<TAsset> : IScopedAssetCollection, IEnumerable<TAsset>
    where TAsset : BaseAsset
{
    private sealed class AssetScope
    {
        public readonly Dictionary<uint, TAsset> Assets = new Dictionary<uint, TAsset>();
        public readonly int StartId;

        public AssetScope(int startId)
        {
            StartId = startId;
        }
    }

    private readonly Stack<AssetScope> _assetScopes = new Stack<AssetScope>();
    private readonly Dictionary<int, TAsset> _byInternalId = new Dictionary<int, TAsset>();
    private readonly List<TAsset> _list = new List<TAsset>();

    private readonly AssetStore _assetStore;
    private readonly IOnDemandAssetLoader<TAsset> _loader;

    private int _nextId = 1;

    public int Count => _byInternalId.Count;

    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    internal ScopedAssetCollection(
        AssetStore assetStore,
        IOnDemandAssetLoader<TAsset> loader = null)
    {
        _assetStore = assetStore;
        _loader = loader;
    }

    void IScopedAssetStorage.PushScope()
    {
        _assetScopes.Push(new AssetScope(_nextId));
    }

    void IScopedAssetStorage.PopScope()
    {
        var assetScope = _assetScopes.Pop();
        foreach (var asset in assetScope.Assets.Values)
        {
            _list.Remove(asset); // TODO: This is slow.
            asset.Dispose();
        }

        for (var i = assetScope.StartId; i < _nextId; i++)
        {
            _byInternalId.Remove(i);
        }
    }

    public TAsset GetByName(string name)
    {
        return GetByName(name, string.Empty);
    }

    public TAsset GetByName(string name, string role)
    {
        // Role variants share the source name but must not collide with the base
        // asset's identity, so the role prefixes the hash case-sensitively.
        var instanceId = role.Length == 0
            ? AssetHash.GetHash(name)
            : AssetHash.GetHashCaseSensitive(role + ':' + name.ToLowerInvariant());

        // Find existing cached item.
        foreach (var assetScope in _assetScopes)
        {
            if (assetScope.Assets.TryGetValue(instanceId, out var result))
            {
                return result;
            }
        }

        // If we can, create new item and cache it.
        if (_loader != null)
        {
            var newValue = _loader is IVariantAwareAssetLoader<TAsset> variantLoader
                ? variantLoader.Load(name, role, _assetStore.LoadContext)
                : _loader.Load(name, _assetStore.LoadContext);

            if (newValue == null)
            {
                return null;
            }

            _assetScopes.Peek().Assets.Add(instanceId, newValue);

            newValue.InternalId = _nextId;
            _byInternalId.Add(_nextId, newValue);

            _nextId++;

            _list.Add(newValue);

            return newValue;
        }

        return null;
    }

    public LazyAssetReference<TAsset> GetLazyAssetReferenceByName(string name)
    {
        return new LazyAssetReference<TAsset>(name, () => GetByName(name));
    }

    public TAsset GetByInternalId(int internalId)
    {
        return _byInternalId[internalId];
    }

    public TAsset GetByInstanceId(uint instanceId)
    {
        foreach (var assetScope in _assetScopes)
        {
            if (assetScope.Assets.TryGetValue(instanceId, out var result))
            {
                return result;
            }
        }

        return null;
    }

    public TAsset GetByIndex(int index)
    {
        return _list[index];
    }

    internal void Add(TAsset asset)
    {
        // Existing entries take precedence
        var assetScope = _assetScopes.Peek();
        var key = asset.InstanceId;
        if (!assetScope.Assets.ContainsKey(key))
        {
            assetScope.Assets.Add(key, asset);

            asset.InternalId = _nextId;
            _byInternalId.Add(_nextId, asset);

            _list.Add(asset);

            _nextId++;
        }
    }

    void IScopedAssetCollection.Add(object asset)
    {
        Add((TAsset)asset);
    }

    BaseAsset IScopedAssetCollection.FindByName(string name) => GetByInstanceId(AssetHash.GetHash(name));

    public IEnumerable<BaseAsset> GetAssets()
    {
        return _byInternalId.Values;
    }

    IEnumerator<TAsset> IEnumerable<TAsset>.GetEnumerator()
    {
        foreach (var assetScope in _assetScopes)
        {
            foreach (var asset in assetScope.Assets.Values)
            {
                yield return asset;
            }
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable<TAsset>)this).GetEnumerator();
}
