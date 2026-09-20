#nullable enable

namespace OpenSage.Tools.ReplaySketch.Model;

/// <summary>
/// Versioned preview request shared by ReplaySketch Core and TestHarness. Distinct model and
/// optional animation source roots; caller-owned absolute output paths; preview corrections
/// and importer options exactly as the session holds them. No parser objects, importer
/// interfaces, streams, or filesystem instances.
/// </summary>
public sealed record ExternalModelRequest
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; }

    public int Generation { get; init; }

    public ExternalModelSourceRequest ModelSource { get; init; } = null!;

    public ExternalModelSourceRequest? AnimationSource { get; init; }

    public ExternalModelImportOptions ImportOptions { get; init; } = ExternalModelImportOptions.Default;

    public ExternalModelPreviewOptions PreviewOptions { get; init; } = ExternalModelPreviewOptions.Default;

    public ExternalModelViewport Viewport { get; init; } = new(256, 256);

    /// <summary>Null for report-only execution; otherwise an absolute image output path.</summary>
    public string? OutputImagePath { get; init; }

    /// <summary>Required absolute report output path.</summary>
    public string OutputReportPath { get; init; } = "";
}
