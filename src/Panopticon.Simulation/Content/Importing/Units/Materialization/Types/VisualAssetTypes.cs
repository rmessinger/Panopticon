namespace Panopticon.Content.Importing.Units.Materialization;

public enum VisualAssetKind
{
    Model,
    Animation,
}

/// <summary>
/// A Track C asset a materialized draw module refers to. <paramref name="SourceKey"/> is the source
/// importer key; <paramref name="RuntimeName"/> is the source-qualified name registered for it.
/// </summary>
public sealed record VisualAssetRequest(ContentId Subject, VisualAssetKind Kind, string SourceKey, string RuntimeName);
