using OpenSage.Logic.Object;
using OpenSage.Tools.ReplaySketch.Services;

namespace OpenSage.Tests.Tools.ReplaySketch;

public class HandleTrackerTests
{
    [Fact]
    public void BeginCommand_OnObjectCreated_EndCommand_ResolveHandle_ReturnsId()
    {
        var tracker = new HandleTracker();
        var expected = new ObjectId(42);

        tracker.BeginCommand("cmd-a");
        tracker.OnObjectCreated(expected);
        tracker.EndCommand();

        Assert.Equal(expected, tracker.ResolveHandle("cmd-a"));
    }

    [Fact]
    public void ResolveHandle_WithoutBeginCommand_ReturnsInvalid()
    {
        var tracker = new HandleTracker();
        tracker.OnObjectCreated(new ObjectId(7));

        Assert.Equal(ObjectId.Invalid, tracker.ResolveHandle("cmd-x"));
    }

    [Fact]
    public void ResolveHandle_BeginCommandButNoObjectCreated_ReturnsInvalid()
    {
        var tracker = new HandleTracker();

        tracker.BeginCommand("cmd-b");
        tracker.EndCommand();

        Assert.Equal(ObjectId.Invalid, tracker.ResolveHandle("cmd-b"));
    }

    [Fact]
    public void OnObjectCreated_AfterEndCommand_NotAttributed()
    {
        var tracker = new HandleTracker();

        tracker.BeginCommand("cmd-c");
        tracker.EndCommand();
        tracker.OnObjectCreated(new ObjectId(5));

        Assert.Equal(ObjectId.Invalid, tracker.ResolveHandle("cmd-c"));
    }

    [Fact]
    public void OnObjectCreated_OnlyFirstObjectPerCommand_IsTracked()
    {
        var tracker = new HandleTracker();

        tracker.BeginCommand("cmd-d");
        tracker.OnObjectCreated(new ObjectId(10));
        tracker.OnObjectCreated(new ObjectId(11));
        tracker.EndCommand();

        Assert.Equal(new ObjectId(10), tracker.ResolveHandle("cmd-d"));
    }

    [Fact]
    public void StackSemantics_InnerCommandGetsItsOwnObject()
    {
        var tracker = new HandleTracker();

        tracker.BeginCommand("outer");
        tracker.BeginCommand("inner");
        tracker.OnObjectCreated(new ObjectId(20));
        tracker.EndCommand(); // ends "inner"
        tracker.OnObjectCreated(new ObjectId(21));
        tracker.EndCommand(); // ends "outer"

        Assert.Equal(new ObjectId(20), tracker.ResolveHandle("inner"));
        Assert.Equal(new ObjectId(21), tracker.ResolveHandle("outer"));
    }
}
