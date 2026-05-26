using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using OpenSage.Content;
using OpenSage.Data;
using OpenSage.Data.Rep;
using OpenSage.Logic.Object;
using OpenSage.Logic.Orders;
using OpenSage.Tools.ReplaySketch.Model;

namespace OpenSage.Tools.ReplaySketch.Services;

/// <summary>
/// Known object-definition names for the initial USA vs GLA scope.
/// Object definition IDs used in replay orders are the <see cref="AssetHash"/> of
/// the lowercased definition name — the same key the <see cref="AssetStore"/> uses.
/// </summary>
public static class KnownDefinitions
{
    public const string UsaBarracksName = "AmericaBarracks";
    public const string UsaRangerName = "AmericaInfantryRanger";
    public const string GlaBarracksName = "GLABarracks";
    public const string GlaRebelName = "GLAInfantryRebel";

    public static int UsaBarracks => (int)AssetHash.GetHash(UsaBarracksName);
    public static int UsaRanger => (int)AssetHash.GetHash(UsaRangerName);
    public static int GlaBarracks => (int)AssetHash.GetHash(GlaBarracksName);
    public static int GlaRebel => (int)AssetHash.GetHash(GlaRebelName);
}

public static class ReplayExporter
{
    private const int ChecksumIntervalFrames = 150;

    /// <summary>
    /// Exports the scenario as a <c>.rep</c> file to <paramref name="outputPath"/>.
    /// Actions with <see cref="AfterActionCompletedTiming"/> are NOT written to the replay;
    /// they are returned via <paramref name="deferredActions"/> for runtime injection.
    /// Returns <see langword="null"/> on success, or an error message string on failure.
    /// </summary>
    public static string? Export(ReplayScenario scenario, MapMetadataService map, string outputPath,
        out IReadOnlyList<DeferredAction> deferredActions)
    {
        var rng = new Random();

        // ------------------------------------------------------------------
        // Build interleaved (frame, Order) sequence and collect deferred actions
        // ------------------------------------------------------------------
        var orders = new List<(uint Frame, Order Order)>();
        var deferred = new List<DeferredAction>();

        for (var ownerIdx = 0; ownerIdx < scenario.Players.Count; ownerIdx++)
        {
            var player = scenario.Players[ownerIdx];
            var enemyIdx = ownerIdx == 0 ? 1 : 0;

            var ctx = TerrainValidator.BuildContextPublic(scenario, map, ownerIdx, enemyIdx);

            uint cumulativeFrame = 0;
            foreach (var action in player.Actions)
            {
                var worldPos = action.Position?.Resolve(ctx, rng) ?? Vector3.Zero;

                if (action.Timing is AfterActionTiming aat)
                {
                    // Deferred: build the orders now (so positions are resolved) but don't
                    // place them in the replay file — the test harness injects them at runtime.
                    var actionOrders = BuildOrders(ownerIdx + 2, action, worldPos, player.FactionIndex)
                        .ToList();
                    deferred.Add(new DeferredAction(ownerIdx + 2, aat.TriggerAction, aat.TriggerPhase, actionOrders, aat.ExtraFrames));
                }
                else
                {
                    cumulativeFrame += action.Timing.Resolve(rng);

                    var actionOrders = BuildOrders(ownerIdx + 2, action, worldPos, player.FactionIndex);
                    foreach (var order in actionOrders)
                    {
                        orders.Add((cumulativeFrame, order));
                    }
                }
            }
        }

        deferredActions = deferred;

        // Sort by frame, then by player index (stable sort)
        orders.Sort((a, b) =>
        {
            var cmp = a.Frame.CompareTo(b.Frame);
            return cmp != 0 ? cmp : a.Order.PlayerIndex.CompareTo(b.Order.PlayerIndex);
        });

        // Inject Checksum orders
        var allOrders = InjectChecksums(orders);

        // ------------------------------------------------------------------
        // Build metadata
        // ------------------------------------------------------------------
        var slots = new List<ReplaySlot>();
        foreach (var player in scenario.Players)
        {
            slots.Add(ReplaySlot.CreateHuman(
                player.Name,
                player.Color,
                player.FactionIndex,
                player.StartPosition,
                player.Team));
        }

        // Fill remaining slots as empty up to 8
        while (slots.Count < 8)
        {
            slots.Add(ReplaySlot.CreateEmpty());
        }

        var metadata = ReplayMetadata.Create(
            mapFile: scenario.MapPath,
            mapCrc: 0,
            mapSize: 0,
            seed: Random.Shared.Next(),
            startingCredits: 10000,
            slots: slots.ToArray());

        var header = ReplayHeader.Create(metadata);

        // ------------------------------------------------------------------
        // Write to disk
        // ------------------------------------------------------------------
        try
        {
            using var stream = File.Open(outputPath, FileMode.Create, FileAccess.Write);
            ReplayFile.Write(stream, header, allOrders);
        }
        catch (Exception ex)
        {
            return $"Write failed: {ex.Message}";
        }

        // ------------------------------------------------------------------
        // Round-trip validation
        // ------------------------------------------------------------------
        try
        {
            RoundTripValidate(outputPath, metadata);
        }
        catch (Exception ex)
        {
            return ex.Message;
        }

        return null;
    }

    private static IEnumerable<(uint Frame, Order Order)> InjectChecksums(
        List<(uint Frame, Order Order)> orders)
    {
        if (orders.Count == 0) yield break;

        uint lastFrame = orders[^1].Frame;
        var orderIdx = 0;

        for (uint f = ChecksumIntervalFrames; f <= lastFrame + ChecksumIntervalFrames; f += ChecksumIntervalFrames)
        {
            // Emit all real orders that happen before this checksum frame
            while (orderIdx < orders.Count && orders[orderIdx].Frame < f)
            {
                yield return orders[orderIdx++];
            }

            // Emit checksum for player 0 (the game expects one per player per interval,
            // but a single one is sufficient to satisfy the NumTimecodes validation)
            var checksumOrder = new Order(0, OrderType.Checksum);
            checksumOrder.AddIntegerArgument(0); // dummy checksum value
            yield return (f, checksumOrder);
        }

        // Flush remaining real orders
        while (orderIdx < orders.Count)
        {
            yield return orders[orderIdx++];
        }
    }

    private static IEnumerable<Order> BuildOrders(
        int playerIndex, ActionEntry action, Vector3 worldPos, int factionIndex)
    {
        bool isGla = factionIndex == 4;

        switch (action.Type)
        {
            case ActionType.Construct:
                {
                    var defName = action.BuildingDefinitionName
                        ?? (isGla ? KnownDefinitions.GlaBarracksName : KnownDefinitions.UsaBarracksName);
                    var defId = (int)AssetHash.GetHash(defName);
                    yield return Order.CreateBuildObject(playerIndex, defId, worldPos, 0f);
                    break;
                }

            case ActionType.GatherResources:
                {
                    // Command the starting supply vehicle (placeholder ObjectId 2) to begin gathering.
                    // The vehicle's AI will locate the nearest supply source automatically.
                    yield return Order.CreateSupplyGatherDump(playerIndex, new ObjectId(2));
                    break;
                }

            case ActionType.RecruitBasicUnit:
                {
                    var unitName = action.UnitDefinitionName
                        ?? (isGla ? KnownDefinitions.GlaRebelName : KnownDefinitions.UsaRangerName);
                    var unitDefId = (int)AssetHash.GetHash(unitName);
                    // Clear the selection before recruiting so the OrderProcessor falls back to
                    // finding the correct producer building (Barracks) via its CommandSet, rather
                    // than routing the order to whatever was previously selected.
                    yield return Order.CreateClearSelection(playerIndex);
                    // Queue 5 units so there are enough attackers to destroy the enemy CC.
                    // Each unit that exits the barracks will re-trigger AttackEnemyBase,
                    // routing all available units toward the enemy base.
                    for (var i = 0; i < 5; i++)
                    {
                        var recruitOrder = new Order(playerIndex, OrderType.CreateUnit);
                        recruitOrder.AddIntegerArgument(unitDefId); // arg[0] = definition InstanceId (hash)
                        recruitOrder.AddIntegerArgument(1);         // arg[1] = place in queue
                        yield return recruitOrder;
                    }
                    break;
                }

            case ActionType.AttackEnemyBase:
                {
                    // Clear the selection first so ForceAttackGround falls back to all
                    // CanAttack units. Without this, the barracks remains selected from the
                    // preceding CreateUnit order and it cannot attack, causing the ranger to
                    // stand still.
                    yield return Order.CreateClearSelection(playerIndex);
                    yield return Order.CreateAttackGround(playerIndex, worldPos);
                    break;
                }
        }
    }

    /// <summary>
    /// Re-parses the written file and verifies that the key metadata fields survived
    /// the write/read cycle. Throws <see cref="InvalidDataException"/> on any mismatch.
    /// </summary>
    private static void RoundTripValidate(string path, ReplayMetadata expected)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream, Encoding.Unicode, leaveOpen: false);

        var header = ReplayHeader.Parse(reader);
        var actual = header.Metadata;

        var errors = new StringBuilder();

        if (header.GameType != ReplayGameType.Generals)
            errors.AppendLine($"GameType mismatch: expected Generals, got {header.GameType}");

        if (actual.MapFile != expected.MapFile)
            errors.AppendLine($"MapFile mismatch: expected '{expected.MapFile}', got '{actual.MapFile}'");

        if ((actual.Slots?.Length ?? 0) != (expected.Slots?.Length ?? 0))
            errors.AppendLine($"Slot count mismatch: expected {expected.Slots?.Length}, got {actual.Slots?.Length}");

        if (actual.SD != expected.SD)
            errors.AppendLine($"SD (seed) mismatch: expected {expected.SD}, got {actual.SD}");

        if (actual.StartingCredits != expected.StartingCredits)
            errors.AppendLine($"StartingCredits mismatch: expected {expected.StartingCredits}, got {actual.StartingCredits}");

        if (errors.Length > 0)
            throw new InvalidDataException($"Round-trip validation failed:\n{errors}");
    }
}
