using System;
using System.Collections.Generic;
using OpenSage;

namespace OpenSage.Tools.ReplaySketch.Assertions;

public static class CodeAssertionRegistry
{
    private static readonly Dictionary<string, Func<Game, uint, bool>> Predicates = new();

    public static void Register(string name, Func<Game, uint, bool> predicate) =>
        Predicates[name] = predicate;

    public static bool TryGet(string name, out Func<Game, uint, bool>? predicate) =>
        Predicates.TryGetValue(name, out predicate);
}
