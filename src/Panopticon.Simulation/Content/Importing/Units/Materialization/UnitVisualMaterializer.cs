#nullable enable

using System.Collections.Generic;
using Panopticon.Graphics;
using Panopticon.Graphics.Animation;
using Panopticon.Logic.Object;
using Panopticon.Mathematics;

namespace Panopticon.Content.Importing.Units.Materialization;

/// <summary>
/// Card WI-H2a-11: binds a typed visual-state record to a native model draw module. The draw data
/// is GPU-free; it names source-qualified models and animations that a visual installer registers
/// at publication. Runtime names carry the source id so they cannot shadow built-in Generals art.
/// </summary>
internal static class UnitVisualMaterializer
{
    public const string DrawTag = "ModuleTag_ImportedDraw";

    private static readonly (string State, ModelConditionFlag? Flag, AnimationMode Mode)[] StateBindings =
    [
        ("Idle", null, AnimationMode.Loop),
        ("Move", ModelConditionFlag.Moving, AnimationMode.Loop),
        ("Attack", ModelConditionFlag.FiringA, AnimationMode.Loop),
        ("Death", ModelConditionFlag.Dying, AnimationMode.Once),
    ];

    public static string RuntimeName(ContentSource source, string sourceKey) => $"{source.Id}:{sourceKey}";

    public static void AddDraw(UnitRecord record, ObjectDefinition definition, UnitImportResolution resolution, UnitMaterializationContext context)
    {
        if (!record.Visual.IsPresent)
        {
            return;
        }

        var visual = resolution.Get<VisualStateRecord>(record.Visual.Value);
        if (visual.Legacy is not null)
        {
            context.Error(record.Id, "Legacy draw data binds through a legacy module, not a visual-state record.");
            return;
        }

        var source = visual.Id.Source;
        var modelName = RuntimeName(source, visual.ModelKey.Value);
        context.VisualAssets.Add(new VisualAssetRequest(visual.Id, VisualAssetKind.Model, visual.ModelKey.Value, modelName));

        var draw = new W3dModelDrawModuleData();
        var defaultState = new ModelConditionState { Model = context.ModelReference(modelName) };
        defaultState.ConditionFlags.Add(new BitArray<ModelConditionFlag>());
        draw.ConditionStates.Add(defaultState);

        foreach (var (stateName, flag, mode) in StateBindings)
        {
            var animationKey = visual.StateAnimations[stateName].Value;
            var animationName = RuntimeName(source, animationKey);
            context.VisualAssets.Add(new VisualAssetRequest(visual.Id, VisualAssetKind.Animation, animationKey, animationName));

            var state = new AnimationState { StateName = stateName.ToUpperInvariant(), IsIdleAnimation = flag is null };
            var flags = new BitArray<ModelConditionFlag>();
            if (flag is { } conditionFlag)
            {
                flags.Set(conditionFlag, true);
            }

            state.ConditionFlags.Add(flags);
            state.Animations.Add(new AnimationStateAnimation
            {
                Animation = context.AnimationReference(animationName),
                AnimationMode = mode,
            });
            draw.AnimationStates.Add(state);
        }

        UnitBodyMovementMaterializer.AddResolved(definition.Draws, new ModuleDataContainer(DrawTag, draw, ModuleInheritanceMode.Default, false));
    }

    /// <summary>The visual states this materializer binds, in binding order.</summary>
    public static IEnumerable<string> BoundStates
    {
        get
        {
            foreach (var binding in StateBindings)
            {
                yield return binding.State;
            }
        }
    }
}
