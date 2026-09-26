using System;
using System.Collections.Generic;
using System.Linq;
using Panopticon.Graphics;

namespace Panopticon.Logic.Object;

/// <summary>
/// Resolves the bone visibility slots of a model's muzzle flashes from INI <c>WeaponMuzzleFlash</c>
/// entries. W3D files do not flag muzzle-flash meshes as hidden, so every renderer must hide them
/// explicitly: <see cref="W3dModelDraw"/> shows them only while <c>FiringA</c> is set, and
/// standalone model renders (no draw module, no firing state) keep them hidden.
/// </summary>
internal static class MuzzleFlashBones
{
    public static List<string> CollectNames(IEnumerable<ModelConditionState> conditionStates)
    {
        return conditionStates
            .SelectMany(cs => cs.WeaponMuzzleFlashes)
            .Select(mf => mf.BoneName)
            .Where(name => !string.IsNullOrEmpty(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Muzzle-flash names declared by every W3D draw condition state that uses the named model.
    /// Matches on the INI model name, so no other model is loaded.
    /// </summary>
    public static List<string> CollectNamesForModel(IEnumerable<ObjectDefinition> objectDefinitions, string modelName)
    {
        if (string.IsNullOrEmpty(modelName))
        {
            return new List<string>();
        }

        var conditionStates = objectDefinitions
            .SelectMany(definition => definition.Draws.Values)
            .Select(container => container.Data)
            .OfType<W3dModelDrawModuleData>()
            .SelectMany(data => data.ConditionStates)
            .Where(cs => string.Equals(cs.Model?.Name, modelName, StringComparison.OrdinalIgnoreCase));

        return CollectNames(conditionStates);
    }

    /// <summary>
    /// A muzzle-flash name is a prefix of either a skeleton bone (vehicles, e.g. AVHUMMER's
    /// MUZZLEFX01 bone) or a sub-object mesh parented to a differently named bone (infantry, e.g.
    /// the Ranger's MUZZLEFX01 mesh on bone MUZZLE01). Both map to a bone visibility index.
    /// </summary>
    public static int[] ResolveBoneIndices(Model model, IReadOnlyList<string> prefixes)
    {
        if (prefixes.Count == 0)
        {
            return Array.Empty<int>();
        }

        var indices = new HashSet<int>();
        var bones = model.BoneHierarchy.Bones;
        for (var i = 0; i < bones.Length; i++)
        {
            if (MatchesAny(bones[i].Name, prefixes))
            {
                indices.Add(i);
            }
        }

        foreach (var subObject in model.SubObjects)
        {
            if (subObject.Bone != null && MatchesAny(subObject.Name, prefixes))
            {
                indices.Add(subObject.Bone.Index);
            }
        }

        return indices.ToArray();
    }

    /// <summary>Hides the muzzle flashes of a model instance rendered outside a draw module.</summary>
    public static void HideForStandaloneRender(ModelInstance modelInstance, IEnumerable<ObjectDefinition> objectDefinitions)
    {
        var names = CollectNamesForModel(objectDefinitions, modelInstance.Model.Name);
        foreach (var index in ResolveBoneIndices(modelInstance.Model, names))
        {
            modelInstance.BoneVisibilities[index] = false;
        }
    }

    private static bool MatchesAny(string name, IReadOnlyList<string> prefixes)
    {
        if (name == null)
        {
            return false;
        }

        foreach (var prefix in prefixes)
        {
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
