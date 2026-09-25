using System;

namespace Panopticon.Scripting;

[AttributeUsage(AttributeTargets.Parameter)]
public sealed class ScriptArgumentTypeAttribute : Attribute
{
    public ScriptArgumentTypeAttribute(ScriptArgumentType argumentType)
    {
        ArgumentType = argumentType;
    }

    public readonly ScriptArgumentType ArgumentType;
}
