#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Panopticon.Content.Importing.Units.Materialization;

namespace Panopticon.Content.Importing.Units.Packages;

/// <summary>
/// Reads a unit-package directory container into engine-owned records, implementing
/// <see cref="IUnitSourceAdapter"/> with strict versions, verified digests, safe paths,
/// valued unclassified fields, and retained-INI legacy binding re-issue.
/// </summary>
public sealed class H4UnitPackageReader : IUnitSourceAdapter
{
    private readonly string _packageRoot;
    private ContentSource? _source;
    private RetainedIniRebuildResult? _rebuiltNatives;

    public H4UnitPackageReader(string packageRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageRoot);
        _packageRoot = packageRoot;
    }

    public string PackageRoot => _packageRoot;

    public ContentSource Source => _source ??= ProbeSource();

    public ILegacyNativeTable? Natives => _rebuiltNatives?.Natives;

    public UnitContentSet Read(IReadOnlyCollection<string> rootNames)
    {
        ArgumentNullException.ThrowIfNull(rootNames);

        var diagnostics = new List<ImportDiagnostic>();

        if (!Directory.Exists(_packageRoot))
        {
            diagnostics.Add(new ImportDiagnostic(
                ImportDiagnosticSeverity.Error,
                ImportStage.Read,
                UnitPackageDiagnosticCodes.FileMissing,
                $"Package directory '{_packageRoot}' does not exist."));
            return Fail(Source, diagnostics);
        }

        var manifestPath = Path.Combine(_packageRoot, UnitPackageFormat.ManifestFileName);
        var manifestInfo = new FileInfo(manifestPath);

        if (!manifestInfo.Exists)
        {
            diagnostics.Add(new ImportDiagnostic(
                ImportDiagnosticSeverity.Error,
                ImportStage.Read,
                UnitPackageDiagnosticCodes.FileMissing,
                $"Package manifest '{manifestPath}' does not exist."));
            return Fail(Source, diagnostics);
        }

        if (manifestInfo.Length > UnitPackageFormat.MaxManifestBytes)
        {
            diagnostics.Add(new ImportDiagnostic(
                ImportDiagnosticSeverity.Error,
                ImportStage.Read,
                UnitPackageDiagnosticCodes.ResourceLimitExceeded,
                $"Package manifest '{manifestPath}' length {manifestInfo.Length} bytes exceeds limit of {UnitPackageFormat.MaxManifestBytes} bytes."));
            return Fail(Source, diagnostics);
        }

        string manifestJson;

        try
        {
            manifestJson = File.ReadAllText(manifestPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(new ImportDiagnostic(
                ImportDiagnosticSeverity.Error,
                ImportStage.Read,
                UnitPackageDiagnosticCodes.IoFailure,
                $"Reading package manifest '{manifestPath}' failed: {ex.Message}"));
            return Fail(Source, diagnostics);
        }

        var (manifest, manifestDiagnostics) = UnitPackageManifestReader.ParseAndValidate(manifestJson);
        diagnostics.AddRange(manifestDiagnostics);

        if (manifest?.Source is null || diagnostics.Any(d => d.IsError))
        {
            return Fail(Source, diagnostics);
        }

        var source = new ContentSource(manifest.Source.Id, manifest.Source.CaseSensitiveNames);
        _source = source;

        _rebuiltNatives = RetainedIniRebuilder.Rebuild(_packageRoot, manifest.RetainedPayloads, source);
        diagnostics.AddRange(_rebuiltNatives.Diagnostics);

        if (diagnostics.Any(d => d.IsError))
        {
            return Fail(source, diagnostics);
        }

        var records = new List<SourceRecord>(manifest.Records.Count);

        foreach (var recordEntry in manifest.Records)
        {
            if (!UnitPackagePayloadFileReader.TryReadVerifiedBytes(
                _packageRoot,
                recordEntry.Path,
                recordEntry.ByteLength,
                recordEntry.Sha256,
                out var payloadBytes,
                out var readErrorCode,
                out var readErrorMessage))
            {
                diagnostics.Add(new ImportDiagnostic(
                    ImportDiagnosticSeverity.Error,
                    ImportStage.Read,
                    readErrorCode,
                    readErrorMessage));
                continue;
            }

            var record = UnitPackageRecordReader.ReadRecord(
                payloadBytes,
                recordEntry.Name,
                recordEntry.Kind,
                source,
                _rebuiltNatives,
                diagnostics);

            if (record is not null)
            {
                records.Add(record);
            }
        }

        if (diagnostics.Any(d => d.IsError))
        {
            return Fail(source, diagnostics);
        }

        var roots = new List<ContentId>();

        if (rootNames.Count > 0)
        {
            foreach (var name in rootNames)
            {
                var matched = records.FirstOrDefault(r => r.Id.Kind == ContentKind.Unit && source.NameComparer.Equals(r.Id.Name, name))
                    ?? records.FirstOrDefault(r => source.NameComparer.Equals(r.Id.Name, name));

                if (matched is not null)
                {
                    roots.Add(matched.Id);
                }
                else
                {
                    diagnostics.Add(new ImportDiagnostic(
                        ImportDiagnosticSeverity.Error,
                        ImportStage.Read,
                        DiagnosticCodes.MissingRoot,
                        $"Requested root '{name}' was not found in package."));
                }
            }
        }
        else if (manifest.Roots.Count > 0)
        {
            foreach (var rootEntry in manifest.Roots)
            {
                var matched = records.FirstOrDefault(r => r.Id.Kind == rootEntry.Kind && source.NameComparer.Equals(r.Id.Name, rootEntry.Name))
                    ?? records.FirstOrDefault(r => source.NameComparer.Equals(r.Id.Name, rootEntry.Name));

                if (matched is not null)
                {
                    roots.Add(matched.Id);
                }
                else
                {
                    diagnostics.Add(new ImportDiagnostic(
                        ImportDiagnosticSeverity.Error,
                        ImportStage.Read,
                        DiagnosticCodes.MissingRoot,
                        $"Manifest root '{rootEntry.Name}' ({rootEntry.Kind}) was not found among records."));
                }
            }
        }
        else
        {
            roots.AddRange(records.Select(r => r.Id));
        }

        if (diagnostics.Any(d => d.IsError))
        {
            return Fail(source, diagnostics);
        }

        return new UnitContentSet(source, records, roots, CapDiagnostics(diagnostics));
    }

    private ContentSource ProbeSource()
    {
        try
        {
            var manifestPath = Path.Combine(_packageRoot, UnitPackageFormat.ManifestFileName);
            var manifestInfo = new FileInfo(manifestPath);

            // Bounded probe: never read a manifest past the format cap; Read reports it typed.
            if (manifestInfo.Exists && manifestInfo.Length > UnitPackageFormat.MaxManifestBytes)
            {
                return new ContentSource("unknown", false);
            }

            if (manifestInfo.Exists)
            {
                var json = File.ReadAllText(manifestPath);
                var (manifest, _) = UnitPackageManifestReader.ParseAndValidate(json);

                if (manifest?.Source is { } src)
                {
                    return new ContentSource(src.Id, src.CaseSensitiveNames);
                }
            }
        }
        catch
        {
            // Fallback when probe fails before Read
        }

        return new ContentSource("unknown", false);
    }

    private static UnitContentSet Fail(ContentSource source, List<ImportDiagnostic> diagnostics)
    {
        return new UnitContentSet(source, records: [], roots: [], diagnostics: CapDiagnostics(diagnostics));
    }

    private static IReadOnlyList<ImportDiagnostic> CapDiagnostics(List<ImportDiagnostic> diagnostics)
    {
        if (diagnostics.Count <= UnitPackageFormat.MaxDiagnosticCount)
        {
            return diagnostics;
        }

        var suppressed = diagnostics.Count - UnitPackageFormat.MaxDiagnosticCount;
        var capped = diagnostics.Take(UnitPackageFormat.MaxDiagnosticCount).ToList();
        capped.Add(new ImportDiagnostic(
            ImportDiagnosticSeverity.Error,
            ImportStage.Read,
            UnitPackageDiagnosticCodes.DiagnosticCapExceeded,
            $"Diagnostic count exceeded limit of {UnitPackageFormat.MaxDiagnosticCount}; {suppressed} additional diagnostics were suppressed."));
        return capped;
    }
}
