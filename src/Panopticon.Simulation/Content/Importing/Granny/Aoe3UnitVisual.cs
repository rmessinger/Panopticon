using System.Collections.Generic;

namespace Panopticon.Content.Importing.Granny;

/// <summary>
/// An attachment resolved from an animfile: the attach element's bones plus the attached
/// component's model path, or null for non-model attachments such as particles.
/// </summary>
internal sealed record Aoe3UnitAttachment(string Name, string FromBone, string ToBone, string? ModelPath);

/// <summary>
/// The resolved visual definition of one proto unit for one tech set: a single Granny model
/// path, clip paths keyed by anim name, attachments, and defined bones.
/// </summary>
internal sealed record Aoe3UnitVisual(
    string ProtoUnitName,
    string ModelPath,
    IReadOnlyDictionary<string, string> ClipsByAnimName,
    IReadOnlyList<Aoe3UnitAttachment> Attachments,
    IReadOnlyList<string> DefinedBones);
