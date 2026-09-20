#nullable enable

using System;
using OpenSage.Content.Importing.Gltf;

namespace OpenSage.Content.Importing;

/// <summary>
/// One explicit importer choice for an external source: public format identifier, immutable
/// options, and the public inspection facade. Exposes neither internal C1 importer interface.
/// </summary>
public sealed record ExternalFormatSelection(
    string FormatId,
    GltfImportOptions Options,
    IExternalAssetInspector Inspector);
