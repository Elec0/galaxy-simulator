namespace GalaxyCommand.Content;

/// <summary>Maps diagnostic categories to stable headless-tool codes.</summary>
public static class ContentDiagnosticCodes
{
    /// <summary>
    /// Returns the stable textual code for a diagnostic category. Codes are
    /// explicit so enum insertion or reordering cannot change tool output.
    /// </summary>
    public static string GetCode(ContentDiagnosticKind kind) => kind switch
    {
        ContentDiagnosticKind.DocumentTooLarge => "GC-CONTENT-0001",
        ContentDiagnosticKind.InvalidUtf8 => "GC-CONTENT-0002",
        ContentDiagnosticKind.InvalidJson => "GC-CONTENT-0003",
        ContentDiagnosticKind.DuplicateProperty => "GC-CONTENT-0004",
        ContentDiagnosticKind.UnknownProperty => "GC-CONTENT-0005",
        ContentDiagnosticKind.MissingProperty => "GC-CONTENT-0006",
        ContentDiagnosticKind.InvalidValue => "GC-CONTENT-0007",
        ContentDiagnosticKind.LimitExceeded => "GC-CONTENT-0008",
        ContentDiagnosticKind.WrongFormat => "GC-CONTENT-0009",
        ContentDiagnosticKind.UnsupportedSchemaVersion => "GC-CONTENT-0010",
        ContentDiagnosticKind.MissingDependency => "GC-CONTENT-0011",
        ContentDiagnosticKind.DependencyCycle => "GC-CONTENT-0012",
        ContentDiagnosticKind.IdentityCollision => "GC-CONTENT-0013",
        ContentDiagnosticKind.UnresolvedReference => "GC-CONTENT-0014",
        ContentDiagnosticKind.UnsupportedContentKind => "GC-CONTENT-0015",
        ContentDiagnosticKind.StorageAccess => "GC-CONTENT-0016",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown content diagnostic kind."),
    };
}
