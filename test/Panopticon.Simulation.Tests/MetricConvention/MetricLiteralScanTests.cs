using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Panopticon.Tests.MetricConvention;

/// <summary>
/// Scans src/**/*.cs (excluding obj and bin) for the feet-to-meters factor 0.3048 and its inverse 3.28084.
/// Comments, string literals, and char literals are blanked before matching, so documentation and
/// messages may cite the constants. A match must be a whole numeric token, so identifiers and longer
/// literals such as 10.3048 or 0.30480 do not match.
/// </summary>
public sealed class MetricLiteralScanTests
{
    private const string Guidance =
        "Use UnitConversionPolicy (GeneralsFeetToMeters, ScaleLength, UnscaleLength) instead of a literal 0.3048 or 3.28084.";

    private static readonly Regex MetricLiteral = new(
        @"(?<![\w.])(?:0?\.3048|3\.28084(?:0)?)[fFdDmM]?(?![\w.])",
        RegexOptions.CultureInvariant);

    private static readonly IReadOnlyDictionary<string, string> Allowlist = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["src/Panopticon.Simulation/Content/Importing/Units/Materialization/UnitConversionPolicy.cs"] =
            "The conversion helper; declares GeneralsFeetToMeters.",
        ["src/Panopticon.Physics/PhysicsUnitConversion.cs"] =
            "Panopticon.Physics references only Panopticon.Mathematics and cannot reference UnitConversionPolicy.",
    };

    [Fact]
    public void MetricLiterals_OutsideAllowlistedFiles_AreAbsent()
    {
        var root = FindRepositoryRoot();
        var violations = new List<string>();

        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (IsBuildOutput(relative) || Allowlist.ContainsKey(relative))
            {
                continue;
            }

            foreach (var hit in FindMetricLiterals(File.ReadAllText(file)))
            {
                violations.Add($"{relative}:{hit.Line}: {hit.Literal}");
            }
        }

        Assert.True(
            violations.Count == 0,
            $"{Guidance}\nStray metric literal(s):\n{string.Join('\n', violations)}");
    }

    [Fact]
    public void Allowlist_EntriesExist()
    {
        var root = FindRepositoryRoot();
        var missing = Allowlist.Keys.Where(path => !File.Exists(Path.Combine(root, path))).ToList();

        Assert.True(missing.Count == 0, $"Allowlist entries missing from src: {string.Join(", ", missing)}");
    }

    [Theory]
    [InlineData("float x = 0.3048f;", 1)]
    [InlineData("var y = .3048;", 1)]
    [InlineData("double z = 3.28084;", 1)]
    [InlineData("var w = 3.280840d;", 1)]
    [InlineData("const float Q = 3.28084F;", 1)]
    [InlineData("var m = 0.3048m;", 1)]
    [InlineData("// 0.3048 meters per legacy unit", 0)]
    [InlineData("/* 3.28084 */ var a = 1;", 0)]
    [InlineData("var s = \"0.3048\";", 0)]
    [InlineData("var c = '3';", 0)]
    [InlineData("var v = @\"0.3048\";", 0)]
    [InlineData("var t = 10.3048;", 0)]
    [InlineData("var u = 0.30480;", 0)]
    [InlineData("var n = 3.2808;", 0)]
    [InlineData("var i = Meters0_3048;", 0)]
    public void Detector_MatchesWholeLiteralsOutsideCommentsAndStrings(string source, int expectedHits)
    {
        Assert.Equal(expectedHits, FindMetricLiterals(source).Count);
    }

    private static IReadOnlyList<(int Line, string Literal)> FindMetricLiterals(string source)
    {
        var stripped = BlankCommentsAndLiterals(source);
        var hits = new List<(int Line, string Literal)>();
        var lines = stripped.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            foreach (Match match in MetricLiteral.Matches(lines[i]))
            {
                hits.Add((i + 1, match.Value));
            }
        }

        return hits;
    }

    private static string BlankCommentsAndLiterals(string source)
    {
        var chars = source.ToCharArray();
        var i = 0;
        while (i < source.Length)
        {
            var end = SkipCommentOrLiteral(source, i);
            if (end == i)
            {
                i++;
                continue;
            }

            for (var j = i; j < end; j++)
            {
                if (chars[j] != '\n')
                {
                    chars[j] = ' ';
                }
            }

            i = end;
        }

        return new string(chars);
    }

    // Returns the index just past the comment or literal that starts at i, or i if none starts there.
    // Interpolation holes inside $"..." are blanked with the rest of the string.
    private static int SkipCommentOrLiteral(string s, int i)
    {
        if (s[i] == '/' && Next(s, i) == '/')
        {
            return IndexOrEnd(s, '\n', i);
        }

        if (s[i] == '/' && Next(s, i) == '*')
        {
            var close = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
            return close < 0 ? s.Length : close + 2;
        }

        if (s[i] == '"' && s.AsSpan(i).StartsWith("\"\"\"".AsSpan()))
        {
            var quotes = 0;
            while (i + quotes < s.Length && s[i + quotes] == '"')
            {
                quotes++;
            }

            var close = s.IndexOf(new string('"', quotes), i + quotes, StringComparison.Ordinal);
            return close < 0 ? s.Length : close + quotes;
        }

        if (s[i] == '"' && i > 0 && s[i - 1] == '@')
        {
            var j = i + 1;
            while (j < s.Length)
            {
                if (s[j] == '"')
                {
                    if (j + 1 < s.Length && s[j + 1] == '"')
                    {
                        j += 2;
                        continue;
                    }

                    return j + 1;
                }

                j++;
            }

            return s.Length;
        }

        if (s[i] == '"')
        {
            var j = i + 1;
            while (j < s.Length && s[j] != '"' && s[j] != '\n')
            {
                j += s[j] == '\\' ? 2 : 1;
            }

            return j < s.Length && s[j] == '"' ? j + 1 : Math.Min(j, s.Length);
        }

        if (s[i] == '\'')
        {
            var j = i + 1;
            if (j < s.Length && s[j] == '\\')
            {
                j++;
            }

            j++;
            while (j < s.Length && s[j] != '\'' && s[j] != '\n')
            {
                j++;
            }

            return j < s.Length && s[j] == '\'' ? j + 1 : Math.Min(j, s.Length);
        }

        return i;
    }

    private static char? Next(string s, int i) => i + 1 < s.Length ? s[i + 1] : null;

    private static int IndexOrEnd(string s, char value, int from)
    {
        var index = s.IndexOf(value, from);
        return index < 0 ? s.Length : index;
    }

    private static bool IsBuildOutput(string relativePath)
    {
        return relativePath.Split('/').Any(segment => segment is "obj" or "bin");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Packages.props")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root (Directory.Packages.props) not found above the test binaries.");
    }
}
