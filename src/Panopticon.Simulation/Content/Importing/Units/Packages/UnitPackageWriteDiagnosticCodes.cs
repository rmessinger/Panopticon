#nullable enable

namespace Panopticon.Content.Importing.Units.Packages;

/// <summary>Stable diagnostic codes for unit package writing and staged verification.</summary>
public static class UnitPackageWriteDiagnosticCodes
{
    public const string WriteRefused = "H4-WRITE-REFUSED";
    public const string RecordUnsupported = "H4-WRITE-RECORD-UNSUPPORTED";
    public const string RecordDuplicate = "H4-WRITE-RECORD-DUPLICATE";
    public const string SourceMismatch = "H4-WRITE-SOURCE-MISMATCH";
    public const string NameInvalid = "H4-WRITE-NAME-INVALID";
    public const string PathInvalid = "H4-WRITE-PATH-INVALID";
    public const string PathDuplicate = "H4-WRITE-PATH-DUPLICATE";
    public const string RecordCountExceeded = "H4-WRITE-RECORD-LIMIT";
    public const string PayloadTooLarge = "H4-WRITE-PAYLOAD-LIMIT";
    public const string QuantityInvalid = "H4-WRITE-QUANTITY-INVALID";
    public const string RetainedBlockMissing = "H4-WRITE-RETAINED-BLOCK-MISSING";
    public const string RetainedDialectUnsupported = "H4-WRITE-RETAINED-DIALECT";
    public const string TargetPathInvalid = "H4-WRITE-TARGET-INVALID";
    public const string StagingFailed = "H4-WRITE-STAGING-FAILED";
    public const string PayloadProviderFailed = "H4-WRITE-PAYLOAD-PROVIDER-FAILED";
    public const string StagedVerificationFailed = "H4-WRITE-STAGED-VERIFY-FAILED";
    public const string StagedManifestInvalid = "H4-WRITE-STAGED-MANIFEST-INVALID";
    public const string CommitFailed = "H4-WRITE-COMMIT-FAILED";
    public const string RecoveryRequired = "H4-WRITE-RECOVERY-REQUIRED";
    public const string CleanupFailed = "H4-WRITE-CLEANUP-FAILED";
}
