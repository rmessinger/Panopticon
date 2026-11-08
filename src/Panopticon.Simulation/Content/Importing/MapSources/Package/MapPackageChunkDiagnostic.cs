#nullable enable

namespace Panopticon.Content.Importing.MapSources.Package;

/// <summary>Stable diagnostic codes for chunk file IO: path guard rejections, digest verification, and staged package writes.</summary>
public static class MapPackageChunkDiagnostic
{
    public const string PathEscape = "path-escape";

    public const string SymlinkRejected = "symlink-rejected";

    public const string FileMissing = "file-missing";

    public const string LengthMismatch = "chunk-length-mismatch";

    public const string DigestMismatch = "chunk-digest-mismatch";

    public const string EncodingMismatch = "chunk-encoding-mismatch";

    public const string IoFailure = "chunk-io-failure";

    public const string TargetPathInvalid = "target-path-invalid";

    public const string StagingFailed = "staging-failed";

    public const string StagedVerificationFailed = "staged-verification-failed";

    public const string StagedManifestInvalid = "staged-manifest-invalid";

    public const string CommitFailed = "commit-failed";

    public const string PayloadProviderFailed = "payload-provider-failed";

    public const string CleanupFailed = "cleanup-failed";

    public const string RecoveryRequired = "recovery-required";
}
