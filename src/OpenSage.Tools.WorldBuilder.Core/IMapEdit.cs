namespace OpenSage.Tools.WorldBuilder.Core;

/// <summary>
/// Represents a reversible map editing operation (Command pattern).
/// Each tool action creates an IMapEdit and pushes it to UndoRedoStack.
/// </summary>
public interface IMapEdit
{
    void Apply();
    void Revert();
}
