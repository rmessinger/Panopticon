using Panopticon.Content;

namespace Panopticon;

public abstract class BaseAsset : DisposableBase
{
    public string FullName { get; private set; }
    public string Name { get; internal set; }
    public uint InstanceId { get; private set; }

    protected void SetNameAndInstanceId(string typeName, string name)
    {
        Name = name;
        FullName = $"{typeName}:{name}";
        InstanceId = AssetHash.GetHash(name);
    }

    /// <summary>Names an asset built by unit-content materialization instead of a parser.</summary>
    internal void AssignImportedName(string typeName, string name) => SetNameAndInstanceId(typeName, name);

    public int InternalId { get; internal set; }

    internal BaseAsset DeepClone()
    {
        // TODO: Deep-clone asset so that it can be overridden.
        return (BaseAsset)MemberwiseClone();
    }
}
