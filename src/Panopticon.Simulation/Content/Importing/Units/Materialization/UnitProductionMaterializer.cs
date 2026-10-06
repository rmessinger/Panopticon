#nullable enable

using Panopticon.Logic.Object;

namespace Panopticon.Content.Importing.Units.Materialization;

/// <summary>
/// Card WI-H2a-10: build cost, build time, object prerequisites, and native upgrades. Costs keep
/// their authored single-resource value. Production links and unit upgrade lists need a native
/// command-set mapping, which typed sources do not have yet, so they fail rather than drop silently.
/// </summary>
internal static class UnitProductionMaterializer
{
    public static void Apply(UnitRecord record, ObjectDefinition definition, UnitMaterializationContext context)
    {
        if (record.BuildCost.IsPresent)
        {
            definition.BuildCost = (float)context.Convert(record.Id, nameof(record.BuildCost), record.BuildCost.Value, record.BuildCost.Value.Value, "currency");
        }

        if (record.BuildTime.IsPresent)
        {
            definition.BuildTime = ConvertFrames(record, nameof(record.BuildTime), record.BuildTime.Value, context);
        }

        var prerequisites = record.Prerequisites.Resolved;
        if (prerequisites.Count > 0)
        {
            var native = new ObjectPrerequisites();
            foreach (var prerequisite in prerequisites)
            {
                if (prerequisite.Kind != ContentKind.Unit)
                {
                    context.Error(record.Id, $"Prerequisite '{prerequisite}' is not an object; native object prerequisites cannot hold it.");
                    continue;
                }

                native.Objects.Add([context.Reference<ObjectDefinition>(prerequisite)]);
            }

            definition.Prerequisites = native;
        }

        if (record.Produces.Resolved.Count > 0)
        {
            context.Error(record.Id, "Production links need a native command-set mapping, which typed sources do not provide.");
        }

        if (record.Upgrades.Resolved.Count > 0)
        {
            context.Error(record.Id, "Unit upgrade lists need a native command-set mapping, which typed sources do not provide.");
        }
    }

    public static UpgradeTemplate Materialize(UpgradeRecord record, UnitMaterializationContext context)
    {
        var upgrade = new UpgradeTemplate();
        upgrade.AssignImportedName("Upgrade", record.Id.Name);

        if (record.DisplayName.IsPresent)
        {
            upgrade.DisplayName = record.DisplayName.Value;
        }

        if (record.BuildCost.IsPresent)
        {
            upgrade.BuildCost = (float)context.Convert(record.Id, nameof(record.BuildCost), record.BuildCost.Value, record.BuildCost.Value.Value, "currency");
        }

        if (record.BuildTime.IsPresent)
        {
            upgrade.BuildTime = ConvertFrames(record, nameof(record.BuildTime), record.BuildTime.Value, context);
        }

        if (record.Scope.IsPresent)
        {
            upgrade.Type = string.Equals(record.Scope.Value, "Object", System.StringComparison.OrdinalIgnoreCase) ? UpgradeType.Object : UpgradeType.Player;
        }

        if (record.Prerequisites.Resolved.Count > 0)
        {
            context.Error(record.Id, "Upgrade prerequisites need a native command-set mapping, which typed sources do not provide.");
        }

        return upgrade;
    }

    private static LogicFrameSpan ConvertFrames(SourceRecord record, string field, Quantity value, UnitMaterializationContext context)
    {
        var frames = context.Policy.ToLogicFrames(value);
        context.Convert(record.Id, field, value, frames, "logic frames");
        return new LogicFrameSpan(frames);
    }
}
