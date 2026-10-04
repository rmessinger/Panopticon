#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Panopticon.Simulation;

/// <summary>How one domain state field is quantified: exact integer or real-valued.</summary>
public enum DomainQuantityType
{
    Integer,
    Real,
}

/// <summary>
/// The conservation role a field plays within its <see cref="DomainStateField.Family"/>:
/// holdings plus sinks minus sources is constant when the family is conserved.
/// </summary>
public enum DomainConservationRole
{
    None,
    Holding,
    Sink,
    Source,
}

/// <summary>
/// One field of a record kind: name, unit label, quantity type, and conservation role.
/// Units use short SI-style or count labels (persons, rounds, guns, s, ratio, index).
/// </summary>
public sealed record DomainStateField(
    string Name,
    string Unit,
    DomainQuantityType Type,
    DomainConservationRole Role,
    string? Family);

/// <summary>
/// One record kind: the owning layer name, the field vector layout in declaration order, and
/// the stable-key reference slot names. A record's Integers/Reals/References vectors follow
/// this layout.
/// </summary>
public sealed record DomainRecordKind(
    string Name,
    string Layer,
    IReadOnlyList<DomainStateField> Fields,
    IReadOnlyList<string> ReferenceNames)
{
    /// <summary>How many <see cref="DomainQuantityType.Integer"/> fields the kind declares.</summary>
    public int IntegerFieldCount => Fields.Count(f => f.Type == DomainQuantityType.Integer);

    /// <summary>How many <see cref="DomainQuantityType.Real"/> fields the kind declares.</summary>
    public int RealFieldCount => Fields.Count(f => f.Type == DomainQuantityType.Real);
}

/// <summary>
/// The declared, versioned state schema of one domain: what record kinds exist and how their
/// field vectors are laid out. Persisted in the domain-state artifact header; readers reject
/// unknown versions instead of guessing.
/// </summary>
public sealed record DomainStateSchema(
    string DomainId,
    int Version,
    IReadOnlyList<DomainRecordKind> Kinds)
{
    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> when a kind or field name is duplicated,
    /// a unit or layer label is null, empty, or whitespace, a conserved field is real or
    /// missing its family, or a neutral field carries a family. The message names the
    /// offending kind and field.
    /// </summary>
    public void Validate()
    {
        var kindNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var kind in Kinds)
        {
            if (!kindNames.Add(kind.Name))
            {
                throw new InvalidOperationException(
                    $"Domain '{DomainId}' kind '{kind.Name}' is declared more than once.");
            }

            if (string.IsNullOrWhiteSpace(kind.Layer))
            {
                throw new InvalidOperationException(
                    $"Domain '{DomainId}' kind '{kind.Name}' has an empty layer.");
            }

            var fieldNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in kind.Fields)
            {
                if (!fieldNames.Add(field.Name))
                {
                    throw new InvalidOperationException(
                        $"Domain '{DomainId}' kind '{kind.Name}' field '{field.Name}' is declared more than once.");
                }

                if (string.IsNullOrWhiteSpace(field.Unit))
                {
                    throw new InvalidOperationException(
                        $"Domain '{DomainId}' kind '{kind.Name}' field '{field.Name}' has an empty unit.");
                }

                if (field.Role != DomainConservationRole.None)
                {
                    if (field.Type == DomainQuantityType.Real)
                    {
                        throw new InvalidOperationException(
                            $"Domain '{DomainId}' kind '{kind.Name}' field '{field.Name}' is conserved but real; conserved fields must be integers.");
                    }

                    if (string.IsNullOrEmpty(field.Family))
                    {
                        throw new InvalidOperationException(
                            $"Domain '{DomainId}' kind '{kind.Name}' field '{field.Name}' has role {field.Role} without a family.");
                    }
                }
                else if (!string.IsNullOrEmpty(field.Family))
                {
                    throw new InvalidOperationException(
                        $"Domain '{DomainId}' kind '{kind.Name}' field '{field.Name}' has no role but declares family '{field.Family}'.");
                }
            }
        }
    }

    /// <summary>Finds a declared kind by name; throws when the schema does not declare it.</summary>
    public DomainRecordKind FindKind(string name)
    {
        foreach (var kind in Kinds)
        {
            if (string.Equals(kind.Name, name, StringComparison.Ordinal))
            {
                return kind;
            }
        }

        throw new InvalidOperationException(
            $"Domain '{DomainId}' declares no kind '{name}'.");
    }
}
