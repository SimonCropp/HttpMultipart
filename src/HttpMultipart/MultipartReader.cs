// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Vendored from dotnet/aspnetcore, src/Http/WebUtilities, and adapted to this project's conventions:
// headers are single-valued strings, and boundary de-quoting is inlined.

namespace HttpMultipart;

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

// https://www.ietf.org/rfc/rfc2046.txt
/// <summary>Reads multipart content from a <see cref="Stream"/>.</summary>
sealed class MultipartReader :
    IDisposable
{
    /// <summary>The default value for <see cref="HeadersCountLimit"/>.</summary>
    public const int DefaultHeadersCountLimit = 16;

    /// <summary>The default value for <see cref="HeadersLengthLimit"/> — approximately 16KB.</summary>
    public const int DefaultHeadersLengthLimit = 1024 * 16;

    const int defaultBufferSize = 1024 * 4;
    const int drainBufferSize = 1024 * 4;

    BufferedReadStream stream;
    MultipartBoundary boundary;
    MultipartReaderStream? currentStream;

    public MultipartReader(string boundary, Stream stream, int bufferSize = defaultBufferSize)
    {
        // Size of the boundary + leading and trailing CRLF + leading and trailing '--' markers.
        if (bufferSize < boundary.Length + 8)
        {
            throw new ArgumentOutOfRangeException(nameof(bufferSize), bufferSize, "Insufficient buffer space, the buffer must be larger than the boundary: " + boundary);
        }

        // leaveOpen: the stream belongs to the caller. Disposing this reader returns the pooled
        // buffer without closing what it was handed.
        this.stream = new(stream, bufferSize, leaveOpen: true);
        this.boundary = new(RemoveQuotes(boundary));
    }

    /// <summary>The limit for the number of headers to read.</summary>
    public int HeadersCountLimit { get; set; } = DefaultHeadersCountLimit;

    /// <summary>The combined size limit for headers per multipart section.</summary>
    public int HeadersLengthLimit { get; set; } = DefaultHeadersLengthLimit;

    /// <summary>
    /// The optional limit for the body length of each multipart section. The transport is responsible
    /// for limiting the overall body length.
    /// </summary>
    public long? BodyLengthLimit { get; set; }

    /// <summary>Reads the next <see cref="MultipartSection"/>, or null at the end of the body.</summary>
    public async Task<MultipartSection?> ReadNextSectionAsync(CancellationToken cancel = default)
    {
        // Only occurs on first call. This stream will drain any preamble data and remove the first
        // boundary marker.
        currentStream ??= new(stream, boundary)
        {
            LengthLimit = HeadersLengthLimit
        };

        // Drain the prior section.
        await Drain(currentStream, limit: null, cancel);
        // If we're at the end return null.
        if (currentStream.FinalBoundaryFound)
        {
            // There may be trailer data after the last boundary.
            await Drain(stream, HeadersLengthLimit, cancel);
            return null;
        }

        var headers = await ReadHeaders(cancel);
        boundary.ExpectLeadingCrlf();
        currentStream = new(stream, boundary)
        {
            LengthLimit = BodyLengthLimit
        };
        return new()
        {
            Headers = headers,
            Body = currentStream
        };
    }

    async Task<Dictionary<string, string>> ReadHeaders(CancellationToken cancel)
    {
        var totalSize = 0;
        // Single-valued, last-wins: the sections this client reads never carry a repeated header.
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var line = await stream.ReadLineAsync(HeadersLengthLimit, cancel);
        while (!string.IsNullOrEmpty(line))
        {
            if (HeadersLengthLimit - totalSize < line.Length)
            {
                throw new InvalidDataException($"Multipart headers length limit {HeadersLengthLimit} exceeded.");
            }

            totalSize += line.Length;
            var splitIndex = line.IndexOf(':');
            if (splitIndex <= 0)
            {
                throw new InvalidDataException($"Invalid header line: {line}");
            }

            var name = line[..splitIndex];
            var value = line[(splitIndex + 1)..].Trim();
            headers[name] = value;
            if (headers.Count > HeadersCountLimit)
            {
                throw new InvalidDataException($"Multipart headers count limit {HeadersCountLimit} exceeded.");
            }

            line = await stream.ReadLineAsync(HeadersLengthLimit - totalSize, cancel);
        }

        return headers;
    }

    /// <summary>
    /// Returns the pooled read buffer. The stream this reader was constructed over is left open — it
    /// belongs to the caller.
    /// </summary>
    /// <remarks>
    /// Optional, and skipping it is what every version before this one did: the buffer is then simply
    /// garbage rather than returned, so a reader costs one allocation of its buffer size. Disposing
    /// ends the life of every section this reader produced, so do it after the last one is read.
    /// </remarks>
    public void Dispose() =>
        stream.Dispose();

    // Upstream's StreamHelperExtensions.DrainAsync, inlined. As an extension on Stream it put a
    // DrainAsync on every stream in a consuming project, and this reader is the only caller it ever had.
    static async Task Drain(Stream stream, long? limit, CancellationToken cancel)
    {
        cancel.ThrowIfCancellationRequested();
        var buffer = ArrayPool<byte>.Shared.Rent(drainBufferSize);
        long total = 0;
        try
        {
            var read = await stream.ReadAsync(buffer.AsMemory(), cancel);
            while (read > 0)
            {
                // Not all streams support cancellation directly.
                cancel.ThrowIfCancellationRequested();
                if (limit.HasValue &&
                    limit.GetValueOrDefault() - total < read)
                {
                    throw new InvalidDataException($"The stream exceeded the data limit {limit.GetValueOrDefault()}.");
                }

                total += read;
                read = await stream.ReadAsync(buffer.AsMemory(), cancel);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    // The one member of Microsoft.Net.Http.Headers.HeaderUtilities the reader needed, inlined.
    static string RemoveQuotes(string value)
    {
        if (value.Length > 1 &&
            value[0] == '"' &&
            value[^1] == '"')
        {
            return value[1..^1];
        }

        return value;
    }
}
