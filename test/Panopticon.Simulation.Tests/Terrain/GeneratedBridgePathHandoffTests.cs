#nullable enable

using System.Numerics;
using Panopticon.Data.Map;
using Panopticon.Terrain;
using Xunit;

namespace Panopticon.Tests.Terrain;

public sealed class GeneratedBridgePathHandoffTests
{
    [Fact]
    public void Handoff_IsMarkedOnlyForTheCurrentVersion()
    {
        var absent = new MapObject(Vector3.Zero, 0f, RoadType.BridgeStart, "Concrete");

        Assert.False(GeneratedBridgePathHandoff.IsPresent(absent));
        Assert.False(GeneratedBridgePathHandoff.IsMarked(absent));

        var current = new MapObject(Vector3.Zero, 0f, RoadType.BridgeStart, "Concrete");
        current.Properties.AddInteger(GeneratedBridgePathHandoff.Key, GeneratedBridgePathHandoff.Version);

        Assert.True(GeneratedBridgePathHandoff.IsPresent(current));
        Assert.True(GeneratedBridgePathHandoff.IsMarked(current));

        var future = new MapObject(Vector3.Zero, 0f, RoadType.BridgeStart, "Concrete");
        future.Properties.AddInteger(GeneratedBridgePathHandoff.Key, 2);

        Assert.True(GeneratedBridgePathHandoff.IsPresent(future));
        Assert.False(GeneratedBridgePathHandoff.IsMarked(future));

        var wrongType = new MapObject(Vector3.Zero, 0f, RoadType.BridgeStart, "Concrete");
        wrongType.Properties.AddAsciiString(GeneratedBridgePathHandoff.Key, "1");

        Assert.True(GeneratedBridgePathHandoff.IsPresent(wrongType));
        Assert.False(GeneratedBridgePathHandoff.IsMarked(wrongType));
    }
}
