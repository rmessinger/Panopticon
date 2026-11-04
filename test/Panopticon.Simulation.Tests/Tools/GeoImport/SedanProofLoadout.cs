using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Panopticon.Tests.Tools.GeoImport;

/// <summary>Validated proof inputs. Disposing releases the parsed prepared manifest.</summary>
public sealed class SedanProofLoadout : IDisposable
{
    private readonly JsonDocument _preparedManifest;

    internal SedanProofLoadout(
        string proofRoot,
        string freezeStage,
        string selectionTsvPath,
        string frozenManifestPath,
        string receiptPath,
        string preparedManifestPath,
        string producerReceiptPath,
        string integrityManifestPath,
        string candidatesTsvSha256,
        string selectionTsvSha256,
        string frozenManifestCanonicalSha256,
        IReadOnlyList<SedanProofSite> primarySites,
        IReadOnlyList<SedanProofSite> alternateSites,
        IReadOnlyList<string> workerRegistry,
        string preparationReceiptPath,
        string commandsLedgerPath,
        string previewTexturesPath,
        double receiptOriginMeters,
        DateTimeOffset freezeVerifiedAt,
        DateTimeOffset firstDemRasterIoAt,
        JsonDocument preparedManifest)
    {
        PreparationReceiptPath = preparationReceiptPath;
        CommandsLedgerPath = commandsLedgerPath;
        PreviewTexturesPath = previewTexturesPath;
        ReceiptOriginMeters = receiptOriginMeters;
        FreezeVerifiedAt = freezeVerifiedAt;
        FirstDemRasterIoAt = firstDemRasterIoAt;
        ProofRoot = proofRoot;
        FreezeStage = freezeStage;
        SelectionTsvPath = selectionTsvPath;
        FrozenManifestPath = frozenManifestPath;
        ReceiptPath = receiptPath;
        PreparedManifestPath = preparedManifestPath;
        ProducerReceiptPath = producerReceiptPath;
        IntegrityManifestPath = integrityManifestPath;
        CandidatesTsvSha256 = candidatesTsvSha256;
        SelectionTsvSha256 = selectionTsvSha256;
        FrozenManifestCanonicalSha256 = frozenManifestCanonicalSha256;
        PrimarySites = primarySites;
        AlternateSites = alternateSites;
        WorkerRegistry = workerRegistry;
        _preparedManifest = preparedManifest;
    }

    public string ProofRoot { get; }

    public string FreezeStage { get; }

    public string SelectionTsvPath { get; }

    public string FrozenManifestPath { get; }

    public string ReceiptPath { get; }

    public string PreparedManifestPath { get; }

    public string ProducerReceiptPath { get; }

    public string IntegrityManifestPath { get; }

    public string CandidatesTsvSha256 { get; }

    public string SelectionTsvSha256 { get; }

    public string FrozenManifestCanonicalSha256 { get; }

    public IReadOnlyList<SedanProofSite> PrimarySites { get; }

    public IReadOnlyList<SedanProofSite> AlternateSites { get; }

    /// <summary>Base and banded worker sha256 values, both approved by the completed freeze.</summary>
    public IReadOnlyList<string> WorkerRegistry { get; }

    public string PreparationReceiptPath { get; }

    public string CommandsLedgerPath { get; }

    public string PreviewTexturesPath { get; }

    public double ReceiptOriginMeters { get; }

    public DateTimeOffset FreezeVerifiedAt { get; }

    public DateTimeOffset FirstDemRasterIoAt { get; }

    public JsonDocument PreparedManifest => _preparedManifest;

    public void Dispose()
    {
        _preparedManifest.Dispose();
    }
}
