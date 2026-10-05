#nullable enable

namespace Panopticon.Content.Importing.Units;

public enum ModuleEditOperation
{
    Add,
    Remove,

    /// <summary>Removes the tag, then adds the module.</summary>
    Replace,
}

/// <summary>An ordered module edit. <paramref name="Module"/> is null only for <see cref="ModuleEditOperation.Remove"/>.</summary>
public sealed record ModuleEdit(ModuleEditOperation Operation, string Tag, UnitModule? Module = null)
{
    public static ModuleEdit Add(UnitModule module) => new(ModuleEditOperation.Add, module.Tag, module);

    public static ModuleEdit Remove(string tag) => new(ModuleEditOperation.Remove, tag);

    public static ModuleEdit Replace(string tag, UnitModule module) => new(ModuleEditOperation.Replace, tag, module);
}
