using System;
using System.Collections.Generic;
using OpenSage;

namespace OpenSage.Tools.ReplaySketch.Assertions;

public static class CodeAssertionRegistry
{
    private static readonly Dictionary<string, Func<IGame, uint, bool>> Predicates = new();

    public static void Register(string name, Func<IGame, uint, bool> predicate) =>
        Predicates[name] = predicate;

    public static bool TryGet(string name, out Func<IGame, uint, bool>? predicate) =>
        Predicates.TryGetValue(name, out predicate);
}
