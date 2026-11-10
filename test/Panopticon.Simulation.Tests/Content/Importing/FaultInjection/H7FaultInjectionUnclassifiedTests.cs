#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Panopticon.Content.Importing.Units;
using Panopticon.Content.Importing.Units.Packages;
using Panopticon.Tests.Content.Importing.Units;
using Panopticon.Tools.H5Migrate;
using Xunit;

namespace Panopticon.Tests.Content.Importing.FaultInjection;

/// <summary>
/// Valued-unclassified fidelity catch proof for unit packages (WI-H7-04). The direct comparer covers the
/// synthetic scout, whose unclassified fields the Generals INI adapter never produces. The CLI cannot
/// reach that source, so its leg adds an unclassified field to the Generals roster instead.
/// </summary>
[Collection("SerialGameTests")]
public sealed class H7FaultInjectionUnclassifiedTests
{
    private const string TypedMismatchCode = "H5-CLI-006";

    [Fact]
    public void Control_UnmodifiedValuedUnclassifiedFields_PassComparer()
    {
        H7FaultInjectionFixtures.WithTempRoot(tempRoot =>
        {
            var packageDir = WriteScout(tempRoot, out var resolution);

            var report = Validate(resolution, packageDir);
            Assert.True(
                report.Passed,
                $"Unmodified valued-unclassified package failed the comparer: {string.Join("; ", report.Sections.SelectMany(s => s.Findings).Select(f => f.Message))}");
        });
    }

    [Fact]
    public void ChangedRawValueWithRecomputedDigests_IsCaughtAsNamedUnclassifiedField()
    {
        InjectScout(
            dir => H7FaultInjector.EditUnitRecord(dir, H7UnclassifiedScout.UnitName, payload =>
            {
                UnclassifiedNamed(payload, H7UnclassifiedScout.ValueFieldName)["rawValue"] = "99.0";
            }),
            withBlockField: false,
            messageFragment: $"Unclassified field '{H7UnclassifiedScout.ValueFieldName}'");
    }

    [Fact]
    public void ChangedRawBlockWithRecomputedDigests_IsCaughtAsNamedUnclassifiedField()
    {
        InjectScout(
            dir => H7FaultInjector.EditUnitRecord(dir, H7UnclassifiedScout.UnitName, payload =>
            {
                UnclassifiedNamed(payload, H7UnclassifiedScout.BlockFieldName)["rawBlock"] = new JsonArray("line 1", "line 2 changed");
            }),
            withBlockField: true,
            messageFragment: $"Unclassified field '{H7UnclassifiedScout.BlockFieldName}'");
    }

    [Fact]
    public void CliValidate_AddedUnclassifiedField_ReportsTypedFieldMismatchInJson()
    {
        H7FaultInjectionFixtures.WithTempRoot(tempRoot =>
        {
            var conversion = H7FaultInjectionFixtures.ConvertRoster(tempRoot);

            var controlExit = H7FaultInjectionFixtures.RunCli(
                ["validate", "--package", conversion.PackageDir, "--game-path", conversion.InstallRoot, .. H7SyntheticIniRoster.RosterRoots, "--json"],
                out _,
                out var controlStderr);
            Assert.True(controlExit == 0, $"validate failed on the unmodified package: {controlStderr}");

            var injectedDir = Path.Combine(tempRoot, "injected-copy");
            H7FaultInjector.CopyDirectory(conversion.PackageDir, injectedDir);
            H7FaultInjector.EditUnitRecord(injectedDir, H7SyntheticIniRoster.InfantryName, payload =>
            {
                payload["unclassified"] = new JsonArray(new JsonObject
                {
                    ["name"] = H7UnclassifiedScout.ValueFieldName,
                    ["affectsGameplay"] = false,
                    ["rawValue"] = "7",
                });
            });
            H7FaultInjector.AssertUnitDigestsHold(injectedDir);

            var exitCode = H7FaultInjectionFixtures.RunCli(
                ["validate", "--package", injectedDir, "--game-path", conversion.InstallRoot, .. H7SyntheticIniRoster.RosterRoots, "--json"],
                out var stdout,
                out _);

            Assert.Equal(1, exitCode);
            H7FaultInjectionAssert.AssertJsonNamedFinding(stdout, TypedMismatchCode, nameof(RoundTripFailureClass.TypedFieldMismatch));
        });
    }

    private static void InjectScout(Action<string> mutate, bool withBlockField, string messageFragment)
    {
        H7FaultInjectionFixtures.WithTempRoot(tempRoot =>
        {
            var packageDir = WriteScout(tempRoot, out var resolution, withBlockField);

            var original = Validate(resolution, packageDir);
            Assert.True(original.Passed, "The unmodified valued-unclassified package failed the comparer.");

            var injectedDir = Path.Combine(tempRoot, "injected-copy");
            H7FaultInjector.CopyDirectory(packageDir, injectedDir);
            mutate(injectedDir);
            H7FaultInjector.AssertUnitDigestsHold(injectedDir);

            var report = Validate(resolution, injectedDir);
            H7FaultInjectionAssert.AssertNamedFinding(report, TypedMismatchCode, RoundTripFailureClass.TypedFieldMismatch, messageFragment);
        });
    }

    private static string WriteScout(string tempRoot, out UnitImportResolution resolution, bool withBlockField = true)
    {
        var packageDir = Path.Combine(tempRoot, "scout-package");
        (resolution, _) = H7UnclassifiedScout.Resolve(policyExcluded: true, withBlockField);

        var write = H4UnitPackageWriter.Write(packageDir, resolution);
        Assert.True(write.Success, string.Join("; ", write.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        return packageDir;
    }

    private static RoundTripReport Validate(UnitImportResolution resolution, string packageDir)
    {
        return RoundTripValidator.ValidateUnits(resolution, null, new H7UnclassifiedScout.EmptyRetainedPayloadProvider(), packageDir);
    }

    private static JsonObject UnclassifiedNamed(JsonObject payload, string name)
    {
        return payload["unclassified"]!.AsArray()
            .Select(node => node!.AsObject())
            .Single(field => field["name"]!.GetValue<string>() == name);
    }
}
