#nullable enable

using System.Collections.Generic;
using Panopticon.FileFormats.M3;

namespace Panopticon.Content.Importing.M3;

internal sealed class M3ImportDiagnostics
{
    private readonly List<M3SkippedBatchDiagnostic> _skippedBatches = new();
    private readonly List<M3IgnoredLayerDiagnostic> _ignoredLayers = new();

    public IReadOnlyList<M3SkippedBatchDiagnostic> SkippedBatches => _skippedBatches;

    public IReadOnlyList<M3IgnoredLayerDiagnostic> IgnoredLayers => _ignoredLayers;

    public void AddSkippedBatch(int batchIndex, int materialReferenceIndex, uint blendMode, string reason)
    {
        _skippedBatches.Add(new M3SkippedBatchDiagnostic(batchIndex, materialReferenceIndex, blendMode, reason));
    }

    public void AddIgnoredLayer(string materialName, M3MaterialLayerSlot slot, string path, string reason)
    {
        _ignoredLayers.Add(new M3IgnoredLayerDiagnostic(materialName, slot, path, reason));
    }
}

internal sealed record M3SkippedBatchDiagnostic(
    int BatchIndex,
    int MaterialReferenceIndex,
    uint BlendMode,
    string Reason);

internal sealed record M3IgnoredLayerDiagnostic(
    string MaterialName,
    M3MaterialLayerSlot Slot,
    string Path,
    string Reason);
