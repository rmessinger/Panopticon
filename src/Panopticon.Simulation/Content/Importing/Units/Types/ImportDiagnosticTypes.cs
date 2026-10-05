namespace Panopticon.Content.Importing.Units;

public enum ImportDiagnosticSeverity
{
    Info,
    Warning,

    /// <summary>Blocks publication of the whole import.</summary>
    Error,
}

public enum ImportStage
{
    Read,
    Resolve,
    Validate,
    Materialize,
    Publish,
}
