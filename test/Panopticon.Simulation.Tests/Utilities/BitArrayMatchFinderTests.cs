using System;
using System.Collections.Generic;
using Panopticon.Logic.Object;
using Panopticon.Mathematics;
using Panopticon.Utilities;
using Xunit;

namespace Panopticon.Tests.Utilities;

public class BitArrayMatchFinderTests
{
    private sealed class TestState : IConditionState<ModelConditionFlag>
    {
        public string Name { get; }

        private readonly BitArray<ModelConditionFlag>[] _flags;

        public ReadOnlySpan<BitArray<ModelConditionFlag>> ConditionFlags => _flags;

        public TestState(string name, params ModelConditionFlag[] flags)
        {
            Name = name;
            // A state with no flags (DefaultConditionState) still owns one empty flag set.
            _flags = [new BitArray<ModelConditionFlag>(flags)];
        }
    }

    private static TestState State(string name, params ModelConditionFlag[] flags) => new(name, flags);

    private static BitArray<ModelConditionFlag> Flags(params ModelConditionFlag[] flags) => new(flags);

    /// <summary>
    /// The Aurora draw-module state matrix, in authored order. A pristine jet flying with
    /// exhaust and moving flags must resolve to the JETEXHAUST state, never the
    /// later-defined REALLYDAMAGED or RUBBLE wreck states that share the exhaust bit.
    /// </summary>
    [Fact]
    public void PristineJetWithExhaustAndMoving_ResolvesExhaustState_NotWreck()
    {
        var states = new List<TestState>
        {
            State("Default"),
            State("JETEXHAUST", ModelConditionFlag.JetExhaust),
            State("JETEXHAUST+JETAFTERBURNER", ModelConditionFlag.JetExhaust, ModelConditionFlag.JetAfterburner),
            State("REALLYDAMAGED", ModelConditionFlag.ReallyDamaged),
            State("REALLYDAMAGED+JETEXHAUST", ModelConditionFlag.ReallyDamaged, ModelConditionFlag.JetExhaust),
            State("REALLYDAMAGED+JETEXHAUST+JETAFTERBURNER", ModelConditionFlag.ReallyDamaged, ModelConditionFlag.JetExhaust, ModelConditionFlag.JetAfterburner),
            State("RUBBLE", ModelConditionFlag.Rubble),
            State("RUBBLE+JETEXHAUST+JETAFTERBURNER", ModelConditionFlag.Rubble, ModelConditionFlag.JetExhaust, ModelConditionFlag.JetAfterburner),
        };

        var cruise = BitArrayMatchFinder.FindBest(states.ToArray(), Flags(ModelConditionFlag.JetExhaust, ModelConditionFlag.Moving));
        Assert.Equal("JETEXHAUST", cruise.Name);

        var takeoff = BitArrayMatchFinder.FindBest(states.ToArray(), Flags(ModelConditionFlag.JetExhaust, ModelConditionFlag.JetAfterburner));
        Assert.Equal("JETEXHAUST+JETAFTERBURNER", takeoff.Name);

        var damaged = BitArrayMatchFinder.FindBest(states.ToArray(), Flags(ModelConditionFlag.ReallyDamaged, ModelConditionFlag.JetExhaust, ModelConditionFlag.Moving));
        Assert.Equal("REALLYDAMAGED+JETEXHAUST", damaged.Name);

        var wreck = BitArrayMatchFinder.FindBest(states.ToArray(), Flags(ModelConditionFlag.Rubble, ModelConditionFlag.JetExhaust, ModelConditionFlag.JetAfterburner));
        Assert.Equal("RUBBLE+JETEXHAUST+JETAFTERBURNER", wreck.Name);

        var parked = BitArrayMatchFinder.FindBest(states.ToArray(), new BitArray<ModelConditionFlag>());
        Assert.Equal("Default", parked.Name);
    }

    /// <summary>
    /// The generic-before-specific convention still holds: ATTACKING alone resolves to the
    /// dedicated ATTACKING state rather than the earlier MOVING+ATTACKING alias, and
    /// ATTACKING+USING_WEAPON_A resolves to the firing state.
    /// </summary>
    [Fact]
    public void SpecificStatesStillBeatGenericAliases()
    {
        var states = new List<TestState>
        {
            State("Default"),
            State("MOVING", ModelConditionFlag.Moving),
            State("MOVING+ATTACKING", ModelConditionFlag.Moving, ModelConditionFlag.Attacking),
            State("ATTACKING", ModelConditionFlag.Attacking),
            State("USING_WEAPON_A", ModelConditionFlag.UsingWeaponA),
            State("ATTACKING+USING_WEAPON_A", ModelConditionFlag.Attacking, ModelConditionFlag.UsingWeaponA),
        };

        var attacking = BitArrayMatchFinder.FindBest(states.ToArray(), Flags(ModelConditionFlag.Attacking));
        Assert.Equal("ATTACKING", attacking.Name);

        var firing = BitArrayMatchFinder.FindBest(states.ToArray(), Flags(ModelConditionFlag.Attacking, ModelConditionFlag.UsingWeaponA));
        Assert.Equal("ATTACKING+USING_WEAPON_A", firing.Name);

        var moving = BitArrayMatchFinder.FindBest(states.ToArray(), Flags(ModelConditionFlag.Moving));
        Assert.Equal("MOVING", moving.Name);
    }
}
