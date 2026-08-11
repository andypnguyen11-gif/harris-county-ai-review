namespace HarrisCountyAI.Application.Documents.GetDocumentContent;

/// <summary>
/// Maps a stored file name to the MIME type a browser needs in order to render
/// it inline rather than download it. Uploads are restricted to this set by
/// <see cref="DocumentFileValidator"/>, so anything unrecognized is treated as
/// opaque bytes.
/// </summary>
public static class DocumentContentTypes
{
    /// <summary>Served for extensions the system does not recognize; browsers will download rather than render.</summary>
    public const string Fallback = "application/octet-stream";

    private static readonly Dictionary<string, string> ByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".tif"] = "image/tiff",
        [".tiff"] = "image/tiff",
    };

    /// <summary>Returns the MIME type for the file's extension, or <see cref="Fallback"/>.</summary>
    public static string FromFileName(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);

        var extension = Path.GetExtension(fileName);
        return extension.Length > 0 && ByExtension.TryGetValue(extension, out var contentType)
            ? contentType
            : Fallback;
    }
}
