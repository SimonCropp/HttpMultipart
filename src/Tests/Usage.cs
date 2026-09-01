/// <summary>
/// The examples the readme embeds. They are tests so the documentation cannot drift from the API.
/// </summary>
[TestFixture]
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
            var reader = new MultipartReader(boundary, body);
            while (await reader.ReadNextSectionAsync() is {} section)
            {
                parts.Add(await section.ReadAsStringAsync());
            }
        }

        #endregion

        Assert.That(parts, Is.EqualTo(["first", "second"]));
    }

    [Test]
    public async Task ReadBinary()
    {
        var response = MultipartResponse();

        #region readBinary

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

        #endregion

        Assert.That(handled, Has.Count.EqualTo(2));
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
        Assert.That(contentType, Is.EqualTo($"multipart/mixed; boundary={writer.Boundary}"));
        Assert.That(
            written,
            Does.StartWith($"--{writer.Boundary}\r\nContent-Type: application/json\r\n\r\n{{\"ok\":true}}"));
        Assert.That(
            written,
            Does.Contain("Content-Type: application/octet-stream\r\nContent-Length: 3\r\n\r\n"));
        Assert.That(written, Does.EndWith($"\r\n--{writer.Boundary}--\r\n"));
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
