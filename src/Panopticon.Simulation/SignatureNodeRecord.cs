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

public sealed class SignatureNodeRecord
{
    public string Key { get; set; } = "";
    public int Level { get; set; }
    public ulong Hash { get; set; }
    public int MemberCount { get; set; }
}
