using System;
using Panopticon.Mathematics;

namespace Panopticon.Logic.Object;

public interface IConditionState<T>
    where T : Enum
{
    ReadOnlySpan<BitArray<T>> ConditionFlags { get; }
}
