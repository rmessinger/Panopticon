using System;

namespace Panopticon;

public class DisplayAttribute : Attribute
{
    public string Name { get; }
    public string Category { get; }

    public DisplayAttribute(string name = null, string category = null)
    {
        Name = name;
        Category = category;
    }
}
