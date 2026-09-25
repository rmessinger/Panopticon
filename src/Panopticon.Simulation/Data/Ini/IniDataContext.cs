using System.Collections.Generic;

namespace Panopticon.Data.Ini;

public sealed class IniDataContext
{
    internal Dictionary<string, IniToken> Defines { get; } = new Dictionary<string, IniToken>();
}
