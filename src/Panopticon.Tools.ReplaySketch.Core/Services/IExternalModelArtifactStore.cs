using System;

namespace Panopticon.Tools.ReplaySketch.Services;

/// <summary>
/// Handle to one stored preview artifact. Disposing releases the session's claim; the
/// artifact bytes remain readable through the store until the store itself is disposed.
/// </summary>
public interface IExternalModelArtifactLease : IDisposable
{
    string ArtifactId { get; }
}

/// <summary>
/// Session-owned preview artifact storage. One store owns exactly one temporary
/// directory; disposing the store deletes that directory and nothing else. Failed
/// preview requests keep their stored report until the next request replaces it.
/// </summary>
public interface IExternalModelArtifactStore : IDisposable
{
    /// <summary>Creates a new artifact lease; at most one lease is active per store.</summary>
    IExternalModelArtifactLease CreateLease();
}
