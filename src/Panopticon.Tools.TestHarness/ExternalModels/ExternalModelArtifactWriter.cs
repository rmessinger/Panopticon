#nullable enable

using System;
using System.IO;
using System.Security.Cryptography;

namespace Panopticon.Tools.TestHarness.ExternalModels;

/// <summary>
/// Atomic artifact publication: write to a sibling staging file, flush, close, then replace
/// the final path. The success report is published last; when its replacement fails, a
/// newly published image is removed again so no partial result set survives.
/// </summary>
internal static class ExternalModelArtifactWriter
{
    public const string StagingSuffix = ".staging";

    /// <summary>
    /// Publishes one binary artifact by writing it to a staging file through the given
    /// callback, then atomically replacing the final path.
    /// </summary>
    public static void Publish(string finalPath, Action<string> writeStagingFile)
    {
        if (string.IsNullOrWhiteSpace(finalPath))
        {
            throw new ArgumentException("Artifact path must not be empty.", nameof(finalPath));
        }

        var stagingPath = finalPath + StagingSuffix;
        writeStagingFile(stagingPath);
        ReplaceFile(stagingPath, finalPath);
    }

    /// <summary>Atomically publishes one text artifact (the report).</summary>
    public static void PublishText(string finalPath, string content)
    {
        Publish(finalPath, stagingPath => File.WriteAllText(stagingPath, content));
    }

    /// <summary>
    /// Removes a published image after a failed report publication, leaving no partial
    /// result set. Missing files are not an error.
    /// </summary>
    public static void RemoveImage(string finalImagePath)
    {
        try
        {
            File.Delete(finalImagePath);
        }
        catch (IOException)
        {
        }
    }

    public static string ComputeFileHash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void ReplaceFile(string stagingPath, string finalPath)
    {
        try
        {
            File.Move(stagingPath, finalPath, overwrite: true);
        }
        catch
        {
            try
            {
                File.Delete(stagingPath);
            }
            catch (IOException)
            {
            }

            throw;
        }
    }
}
