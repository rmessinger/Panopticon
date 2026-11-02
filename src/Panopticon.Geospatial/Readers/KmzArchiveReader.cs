using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml;
using Panopticon.Geospatial.Interchange;

namespace Panopticon.Geospatial.Readers;

/// <summary>
/// Opens a seekable KMZ read-only and imports its KML through <see cref="KmlReader"/>; entries
/// are never extracted to disk. Entry count is enforced by a bounded central-directory preflight
/// before <see cref="ZipArchive"/> materializes any header; entry names, per-entry and total
/// expanded size, and compression ratio are validated from metadata before an entry is opened,
/// and reads are byte-counted against declared sizes. Every KML entry passes a reference
/// preflight that rejects NetworkLink elements and remote or escaping references. The selected
/// entry is recorded as the <c>archiveEntry</c> attribute on each feature.
/// </summary>
public sealed class KmzArchiveReader : IVectorSourceReader
{
    private const string RootDocumentName = "doc.kml";

    private static readonly HashSet<string> ReferenceElementNames = ["href", "targetHref", "sourceHref", "styleUrl"];

    /// <summary>
    /// Reads the KMZ file at <paramref name="path"/>. The file name without extension is used as
    /// the feature source id.
    /// </summary>
    public IReadOnlyList<GeospatialFeature> Read(string path, GeospatialImportLimits limits, GeoDiagnosticBag diagnostics)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(diagnostics);

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096);

            return Read(stream, Path.GetFileNameWithoutExtension(path), limits, diagnostics, path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(new GeoDiagnostic(
                GeoDiagnosticCodes.ArchiveFailure,
                GeoStage.Archive,
                GeoSeverity.Error,
                $"KMZ file could not be read: {ex.Message}",
                path,
                ex));

            return [];
        }
    }

    /// <summary>
    /// Reads a KMZ from an open, readable, seekable stream. The stream is not disposed;
    /// diagnostics carry the qualified <c>path!entry</c> source path when
    /// <paramref name="sourcePath"/> is given.
    /// </summary>
    public IReadOnlyList<GeospatialFeature> Read(
        Stream stream,
        string sourceId,
        GeospatialImportLimits limits,
        GeoDiagnosticBag diagnostics,
        string? sourcePath = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourceId);
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(diagnostics);

        var context = new ArchiveContext(sourceId, sourcePath, limits, diagnostics);

        try
        {
            // ZipArchive would buffer the whole untrusted archive on a non-seekable stream.
            if (!stream.CanSeek)
            {
                context.AddFailure("KMZ stream is not seekable; the reader does not buffer an untrusted archive into memory.");

                return [];
            }

            if (!ZipArchivePreflight.TryValidate(stream, limits, sourcePath, diagnostics))
            {
                return [];
            }

            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);

            if (!context.TryValidateMetadata(archive) || !context.TryPreflightReferences())
            {
                return [];
            }

            return context.ImportSelectedKml();
        }
        catch (EntryLimitException ex)
        {
            context.AddError(GeoDiagnosticCodes.ArchiveLimitExceeded, ex.Message);

            return [];
        }
        catch (OverflowException)
        {
            context.AddError(GeoDiagnosticCodes.ArchiveLimitExceeded, "KMZ entry declared sizes overflow the total expanded size calculation.");

            return [];
        }
        catch (Exception ex) when (ex is InvalidDataException or NotSupportedException)
        {
            context.AddFailure($"KMZ archive could not be opened: {ex.Message}", ex);

            return [];
        }
        catch (IOException ex)
        {
            context.AddFailure($"KMZ archive stream could not be read: {ex.Message}", ex);

            return [];
        }
    }

    private sealed class EntryLimitException(string message) : Exception(message);

    private readonly record struct ReferenceFinding(string Code, string Message, Exception? Detail = null);

    private static string PastDeclaredSizeMessage(string entryName, long declaredBytes)
    {
        return $"KMZ entry '{entryName}' expanded past its declared size of {declaredBytes.ToString(CultureInfo.InvariantCulture)} bytes.";
    }

    private static string DeclaredSizeMismatchMessage(string entryName, long actualBytes, long declaredBytes)
    {
        return $"KMZ entry '{entryName}' expanded to {actualBytes.ToString(CultureInfo.InvariantCulture)} bytes, which does not match its declared size of {declaredBytes.ToString(CultureInfo.InvariantCulture)} bytes.";
    }

    private sealed class ArchiveContext(
        string sourceId,
        string? sourcePath,
        GeospatialImportLimits limits,
        GeoDiagnosticBag diagnostics)
    {
        private readonly string _sourceId = sourceId;
        private readonly string? _sourcePath = sourcePath;
        private readonly GeospatialImportLimits _limits = limits;
        private readonly GeoDiagnosticBag _diagnostics = diagnostics;

        private readonly List<ZipArchiveEntry> _kmlEntries = [];
        private ZipArchiveEntry? _rootDocument;
        private ZipArchiveEntry? _firstKml;
        private byte[]? _selectedBytes;

        private ZipArchiveEntry? SelectedEntry => _rootDocument ?? _firstKml;

        internal bool TryValidateMetadata(ZipArchive archive)
        {
            if (archive.Entries.Count > _limits.MaxKmzEntryCount)
            {
                AddError(
                    GeoDiagnosticCodes.ArchiveLimitExceeded,
                    $"KMZ entry count {archive.Entries.Count.ToString(CultureInfo.InvariantCulture)} exceeds the limit of {_limits.MaxKmzEntryCount.ToString(CultureInfo.InvariantCulture)}.");

                return false;
            }

            var seenNames = new HashSet<string>(StringComparer.Ordinal);
            var totalExpanded = 0L;

            foreach (var entry in archive.Entries)
            {
                // Every limit below is decided from central-directory metadata, before any entry
                // stream is opened during the reference preflight.
                var normalized = NormalizeEntryName(entry.FullName, out var isDirectory, out var unsafeNameReason);

                if (unsafeNameReason is null && normalized.Length == 0)
                {
                    unsafeNameReason = "the name normalizes to an empty path";
                }

                if (unsafeNameReason is not null)
                {
                    AddError(
                        GeoDiagnosticCodes.ArchiveUnsafeEntryName,
                        $"KMZ entry name '{entry.FullName}' is rejected: {unsafeNameReason}.");

                    return false;
                }

                if (!seenNames.Add(normalized))
                {
                    AddError(
                        GeoDiagnosticCodes.ArchiveUnsafeEntryName,
                        $"KMZ entry name '{entry.FullName}' duplicates '{normalized}' after path normalization.");

                    return false;
                }

                if (entry.Length > _limits.MaxKmzExpandedBytes)
                {
                    AddError(
                        GeoDiagnosticCodes.ArchiveLimitExceeded,
                        $"KMZ entry '{entry.FullName}' declares {entry.Length.ToString(CultureInfo.InvariantCulture)} expanded bytes, over the per-entry limit of {_limits.MaxKmzExpandedBytes.ToString(CultureInfo.InvariantCulture)}.");

                    return false;
                }

                totalExpanded = checked(totalExpanded + entry.Length);

                if (totalExpanded > _limits.MaxKmzExpandedBytes)
                {
                    AddError(
                        GeoDiagnosticCodes.ArchiveLimitExceeded,
                        $"KMZ entries declare {totalExpanded.ToString(CultureInfo.InvariantCulture)} expanded bytes in total, over the limit of {_limits.MaxKmzExpandedBytes.ToString(CultureInfo.InvariantCulture)}.");

                    return false;
                }

                if (entry.Length > 0 && entry.CompressedLength == 0)
                {
                    AddError(
                        GeoDiagnosticCodes.ArchiveLimitExceeded,
                        $"KMZ entry '{entry.FullName}' declares {entry.Length.ToString(CultureInfo.InvariantCulture)} expanded bytes in 0 compressed bytes; the archive metadata is invalid.");

                    return false;
                }

                var compressionRatio = entry.Length / (double)entry.CompressedLength;

                if (entry.CompressedLength > 0 && compressionRatio > _limits.MaxKmzCompressionRatio)
                {
                    AddError(
                        GeoDiagnosticCodes.ArchiveLimitExceeded,
                        $"KMZ entry '{entry.FullName}' declares a compression ratio of {compressionRatio.ToString("0.#", CultureInfo.InvariantCulture)}, over the limit of {_limits.MaxKmzCompressionRatio.ToString(CultureInfo.InvariantCulture)}.");

                    return false;
                }

                if (isDirectory || !IsKmlName(normalized))
                {
                    continue;
                }

                if (entry.Length > _limits.MaxKmlEntryBytes)
                {
                    AddError(
                        GeoDiagnosticCodes.ArchiveLimitExceeded,
                        $"KMZ entry '{entry.FullName}' declares {entry.Length.ToString(CultureInfo.InvariantCulture)} expanded bytes, over the KML entry limit of {_limits.MaxKmlEntryBytes.ToString(CultureInfo.InvariantCulture)}.");

                    return false;
                }

                _kmlEntries.Add(entry);

                // Only a root-level doc.kml wins over archive order; entries inside directories
                // fall back to the first-KML rule.
                if (_rootDocument is null
                    && !normalized.Contains('/')
                    && string.Equals(normalized, RootDocumentName, StringComparison.OrdinalIgnoreCase))
                {
                    _rootDocument = entry;
                }

                _firstKml ??= entry;
            }

            return true;
        }

        internal bool TryPreflightReferences()
        {
            // Every KML entry is scanned, not just the selected one, so an unsafe reference cannot
            // hide in an unselected document. The selected entry's scan doubles as its bounded
            // read: its bytes are cached and reused, so nothing is inflated twice.
            foreach (var entry in _kmlEntries)
            {
                var isSelected = ReferenceEquals(entry, SelectedEntry);
                using Stream content = isSelected ? SelectedEntryContent(entry) : new DeclaredSizeStream(entry);
                var finding = ScanEntryReferences(content, entry, isSelected);

                if (finding is { } referenceFinding)
                {
                    AddError(referenceFinding.Code, referenceFinding.Message, referenceFinding.Detail);

                    return false;
                }
            }

            return true;
        }

        private MemoryStream SelectedEntryContent(ZipArchiveEntry entry)
        {
            _selectedBytes = ReadEntryBytes(entry);

            return new MemoryStream(_selectedBytes);
        }

        internal IReadOnlyList<GeospatialFeature> ImportSelectedKml()
        {
            var selected = SelectedEntry;

            if (selected is null)
            {
                AddError(GeoDiagnosticCodes.ArchiveFailure, "KMZ archive contains no KML entry.");

                return [];
            }

            var bytes = _selectedBytes ?? ReadEntryBytes(selected);
            var qualifiedSourcePath = _sourcePath is null ? selected.FullName : _sourcePath + "!" + selected.FullName;

            using var kmlStream = new MemoryStream(bytes);
            var features = new KmlReader().Read(kmlStream, _sourceId, _limits, _diagnostics, qualifiedSourcePath);

            return [.. features.Select(feature => WithArchiveEntry(feature, selected.FullName))];
        }

        private ReferenceFinding? ScanEntryReferences(Stream content, ZipArchiveEntry entry, bool isSelected)
        {
            try
            {
                var settings = new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    MaxCharactersInDocument = _limits.MaxKmlEntryBytes,
                    IgnoreComments = true,
                    IgnoreProcessingInstructions = true,
                    IgnoreWhitespace = true,
                };

                using var reader = XmlReader.Create(content, settings);

                // Advance contract: a reference element consumes itself through
                // ReadElementContentAsString, which leaves the reader on the following node, so
                // the loop must not read again in that case; reading unconditionally would skip
                // the sibling after every reference element. The scan is namespace-agnostic
                // because KMZ is stricter than plain KML.
                while (!reader.EOF)
                {
                    if (reader.NodeType == XmlNodeType.Element)
                    {
                        if (reader.LocalName == "NetworkLink")
                        {
                            return new ReferenceFinding(
                                GeoDiagnosticCodes.ArchiveUnsafeReference,
                                $"KMZ entry '{entry.FullName}' contains a NetworkLink; KMZ archives do not load linked documents.");
                        }

                        if (ReferenceElementNames.Contains(reader.LocalName))
                        {
                            var finding = CheckReferenceElement(reader, entry);

                            if (finding is not null)
                            {
                                return finding;
                            }

                            continue;
                        }
                    }

                    reader.Read();
                }

                return null;
            }
            catch (XmlException ex)
            {
                if (isSelected)
                {
                    // KmlReader reports the parse error for the selected entry when it imports.
                    return null;
                }

                return new ReferenceFinding(
                    GeoDiagnosticCodes.ArchiveFailure,
                    $"KMZ entry '{entry.FullName}' is malformed and its references cannot be verified.",
                    ex);
            }
        }

        private static ReferenceFinding? CheckReferenceElement(XmlReader reader, ZipArchiveEntry entry)
        {
            string text;

            try
            {
                text = reader.ReadElementContentAsString();
            }
            catch (Exception ex) when (ex is XmlException or InvalidOperationException)
            {
                return new ReferenceFinding(
                    GeoDiagnosticCodes.ArchiveUnsafeReference,
                    $"KMZ entry '{entry.FullName}' contains unreadable markup in a '{reader.Name}' reference element.");
            }

            var trimmed = text.Trim();

            if (trimmed.Length == 0)
            {
                return null;
            }

            var reason = DescribeUnsafeReference(trimmed);

            return reason is null
                ? null
                : new ReferenceFinding(
                    GeoDiagnosticCodes.ArchiveUnsafeReference,
                    $"KMZ entry '{entry.FullName}' reference '{trimmed}' is rejected: {reason}.");
        }

        private static string? DescribeUnsafeReference(string text)
        {
            var reason = DescribeUnsafeReferencePath(text);

            if (reason is not null)
            {
                return reason;
            }

            // Percent-encoding can hide traversal, roots, and schemes from the raw-path check.
            var current = text;

            for (var level = 0; level < 3; level++)
            {
                string decoded;

                try
                {
                    decoded = Uri.UnescapeDataString(current);
                }
                catch (UriFormatException)
                {
                    return "it contains malformed percent-encoding";
                }

                if (decoded == current)
                {
                    break;
                }

                reason = DescribeUnsafeReferencePath(decoded);

                if (reason is not null)
                {
                    return reason;
                }

                current = decoded;
            }

            return null;
        }

        private static string? DescribeUnsafeReferencePath(string text)
        {
            if (text.Contains('\0'))
            {
                return "it contains a null character";
            }

            var path = text;
            var fragmentIndex = path.IndexOf('#');

            if (fragmentIndex >= 0)
            {
                path = path[..fragmentIndex];
            }

            if (path.Length == 0)
            {
                return null;
            }

            if (path.Contains('\\'))
            {
                return "backslash separators are not permitted";
            }

            if (path.Contains(':'))
            {
                return "absolute paths and URL schemes (http, https, ftp, file, drive letters) are not permitted";
            }

            if (path[0] == '/')
            {
                return "the path is rooted";
            }

            if (path.Split('/').Contains(".."))
            {
                return "the path contains a parent-relative '..' segment";
            }

            return null;
        }

        private static string NormalizeEntryName(string fullName, out bool isDirectory, out string? unsafeReason)
        {
            isDirectory = fullName.EndsWith('/');
            unsafeReason = null;

            if (fullName.Length == 0)
            {
                unsafeReason = "the name is empty";

                return string.Empty;
            }

            if (fullName.Contains('\0'))
            {
                unsafeReason = "null characters are not permitted";

                return string.Empty;
            }

            if (fullName.Contains('\\'))
            {
                unsafeReason = "backslashes are not permitted";

                return string.Empty;
            }

            if (fullName.Contains(':'))
            {
                unsafeReason = "colons (drive letters, UNC roots, URL schemes) are not permitted";

                return string.Empty;
            }

            if (fullName[0] == '/')
            {
                unsafeReason = "the path is rooted";

                return string.Empty;
            }

            var segments = new List<string>();

            foreach (var segment in fullName.Split('/'))
            {
                if (segment.Length == 0 || segment == ".")
                {
                    continue;
                }

                if (segment == "..")
                {
                    unsafeReason = "the path contains a parent-relative '..' segment";

                    return string.Empty;
                }

                segments.Add(segment);
            }

            return string.Join("/", segments);
        }

        private static bool IsKmlName(string normalizedEntryName)
        {
            var lastSegment = normalizedEntryName[(normalizedEntryName.LastIndexOf('/') + 1)..];

            return lastSegment.EndsWith(".kml", StringComparison.OrdinalIgnoreCase);
        }

        // The declared length is untrusted metadata; the actual byte count defends the limit.
        private byte[] ReadEntryBytes(ZipArchiveEntry entry)
        {
            using var entryStream = entry.Open();
            using var buffered = new MemoryStream((int)Math.Min(entry.Length, int.MaxValue));
            var chunk = new byte[64 * 1024];
            long total = 0;

            while (true)
            {
                var read = entryStream.Read(chunk, 0, chunk.Length);

                if (read == 0)
                {
                    break;
                }

                total += read;

                if (total > entry.Length)
                {
                    throw new EntryLimitException(PastDeclaredSizeMessage(entry.FullName, entry.Length));
                }

                buffered.Write(chunk, 0, read);
            }

            if (total != entry.Length)
            {
                throw new EntryLimitException(DeclaredSizeMismatchMessage(entry.FullName, total, entry.Length));
            }

            return buffered.ToArray();
        }

        /// <summary>Streaming counterpart of <see cref="ReadEntryBytes"/> for entries that are scanned, not cached.</summary>
        private sealed class DeclaredSizeStream(ZipArchiveEntry entry) : Stream
        {
            private readonly Stream _inner = entry.Open();
            private long _totalRead;

            public override bool CanRead => true;

            public override bool CanSeek => false;

            public override bool CanWrite => false;

            public override long Length => throw new NotSupportedException();

            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                var read = _inner.Read(buffer, offset, count);

                if (read == 0)
                {
                    if (_totalRead != entry.Length)
                    {
                        throw new EntryLimitException(DeclaredSizeMismatchMessage(entry.FullName, _totalRead, entry.Length));
                    }

                    return 0;
                }

                _totalRead += read;

                if (_totalRead > entry.Length)
                {
                    throw new EntryLimitException(PastDeclaredSizeMessage(entry.FullName, entry.Length));
                }

                return read;
            }

            public override void Flush()
            {
                throw new NotSupportedException();
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotSupportedException();
            }

            public override void SetLength(long value)
            {
                throw new NotSupportedException();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new NotSupportedException();
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    _inner.Dispose();
                }

                base.Dispose(disposing);
            }
        }

        private static GeospatialFeature WithArchiveEntry(GeospatialFeature feature, string entryName)
        {
            var attributes = new Dictionary<string, string>(feature.Attributes) { ["archiveEntry"] = entryName };

            return new GeospatialFeature(
                feature.SourceId,
                feature.FeatureId,
                feature.Geometry,
                feature.CoordinateSystem,
                attributes,
                feature.AltitudeMode);
        }

        internal void AddError(string code, string message, Exception? detail = null)
        {
            _diagnostics.Add(new GeoDiagnostic(code, GeoStage.Archive, GeoSeverity.Error, message, _sourcePath, detail));
        }

        internal void AddFailure(string message, Exception? detail = null)
        {
            AddError(GeoDiagnosticCodes.ArchiveFailure, message, detail);
        }
    }
}
