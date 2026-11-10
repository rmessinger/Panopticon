#nullable enable

using System;
using System.Linq;
using System.Text.Json;
using Panopticon.Tools.H5Migrate;
using Xunit;

namespace Panopticon.Tests.Content.Importing.FaultInjection;

internal static class H7FaultInjectionAssert
{
    public static void AssertNamedFinding(
        RoundTripReport report,
        string code,
        RoundTripFailureClass failureClass,
        string messageFragment)
    {
        var findings = report.Sections.SelectMany(section => section.Findings).ToList();

        Assert.False(report.Passed, "The comparer passed a tampered package.");

        if (!findings.Any(f => f.Code == code && f.FailureClass == failureClass && f.Message.Contains(messageFragment, StringComparison.Ordinal)))
        {
            Assert.Fail(
                $"Expected {code} {failureClass} containing '{messageFragment}'. Findings: "
                + string.Join("; ", findings.Select(f => $"{f.Code} {f.FailureClass} {f.Message}")));
        }
    }

    public static void AssertJsonNamedFinding(string json, string code, string failureClass)
    {
        using var document = JsonDocument.Parse(json);

        Assert.False(document.RootElement.GetProperty("passed").GetBoolean());

        var findings = document.RootElement.GetProperty("sections").EnumerateArray()
            .SelectMany(section => section.GetProperty("findings").EnumerateArray())
            .Select(finding => (
                Code: finding.GetProperty("code").GetString(),
                FailureClass: finding.GetProperty("failureClass").GetString()))
            .ToList();

        Assert.Contains(findings, f => f.Code == code && f.FailureClass == failureClass);
    }
}
