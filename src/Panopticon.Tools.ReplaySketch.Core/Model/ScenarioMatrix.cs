using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Panopticon.Tools.ReplaySketch.Assertions;

namespace Panopticon.Tools.ReplaySketch.Model;

public sealed class SweepAxis
{
    public string Name { get; set; } = string.Empty;
    public IReadOnlyList<string> Values { get; set; } = [];
}

/// <summary>One serializable equality check inside a <see cref="ConstraintFilter"/>; terms AND together.</summary>
public sealed class ConstraintTerm
{
    public string Axis { get; set; } = string.Empty;
    public string Op { get; set; } = "==";
    public string Value { get; set; } = string.Empty;
}

/// <summary>
/// WI-A5-15: constraints are serializable <see cref="ConstraintTerm"/>s. The delegate constructor
/// stays for in-code matrices (terms take precedence when present), so matrix JSON never needs an
/// executable delegate to round-trip.
/// </summary>
public sealed class ConstraintFilter
{
    private readonly Func<IReadOnlyDictionary<string, string>, bool> _predicate;

    public string Expression { get; set; } = string.Empty;

    public IReadOnlyList<ConstraintTerm> Terms { get; set; } = [];

    public ConstraintFilter()
        : this(string.Empty, _ => true)
    {
    }

    public ConstraintFilter(string expression, Func<IReadOnlyDictionary<string, string>, bool> predicate)
    {
        Expression = expression;
        _predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
    }

    public bool Allows(IReadOnlyDictionary<string, string> bindings)
    {
        if (Terms.Count == 0)
        {
            return _predicate(bindings);
        }

        foreach (var term in Terms)
        {
            if (!bindings.TryGetValue(term.Axis, out var bound))
            {
                return false;
            }

            var allowed = term.Op switch
            {
                "==" => string.Equals(bound, term.Value, StringComparison.Ordinal),
                "!=" => !string.Equals(bound, term.Value, StringComparison.Ordinal),
                _ => false,
            };
            if (!allowed)
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>Where a matrix axis value lands when building a cell's concrete scenario (WI-A5-15).</summary>
public enum MatrixBindingTarget
{
    /// <summary>Replaces the scenario map path (Format may contain a `{value}` token).</summary>
    Map,

    /// <summary>Sets the scenario seed; every axis value must parse as uint.</summary>
    Seed,

    /// <summary>Sets scenario Parameters[Key]; Key is required.</summary>
    Parameter,

    /// <summary>Records an experiment-seam implementation selection on the cell; Key is the seam name.</summary>
    Seam,

    /// <summary>Sets Quantity on placements named Key; every axis value must parse as uint.</summary>
    PlacementCount,
}

/// <summary>Binds one sweep axis to one scenario target (WI-A5-15).</summary>
public sealed class MatrixBinding
{
    public MatrixBindingTarget Target { get; set; }
    public string Axis { get; set; } = string.Empty;
    public string? Key { get; set; }
    public string? Format { get; set; }
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

    /// <summary>WI-A5-15/16: seam-name to implementation selections recorded from Seam bindings.</summary>
    public IReadOnlyDictionary<string, string> SeamSelections { get; }

    public MatrixCell(
        IReadOnlyDictionary<string, string> bindings,
        ReplayScenario concreteScenario,
        IReadOnlyDictionary<string, string>? seamSelections = null)
    {
        Bindings = bindings;
        ConcreteScenario = concreteScenario;
        SeamSelections = seamSelections ?? new Dictionary<string, string>();
    }
}

public sealed class ScenarioMatrix
{
    public ReplayScenario BaseScenario { get; set; } = new();
    public IReadOnlyList<SweepAxis> Axes { get; set; } = [];
    public IReadOnlyList<ConstraintFilter> Filters { get; set; } = [];
    public IReadOnlyList<MatrixBinding> Bindings { get; set; } = [];
    public IReadOnlyList<AssertionTemplate> AssertionTemplates { get; set; } = [];

    /// <summary>
    /// WI-A5-15: whole-matrix validation that runs before any cell is expanded or run. Unknown
    /// paths, incompatible values, and duplicate bindings surface here instead of mid-sweep.
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var diagnostics = new List<string>();
        var axisNames = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < Axes.Count; i++)
        {
            var axis = Axes[i];
            if (string.IsNullOrWhiteSpace(axis.Name))
            {
                diagnostics.Add($"axes[{i}].name: axis name is empty");
            }
            else if (!axisNames.Add(axis.Name))
            {
                diagnostics.Add($"axes[{i}].name: duplicate axis name '{axis.Name}'");
            }

            if (axis.Values.Count == 0)
            {
                diagnostics.Add($"axes[{i}].values: axis '{axis.Name}' has no values");
            }
        }

        var seenBindings = new HashSet<(MatrixBindingTarget, string?, string)>();
        for (var i = 0; i < Bindings.Count; i++)
        {
            var binding = Bindings[i];
            var path = $"bindings[{i}]";
            if (!axisNames.Contains(binding.Axis))
            {
                diagnostics.Add($"{path}.axis: unknown axis '{binding.Axis}'");
            }

            switch (binding.Target)
            {
                case MatrixBindingTarget.Parameter:
                case MatrixBindingTarget.Seam:
                case MatrixBindingTarget.PlacementCount:
                    if (string.IsNullOrWhiteSpace(binding.Key))
                    {
                        diagnostics.Add($"{path}.key: target {binding.Target} requires a key");
                    }

                    break;
                case MatrixBindingTarget.Map:
                case MatrixBindingTarget.Seed:
                    break;
                default:
                    diagnostics.Add($"{path}.target: unsupported target '{binding.Target}'");
                    break;
            }

            if (!seenBindings.Add((binding.Target, binding.Key, binding.Axis)))
            {
                diagnostics.Add($"{path}: duplicate binding for target {binding.Target}, key '{binding.Key}', axis '{binding.Axis}'");
            }

            if (binding.Target is MatrixBindingTarget.Seed or MatrixBindingTarget.PlacementCount)
            {
                var axis = Axes.FirstOrDefault(a => a.Name == binding.Axis);
                if (axis is null)
                {
                    continue;
                }

                foreach (var value in axis.Values)
                {
                    if (!uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                    {
                        diagnostics.Add($"{path}: axis '{binding.Axis}' value '{value}' does not parse as uint for target {binding.Target}");
                        break;
                    }
                }
            }
        }

        for (var i = 0; i < Filters.Count; i++)
        {
            var filter = Filters[i];
            for (var j = 0; j < filter.Terms.Count; j++)
            {
                var term = filter.Terms[j];
                if (!axisNames.Contains(term.Axis))
                {
                    diagnostics.Add($"filters[{i}].terms[{j}].axis: unknown axis '{term.Axis}'");
                }

                if (term.Op is not ("==" or "!="))
                {
                    diagnostics.Add($"filters[{i}].terms[{j}].op: unsupported operator '{term.Op}'");
                }
            }
        }

        return diagnostics;
    }

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

            cells.Add(BuildConcreteScenario(binding));
        }

        return cells;
    }

    private MatrixCell BuildConcreteScenario(IReadOnlyDictionary<string, string> bindings)
    {
        var scenario = CloneBaseScenario();
        var seamSelections = new Dictionary<string, string>(StringComparer.Ordinal);
        var mapBound = false;

        foreach (var binding in Bindings)
        {
            if (!bindings.TryGetValue(binding.Axis, out var value))
            {
                continue;
            }

            switch (binding.Target)
            {
                case MatrixBindingTarget.Map:
                    scenario.MapPath = binding.Format is { } format
                        ? format.Replace("{value}", value, StringComparison.Ordinal)
                        : value;
                    mapBound = true;
                    break;
                case MatrixBindingTarget.Seed:
                    scenario.Seed = uint.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
                    break;
                case MatrixBindingTarget.Parameter:
                    if (!string.IsNullOrWhiteSpace(binding.Key))
                    {
                        scenario.Parameters[binding.Key] = value;
                    }

                    break;
                case MatrixBindingTarget.Seam:
                    if (!string.IsNullOrWhiteSpace(binding.Key))
                    {
                        seamSelections[binding.Key] = value;
                    }

                    break;
                case MatrixBindingTarget.PlacementCount:
                    ApplyPlacementCount(scenario, binding.Key, value);
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported matrix binding target '{binding.Target}'.");
            }
        }

        if (!mapBound)
        {
            scenario.MapPath = Substitute(BaseScenario.MapPath, bindings);
        }

        scenario.Assertions.AddRange(AssertionTemplates.Select(template => template.ToConcreteAssertion(bindings)));
        return new MatrixCell(bindings, scenario, seamSelections);
    }

    private ReplayScenario CloneBaseScenario()
    {
        return new ReplayScenario
        {
            SchemaVersion = BaseScenario.SchemaVersion,
            DomainId = BaseScenario.DomainId,
            Seed = BaseScenario.Seed,
            MapPath = BaseScenario.MapPath,
            BaseRadiusWorldUnits = BaseScenario.BaseRadiusWorldUnits,
            Placements = BaseScenario.Placements.ToList(),
            Parameters = new Dictionary<string, string>(BaseScenario.Parameters),
            StopConditions = BaseScenario.StopConditions.ToList(),
            RecordedMetricSeries = BaseScenario.RecordedMetricSeries.ToList(),
            Recording = BaseScenario.Recording,
            Graph = BaseScenario.Graph,
            PopulationGenerators = BaseScenario.PopulationGenerators.ToList(),
            CommandGenerators = BaseScenario.CommandGenerators.ToList(),
            Players = BaseScenario.Players.Select(ClonePlayer).ToList(),
            Assertions = BaseScenario.Assertions.ToList(),
        };
    }

    private static PlayerSlotConfig ClonePlayer(PlayerSlotConfig player)
    {
        return new PlayerSlotConfig
        {
            Name = player.Name,
            FactionIndex = player.FactionIndex,
            Color = player.Color,
            StartPosition = player.StartPosition,
            Team = player.Team,
            StartingMoney = player.StartingMoney,
            Actions = player.Actions.ToList(),
        };
    }

    private static void ApplyPlacementCount(ReplayScenario scenario, string? definitionName, string value)
    {
        if (string.IsNullOrWhiteSpace(definitionName))
        {
            return;
        }

        var quantity = uint.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
        foreach (var placement in scenario.Placements)
        {
            if (string.Equals(placement.DefinitionName, definitionName, StringComparison.Ordinal))
            {
                placement.Quantity = (int)quantity;
            }
        }
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
