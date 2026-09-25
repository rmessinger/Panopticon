#nullable enable

using System;
using Panopticon.Content.Importing;
using Panopticon.Tools.ReplaySketch.Model;

namespace Panopticon.Tools.ReplaySketch.Services;

/// <summary>
/// One mounted external source visible to the session: its root virtual key, explicit
/// format selection with the public inspector facade, and its isolated import context.
/// </summary>
public interface IExternalSourceMount : IDisposable
{
    string RootVirtualKey { get; }

    ExternalFormatSelection FormatSelection { get; }

    AssetImportContext ImportContext { get; }
}

/// <summary>
/// Creates source mounts for the session. Production routes through the engine's
/// ExternalAssetSourceMount with importer options mapped from the session's
/// ExternalModelImportOptions; Core tests substitute fakes without touching disk.
/// </summary>
public interface IExternalAssetSourceMountFactory
{
    /// <summary>
    /// Mounts one absolute selected file under the given importer options. Throws
    /// InvalidDataException (EXT001) for unsupported formats; other creation failures
    /// throw InvalidDataException as well.
    /// </summary>
    IExternalSourceMount Create(string absoluteSelectedFile, ExternalModelImportOptions options);
}
