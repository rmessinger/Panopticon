#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Panopticon.Tests.Content.Importing.Units;
using Panopticon.Tools.H5Migrate;
using Xunit;

namespace Panopticon.Tests.Content.Importing.FaultInjection;

/// <summary>
/// Semantic fault-injection catch proof for unit packages (WI-H7-04). Each injection is valid tamper: the
/// copied package keeps its manifest digests, and the H5-03 unit comparer reports the named mismatch.
/// </summary>
[Collection("SerialGameTests")]
public sealed class H7FaultInjectionUnitTests
{
    private const string TypedMismatchCode = "H5-CLI-006";

    private const string RetainedDigestCode = "H5-CLI-007";

    private const string MissingRecordCode = "H5-CLI-013";

    [Fact]
    public void Control_UnmodifiedUnitPackage_PassesComparerAndCliInSameRun()
    {
        H7FaultInjectionFixtures.WithTempRoot(tempRoot =>
        {
            var conversion = H7FaultInjectionFixtures.ConvertRoster(tempRoot);

            AssertOriginalPasses(conversion);

            var exitCode = H7FaultInjectionFixtures.RunCli(
                ["validate", "--package", conversion.PackageDir, "--game-path", conversion.InstallRoot, .. H7SyntheticIniRoster.RosterRoots, "--json"],
                out _,
                out var stderr);
            Assert.True(exitCode == 0, $"validate failed on the unmodified package: {stderr}");
        });
    }

    [Fact]
    public void AuthoredValueChangeWithRecomputedDigests_IsCaughtAsTypedFieldMismatch()
    {
        Inject(
            dir => H7FaultInjector.EditUnitRecord(dir, H7SyntheticIniRoster.InfantryName, payload =>
            {
                payload["maxHealth"]!["value"]!["value"] = 200.0;
            }),
            TypedMismatchCode,
            RoundTripFailureClass.TypedFieldMismatch,
            "value mismatch for");
    }

    [Fact]
    public void AuthoredUnitFlipToValidName_IsCaughtAsTypedFieldMismatch()
    {
        Inject(
            dir => H7FaultInjector.EditUnitRecord(dir, H7SyntheticIniRoster.InfantryName, payload =>
            {
                payload["maxHealth"]!["value"]!["unit"] = "Seconds";
            }),
            TypedMismatchCode,
            RoundTripFailureClass.TypedFieldMismatch,
            "value mismatch for");
    }

    [Fact]
    public void ProvenanceLineChangeWithRecomputedDigests_IsCaughtAsTypedFieldMismatch()
    {
        Inject(
            dir => H7FaultInjector.EditUnitRecord(dir, H7SyntheticIniRoster.InfantryName, payload =>
            {
                var provenance = payload["provenance"]!;
                provenance["line"] = provenance["line"]!.GetValue<int>() + 1;
            }),
            TypedMismatchCode,
            RoundTripFailureClass.TypedFieldMismatch,
            "Provenance mismatch for");
    }

    [Fact]
    public void RetainedIniDigitFlipWithRecomputedDigest_IsCaughtAsRetainedHashMismatch()
    {
        Inject(
            dir => H7FaultInjector.EditUnitRetainedPayload(dir, $"{H7SyntheticIniRoster.InfantryName}.ini", bytes =>
            {
                var edited = (byte[])bytes.Clone();
                var index = Array.FindIndex(edited, IsAsciiDigit);
                Assert.True(index >= 0, "The retained INI holds no ASCII digit to flip.");
                edited[index] ^= 0x01;
                return edited;
            }),
            RetainedDigestCode,
            RoundTripFailureClass.RetainedHashMismatch,
            "Retained block");
    }

    [Fact]
    public void DroppedWeaponRecord_IsCaughtAsMissingRecord()
    {
        Inject(
            dir => H7FaultInjector.DropUnitRecord(dir, H7SyntheticIniRoster.RifleName),
            MissingRecordCode,
            RoundTripFailureClass.MissingRecord,
            "is missing from reloaded package");
    }

    [Fact]
    public void CliValidate_AuthoredValueChange_ReportsTypedFieldMismatchInJson()
    {
        H7FaultInjectionFixtures.WithTempRoot(tempRoot =>
        {
            var conversion = H7FaultInjectionFixtures.ConvertRoster(tempRoot);
            AssertOriginalPasses(conversion);

            var injectedDir = CopyAndMutate(tempRoot, conversion.PackageDir, dir => H7FaultInjector.EditUnitRecord(dir, H7SyntheticIniRoster.InfantryName, payload =>
            {
                payload["maxHealth"]!["value"]!["value"] = 200.0;
            }));

            var exitCode = H7FaultInjectionFixtures.RunCli(
                ["validate", "--package", injectedDir, "--game-path", conversion.InstallRoot, .. H7SyntheticIniRoster.RosterRoots, "--json"],
                out var stdout,
                out _);

            Assert.Equal(1, exitCode);
            H7FaultInjectionAssert.AssertJsonNamedFinding(stdout, TypedMismatchCode, nameof(RoundTripFailureClass.TypedFieldMismatch));
        });
    }

    private static void Inject(
        Action<string> mutate,
        string code,
        RoundTripFailureClass failureClass,
        string messageFragment)
    {
        H7FaultInjectionFixtures.WithTempRoot(tempRoot =>
        {
            var conversion = H7FaultInjectionFixtures.ConvertRoster(tempRoot);

            AssertOriginalPasses(conversion);

            var injectedDir = CopyAndMutate(tempRoot, conversion.PackageDir, mutate);
            H7FaultInjector.AssertUnitDigestsHold(injectedDir);

            var report = RoundTripValidator.ValidateUnits(conversion.Resolution, conversion.Natives, conversion.Provider, injectedDir);
            H7FaultInjectionAssert.AssertNamedFinding(report, code, failureClass, messageFragment);
        });
    }

    private static string CopyAndMutate(string tempRoot, string packageDir, Action<string> mutate)
    {
        var injectedDir = Path.Combine(tempRoot, "injected-copy");
        H7FaultInjector.CopyDirectory(packageDir, injectedDir);
        mutate(injectedDir);
        return injectedDir;
    }

    private static void AssertOriginalPasses(H7FaultInjectionFixtures.UnitConversion conversion)
    {
        var report = RoundTripValidator.ValidateUnits(conversion.Resolution, conversion.Natives, conversion.Provider, conversion.PackageDir);
        Assert.True(
            report.Passed,
            $"Unmodified unit package failed the comparer: {string.Join("; ", report.Sections.SelectMany(s => s.Findings).Select(f => f.Message))}");
    }

    private static bool IsAsciiDigit(byte value) => value is >= (byte)'0' and <= (byte)'9';
}
