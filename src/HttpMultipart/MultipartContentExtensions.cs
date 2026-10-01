namespace HttpMultipart;

using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net.Http;

/// <summary>Extension methods for the media type and multipart boundary of an <see cref="HttpContent"/>.</summary>
static class MultipartContentExtensions
{
    /// <summary>
    /// Whether the content's <c>Content-Type</c> names <paramref name="mediaType"/>, compared without
    /// case and ignoring any parameters. Content with no <c>Content-Type</c> names none.
    /// </summary>
    public static bool HasMediaType(this HttpContent content, string mediaType) =>
        string.Equals(content.Headers.ContentType?.MediaType, mediaType, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The <c>boundary</c> parameter of the content type, where the content declares one. The value is
    /// passed on as it arrived — <see cref="MultipartReader"/> strips the quotes from a quoted boundary
    /// itself.
    /// </summary>
    public static bool TryGetMultipartBoundary(this HttpContent content, [NotNullWhen(true)] out string? boundary)
    {
        boundary = content.Headers.ContentType?
            .Parameters
            .FirstOrDefault(_ => string.Equals(_.Name, "boundary", StringComparison.OrdinalIgnoreCase))
            ?.Value;

        if (string.IsNullOrEmpty(boundary))
        {
            boundary = null;
            return false;
        }

        return true;
    }

    /// <summary>
    /// As <see cref="TryGetMultipartBoundary(HttpContent, out string)"/>, but false unless the content
    /// is of <paramref name="mediaType"/>.
    /// </summary>
    public static bool TryGetMultipartBoundary(this HttpContent content, string mediaType, [NotNullWhen(true)] out string? boundary)
    {
        if (!content.HasMediaType(mediaType))
        {
            boundary = null;
            return false;
        }

        return content.TryGetMultipartBoundary(out boundary);
    }
}
