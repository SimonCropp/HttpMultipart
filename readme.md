# HttpMultipart

A source-only NuGet package for reading and writing HTTP multipart content.

The reader is vendored from [dotnet/aspnetcore](https://github.com/dotnet/aspnetcore)'s
`Microsoft.AspNetCore.WebUtilities` and stripped of its dependencies, so it can be used anywhere a
`Stream` can — a Blazor WebAssembly client, a console app, a library that does not want
`Microsoft.Extensions.Primitives` and `Microsoft.Net.Http.Headers` in its dependency graph.

Because the package ships C# source rather than an assembly, it compiles into the consuming project.
There is no runtime dependency, nothing to deploy, and the types stay `internal` to whoever compiled
them.


## Installation

Requires `net10.0` or later.

```xml
<PackageReference Include="HttpMultipart" Version="*" PrivateAssets="all" />
```

`PrivateAssets="all"` keeps the package out of the dependency graph of anything that references your
project. The source is already compiled into your assembly, so there is nothing to flow onwards.


## Namespace

The types live in the `HttpMultipart` namespace, and the package adds a global `using` for it wherever
the consuming project has implicit usings enabled. If it does not, add the using yourself:

```csharp
using HttpMultipart;
```

The namespace matters: without it a global-namespace `MultipartReader` would silently win name lookup
over `Microsoft.AspNetCore.WebUtilities.MultipartReader` in an ASP.NET Core app.


## Reading

<!-- snippet: read -->
<a id='snippet-read'></a>
```cs
var parts = new List<string>();
if (response.Content.TryGetMultipartBoundary(out var boundary))
{
    await using var body = await response.Content.ReadAsStreamAsync();
    var reader = new MultipartReader(boundary, body);
    while (await reader.ReadNextSectionAsync() is {} section)
    {
        parts.Add(await section.ReadAsStringAsync());
    }
}
```
<sup><a href='/src/Tests/Usage.cs#L12-L25' title='Snippet source file'>snippet source</a> | <a href='#snippet-read' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

`ReadNextSectionAsync` is forward-only, and a section's `Body` is valid only until the next section is
read. Read or copy what you need before moving on.

For binary parts, `ReadAsBytesAsync` uses the `Content-Length` header to size its buffer without ever
trusting it for the read itself:

<!-- snippet: readBinary -->
<a id='snippet-readBinary'></a>
```cs
response.Content.TryGetMultipartBoundary("multipart/mixed", out var boundary);
await using var body = await response.Content.ReadAsStreamAsync();
var reader = new MultipartReader(boundary!, body)
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
<sup><a href='/src/Tests/Usage.cs#L35-L50' title='Snippet source file'>snippet source</a> | <a href='#snippet-readBinary' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->


### Limits

| Property | Default | What it bounds |
| --- | --- | --- |
| `HeadersCountLimit` | 16 | Headers per section |
| `HeadersLengthLimit` | 16 KiB | Combined header bytes per section, and the preamble |
| `BodyLengthLimit` | none | Bytes in any one section body |

Exceeding a limit throws `InvalidDataException`. The transport is responsible for bounding the overall
body length; these bound what one section can cost you.


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
<sup><a href='/src/Tests/Usage.cs#L60-L75' title='Snippet source file'>snippet source</a> | <a href='#snippet-write' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The delimiter's leading CRLF is written by the *next* part, or by the terminator — which is what keeps
every part's content byte-exact, since a reader strips that CRLF as part of the delimiter.

`OpenPart` caches the framing bytes for a repeated content type, so a caller opening a part per row
pays a single array write per part rather than re-encoding the delimiter each time.


## API

| Type | Purpose |
| --- | --- |
| `MultipartReader` | Reads sections from a `Stream` |
| `MultipartSection` | One section: `Headers`, `Body`, `ContentType`, `ContentDisposition`, `ContentLength` |
| `MultipartWriter` | Writes multipart framing to a `Stream` |
| `MultipartSectionExtensions` | `ReadAsBytesAsync`, `ReadAsStringAsync` |
| `MultipartContentExtensions` | `TryGetMultipartBoundary` on an `HttpContent` |
| `BufferedReadStream` | Line-reading buffered stream the reader is built on |
| `StreamHelperExtensions` | `DrainAsync` |


## Differences from Microsoft.AspNetCore.WebUtilities

The reader is behaviourally identical — the aspnetcore test suite passes against it unchanged — with
three deliberate API differences, all to drop package dependencies:

* `MultipartSection.Headers` is a `Dictionary<string, string>` rather than
  `Dictionary<string, StringValues>`. A repeated header name is last-wins. This drops
  `Microsoft.Extensions.Primitives`.
* Boundary de-quoting is inlined rather than calling `HeaderUtilities.RemoveQuotes`, and header names
  are string literals rather than `HeaderNames` constants. This drops `Microsoft.Net.Http.Headers`.
* `MultipartSection.BaseStreamOffset` is not carried.

`FileMultipartSection`, `FormMultipartSection` and the `ContentDispositionHeaderValue` parsing helpers
are not included — they exist to serve `FormFeature`, and they are what would pull
`Microsoft.Net.Http.Headers` back in.


## Credits

The reader is derived from [dotnet/aspnetcore](https://github.com/dotnet/aspnetcore), MIT licensed,
copyright .NET Foundation and Contributors. See [license.txt](license.txt).
