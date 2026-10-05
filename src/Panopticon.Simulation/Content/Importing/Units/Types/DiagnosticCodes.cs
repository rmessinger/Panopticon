namespace Panopticon.Content.Importing.Units;

/// <summary>Stable diagnostic codes asserted by tests and reports.</summary>
public static class DiagnosticCodes
{
    public const string DuplicateRecord = "H2A-DUPLICATE-RECORD";
    public const string MissingParent = "H2A-MISSING-PARENT";
    public const string ParentKindMismatch = "H2A-PARENT-KIND";
    public const string InheritanceCycle = "H2A-INHERITANCE-CYCLE";
    public const string InheritanceTooDeep = "H2A-INHERITANCE-DEPTH";
    public const string MissingRoot = "H2A-MISSING-ROOT";
    public const string MissingDependency = "H2A-MISSING-DEPENDENCY";
    public const string MissingOptionalDependency = "H2A-MISSING-OPTIONAL";
    public const string WrongDependencyKind = "H2A-DEPENDENCY-KIND";
    public const string DependencyCycle = "H2A-DEPENDENCY-CYCLE";
    public const string UnsupportedMechanic = "H2A-UNSUPPORTED-MECHANIC";
    public const string NonGameplayMetadata = "H2A-NON-GAMEPLAY-METADATA";
    public const string UnsupportedModule = "H2A-UNSUPPORTED-MODULE";
    public const string LegacyBindingNotAllowed = "H2A-LEGACY-BINDING";
    public const string UnsupportedAbility = "H2A-UNSUPPORTED-ABILITY";
    public const string MissingRequiredValue = "H2A-MISSING-VALUE";
    public const string InvalidNumber = "H2A-INVALID-NUMBER";
    public const string UnitMismatch = "H2A-UNIT-MISMATCH";
    public const string InvalidEnumValue = "H2A-INVALID-ENUM";
    public const string MissingVisualState = "H2A-MISSING-VISUAL-STATE";
    public const string NoVisualBinding = "H2A-NO-VISUAL";
    public const string MaterializationFailed = "H2A-MATERIALIZE";
    public const string NameCollision = "H2A-NAME-COLLISION";
    public const string PublicationFailed = "H2A-PUBLISH";
}
