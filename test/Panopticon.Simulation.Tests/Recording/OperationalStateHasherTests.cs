using System;
using System.Collections.Generic;
using System.Linq;
using Panopticon;
using Panopticon.Recording;
using Xunit;

namespace Panopticon.Tests.Recording;

public class OperationalStateHasherTests : StatePersisterTest
{
    [Fact]
    public void EqualInputs_ProduceEqualHashesAtEveryLevel()
    {
        var first = HashSample();
        var second = HashSample();

        Assert.Equal(first.WorldHash, second.WorldHash);
        Assert.Equal(first.GlobalsHash, second.GlobalsHash);
        Assert.Equal(first.Buckets, second.Buckets);
    }

    [Fact]
    public void ObjectOrderWithinBucket_DoesNotChangeHashes()
    {
        var objects = new List<(string BucketKey, ulong ObjectHash)>
        {
            ("cell:0,0", OperationalStateHasher.HashPersisted(Object(1), Generals)),
            ("cell:0,0", OperationalStateHasher.HashPersisted(Object(2), Generals)),
            ("cell:1,0", OperationalStateHasher.HashPersisted(Object(3), Generals)),
        };

        var forward = OperationalStateHasher.Build(objects, Globals());
        var reversed = OperationalStateHasher.Build(objects.AsEnumerable().Reverse(), Globals());

        Assert.Equal(forward.WorldHash, reversed.WorldHash);
        Assert.Equal(forward.Buckets, reversed.Buckets);
        Assert.Equal(forward.GlobalsHash, reversed.GlobalsHash);
    }

    [Fact]
    public void BucketSupplyOrder_DoesNotChangeWorldHash()
    {
        var objectsA = new List<(string BucketKey, ulong ObjectHash)>
        {
            ("cell:0,0", OperationalStateHasher.HashPersisted(Object(1), Generals)),
            ("region:west", OperationalStateHasher.HashPersisted(Object(2), Generals)),
            ("cell:1,0", OperationalStateHasher.HashPersisted(Object(3), Generals)),
        };

        var forward = OperationalStateHasher.Build(objectsA, Globals());
        var grouped = OperationalStateHasher.Build(
            objectsA.AsEnumerable().OrderBy(o => o.BucketKey, StringComparer.Ordinal),
            Globals());

        Assert.Equal(forward.WorldHash, grouped.WorldHash);
        Assert.Equal(forward.Buckets, grouped.Buckets);
    }

    [Fact]
    public void ChangedObject_ChangesOnlyItsBucketAndWorldHash()
    {
        var original = OperationalStateHasher.Build(SampleObjects(), Globals());
        var objects = SampleObjects().ToList();
        objects[0] = (objects[0].BucketKey, OperationalStateHasher.HashPersisted(Object(1, health: 90), Generals));
        var changed = OperationalStateHasher.Build(objects, Globals());

        Assert.Equal(original.Buckets["cell:1,0"], changed.Buckets["cell:1,0"]);
        Assert.NotEqual(original.Buckets["cell:0,0"], changed.Buckets["cell:0,0"]);
        Assert.Equal(original.GlobalsHash, changed.GlobalsHash);
        Assert.NotEqual(original.WorldHash, changed.WorldHash);
    }

    [Fact]
    public void ChangedGlobalsSection_ChangesGlobalsAndWorldHashOnly()
    {
        var original = OperationalStateHasher.Build(SampleObjects(), Globals());
        var changed = OperationalStateHasher.Build(SampleObjects(), Globals(randomWord: 0xDEADBEEF));

        Assert.Equal(original.Buckets, changed.Buckets);
        Assert.NotEqual(original.GlobalsHash, changed.GlobalsHash);
        Assert.NotEqual(original.WorldHash, changed.WorldHash);
    }

    [Fact]
    public void HashPersisted_DistinguishesChangedCheckpointBytes()
    {
        Assert.Equal(
            OperationalStateHasher.HashPersisted(Object(7), Generals),
            OperationalStateHasher.HashPersisted(Object(7), Generals));
        Assert.NotEqual(
            OperationalStateHasher.HashPersisted(Object(7), Generals),
            OperationalStateHasher.HashPersisted(Object(7, health: 1), Generals));
    }

    private OperationalStateHashes HashSample() =>
        OperationalStateHasher.Build(SampleObjects(), Globals());

    private IEnumerable<(string BucketKey, ulong ObjectHash)> SampleObjects()
    {
        yield return ("cell:0,0", OperationalStateHasher.HashPersisted(Object(1), Generals));
        yield return ("cell:0,0", OperationalStateHasher.HashPersisted(Object(2), Generals));
        yield return ("cell:1,0", OperationalStateHasher.HashPersisted(Object(3), Generals));
    }

    private IEnumerable<(string SectionName, ulong SectionHash)> Globals(uint randomWord = 42)
    {
        yield return ("random", OperationalStateHasher.HashBytes(BitConverter.GetBytes(randomWord)));
        yield return ("host", OperationalStateHasher.HashBytes(BitConverter.GetBytes(1234u)));
    }

    private static WorldObjectState Object(uint id, float health = 100) =>
        new()
        {
            ObjectId = id,
            DefinitionName = "TestObject",
            OwnerIndex = 0,
            Health = health,
            MaxHealth = 100,
        };
}
