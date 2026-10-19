#nullable enable annotations
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Panopticon.Logic;
using Panopticon.Recording;

namespace Panopticon;

/// <summary>
/// Terminal <see cref="GameOutcome"/> of a headless run, serialized without live object
/// references.
/// </summary>
public sealed class RunOutcomeRecord
{
    public OutcomeStatus Status { get; set; }
    public OutcomeReason Reason { get; set; }
    public List<string> WinningPlayers { get; set; } = new();
    public int? DecidedOnFrame { get; set; }
}
