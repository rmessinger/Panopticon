#nullable enable annotations

using System;
using System.Collections.Generic;
using Panopticon.IO;

namespace Panopticon.Recording;

/// <summary>WI-A5-29: leading record of a region-indexed artifact; readers reject unknown versions.</summary>
public sealed class RegionSchemaHeader : IPersistableObject
{
    public int SchemaVersion { get; set; }
    public string RegionSchemaId { get; set; } = "";

    public void Persist(StatePersister persister)
    {
        persister.PersistVersion(1);

        var schemaVersion = SchemaVersion;
        persister.PersistInt32(ref schemaVersion, "SchemaVersion");
        SchemaVersion = schemaVersion;

        var schemaId = RegionSchemaId;
        persister.PersistAsciiString(ref schemaId, "RegionSchemaId");
        RegionSchemaId = schemaId;
    }
}
