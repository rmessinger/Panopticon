#nullable enable

using System.Collections.Generic;
using OpenSage;
using OpenSage.Content.Loaders;
using OpenSage.Graphics;
using OpenSage.Graphics.Animation;
using OpenSage.Tools.ReplaySketch.Model;

namespace OpenSage.Tools.TestHarness.ExternalModels;

/// <summary>Inputs to one render-stage execution.</summary>
internal sealed record ExternalModelRenderInputs(
    Game Game,
    Model RuntimeModel,
    AssetLoadContext LoadContext,
    ModelAnimation? Animation,
    ExternalModelRequest Request);

/// <summary>The outcome of one render-stage execution.</summary>
internal sealed record ExternalModelRenderOutcome(
    bool Succeeded,
    IReadOnlyList<ExternalModelDiagnostic> Diagnostics,
    long? PngByteLength,
    string? PngContentHash);

/// <summary>
/// Injectable render stage implemented by WI-C5-08. The default stage returns a structured
/// not-rendered result after successful materialization and binding.
/// </summary>
internal interface IExternalModelRenderStage
{
    ExternalModelRenderOutcome Render(ExternalModelRenderInputs inputs);
}
