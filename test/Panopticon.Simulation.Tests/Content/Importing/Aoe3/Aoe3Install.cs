using System;
using System.IO;
using Panopticon.IO;

namespace Panopticon.Tests.Content.Importing.Aoe3;

/// <summary>
/// Resolves the AoE3 DE install for Interactive tests: AOE3DE_PATH points at
/// the install root containing Game.
/// </summary>
internal static class Aoe3Install
{
    public static string GetGameDirectory()
    {
        var root = Environment.GetEnvironmentVariable("AOE3DE_PATH");
        if (string.IsNullOrEmpty(root))
        {
            throw new InvalidOperationException(
                "Interactive test requires AOE3DE_PATH pointing at an Age of Empires III DE install root (containing Game).");
        }

        var gameDirectory = Path.Combine(root, "Game");
        if (!Directory.Exists(gameDirectory))
        {
            throw new InvalidOperationException(
                $"Interactive test requires the AoE3 DE Game directory at '{gameDirectory}'.");
        }

        return gameDirectory;
    }

    public static BarFileSystem CreateFileSystem()
    {
        return new BarFileSystem(GetGameDirectory());
    }
}
