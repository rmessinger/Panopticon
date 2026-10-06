using System.Collections.Generic;
using System.Linq;
using Panopticon.Content.Importing.Units;
using Panopticon.Content.Importing.Units.Materialization;
using Xunit;
using static Panopticon.Tests.Content.Importing.Units.UnitRecordFixtures;

namespace Panopticon.Tests.Content.Importing.Units;

internal static class MaterializationFixtures
{
    public static readonly UnitCapabilityRegistry Registry = new(
        UnitCapabilityRegistry.DefaultTypedModuleFamilies,
        ["SquishCollide"],
        UnitCapabilityRegistry.DefaultAbilityFamilies,
        Source);

    public static UnitConversionPolicy Policy { get; } = new(SageGame.CncGenerals, gameUnitsPerMeter: 10);

    public sealed class LegacyTable(params object[] values) : ILegacyNativeTable
    {
        public ContentSource Owner => Source;

        public object? Get(int handle) => handle < values.Length ? values[handle] : null;
    }

    public static MaterializedUnitContent Materialize(UnitContentSet set, ILegacyNativeTable? legacy = null)
    {
        var resolution = UnitImportResolution.Resolve(set, Registry);
        Assert.True(resolution.Succeeded, string.Join("\n", resolution.Diagnostics.Where(d => d.IsError)));
        return UnitRuntimeMaterializer.Materialize(resolution, new UnitMaterializationContext(Policy, legacyNatives: legacy));
    }

    public static UnitContentSet Set(UnitRecord root, params SourceRecord[] extra)
    {
        return new UnitContentSet(Source, new List<SourceRecord> { root }.Concat(extra), [root.Id]);
    }
}
