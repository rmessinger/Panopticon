#nullable enable

using System;
using System.IO;

namespace Panopticon.Tools.ReplaySketch.Services;

/// <summary>
/// Production preview artifact store: one session-owned directory under the application
/// cache root. Disposal deletes that directory only. Each lease addresses fixed artifact
/// file names inside it, so consecutive requests replace artifacts in place and a failed
/// request's report survives until the next request overwrites it.
/// </summary>
public sealed class ExternalModelArtifactStore : IExternalModelArtifactStore
{
    private readonly string _directory;

    public ExternalModelArtifactStore()
    {
        _directory = Path.Combine(
            PlatformPaths.CacheBase,
            "external-model-previews",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(_directory);
    }

    public string DirectoryPath => _directory;

    public IExternalModelArtifactLease CreateLease()
    {
        return new Lease(Guid.NewGuid().ToString("N"));
    }

    public string GetCapturePath(IExternalModelArtifactLease lease)
    {
        return Path.Combine(_directory, "preview.png");
    }

    public string GetReportPath(IExternalModelArtifactLease lease)
    {
        return Path.Combine(_directory, "report.json");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
        catch (IOException)
        {
            // Best effort cleanup of a cache directory; never fatal on dispose.
        }
    }

    private sealed class Lease(string artifactId) : IExternalModelArtifactLease
    {
        public string ArtifactId { get; } = artifactId;

        public void Dispose()
        {
        }
    }
}
