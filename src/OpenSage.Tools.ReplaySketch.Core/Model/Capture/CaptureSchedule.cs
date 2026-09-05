using System;
using System.Collections.Generic;
namespace OpenSage.Tools.ReplaySketch.Model.Capture;

/// <summary>
/// Groups a profile's commands by capture-relative frame, preserving declaration order within a
/// frame. Pure so scheduling can be tested without a game.
/// </summary>
public sealed class CaptureSchedule
{
    private readonly Dictionary<int, List<CaptureCommand>> _commandsByFrame;

    public CaptureSchedule(CaptureProfile profile)
    {
        _commandsByFrame = [];
        foreach (var command in profile.Commands)
        {
            if (!_commandsByFrame.TryGetValue(command.Frame, out var due))
            {
                due = [];
                _commandsByFrame[command.Frame] = due;
            }

            due.Add(command);
        }
    }

    /// <summary>Commands due before the tick of the given capture-relative frame, in declaration order.</summary>
    public IReadOnlyList<CaptureCommand> DueAt(int frame) =>
        _commandsByFrame.TryGetValue(frame, out var due) ? due : [];
}
