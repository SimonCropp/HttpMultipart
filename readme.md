# HttpMultipart

[![Build status](https://github.com/SimonCropp/HttpMultipart/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/VerifyTests/HttpMultipart/actions/workflows/ci.yml)
[![NuGet Status](https://img.shields.io/nuget/v/HttpMultipart.svg?label=HttpMultipart)](https://www.nuget.org/packages/HttpMultipart/)

A source-only NuGet package for reading and writing HTTP multipart content.

The reader is vendored from [dotnet/aspnetcore](https://github.com/dotnet/aspnetcore)'s `Microsoft.AspNetCore.WebUtilities` and stripped of its dependencies, so it can be used anywhere a `Stream` can - a Blazor WebAssembly client, a console app, a library that does not want `Microsoft.Extensions.Primitives` and `Microsoft.Net.Http.Headers` in its dependency graph.

Because the package ships C# source rather than an assembly, it compiles into the consuming project. There is no runtime dependency, nothing to deploy, and the types stay `internal` to whoever compiled them.


## Installation

Requires `net10.0` or later.

```xml
<PackageReference Include="HttpMultipart" Version="*" PrivateAssets="all" />
```

`PrivateAssets="all"` keeps the package out of the dependency graph of anything that references the consuming project. The source is already compiled into that assembly, so there is nothing to flow onwards.


## Namespace

The types live in the `HttpMultipart` namespace, and the package adds a global `using` for it wherever the consuming project has implicit usings enabled. If it does not, add the using explicitly:

```csharp
using HttpMultipart;
```

The namespace matters: without it a global-namespace `MultipartReader` would silently win name lookup over `Microsoft.AspNetCore.WebUtilities.MultipartReader` in an ASP.NET Core app.


## Reading

<!-- snippet: read -->
<a id='snippet-read'></a>
```cs
var parts = new List<string>();
if (response.Content.TryGetMultipartBoundary(out var boundary))
{
    await using var body = await response.Content.ReadAsStreamAsync();
    // Disposing returns the reader's pooled buffer; it leaves the body stream alone.
    using var reader = new MultipartReader(boundary, body);
    while (await reader.ReadNextSectionAsync() is {} section)
    {
        parts.Add(await section.ReadAsStringAsync());
    }
}
```
<sup><a href='/src/Tests/Usage.cs#L9-L23' title='Snippet source file'>snippet source</a> | <a href='#snippet-read' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

`ReadNextSectionAsync` is forward-only, and a section's `Body` is valid only until the next section is read. Read or copy the content before moving on.

For binary parts, `ReadAsBytesAsync` buffers the whole part. It uses `Content-Length` to size the initial buffer - capped, since the header comes from the part itself - and never trusts it for the read. See [Large payloads](#large-payloads) before applying it to anything unbounded:

<!-- snippet: readBinary -->
<a id='snippet-readBinary'></a>
```cs
response.Content.TryGetMultipartBoundary("multipart/mixed", out var boundary);
await using var body = await response.Content.ReadAsStreamAsync();
using var reader = new MultipartReader(boundary!, body)
{
    // The transport bounds the whole body; this bounds any one part.
    BodyLengthLimit = 10 * 1024 * 1024
};
while (await reader.ReadNextSectionAsync() is {} section)
{
    var bytes = await section.ReadAsBytesAsync();
    Handle(section.ContentType, bytes);
}
```
<sup><a href='/src/Tests/Usage.cs#L33-L48' title='Snippet source file'>snippet source</a> | <a href='#snippet-readBinary' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->


### Limits

| Property | Default | What it bounds |
| --- | --- | --- |
| `HeadersCountLimit` | 16 | Distinct header names per section |
| `HeadersLengthLimit` | 16 KiB | Combined headers per section, and the preamble, and the epilogue |
| `BodyLengthLimit` | **none** | Bytes in any one section body |

Exceeding a limit throws `InvalidDataException`. The transport is responsible for bounding the overall body length; these bound what one section can cost.

`BodyLengthLimit` defaulting to no limit matches `Microsoft.AspNetCore.WebUtilities`, and costs nothing while a section is streamed - but **set it before reading untrusted input**, because it is the only thing bounding a part that is then buffered. Two things it does not bound: the number of sections, and the combined header size in *bytes* rather than UTF-16 chars, which multi-byte header values can push to roughly three times `HeadersLengthLimit`.


## Writing

<!-- snippet: write -->
<a id='snippet-write'></a>
```cs
var writer = MultipartWriter.Create(stream);
// The value to send as the Content-Type of the whole body.
var contentType = writer.ContentType;

// A part whose content the caller writes to the stream itself.
await writer.OpenPart("application/json");
await stream.WriteAsync("""{"ok":true}"""u8.ToArray());

// A part written whole, with a Content-Length.
await writer.WritePart("application/octet-stream", new byte[] {1, 2, 3});

await writer.Terminate();
```
<sup><a href='/src/Tests/Usage.cs#L58-L73' title='Snippet source file'>snippet source</a> | <a href='#snippet-write' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The delimiter's leading CRLF is written by the *next* part, or by the terminator - which is what keeps every part's content byte-exact, since a reader strips that CRLF as part of the delimiter.

`OpenPart` caches the framing bytes for a repeated content type, so a caller opening a part per row pays a single array write per part rather than re-encoding the delimiter each time.

A large part can declare its length without ever being held in memory:

<!-- snippet: writeLarge -->
<a id='snippet-writeLarge'></a>
```cs
var writer = MultipartWriter.Create(stream);

// Declares the length without the part ever being held in memory: it is copied from the source
// straight into the body.
await writer.WritePart("application/octet-stream", source, source.Length);

await writer.Terminate();
```
<sup><a href='/src/Tests/Usage.cs#L96-L106' title='Snippet source file'>snippet source</a> | <a href='#snippet-writeLarge' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

`OpenPart(contentType, contentLength)` is the same thing split in two, for a caller who wants to write the content itself rather than hand over a `Stream`. The length is advisory in both - nothing verifies that exactly that many bytes are written.


## API

| Type | Purpose |
| --- | --- |
| `MultipartReader` | Reads sections from a `Stream` |
| `MultipartSection` | One section: `Headers`, `Body`, `ContentType`, `ContentDisposition`, `ContentLength` |
| `MultipartWriter` | Writes multipart framing to a `Stream` |
| `MultipartSectionExtensions` | `ReadAsBytesAsync`, `ReadAsStringAsync` - both buffer the whole part |
| `MultipartContentExtensions` | `TryGetMultipartBoundary` on an `HttpContent` |
| `BufferedReadStream` | Line-reading buffered stream the reader is built on |


## Large payloads

The reader streams. `section.Body` is forward-only over a fixed buffer, so copying a part costs the same whether it is 64 KB or 4 MB - measured at **7.5 KB allocated, either way**. That is the path to use for anything unbounded.

The two helpers buffer, and are for parts known to be small. Against streaming the same 4 MB part:

| | Time | Allocated |
| --- | --- | --- |
| `section.Body.CopyToAsync(destination)` | 216 µs | **7.5 KB** |
| `section.ReadAsBytesAsync()` | 1,603 µs | 11,272 KB |
| `section.ReadAsStringAsync()` | 4,211 µs | 16,450 KB |

Buffering to bytes costs roughly 2.7× the part, and to a string roughly 4×.

**Set `BodyLengthLimit` before reading untrusted input.** It defaults to no limit, and it is the only thing bounding a part that goes on to be buffered. `Content-Length` is not that thing: it is advisory, it sizes the initial buffer and is capped, and it is never trusted for a read.

**Dispose the reader.** It rents its read buffer from `ArrayPool`, and disposing is what returns it - without that, every reader allocates a fresh one. Disposing takes a 16 MB read from 6.9 KB allocated to **2.88 KB, flat at any buffer size**. It leaves the caller's stream open, and it ends the life of every section it produced, so dispose after the last one is read. Skipping it is safe and is what the upstream shape does; it costs an allocation per reader.

**Leave `bufferSize` alone when copying with `CopyToAsync`.** A 16 MB part takes 1.2 ms at the 4 KB default and 56 ms at 1 MB, because `CopyToAsync` reads in 4 KB whatever the configured size: the base implementation asks for a buffer of 1 byte when a seekable stream reports a length at or below its position, which a section always does before its first read, so the override floors it at 4 KB. A larger internal buffer then buys nothing and costs cache.

**Read with a caller-supplied buffer for a larger one to pay off.** The same 16 MB part through a 256 KB read buffer over a 1 MB internal buffer takes 1.4 ms, and a read only scans as far as it could return, so the boundary search does not repeat over what it could not hand back.

To write a large part, declare the length and stream the content rather than materialising it - the `WritePart(contentType, Stream, contentLength)` overload above allocates 759× less than handing over a `ReadOnlyMemory<byte>`.

See [Benchmarks](Benchmarks/readme.md) for the full numbers and how to reproduce them.


## Alternatives

| | Reads | Writes | Adds to the dependency graph |
| --- | --- | --- | --- |
| **HttpMultipart** | any `multipart/*`, a section at a time | any `multipart/*`, straight to a `Stream` | nothing - it ships source |
| [`Microsoft.AspNetCore.WebUtilities`](https://www.nuget.org/packages/Microsoft.AspNetCore.WebUtilities) | any `multipart/*`, a section at a time | - | `Microsoft.Net.Http.Headers`, `Microsoft.Extensions.Primitives` |
| `System.Net.Http.MultipartContent` | - | any `multipart/*`, one `HttpContent` per part | nothing - it is the BCL |
| ASP.NET Core form binding (`IFormFile`) | `multipart/form-data` requests, buffered | - | nothing - it is the framework |
| [`HttpMultipartParser`](https://www.nuget.org/packages/HttpMultipartParser) | `multipart/form-data` | - | `Microsoft.IO.RecyclableMemoryStream`, `System.Buffers` |
| [`MimeKit`](https://www.nuget.org/packages/MimeKit) | all of MIME | all of MIME | `BouncyCastle.Cryptography`, `System.Security.Cryptography.Pkcs` |
| [`Microsoft.AspNet.WebApi.Client`](https://www.nuget.org/packages/Microsoft.AspNet.WebApi.Client) | any `multipart/*`, buffered | one `HttpContent` per part | `Newtonsoft.Json`, `Newtonsoft.Json.Bson`, `System.Memory`, `System.Threading.Tasks.Extensions` |

**`Microsoft.AspNetCore.WebUtilities`** is where this reader came from, and inside an ASP.NET Core app it is already there: `Microsoft.AspNetCore.WebUtilities.dll` sits in the shared framework alongside both of its dependencies, so `MultipartReader` there costs a `using` and nothing else. Use it there; what this package adds is the writer and the fixes below. Outside ASP.NET Core - a Blazor WebAssembly client, a console app, a library that does not want them - it is a `PackageReference` that puts two assemblies into the graph of everything downstream. Past the [four API differences](#differences-from-microsoftaspnetcorewebutilities), the reader here also carries fixes upstream does not. The ported upstream tests pass unchanged either way, so none of them changes how a well-formed body reads:

* `BufferedReadStream` overrides `Read(Span<byte>)`. Upstream does not, and both of `MultipartReaderStream`'s read paths call exactly that, so every read there falls to the base `Stream` shim - a pooled array and a full extra copy of the payload, per read.
* The boundary search scans only as far as a read could return, rather than over everything buffered. That is 3.8x on a 16 MB part read through a 64 KB buffer, where the internal buffer is larger than the caller's and the next read would otherwise repeat the search over what the last could not hand back.
* `MultipartReader` is `IDisposable`, so the read buffer it rents from `ArrayPool` goes back. Upstream has no disposal, so every reader drops one of its buffer size - the difference between 6.9 KB and 2.88 KB allocated for a 16 MB read, and far more at a larger buffer size.
* `BufferedReadStream`'s `Position` setter keeps its seek arithmetic in `long`. Upstream casts the difference to `int`, so a backward seek of 2^32 on a seekable stream over 2 GB truncates to zero and silently moves nothing.

**`System.Net.Http.MultipartContent`** and `MultipartFormDataContent` are the BCL's writers, and they are the right thing when the destination is an `HttpClient` request: `MultipartFormDataContent` writes the `Content-Disposition: form-data; name=...` headers a browser-style upload needs, which `MultipartWriter` does not - it frames a part with `Content-Type`, and optionally `Content-Length`. The shapes differ as much as the output: a `MultipartContent` is a collection of `HttpContent`, one object per part, assembled and then serialized, where `MultipartWriter` writes framing into a `Stream` the caller already holds - which is what a server writing a response body has, and what lets a part be opened and streamed into without existing as an object first. Neither the BCL nor `HttpClient` reads multipart back.

**ASP.NET Core form binding** - `HttpRequest.Form`, `IFormFile`, `[FromForm]` - is what a `multipart/form-data` endpoint should use. It is layered on the same reader, and adds what this package leaves out: `Content-Disposition` parsed into named fields and files, rather than the raw header value `MultipartSection.ContentDisposition` hands back. It buffers each section - in memory to `FormOptions.MemoryBufferThreshold`, 64 KB by default, and to a temp file past that - which is the trade for handing back a collection the action can bind against. It is request-side only, form-data only, and unavailable outside ASP.NET Core.

**`HttpMultipartParser`** is form-data only as well, but standalone, so it is the closest alternative for reading a form outside ASP.NET Core. It hands back fields and files by name, either parsed up front or through `ParameterHandler` and `FileHandler` delegates for the streaming parser, rather than as a forward-only sequence of sections; it does not write. Prefer it where the payload is a form and named access is the point. Prefer this package if the payload is `multipart/mixed`, or anything else where a part is a content type rather than a named field.

**`MimeKit`** implements MIME properly - nested multiparts, `Content-Transfer-Encoding` (base64, quoted-printable), RFC 2047 encoded words, folded headers, S/MIME and PGP. This package does none of that, deliberately: an HTTP multipart body is 8-bit and unfolded, so a part body is opaque bytes and a header line is `name: value`. If the payload is really MIME - mail, or anything carrying a transfer encoding - use MimeKit and accept a full MIME implementation plus its dependencies where this is roughly 1,300 lines of source.

**`Microsoft.AspNet.WebApi.Client`**'s `HttpContent.ReadAsMultipartAsync` is the long-standing answer, and now mostly a legacy one: the package targets `netstandard2.0` at the newest, it brings `Newtonsoft.Json` and `Newtonsoft.Json.Bson` along for a multipart reader, and its default provider buffers every part into a `MemoryStream` before the caller sees any of it.


## Differences from Microsoft.AspNetCore.WebUtilities

The reader is behaviourally identical - the aspnetcore test suite passes against it unchanged - with four deliberate API differences:

* `MultipartSection.Headers` is a `Dictionary<string, string>` rather than  `Dictionary<string, StringValues>`. A repeated header name is last-wins. This drops   `Microsoft.Extensions.Primitives`.
* Boundary de-quoting is inlined rather than calling `HeaderUtilities.RemoveQuotes`, and header names   are string literals rather than `HeaderNames` constants. This drops `Microsoft.Net.Http.Headers`.
* `MultipartSection.BaseStreamOffset` is not carried.
* `StreamHelperExtensions` is not shipped. Its `DrainAsync` was an extension on `Stream`, so in a source package it would land on every stream in the consuming project - for a method only the reader ever called. It is a private member of `MultipartReader` here.

The first three drop package dependencies. The last is about the shipped sources compiling into the consumer, where a public extension method is not a free addition.

Single-valued headers are a simplification rather than a loss. What a body part carries is the `Content-*` fields, which RFC 2045 allows at most once per entity; the HTTP headers that do legitimately repeat - `Accept`, `Set-Cookie`, `Via` - are list-valued and have no meaning on a part. A repeated name is malformed input, and modelling it does not read any better: aspnetcore's `MultipartSection.ContentType` is `StringValues.ToString()`, which joins with a comma, so a part with two `Content-Type` lines yields `text/plain, application/json` there. Last-wins at least yields a value that parses. Either way the repeats are bounded - a duplicate name does not grow the dictionary, so `HeadersCountLimit` never sees it, but `HeadersLengthLimit` counts the raw header lines.

`FileMultipartSection`, `FormMultipartSection` and the `ContentDispositionHeaderValue` parsing helpers are not included - they exist to serve `FormFeature`, and they are what would pull `Microsoft.Net.Http.Headers` back in.


## Credits

The reader is derived from [dotnet/aspnetcore](https://github.com/dotnet/aspnetcore), MIT licensed, copyright .NET Foundation and Contributors. See [license.txt](license.txt).
