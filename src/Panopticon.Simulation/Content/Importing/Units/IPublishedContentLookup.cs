namespace Panopticon.Content.Importing.Units;

/// <summary>
/// Content already published outside the current import (Generals built-ins, earlier imports).
/// References to it satisfy dependencies without joining the import's closure.
/// </summary>
public interface IPublishedContentLookup
{
    bool Contains(ContentId id);
}
