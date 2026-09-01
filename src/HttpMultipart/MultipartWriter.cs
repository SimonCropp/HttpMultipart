namespace HttpMultipart;

using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Writes multipart framing to a stream: parts opened with a boundary line and headers, content written
/// by the caller, the whole body closed by a terminator. The delimiter's leading CRLF is written by the
/// <b>next</b> part (or by the terminator), which is what keeps every part's content byte-exact — a
/// reader strips that CRLF as part of the delimiter.
/// </summary>
/// <remarks>
/// The boundary is fixed for the body, so every framing byte that does not depend on the part is encoded
/// once here rather than interpolated and re-encoded per part. A streaming caller opens a part per row,
/// usually with the same content type, so the opening bytes for the last type opened are kept and
/// reused — which makes the per-part framing cost a single write of a cached array.
/// </remarks>
sealed class MultipartWriter
{
    readonly Stream body;
    readonly byte[] firstDelimiter;
    readonly byte[] delimiter;
    readonly byte[] terminator;
    readonly byte[] emptyTerminator;

    // The last content type OpenPart was called with, and the complete opening bytes for it — the
    // delimiter and the headers together, since after the first part those never differ again.
    string? openedType;
    byte[]? opening;

    bool first = true;

    public MultipartWriter(Stream body, string boundary, string mediaType = "multipart/mixed")
    {
        this.body = body;
        Boundary = boundary;
        ContentType = $"{mediaType}; boundary={boundary}";
        firstDelimiter = Encoding.ASCII.GetBytes($"--{boundary}\r\n");
        delimiter = Encoding.ASCII.GetBytes($"\r\n--{boundary}\r\n");
        terminator = Encoding.ASCII.GetBytes($"\r\n--{boundary}--\r\n");
        // A body with no parts at all has no preceding part for the delimiter's CRLF to close.
        emptyTerminator = Encoding.ASCII.GetBytes($"--{boundary}--\r\n");
    }

    /// <summary>The boundary, without the <c>--</c> markers.</summary>
    public string Boundary { get; }

    /// <summary>The value to send as the <c>Content-Type</c> of the whole body.</summary>
    public string ContentType { get; }

    /// <summary>
    /// A writer over a fresh boundary. The content is never scanned for collisions: 122 bits of
    /// randomness make an accidental delimiter sequence in part content cryptographically negligible,
    /// the same bet the BCL's own multipart writers make.
    /// </summary>
    public static MultipartWriter Create(Stream body, string mediaType = "multipart/mixed", string boundaryPrefix = "") =>
        new(body, boundaryPrefix + Guid.NewGuid().ToString("N"), mediaType);

    /// <summary>Opens a part of <paramref name="contentType"/>; the caller writes the content raw.</summary>
    public async Task OpenPart(string contentType, CancellationToken cancel = default)
    {
        // Cached past the first part, where the delimiter stops differing — so a caller opening a part
        // per row writes one array it built once.
        if (!first &&
            openedType == contentType &&
            opening is { } cached)
        {
            await body.WriteAsync(cached, cancel);
            return;
        }

        var headers = Encoding.ASCII.GetBytes($"Content-Type: {contentType}\r\n\r\n");
        var wasFirst = first;
        await Open(headers, cancel);
        if (!wasFirst)
        {
            openedType = contentType;
            opening = [.. delimiter, .. headers];
        }
    }

    /// <summary>
    /// Opens a part of <paramref name="contentType"/> and writes <paramref name="content"/>, declaring
    /// its length.
    /// </summary>
    public async Task WritePart(string contentType, ReadOnlyMemory<byte> content, CancellationToken cancel = default)
    {
        // Content-Length differs per part, so these headers are built each time rather than cached.
        await Open(
            Encoding.ASCII.GetBytes($"Content-Type: {contentType}\r\nContent-Length: {content.Length}\r\n\r\n"),
            cancel);
        await body.WriteAsync(content, cancel);
    }

    /// <summary>Closes the body. Nothing may be written after this.</summary>
    public Task Terminate(CancellationToken cancel = default) =>
        body.WriteAsync(first ? emptyTerminator : terminator, cancel).AsTask();

    async Task Open(byte[] headers, CancellationToken cancel)
    {
        await body.WriteAsync(first ? firstDelimiter : delimiter, cancel);
        first = false;
        await body.WriteAsync(headers, cancel);
    }
}
