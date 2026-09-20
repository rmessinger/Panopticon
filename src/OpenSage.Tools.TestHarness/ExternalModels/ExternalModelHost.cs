#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenSage.Content;
using OpenSage.Content.Importing;
using OpenSage.Content.Importing.Gltf;
using OpenSage.Content.Loaders;
using OpenSage.Data;
using OpenSage.Graphics;
using OpenSage.Graphics.Animation;
using OpenSage.Logic.Object;
using OpenSage.Mathematics;
using OpenSage.Mods.BuiltIn;
using OpenSage.Tools.ReplaySketch.Model;

namespace OpenSage.Tools.TestHarness.ExternalModels;

/// <summary>
/// TestHarness external-model mode: parses and validates the request, resolves the game
/// installation only afterwards, mounts isolated model and optional animation sources,
/// imports CPU graphs, materializes through an external AssetLoadContext whose filesystem
/// is the model mount, binds the optional animation through C2, runs the injectable render
/// stage, and disposes every owned resource in reverse order on all paths.
/// </summary>
internal sealed class ExternalModelHost
{
    private readonly ITestHarnessReporter _reporter;
    private readonly IExternalModelRenderStage _renderStage;

    public ExternalModelHost(
        ITestHarnessReporter reporter,
        IExternalModelRenderStage? renderStage = null)
    {
        _reporter = reporter ?? throw new ArgumentNullException(nameof(reporter));
        _renderStage = renderStage ?? new NotRenderedStage();
    }

    /// <summary>
    /// Argument-level handling: exact-one argument value, readable request file, then
    /// request execution. Returned codes are process exit codes.
    /// </summary>
    public static ExternalModelHostResult RunArguments(
        string[] args,
        ITestHarnessReporter reporter,
        IExternalModelRenderStage? renderStage = null)
    {
        const string flag = "--external-model-request";

        var value = default(string);
        var count = 0;
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].Equals(flag, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            count++;
            if (i + 1 >= args.Length)
            {
                reporter.Error($"{flag} requires a request file path.");
                return new ExternalModelHostResult(2, [], false);
            }

            value = args[i + 1];
        }

        if (count == 0)
        {
            reporter.Error($"{flag} requires a request file path.");
            return new ExternalModelHostResult(2, [], false);
        }

        if (count > 1)
        {
            reporter.Error($"{flag} must appear exactly once.");
            return new ExternalModelHostResult(2, [], false);
        }

        string json;
        try
        {
            json = File.ReadAllText(value!);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            reporter.Error($"Request file '{value}' could not be read: {exception.Message}");
            return new ExternalModelHostResult(
                (int)ExternalModelHostResult.ExitRequestFileUnreadable,
                [],
                false);
        }

        return new ExternalModelHost(reporter, renderStage).RunJson(json);
    }

    public ExternalModelHostResult RunJson(string requestJson)
    {
        ExternalModelRequest request;
        try
        {
            request = ExternalModelProtocolJson.Deserialize(requestJson);
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or InvalidDataException)
        {
            var diagnostic = ExternalModelDiagnosticMapper.Map(
                ExternalDiagnosticStage.Selection,
                ExternalDiagnosticSourceRole.Model,
                null,
                new InvalidDataException($"Request could not be parsed: {exception.Message}"));
            _reporter.Error(diagnostic.Message);
            return new ExternalModelHostResult(
                (int)ExternalModelHostResult.ExitRequestInvalid,
                [diagnostic],
                false);
        }

        var validation = ExternalModelRequestValidator.Validate(request);
        if (validation.Count > 0)
        {
            foreach (var diagnostic in validation)
            {
                _reporter.Error($"[{diagnostic.Code}] {diagnostic.Message}");
            }

            TryWriteFailureReport(request, validation);
            return new ExternalModelHostResult(
                (int)ExternalModelHostResult.ExitRequestInvalid,
                validation,
                ReportWasWritten(request));
        }

        return RunRequest(request);
    }

    /// <summary>
    /// Executes one validated request end to end. The game installation is resolved only
    /// after validation.
    /// </summary>
    public ExternalModelHostResult RunRequest(ExternalModelRequest request)
    {
        var validation = ExternalModelRequestValidator.Validate(request);
        if (validation.Count > 0)
        {
            foreach (var diagnostic in validation)
            {
                _reporter.Error($"[{diagnostic.Code}] {diagnostic.Message}");
            }

            TryWriteFailureReport(request, validation);
            return new ExternalModelHostResult(
                (int)ExternalModelHostResult.ExitRequestInvalid,
                validation,
                ReportWasWritten(request));
        }

        var diagnostics = new List<ExternalModelDiagnostic>();

        var config = new TestHarnessConfig();
        var definition = GameDefinition.FromGame(config.Game);
        var installation = GameInstallation.FindAll(new[] { definition }).FirstOrDefault();
        if (installation is null)
        {
            diagnostics.Add(Error(
                "EXT020",
                $"Could not find an installation of {definition.DisplayName}; set {definition.Identifier.ToUpper()}_PATH."));
            foreach (var diagnostic in diagnostics)
            {
                _reporter.Error(diagnostic.Message);
            }

            return new ExternalModelHostResult(
                (int)ExternalModelHostResult.ExitOperationFailed,
                diagnostics,
                TryWriteFailureReport(request, diagnostics));
        }

        var gltfOptions = new GltfImportOptions(unitsPerMeter: request.ImportOptions.UnitsPerMeter);

        using var modelMount = CreateMount(request.ModelSource, gltfOptions);
        using var animationMount = request.AnimationSource is { } animationSource
            ? CreateMount(animationSource, gltfOptions)
            : null;

        ImportedModel? importedModel = null;
        ImportedAnimation? importedAnimation = null;
        Model? runtimeModel = null;
        ModelAnimation? runtimeAnimation = null;
        AssetLoadContext? externalLoadContext = null;
        Game? game = null;

        try
        {
            try
            {
                game = new Game(
                    installation,
                    config.Renderer,
                    new Configuration { NoAudio = true },
                    null);

                if (!ExternalFormatFactory.TryCreateModelImporter(
                        request.ModelSource.FormatId,
                        gltfOptions,
                        diagnostics: null,
                        out var modelImporter) ||
                    modelImporter is null)
                {
                    throw new InvalidDataException(
                        $"Model format '{request.ModelSource.FormatId}' is not supported.");
                }

                importedModel = modelImporter.ImportModel(
                    request.ModelSource.VirtualKey,
                    modelMount.ImportContext);
                if (importedModel is null)
                {
                    // A missing root document is a selection failure, not a parse failure.
                    diagnostics.Add(Error(
                        "EXT002",
                        $"Model document '{request.ModelSource.VirtualKey}' was not found in its mount."));
                    return Failure(request, diagnostics);
                }

                var animationKey = ResolveAnimationKey(request);
                if (animationKey is not null)
                {
                    if (!ExternalFormatFactory.TryCreateAnimationImporter(
                            request.AnimationSource?.FormatId ?? request.ModelSource.FormatId,
                            gltfOptions,
                            diagnostics: null,
                            out var animationImporter) ||
                        animationImporter is null)
                    {
                        throw new InvalidDataException(
                            $"Animation format '{request.AnimationSource?.FormatId}' is not supported.");
                    }

                    importedAnimation = animationImporter.ImportAnimation(
                        animationKey,
                        (animationMount ?? modelMount).ImportContext);
                    if (importedAnimation is null)
                    {
                        diagnostics.Add(Error(
                            "EXT002",
                            $"Animation '{animationKey}' was not found in its mount."));
                        return Failure(request, diagnostics);
                    }
                }
            }
            catch (Exception exception)
            {
                diagnostics.Add(ExternalModelDiagnosticMapper.Map(
                    ExternalDiagnosticStage.Parse,
                    ExternalDiagnosticSourceRole.Model,
                    null,
                    exception));
                return Failure(request, diagnostics);
            }

            try
            {
                var hostLoadContext = game!.AssetStore.LoadContext;
                // The model mount is the materializer's only filesystem: relative C3 image
                // paths resolve inside the external root and can never reach or shadow the
                // installation. Engine-owned services still come from the game host.
                externalLoadContext = new AssetLoadContext(
                    modelMount.ImportContext.FileSystem,
                    hostLoadContext.Language,
                    hostLoadContext.GraphicsDevice,
                    hostLoadContext.StandardGraphicsResources,
                    hostLoadContext.ShaderResources,
                    hostLoadContext.ShaderSetStore,
                    hostLoadContext.AssetStore);

                runtimeModel = ImportedModelMaterializer.MaterializeModel(importedModel, externalLoadContext);

                if (importedAnimation is not null)
                {
                    runtimeAnimation = ImportedAnimationMaterializer.Materialize(importedAnimation);
                }

                // Bind once through C2 to validate the pairing against the hierarchy.
                using (var instance = runtimeModel.CreateInstance(externalLoadContext))
                {
                    if (runtimeAnimation is not null)
                    {
                        var animationInstance = instance.CreateAnimationInstance(
                            runtimeAnimation,
                            AnimationMode.Once,
                            AnimationFlags.StartFrameFirst,
                            gameObject: null!,
                            random: new ConstantRandom());
                        animationInstance.Play();
                    }
                }
            }
            catch (Exception exception)
            {
                diagnostics.Add(ExternalModelDiagnosticMapper.Map(
                    ExternalDiagnosticStage.Materialize,
                    ExternalDiagnosticSourceRole.Model,
                    null,
                    exception));
                return Failure(request, diagnostics);
            }

            ExternalModelRenderOutcome outcome;
            try
            {
                outcome = _renderStage.Render(new ExternalModelRenderInputs(
                    game!,
                    runtimeModel,
                    externalLoadContext!,
                    runtimeAnimation,
                    request));
            }
            catch (Exception exception)
            {
                diagnostics.Add(ExternalModelDiagnosticMapper.Map(
                    ExternalDiagnosticStage.Render,
                    ExternalDiagnosticSourceRole.Model,
                    null,
                    exception));
                return Failure(request, diagnostics);
            }

            diagnostics.AddRange(outcome.Diagnostics);
            if (!outcome.Succeeded)
            {
                return Failure(request, diagnostics);
            }

            var report = BuildReport(request, importedModel, importedAnimation, diagnostics);
            WriteReport(request.OutputReportPath, report);
            foreach (var diagnostic in diagnostics)
            {
                _reporter.Info($"[{diagnostic.Code}] {diagnostic.Message}");
            }

            _reporter.Info($"External model report written to {request.OutputReportPath}.");
            return new ExternalModelHostResult(
                (int)ExternalModelHostResult.ExitOk,
                ExternalModelDiagnosticMapper.SortAndCollapse(diagnostics),
                true);
        }
        finally
        {
            // Reverse ownership order.
            runtimeAnimation?.Dispose();
            runtimeModel?.Dispose();
            importedAnimation = null;
            importedModel = null;
            game?.Dispose();
        }
    }

    internal static ExternalAssetSourceMount CreateMount(
        ExternalModelSourceRequest source,
        GltfImportOptions options)
    {
        var absoluteFile = Path.Combine(source.AbsoluteRoot, source.VirtualKey);
        return new ExternalAssetSourceMount(absoluteFile, options);
    }

    internal static string? ResolveAnimationKey(ExternalModelRequest request)
    {
        if (request.PreviewOptions.SelectedAnimationKey is { } key)
        {
            return key;
        }

        if (request.AnimationSource is { } source)
        {
            return $"{source.VirtualKey}#animation=0";
        }

        return null;
    }

    private ExternalModelHostResult Failure(
        ExternalModelRequest request,
        List<ExternalModelDiagnostic> diagnostics)
    {
        foreach (var diagnostic in diagnostics)
        {
            _reporter.Error($"[{diagnostic.Code}] {diagnostic.Message}");
        }

        var reportWritten = TryWriteFailureReport(request, diagnostics);
        return new ExternalModelHostResult(
            (int)ExternalModelHostResult.ExitOperationFailed,
            ExternalModelDiagnosticMapper.SortAndCollapse(diagnostics),
            reportWritten);
    }

    private static ExternalModelReport BuildReport(
        ExternalModelRequest request,
        ImportedModel importedModel,
        ImportedAnimation? importedAnimation,
        IReadOnlyList<ExternalModelDiagnostic> diagnostics)
    {
        return new ExternalModelReport(
            ReadIdentity(request.ModelSource),
            request.AnimationSource is { } source ? ReadIdentity(source) : null,
            catalog: null,
            diagnostics,
            PreviewEligibility.Evaluate(diagnostics),
            animationPairing: BuildPairing(request, importedAnimation),
            importOptions: request.ImportOptions,
            previewOptions: request.PreviewOptions);
    }

    private static ExternalAnimationPairing? BuildPairing(
        ExternalModelRequest request,
        ImportedAnimation? importedAnimation)
    {
        if (request.PreviewOptions.SelectedAnimationKey is not { } key)
        {
            return null;
        }

        var document = request.AnimationSource is not null
            ? ExternalAnimationSourceDocument.SeparateDocument
            : ExternalAnimationSourceDocument.SameDocument;

        var sourceName = document == ExternalAnimationSourceDocument.SeparateDocument
            ? request.AnimationSource!.VirtualKey
            : request.ModelSource.VirtualKey;

        var descriptor = new ExternalAnimationDescriptor(
            key,
            importedAnimation?.Name ?? "",
            importedAnimation?.Duration ?? TimeSpan.Zero,
            importedAnimation?.Channels.Count ?? 0);

        return new ExternalAnimationPairing(document, sourceName, descriptor);
    }

    private static ExternalSourceIdentity ReadIdentity(ExternalModelSourceRequest source)
    {
        var path = Path.Combine(source.AbsoluteRoot, source.VirtualKey);
        using var stream = File.OpenRead(path);
        return ExternalSourceIdentity.Create(
            source.VirtualKey,
            source.VirtualKey,
            stream);
    }

    private bool TryWriteFailureReport(
        ExternalModelRequest request,
        IReadOnlyList<ExternalModelDiagnostic> diagnostics)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.OutputReportPath) ||
                !Path.IsPathRooted(request.OutputReportPath))
            {
                return false;
            }

            var parent = Path.GetDirectoryName(Path.GetFullPath(request.OutputReportPath));
            if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent))
            {
                return false;
            }

            var report = new ExternalModelReport(
                modelSource: null,
                animationSource: null,
                catalog: null,
                diagnostics,
                PreviewEligibility.Evaluate(diagnostics),
                importOptions: null,
                previewOptions: null);
            WriteReport(request.OutputReportPath, report);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _reporter.Error($"Failure report could not be written: {exception.Message}");
            return false;
        }
    }

    private static void WriteReport(string path, ExternalModelReport report)
    {
        File.WriteAllText(path, ExternalModelReportJson.Serialize(report));
    }

    private static bool ReportWasWritten(ExternalModelRequest request)
    {
        return File.Exists(request.OutputReportPath);
    }

    private static ExternalModelDiagnostic Error(string code, string message)
    {
        return new ExternalModelDiagnostic(
            code,
            ExternalDiagnosticSeverity.Error,
            ExternalDiagnosticStage.Selection,
            ExternalDiagnosticSourceRole.Model,
            null,
            message);
    }

    private sealed class NotRenderedStage : IExternalModelRenderStage
    {
        public ExternalModelRenderOutcome Render(ExternalModelRenderInputs inputs)
        {
            return new ExternalModelRenderOutcome(
                true,
                [
                    new ExternalModelDiagnostic(
                        "EXT300",
                        ExternalDiagnosticSeverity.Info,
                        ExternalDiagnosticStage.Render,
                        ExternalDiagnosticSourceRole.Model,
                        null,
                        "Render stage not implemented; request completed in report-only form."),
                ],
                PngByteLength: null);
        }
    }

    private sealed class ConstantRandom : IRandom
    {
        public uint Seed => 0;

        public void Initialize(uint seed)
        {
        }

        public int Next(int lo, int hi) => lo;

        public float NextSingle(float lo, float hi) => lo;
    }
}
