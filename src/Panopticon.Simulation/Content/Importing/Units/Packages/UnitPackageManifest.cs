#nullable enable

using System.Collections.Generic;
using System.Linq;

namespace Panopticon.Content.Importing.Units.Packages;

/// <summary>Parsed model of a unit-package manifest.json.</summary>
public sealed class UnitPackageManifest
{
    private readonly IReadOnlyList<UnitPackageRootRecordEntry> _roots;
    private readonly IReadOnlyList<UnitPackageRecordIndexEntry> _records;
    private readonly IReadOnlyList<UnitPackageRetainedPayloadEntry> _retainedPayloads;
    private readonly IReadOnlyList<string> _unknownMembers;
    private readonly IReadOnlyList<ContentId> _overrides;

    public string ContainerKind { get; }

    public int SchemaVersion { get; }

    public UnitPackageSourceIdentity? Source { get; }

    public IReadOnlyList<ContentId> Overrides
    {
        get => _overrides;
        init => _overrides = value.ToArray();
    }

    public IReadOnlyList<UnitPackageRootRecordEntry> Roots
    {
        get => _roots;
        init => _roots = value.ToArray();
    }

    public IReadOnlyList<UnitPackageRecordIndexEntry> Records
    {
        get => _records;
        init => _records = value.ToArray();
    }

    public IReadOnlyList<UnitPackageRetainedPayloadEntry> RetainedPayloads
    {
        get => _retainedPayloads;
        init => _retainedPayloads = value.ToArray();
    }

    public UnitPackageProvenance? Provenance { get; }

    public IReadOnlyList<string> UnknownMembers
    {
        get => _unknownMembers;
        init => _unknownMembers = value.ToArray();
    }

    public UnitPackageManifest(
        string containerKind,
        int schemaVersion,
        UnitPackageSourceIdentity? source,
        IEnumerable<UnitPackageRootRecordEntry>? roots = null,
        IEnumerable<UnitPackageRecordIndexEntry>? records = null,
        IEnumerable<UnitPackageRetainedPayloadEntry>? retainedPayloads = null,
        UnitPackageProvenance? provenance = null,
        IEnumerable<string>? unknownMembers = null,
        IEnumerable<ContentId>? overrides = null)
    {
        ContainerKind = containerKind;
        SchemaVersion = schemaVersion;
        Source = source;
        _roots = roots?.ToArray() ?? [];
        _records = records?.ToArray() ?? [];
        _retainedPayloads = retainedPayloads?.ToArray() ?? [];
        Provenance = provenance;
        _unknownMembers = unknownMembers?.ToArray() ?? [];
        _overrides = overrides?.ToArray() ?? [];
    }
}
