#nullable enable

using System;

using System.Numerics;

namespace OpenSage.Content.Importing;

/// <summary>
/// One skeleton bone: name, parent index (-1 for a root), and bind pose transform.
/// </summary>
public sealed class ImportedBone
{
    public string Name { get; }

    public int ParentIndex { get; }

    public Vector3 BindTranslation { get; }

    public Quaternion BindRotation { get; }

    public ImportedBone(string name, int parentIndex, in Vector3 bindTranslation, in Quaternion bindRotation)
    {
        if (name is null)
        {
            throw new ArgumentNullException(nameof(name));
        }

        if (parentIndex < -1)
        {
            throw new ArgumentException("Parent index must be -1 (root) or a bone index.", nameof(parentIndex));
        }

        Name = name;
        ParentIndex = parentIndex;
        BindTranslation = bindTranslation;
        BindRotation = bindRotation;
    }
}
