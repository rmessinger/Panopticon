#nullable enable

using System.Collections.Generic;
using System.Linq;
using Panopticon.Logic.Object;

namespace Panopticon.Content.Importing.Units.Materialization;

/// <summary>
/// Card WI-H2a-07: builds the detached <see cref="ObjectDefinition"/> shell with identity, kinds,
/// geometry, vision, typed body and AI modules, and native locomotors.
/// </summary>
internal static class UnitBodyMovementMaterializer
{
    public const string DefaultBodyTag = "ModuleTag_ImportedBody";

    public static LocomotorTemplate Materialize(LocomotorRecord record, UnitMaterializationContext context)
    {
        var locomotor = new LocomotorTemplate();
        locomotor.AssignImportedName("LocomotorTemplate", record.Id.Name);

        if (record.Speed.IsPresent)
        {
            locomotor.MaxSpeed = (float)context.Convert(record.Id, nameof(record.Speed), record.Speed.Value, context.Policy.ToDistancePerFrame(record.Speed.Value), "distance/frame");
        }

        if (record.TurnRate.IsPresent)
        {
            locomotor.MaxTurnRate = (float)context.Convert(record.Id, nameof(record.TurnRate), record.TurnRate.Value, context.Policy.ToRadiansPerFrame(record.TurnRate.Value), "radians/frame");
        }

        if (record.Acceleration.IsPresent)
        {
            locomotor.Acceleration = (float)context.Convert(record.Id, nameof(record.Acceleration), record.Acceleration.Value, context.Policy.ToDistancePerFrameSquared(record.Acceleration.Value), "distance/frame^2");
        }
        else if (record.Speed.IsPresent)
        {
            // Sources without an acceleration (EE2) reach full speed in one frame; a zero native
            // acceleration would leave the unit unable to move.
            locomotor.Acceleration = locomotor.MaxSpeed;
        }

        if (record.Braking.IsPresent)
        {
            locomotor.Braking = (float)context.Convert(record.Id, nameof(record.Braking), record.Braking.Value, context.Policy.ToDistancePerFrameSquared(record.Braking.Value), "distance/frame^2");
        }

        if (record.Appearance.IsPresent)
        {
            if (EngineEnumNames.TryParse<LocomotorAppearance>(record.Appearance.Value, out var appearance))
            {
                locomotor.Appearance = appearance;
            }
            else
            {
                context.Error(record.Id, $"Unknown locomotor appearance '{record.Appearance.Value}'.");
            }
        }

        foreach (var surfaceName in record.Surfaces.Resolved)
        {
            if (EngineEnumNames.TryParse<Surfaces>(surfaceName, out var surface))
            {
                locomotor.Surfaces |= surface;
            }
            else
            {
                context.Error(record.Id, $"Unknown surface '{surfaceName}'.");
            }
        }

        return locomotor;
    }

    public static ObjectDefinition CreateDefinition(UnitRecord record, UnitMaterializationContext context)
    {
        var definition = new ObjectDefinition(record.Id.Name);

        if (record.DisplayName.IsPresent)
        {
            definition.DisplayName = record.DisplayName.Value;
        }

        foreach (var kindName in record.KindOf.Resolved)
        {
            if (EngineEnumNames.TryParse<ObjectKinds>(kindName, out var kind))
            {
                definition.KindOf.Set(kind, true);
            }
            else
            {
                context.Error(record.Id, $"Unknown object kind '{kindName}'.");
            }
        }

        var shape = definition.Geometry.Shapes[0];
        if (record.GeometryMajorRadius.IsPresent)
        {
            shape.MajorRadius = (float)ConvertDistance(record, nameof(record.GeometryMajorRadius), record.GeometryMajorRadius.Value, context);
            shape.MinorRadius = shape.MajorRadius;
        }

        if (record.GeometryHeight.IsPresent)
        {
            shape.Height = (float)ConvertDistance(record, nameof(record.GeometryHeight), record.GeometryHeight.Value, context);
        }

        if (record.VisionRange.IsPresent)
        {
            definition.VisionRange = (float)ConvertDistance(record, nameof(record.VisionRange), record.VisionRange.Value, context);
            definition.ShroudClearingRange = definition.VisionRange;
        }

        AddTypedModules(record, definition, context);
        AddLocomotors(record, definition, context);

        return definition;
    }

    private static void AddTypedModules(UnitRecord record, ObjectDefinition definition, UnitMaterializationContext context)
    {
        var hasBody = false;

        foreach (var module in record.ResolvedModules.Where(m => m.Ability is null && m.Legacy is null))
        {
            ModuleData? data = module.Family switch
            {
                "ActiveBody" => new ActiveBodyModuleData(),
                "StructureBody" => new StructureBodyModuleData(),
                "ImmortalBody" => new ImmortalBodyModuleData(),
                "HighlanderBody" => new HighlanderBodyModuleData(),
                "UndeadBody" => new UndeadBodyModuleData(),
                "AIUpdateInterface" => new AIUpdateModuleData(),
                "PhysicsBehavior" => new PhysicsBehaviorModuleData(),
                "SlowDeathBehavior" => SlowDeath(record, context),
                _ => null,
            };

            if (data is null)
            {
                context.Error(record.Id, $"Typed module family '{module.Family}' has no materializer.");
                continue;
            }

            if (data is ActiveBodyModuleData body)
            {
                ApplyHealth(record, body, context);
                hasBody = true;
            }

            AddResolved(definition.Behaviors, new ModuleDataContainer(module.Tag, data, module.InheritanceMode, module.Inherited));
        }

        if (!hasBody && record.MaxHealth.IsPresent && !record.ResolvedModules.Any(m => (m.Kinds & ModuleKinds.Body) != 0))
        {
            var body = new ActiveBodyModuleData();
            ApplyHealth(record, body, context);
            AddResolved(definition.Behaviors, new ModuleDataContainer(DefaultBodyTag, body, ModuleInheritanceMode.Default, false));
        }
    }

    private static SlowDeathBehaviorModuleData SlowDeath(UnitRecord record, UnitMaterializationContext context)
    {
        var data = new SlowDeathBehaviorModuleData();
        if (record.DeathDuration.IsPresent)
        {
            var frames = context.Policy.ToLogicFrames(record.DeathDuration.Value);
            context.Convert(record.Id, nameof(record.DeathDuration), record.DeathDuration.Value, frames, "logic frames");
            data.DestructionDelay = new LogicFrameSpan(frames);
        }

        return data;
    }

    /// <summary>Overrides health on a body module; also used for legacy bodies so typed values stay authoritative.</summary>
    internal static void ApplyHealth(UnitRecord record, ActiveBodyModuleData body, UnitMaterializationContext context)
    {
        if (record.MaxHealth.IsPresent)
        {
            body.MaxHealth = (float)context.Convert(record.Id, nameof(record.MaxHealth), record.MaxHealth.Value, record.MaxHealth.Value.Value, "hit points");
            body.InitialHealth = body.MaxHealth;
        }

        if (record.InitialHealth.IsPresent)
        {
            body.InitialHealth = (float)context.Convert(record.Id, nameof(record.InitialHealth), record.InitialHealth.Value, record.InitialHealth.Value.Value, "hit points");
        }
    }

    private static void AddLocomotors(UnitRecord record, ObjectDefinition definition, UnitMaterializationContext context)
    {
        var locomotors = record.Locomotors.Resolved;
        if (locomotors.Count == 0)
        {
            return;
        }

        definition.LocomotorSets[LocomotorSetType.Normal] = new LocomotorSetTemplate
        {
            Condition = LocomotorSetType.Normal,
            Locomotors = locomotors.Select(context.Reference<LocomotorTemplate>).ToArray(),
            Speed = 100,
        };
    }

    /// <summary>
    /// Adds an already-resolved module by tag. Inheritance was applied by the shared resolver, so
    /// the native parse-time merge (<c>ObjectDefinition.AddModuleData</c>) must not run again: it
    /// would evict inherited modules of overlapping kind.
    /// </summary>
    internal static void AddResolved(Dictionary<string, ModuleDataContainer> modules, ModuleDataContainer module)
    {
        modules[module.Tag] = module;
    }

    internal static double ConvertDistance(SourceRecord record, string field, Quantity value, UnitMaterializationContext context)
    {
        return context.Convert(record.Id, field, value, context.Policy.ToGameDistance(value), "game distance");
    }
}
