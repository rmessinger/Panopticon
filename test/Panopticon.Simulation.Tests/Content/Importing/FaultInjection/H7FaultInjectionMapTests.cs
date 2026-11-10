#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Panopticon.Content.Importing.Maps;
using Panopticon.Content.Importing.MapSources.Package;
using Panopticon.Tools.H5Migrate;
using Xunit;

namespace Panopticon.Tests.Content.Importing.FaultInjection;

/// <summary>
/// Semantic fault-injection catch proof for map packages (WI-H7-04). Each injection is valid tamper: the
/// copied package keeps its manifest digests, and the H5-03 map comparer reports the named mismatch.
/// </summary>
[Collection("SerialGameTests")]
public sealed class H7FaultInjectionMapTests
{
    private const string TypedMismatchCode = "H5-CLI-006";

    private const string RetainedDigestCode = "H5-CLI-007";

    private const string MissingRecordCode = "H5-CLI-013";

    [Fact]
    public void Control_UnmodifiedMapPackage_PassesComparerAndCliInSameRun()
    {
        H7FaultInjectionFixtures.WithTempRoot(tempRoot =>
        {
            var conversion = H7FaultInjectionFixtures.ConvertMap(tempRoot);

            AssertOriginalPasses(conversion);

            var exitCode = H7FaultInjectionFixtures.RunCli(
                ["validate", "--input", conversion.MapPath, "--package", conversion.PackageDir, "--json"],
                out _,
                out var stderr);
            Assert.True(exitCode == 0, $"validate failed on the unmodified package: {stderr}");
        });
    }

    [Fact]
    public void LegacyPropertyRealChange_WithRecomputedDigests_IsCaughtAsTypedFieldMismatch()
    {
        Inject(
            dir => H7FaultInjector.EditMapManifest(dir, manifest => WithObject(
                manifest,
                H7FaultInjectionFixtures.PrimaryObjectType,
                o => o with { LegacyProperties = WithReal(o.LegacyProperties, "AuthoredElevationOffset", 13.5f) })),
            TypedMismatchCode,
            RoundTripFailureClass.TypedFieldMismatch,
            "property 'AuthoredElevationOffset' differs");
    }

    [Fact]
    public void QuantityFrameChangeWithinValidPairs_IsCaughtAsTypedFieldMismatch()
    {
        Inject(
            dir => H7FaultInjector.EditMapManifest(dir, manifest => WithObject(
                manifest,
                H7FaultInjectionFixtures.PrimaryObjectType,
                o => o with
                {
                    X = o.X with { Frame = nameof(MapQuantityFrame.MapLocalXy) },
                    Y = o.Y with { Frame = nameof(MapQuantityFrame.MapLocalXy) },
                })),
            TypedMismatchCode,
            RoundTripFailureClass.TypedFieldMismatch,
            $", {nameof(MapQuantityFrame.MapLocalXy)})");
    }

    [Fact]
    public void AssetReferenceScopeChangeWithRecomputedDigests_IsCaughtAsTypedFieldMismatch()
    {
        Inject(
            dir => H7FaultInjector.EditMapManifest(dir, manifest => WithObject(
                manifest,
                H7FaultInjectionFixtures.PrimaryObjectType,
                o => o with { Type = o.Type with { Scope = H7FaultInjectionFixtures.TamperedScope } })),
            TypedMismatchCode,
            RoundTripFailureClass.TypedFieldMismatch,
            "type differs");
    }

    [Fact]
    public void RetainedByteFlipWithRecomputedDigest_IsCaughtAsRetainedHashMismatch()
    {
        Inject(
            dir => H7FaultInjector.EditMapRetainedSection(dir, Panopticon.Data.Map.BlendTileData.AssetName, bytes =>
            {
                var edited = (byte[])bytes.Clone();
                edited[edited.Length / 2] ^= 0xFF;
                return edited;
            }),
            RetainedDigestCode,
            RoundTripFailureClass.RetainedHashMismatch,
            Panopticon.Data.Map.BlendTileData.AssetName);
    }

    [Fact]
    public void HeightSampleChangeWithRecomputedDigest_IsCaughtAsTypedFieldMismatch()
    {
        Inject(
            dir => H7FaultInjector.EditMapLayer(dir, MapPackageFormat.LayerKindHeights, bytes =>
            {
                Assert.True(MapPackageChunkCodec.TryDecodeHeights(bytes, out var samples, out _));
                samples![samples.Length / 2] += 1.0f;
                return MapPackageChunkCodec.EncodeHeights(samples);
            }),
            TypedMismatchCode,
            RoundTripFailureClass.TypedFieldMismatch,
            "height sample");
    }

    [Fact]
    public void DroppedObjectWithManifestEntryRemoved_IsCaughtAsMissingRecord()
    {
        Inject(
            dir => H7FaultInjector.EditMapManifest(dir, manifest => manifest with
            {
                Objects = manifest.Objects!.Where(o => o.Type.Name != H7FaultInjectionFixtures.SecondaryObjectType).ToArray(),
            }),
            MissingRecordCode,
            RoundTripFailureClass.MissingRecord,
            "objects count");
    }

    [Fact]
    public void OrientationChangeWithRecomputedDigests_IsCaughtAsTypedFieldMismatch()
    {
        Inject(
            dir => H7FaultInjector.EditMapManifest(dir, manifest => WithObject(
                manifest,
                H7FaultInjectionFixtures.PrimaryObjectType,
                o => o with { OrientationRadians = 1.5 })),
            TypedMismatchCode,
            RoundTripFailureClass.TypedFieldMismatch,
            "orientationRadians 0.75 differs from re-read 1.5");
    }

    [Fact]
    public void CliValidate_QuantityFrameChange_ReportsTypedFieldMismatchInJson()
    {
        H7FaultInjectionFixtures.WithTempRoot(tempRoot =>
        {
            var conversion = H7FaultInjectionFixtures.ConvertMap(tempRoot);
            AssertOriginalPasses(conversion);

            var injectedDir = CopyAndMutate(tempRoot, conversion.PackageDir, dir => H7FaultInjector.EditMapManifest(dir, manifest => WithObject(
                manifest,
                H7FaultInjectionFixtures.PrimaryObjectType,
                o => o with
                {
                    X = o.X with { Frame = nameof(MapQuantityFrame.MapLocalXy) },
                    Y = o.Y with { Frame = nameof(MapQuantityFrame.MapLocalXy) },
                })));

            var exitCode = H7FaultInjectionFixtures.RunCli(
                ["validate", "--input", conversion.MapPath, "--package", injectedDir, "--json"],
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
            var conversion = H7FaultInjectionFixtures.ConvertMap(tempRoot);

            AssertOriginalPasses(conversion);

            var injectedDir = CopyAndMutate(tempRoot, conversion.PackageDir, mutate);
            H7FaultInjector.AssertMapDigestsHold(injectedDir);

            var report = RoundTripValidator.ValidateMap(conversion.SourceMap, conversion.MapPath, injectedDir);
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

    private static void AssertOriginalPasses(H7FaultInjectionFixtures.MapConversion conversion)
    {
        var report = RoundTripValidator.ValidateMap(conversion.SourceMap, conversion.MapPath, conversion.PackageDir);
        Assert.True(
            report.Passed,
            $"Unmodified map package failed the comparer: {string.Join("; ", report.Sections.SelectMany(s => s.Findings).Select(f => f.Message))}");
    }

    private static MapPackageManifest WithObject(
        MapPackageManifest manifest,
        string typeName,
        Func<MapPackagePlacedObject, MapPackagePlacedObject> edit)
    {
        var objects = manifest.Objects!.Select(o => o.Type.Name == typeName ? edit(o) : o).ToArray();
        return manifest with { Objects = objects };
    }

    private static IReadOnlyDictionary<string, MapPackagePropertyValue> WithReal(
        IReadOnlyDictionary<string, MapPackagePropertyValue> properties,
        string key,
        float value)
    {
        var copy = new Dictionary<string, MapPackagePropertyValue>(properties, StringComparer.Ordinal)
        {
            [key] = new MapPackagePropertyValue.RealValue(value),
        };
        return copy;
    }
}
