using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Panopticon.Client;
using Panopticon.Logic.Object;
using Xunit;

namespace Panopticon.Tests.Logic;

/// <summary>
/// Enforces the A3 simulation/presentation seam: logic types must not reference presentation
/// types. Definitions live in docs/roadmap/deep-dives/a3-sim-presentation-seam.md (section 4).
/// The allowlist below must stay exact: new violations fail this test, and fixed violations
/// left in the allowlist also fail it.
/// </summary>
public class LogicPresentationBoundaryTests
{
    private static readonly Assembly GameAssembly = typeof(GameObject).Assembly;

    private static readonly HashSet<Type> AllTypes = GameAssembly.GetTypes().ToHashSet();

    // Types (and their subclasses) that are presentation-by-role even though they are declared
    // under Panopticon.Logic namespaces. They are neither scanned as logic nor counted as logic.
    private static readonly Type[] PresentationByRoleRoots =
    [
        typeof(DrawModule),
        typeof(ClientUpdateModule),
        typeof(DrawModuleData),
        typeof(ClientUpdateModuleData),
    ];

    private const string LogicNamespacePrefix = "Panopticon.Logic";

    private static readonly string[] PresentationByRoleNamespaces =
    [
        $"{LogicNamespacePrefix}.OrderGenerators",
    ];

    private static readonly string[] PresentationByRoleTypes =
    [
        $"{LogicNamespacePrefix}.OrderGeneratorSystem",
        $"{LogicNamespacePrefix}.SelectionSystem",
        $"{LogicNamespacePrefix}.SelectionMessageHandler",
        $"{LogicNamespacePrefix}.GhostObject",
        $"{LogicNamespacePrefix}.GhostObjectManager",
        // Declared alongside the draw module it serves (W3dModelDraw.cs); holds the active
        // condition state's ModelInstance and render state.
        "Panopticon.Logic.Object.W3dModelDrawConditionState",
    ];

    // Presentation state types logic must not reference, by name.
    private static readonly string[] PresentationTypeNames =
    [
        typeof(Drawable).FullName!,
        "Panopticon.Graphics.Cameras.Camera",
        "Panopticon.Graphics.ModelInstance",
        "Panopticon.Graphics.Rendering.RenderList",
        "Panopticon.Gui.DrawingContext2D",
        "Veldrid.GraphicsDevice",
    ];

    private static readonly string[] PresentationNamespaces = ["Panopticon.Rendering"];

    // (logic type full name, presentation type full name) -> justification. Empty: the seam is
    // clean. When a violation is fixed, remove its entry or this test fails.
    private static readonly Dictionary<string, string> ExpectedViolations = [];

    private static bool IsPresentationByRole(Type type)
    {
        // Nested types (including compiler-generated closures) share their declaring type's role.
        for (var declaring = type; declaring is not null; declaring = declaring.DeclaringType)
        {
            if (PresentationByRoleRoots.Any(root => root.IsAssignableFrom(declaring)))
            {
                return true;
            }

            if (PresentationByRoleNamespaces.Any(ns => declaring.Namespace?.StartsWith(ns, StringComparison.Ordinal) == true)
                || PresentationByRoleTypes.Contains(declaring.FullName))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsLogicType(Type type)
    {
        var ns = type.Namespace;
        if (ns is null || !ns.StartsWith(LogicNamespacePrefix, StringComparison.Ordinal))
        {
            return false;
        }

        if (ns != LogicNamespacePrefix && !ns.StartsWith($"{LogicNamespacePrefix}.", StringComparison.Ordinal))
        {
            return false;
        }

        return !IsPresentationByRole(type);
    }

    private static bool IsPresentationType(Type type, IReadOnlySet<Type> presentationByRoleDerived)
    {
        if (PresentationTypeNames.Contains(type.FullName))
        {
            return true;
        }

        if (PresentationNamespaces.Any(ns => type.Namespace?.StartsWith(ns, StringComparison.Ordinal) == true))
        {
            return true;
        }

        return type is { IsClass: true, FullName: not null }
            && presentationByRoleDerived.Contains(type);
    }

    private static IEnumerable<Type> EnumerateReferencedTypes(Type type)
    {
        if (type.BaseType is not null)
        {
            yield return type.BaseType;
        }

        foreach (var implemented in type.GetInterfaces())
        {
            yield return implemented;
        }

        foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
        {
            foreach (var referenced in ExpandType(field.FieldType))
            {
                yield return referenced;
            }
        }

        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
        {
            foreach (var referenced in ExpandType(property.PropertyType))
            {
                yield return referenced;
            }

            foreach (var accessor in property.GetAccessors(true))
            {
                foreach (var referenced in EnumerateMethodTypes(accessor))
                {
                    yield return referenced;
                }
            }
        }

        foreach (var constructor in type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
        {
            foreach (var parameter in constructor.GetParameters())
            {
                foreach (var referenced in ExpandType(parameter.ParameterType))
                {
                    yield return referenced;
                }
            }
        }

        foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
        {
            foreach (var referenced in EnumerateMethodTypes(method))
            {
                yield return referenced;
            }
        }

        foreach (var eventInfo in type.GetEvents(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
        {
            foreach (var referenced in ExpandType(eventInfo.EventHandlerType!))
            {
                yield return referenced;
            }
        }
    }

    private static IEnumerable<Type> EnumerateMethodTypes(MethodInfo method)
    {
        foreach (var referenced in ExpandType(method.ReturnType))
        {
            yield return referenced;
        }

        foreach (var parameter in method.GetParameters())
        {
            foreach (var referenced in ExpandType(parameter.ParameterType))
            {
                yield return referenced;
            }
        }
    }

    private static IEnumerable<Type> ExpandType(Type type)
    {
        if (type.IsArray)
        {
            foreach (var expanded in ExpandType(type.GetElementType()!))
            {
                yield return expanded;
            }

            yield break;
        }

        if (type.IsByRef || type.IsPointer)
        {
            foreach (var expanded in ExpandType(type.GetElementType()!))
            {
                yield return expanded;
            }

            yield break;
        }

        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments())
            {
                foreach (var expanded in ExpandType(argument))
                {
                    yield return expanded;
                }
            }
        }

        if (!type.IsGenericParameter)
        {
            yield return type;
        }
    }

    [Fact]
    public void LogicTypes_DoNotReferencePresentationTypes()
    {
        var presentationByRoleDerived = AllTypes
            .Where(t => PresentationByRoleRoots.Any(root => root.IsAssignableFrom(t)))
            .ToHashSet();

        var actualViolations = new SortedSet<string>();
        var details = new Dictionary<string, string>();

        foreach (var type in AllTypes.Where(IsLogicType))
        {
            foreach (var referenced in EnumerateReferencedTypes(type))
            {
                if (referenced.Assembly != GameAssembly && referenced.FullName?.StartsWith("Veldrid") != true)
                {
                    continue;
                }

                if (!IsPresentationType(referenced, presentationByRoleDerived))
                {
                    continue;
                }

                var key = $"{type.FullName} -> {referenced.FullName}";
                if (actualViolations.Add(key) && !details.ContainsKey(key))
                {
                    details[key] = $"{type.FullName} ({(type.IsVisible ? "public" : "internal")} {type.MemberType}) references {referenced.FullName}";
                }
            }
        }

        var expected = new SortedSet<string>(ExpectedViolations.Keys);

        Assert.True(
            actualViolations.SetEquals(expected),
            $"Logic-to-presentation boundary drift.\nNew violations (remove them, or justify + allowlist them in {nameof(ExpectedViolations)}):\n{JoinMissing(actualViolations, expected)}\nStale allowlist entries (violation was fixed; remove the entry):\n{JoinMissing(expected, actualViolations)}\nDetails:\n{string.Join("\n", details.Values.OrderBy(x => x))}");
    }

    private static string JoinMissing(SortedSet<string> source, SortedSet<string> other)
    {
        var missing = source.Except(other).ToList();
        return missing.Count == 0 ? "  (none)" : string.Join("\n", missing.Select(x => $"  {x}"));
    }
}
