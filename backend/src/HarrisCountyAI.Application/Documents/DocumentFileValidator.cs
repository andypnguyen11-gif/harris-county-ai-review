namespace HarrisCountyAI.Application.Documents;

/// <summary>
/// Validates uploaded document files against the allowed extensions, allowed
/// MIME types, and the configured maximum file size. Returns a
/// <see cref="DocumentFileValidationResult"/> with error reasons rather than
/// throwing for invalid input.
/// </summary>
public sealed class DocumentFileValidator
{
    /// <summary>Default maximum file size: 50 MB.</summary>
    public const long DefaultMaxFileSizeBytes = 50L * 1024 * 1024;

    private static readonly string[] DefaultAllowedExtensions =
        [".pdf", ".png", ".jpg", ".jpeg", ".tif", ".tiff"];

    private static readonly string[] DefaultAllowedContentTypes =
        ["application/pdf", "image/png", "image/jpeg", "image/tiff"];

    private readonly HashSet<string> _allowedExtensions;
    private readonly HashSet<string> _allowedContentTypes;

    public DocumentFileValidator(
        long maxFileSizeBytes = DefaultMaxFileSizeBytes,
        IEnumerable<string>? allowedExtensions = null,
        IEnumerable<string>? allowedContentTypes = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxFileSizeBytes);

        MaxFileSizeBytes = maxFileSizeBytes;
        _allowedExtensions = new HashSet<string>(
            allowedExtensions ?? DefaultAllowedExtensions,
            StringComparer.OrdinalIgnoreCase);
        _allowedContentTypes = new HashSet<string>(
            allowedContentTypes ?? DefaultAllowedContentTypes,
            StringComparer.OrdinalIgnoreCase);
    }

    public long MaxFileSizeBytes { get; }

    /// <summary>
    /// Validates the file's name (extension), declared content type, and size.
    /// All rules are evaluated so the result carries every failure at once.
    /// </summary>
    public DocumentFileValidationResult Validate(string? fileName, string? contentType, long fileSizeBytes)
    {
        var errors = new List<string>();

        ValidateFileName(fileName, errors);
        ValidateContentType(contentType, errors);
        ValidateSize(fileSizeBytes, errors);

        return errors.Count == 0
            ? DocumentFileValidationResult.Success
            : DocumentFileValidationResult.Failure(errors);
    }

    private void ValidateFileName(string? fileName, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            errors.Add("A file name is required.");
            return;
        }

        var extension = Path.GetExtension(fileName.Trim());
        if (string.IsNullOrEmpty(extension))
        {
            errors.Add($"File '{fileName}' has no extension. Allowed extensions: {FormatAllowed(_allowedExtensions)}.");
            return;
        }

        if (!_allowedExtensions.Contains(extension))
        {
            errors.Add($"File extension '{extension}' is not allowed. Allowed extensions: {FormatAllowed(_allowedExtensions)}.");
        }
    }

    private void ValidateContentType(string? contentType, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            errors.Add("A content type is required.");
            return;
        }

        // Ignore parameters such as "; charset=utf-8".
        var mediaType = contentType.Split(';', 2)[0].Trim();
        if (!_allowedContentTypes.Contains(mediaType))
        {
            errors.Add($"Content type '{mediaType}' is not allowed. Allowed content types: {FormatAllowed(_allowedContentTypes)}.");
        }
    }

    private void ValidateSize(long fileSizeBytes, List<string> errors)
    {
        if (fileSizeBytes <= 0)
        {
            errors.Add("The file is empty.");
            return;
        }

        if (fileSizeBytes > MaxFileSizeBytes)
        {
            errors.Add($"The file is {fileSizeBytes:N0} bytes, which exceeds the maximum allowed size of {MaxFileSizeBytes:N0} bytes.");
        }
    }

    private static string FormatAllowed(IEnumerable<string> values)
        => string.Join(", ", values.Order(StringComparer.OrdinalIgnoreCase));
}
