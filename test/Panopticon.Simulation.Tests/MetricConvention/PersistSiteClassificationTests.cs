using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Panopticon.Tests.MetricConvention;

/// <summary>
/// Scans the save-stream sources (src/Panopticon.Simulation/Logic and Data/Sav, excluding obj and bin) for plain
/// length-capable persist calls. Comments and string literals are blanked before matching. A plain call passes only
/// when its (file, field) pair is allowlisted with a classification reason. PersistLength* calls never match.
/// </summary>
public sealed class PersistSiteClassificationTests
{
    private const string Guidance =
        "Each length-capable save-stream persist call must use a PersistLength* helper, which converts legacy-feet values on read. " +
        "Classify each unlisted plain call: convert it to PersistLength*, or add its (file, field) with a reason to PersistSiteClassificationTests.Allowlist.";

    private const string Opaque = "persisted opaque, no consumer evidence; explicit diagnostic, never blanket-converted";

    private static readonly string[] ScanRoots =
    {
        "src/Panopticon.Simulation/Logic",
        "src/Panopticon.Simulation/Data/Sav",
    };

    private static readonly Regex PlainPersistCall = new(
        @"\b(?<method>Persist(?:Single|Vector3|Vector2|Point2D|Point3D|Matrix4x3)(?:Value)?)\s*\(\s*ref\s+(?<field>[^,)]+?)\s*[,)]",
        RegexOptions.CultureInvariant);

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Allowlist = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
    {
        ["src/Panopticon.Simulation/Logic/Object/GameObject.cs"] = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["_buildProgress100"] = "progress percentage",
            ["_integerPosition"] = "unused field, no consumer evidence",
        },
        ["src/Panopticon.Simulation/Logic/Object/Update/AIUpdate/AIUpdate.cs"] = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["_desiredSpeed"] = "speed rate scalar; rates set from INI at runtime, not raw lengths in this chunk",
            ["_pathfindGoalCell"] = "navigation cell indices",
            ["_pathfindCurrentCell"] = "navigation cell indices",
            ["_locomotorGoalData"] = "opaque union: position or angle per LocomotorGoalType, not consumed in simulation logic",
        },
        ["src/Panopticon.Simulation/Logic/Object/Update/AIUpdate/MissileAIUpdate.cs"] = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["unknownFloat2"] = "fixed sentinel 99999.0f check",
            ["_unknownPosition"] = "persisted only, no consumer evidence",
            ["_unknownFloat1"] = "persisted only, no consumer evidence",
            ["_currentPositionMaybe"] = "persisted only, no consumer evidence",
        },
        ["src/Panopticon.Simulation/Logic/Object/Update/ToppleUpdate.cs"] = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["_toppleDirection"] = "normalized unit direction",
            ["_angularVelocity"] = "angular rate",
            ["_angularAcceleration"] = "angular rate",
            ["_angularAccumulation"] = "angle",
            ["_angleDeltaX"] = "angle",
        },
        ["src/Panopticon.Simulation/Logic/Object/Locomotor.cs"] = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["_brakingFactor"] = "ratio",
            ["_maxLift"] = "lift acceleration rate",
            ["_maxSpeed"] = "speed rate",
            ["_maxAcceleration"] = "acceleration rate",
            ["_maxBraking"] = "deceleration rate",
            ["_maxTurnRate"] = "angular rate",
            ["_preferredHeightDamping"] = "ratio",
            ["_angleOffset"] = "angle",
            ["_offsetIncrement"] = "angle",
        },
        ["src/Panopticon.Simulation/Logic/Object/Behaviors/PhysicsBehavior.cs"] = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["yawRate"] = "angular rate",
            ["rollRate"] = "angular rate",
            ["pitchRate"] = "angular rate",
            ["_mass"] = "mass",
            ["_extraBounciness"] = "restitution",
            ["_extraFriction"] = "friction",
            ["tmp"] = "dead legacy v1 field",
        },
        ["src/Panopticon.Simulation/Logic/Object/ClientUpdate/SwayClientUpdate.cs"] = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["_currentValue"] = "interpolation factor",
            ["_currentAngle"] = "angle",
            ["_currentDelta"] = "angle delta",
            ["_currentAngleLimit"] = "angle limit",
            ["_leanAngle"] = "angle",
        },
        ["src/Panopticon.Simulation/Logic/Object/Damage/DamageInfo.cs"] = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Amount"] = "damage",
            ["ShockWaveVector"] = "no consumer evidence",
            ["ShockWaveAmount"] = "impulse magnitude",
            ["ShockWaveTaperOff"] = "ratio",
            ["ActualDamageDealt"] = "damage",
            ["ActualDamageClipped"] = "damage",
        },
        ["src/Panopticon.Simulation/Logic/Object/ExperienceTracker.cs"] = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["_experienceScalar"] = "multiplier ratio",
        },
        ["src/Panopticon.Simulation/Logic/Player.cs"] = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["GeneralsExperienceMultiplier"] = "ratio",
            ["_activeArmorDamageScalar"] = "ratio",
            ["_activeSightRangeScalar"] = "ratio",
        },
        ["src/Panopticon.Simulation/Logic/PartitionCellManager.cs"] = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["partitionCellSize"] = "fixed partition grid cell size constant check",
        },
        ["src/Panopticon.Simulation/Logic/Object/Draw/W3dModelDraw.cs"] = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["_unknownFloat"] = "no consumer evidence",
            ["UnknownFloat1"] = "no consumer evidence",
            ["UnknownFloat2"] = "no consumer evidence",
        },
        ["src/Panopticon.Simulation/Logic/Object/Draw/W3dRopeDraw.cs"] = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["_unknownFloat1"] = "unconsumed rope parameter",
            ["_unknownFloat2"] = "unconsumed rope parameter",
            ["_unknownFloat3"] = "unconsumed rope parameter",
            ["_unknownFloat4"] = "unconsumed rope parameter",
            ["_width"] = "visual rope model-space parameter, unconsumed in simulation logic",
            ["_wobbleLength"] = "visual rope model-space parameter, unconsumed in simulation logic",
            ["_wobbleAmplitude"] = "visual rope model-space parameter, unconsumed in simulation logic",
        },
        ["src/Panopticon.Simulation/Logic/Object/Update/AIUpdate/JetAIUpdate.cs"] = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["_positionSomething"] = "persisted only, no consumer evidence",
        },
        ["src/Panopticon.Simulation/Logic/Object/Update/AIUpdate/TurretAIUpdate.cs"] = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["_unknownFloat1"] = "no consumer evidence",
            ["_unknownFloat2"] = "no consumer evidence",
        },
        ["src/Panopticon.Simulation/Logic/Object/Update/StealthUpdate.cs"] = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["_unknownFloat1"] = "no consumer evidence",
            ["_unknownFloat2"] = "no consumer evidence",
        },
        ["src/Panopticon.Simulation/Logic/Object/Update/AIUpdate/UnknownStateData.cs"] = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["TargetPosition"] = "opaque state target union",
        },
        ["src/Panopticon.Simulation/Logic/Object/Update/AIUpdate/DozerAndWorkerState.cs"] = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["UnknownPos"] = "persisted only, no consumer evidence",
        },
        ["src/Panopticon.Simulation/Logic/AI/AIStates/AttackState.cs"] = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["_unknownPosition"] = "persisted only, no consumer evidence (FireWeapon too)",
        },
        ["src/Panopticon.Simulation/Logic/Object/ClientUpdate/LaserUpdate.cs"] = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["_unknownFloat"] = "no consumer evidence",
        },
        ["src/Panopticon.Simulation/Logic/GhostObject.cs"] = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["_angle"] = "angle",
            ["scale"] = "sentinel check (must equal 1.0f), dimensionless",
            ["meshTransform"] = "model-space bone transform, not a world translation; TODO notes absolute, left unconverted pending a mesh-space audit",
        },
        ["src/Panopticon.Simulation/Logic/Map/BuildListInfo.cs"] = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["_angle"] = "angle",
        },
        ["src/Panopticon.Simulation/Logic/Object/Animation.cs"] = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["unknownFloat"] = "sentinel check (must equal 1.0f), dimensionless",
        },
        ["src/Panopticon.Simulation/Logic/AI/SkirmishAIPlayer.cs"] = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["_unknownFloat1"] = Opaque,
            ["_unknownFloat2"] = Opaque,
        },
        ["src/Panopticon.Simulation/Logic/AI/AIPlayer.cs"] = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["_unknownPosition"] = Opaque,
            ["_unknownFloat"] = Opaque,
        },
        ["src/Panopticon.Simulation/Logic/Object/Update/StructureToppleUpdate.cs"] = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["_unknownFloat"] = Opaque,
        },
        ["src/Panopticon.Simulation/Logic/Object/Update/BattlePlanUpdate.cs"] = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["_activeArmorDamageScalar"] = "ratio (1.0 default, 0.9 under hold the line)",
            ["_activeSightRangeScalar"] = "ratio (1.0 default, 1.2 under search and destroy)",
        },
        ["src/Panopticon.Simulation/Logic/Object/Update/DynamicShroudClearingRangeUpdate.cs"] = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["_unknown9"] = "sentinel check (must equal 300.0f or 0); conversion would break the check",
            ["_unknownFloat"] = Opaque,
        },
        ["src/Panopticon.Simulation/Logic/Object/Update/FlammableUpdate.cs"] = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["_remainingDamageBeforeCatchingFire"] = "damage",
        },
        ["src/Panopticon.Simulation/Logic/Object/Contain/OpenContain.cs"] = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["item"] = Opaque,
        },
        ["src/Panopticon.Simulation/Logic/Object/Production/ProductionJob.cs"] = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["_progressPercentage"] = "progress percentage (can exceed 100%)",
        },
        ["src/Panopticon.Simulation/Logic/Object/Behaviors/BezierProjectileBehavior.cs"] = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["item"] = Opaque,
        },
        ["src/Panopticon.Simulation/Logic/Object/Behaviors/SlowDeathBehavior.cs"] = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["_slowDeathScale"] = "dimensionless scale factor, persisted only",
        },
        ["src/Panopticon.Simulation/Logic/Object/Behaviors/RailroadBehavior.cs"] = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["_unknownFloat1"] = Opaque,
            ["_unknownFloat2"] = Opaque,
            ["_unknownFloat3"] = Opaque,
            ["_unknownVector"] = Opaque,
        },
        ["src/Panopticon.Simulation/Logic/Object/Body/BodyModule.cs"] = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["_damageScalar"] = "ratio (damage multiplier)",
        },
        ["src/Panopticon.Simulation/Logic/Object/Body/ActiveBody.cs"] = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["_currentHealth"] = "health points, not a length",
            ["_currentSubdualDamage"] = "health points, not a length",
            ["_previousHealth"] = "health points, not a length",
            ["_maxHealth"] = "health points, not a length",
            ["_initialHealth"] = "health points, not a length",
        },
        ["src/Panopticon.Simulation/Logic/Object/SpecialPower/SpecialPower.cs"] = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["_unknownFloat"] = Opaque,
        },
        ["src/Panopticon.Simulation/Logic/AI/AIStates/RappelState.cs"] = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["_unknownFloat1"] = Opaque,
            ["_unknownFloat2"] = Opaque,
        },
        ["src/Panopticon.Simulation/Logic/AI/AIStates/WanderInPlaceState.cs"] = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["_unknownPos"] = Opaque,
        },
        ["src/Panopticon.Simulation/Logic/Object/Update/SpecialAbilityUpdate/SpecialAbilityUpdate.cs"] = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["_unknownFloat"] = Opaque,
        },
        ["src/Panopticon.Simulation/Logic/AI/AIStates/ChinookAIUpdate/ChinookCombatDropState.cs"] = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Transform"] = "rope drawable transform with no consumer read; unconsumed by simulation",
            ["UnknownFloat1"] = Opaque,
            ["UnknownFloat2"] = Opaque,
            ["UnknownFloat3"] = Opaque,
        },
        ["src/Panopticon.Simulation/Logic/AI/AIStates/ChinookAIUpdate/ChinookMoveToCombatDropState.cs"] = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["_unknownFloat1"] = Opaque,
            ["_unknownFloat2"] = Opaque,
            ["_unknownFloat3"] = Opaque,
        },
    };

    [Fact]
    public void PlainPersistSites_InSaveStream_AreAllowlisted()
    {
        var root = FindRepositoryRoot();
        var violations = new List<string>();

        foreach (var site in FindAllPlainSites(root))
        {
            if (!IsAllowlisted(site.File, site.Field))
            {
                violations.Add($"{site.File}:{site.Line}: {site.Method}(ref {site.Field})");
            }
        }

        Assert.True(
            violations.Count == 0,
            $"{Guidance}\nUnclassified plain persist site(s):\n{string.Join('\n', violations)}");
    }

    [Fact]
    public void Allowlist_EntriesAreLiveAndReasoned()
    {
        var live = FindAllPlainSites(FindRepositoryRoot())
            .Select(site => (site.File, site.Field))
            .ToHashSet();

        var entries = Allowlist.SelectMany(file => file.Value.Select(field => (File: file.Key, Field: field.Key, Reason: field.Value))).ToList();
        var stale = entries.Where(entry => !live.Contains((entry.File, entry.Field))).Select(entry => $"{entry.File}: {entry.Field}").ToList();
        var blankReasons = entries.Where(entry => string.IsNullOrWhiteSpace(entry.Reason)).Select(entry => $"{entry.File}: {entry.Field}").ToList();

        Assert.True(
            stale.Count == 0,
            $"Allowlist entries with no plain persist call (converted to PersistLength* or renamed; remove them):\n{string.Join('\n', stale)}");
        Assert.True(
            blankReasons.Count == 0,
            $"Allowlist entries without a classification reason:\n{string.Join('\n', blankReasons)}");
    }

    [Theory]
    [InlineData("persister.PersistSingle(ref _x);", 1, "_x")]
    [InlineData("reader.PersistVector3Value(ref item);", 1, "item")]
    [InlineData("persister.PersistPoint3DValue(ref p);", 1, "p")]
    [InlineData("persister.PersistMatrix4x3(ref m, readVersion: false);", 1, "m")]
    [InlineData("persister.PersistSingleValue(ref item);", 1, "item")]
    [InlineData("persister.PersistLengthSingle(ref _x);", 0, "")]
    [InlineData("persister.PersistLengthMatrix4x3Value(ref m, readVersion: false);", 0, "")]
    [InlineData("persister.PersistFrame(ref frame);", 0, "")]
    [InlineData("// persister.PersistSingle(ref _x);", 0, "")]
    [InlineData("var s = \"PersistSingle(ref _x)\";", 0, "")]
    public void Detector_MatchesPlainPersistCallsOutsideCommentsAndStrings(string source, int expectedHits, string expectedField)
    {
        var hits = FindPlainSites(source);

        Assert.Equal(expectedHits, hits.Count);
        Assert.All(hits, hit => Assert.Equal(expectedField, hit.Field));
    }

    private static bool IsAllowlisted(string file, string field)
    {
        return Allowlist.TryGetValue(file, out var fields) && fields.ContainsKey(field);
    }

    private static List<(string File, int Line, string Method, string Field)> FindAllPlainSites(string root)
    {
        var sites = new List<(string File, int Line, string Method, string Field)>();
        foreach (var scanRoot in ScanRoots)
        {
            foreach (var file in Directory.EnumerateFiles(Path.Combine(root, scanRoot), "*.cs", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                if (IsBuildOutput(relative))
                {
                    continue;
                }

                foreach (var hit in FindPlainSites(File.ReadAllText(file)))
                {
                    sites.Add((relative, hit.Line, hit.Method, hit.Field));
                }
            }
        }

        return sites;
    }

    private static IReadOnlyList<(int Line, string Method, string Field)> FindPlainSites(string source)
    {
        var stripped = BlankCommentsAndLiterals(source);
        var sites = new List<(int Line, string Method, string Field)>();
        foreach (Match match in PlainPersistCall.Matches(stripped))
        {
            var line = stripped.Take(match.Index).Count(c => c == '\n') + 1;
            sites.Add((line, match.Groups["method"].Value, match.Groups["field"].Value.Trim()));
        }

        return sites;
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
