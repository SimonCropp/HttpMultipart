// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Vendored from dotnet/aspnetcore, src/Http/WebUtilities, and adapted to this project's conventions:
// headers are single-valued strings.

namespace HttpMultipart;

using System.Collections.Generic;
using System.IO;

/// <summary>A multipart section read by <see cref="MultipartReader"/>.</summary>
sealed class MultipartSection
{
    /// <summary>The value of the <c>Content-Type</c> header, or null.</summary>
    public string? ContentType => Header("Content-Type");

    /// <summary>The value of the <c>Content-Disposition</c> header, or null.</summary>
    public string? ContentDisposition => Header("Content-Disposition");

    /// <summary>
    /// The value of the <c>Content-Length</c> header, or null. Advisory — it sizes a buffer and is
    /// never trusted for a read. <c>long</c> because the header is a 64-bit quantity: parsed as an
    /// <c>int</c>, a part declaring more than two gigabytes would read as null, which is
    /// indistinguishable from a part declaring nothing.
    /// </summary>
    public long? ContentLength
    {
        get
        {
            if (Header("Content-Length") is { } value &&
                long.TryParse(value, out var length) &&
                length >= 0)
            {
                return length;
            }

            return null;
        }
    }

    /// <summary>The section headers. Names are compared case-insensitively, and a repeated name is last-wins.</summary>
    public Dictionary<string, string>? Headers { get; set; }

    /// <summary>The section body. Forward-only, and valid only until the next section is read.</summary>
    public Stream Body { get; set; } = null!;

    string? Header(string name)
    {
        if (Headers is not null &&
            Headers.TryGetValue(name, out var value))
        {
            return value;
        }

        return null;
    }
}
