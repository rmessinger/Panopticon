#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using OpenSage.IO;

namespace OpenSage.Content.Importing.Gltf;

/// <summary>
/// Resolves one import's external buffers, images, and data URIs against the root document's
/// virtual directory. Reads only through <see cref="AssetImportContext.FileSystem"/>, enforces
/// per-resource and aggregate byte limits, and shares one immutable payload per normalized
/// reference.
/// </summary>
internal sealed class GltfResourceResolver
{
    private const string DataUriScheme = "data:";
    private const string DataUriBase64Marker = ";base64";

    private static readonly HashSet<string> AllowedDataUriMediaTypes =
    [
        "image/png",
        "image/jpeg",
        "application/octet-stream",
        "application/gltf-buffer",
    ];

    private readonly string _rootDocumentPath;
    private readonly string _rootDirectory;
    private readonly FileSystem _fileSystem;
    private readonly GltfImportOptions _options;

    private readonly Dictionary<string, byte[]> _payloadCache = new(StringComparer.Ordinal);
    private long _aggregateExternalBytes;

    public GltfResourceResolver(string rootDocumentPath, FileSystem fileSystem, GltfImportOptions options)
    {
        if (string.IsNullOrWhiteSpace(rootDocumentPath))
        {
            throw new ArgumentException("Root document path must not be null or empty.", nameof(rootDocumentPath));
        }

        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(options);

        _rootDocumentPath = rootDocumentPath;
        _rootDirectory = Path.GetDirectoryName(rootDocumentPath) ?? string.Empty;
        _fileSystem = fileSystem;
        _options = options;
    }

    /// <summary>
    /// Normalized virtual path of an external (non data-URI) reference, or null for data URIs.
    /// Throws for references that would leave the virtual root.
    /// </summary>
    public string? TryGetExternalPath(string uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        return IsDataUri(uri)
            ? null
            : NormalizeExternalPath(uri);
    }

    public byte[] Resolve(string uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        if (IsDataUri(uri))
        {
            return ResolveDataUri(uri);
        }

        var normalizedPath = NormalizeExternalPath(uri);
        if (_payloadCache.TryGetValue(normalizedPath, out var cached))
        {
            return cached;
        }

        var entry = _fileSystem.GetFile(normalizedPath);
        if (entry is null)
        {
            throw new InvalidDataException(
                $"glTF document '{_rootDocumentPath}' references external resource '{normalizedPath}', which does not exist.");
        }

        if (entry.Length > _options.MaxExternalResourceBytes)
        {
            throw new InvalidDataException(
                $"glTF external resource '{normalizedPath}' is {entry.Length} bytes; the per-resource limit is {_options.MaxExternalResourceBytes} bytes.");
        }

        if (_aggregateExternalBytes + entry.Length > _options.MaxAggregateExternalResourceBytes)
        {
            throw new InvalidDataException(
                $"glTF external resources for '{_rootDocumentPath}' exceed the aggregate limit of {_options.MaxAggregateExternalResourceBytes} bytes.");
        }

        byte[] payload;
        using (var stream = entry.Open())
        using (var bounded = new MemoryStream(capacity: (int)entry.Length))
        {
            stream.CopyTo(bounded);
            payload = bounded.ToArray();
        }

        _aggregateExternalBytes += payload.Length;
        _payloadCache.Add(normalizedPath, payload);
        return payload;
    }

    private static bool IsDataUri(string uri)
    {
        return uri.StartsWith(DataUriScheme, StringComparison.OrdinalIgnoreCase);
    }

    private byte[] ResolveDataUri(string uri)
    {
        if (_payloadCache.TryGetValue(uri, out var cached))
        {
            return cached;
        }

        var commaIndex = uri.IndexOf(',');
        if (commaIndex < 0)
        {
            throw new InvalidDataException($"glTF data URI in '{_rootDocumentPath}' has no comma separator.");
        }

        var metadata = uri[DataUriScheme.Length..commaIndex];
        var payloadText = uri[(commaIndex + 1)..];

        string mediaType;
        bool isBase64;
        if (metadata.EndsWith(DataUriBase64Marker, StringComparison.OrdinalIgnoreCase))
        {
            isBase64 = true;
            mediaType = metadata[..^DataUriBase64Marker.Length];
        }
        else
        {
            isBase64 = false;
            mediaType = metadata;
        }

        if (!AllowedDataUriMediaTypes.Contains(mediaType))
        {
            throw new InvalidDataException(
                $"glTF data URI in '{_rootDocumentPath}' uses unsupported media type '{mediaType}'.");
        }

        byte[] payload;
        try
        {
            payload = isBase64
                ? Convert.FromBase64String(payloadText)
                : DecodePercentEncoded(payloadText);
        }
        catch (FormatException ex)
        {
            throw new InvalidDataException(
                $"glTF data URI in '{_rootDocumentPath}' contains malformed base64 data.", ex);
        }

        if (payload.Length > _options.MaxDataUriBytes)
        {
            throw new InvalidDataException(
                $"glTF data URI in '{_rootDocumentPath}' decodes to {payload.Length} bytes; the limit is {_options.MaxDataUriBytes} bytes.");
        }

        _payloadCache.Add(uri, payload);
        return payload;
    }

    private static byte[] DecodePercentEncoded(string text)
    {
        var result = new List<byte>(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '%')
            {
                result.Add((byte)text[i]);
                continue;
            }

            if (i + 2 >= text.Length ||
                !byte.TryParse(text.Substring(i + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
            {
                throw new InvalidDataException("Malformed percent escape in glTF data URI.");
            }

            result.Add(value);
            i += 2;
        }

        return [.. result];
    }

    private string NormalizeExternalPath(string uri)
    {
        if (uri.Length == 0)
        {
            throw new InvalidDataException(
                $"glTF document '{_rootDocumentPath}' contains an empty external resource reference.");
        }

        if (uri.Contains("://", StringComparison.Ordinal) ||
            (uri.Length > 1 && uri[1] == ':' && uri[0] is not '/' and not '\\'))
        {
            throw new InvalidDataException(
                $"glTF document '{_rootDocumentPath}' references '{uri}', which uses an unsupported non-data URI scheme.");
        }

        if (uri.Contains("%2f", StringComparison.OrdinalIgnoreCase) ||
            uri.Contains("%5c", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"glTF document '{_rootDocumentPath}' references '{uri}', which contains an encoded path separator.");
        }

        var decoded = Uri.UnescapeDataString(uri);

        if (decoded.Length > 0 && (decoded[0] == '/' || decoded[0] == '\\'))
        {
            throw new InvalidDataException(
                $"glTF document '{_rootDocumentPath}' references '{uri}', which is a rooted path.");
        }

        if (decoded.Contains('%'))
        {
            throw new InvalidDataException(
                $"glTF document '{_rootDocumentPath}' references '{uri}', which contains a malformed percent escape.");
        }

        var combined = _rootDirectory.Length == 0
            ? decoded.Replace('\\', '/')
            : (_rootDirectory.Replace('\\', '/') + "/" + decoded.Replace('\\', '/'));

        var segments = combined.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var stack = new List<string>(segments.Length);
        foreach (var segment in segments)
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (stack.Count == 0)
                {
                    throw new InvalidDataException(
                        $"glTF document '{_rootDocumentPath}' references '{uri}', which escapes the virtual root.");
                }

                stack.RemoveAt(stack.Count - 1);
                continue;
            }

            stack.Add(segment);
        }

        if (stack.Count == 0)
        {
            throw new InvalidDataException(
                $"glTF document '{_rootDocumentPath}' references '{uri}', which escapes the virtual root.");
        }

        return string.Join("/", stack);
    }
}
