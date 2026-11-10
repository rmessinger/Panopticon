#nullable enable

using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Panopticon.Content.Importing.Units.Packages;

/// <summary>Parses and validates a unit-package manifest from JSON.</summary>
public static class UnitPackageManifestReader
{
    private static readonly HashSet<string> KnownTopLevelMembers = new(StringComparer.OrdinalIgnoreCase)
    {
        "containerKind",
        "schemaVersion",
        "source",
        "roots",
        "rootRecords",
        "records",
        "retainedPayloads",
        "provenance",
        "overrides",
    };

    public static (UnitPackageManifest? Manifest, IReadOnlyList<ImportDiagnostic> Diagnostics) ParseAndValidate(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        var diagnostics = new List<ImportDiagnostic>();

        JsonDocument doc;

        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            diagnostics.Add(new ImportDiagnostic(
                ImportDiagnosticSeverity.Error,
                ImportStage.Read,
                UnitPackageDiagnosticCodes.PayloadInvalid,
                $"Manifest JSON parsing failed: {ex.Message}"));
            return (null, diagnostics);
        }

        using (doc)
        {
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                diagnostics.Add(new ImportDiagnostic(
                    ImportDiagnosticSeverity.Error,
                    ImportStage.Read,
                    UnitPackageDiagnosticCodes.PayloadInvalid,
                    "Manifest root must be a JSON object."));
                return (null, diagnostics);
            }

            var unknownMembers = new List<string>();

            foreach (var prop in root.EnumerateObject())
            {
                if (!KnownTopLevelMembers.Contains(prop.Name))
                {
                    unknownMembers.Add(prop.Name);
                    diagnostics.Add(new ImportDiagnostic(
                        ImportDiagnosticSeverity.Warning,
                        ImportStage.Read,
                        UnitPackageDiagnosticCodes.UnknownManifestMember,
                        $"Unknown manifest member '{prop.Name}' was ignored."));
                }
            }

            string containerKind = string.Empty;

            if (root.TryGetProperty("containerKind", out var containerKindProp))
            {
                containerKind = containerKindProp.GetString() ?? string.Empty;
            }

            if (!string.Equals(containerKind, UnitPackageFormat.ContainerKind, StringComparison.Ordinal))
            {
                diagnostics.Add(new ImportDiagnostic(
                    ImportDiagnosticSeverity.Error,
                    ImportStage.Read,
                    UnitPackageDiagnosticCodes.ContainerKind,
                    $"ContainerKind '{containerKind}' is not the expected '{UnitPackageFormat.ContainerKind}'."));
            }

            var schemaVersion = 0;

            if (root.TryGetProperty("schemaVersion", out var schemaVersionProp))
            {
                if (schemaVersionProp.ValueKind == JsonValueKind.Number && schemaVersionProp.TryGetInt32(out var version))
                {
                    schemaVersion = version;
                }
                else
                {
                    diagnostics.Add(new ImportDiagnostic(
                        ImportDiagnosticSeverity.Error,
                        ImportStage.Read,
                        UnitPackageDiagnosticCodes.SchemaVersion,
                        $"SchemaVersion '{schemaVersionProp.GetRawText()}' is not a valid integer."));
                }
            }
            else
            {
                diagnostics.Add(new ImportDiagnostic(
                    ImportDiagnosticSeverity.Error,
                    ImportStage.Read,
                    UnitPackageDiagnosticCodes.SchemaVersion,
                    "Manifest is missing required 'schemaVersion'."));
            }

            if (schemaVersion != UnitPackageFormat.CurrentSchemaVersion)
            {
                diagnostics.Add(new ImportDiagnostic(
                    ImportDiagnosticSeverity.Error,
                    ImportStage.Read,
                    UnitPackageDiagnosticCodes.SchemaVersion,
                    $"SchemaVersion {schemaVersion} is not supported; expected version {UnitPackageFormat.CurrentSchemaVersion}."));
            }

            UnitPackageSourceIdentity? sourceIdentity = null;

            if (root.TryGetProperty("source", out var sourceProp) && sourceProp.ValueKind == JsonValueKind.Object)
            {
                var id = sourceProp.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
                var caseSensitive = sourceProp.TryGetProperty("caseSensitiveNames", out var csProp) && csProp.GetBoolean();

                if (string.IsNullOrWhiteSpace(id))
                {
                    diagnostics.Add(new ImportDiagnostic(
                        ImportDiagnosticSeverity.Error,
                        ImportStage.Read,
                        UnitPackageDiagnosticCodes.PayloadInvalid,
                        "Source identity must declare a non-empty 'id'."));
                }
                else
                {
                    sourceIdentity = new UnitPackageSourceIdentity(id, caseSensitive);
                }
            }
            else
            {
                diagnostics.Add(new ImportDiagnostic(
                    ImportDiagnosticSeverity.Error,
                    ImportStage.Read,
                    UnitPackageDiagnosticCodes.PayloadInvalid,
                    "Manifest is missing required 'source' identity object."));
            }

            var rootsList = new List<UnitPackageRootRecordEntry>();

            if (root.TryGetProperty("roots", out var rootsProp) || root.TryGetProperty("rootRecords", out rootsProp))
            {
                if (rootsProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var elem in rootsProp.EnumerateArray())
                    {
                        if (elem.ValueKind == JsonValueKind.String)
                        {
                            var rootName = elem.GetString();
                            if (!string.IsNullOrWhiteSpace(rootName))
                            {
                                rootsList.Add(new UnitPackageRootRecordEntry(rootName, ContentKind.Unit));
                            }
                        }
                        else if (elem.ValueKind == JsonValueKind.Object)
                        {
                            var name = elem.TryGetProperty("name", out var nProp) ? nProp.GetString() : null;
                            var kind = ContentKind.Unit;

                            if (elem.TryGetProperty("kind", out var kProp))
                            {
                                if (StrictEnumParser.TryParse<ContentKind>(kProp, out var parsedKind, out var kindError))
                                {
                                    kind = parsedKind.Value;
                                }
                                else
                                {
                                    diagnostics.Add(new ImportDiagnostic(
                                        ImportDiagnosticSeverity.Error,
                                        ImportStage.Read,
                                        UnitPackageDiagnosticCodes.InvalidEnumValue,
                                        $"Root entry kind invalid: {kindError}"));
                                }
                            }

                            if (!string.IsNullOrWhiteSpace(name))
                            {
                                rootsList.Add(new UnitPackageRootRecordEntry(name, kind));
                            }
                        }
                    }
                }
            }

            var recordsList = new List<UnitPackageRecordIndexEntry>();

            if (root.TryGetProperty("records", out var recordsProp))
            {
                if (recordsProp.ValueKind == JsonValueKind.Array)
                {
                    var recordCount = 0;

                    foreach (var elem in recordsProp.EnumerateArray())
                    {
                        recordCount++;

                        if (recordCount > UnitPackageFormat.MaxRecordCount)
                        {
                            diagnostics.Add(new ImportDiagnostic(
                                ImportDiagnosticSeverity.Error,
                                ImportStage.Read,
                                UnitPackageDiagnosticCodes.ResourceLimitExceeded,
                                $"Manifest record count exceeds limit of {UnitPackageFormat.MaxRecordCount}."));
                            break;
                        }

                        if (elem.ValueKind != JsonValueKind.Object)
                        {
                            diagnostics.Add(new ImportDiagnostic(
                                ImportDiagnosticSeverity.Error,
                                ImportStage.Read,
                                UnitPackageDiagnosticCodes.PayloadInvalid,
                                "Manifest record entry must be a JSON object."));
                            continue;
                        }

                        var name = elem.TryGetProperty("name", out var nProp) ? nProp.GetString() : null;
                        var path = elem.TryGetProperty("path", out var pProp) ? pProp.GetString() : null;
                        var sha256 = elem.TryGetProperty("sha256", out var sProp) ? sProp.GetString() : null;
                        var byteLength = elem.TryGetProperty("byteLength", out var bProp) && bProp.TryGetInt64(out var bl) ? bl : -1;

                        string? kindError = null;

                        if (!elem.TryGetProperty("kind", out var kProp) || !StrictEnumParser.TryParse<ContentKind>(kProp, out var kind, out kindError))
                        {
                            diagnostics.Add(new ImportDiagnostic(
                                ImportDiagnosticSeverity.Error,
                                ImportStage.Read,
                                UnitPackageDiagnosticCodes.InvalidEnumValue,
                                $"Record entry '{name ?? "unknown"}' kind invalid: {kindError}"));
                            continue;
                        }

                        if (string.IsNullOrWhiteSpace(name))
                        {
                            diagnostics.Add(new ImportDiagnostic(
                                ImportDiagnosticSeverity.Error,
                                ImportStage.Read,
                                UnitPackageDiagnosticCodes.PayloadInvalid,
                                "Record entry missing name."));
                            continue;
                        }

                        if (!UnitPackagePath.TryValidatePath(path, out var pathReason))
                        {
                            diagnostics.Add(new ImportDiagnostic(
                                ImportDiagnosticSeverity.Error,
                                ImportStage.Read,
                                UnitPackageDiagnosticCodes.PathInvalid,
                                $"Record entry '{name}' path invalid: {pathReason}"));
                        }

                        if (!UnitPackagePath.IsValidSha256(sha256))
                        {
                            diagnostics.Add(new ImportDiagnostic(
                                ImportDiagnosticSeverity.Error,
                                ImportStage.Read,
                                UnitPackageDiagnosticCodes.DigestInvalid,
                                $"Record entry '{name}' SHA-256 '{sha256}' must be 64 lowercase hex characters."));
                        }

                        if (byteLength <= 0)
                        {
                            diagnostics.Add(new ImportDiagnostic(
                                ImportDiagnosticSeverity.Error,
                                ImportStage.Read,
                                UnitPackageDiagnosticCodes.LengthMismatch,
                                $"Record entry '{name}' declared non-positive byteLength {byteLength}."));
                        }

                        recordsList.Add(new UnitPackageRecordIndexEntry(name, kind.Value, path ?? string.Empty, byteLength, sha256 ?? string.Empty));
                    }
                }
            }

            var retainedList = new List<UnitPackageRetainedPayloadEntry>();

            if (root.TryGetProperty("retainedPayloads", out var retainedProp))
            {
                if (retainedProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var elem in retainedProp.EnumerateArray())
                    {
                        if (elem.ValueKind != JsonValueKind.Object)
                        {
                            diagnostics.Add(new ImportDiagnostic(
                                ImportDiagnosticSeverity.Error,
                                ImportStage.Read,
                                UnitPackageDiagnosticCodes.PayloadInvalid,
                                "Manifest retained payload entry must be a JSON object."));
                            continue;
                        }

                        var path = elem.TryGetProperty("path", out var pProp) ? pProp.GetString() : null;
                        var dialect = elem.TryGetProperty("dialect", out var dProp) ? dProp.GetString() : string.Empty;
                        var dialectVersion = elem.TryGetProperty("dialectVersion", out var dvProp) && dvProp.TryGetInt32(out var dv) ? dv : 0;
                        var sha256 = elem.TryGetProperty("sha256", out var sProp) ? sProp.GetString() : null;
                        var byteLength = elem.TryGetProperty("byteLength", out var bProp) && bProp.TryGetInt64(out var bl) ? bl : -1;

                        UnitPackageProvenance? provenance = null;

                        if (elem.TryGetProperty("provenance", out var provProp) && provProp.ValueKind == JsonValueKind.Object)
                        {
                            var srcId = provProp.TryGetProperty("sourceId", out var sidProp) ? sidProp.GetString() : string.Empty;
                            var fPath = provProp.TryGetProperty("filePath", out var fpProp) ? fpProp.GetString() : string.Empty;
                            var line = provProp.TryGetProperty("line", out var lProp) && lProp.TryGetInt32(out var ln) ? ln : 0;
                            var layer = provProp.TryGetProperty("archiveLayer", out var alProp) ? alProp.GetString() : null;
                            provenance = new UnitPackageProvenance(srcId ?? string.Empty, fPath ?? string.Empty, line, layer);
                        }

                        if (!UnitPackagePath.TryValidatePath(path, out var pathReason))
                        {
                            diagnostics.Add(new ImportDiagnostic(
                                ImportDiagnosticSeverity.Error,
                                ImportStage.Read,
                                UnitPackageDiagnosticCodes.PathInvalid,
                                $"Retained payload path '{path}' invalid: {pathReason}"));
                        }

                        if (!UnitPackagePath.IsValidSha256(sha256))
                        {
                            diagnostics.Add(new ImportDiagnostic(
                                ImportDiagnosticSeverity.Error,
                                ImportStage.Read,
                                UnitPackageDiagnosticCodes.DigestInvalid,
                                $"Retained payload SHA-256 '{sha256}' must be 64 lowercase hex characters."));
                        }

                        if (byteLength <= 0)
                        {
                            diagnostics.Add(new ImportDiagnostic(
                                ImportDiagnosticSeverity.Error,
                                ImportStage.Read,
                                UnitPackageDiagnosticCodes.LengthMismatch,
                                $"Retained payload '{path}' declared non-positive byteLength {byteLength}."));
                        }

                        retainedList.Add(new UnitPackageRetainedPayloadEntry(
                            path ?? string.Empty,
                            dialect ?? string.Empty,
                            dialectVersion,
                            byteLength,
                            sha256 ?? string.Empty,
                            provenance));
                    }
                }
            }

            UnitPackageProvenance? pkgProvenance = null;

            if (root.TryGetProperty("provenance", out var pkgProvProp) && pkgProvProp.ValueKind == JsonValueKind.Object)
            {
                var srcId = pkgProvProp.TryGetProperty("sourceId", out var sidProp) ? sidProp.GetString() : string.Empty;
                var fPath = pkgProvProp.TryGetProperty("filePath", out var fpProp) ? fpProp.GetString() : string.Empty;
                var line = pkgProvProp.TryGetProperty("line", out var lProp) && lProp.TryGetInt32(out var ln) ? ln : 0;
                var layer = pkgProvProp.TryGetProperty("archiveLayer", out var alProp) ? alProp.GetString() : null;
                pkgProvenance = new UnitPackageProvenance(srcId ?? string.Empty, fPath ?? string.Empty, line, layer);
            }

            var overridesList = new List<ContentId>();
            var sourceObj = sourceIdentity is not null ? new ContentSource(sourceIdentity.Id, sourceIdentity.CaseSensitiveNames) : ContentSource.Generals;

            if (root.TryGetProperty("overrides", out var overridesProp) && overridesProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var elem in overridesProp.EnumerateArray())
                {
                    if (elem.ValueKind == JsonValueKind.String)
                    {
                        var name = elem.GetString();
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            overridesList.Add(new ContentId(sourceObj, ContentKind.Unit, name));
                        }
                    }
                    else if (elem.ValueKind == JsonValueKind.Object)
                    {
                        var name = elem.TryGetProperty("name", out var nProp) ? nProp.GetString() : null;
                        var kind = ContentKind.Unit;
                        if (elem.TryGetProperty("kind", out var kProp) && StrictEnumParser.TryParse<ContentKind>(kProp, out var parsedKind, out _))
                        {
                            kind = parsedKind.Value;
                        }

                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            overridesList.Add(new ContentId(sourceObj, kind, name));
                        }
                    }
                }
            }

            var manifest = new UnitPackageManifest(
                containerKind,
                schemaVersion,
                sourceIdentity,
                rootsList,
                recordsList,
                retainedList,
                pkgProvenance,
                unknownMembers,
                overridesList);

            return (manifest, diagnostics);
        }
    }
}
