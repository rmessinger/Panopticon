#nullable enable

using System;
using Panopticon;

namespace Panopticon.Content.Importing;

/// <summary>
/// One named renderable attached to a skeleton bone.
/// </summary>
public sealed class ImportedSubObject
{
    public string FullName { get; }

    public string DisplayName { get; }

    public int ParentBoneIndex { get; }

    public ImportedRenderObject RenderObject { get; }

    public ImportedSubObject(
        string fullName,
        string displayName,
        int parentBoneIndex,
        ImportedRenderObject renderObject)
    {
        ArgumentNullException.ThrowIfNull(fullName);

        ArgumentNullException.ThrowIfNull(displayName);

        Ensure.AtLeast(parentBoneIndex, 0, "Parent bone index must not be negative.");

        ArgumentNullException.ThrowIfNull(renderObject);

        FullName = fullName;
        DisplayName = displayName;
        ParentBoneIndex = parentBoneIndex;
        RenderObject = renderObject;
    }
}
