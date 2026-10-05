#nullable enable

using System.Collections.Generic;
using System.Linq;

namespace Panopticon.Content.Importing.Units;

/// <summary>
/// Applies a child's module edits over its parent's resolved modules with the Generals inheritance
/// rules (<c>ObjectDefinition.AddModuleData</c>): an inherited <c>Default</c> module yields to a
/// child module of overlapping kinds, <c>OverrideableByLikeKind</c> yields to the same family,
/// <c>AlwaysInherit</c> survives until removed by tag, and a shared exclusive group always replaces.
/// </summary>
internal static class ModuleMerger
{
    public static IReadOnlyList<ModuleEdit> Merge(IEnumerable<UnitModule> parentModules, IReadOnlyList<ModuleEdit> edits)
    {
        var modules = parentModules.Select(module => module with { Inherited = true }).ToList();

        foreach (var edit in edits)
        {
            switch (edit.Operation)
            {
                case ModuleEditOperation.Remove:
                    modules.RemoveAll(module => module.Tag == edit.Tag);
                    break;

                case ModuleEditOperation.Replace:
                    modules.RemoveAll(module => module.Tag == edit.Tag);
                    Add(modules, edit.Module!);
                    break;

                default:
                    Add(modules, edit.Module!);
                    break;
            }
        }

        return modules.Select(ModuleEdit.Add).ToArray();
    }

    private static void Add(List<UnitModule> modules, UnitModule module)
    {
        modules.RemoveAll(existing => IsDisplacedBy(existing, module));

        var index = modules.FindIndex(existing => existing.Tag == module.Tag);
        if (index >= 0)
        {
            modules[index] = module;
        }
        else
        {
            modules.Add(module);
        }
    }

    private static bool IsDisplacedBy(UnitModule existing, UnitModule incoming)
    {
        if (existing.Inherited)
        {
            return existing.InheritanceMode switch
            {
                Logic.Object.ModuleInheritanceMode.Default => (existing.Kinds & incoming.Kinds) != 0,
                Logic.Object.ModuleInheritanceMode.OverrideableByLikeKind => existing.Family == incoming.Family,
                _ => false,
            };
        }

        return existing.ExclusiveGroup is not null && existing.ExclusiveGroup == incoming.ExclusiveGroup;
    }
}
