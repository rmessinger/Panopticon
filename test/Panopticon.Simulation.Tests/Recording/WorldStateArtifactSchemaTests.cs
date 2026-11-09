using System;
using System.IO;
using System.Numerics;
using Panopticon;
using Panopticon.Recording;
using Xunit;

namespace Panopticon.Tests.Recording;

public sealed class WorldStateArtifactSchemaTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"h2b-15-schema-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void CurrentSchema_RoundTrips()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "current.bin");
        var summary = new WorldStateSummary { Frame = 0 };
        summary.Objects.Add(new WorldObjectState
        {
            ObjectId = 1,
            DefinitionName = "Thing1",
            OwnerIndex = 0,
            Position = new Vector3(10f, 10f, 0f),
            Health = 90f,
            MaxHealth = 100f,
        });
        summary.PlayerMoney.Add(1000u);

        using (var writer = new WorldStateArtifactWriter(path, SageGame.CncGenerals, _ => "near"))
        {
            writer.WriteSnapshot(summary);
        }

        using var reader = new WorldStateArtifactReader(path, SageGame.CncGenerals);
        var loaded = reader.ReconstructStateAt(0u);

        Assert.Equal(summary.Objects.Count, loaded.Objects.Count);
        Assert.Equal(summary.Objects[0].Health, loaded.Objects[0].Health);
        Assert.Equal(summary.PlayerMoney, loaded.PlayerMoney);
    }

    [Fact]
    public void OlderSchemaHeader_RejectedWithTypedRejection()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "older.bin");
        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
        using (var writer = new StateWriter(stream, SageGame.CncGenerals))
        {
            var tag = RegionIndexedFormat.SchemaHeaderTag;
            writer.PersistByte(ref tag, "RecordType");
            var header = new RegionSchemaHeader
            {
                SchemaVersion = RegionIndexedFormat.CurrentSchemaVersion - 1,
                RegionSchemaId = RegionIndexedFormat.DefaultSchemaId,
            };
            writer.PersistObject(header, "header");
        }

        var error = Assert.Throws<InvalidOperationException>(() => new WorldStateArtifactReader(path, SageGame.CncGenerals));
        Assert.Contains($"Region schema version {RegionIndexedFormat.CurrentSchemaVersion - 1} is not understood", error.Message);
    }
}
