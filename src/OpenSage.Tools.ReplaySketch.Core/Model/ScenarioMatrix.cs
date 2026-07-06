using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OpenSage.Tools.ReplaySketch.Assertions;

namespace OpenSage.Tools.ReplaySketch.Model;

public sealed class SweepAxis
{
    public string Name { get; set; } = string.Empty;
    public IReadOnlyList<string> Values { get; set; } = [];
}

public sealed class ConstraintFilter
{
    private readonly Func<IReadOnlyDictionary<string, string>, bool> _predicate;

    public string Expression { get; set; } = string.Empty;

    public ConstraintFilter()
        : this(string.Empty, _ => true)
    {
    }

    public ConstraintFilter(string expression, Func<IReadOnlyDictionary<string, string>, bool> predicate)
    {
        Expression = expression;
        _predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
    }

    public bool Allows(IReadOnlyDictionary<string, string> bindings) => _predicate(bindings);
}

public sealed class AssertionTemplate
{
    public string Kind { get; set; } = "state";
    public string Id { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Op { get; set; } = "==";
    public string Value { get; set; } = "0";

    public ScenarioAssertion ToConcreteAssertion(IReadOnlyDictionary<string, string> bindings)
    {
        var concreteKind = Substitute(Kind, bindings);
        var concreteId = Substitute(Id, bindings);
        var concretePath = Substitute(Path, bindings);
        var concreteOp = Substitute(Op, bindings);
        var concreteValue = Substitute(Value, bindings);
        var numericValue = double.Parse(concreteValue, NumberStyles.Float, CultureInfo.InvariantCulture);

        return concreteKind switch
        {
            "state" => new StateAssertion
            {
                Id = concreteId,
                Path = concretePath,
                Op = concreteOp,
                Value = numericValue,
            },
            "invariant" => new InvariantAssertion
            {
                Id = concreteId,
                Path = concretePath,
                Op = concreteOp,
                Value = numericValue,
            },
            _ => throw new InvalidOperationException($"Unsupported assertion template kind '{concreteKind}'."),
        };
    }

    private static string Substitute(string template, IReadOnlyDictionary<string, string> bindings)
    {
        var output = template;
        foreach (var (name, value) in bindings)
        {
            output = output.Replace($"{{{name}}}", value, StringComparison.Ordinal);
        }

        return output;
    }
}

public sealed class MatrixCell
{
    public IReadOnlyDictionary<string, string> Bindings { get; }
    public ReplayScenario ConcreteScenario { get; }

    public MatrixCell(IReadOnlyDictionary<string, string> bindings, ReplayScenario concreteScenario)
    {
        Bindings = bindings;
        ConcreteScenario = concreteScenario;
    }
}

public sealed class ScenarioMatrix
{
    public ReplayScenario BaseScenario { get; set; } = new();
    public IReadOnlyList<SweepAxis> Axes { get; set; } = [];
    public IReadOnlyList<ConstraintFilter> Filters { get; set; } = [];
    public IReadOnlyList<AssertionTemplate> AssertionTemplates { get; set; } = [];

    public IReadOnlyList<MatrixCell> Expand()
    {
        var expandedBindings = new List<Dictionary<string, string>> { new() };

        foreach (var axis in Axes)
        {
            var next = new List<Dictionary<string, string>>();
            foreach (var binding in expandedBindings)
            {
                foreach (var value in axis.Values)
                {
                    var candidate = new Dictionary<string, string>(binding)
                    {
                        [axis.Name] = value,
                    };
                    next.Add(candidate);
                }
            }

            expandedBindings = next;
        }

        var cells = new List<MatrixCell>();
        foreach (var binding in expandedBindings)
        {
            if (!Filters.All(filter => filter.Allows(binding)))
            {
                continue;
            }

            cells.Add(new MatrixCell(binding, BuildConcreteScenario(binding)));
        }

        return cells;
    }

    private ReplayScenario BuildConcreteScenario(IReadOnlyDictionary<string, string> bindings)
    {
        var assertions = BaseScenario.Assertions.ToList();
        assertions.AddRange(AssertionTemplates.Select(template => template.ToConcreteAssertion(bindings)));

        return new ReplayScenario
        {
            MapPath = Substitute(BaseScenario.MapPath, bindings),
            BaseRadiusWorldUnits = BaseScenario.BaseRadiusWorldUnits,
            Players = BaseScenario.Players.ToList(),
            Assertions = assertions,
        };
    }

    private static string Substitute(string template, IReadOnlyDictionary<string, string> bindings)
    {
        var output = template;
        foreach (var (name, value) in bindings)
        {
            output = output.Replace($"{{{name}}}", value, StringComparison.Ordinal);
        }

        return output;
    }
}
