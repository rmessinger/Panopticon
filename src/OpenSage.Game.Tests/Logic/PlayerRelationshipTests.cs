using OpenSage.Logic;
using OpenSage.Mathematics;
using Xunit;

namespace OpenSage.Tests.Logic;

public class PlayerRelationshipTests : MockedGameTest
{
    [Fact]
    public void AddEnemy_MakesRelationshipReciprocal()
    {
        var first = CreatePlayer(1);
        var second = CreatePlayer(2);

        first.AddEnemy(second);

        Assert.Contains(second, first.Enemies);
        Assert.Contains(first, second.Enemies);
        Assert.Single(first.Enemies);
        Assert.Single(second.Enemies);
    }

    [Fact]
    public void AddAlly_MakesRelationshipReciprocal()
    {
        var first = CreatePlayer(1);
        var second = CreatePlayer(2);

        first.AddAlly(second);

        Assert.Contains(second, first.Allies);
        Assert.Contains(first, second.Allies);
        Assert.Single(first.Allies);
        Assert.Single(second.Allies);
    }

    [Fact]
    public void AddEnemy_IsIdempotent()
    {
        var first = CreatePlayer(1);
        var second = CreatePlayer(2);

        first.AddEnemy(second);
        first.AddEnemy(second);

        Assert.Single(first.Enemies);
        Assert.Single(second.Enemies);
    }

    private Player CreatePlayer(uint id) => new(id, null, new ColorRgb(0, 0, 0), Generals);
}
