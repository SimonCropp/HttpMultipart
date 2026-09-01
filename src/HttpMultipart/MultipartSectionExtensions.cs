namespace HttpMultipart;

using System;
using System.IO;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

/// <summary>Extension methods for reading the body of a <see cref="MultipartSection"/>.</summary>
static class MultipartSectionExtensions
{
    const int maxPresize = 1024 * 1024;

    /// <summary>Reads the section body to a byte array.</summary>
    public static async Task<byte[]> ReadAsBytesAsync(this MultipartSection section, CancellationToken cancel = default)
    {
        // Content-Length sizes the initial buffer and nothing else: it is never trusted for the read,
        // and it is capped because it comes from the part itself. Uncapped, a part declaring two
        // gigabytes over a one-byte body would have that allocated before a byte was read.
        using var memory = section.ContentLength is { } length
            ? new MemoryStream((int) Math.Min(length, maxPresize))
            : new MemoryStream();
        await section.Body.CopyToAsync(memory, cancel);
        return memory.ToArray();
    }

    /// <summary>
    /// Reads the section body as a string, decoded with the charset named by its <c>Content-Type</c>,
    /// or UTF-8 where the section names none this runtime can supply.
    /// </summary>
    public static async Task<string> ReadAsStringAsync(this MultipartSection section, CancellationToken cancel = default)
    {
        using var reader = new StreamReader(
            section.Body,
            EncodingFor(section),
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 1024,
            leaveOpen: true);
        return await reader.ReadToEndAsync(cancel);
    }

    static Encoding EncodingFor(MultipartSection section)
    {
        if (!MediaTypeHeaderValue.TryParse(section.ContentType, out var mediaType) ||
            mediaType.CharSet is not {Length: > 0} charSet)
        {
            return Encoding.UTF8;
        }

        try
        {
            var encoding = Encoding.GetEncoding(charSet.Trim('"'));
            // UTF-7 is obsolete and unsafe to decode. The runtime already refuses it below unless the
            // consumer has switched it back on, and this is what declines it in that case too:
            // https://learn.microsoft.com/dotnet/core/compatibility/syslib-warnings/syslib0001
            if (encoding.CodePage == 65000)
            {
                return Encoding.UTF8;
            }

            return encoding;
        }
        // ArgumentException: a charset this runtime has no provider for. NotSupportedException: one it
        // has withdrawn, which is how it answers utf-7. Either way the body is far more likely to be
        // UTF-8 than to be unreadable, and the alternative is throwing from a read no caller can retry.
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return Encoding.UTF8;
        }
    }
}
