#nullable enable

using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Panopticon.Content.Importing.MapSources.Package;

namespace Panopticon.Tools.H5Migrate;

/// <summary>
/// Maps verifier diagnostics to a named failure class, a comparison section, and a stable code.
/// Verifiers emit free-text messages; the phrases matched here are pinned by the validator tests.
/// </summary>
internal static class RoundTripFailureClassifier
{
    private static readonly Regex MapCountPattern = new(@"(\d+) differs from re-read (\d+)", RegexOptions.CultureInvariant);

    private static readonly Regex UnitCountPattern = new(@"expected (\d+), got (\d+)", RegexOptions.CultureInvariant);

    private static readonly Regex QuotedSubjectPattern = new(@"'([^']+)'", RegexOptions.CultureInvariant);

    public static string CodeFor(RoundTripFailureClass failureClass) => failureClass switch
    {
        RoundTripFailureClass.TypedFieldMismatch => H5MigrateCliDiagnostics.TypedMismatch,
        RoundTripFailureClass.RetainedHashMismatch => H5MigrateCliDiagnostics.RetainedDigestMismatch,
        RoundTripFailureClass.MissingSection => H5MigrateCliDiagnostics.SourceSectionGap,
        RoundTripFailureClass.MissingRecord => H5MigrateCliDiagnostics.MissingRecord,
        RoundTripFailureClass.ExtraRecord => H5MigrateCliDiagnostics.ExtraRecord,
        RoundTripFailureClass.MaterializedTemplateMismatch => H5MigrateCliDiagnostics.MaterializedTemplateMismatch,
        _ => throw new System.ArgumentOutOfRangeException(nameof(failureClass), failureClass, null),
    };

    public static RoundTripFinding ForMap(MapPackageDiagnostic diagnostic)
    {
        var (section, failureClass) = ClassifyMap(diagnostic);
        return Build(diagnostic.Code, failureClass, section, diagnostic.Message);
    }

    public static RoundTripFinding ForUnit(UnitPackageConversionDiagnostic diagnostic)
    {
        var (section, failureClass) = ClassifyUnit(diagnostic);
        return Build(diagnostic.Code, failureClass, section, diagnostic.Message);
    }

    public static RoundTripFinding Operational(string code, string section, string message)
    {
        return new RoundTripFinding(code, FailureClass: null, section, Subject(message), message);
    }

    /// <summary>The last quoted token: verifier messages name the record after its field, reference, or path.</summary>
    public static string Subject(string message)
    {
        return QuotedSubjectPattern.Matches(message).Select(match => match.Groups[1].Value).LastOrDefault() ?? string.Empty;
    }

    private static RoundTripFinding Build(string sourceCode, RoundTripFailureClass? failureClass, string section, string message)
    {
        var code = failureClass is { } value ? CodeFor(value) : sourceCode;
        return new RoundTripFinding(code, failureClass, section, Subject(message), message);
    }

    private static (string Section, RoundTripFailureClass? Class) ClassifyMap(MapPackageDiagnostic diagnostic)
    {
        var message = diagnostic.Message;

        if (diagnostic.Code == H5MigrateCliDiagnostics.SourceSectionGap)
        {
            return ("sections", RoundTripFailureClass.MissingSection);
        }

        if (diagnostic.Code == H5MigrateCliDiagnostics.RetainedDigestMismatch)
        {
            return ("retained", RoundTripFailureClass.RetainedHashMismatch);
        }

        var section = MapSection(message);
        var countMatch = MapCountPattern.Match(message);
        var countsRecords = section is "objects" or "waypoints" or "retained";

        if (countMatch.Success && countsRecords)
        {
            var expected = int.Parse(countMatch.Groups[1].Value, CultureInfo.InvariantCulture);
            var actual = int.Parse(countMatch.Groups[2].Value, CultureInfo.InvariantCulture);

            if (actual < expected)
            {
                return (section, section == "retained" ? RoundTripFailureClass.MissingSection : RoundTripFailureClass.MissingRecord);
            }

            if (actual > expected)
            {
                return (section, RoundTripFailureClass.ExtraRecord);
            }
        }

        return (section, RoundTripFailureClass.TypedFieldMismatch);
    }

    private static string MapSection(string message)
    {
        if (message.Contains("identity", System.StringComparison.Ordinal))
        {
            return "identity";
        }

        if (message.Contains("object", System.StringComparison.Ordinal))
        {
            return "objects";
        }

        if (message.Contains("waypoint", System.StringComparison.Ordinal))
        {
            return "waypoints";
        }

        if (message.Contains("blend", System.StringComparison.Ordinal))
        {
            return "blend";
        }

        if (message.Contains("retained section", System.StringComparison.Ordinal))
        {
            return "retained";
        }

        if (message.Contains("height", System.StringComparison.Ordinal))
        {
            return "heights";
        }

        if (message.Contains("imagery", System.StringComparison.Ordinal)
            || message.Contains("presence", System.StringComparison.Ordinal)
            || message.Contains("bytes", System.StringComparison.Ordinal))
        {
            return "layers";
        }

        return "layout";
    }

    private static (string Section, RoundTripFailureClass? Class) ClassifyUnit(UnitPackageConversionDiagnostic diagnostic)
    {
        var message = diagnostic.Message;

        if (diagnostic.Code == H5MigrateCliDiagnostics.RetainedDigestMismatch)
        {
            return ("retained", RoundTripFailureClass.RetainedHashMismatch);
        }

        if (diagnostic.Code == H5MigrateCliDiagnostics.RetainedBlockMissing)
        {
            return ("retained", null);
        }

        if (message.Contains("count mismatch", System.StringComparison.Ordinal))
        {
            var countMatch = UnitCountPattern.Match(message);
            if (countMatch.Success)
            {
                var expected = int.Parse(countMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                var actual = int.Parse(countMatch.Groups[2].Value, CultureInfo.InvariantCulture);
                return ("closure", actual < expected ? RoundTripFailureClass.MissingRecord : RoundTripFailureClass.ExtraRecord);
            }
        }

        if (message.Contains("is missing from reloaded package", System.StringComparison.Ordinal))
        {
            return ("records", RoundTripFailureClass.MissingRecord);
        }

        if (message.Contains("Retained INI file missing", System.StringComparison.Ordinal)
            || message.Contains("Failed to read retained INI file", System.StringComparison.Ordinal))
        {
            return ("retained", RoundTripFailureClass.MissingSection);
        }

        if (message.Contains("Retained block byte mismatch", System.StringComparison.Ordinal))
        {
            return ("retained", RoundTripFailureClass.RetainedHashMismatch);
        }

        if (message.Contains("classified", System.StringComparison.Ordinal))
        {
            return ("unclassified", RoundTripFailureClass.TypedFieldMismatch);
        }

        return ("records", RoundTripFailureClass.TypedFieldMismatch);
    }
}
