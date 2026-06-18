// P0 acceptance tests — Core-only deliverables (no game engine, no game files).
// These are STUBS: each test throws NotImplementedException until the corresponding
// P0 sub-phase is implemented. See §8 of docs/roadmap/deep-dives/playable-baseline-harness.md
// for the full spec.
//
// Run with: dotnet test src/OpenSage.Tools.ReplaySketch.Tests

namespace OpenSage.Tests.Tools.ReplaySketch;

// ── P0-1c: MAUI command-flow testability seams ────────────────────────────────
// MainThread dispatcher + Process.Start seams injected so ValidateCommand,
// ExportCommand, PlayInGameCommand, and background-load apply-results are
// unit-testable without a MAUI runtime.

public class P0_1c_MauiCommandFlowTests
{
    [Fact]
    public void ValidateCommand_WhenMapLoaded_SetsValidationResult()
    {
        // Arrange: construct MainViewModel with MemoryPreferencesStore + synchronous dispatcher
        // + stub map. Act: execute ValidateCommand. Assert: ValidationOk / ValidationResult set.
        throw new NotImplementedException("P0-1c: inject IMainThreadDispatcher seam; exercise ValidateCommand headlessly");
    }

    [Fact]
    public void ExportCommand_WhenMapLoaded_SetsExportResult()
    {
        // Assert: ExportOk / ExportResult set; export file written.
        throw new NotImplementedException("P0-1c: inject IMainThreadDispatcher seam; exercise ExportCommand headlessly");
    }

    [Fact]
    public void PlayInGameCommand_RecordsLauncherInvocation()
    {
        // Assert: stub IProcessLauncher records the expected path + args; no real process spawned.
        throw new NotImplementedException("P0-1c: inject IProcessLauncher seam; verify PlayInGame records the invocation");
    }

    [Fact]
    public void BackgroundLoad_OnCompletion_AppliesMapAndUpdatesCommandStates()
    {
        // Assert: after background load completes (via synchronous dispatcher), Map is set and
        // command states (CanExecute) are updated.
        throw new NotImplementedException("P0-1c: synchronous dispatcher seam lets us drive the apply-results path headlessly");
    }
}

// ── P0-2: Generic definition resolution ──────────────────────────────────────
// KnownDefinitions hardcoding removed; any object definition resolves by name.

public class P0_2_GenericDefinitionResolutionTests
{
    [Fact]
    public void ReplayExporter_HasNoHardcodedFactionNames()
    {
        // Assert: ReplayExporter source contains no literal "AmericaInfantryRanger",
        // "GLAInfantryRebel", or similar hardcoded faction-specific strings.
        throw new NotImplementedException("P0-2: remove KnownDefinitions; verify no hardcoded faction strings remain in ReplayExporter");
    }

    [Fact]
    public void CommandSpec_WithNonDefaultFaction_ResolvesDefinitionByName()
    {
        // Arrange: a CommandSpec referencing a non-USA/GLA definition name.
        // Act: resolve via AssetStore.
        // Assert: the resolved hash matches the expected definition.
        throw new NotImplementedException("P0-2: AssetStore-based resolution for any faction/definition name");
    }
}

// ── P0-2b: Per-player/owner targeting ────────────────────────────────────────
// A scenario with ≥3 players correctly attributes each command to its PlayerIndex.

public class P0_2b_PlayerOwnerTargetingTests
{
    [Fact]
    public void Scenario_ThreePlayers_CommandsAttributedToCorrectPlayerIndex()
    {
        // Arrange: scenario with 3 players each issuing a distinct command.
        // Act: export to orders.
        // Assert: each order carries the PlayerIndex of the player who authored it.
        throw new NotImplementedException("P0-2b: ActionEntry/scenario targets any player; per-player order attribution");
    }
}

// ── P0-6: Procedural authoring at scale ──────────────────────────────────────
// ≥10k programmatically-generated orders export and load within budget.

public class P0_6_ProceduralAuthoringTests
{
    [Fact]
    public void ProceduralScenario_TenThousandOrders_ExportsAndLoadsWithinBudget()
    {
        // Arrange: programmatically generate a scenario with ≥10k commands (no hand-entry).
        // Act: export to .rep; measure elapsed time and peak allocation.
        // Assert: export time < documented budget; .rep round-trips without error;
        // no per-command hand-entry in the generation loop.
        throw new NotImplementedException("P0-6: ≥10k procedurally-generated orders export within time/memory budget");
    }
}
