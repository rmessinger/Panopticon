#nullable enable

using System;

namespace OpenSage.Content.Importing;

public enum ImportedImageSourceKind
{
    VirtualPath,
    EmbeddedBytes,
}

/// <summary>
/// Immutable image source: either an exact normalized virtual-file path resolved by the importer,
/// or a private copy of encoded bytes with a MIME type and diagnostic name. Raw GPU textures and
/// parser objects are intentionally not representable.
/// </summary>
public sealed class ImportedImageSource
{
    private readonly byte[]? _embeddedBytes;

    public ImportedImageSourceKind Kind { get; }

    public string VirtualPath => Kind == ImportedImageSourceKind.VirtualPath
        ? _path!
        : throw new InvalidOperationException("Image source is not a virtual path.");

    public ReadOnlyMemory<byte> EmbeddedBytes => Kind == ImportedImageSourceKind.EmbeddedBytes
        ? _embeddedBytes!
        : throw new InvalidOperationException("Image source is not embedded bytes.");

    public string MimeType { get; }

    public string DiagnosticName { get; }

    private readonly string? _path;

    private ImportedImageSource(ImportedImageSourceKind kind, string? path, byte[]? embeddedBytes, string mimeType, string diagnosticName)
    {
        Kind = kind;
        _path = path;
        _embeddedBytes = embeddedBytes;
        MimeType = mimeType;
        DiagnosticName = diagnosticName;
    }

    public static ImportedImageSource CreateVirtualPath(string virtualPath)
    {
        ValidateVirtualPath(virtualPath);

        return new ImportedImageSource(ImportedImageSourceKind.VirtualPath, virtualPath, null, string.Empty, virtualPath);
    }

    public static ImportedImageSource CreateEmbeddedBytes(byte[] bytes, string mimeType, string diagnosticName)
    {
        if (bytes is null || bytes.Length == 0)
        {
            throw new ArgumentException("Embedded image bytes must not be null or empty.", nameof(bytes));
        }

        if (string.IsNullOrEmpty(mimeType))
        {
            throw new ArgumentException("MIME type must not be null or empty.", nameof(mimeType));
        }

        if (string.IsNullOrEmpty(diagnosticName))
        {
            throw new ArgumentException("Diagnostic name must not be null or empty.", nameof(diagnosticName));
        }

        return new ImportedImageSource(ImportedImageSourceKind.EmbeddedBytes, null, (byte[])bytes.Clone(), mimeType, diagnosticName);
    }

    internal static void ValidateVirtualPath(string virtualPath)
    {
        if (string.IsNullOrEmpty(virtualPath))
        {
            throw new ArgumentException("Virtual path must not be null or empty.", nameof(virtualPath));
        }

        if (virtualPath.Contains('\\')
            || virtualPath.StartsWith('/')
            || virtualPath.Split('/').Contains(".")
            || virtualPath.Split('/').Contains(".."))
        {
            throw new ArgumentException(
                $"Virtual path '{virtualPath}' is not normalized: use forward slashes, no leading slash, no '.' or '..' segments.",
                nameof(virtualPath));
        }
    }
}
