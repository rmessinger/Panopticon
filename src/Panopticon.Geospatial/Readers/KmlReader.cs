using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using Panopticon.Geospatial.Interchange;

namespace Panopticon.Geospatial.Readers;

/// <summary>
/// Reads the bounded KML subset (point, line string, polygon with holes, multi geometry,
/// folder structure, and string attributes) into interchange features. Parsing is streaming
/// and bounded: DTDs are prohibited, no XML resolver is installed, and byte, nesting, feature,
/// and coordinate limits are enforced while reading.
/// </summary>
public sealed class KmlReader : IVectorSourceReader
{
    /// <summary>
    /// Reads the KML file at <paramref name="path"/>. The file name without extension is used as
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

            if (stream.Length > limits.MaxKmlEntryBytes)
            {
                diagnostics.Add(ByteLimitDiagnostic(stream.Length, limits.MaxKmlEntryBytes, path));
                return [];
            }

            return Parser.Parse(stream, Path.GetFileNameWithoutExtension(path), limits, diagnostics, path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(new GeoDiagnostic(
                GeoDiagnosticCodes.ParseFailure,
                GeoStage.Parse,
                GeoSeverity.Error,
                $"KML file could not be read: {ex.Message}",
                path,
                ex));
            return [];
        }
    }

    /// <summary>
    /// Reads KML from an open stream so callers such as the KMZ reader can import archive
    /// entries without extracting them to disk. The stream is read forward-only and is not
    /// disposed; byte, character, nesting, feature, and coordinate limits are enforced while
    /// reading.
    /// </summary>
    public IReadOnlyList<GeospatialFeature> Read(
        Stream stream,
        string sourceId,
        GeospatialImportLimits limits,
        GeoDiagnosticBag diagnostics,
        string? sourcePath = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentException.ThrowIfNullOrEmpty(sourceId);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(diagnostics);

        return Parser.Parse(stream, sourceId, limits, diagnostics, sourcePath);
    }

    private static GeoDiagnostic ByteLimitDiagnostic(long lengthBytes, long limitBytes, string sourcePath)
    {
        return new GeoDiagnostic(
            GeoDiagnosticCodes.ParseKmlLimitExceeded,
            GeoStage.Parse,
            GeoSeverity.Error,
            $"KML content of {lengthBytes.ToString(CultureInfo.InvariantCulture)} bytes exceeds the limit of "
                + $"{limitBytes.ToString(CultureInfo.InvariantCulture)} bytes.",
            sourcePath);
    }

    private readonly record struct GeometryResult(GeoGeometry? Geometry, GeoAltitudeMode? AltitudeMode, string? ModeText);

    private sealed class LimitExceededException : Exception;

    private sealed class ByteLimitException(long actualBytes, long limitBytes)
        : Exception(string.Create(
            CultureInfo.InvariantCulture,
            $"KML content of {actualBytes} bytes exceeds the limit of {limitBytes} bytes."))
    {
    }

    /// <summary>
    /// Counts bytes read and aborts once the byte limit is exceeded, so non-seekable and UTF-16
    /// inputs are bounded by bytes, not characters. Never disposes the wrapped stream.
    /// </summary>
    private sealed class BoundedStream(Stream inner, long limitBytes) : Stream
    {
        private long _bytesRead;

        public override bool CanRead => inner.CanRead;

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
            var read = inner.Read(buffer, offset, count);
            _bytesRead += read;

            if (_bytesRead > limitBytes)
            {
                throw new ByteLimitException(_bytesRead, limitBytes);
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
    }

    private sealed class Parser
    {
        // XML nesting is not otherwise bounded; recursive Folder/MultiGeometry descent would
        // overflow the stack, so nesting beyond this depth is a limit error.
        private const int MaxNestingDepth = 128;

        private const string GxNamespace = "http://www.google.com/kml/ext/2.2";

        private static readonly HashSet<string> KmlNamespaces =
        [
            "http://www.opengis.net/kml/2.2",
            "http://earth.google.com/kml/2.0",
            "http://earth.google.com/kml/2.1",
            "http://earth.google.com/kml/2.2",
        ];

        private static readonly SearchValues<char> TupleSeparators = SearchValues.Create(" \t\r\n");

        private readonly string _sourceId;
        private readonly string? _sourcePath;
        private readonly GeospatialImportLimits _limits;
        private readonly GeoDiagnosticBag _diagnostics;
        private readonly List<GeospatialFeature> _features = [];
        private readonly List<string?> _folderNames = [];
        private readonly HashSet<string> _featureIds = new(StringComparer.Ordinal);
        private long _coordinateCount;
        private int _placemarkCount;
        private int _depth;
        private XmlReader _reader = null!;

        private Parser(string sourceId, string? sourcePath, GeospatialImportLimits limits, GeoDiagnosticBag diagnostics)
        {
            _sourceId = sourceId;
            _sourcePath = sourcePath;
            _limits = limits;
            _diagnostics = diagnostics;
        }

        internal static IReadOnlyList<GeospatialFeature> Parse(
            Stream stream,
            string sourceId,
            GeospatialImportLimits limits,
            GeoDiagnosticBag diagnostics,
            string? sourcePath)
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = limits.MaxKmlEntryBytes,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true,
                IgnoreWhitespace = true,
            };

            return new Parser(sourceId, sourcePath, limits, diagnostics).Run(stream, settings);
        }

        private IReadOnlyList<GeospatialFeature> Run(Stream stream, XmlReaderSettings settings)
        {
            try
            {
                // XmlReader.Create eagerly reads for encoding detection, so stream and byte-limit
                // failures can surface here as well as during traversal.
                using var reader = XmlReader.Create(new BoundedStream(stream, _limits.MaxKmlEntryBytes), settings);

                _reader = reader;
                ReadRootElement();
            }
            catch (ByteLimitException ex)
            {
                AddError(GeoDiagnosticCodes.ParseKmlLimitExceeded, ex.Message);
            }
            catch (LimitExceededException)
            {
                // The limit diagnostic was recorded before throwing; partial features are returned.
            }
            catch (XmlException ex)
            {
                AddError(GeoDiagnosticCodes.ParseFailure, $"KML document could not be parsed: {ex.Message}", ex);
            }
            catch (IOException ex)
            {
                AddError(GeoDiagnosticCodes.ParseFailure, $"KML stream could not be read: {ex.Message}", ex);
            }

            return _features.AsReadOnly();
        }

        private void ReadRootElement()
        {
            while (_reader.Read())
            {
                if (_reader.NodeType != XmlNodeType.Element)
                {
                    continue;
                }

                if (IsKmlNamespace(_reader.NamespaceURI) && _reader.LocalName is "kml" or "Document")
                {
                    if (_reader.LocalName == "kml")
                    {
                        ForEachChild(DispatchContainerChild);
                    }
                    else
                    {
                        ReadContainerChildren();
                    }
                }
                else
                {
                    AddError(GeoDiagnosticCodes.ParseFailure, $"Root element '{_reader.Name}' is not a KML document.");
                }

                DrainDocument();

                return;
            }

            AddError(GeoDiagnosticCodes.ParseFailure, "The document contains no XML root element.");
        }

        private void DrainDocument()
        {
            // Trailing content after the root (a second root or stray text) must surface as a parse error.
            while (_reader.Read())
            {
            }
        }

        private void DispatchContainerChild()
        {
            if (_reader.NamespaceURI == GxNamespace)
            {
                if (_reader.LocalName == "Tour")
                {
                    WarnAndSkip("gx:Tour");
                    return;
                }

                SkipSubtree();
                return;
            }

            if (!IsKmlNamespace(_reader.NamespaceURI))
            {
                SkipSubtree();
                return;
            }

            switch (_reader.LocalName)
            {
                case "Document":
                case "Folder":
                    ReadContainerChildren();
                    break;
                case "Placemark":
                    ReadPlacemark();
                    break;
                case "NetworkLinkControl":
                    ReadNetworkLinkControl();
                    break;
                case "NetworkLink":
                    WarnAndSkip("NetworkLink");
                    break;
                case "GroundOverlay":
                    WarnAndSkip("GroundOverlay");
                    break;
                case "ScreenOverlay":
                    WarnAndSkip("ScreenOverlay");
                    break;
                default:
                    SkipSubtree();
                    break;
            }
        }

        private void ReadContainerChildren()
        {
            var isFolder = _reader.LocalName == "Folder";

            if (isFolder)
            {
                _folderNames.Add(null);
            }

            EnterNesting();

            try
            {
                ForEachChild(() =>
                {
                    if (isFolder && _folderNames[^1] is null && IsKmlElement("name"))
                    {
                        // A folder's name element conventionally precedes the placemarks that use it in ids.
                        _folderNames[^1] = ReadTextElement();
                        return;
                    }

                    DispatchContainerChild();
                });
            }
            finally
            {
                ExitNesting();
            }

            if (isFolder)
            {
                _folderNames.RemoveAt(_folderNames.Count - 1);
            }
        }

        private void ReadNetworkLinkControl()
        {
            ForEachChild(() =>
            {
                if (IsKmlElement("Update"))
                {
                    WarnAndSkip("Update");
                    return;
                }

                SkipSubtree();
            });
        }

        private void ReadPlacemark()
        {
            _placemarkCount++;
            var explicitId = _reader.GetAttribute("id");
            var diagnosticsBefore = _diagnostics.Items.Count;
            string? name = null;
            string? styleUrl = null;
            string? altitudeModeText = null;
            GeoAltitudeMode? altitudeMode = null;
            var attributes = new Dictionary<string, string>(StringComparer.Ordinal);
            var geometries = new List<GeoGeometry>();

            ForEachChild(() =>
            {
                if (_reader.NamespaceURI == GxNamespace)
                {
                    if (_reader.LocalName == "Track")
                    {
                        WarnAndSkip("gx:Track");
                        return;
                    }

                    SkipSubtree();
                    return;
                }

                if (!IsKmlNamespace(_reader.NamespaceURI))
                {
                    SkipSubtree();
                    return;
                }

                switch (_reader.LocalName)
                {
                    case "name":
                        var nameText = ReadTextElement();
                        name ??= nameText;
                        break;
                    case "styleUrl":
                        var styleText = ReadTextElement();
                        styleUrl ??= styleText;
                        break;
                    case "altitudeMode":
                        var modeText = ReadTextElement();

                        if (altitudeModeText is null)
                        {
                            altitudeModeText = modeText;
                            altitudeMode ??= ParseAltitudeMode(modeText);
                        }

                        break;
                    case "ExtendedData":
                        ReadExtendedData(attributes);
                        break;
                    case "Point":
                    case "LineString":
                    case "Polygon":
                    case "MultiGeometry":
                        var result = ReadGeometry();

                        if (result.Geometry is { } geometry)
                        {
                            geometries.Add(geometry);
                        }

                        altitudeMode ??= result.AltitudeMode;
                        altitudeModeText ??= result.ModeText;
                        break;
                    case "Model":
                        WarnAndSkip("Model");
                        break;
                    default:
                        SkipSubtree();
                        break;
                }
            });

            if (geometries.Count == 0)
            {
                if (_diagnostics.Items.Count == diagnosticsBefore)
                {
                    Warn($"Placemark '{name ?? explicitId ?? "#" + _placemarkCount.ToString(CultureInfo.InvariantCulture)}' has no supported geometry and was skipped.");
                }

                return;
            }

            var featureId = explicitId is { Length: > 0 } ? explicitId : BuildImplicitFeatureId(name);

            if (!_featureIds.Add(featureId))
            {
                AddError(GeoDiagnosticCodes.ParseFailure, $"Duplicate KML feature id '{featureId}'.");
                return;
            }

            if (_features.Count >= _limits.MaxFeatureCount)
            {
                AddLimitError($"KML feature count exceeds the limit of {_limits.MaxFeatureCount.ToString(CultureInfo.InvariantCulture)}.");
                throw new LimitExceededException();
            }

            var geometry = geometries.Count == 1 ? geometries[0] : new GeoMultiGeometry(geometries);
            var featureAttributes = BuildAttributes(attributes, name, styleUrl, altitudeModeText);

            _features.Add(new GeospatialFeature(_sourceId, featureId, geometry, CoordinateSystemId.Wgs84, featureAttributes, altitudeMode));
        }

        private string BuildImplicitFeatureId(string? name)
        {
            var folderPath = FolderPath;
            var core = string.IsNullOrEmpty(folderPath) ? name ?? string.Empty : folderPath + "/" + (name ?? string.Empty);

            return core + "#" + _placemarkCount.ToString(CultureInfo.InvariantCulture);
        }

        private IReadOnlyDictionary<string, string> BuildAttributes(
            Dictionary<string, string> attributes,
            string? name,
            string? styleUrl,
            string? altitudeModeText)
        {
            // Reserved keys win over ExtendedData keys that reuse the same name.
            attributes["name"] = name ?? string.Empty;
            attributes["folder"] = FolderPath;

            if (styleUrl is not null)
            {
                attributes["styleUrl"] = styleUrl;
            }

            if (!string.IsNullOrEmpty(altitudeModeText))
            {
                attributes["altitudeMode"] = altitudeModeText;
            }

            return attributes;
        }

        private string FolderPath
        {
            get
            {
                return string.Join("/", _folderNames.Where(static folderName => !string.IsNullOrEmpty(folderName)));
            }
        }

        private GeometryResult ReadGeometry()
        {
            return _reader.LocalName switch
            {
                "Point" => ReadPoint(),
                "LineString" => ReadLineString(),
                "Polygon" => ReadPolygon(),
                _ => ReadMultiGeometry(),
            };
        }

        private GeometryResult ReadPoint()
        {
            var state = new GeometryState(this);
            var diagnosticsBefore = _diagnostics.Items.Count;
            List<GeoPosition>? positions = ReadSingleCoordinatesElement(state);

            if (positions is null)
            {
                AddMissingCoordinatesError("Point", diagnosticsBefore);
                return state.Result(null);
            }

            if (positions.Count != 1)
            {
                AddError(GeoDiagnosticCodes.ParseFailure, "Point requires exactly one coordinate tuple.");
                return state.Result(null);
            }

            return state.Result(new GeoPointGeometry(positions[0]));
        }

        private GeometryResult ReadLineString()
        {
            var state = new GeometryState(this);
            var diagnosticsBefore = _diagnostics.Items.Count;
            List<GeoPosition>? positions = ReadSingleCoordinatesElement(state);

            if (positions is null)
            {
                AddMissingCoordinatesError("LineString", diagnosticsBefore);
                return state.Result(null);
            }

            if (positions.Count < 2)
            {
                AddError(GeoDiagnosticCodes.ParseFailure, "LineString requires at least two coordinate tuples.");
                return state.Result(null);
            }

            return state.Result(new GeoLineString(positions));
        }

        /// <summary>
        /// Reads the children of a simple geometry element, keeping the first coordinates
        /// element and consuming (and discarding) any duplicate.
        /// </summary>
        private List<GeoPosition>? ReadSingleCoordinatesElement(GeometryState state)
        {
            List<GeoPosition>? positions = null;

            ForEachChild(() =>
            {
                if (state.TryReadAltitudeMode(ParseAltitudeMode))
                {
                    return;
                }

                if (IsKmlElement("coordinates"))
                {
                    if (positions is null)
                    {
                        positions = ReadCoordinates();
                    }
                    else
                    {
                        SkipSubtree();
                    }

                    return;
                }

                SkipSubtree();
            });

            return positions;
        }

        private void AddMissingCoordinatesError(string elementName, int diagnosticsBefore)
        {
            if (!HasErrorSince(diagnosticsBefore))
            {
                AddError(GeoDiagnosticCodes.ParseFailure, $"{elementName} requires a coordinates element.");
            }
        }

        private bool HasErrorSince(int start)
        {
            for (var i = start; i < _diagnostics.Items.Count; i++)
            {
                if (_diagnostics.Items[i].Severity == GeoSeverity.Error)
                {
                    return true;
                }
            }

            return false;
        }

        private GeometryResult ReadPolygon()
        {
            var state = new GeometryState(this);
            var diagnosticsBefore = _diagnostics.Items.Count;
            GeoRing? outerRing = null;
            var innerRings = new List<GeoRing>();

            ForEachChild(() =>
            {
                if (state.TryReadAltitudeMode(ParseAltitudeMode))
                {
                    return;
                }

                if (IsKmlElement("outerBoundaryIs"))
                {
                    if (outerRing is null)
                    {
                        outerRing = ReadBoundaryRing();
                    }
                    else
                    {
                        SkipSubtree();
                    }

                    return;
                }

                if (IsKmlElement("innerBoundaryIs"))
                {
                    var ring = ReadBoundaryRing();

                    if (ring is not null)
                    {
                        innerRings.Add(ring);
                    }

                    return;
                }

                SkipSubtree();
            });

            if (outerRing is null)
            {
                if (!HasErrorSince(diagnosticsBefore))
                {
                    AddError(GeoDiagnosticCodes.ParseFailure, "Polygon requires an outerBoundaryIs LinearRing with coordinates.");
                }

                return state.Result(null);
            }

            return state.Result(new GeoPolygon(outerRing, innerRings));
        }

        private GeoRing? ReadBoundaryRing()
        {
            var diagnosticsBefore = _diagnostics.Items.Count;
            GeoRing? ring = null;

            ForEachChild(() =>
            {
                if (ring is null && IsKmlElement("LinearRing"))
                {
                    ring = ReadLinearRing();
                    return;
                }

                SkipSubtree();
            });

            if (ring is null && !HasErrorSince(diagnosticsBefore))
            {
                AddError(GeoDiagnosticCodes.ParseFailure, "Polygon boundary requires a LinearRing.");
            }

            return ring;
        }

        private GeoRing? ReadLinearRing()
        {
            var diagnosticsBefore = _diagnostics.Items.Count;
            List<GeoPosition>? positions = ReadSingleCoordinatesElement(new GeometryState(this));

            if (positions is null)
            {
                AddMissingCoordinatesError("LinearRing", diagnosticsBefore);
                return null;
            }

            if (!HasThreeDistinctVertices(positions))
            {
                AddError(GeoDiagnosticCodes.ParseFailure, "LinearRing requires at least three distinct positions plus closure.");
                return null;
            }

            return new GeoRing(CloseRing(positions));
        }

        private static bool HasThreeDistinctVertices(List<GeoPosition> positions)
        {
            if (positions.Count < 3)
            {
                return false;
            }

            var distinct = new HashSet<Vector2d>(positions.Count);

            foreach (var position in positions)
            {
                distinct.Add(position.Coordinate);
            }

            return distinct.Count >= 3;
        }

        private static List<GeoPosition> CloseRing(List<GeoPosition> positions)
        {
            if (positions[^1].Coordinate != positions[0].Coordinate)
            {
                positions.Add(positions[0]);
            }

            return positions;
        }

        private GeometryResult ReadMultiGeometry()
        {
            var state = new GeometryState(this);
            var geometries = new List<GeoGeometry>();

            EnterNesting();

            try
            {
                ForEachChild(() =>
                {
                    if (state.TryReadAltitudeMode(ParseAltitudeMode))
                    {
                        return;
                    }

                    if (IsKmlElement("Model"))
                    {
                        WarnAndSkip("Model");
                        return;
                    }

                    if (_reader.NamespaceURI == GxNamespace && _reader.LocalName == "Track")
                    {
                        WarnAndSkip("gx:Track");
                        return;
                    }

                    if (IsKmlNamespace(_reader.NamespaceURI) && _reader.LocalName is "Point" or "LineString" or "Polygon" or "MultiGeometry")
                    {
                        var result = ReadGeometry();

                        if (result.Geometry is { } geometry)
                        {
                            geometries.Add(geometry);
                        }

                        state.Adopt(result);
                        return;
                    }

                    SkipSubtree();
                });
            }
            finally
            {
                ExitNesting();
            }

            if (geometries.Count == 0)
            {
                AddError(GeoDiagnosticCodes.ParseFailure, "MultiGeometry requires at least one supported geometry.");
                return state.Result(null);
            }

            return state.Result(new GeoMultiGeometry(geometries));
        }

        private void ReadExtendedData(Dictionary<string, string> attributes)
        {
            ForEachChild(() =>
            {
                if (IsKmlElement("Data"))
                {
                    var key = _reader.GetAttribute("name");
                    string? value = null;

                    ForEachChild(() =>
                    {
                        if (value is null && IsKmlElement("value"))
                        {
                            value = ReadTextElement();
                            return;
                        }

                        SkipSubtree();
                    });

                    AddAttribute(attributes, key, value);
                    return;
                }

                if (IsKmlElement("SchemaData"))
                {
                    ForEachChild(() =>
                    {
                        if (IsKmlElement("SimpleData"))
                        {
                            var key = _reader.GetAttribute("name");
                            var value = ReadTextElement();
                            AddAttribute(attributes, key, value);
                            return;
                        }

                        SkipSubtree();
                    });

                    return;
                }

                SkipSubtree();
            });
        }

        private static void AddAttribute(Dictionary<string, string> attributes, string? key, string? value)
        {
            if (!string.IsNullOrEmpty(key) && value is not null)
            {
                attributes[key] = value;
            }
        }

        private List<GeoPosition>? ReadCoordinates()
        {
            var text = ReadTextElement();

            if (string.IsNullOrEmpty(text))
            {
                return [];
            }

            var positions = new List<GeoPosition>();
            var remainder = text.AsSpan();

            while (true)
            {
                var start = remainder.IndexOfAnyExcept(TupleSeparators);

                if (start < 0)
                {
                    return positions;
                }

                remainder = remainder[start..];
                var separator = remainder.IndexOfAny(TupleSeparators);
                var tuple = separator < 0 ? remainder : remainder[..separator];
                remainder = separator < 0 ? default : remainder[(separator + 1)..];

                if (_coordinateCount >= _limits.MaxCoordinateCount)
                {
                    AddLimitError($"KML coordinate count exceeds the limit of {_limits.MaxCoordinateCount.ToString(CultureInfo.InvariantCulture)}.");
                    throw new LimitExceededException();
                }

                if (!TryParseTuple(tuple, out var position))
                {
                    return null;
                }

                positions.Add(position);
                _coordinateCount++;
            }
        }

        private bool TryParseTuple(ReadOnlySpan<char> tuple, out GeoPosition position)
        {
            position = default;
            var firstComma = tuple.IndexOf(',');

            if (firstComma < 0)
            {
                AddError(GeoDiagnosticCodes.ParseFailure, $"Malformed KML coordinate tuple '{tuple.ToString()}': expected 'lon,lat[,altitude]'.");
                return false;
            }

            var rest = tuple[(firstComma + 1)..];
            var secondComma = rest.IndexOf(',');
            ReadOnlySpan<char> latitudeText;
            ReadOnlySpan<char> altitudeText;

            if (secondComma < 0)
            {
                latitudeText = rest;
                altitudeText = default;
            }
            else
            {
                latitudeText = rest[..secondComma];
                altitudeText = rest[(secondComma + 1)..];

                if (altitudeText.Contains(','))
                {
                    AddError(GeoDiagnosticCodes.ParseFailure, $"Malformed KML coordinate tuple '{tuple.ToString()}': expected 'lon,lat[,altitude]'.");
                    return false;
                }
            }

            var longitudeText = tuple[..firstComma];

            if (!TryParseFinite(longitudeText, out var longitude) || !TryParseFinite(latitudeText, out var latitude))
            {
                AddError(GeoDiagnosticCodes.ParseFailure, $"Malformed KML coordinate tuple '{tuple.ToString()}': values must be finite numbers.");
                return false;
            }

            if (longitude is < -180d or > 180d || latitude is < -90d or > 90d)
            {
                AddError(GeoDiagnosticCodes.ParseFailure, $"KML coordinate tuple '{tuple.ToString()}' is outside geographic bounds.");
                return false;
            }

            double? altitude = null;

            if (!altitudeText.IsEmpty)
            {
                if (!TryParseFinite(altitudeText, out var altitudeValue))
                {
                    AddError(GeoDiagnosticCodes.ParseFailure, $"Malformed KML coordinate tuple '{tuple.ToString()}': values must be finite numbers.");
                    return false;
                }

                altitude = altitudeValue;
            }

            position = new GeoPosition(new Vector2d(longitude, latitude), altitude);

            return true;
        }

        private static bool TryParseFinite(ReadOnlySpan<char> text, out double value)
        {
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value))
            {
                return true;
            }

            value = 0;

            return false;
        }

        private GeoAltitudeMode? ParseAltitudeMode(string? text)
        {
            switch (text?.Trim())
            {
                case null:
                case "":
                    return null;
                case "clampToGround":
                    return GeoAltitudeMode.ClampToGround;
                case "relativeToGround":
                    return GeoAltitudeMode.RelativeToGround;
                case "absolute":
                    return GeoAltitudeMode.Absolute;
                case "clampToSeaFloor":
                case "relativeToSeaFloor":
                    Warn($"gx altitudeMode '{text.Trim()}' is unsupported and was treated as clampToGround.");
                    return GeoAltitudeMode.ClampToGround;
                default:
                    Warn($"altitudeMode '{text.Trim()}' is unknown and was treated as clampToGround.");
                    return GeoAltitudeMode.ClampToGround;
            }
        }

        private void ForEachChild(Action handler)
        {
            if (_reader.IsEmptyElement)
            {
                return;
            }

            while (_reader.Read())
            {
                if (_reader.NodeType == XmlNodeType.Element)
                {
                    handler();
                }
                else if (_reader.NodeType == XmlNodeType.EndElement)
                {
                    return;
                }
            }
        }

        private string ReadTextElement()
        {
            if (_reader.IsEmptyElement)
            {
                return string.Empty;
            }

            var localName = _reader.LocalName;
            var depth = _reader.Depth;
            var builder = new StringBuilder();

            while (_reader.Read())
            {
                if (_reader.NodeType is XmlNodeType.Text or XmlNodeType.CDATA)
                {
                    builder.Append(_reader.Value);
                }
                else if (_reader.NodeType == XmlNodeType.EndElement && _reader.Depth == depth)
                {
                    return builder.ToString();
                }
                else if (_reader.NodeType == XmlNodeType.Element)
                {
                    AddError(GeoDiagnosticCodes.ParseFailure, $"Scalar KML element '{localName}' contains nested markup '{_reader.Name}'.");
                    SkipSubtree();
                }
            }

            return builder.ToString();
        }

        private void SkipSubtree()
        {
            if (_reader.IsEmptyElement)
            {
                return;
            }

            var depth = _reader.Depth;

            while (_reader.Read())
            {
                if (_reader.NodeType == XmlNodeType.EndElement && _reader.Depth == depth)
                {
                    return;
                }
            }
        }

        private void EnterNesting()
        {
            if (_depth >= MaxNestingDepth)
            {
                AddLimitError($"KML nesting depth exceeds {MaxNestingDepth.ToString(CultureInfo.InvariantCulture)}.");
                throw new LimitExceededException();
            }

            _depth++;
        }

        private void ExitNesting()
        {
            _depth--;
        }

        private bool IsKmlElement(string localName)
        {
            return _reader.LocalName == localName && IsKmlNamespace(_reader.NamespaceURI);
        }

        private bool IsAltitudeModeElement()
        {
            return _reader.LocalName == "altitudeMode"
                && (IsKmlNamespace(_reader.NamespaceURI) || _reader.NamespaceURI == GxNamespace);
        }

        private static bool IsKmlNamespace(string namespaceUri)
        {
            return namespaceUri.Length == 0 || KmlNamespaces.Contains(namespaceUri);
        }

        private void AddError(string code, string message, Exception? detail = null)
        {
            _diagnostics.Add(new GeoDiagnostic(code, GeoStage.Parse, GeoSeverity.Error, message, _sourcePath, detail));
        }

        private void AddLimitError(string message)
        {
            AddError(GeoDiagnosticCodes.ParseKmlLimitExceeded, message);
        }

        private void Warn(string message)
        {
            _diagnostics.Add(new GeoDiagnostic(
                GeoDiagnosticCodes.ParseUnsupportedKmlElement,
                GeoStage.Parse,
                GeoSeverity.Warning,
                message,
                _sourcePath));
        }

        private void WarnAndSkip(string elementLabel)
        {
            Warn($"Unsupported KML content was skipped: {elementLabel}.");
            SkipSubtree();
        }

        private sealed class GeometryState(Parser parser)
        {
            internal GeoAltitudeMode? AltitudeMode { get; private set; }

            internal string? ModeText { get; private set; }

            internal bool TryReadAltitudeMode(Func<string?, GeoAltitudeMode?> parse)
            {
                if (!parser.IsAltitudeModeElement())
                {
                    return false;
                }

                var text = parser.ReadTextElement();
                ModeText ??= text;
                AltitudeMode ??= parse(text);

                return true;
            }

            internal GeometryResult Result(GeoGeometry? geometry)
            {
                return new GeometryResult(geometry, AltitudeMode, ModeText);
            }

            internal void Adopt(GeometryResult child)
            {
                AltitudeMode ??= child.AltitudeMode;
                ModeText ??= child.ModeText;
            }
        }
    }
}
