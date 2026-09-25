using System;

namespace Panopticon.Utilities;

public static class TimeSpanUtility
{
    public static TimeSpan Max(TimeSpan a, TimeSpan b)
    {
        return a > b ? a : b;
    }
}
