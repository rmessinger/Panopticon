#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Panopticon.Content.Importing.Units;
using Panopticon.Content.Importing.Units.Packages;
using Panopticon.IO;

namespace Panopticon.Tools.H5Migrate;

/// <summary>
/// Production retained INI payload provider sourcing exact original INI block bytes
/// for legacy-bound records directly from the installed Generals corpus (WI-H5-02).
/// Blocks are extracted directly from the file stream; no INI text is synthesized or re-serialized.
/// </summary>
public sealed class IniRetainedPayloadProvider : IUnitPackageRetainedPayloadProvider
{
    private readonly Dictionary<(ContentKind Kind, string Name), (byte[] Bytes, string FilePath, int Line)> _blocks =
        new(RecordKeyComparer.Instance);

    public IEnumerable<(ContentKind Kind, string Name)> Keys => _blocks.Keys;

    public IniRetainedPayloadProvider(FileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        ScanFile(fileSystem, @"Data\INI\Armor.ini");
        ScanFile(fileSystem, @"Data\INI\Locomotor.ini");
        ScanFile(fileSystem, @"Data\INI\Weapon.ini");
        ScanFile(fileSystem, @"Data\INI\Default\Upgrade.ini");
        ScanFile(fileSystem, @"Data\INI\Upgrade.ini");
        ScanFile(fileSystem, @"Data\INI\Default\Object.ini");
        foreach (var file in fileSystem.GetFilesInDirectory(@"Data\INI\Object", "*.ini", SearchOption.AllDirectories))
        {
            ScanFile(fileSystem, file.FilePath);
        }
    }

    public UnitPackageRetainedBlock? GetBlock(UnitRetainedBlockRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_blocks.TryGetValue((request.RecordId.Kind, request.RecordId.Name), out var data))
        {
            return new UnitPackageRetainedBlock(
                UnitPackageFormat.SupportedGeneralsIniDialect,
                UnitPackageFormat.SupportedGeneralsIniDialectVersion,
                data.Bytes,
                new UnitPackageProvenance("generals", data.FilePath, data.Line));
        }

        return null;
    }

    public bool TryGetBlockData(ContentId recordId, out (byte[] Bytes, string FilePath, int Line) data)
    {
        return _blocks.TryGetValue((recordId.Kind, recordId.Name), out data);
    }

    private void ScanFile(FileSystem fileSystem, string filePath)
    {
        var entry = fileSystem.GetFile(filePath);
        if (entry is null)
        {
            return;
        }

        using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);

        string? line;
        var lineNumber = 0;
        var blockStartLine = -1;
        var blockName = string.Empty;
        ContentKind? blockKind = null;
        var depth = 0;
        var currentLines = new List<string>();

        while ((line = reader.ReadLine()) != null)
        {
            lineNumber++;
            var commentSplit = line.Split(';')[0].Split("//")[0].Trim();
            var tokens = commentSplit.Split((char[])[' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
            {
                if (blockKind is not null)
                {
                    currentLines.Add(line);
                }

                continue;
            }

            ContentKind? matchedKind = null;
            if (tokens.Length >= 2 && !tokens[1].Equals("="))
            {
                matchedKind = tokens[0].ToLowerInvariant() switch
                {
                    "object" or "childobject" or "objectreskin" => ContentKind.Unit,
                    "weapon" => ContentKind.Weapon,
                    "armor" => ContentKind.Armor,
                    "locomotor" => ContentKind.Locomotor,
                    "upgrade" => ContentKind.Upgrade,
                    _ => null,
                };
            }

            if (matchedKind is not null)
            {
                depth = 1;
                blockKind = matchedKind;
                blockName = tokens[1];
                blockStartLine = lineNumber;
                currentLines.Clear();
                currentLines.Add(line);
                continue;
            }

            if (depth == 0)
            {
                continue;
            }

            if (blockKind is not null)
            {
                currentLines.Add(line);
            }

            if (tokens[0].Equals("End", StringComparison.OrdinalIgnoreCase))
            {
                depth--;
                if (depth <= 0)
                {
                    if (blockKind is not null)
                    {
                        var fullBlockText = string.Join("\n", currentLines) + "\n";
                        var bytes = Encoding.UTF8.GetBytes(fullBlockText);
                        _blocks[(blockKind.Value, blockName)] = (bytes, filePath, blockStartLine);
                        currentLines.Clear();
                        blockKind = null;
                    }

                    depth = 0;
                }

                continue;
            }

            if (IsNestedBlockOpener(tokens))
            {
                depth++;
            }
        }
    }

    private static bool IsNestedBlockOpener(string[] tokens)
    {
        if (tokens.Length == 0)
        {
            return false;
        }

        var keyword = tokens[0];
        if (tokens.Length >= 2 && tokens[1].Equals("="))
        {
            return keyword is "Body" or "Behavior" or "Draw" or "ClientUpdate" or "ClientBehavior"
                or "AddModule" or "ReplaceModule" or "InheritableModule" or "OverrideableByLikeKind"
                or "ConditionState" or "TransitionState";
        }

        return keyword is "ArmorSet" or "WeaponSet" or "Prerequisites" or "UnitSpecificSounds"
            or "UnitSpecificFX" or "ConditionState" or "DefaultConditionState" or "TransitionState"
            or "Turret" or "AltTurret" or "Flammability" or "ThreatBreakdown" or "AutoResolveArmor"
            or "AutoResolveWeapon" or "Decal"
            or "AddModule" or "ReplaceModule" or "InheritableModule" or "OverrideableByLikeKind";
    }

    private sealed class RecordKeyComparer : IEqualityComparer<(ContentKind Kind, string Name)>
    {
        public static readonly RecordKeyComparer Instance = new();

        public bool Equals((ContentKind Kind, string Name) x, (ContentKind Kind, string Name) y)
        {
            return x.Kind == y.Kind && string.Equals(x.Name, y.Name, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode((ContentKind Kind, string Name) obj)
        {
            return HashCode.Combine(obj.Kind, StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Name));
        }
    }
}
