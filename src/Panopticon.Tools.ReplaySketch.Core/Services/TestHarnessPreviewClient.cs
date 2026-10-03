#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Panopticon.Tools.ReplaySketch.Model;

namespace Panopticon.Tools.ReplaySketch.Services;

/// <summary>
/// Preview client that runs one request through a TestHarness process: locates the built
/// executable through the sibling-build convention, serializes the request next to the
/// artifact outputs, launches external-model mode directly (never <c>dotnet run</c> or a
/// shell), and validates exit code, report validity, generation echo, and the requested
/// PNG. Cancellation terminates exactly the owned process and awaits exit and stderr drain.
/// </summary>
public sealed class TestHarnessPreviewClient : IExternalModelPreviewClient
{
    public const string MissingExecutableCode = "EXT200";
    public const string StartFailedCode = "EXT201";
    public const string NonZeroExitCode = "EXT210";
    public const string MissingReportCode = "EXT211";
    public const string MalformedReportCode = "EXT212";
    public const string GenerationMismatchCode = "EXT213";
    public const string MissingPngCode = "EXT214";

    private readonly Func<string?> _locateExecutable;
    private readonly IExternalModelProcessFactory _processFactory;

    internal TestHarnessPreviewClient(
        Func<string?>? locateExecutable,
        IExternalModelProcessFactory? processFactory)
    {
        var locator = new SiblingBuildExecutableLocator();
        _locateExecutable = locateExecutable
            ?? (() => locator.Locate("Panopticon.Tools.TestHarness"));
        _processFactory = processFactory ?? new SystemExternalModelProcessFactory();
    }

    public TestHarnessPreviewClient()
        : this(null, null)
    {
    }

    public async Task<ExternalModelPreviewResult> ExecuteAsync(
        ExternalModelRequest request,
        IExternalModelArtifactLease lease,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        ArgumentNullException.ThrowIfNull(lease);

        var executablePath = _locateExecutable();
        if (executablePath is null)
        {
            return Failure(MissingExecutableCode,
                "The TestHarness executable was not found in a sibling build output.",
                null);
        }

        var artifactDirectory = Path.GetDirectoryName(Path.GetFullPath(request.OutputReportPath))
            ?? throw new InvalidOperationException("Request report path has no directory.");
        var requestPath = Path.Combine(artifactDirectory, $"request-{lease.ArtifactId}.json");
        File.WriteAllText(requestPath, ExternalModelProtocolJson.Serialize(request));

        IExternalModelProcess? process = null;
        try
        {
            try
            {
                process = _processFactory.Start(executablePath, $"--external-model-request {Quote(requestPath)}");
            }
            catch (Exception exception)
            {
                return Failure(StartFailedCode,
                    $"The TestHarness process could not be started: {exception.Message}",
                    exception);
            }

            using var cancellationRegistration = cancellationToken.Register(
                () => process.KillTree());

            Task<string> stderrTask;
            Task<int> exitTask;
            try
            {
                stderrTask = process.ReadStandardErrorAsync(CancellationToken.None);
                exitTask = process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                await DrainAfterCancellationAsync(process).ConfigureAwait(false);
                throw;
            }

            int exitCode;
            try
            {
                exitCode = await exitTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // The cancellation registration already terminated the owned process;
                // await its complete exit and the stderr drain before propagating.
                await Task.WhenAll(
                    process.WaitForExitAsync(CancellationToken.None),
                    SafeDrainAsync(process)).ConfigureAwait(false);
                throw;
            }

            var stderr = await stderrTask.ConfigureAwait(false);

            if (exitCode != 0)
            {
                return Failure(NonZeroExitCode,
                    $"The TestHarness process exited with code {exitCode}.",
                    null,
                    stderr);
            }

            if (!File.Exists(request.OutputReportPath))
            {
                return Failure(MissingReportCode,
                    "The TestHarness process did not produce a report.",
                    null,
                    stderr);
            }

            JsonDocument document;
            try
            {
                document = ExternalModelReportJson.Parse(
                    await File.ReadAllTextAsync(request.OutputReportPath, CancellationToken.None).ConfigureAwait(false));
            }
            catch (Exception exception) when (exception is JsonException or InvalidDataException)
            {
                return Failure(MalformedReportCode,
                    "The produced report is not a valid external model report.",
                    exception,
                    stderr);
            }

            using (document)
            {
                var root = document.RootElement;
                if (root.TryGetProperty("requestGeneration", out var generationProperty) &&
                    generationProperty.ValueKind == JsonValueKind.Number &&
                    generationProperty.GetInt32() != request.Generation)
                {
                    return Failure(GenerationMismatchCode,
                        $"The produced report answers request generation " +
                        $"{generationProperty.GetInt32()} instead of {request.Generation}.",
                        null,
                        stderr);
                }

                if (request.OutputImagePath is { } imagePath && !File.Exists(imagePath))
                {
                    return Failure(MissingPngCode,
                        "The TestHarness process did not produce the requested preview image.",
                        null,
                        stderr);
                }
            }

            return new ExternalModelPreviewResult(true, []);
        }
        finally
        {
            process?.Dispose();

            try
            {
                File.Delete(requestPath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static async Task DrainAfterCancellationAsync(IExternalModelProcess process)
    {
        process.KillTree();
        await Task.WhenAll(
            process.WaitForExitAsync(CancellationToken.None),
            SafeDrainAsync(process)).ConfigureAwait(false);
    }

    private static async Task SafeDrainAsync(IExternalModelProcess process)
    {
        try
        {
            await process.ReadStandardErrorAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }
    }

    private static ExternalModelPreviewResult Failure(
        string code,
        string message,
        Exception? exception,
        string? standardError = null)
    {
        var detail = exception is not null
            ? new InvalidOperationException(message, exception)
            : new InvalidOperationException(
                string.IsNullOrEmpty(standardError) ? message : $"{message}\n{standardError}");

        // Client-owned failures carry their own codes; only their local detail is an
        // exception, never the code source.
        return new ExternalModelPreviewResult(
            false,
            [
                new ExternalModelDiagnostic(
                    code,
                    ExternalDiagnosticSeverity.Error,
                    ExternalDiagnosticStage.Render,
                    ExternalDiagnosticSourceRole.Model,
                    null,
                    message,
                    exceptionDetail: detail),
            ]);
    }

    private static string Quote(string path)
    {
        return path.Contains(' ', StringComparison.Ordinal)
            ? $"\"{path}\""
            : path;
    }
}
