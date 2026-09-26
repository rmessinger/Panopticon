#nullable enable

using System;
using System.Collections.Generic;
using System.IO;

namespace Panopticon.Content.Importing.Nif;

/// <summary>
/// One material slot targeted by property animation: the sub-object full
/// name plus the material index inside that sub-object's mesh.
/// </summary>
internal readonly record struct NifMaterialSlotTarget(string SubObjectFullName, int MaterialIndex);

/// <summary>
/// Maps NIF source block indices to imported animation targets: nodes to
/// bone names, shapes to sub-object full names, and material or texturing
/// properties to every material slot that uses them. Built during the
/// scene walk; consumed by the C4e property-animation converter.
/// </summary>
internal sealed class NifPropertyTargetMap
{
    private readonly string _modelName;
    private readonly Dictionary<int, string> _nodeToBoneName = new();
    private readonly Dictionary<int, string> _shapeToSubObject = new();
    private readonly Dictionary<string, List<int>> _shapesByName =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, List<NifMaterialSlotTarget>> _propertyToSlots = new();
    private readonly Dictionary<int, List<(int PropertyIndex, string TypeName)>> _shapeProperties = new();

    internal NifPropertyTargetMap(string modelName)
    {
        _modelName = modelName;
    }

    /// <summary>
    /// Controlled-block property targets look like
    /// "&lt;shape-name&gt;\nPROP\n&lt;property-type&gt;" with a case-insensitive
    /// PROP separator; plain node names are not property targets.
    /// </summary>
    public static bool TryParsePropertyTarget(
        string controlledBlockTarget,
        out string shapeName,
        out string propertyTypeName)
    {
        var parts = controlledBlockTarget.Split('\n');
        if (parts.Length == 3 && parts[1].Equals("PROP", StringComparison.OrdinalIgnoreCase))
        {
            shapeName = parts[0];
            propertyTypeName = parts[2];
            return true;
        }

        shapeName = "";
        propertyTypeName = "";
        return false;
    }

    internal void RecordBone(int nodeIndex, string boneName)
    {
        _nodeToBoneName[nodeIndex] = boneName;
    }

    internal void RecordShape(
        int shapeIndex,
        string shapeName,
        string subObjectFullName,
        IReadOnlyList<(int PropertyIndex, string TypeName)> properties)
    {
        _shapeToSubObject[shapeIndex] = subObjectFullName;
        if (!_shapesByName.TryGetValue(shapeName, out var candidates))
        {
            candidates = [];
            _shapesByName[shapeName] = candidates;
        }

        candidates.Add(shapeIndex);
        _shapeProperties[shapeIndex] = new List<(int PropertyIndex, string TypeName)>(properties);
        foreach (var (propertyIndex, _) in properties)
        {
            if (!_propertyToSlots.TryGetValue(propertyIndex, out var slots))
            {
                slots = [];
                _propertyToSlots[propertyIndex] = slots;
            }

            slots.Add(new NifMaterialSlotTarget(subObjectFullName, MaterialIndex: 0));
        }
    }

    public bool TryGetBoneName(int nodeIndex, out string boneName)
    {
        return _nodeToBoneName.TryGetValue(nodeIndex, out boneName!);
    }

    public bool TryGetSubObjectFullName(int shapeIndex, out string subObjectFullName)
    {
        return _shapeToSubObject.TryGetValue(shapeIndex, out subObjectFullName!);
    }

    /// <summary>
    /// Resolves a visibility target name against converted shapes: a
    /// unique name match yields the sub-object full name; duplicate shape
    /// names are ambiguous and fail.
    /// </summary>
    public bool TryResolveShapeSubObject(string shapeName, out string subObjectFullName)
    {
        if (!_shapesByName.TryGetValue(shapeName, out var candidates))
        {
            subObjectFullName = "";
            return false;
        }

        if (candidates.Count > 1)
        {
            throw new InvalidDataException(
                $"NIF model '{_modelName}' has {candidates.Count} shapes named '{shapeName}'; " +
                "the visibility target is ambiguous.");
        }

        subObjectFullName = _shapeToSubObject[candidates[0]];
        return true;
    }

    /// <summary>
    /// Resolves an embedded controller target ref: every material slot
    /// whose shape uses the referenced property block, in walk order.
    /// </summary>
    public IReadOnlyList<NifMaterialSlotTarget> ResolvePropertySlots(int propertyIndex)
    {
        if (_propertyToSlots.TryGetValue(propertyIndex, out var slots) && slots.Count > 0)
        {
            return slots;
        }

        throw new InvalidDataException(
            $"NIF model '{_modelName}' property block {propertyIndex} is not used by any converted material slot.");
    }

    /// <summary>
    /// Resolves a controlled-block property string. Duplicate shape names
    /// are ambiguous and fail instead of picking the first match.
    /// </summary>
    public IReadOnlyList<NifMaterialSlotTarget> ResolvePropertySlots(
        string shapeName,
        string propertyTypeName)
    {
        if (!_shapesByName.TryGetValue(shapeName, out var candidates))
        {
            throw new InvalidDataException(
                $"NIF model '{_modelName}' has no converted shape named '{shapeName}' " +
                $"for property target '{shapeName}\\nPROP\\n{propertyTypeName}'.");
        }

        if (candidates.Count > 1)
        {
            throw new InvalidDataException(
                $"NIF model '{_modelName}' has {candidates.Count} shapes named '{shapeName}'; " +
                $"the property target '{shapeName}\\nPROP\\n{propertyTypeName}' is ambiguous.");
        }

        foreach (var (propertyIndex, typeName) in _shapeProperties[candidates[0]])
        {
            if (typeName.Equals(propertyTypeName, StringComparison.OrdinalIgnoreCase))
            {
                return ResolvePropertySlots(propertyIndex);
            }
        }

        throw new InvalidDataException(
            $"NIF shape '{shapeName}' in model '{_modelName}' has no {propertyTypeName} to animate.");
    }
}
