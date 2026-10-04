#nullable enable annotations
using System;
using System.Collections.Generic;
using System.IO;
using Panopticon.Data.Sav;
using Panopticon.Mathematics;
using Panopticon.Simulation;

namespace Panopticon.Recording;

/// <summary>
/// Restores a booted <see cref="SimulationHost"/> from an <see cref="EngineCheckpointData"/>.
/// The host must already run the same scenario the checkpoint was captured from; the engine
/// section loads through <see cref="SaveFile.LoadFromStream(Stream, IGame)"/>, whose
/// start-campaign restart is bypassed because the booted game is already in-game (WI-A1-13).
/// </summary>
public static class EngineCheckpointRestorer
{
    public static void Restore(SimulationHost host, EngineCheckpointData checkpoint)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(checkpoint);

        var game = host.Game;

        RestoreEngineSection(checkpoint, game);
        RestoreRandomSection(checkpoint, game);
        RestoreHostSection(checkpoint, host);
        RestoreDomainRuntimeSection(checkpoint, host);
        VerifyDomainStateSection(checkpoint, host);
    }

    private static void RestoreEngineSection(EngineCheckpointData checkpoint, IGame game)
    {
        using var stream = new MemoryStream(checkpoint.Sections[EngineCheckpointData.EngineSection], writable: false);
        SaveFile.LoadFromStream(stream, game);
    }

    private static void RestoreRandomSection(EngineCheckpointData checkpoint, IGame game)
    {
        var (_, words) = EngineCheckpoint.DecodeRandomSection(checkpoint.Sections[EngineCheckpointData.RandomSection]);
        if (game.GameLogic?.Random is not SageRandom sageRandom)
        {
            throw new InvalidOperationException("Checkpoint restore requires the logic random to be a SageRandom.");
        }

        sageRandom.RestoreState(words);
    }

    private static void RestoreHostSection(EngineCheckpointData checkpoint, SimulationHost host)
    {
        var (frame, mapTime, pendingOrders) = EngineCheckpoint.DecodeHostSection(checkpoint.Sections[EngineCheckpointData.HostSection]);
        host.RestoreHostState(frame, mapTime, pendingOrders);
    }

    private static void RestoreDomainRuntimeSection(EngineCheckpointData checkpoint, SimulationHost host)
    {
        if (!checkpoint.Sections.TryGetValue(EngineCheckpointData.DomainRuntimeSection, out var section))
        {
            return;
        }

        if (host.DomainModel is not IReconstructableDomain domain)
        {
            throw new InvalidOperationException("Checkpoint carries a domain-runtime section but the host domain is not reconstructable.");
        }

        using var stream = new MemoryStream(section, writable: false);
        using var reader = new StateReader(stream, host.Game);
        domain.PersistRuntime(reader);
    }

    private static void VerifyDomainStateSection(EngineCheckpointData checkpoint, SimulationHost host)
    {
        if (!checkpoint.Sections.TryGetValue(EngineCheckpointData.DomainStateSection, out var section))
        {
            return;
        }

        if (host.DomainModel is not IDomainStateProjection projection)
        {
            throw new InvalidOperationException("Checkpoint carries a domain-state section but the host domain has no state projection.");
        }

        // A mismatch means the checkpoint misses state the domain projects (a persistence
        // coverage bug), not simulation drift: restore happens before any frame is stepped.
        var fresh = EngineCheckpoint.CaptureDomainStateSection(host, projection);
        if (!fresh.AsSpan().SequenceEqual(section))
        {
            throw new InvalidDataException(
                $"Domain state at frame {checkpoint.Header.Frame} does not match the checkpoint domain-state section.");
        }
    }
}
