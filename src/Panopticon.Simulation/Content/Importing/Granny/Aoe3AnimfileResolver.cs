using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Panopticon.FileFormats.Bar;

namespace Panopticon.Content.Importing.Granny;

/// <summary>
/// Resolves a proto unit name plus an active tech set into an <see cref="Aoe3UnitVisual"/> by
/// decoding Data\protoy.xml.XMB, the unit's animfile XMB, and attachment includes. Variant
/// selection is deterministic: active techs match logic children by name (ties fail loudly),
/// otherwise the none branch; non-tech logics such as Destruction pick none or the last phase
/// child, which is the undamaged state in the corpus.
/// </summary>
internal sealed class Aoe3AnimfileResolver
{
    private const string ProtoyKey = @"Data\protoy.xml.XMB";
    private const string ArtPrefix = @"Art\";
    private const string ModelComponentName = "ModelComp";

    private readonly AssetImportContext _context;

    public Aoe3AnimfileResolver(AssetImportContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    public Aoe3UnitVisual Resolve(string protoUnitName, IEnumerable<string>? activeTechs = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protoUnitName);

        var techs = new HashSet<string>(activeTechs ?? [], StringComparer.OrdinalIgnoreCase);
        var animfileKey = FindAnimfileKey(protoUnitName);
        var animfile = LoadXmlDocument(animfileKey);
        var root = animfile.Root ?? throw NewInvalid(protoUnitName, $"animfile '{animfileKey}' is empty.");

        var (modelPath, attachElements) = ResolveModelComponent(root, protoUnitName, techs, animfileKey);
        var clips = CollectClips(root);
        var attachments = ResolveAttachments(attachElements, root, techs, protoUnitName);
        var definedBones = root.Elements("definebone")
            .Select(TextName)
            .Where(name => name.Length > 0)
            .ToList();

        return new Aoe3UnitVisual(protoUnitName, modelPath, clips, attachments, definedBones);
    }

    private string FindAnimfileKey(string protoUnitName)
    {
        var entry = _context.FileSystem.GetFile(ProtoyKey)
            ?? throw NewInvalid(protoUnitName, $"'{ProtoyKey}' was not found.");
        XDocument protoy;
        using (var stream = entry.Open())
        {
            protoy = XmbDocument.Read(stream);
        }

        var unit = protoy.Root?.Elements("unit")
            .FirstOrDefault(u => string.Equals(u.Attribute("name")?.Value, protoUnitName, StringComparison.OrdinalIgnoreCase))
            ?? throw NewInvalid(protoUnitName, "the proto file has no unit with that name.");
        var animfileValue = unit.Element("animfile")?.Value.Trim();
        if (string.IsNullOrWhiteSpace(animfileValue))
        {
            throw NewInvalid(protoUnitName, "the unit has no animfile entry.");
        }

        return animfileValue;
    }

    private XDocument LoadXmlDocument(string artRelativePath)
    {
        var xmbKey = ArtPrefix + artRelativePath + ".XMB";
        var entry = _context.FileSystem.GetFile(xmbKey);
        if (entry is not null)
        {
            using var stream = entry.Open();
            return XmbDocument.Read(stream);
        }

        var plainKey = ArtPrefix + artRelativePath;
        var plainEntry = _context.FileSystem.GetFile(plainKey)
            ?? throw new InvalidDataException($"Neither '{xmbKey}' nor '{plainKey}' was found.");
        using var plainStream = plainEntry.Open();
        return XDocument.Load(plainStream);
    }

    private (string ModelPath, List<XElement> AttachElements) ResolveModelComponent(
        XElement root, string protoUnitName, HashSet<string> techs, string animfileKey)
    {
        var components = root.Elements("component").ToList();
        var modelComponent = components.FirstOrDefault(c => TextEquals(c, ModelComponentName))
            ?? components.FirstOrDefault(c => TextEquals(c, protoUnitName))
            ?? FindFirstModelComponent(components, techs, protoUnitName, animfileKey)
            ?? throw NewInvalid(protoUnitName, $"animfile '{animfileKey}' has no component that resolves to a Granny model.");

        var (files, attachElements) = ResolveComponent(modelComponent, techs, protoUnitName, animfileKey);
        if (files.Count > 1)
        {
            throw NewInvalid(protoUnitName,
                $"component '{TextName(modelComponent)}' resolves to {files.Count} Granny models ({string.Join(", ", files)}); expected exactly one.");
        }

        return (BuildModelPath(files[0]), attachElements);
    }

    private XElement? FindFirstModelComponent(
        List<XElement> components, HashSet<string> techs, string protoUnitName, string animfileKey)
    {
        foreach (var component in components)
        {
            var (files, _) = ResolveComponent(component, techs, protoUnitName, animfileKey);
            if (files.Count == 1)
            {
                return component;
            }
        }

        return null;
    }

    private (List<string> ModelFiles, List<XElement> AttachElements) ResolveComponent(
        XElement component, HashSet<string> techs, string protoUnitName, string sourceDescription)
    {
        var modelFiles = new List<string>();
        var attachElements = new List<XElement>();
        Collect(component, modelFiles, attachElements, techs, protoUnitName, sourceDescription);
        return (modelFiles, attachElements);
    }

    private void Collect(
        XElement element,
        List<string> modelFiles,
        List<XElement> attachElements,
        HashSet<string> techs,
        string protoUnitName,
        string sourceDescription)
    {
        modelFiles.AddRange(element.Elements("assetreference")
            .Where(a => string.Equals(a.Attribute("type")?.Value, "GrannyModel", StringComparison.OrdinalIgnoreCase))
            .Select(a => a.Element("file")?.Value.Trim())
            .Where(file => !string.IsNullOrWhiteSpace(file))!);
        attachElements.AddRange(element.Elements("attach"));

        foreach (var logic in element.Elements("logic"))
        {
            var selected = SelectLogicChild(logic, techs, protoUnitName, sourceDescription);
            if (selected is not null)
            {
                Collect(selected, modelFiles, attachElements, techs, protoUnitName, sourceDescription);
            }
        }
    }

    private XElement? SelectLogicChild(XElement logic, HashSet<string> techs, string protoUnitName, string sourceDescription)
    {
        var children = logic.Elements().ToList();
        var logicType = logic.Attribute("type")?.Value ?? "Tech";
        if (string.Equals(logicType, "Tech", StringComparison.OrdinalIgnoreCase)
            || string.Equals(logicType, "Age", StringComparison.OrdinalIgnoreCase))
        {
            var matches = children
                .Where(c => c.Name.LocalName != "none" && techs.Contains(c.Name.LocalName))
                .ToList();
            var distinctNames = matches.Select(c => c.Name.LocalName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (distinctNames.Count > 1)
            {
                throw NewInvalid(protoUnitName,
                    $"'{sourceDescription}' logic '{logicType}' matches multiple active techs ({string.Join(", ", distinctNames)}).");
            }

            if (matches.Count > 0)
            {
                return matches[0];
            }

            return children.FirstOrDefault(c => c.Name.LocalName == "none");
        }

        // Non-tech logics such as Destruction carry phase children (p1 damaged, p99 undamaged);
        // none is preferred, otherwise the last phase child is the undamaged state.
        return children.FirstOrDefault(c => c.Name.LocalName == "none") ?? children.LastOrDefault();
    }

    private Dictionary<string, string> CollectClips(XElement root)
    {
        var clips = new Dictionary<string, string>();
        foreach (var anim in root.Elements("anim"))
        {
            var animName = TextName(anim);
            if (animName.Length == 0 || clips.ContainsKey(animName))
            {
                continue;
            }

            var best = anim.Elements("assetreference")
                .Where(a => string.Equals(a.Attribute("type")?.Value, "GrannyAnim", StringComparison.OrdinalIgnoreCase))
                .Select(a => (File: a.Element("file")?.Value.Trim(), Weight: ParseWeight(a)))
                .Where(candidate => !string.IsNullOrWhiteSpace(candidate.File))
                .OrderByDescending(candidate => candidate.Weight)
                .FirstOrDefault();
            if (best.File is not null)
            {
                clips[animName] = BuildModelPath(best.File);
            }
        }

        return clips;
    }

    private static int ParseWeight(XElement assetReference)
    {
        var weight = assetReference.Element("weight")?.Value.Trim();
        return int.TryParse(weight, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;
    }

    private List<Aoe3UnitAttachment> ResolveAttachments(
        List<XElement> attachElements, XElement animfileRoot, HashSet<string> techs, string protoUnitName)
    {
        var attachments = new List<Aoe3UnitAttachment>();
        var resolvedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var attach in attachElements)
        {
            var name = attach.Attribute("a")?.Value.Trim() ?? string.Empty;
            if (name.Length == 0 || !resolvedNames.Add(name))
            {
                continue;
            }

            var fromBone = attach.Attribute("frombone")?.Value.Trim() ?? string.Empty;
            var toBone = attach.Attribute("tobone")?.Value.Trim() ?? string.Empty;
            var modelPath = ResolveAttachmentModelPath(name, animfileRoot, techs, protoUnitName);
            attachments.Add(new Aoe3UnitAttachment(name, fromBone, toBone, modelPath));
        }

        return attachments;
    }

    private string? ResolveAttachmentModelPath(
        string attachmentName, XElement animfileRoot, HashSet<string> techs, string protoUnitName)
    {
        var definition = animfileRoot.Elements("attachment")
            .FirstOrDefault(d => TextEquals(d, attachmentName));
        if (definition is null)
        {
            return null;
        }

        var candidates = new List<XElement> { definition };
        candidates.AddRange(definition.Elements("include")
            .Select(include => TextName(include))
            .Where(path => path.Length > 0)
            .Select(path => LoadXmlDocument(path))
            .Select(doc => doc.Root)
            .Where(r => r is not null)!);

        XElement? component = null;
        foreach (var candidate in candidates)
        {
            component = candidate.Elements("component").FirstOrDefault(c => TextEquals(c, attachmentName));
            if (component is not null)
            {
                break;
            }
        }

        if (component is null)
        {
            // Included documents name their model component after the template (ModelComp),
            // not after the attachment; fall back to the first component with a model.
            foreach (var candidate in candidates)
            {
                component = candidate.Elements("component")
                    .FirstOrDefault(c => CountResolvedModels(c, techs, protoUnitName) == 1);
                if (component is not null)
                {
                    break;
                }
            }
        }

        if (component is null)
        {
            return null;
        }

        var (files, _) = ResolveComponent(component, techs, protoUnitName, attachmentName);
        return files.Count == 1 ? BuildModelPath(files[0]) : null;
    }

    private int CountResolvedModels(XElement component, HashSet<string> techs, string protoUnitName)
    {
        var (files, _) = ResolveComponent(component, techs, protoUnitName, component.Name.LocalName);
        return files.Count;
    }

    private static string BuildModelPath(string file)
    {
        return file.EndsWith(".gr2", StringComparison.OrdinalIgnoreCase) ? ArtPrefix + file : ArtPrefix + file + ".gr2";
    }

    private static string TextName(XElement element)
    {
        return (element.FirstNode as XText)?.Value.Trim() ?? string.Empty;
    }

    private static bool TextEquals(XElement element, string expected)
    {
        return string.Equals(TextName(element), expected, StringComparison.OrdinalIgnoreCase);
    }

    private static InvalidDataException NewInvalid(string protoUnitName, string detail)
    {
        return new InvalidDataException($"AoE3 unit '{protoUnitName}': {detail}");
    }
}
