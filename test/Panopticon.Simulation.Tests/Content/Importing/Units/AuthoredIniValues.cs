using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Panopticon.IO;

namespace Panopticon.Tests.Content.Importing.Units;

/// <summary>
/// Reads authored numbers straight from installed INI text, independently of the INI parser and
/// the adapter, so parity tests compare source text with what the shared pipeline publishes.
/// Returns null for a missing block or field and for non-numeric (macro) values.
/// </summary>
internal sealed class AuthoredIniValues
{
    private readonly Dictionary<string, List<string>> _blocks = new(StringComparer.OrdinalIgnoreCase);

    public AuthoredIniValues(FileSystem fileSystem, string blockKeyword, IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            var entry = fileSystem.GetFile(path);
            if (entry is null)
            {
                continue;
            }

            using var reader = new StreamReader(entry.Open());
            List<string>? current = null;
            var depth = 0;
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                var text = line.Split(';')[0].Split("//")[0].Trim();
                var tokens = text.Split((char[])[' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length == 0)
                {
                    continue;
                }

                if (current is null)
                {
                    if (tokens.Length >= 2 && tokens[0].Equals(blockKeyword, StringComparison.OrdinalIgnoreCase))
                    {
                        current = [];
                        _blocks[tokens[1]] = current;
                        depth = 1;
                    }

                    continue;
                }

                if (tokens[0].Equals("End", StringComparison.OrdinalIgnoreCase) || tokens[0].Equals("END", StringComparison.Ordinal))
                {
                    if (--depth == 0)
                    {
                        current = null;
                    }

                    continue;
                }

                if (IsNestedBlockOpener(tokens[0]))
                {
                    depth++;
                }

                current.Add(text);
            }
        }
    }

    public static IEnumerable<string> IniFiles(FileSystem fileSystem, string directory)
    {
        return fileSystem.GetFilesInDirectory(directory, "*.ini", SearchOption.AllDirectories).Select(e => e.FilePath);
    }

    public double? Get(string block, string field)
    {
        if (!_blocks.TryGetValue(block, out var lines))
        {
            return null;
        }

        foreach (var line in lines)
        {
            var parts = line.Split('=', 2);
            if (parts.Length == 2 && parts[0].Trim().Equals(field, StringComparison.OrdinalIgnoreCase))
            {
                var value = parts[1].Trim().Split((char[])[' ', '\t'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.TrimEnd('%');
                return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : null;
            }
        }

        return null;
    }

    public IReadOnlyList<string> Lines(string block) => _blocks.TryGetValue(block, out var lines) ? lines : [];

    // Body/Behavior/Draw modules and set blocks open with "Key = Type Tag", so an '=' alone does not mean a field.
    private static bool IsNestedBlockOpener(string keyword)
    {
        return keyword is "Body" or "Behavior" or "Draw" or "ClientUpdate" or "ArmorSet" or "WeaponSet" or "Prerequisites"
            or "UnitSpecificSounds" or "UnitSpecificFX" or "ConditionState" or "DefaultConditionState" or "TransitionState"
            or "AddModule" or "ReplaceModule" or "InheritableModule" or "OverrideableByLikeKind" or "Turret" or "AltTurret";
    }
}
