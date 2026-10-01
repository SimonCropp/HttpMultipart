public class Usage
{
    [Test]
    public async Task Read()
    {
        var response = MultipartResponse();

        #region read

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

        #endregion

        await Assert.That(parts).IsEquivalentTo(["first", "second"], CollectionOrdering.Matching);
    }

    [Test]
    public async Task ReadBinary()
    {
        var response = MultipartResponse();

        #region readBinary

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

        #endregion

        await Assert.That(handled).Count().IsEqualTo(2);
    }

    [Test]
    public async Task ReadBinaryStrict()
    {
        var response = MultipartResponse();

        response.Content.TryGetMultipartBoundary("multipart/mixed", out var boundary);
        await using var body = await response.Content.ReadAsStreamAsync();
        using var reader = new MultipartReader(boundary!, body);
        while (await reader.ReadNextSectionAsync() is {} section)
        {
            #region readBinaryStrict

            // Throws InvalidDataException when a declared Content-Length up to 64 MiB
            // does not match the body that arrived.
            var bytes = await section.ReadAsBytesStrictAsync(maxDeclaredLength: 64 * 1024 * 1024);

            #endregion

            Handle(section.ContentType, bytes);
        }

        await Assert.That(handled).Count().IsEqualTo(2);
    }

    [Test]
    public async Task Write()
    {
        var stream = new MemoryStream();

        #region write

        var writer = MultipartWriter.Create(stream);
        // The value to send as the Content-Type of the whole body.
        var contentType = writer.ContentType;

        // A part whose content the caller writes to the stream itself.
        await writer.OpenPart("application/json");
        await stream.WriteAsync("""{"ok":true}"""u8.ToArray());

        // A part written whole, with a Content-Length.
        await writer.WritePart("application/octet-stream", new byte[] {1, 2, 3});

        await writer.Terminate();

        #endregion

        // Asserted in pieces rather than byte-for-byte: the binary part's content is three control
        // bytes, and MultipartWriterTests already pins the exact framing.
        var written = Encoding.UTF8.GetString(stream.ToArray());
        await Assert.That(contentType).IsEqualTo($"multipart/mixed; boundary={writer.Boundary}");
        await Assert.That(written).StartsWith($"--{writer.Boundary}\r\nContent-Type: application/json\r\n\r\n{{\"ok\":true}}");
        await Assert.That(written).Contains("Content-Type: application/octet-stream\r\nContent-Length: 3\r\n\r\n");
        await Assert.That(written).EndsWith($"\r\n--{writer.Boundary}--\r\n");
    }

    [Test]
    public async Task WriteLarge()
    {
        var stream = new MemoryStream();
        var content = new byte[64 * 1024];
        Random.Shared.NextBytes(content);
        var source = new MemoryStream(content);

        #region writeLarge

        var writer = MultipartWriter.Create(stream);

        // Declares the length without the part ever being held in memory: it is copied from the source
        // straight into the body.
        await writer.WritePart("application/octet-stream", source, source.Length);

        await writer.Terminate();

        #endregion

        stream.Seek(0, SeekOrigin.Begin);
        var reader = new MultipartReader(writer.Boundary, stream);
        var section = await reader.ReadNextSectionAsync();
        await Assert.That(section!.ContentLength).IsEqualTo(content.Length);
        await Assert.That(await section.ReadAsBytesAsync()).IsEquivalentTo(content, CollectionOrdering.Matching);
    }

    readonly List<string> handled = [];

    void Handle(string? contentType, byte[] bytes) =>
        handled.Add($"{contentType}:{bytes.Length}");

    static HttpResponseMessage MultipartResponse()
    {
        const string boundary = "b1a2c3";
        var body =
            $"""
            --{boundary}
            Content-Type: text/plain

            first
            --{boundary}
            Content-Type: text/plain

            second
            --{boundary}--

            """.Crlf();

        var response = new HttpResponseMessage
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body))
        };
        response.Content.Headers.ContentType =
            MediaTypeHeaderValue.Parse($"multipart/mixed; boundary={boundary}");
        return response;
    }
}
