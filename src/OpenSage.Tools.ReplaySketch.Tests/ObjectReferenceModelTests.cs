using System.Text.Json;
using OpenSage.Logic.Object;
using OpenSage.Tools.ReplaySketch.Model;

namespace OpenSage.Tests.Tools.ReplaySketch;

public class ObjectReferenceModelTests
{
    [Fact]
    public void HandleCommandArgument_SerializesAndDeserializes()
    {
        CommandArgument argument = new HandleCommandArgument("cmd-1");

        var json = JsonSerializer.Serialize(argument);
        var deserialized = JsonSerializer.Deserialize<CommandArgument>(json);
        var typed = Assert.IsType<HandleCommandArgument>(deserialized);

        Assert.Contains("\"kind\":\"handle\"", json);
        Assert.Equal("cmd-1", typed.CommandId);
    }

    [Fact]
    public void QuerySelectorCommandArgument_SerializesAndDeserializes()
    {
        CommandArgument argument = new QuerySelectorCommandArgument("enemy", "AmericaBarracks");

        var json = JsonSerializer.Serialize(argument);
        var deserialized = JsonSerializer.Deserialize<CommandArgument>(json);
        var typed = Assert.IsType<QuerySelectorCommandArgument>(deserialized);

        Assert.Contains("\"kind\":\"query\"", json);
        Assert.Equal("enemy", typed.Relationship);
        Assert.Equal("AmericaBarracks", typed.DefinitionName);
    }

    [Fact]
    public void CommandSpec_Resolve_SubstitutesHandle()
    {
        var command = CommandSpec.CreateOpen(
            OpenSage.Logic.Orders.OrderType.AttackObject,
            new HandleCommandArgument("cmd-1"));

        var resolved = command.Resolve(new StubResolver());

        var argument = Assert.IsType<ObjectIdCommandArgument>(Assert.Single(resolved.Arguments));
        Assert.Equal(42u, argument.Value);
    }

    [Fact]
    public void HandleCommandArgument_AppendTo_Throws()
    {
        var argument = new HandleCommandArgument("cmd-1");
        var order = new OpenSage.Logic.Orders.Order(2, OpenSage.Logic.Orders.OrderType.AttackObject);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            argument.AppendTo(order, ctx: null!, new Random(0)));

        Assert.Contains("cmd-1", ex.Message);
    }

    private sealed class StubResolver : IObjectReferenceResolver
    {
        public ObjectId ResolveHandle(string commandId) =>
            commandId == "cmd-1" ? new ObjectId(42) : ObjectId.Invalid;

        public ObjectId ResolveSelector(QuerySelectorCommandArgument selector) => ObjectId.Invalid;
    }
}
