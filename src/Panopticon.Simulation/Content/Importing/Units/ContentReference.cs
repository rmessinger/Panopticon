namespace Panopticon.Content.Importing.Units;

/// <summary>A typed edge from one record to another. Optional edges only warn when unresolved.</summary>
public readonly record struct ContentReference(ContentId Target, DependencyKind Kind)
{
    public bool IsRequired => Kind != DependencyKind.Presentation;
}
