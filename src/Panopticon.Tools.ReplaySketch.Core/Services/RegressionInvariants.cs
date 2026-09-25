using System.Linq;
using Panopticon;
using Panopticon.Tools.ReplaySketch.Assertions;
using Panopticon.Tools.ReplaySketch.Model;

namespace Panopticon.Tools.ReplaySketch.Services;

/// <summary>
/// The three universal P7 invariants (deep dive section 3), attached to every regression cell and
/// checked per tick. Implemented as <see cref="CodeAssertion"/> predicates so they can express
/// "for every player" logic that the JSON-path assertion vocabulary cannot.
///
/// The headless [Fact] gate evaluates them directly against the mocked game state; the live
/// <c>AssertionEvaluator</c> re-evaluates them every tick via <c>Game.Updating</c> and aborts on the
/// first violation.
/// </summary>
public static class RegressionInvariants
{
    public const string MoneyNeverNegative = "p7-money-never-negative";
    public const string NoUnitDuplication = "p7-no-unit-duplication";
    public const string AiMadeProgress = "p7-ai-made-progress";

    private static bool Registered;

    /// <summary>Idempotently registers the three predicates with the global registry.</summary>
    public static void EnsureRegistered()
    {
        if (Registered)
        {
            return;
        }

        CodeAssertionRegistry.Register(MoneyNeverNegative, (game, _) =>
            game.PlayerManager.Players.All(p => p.BankAccount.Money >= 0));

        CodeAssertionRegistry.Register(NoUnitDuplication, (game, _) =>
            game.PlayerManager.Players.All(p =>
                p.OwnedObjects.Count == p.OwnedObjects.Select(o => o.Id).Distinct().Count()));

        // AI-progress proxy: every non-human competitive player (PlayerManager index >= 2, the
        // VictorySystem's competitive threshold) must own at least one object. The live run
        // additionally requires >= 1 issued order; owned-object count is the headless-evaluable proxy.
        CodeAssertionRegistry.Register(AiMadeProgress, (game, _) =>
        {
            for (var i = 0; i < game.PlayerManager.Players.Count; i++)
            {
                var player = game.PlayerManager.Players[i];
                if (i < 2 || player.IsHuman)
                {
                    continue;
                }

                if (player.OwnedObjects.Count == 0)
                {
                    return false;
                }
            }

            return true;
        });

        Registered = true;
    }

    /// <summary>The three universal invariants as harness assertions, in a stable order.</summary>
    public static System.Collections.Generic.IReadOnlyList<ScenarioAssertion> Assertions =>
    [
        new CodeAssertion { Id = MoneyNeverNegative, PredicateName = MoneyNeverNegative },
        new CodeAssertion { Id = NoUnitDuplication, PredicateName = NoUnitDuplication },
        new CodeAssertion { Id = AiMadeProgress, PredicateName = AiMadeProgress },
    ];
}
