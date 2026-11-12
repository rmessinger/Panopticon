using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Panopticon.Data.Map;

public sealed class ObjectsList : Asset
{
    public const string AssetName = "ObjectsList";

    public MapObject[] Objects { get; private set; } = [];

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

    /// <summary>
    /// Adds multiple objects to the list in a single batch with one result-array allocation.
    /// Used by the editor for bulk placement operations.
    /// </summary>
    public void AddObjects(IEnumerable<MapObject> objects)
    {
        ArgumentNullException.ThrowIfNull(objects);

        var batch = objects.ToArray();
        if (batch.Length == 0)
        {
            return;
        }

        var batchSet = new HashSet<MapObject>(batch.Length, ReferenceEqualityComparer.Instance);
        for (var i = 0; i < batch.Length; i++)
        {
            var item = batch[i];
            if (item is null)
            {
                throw new ArgumentException("Batch contains a null object reference.", nameof(objects));
            }

            if (!batchSet.Add(item))
            {
                throw new ArgumentException("Batch contains duplicate object references.", nameof(objects));
            }
        }

        for (var i = 0; i < Objects.Length; i++)
        {
            if (batchSet.Contains(Objects[i]))
            {
                throw new ArgumentException("Batch contains an object reference already present in the list.", nameof(objects));
            }
        }

        var updated = new MapObject[Objects.Length + batch.Length];
        Array.Copy(Objects, 0, updated, 0, Objects.Length);
        Array.Copy(batch, 0, updated, Objects.Length, batch.Length);
        Objects = updated;
    }

    /// <summary>
    /// Removes multiple objects by reference in a single batch with one result-array allocation.
    /// Used by the editor for bulk revert/deletion operations.
    /// </summary>
    public void RemoveObjects(IEnumerable<MapObject> objects)
    {
        if (objects is null || Objects.Length == 0)
        {
            return;
        }

        var toRemove = new HashSet<MapObject>(ReferenceEqualityComparer.Instance);
        foreach (var item in objects)
        {
            if (item is not null)
            {
                toRemove.Add(item);
            }
        }

        if (toRemove.Count == 0)
        {
            return;
        }

        var keepCount = 0;
        for (var i = 0; i < Objects.Length; i++)
        {
            if (!toRemove.Contains(Objects[i]))
            {
                keepCount++;
            }
        }

        if (keepCount == Objects.Length)
        {
            return;
        }

        var updated = new MapObject[keepCount];
        var destIndex = 0;
        for (var i = 0; i < Objects.Length; i++)
        {
            var item = Objects[i];
            if (!toRemove.Contains(item))
            {
                updated[destIndex++] = item;
            }
        }

        Objects = updated;
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
