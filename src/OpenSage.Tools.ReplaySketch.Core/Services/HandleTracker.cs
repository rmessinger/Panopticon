using System.Collections.Generic;
using OpenSage.Logic.Object;

namespace OpenSage.Tools.ReplaySketch.Services;

/// <summary>
/// Tracks which <see cref="Logic.Object.ObjectId"/> was created as the first direct result of
/// each named command dispatch. Callers wrap each command dispatch with
/// <see cref="BeginCommand"/> / <see cref="EndCommand"/>; all
/// <see cref="OnObjectCreated"/> calls that occur while a command is active are attributed
/// to that command (first object only).
///
/// Uses a stack so that nested Begin/End pairs are handled correctly when multiple commands
/// land on the same game frame.
/// </summary>
public sealed class HandleTracker
{
    private readonly Dictionary<string, ObjectId> _handleMap = new();
    private readonly Stack<string> _pending = new();

    /// <summary>Marks the start of a command dispatch. May be called before a game update.</summary>
    public void BeginCommand(string commandId) => _pending.Push(commandId);

    /// <summary>Marks the end of a command dispatch. Call after the game update that processes it.</summary>
    public void EndCommand() => _pending.TryPop(out _);

    /// <summary>
    /// Called when the engine creates a new game object. Attributes the object to the
    /// innermost active command (if any) whose handle has not yet been set.
    /// </summary>
    public void OnObjectCreated(ObjectId id)
    {
        if (_pending.TryPeek(out var commandId) && !_handleMap.ContainsKey(commandId))
        {
            _handleMap[commandId] = id;
        }
    }

    /// <summary>
    /// Returns the <see cref="ObjectId"/> of the first object created after
    /// <see cref="BeginCommand"/> was called for <paramref name="commandId"/>,
    /// or <see cref="ObjectId.Invalid"/> if no object was tracked.
    /// </summary>
    public ObjectId ResolveHandle(string commandId) =>
        _handleMap.TryGetValue(commandId, out var id) ? id : ObjectId.Invalid;
}
