using System;
using System.IO;

namespace OpenSage.Tools.ReplaySketch.Services;

public static class ReplaysPathResolver
{
    /// <summary>
    /// Returns the path to the Generals replays directory, creating it if absent.
    /// Typically: <c>%USERPROFILE%\Documents\Command and Conquer Generals Data\Replays</c>
    /// </summary>
    public static string Resolve()
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Command and Conquer Generals Data",
            "Replays");

        Directory.CreateDirectory(path);
        return path;
    }
}
