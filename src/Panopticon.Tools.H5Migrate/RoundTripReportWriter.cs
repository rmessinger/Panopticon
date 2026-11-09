#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Panopticon.Tools.H5Migrate;

/// <summary>Renders a round-trip report as JSON (stable field names) or text (one verdict per line).</summary>
public static class RoundTripReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string ToJson(RoundTripReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var model = new
        {
            mode = report.Mode,
            passed = report.Passed,
            sections = report.Sections.Select(section => new
            {
                section = section.Section,
                passed = section.Passed,
                findings = section.Findings.Select(ToFindingModel),
            }),
            records = report.Records.Select(record => new { record = record.Record, passed = record.Passed }),
        };

        return JsonSerializer.Serialize(model, JsonOptions);
    }

    public static IReadOnlyList<string> ToText(RoundTripReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var lines = new List<string>
        {
            $"mode: {report.Mode}",
            $"verdict: {(report.Passed ? "identical" : "mismatch")}",
        };

        foreach (var section in report.Sections)
        {
            lines.Add($"section {section.Section}: {(section.Passed ? "PASS" : "FAIL")}");

            foreach (var finding in section.Findings)
            {
                var failureClass = finding.FailureClass?.ToString() ?? "operational";
                lines.Add($"  {finding.Code} {failureClass}: {finding.Message}");
            }
        }

        foreach (var record in report.Records)
        {
            lines.Add($"record {record.Record}: {(record.Passed ? "PASS" : "FAIL")}");
        }

        return lines;
    }

    private static object ToFindingModel(RoundTripFinding finding)
    {
        return new
        {
            code = finding.Code,
            failureClass = finding.FailureClass?.ToString(),
            section = finding.Section,
            subject = finding.Subject,
            message = finding.Message,
        };
    }
}
