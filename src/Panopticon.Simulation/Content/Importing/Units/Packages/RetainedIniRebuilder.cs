#nullable enable

using System;
using System.Collections.Generic;
using System.Text;
using Panopticon.Content;
using Panopticon.Content.Importing.Units.Generals;
using Panopticon.Data.Ini;

namespace Panopticon.Content.Importing.Units.Packages;

/// <summary>
/// Rebuilds native templates from retained INI blocks and re-issues legacy bindings
/// using the Generals staging and adapter path.
/// </summary>
public static class RetainedIniRebuilder
{
    public static RetainedIniRebuildResult Rebuild(
        string packageRoot,
        IReadOnlyList<UnitPackageRetainedPayloadEntry> entries,
        ContentSource source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageRoot);
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(source);

        var diagnostics = new List<ImportDiagnostic>();

        if (entries.Count == 0)
        {
            return new RetainedIniRebuildResult(null, null, diagnostics);
        }

        var loaded = new List<(UnitPackageRetainedPayloadEntry Entry, byte[] Bytes)>();

        foreach (var entry in entries)
        {
            if (!string.Equals(entry.Dialect, UnitPackageFormat.SupportedGeneralsIniDialect, StringComparison.Ordinal))
            {
                diagnostics.Add(new ImportDiagnostic(
                    ImportDiagnosticSeverity.Error,
                    ImportStage.Read,
                    UnitPackageDiagnosticCodes.UnsupportedDialect,
                    $"Retained payload '{entry.Path}' dialect '{entry.Dialect}' is not supported; expected '{UnitPackageFormat.SupportedGeneralsIniDialect}'."));
                continue;
            }

            if (entry.DialectVersion != UnitPackageFormat.SupportedGeneralsIniDialectVersion)
            {
                diagnostics.Add(new ImportDiagnostic(
                    ImportDiagnosticSeverity.Error,
                    ImportStage.Read,
                    UnitPackageDiagnosticCodes.UnsupportedDialectVersion,
                    $"Retained payload '{entry.Path}' dialect version {entry.DialectVersion} is not supported; expected version {UnitPackageFormat.SupportedGeneralsIniDialectVersion}."));
                continue;
            }

            if (!UnitPackagePayloadFileReader.TryReadVerifiedBytes(
                packageRoot,
                entry.Path,
                entry.ByteLength,
                entry.Sha256,
                out var bytes,
                out var errorCode,
                out var errorMessage))
            {
                diagnostics.Add(new ImportDiagnostic(
                    ImportDiagnosticSeverity.Error,
                    ImportStage.Read,
                    errorCode,
                    errorMessage));
                continue;
            }

            loaded.Add((entry, bytes));
        }

        if (diagnostics.Count > 0)
        {
            return new RetainedIniRebuildResult(null, null, diagnostics);
        }

        var memoryFs = new UnitPackageMemoryFileSystem();

        foreach (var (entry, bytes) in loaded)
        {
            memoryFs.AddFile(entry.Path, bytes);
        }

        var store = new AssetStore(SageGame.CncGenerals, memoryFs, null, null, null, null, null, OnDemandAssetLoadStrategy.None);
        store.PushScope();
        var staging = new UnitContentStaging();
        var dataContext = new IniDataContext();

        foreach (var (entry, _) in loaded)
        {
            var fileEntry = memoryFs.GetFile(entry.Path);

            if (fileEntry is null)
            {
                diagnostics.Add(new ImportDiagnostic(
                    ImportDiagnosticSeverity.Error,
                    ImportStage.Read,
                    UnitPackageDiagnosticCodes.FileMissing,
                    $"Retained payload file '{entry.Path}' was not found in staging."));
                return new RetainedIniRebuildResult(null, null, diagnostics);
            }

            try
            {
                var parser = new IniParser(fileEntry, store, SageGame.CncGenerals, dataContext, Encoding.UTF8, staging);
                parser.ParseFile();
            }
            catch (Exception ex)
            {
                diagnostics.Add(new ImportDiagnostic(
                    ImportDiagnosticSeverity.Error,
                    ImportStage.Read,
                    UnitPackageDiagnosticCodes.PayloadInvalid,
                    $"Parsing retained INI block '{entry.Path}' failed: {ex.Message}"));
                return new RetainedIniRebuildResult(null, null, diagnostics);
            }
        }

        var generalsAdapter = new GeneralsUnitAdapter(staging, SageGame.CncGenerals);
        var set = generalsAdapter.Read([]);

        var rebuiltLookup = new Dictionary<ContentId, SourceRecord>();

        foreach (var record in set.Records)
        {
            // Rebuilt records are keyed by their ContentId
            rebuiltLookup[record.Id] = record;
        }

        return new RetainedIniRebuildResult(rebuiltLookup, generalsAdapter.Natives, diagnostics);
    }
}
