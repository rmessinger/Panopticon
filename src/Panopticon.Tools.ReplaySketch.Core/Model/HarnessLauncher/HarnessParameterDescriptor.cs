using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace Panopticon.Tools.ReplaySketch.Model.HarnessLauncher;

public sealed record HarnessParameterDescriptor(
    string Name,
    string Group,
    HarnessParameterKind Kind,
    IReadOnlyList<string> EnumValues,
    JsonNode? Default,
    string Description,
    IReadOnlyList<string> AppliesTo);
