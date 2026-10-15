using System;

namespace Panopticon.Tools.TestHarness;

[AttributeUsage(AttributeTargets.Property)]
internal sealed class HarnessParameterAttribute : Attribute
{
    public HarnessParameterAttribute(string group, string description, params string[] scenarios)
    {
        Group = group;
        Description = description;
        Scenarios = scenarios;
    }

    public string Group { get; }
    public string Description { get; }
    public string[] Scenarios { get; }
}
