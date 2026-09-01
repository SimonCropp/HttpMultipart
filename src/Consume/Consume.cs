// Deliberately no `using HttpMultipart;`. This project has implicit usings on, so the global using the
// package ships in build/HttpMultipart.props is what has to make these names resolve.

static class Consume
{
    static async Task Read(HttpResponseMessage response, CancellationToken cancel)
    {
        if (!response.Content.TryGetMultipartBoundary(out var boundary))
        {
            return;
        }

        await using var body = await response.Content.ReadAsStreamAsync(cancel);
        var reader = new MultipartReader(boundary, body)
        {
            HeadersCountLimit = 32,
            HeadersLengthLimit = 8 * 1024,
            BodyLengthLimit = 1024 * 1024
        };
        while (await reader.ReadNextSectionAsync(cancel) is {} section)
        {
            _ = section.ContentType;
            _ = section.ContentDisposition;
            _ = section.ContentLength;
            _ = section.Headers;
            _ = await section.ReadAsBytesAsync(cancel);
            _ = await section.ReadAsStringAsync(cancel);
        }
    }

    static async Task ReadOfMediaType(HttpContent content, CancellationToken cancel)
    {
        if (content.TryGetMultipartBoundary("multipart/mixed", out var boundary))
        {
            var reader = new MultipartReader(boundary, await content.ReadAsStreamAsync(cancel), 8192);
            _ = await reader.ReadNextSectionAsync(cancel);
        }
    }

    static async Task Write(Stream stream, CancellationToken cancel)
    {
        var writer = MultipartWriter.Create(stream, "multipart/related", "consume-");
        _ = writer.Boundary;
        _ = writer.ContentType;

        await writer.OpenPart("application/json", cancel);
        await stream.WriteAsync(new byte[] {1}, cancel);
        await writer.WritePart("application/octet-stream", new byte[] {2}, cancel);

        // The streaming half: a length declared without the part being held in memory.
        await writer.OpenPart("application/octet-stream", 1, cancel);
        await stream.WriteAsync(new byte[] {3}, cancel);
        await writer.WritePart("application/octet-stream", Stream.Null, 0, cancel);

        await writer.Terminate(cancel);

        var explicitBoundary = new MultipartWriter(stream, "abc");
        await explicitBoundary.Terminate(cancel);
    }

    static async Task Buffered(Stream inner, CancellationToken cancel)
    {
        var buffered = new BufferedReadStream(inner, 4096);
        _ = buffered.BufferedData;
        _ = buffered.EnsureBuffered();
        _ = await buffered.EnsureBufferedAsync(cancel);
        _ = buffered.ReadLine(100);
        _ = await buffered.ReadLineAsync(100, cancel);
        await inner.DrainAsync(cancel);
        await inner.DrainAsync(1024, cancel);
    }

    static Task Main() =>
        Task.WhenAll(
            Read(new(), default),
            ReadOfMediaType(new ByteArrayContent([]), default),
            Write(Stream.Null, default),
            Buffered(Stream.Null, default));
}
