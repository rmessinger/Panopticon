#nullable enable

using System;
using System.Collections.Generic;
using System.IO;

namespace Panopticon.Tools.ReplaySketch.Model;

/// <summary>
/// Validates one external model request without opening a game installation or source
/// document. Failures are structured EXT diagnostics at the Selection stage.
/// </summary>
public static class ExternalModelRequestValidator
{
    public const string SchemaVersionCode = "EXT100";
    public const string UnsupportedFormatCode = "EXT001";
    public const string InvalidVirtualKeyCode = "EXT102";
    public const string MissingSourceRootCode = "EXT103";
    public const string InvalidViewportCode = "EXT105";
    public const string InvalidPreviewOptionsCode = "EXT106";
    public const string InvalidOutputCode = "EXT110";
    public const string MissingReportPathCode = "EXT111";

    /// <summary>Returns an empty list when the request is valid.</summary>
    public static IReadOnlyList<ExternalModelDiagnostic> Validate(ExternalModelRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var diagnostics = new List<ExternalModelDiagnostic>();

        if (request.SchemaVersion != ExternalModelRequest.CurrentSchemaVersion)
        {
            diagnostics.Add(Error(
                SchemaVersionCode,
                $"Request schema version {request.SchemaVersion} is not supported; expected {ExternalModelRequest.CurrentSchemaVersion}."));
            return diagnostics;
        }

        ValidateSource(request.ModelSource, "model", diagnostics);
        if (request.AnimationSource is not null)
        {
            ValidateSource(request.AnimationSource, "animation", diagnostics);
        }

        if (request.Viewport.Width <= 0 || request.Viewport.Height <= 0)
        {
            diagnostics.Add(Error(
                InvalidViewportCode,
                $"Viewport dimensions must be positive; got {request.Viewport.Width}x{request.Viewport.Height}."));
        }

        try
        {
            request.PreviewOptions.Validate(request.ImportOptions, selectedAnimationDurationSeconds: null);
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(Error(InvalidPreviewOptionsCode, exception.Message));
        }

        ValidateOutputs(request, diagnostics);

        return diagnostics;
    }

    private static void ValidateSource(
        ExternalModelSourceRequest source,
        string role,
        List<ExternalModelDiagnostic> diagnostics)
    {
        if (!string.Equals(
                source.FormatId,
                Content.Importing.ExternalAssetInspection.GltfFormatId,
                StringComparison.Ordinal))
        {
            diagnostics.Add(Error(
                UnsupportedFormatCode,
                $"{role} source format '{source.FormatId}' is not supported."));
        }

        if (!IsNormalizedRelativeKey(source.VirtualKey))
        {
            diagnostics.Add(Error(
                InvalidVirtualKeyCode,
                $"{role} virtual key '{source.VirtualKey}' must be a normalized relative path."));
        }

        if (string.IsNullOrWhiteSpace(source.AbsoluteRoot) ||
            !Path.IsPathRooted(source.AbsoluteRoot) ||
            !Directory.Exists(source.AbsoluteRoot))
        {
            diagnostics.Add(Error(
                MissingSourceRootCode,
                $"{role} source root '{source.AbsoluteRoot}' must be an existing absolute directory."));
        }
    }

    private static void ValidateOutputs(
        ExternalModelRequest request,
        List<ExternalModelDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(request.OutputReportPath))
        {
            diagnostics.Add(Error(
                MissingReportPathCode,
                "The output report path is required."));
            return;
        }

        string? imageParent = null;

        if (request.OutputImagePath is { } imagePath)
        {
            if (ValidateOutputPath(imagePath, "image", request, diagnostics) is null)
            {
                imageParent = Path.GetFullPath(Path.GetDirectoryName(imagePath)!);
            }
        }

        if (ValidateOutputPath(request.OutputReportPath, "report", request, diagnostics) is null &&
            imageParent is not null)
        {
            var reportParent = Path.GetFullPath(Path.GetDirectoryName(request.OutputReportPath)!);
            if (!string.Equals(imageParent, reportParent, PathComparison))
            {
                diagnostics.Add(Error(
                    InvalidOutputCode,
                    "Image and report outputs must share one canonical parent directory."));
            }
        }

        if (!string.IsNullOrWhiteSpace(request.OutputImagePath) &&
            string.Equals(
                Path.GetFullPath(request.OutputImagePath),
                Path.GetFullPath(request.OutputReportPath),
                PathComparison))
        {
            diagnostics.Add(Error(
                InvalidOutputCode,
                "Image and report outputs must be distinct paths."));
        }
    }

    private static string? ValidateOutputPath(
        string path,
        string role,
        ExternalModelRequest request,
        List<ExternalModelDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path))
        {
            diagnostics.Add(Error(
                InvalidOutputCode,
                $"{role} output path '{path}' must be absolute."));
            return path;
        }

        var fullPath = Path.GetFullPath(path);

        if (Directory.Exists(fullPath))
        {
            diagnostics.Add(Error(
                InvalidOutputCode,
                $"{role} output path '{fullPath}' is an existing directory."));
            return fullPath;
        }

        var parent = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent))
        {
            diagnostics.Add(Error(
                InvalidOutputCode,
                $"{role} output parent directory '{parent}' does not exist."));
            return fullPath;
        }

        foreach (var source in new[] { request.ModelSource, request.AnimationSource })
        {
            if (source is null || string.IsNullOrEmpty(source.VirtualKey))
            {
                continue;
            }

            var sourcePath = Path.GetFullPath(Path.Combine(source.AbsoluteRoot, source.VirtualKey));
            if (string.Equals(sourcePath, fullPath, PathComparison))
            {
                diagnostics.Add(Error(
                    InvalidOutputCode,
                    $"{role} output path '{fullPath}' must not overwrite a source file."));
            }
        }

        return null;
    }

    public static bool IsNormalizedRelativeKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) ||
            key.Contains('\\') ||
            key[0] == '/' ||
            key.Contains(':'))
        {
            return false;
        }

        foreach (var segment in key.Split('/'))
        {
            if (segment.Length == 0 ||
                segment is "." or ".." ||
                string.IsNullOrWhiteSpace(segment))
            {
                return false;
            }
        }

        return true;
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

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
}
