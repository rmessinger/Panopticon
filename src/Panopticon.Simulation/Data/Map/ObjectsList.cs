using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Panopticon.Data.Map;

public sealed class ObjectsList : Asset
{
    public const string AssetName = "ObjectsList";

    public MapObject[] Objects { get; private set; }

    /// <summary>Adds a new object to the list. Used by the editor for placement operations.</summary>
    public void AddObject(MapObject mapObject)
    {
        var updated = new MapObject[Objects.Length + 1];
        Array.Copy(Objects, updated, Objects.Length);
        updated[Objects.Length] = mapObject;
        Objects = updated;
    }

    /// <summary>Removes an object by reference. Used by the editor for deletion operations.</summary>
    public void RemoveObject(MapObject mapObject)
    {
        Objects = Objects.Where(o => !ReferenceEquals(o, mapObject)).ToArray();
    }

    internal static ObjectsList Parse(BinaryReader reader, MapParseContext context)
    {
        return ParseAsset(reader, context, version =>
        {
            var objects = new List<MapObject>();

            ParseAssets(reader, context, assetName =>
            {
                if (assetName != MapObject.AssetName)
                {
                    throw new InvalidDataException();
                }

                objects.Add(MapObject.Parse(reader, context));
            });

            return new ObjectsList
            {
                Objects = objects.ToArray()
            };
        });
    }

    internal void WriteTo(BinaryWriter writer, AssetNameCollection assetNames)
    {
        WriteAssetTo(writer, () =>
        {
            foreach (var mapObject in Objects)
            {
                writer.Write(assetNames.GetOrCreateAssetIndex(MapObject.AssetName));
                mapObject.WriteTo(writer, assetNames);
            }
        });
    }
}
