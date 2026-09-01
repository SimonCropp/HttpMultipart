namespace HttpMultipart;

using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net.Http;

/// <summary>Extension methods for finding the multipart boundary of an <see cref="HttpContent"/>.</summary>
static class MultipartContentExtensions
{
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
        if (!string.Equals(content.Headers.ContentType?.MediaType, mediaType, StringComparison.OrdinalIgnoreCase))
        {
            boundary = null;
            return false;
        }

        return content.TryGetMultipartBoundary(out boundary);
    }
}
