#nullable enable

using System;

namespace OpenSage.Content.Importing;

/// <summary>
/// Result of explicit format selection: either a usable selection, or a stable error code.
/// Failures carry no exception; callers surface the code through their diagnostics table.
/// </summary>
public sealed class ExternalFormatSelectionResult
{
    public const string UnsupportedFormatCode = "EXT001";

    public bool IsSuccess { get; }

    public ExternalFormatSelection? Selection { get; }

    public string? ErrorCode { get; }

    public string? ErrorMessage { get; }

    private ExternalFormatSelectionResult(
        bool isSuccess,
        ExternalFormatSelection? selection,
        string? errorCode,
        string? errorMessage)
    {
        IsSuccess = isSuccess;
        Selection = selection;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public static ExternalFormatSelectionResult Success(ExternalFormatSelection selection)
    {
        if (selection is null)
        {
            throw new ArgumentNullException(nameof(selection));
        }

        return new ExternalFormatSelectionResult(true, selection, null, null);
    }

    public static ExternalFormatSelectionResult UnsupportedFormat(string fileName)
    {
        return new ExternalFormatSelectionResult(
            false,
            null,
            UnsupportedFormatCode,
            $"'{fileName}' does not use a supported external model format.");
    }
}
