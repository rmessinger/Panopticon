#nullable enable

using System;
using System.IO;

namespace Panopticon.Content.Importing.Gltf;

/// <summary>
/// Converts glTF images into C3 image sources: external images keep the exact normalized
/// virtual path the resolver served them from; data-URI and buffer-view images become copied
/// encoded bytes with MIME type and diagnostic name.
/// </summary>
internal static class GltfImageSourceConverter
{
    public static ImportedImageSource Convert(SharpGLTF.Schema2.Image image, GltfResourceResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(resolver);

        var content = image.Content;

        if (content.IsEmpty)
        {
            throw new InvalidDataException(
                $"glTF image '{image.Name}' has no content; the document loader did not resolve it.");
        }

        if (!string.IsNullOrEmpty(content.SourcePath))
        {
            var normalizedPath = resolver.TryGetExternalPath(content.SourcePath);
            if (normalizedPath == null)
            {
                throw new InvalidDataException(
                    $"glTF image '{image.Name}' references '{content.SourcePath}', which does not resolve to a path inside the virtual root.");
            }

            return ImportedImageSource.CreateVirtualPath(normalizedPath);
        }

        var bytes = content.Content.ToArray();

        var mimeType = content.MimeType;
        if (string.IsNullOrEmpty(mimeType))
        {
            if (content.IsPng)
            {
                mimeType = "image/png";
            }
            else if (content.IsJpg)
            {
                mimeType = "image/jpeg";
            }
        }

        if (mimeType is not ("image/png" or "image/jpeg"))
        {
            throw new NotSupportedException(
                $"glTF image '{image.Name}' uses unsupported MIME type '{mimeType}'; only PNG and JPEG are supported.");
        }

        var diagnosticName = string.IsNullOrEmpty(image.Name)
            ? $"gltf image {image.LogicalIndex}"
            : image.Name!;

        return ImportedImageSource.CreateEmbeddedBytes(bytes, mimeType, diagnosticName);
    }
}
