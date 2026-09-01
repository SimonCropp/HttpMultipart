[TestFixture]
public class MultipartExtensionsTests
{
    [Test]
    public void BoundaryIsFoundOnTheContentType()
    {
        var content = Content("multipart/mixed; boundary=abc123");

        Assert.That(content.TryGetMultipartBoundary(out var boundary), Is.True);
        Assert.That(boundary, Is.EqualTo("abc123"));
    }

    // The reader de-quotes the boundary itself, so a quoted one is passed on as it arrived.
    [Test]
    public void AQuotedBoundaryIsPassedOnQuoted()
    {
        var content = Content("multipart/mixed; boundary=\"abc123\"");

        Assert.That(content.TryGetMultipartBoundary(out var boundary), Is.True);
        Assert.That(boundary, Is.EqualTo("\"abc123\""));
    }

    [Test]
    public void TheBoundaryParameterNameIsCaseInsensitive()
    {
        var content = Content("multipart/mixed; BOUNDARY=abc123");

        Assert.That(content.TryGetMultipartBoundary(out var boundary), Is.True);
        Assert.That(boundary, Is.EqualTo("abc123"));
    }

    [Test]
    public void NoContentTypeIsNoBoundary()
    {
        var content = new ByteArrayContent([]);
        content.Headers.ContentType = null;

        Assert.That(content.TryGetMultipartBoundary(out var boundary), Is.False);
        Assert.That(boundary, Is.Null);
    }

    [Test]
    public void AContentTypeWithoutABoundaryIsNoBoundary()
    {
        var content = Content("multipart/mixed");

        Assert.That(content.TryGetMultipartBoundary(out var boundary), Is.False);
        Assert.That(boundary, Is.Null);
    }

    [Test]
    public void AMatchingMediaTypeYieldsTheBoundary()
    {
        var content = Content("multipart/mixed; boundary=abc123");

        Assert.That(content.TryGetMultipartBoundary("MULTIPART/MIXED", out var boundary), Is.True);
        Assert.That(boundary, Is.EqualTo("abc123"));
    }

    [Test]
    public void AMismatchedMediaTypeYieldsNothing()
    {
        var content = Content("multipart/mixed; boundary=abc123");

        Assert.That(content.TryGetMultipartBoundary("multipart/related", out var boundary), Is.False);
        Assert.That(boundary, Is.Null);
    }

    [Test]
    public async Task ReadAsBytesAsyncReadsTheWholeBody()
    {
        var section = Section("application/octet-stream", [1, 2, 3, 0, 255]);

        Assert.That(await section.ReadAsBytesAsync(), Is.EqualTo(new byte[] {1, 2, 3, 0, 255}));
    }

    // Content-Length only sizes the buffer. A body longer than it claims is still read whole.
    [Test]
    public async Task ReadAsBytesAsyncDoesNotTrustContentLength()
    {
        var section = Section("application/octet-stream", [1, 2, 3, 4, 5]);
        section.Headers!["Content-Length"] = "2";

        Assert.That(await section.ReadAsBytesAsync(), Is.EqualTo(new byte[] {1, 2, 3, 4, 5}));
    }

    // Content-Length sizes a buffer and nothing else. A part declaring two gigabytes over a three-byte
    // body must not have that allocated, and must still read back what is actually there.
    [Test]
    public async Task ReadAsBytesAsyncIgnoresAnAbsurdContentLength()
    {
        var section = Section("application/octet-stream", [1, 2, 3]);
        section.Headers!["Content-Length"] = "2147483647";

        Assert.That(await section.ReadAsBytesAsync(), Is.EqualTo(new byte[] {1, 2, 3}));
    }

    // Parsed as an int this would read as null, which is indistinguishable from a part declaring
    // nothing at all.
    [Test]
    public void ContentLengthIsA64BitQuantity()
    {
        var section = Section("application/octet-stream", []);
        section.Headers!["Content-Length"] = "3000000000";

        Assert.That(section.ContentLength, Is.EqualTo(3_000_000_000L));
    }

    [Test]
    public async Task ReadAsStringAsyncDefaultsToUtf8()
    {
        var section = Section("text/plain", "héllo"u8.ToArray());

        Assert.That(await section.ReadAsStringAsync(), Is.EqualTo("héllo"));
    }

    [Test]
    public async Task ReadAsStringAsyncHonoursTheDeclaredCharset()
    {
        var section = Section("text/plain; charset=iso-8859-1", [0x68, 0xE9, 0x6C, 0x6C, 0x6F]);

        Assert.That(await section.ReadAsStringAsync(), Is.EqualTo("héllo"));
    }

    [Test]
    public async Task ReadAsStringAsyncFallsBackToUtf8ForAnUnknownCharset()
    {
        var section = Section("text/plain; charset=not-a-charset", "héllo"u8.ToArray());

        Assert.That(await section.ReadAsStringAsync(), Is.EqualTo("héllo"));
    }

    // UTF-7 is obsolete and unsafe to decode, so it is treated as absent rather than honoured.
    [Test]
    public async Task ReadAsStringAsyncRefusesUtf7()
    {
        var section = Section("text/plain; charset=utf-7", "héllo"u8.ToArray());

        Assert.That(await section.ReadAsStringAsync(), Is.EqualTo("héllo"));
    }

    [Test]
    public async Task ReadAsStringAsyncWithNoContentTypeIsUtf8()
    {
        var section = new MultipartSection
        {
            Headers = new(StringComparer.OrdinalIgnoreCase),
            Body = new MemoryStream("héllo"u8.ToArray())
        };

        Assert.That(await section.ReadAsStringAsync(), Is.EqualTo("héllo"));
    }

    static ByteArrayContent Content(string contentType)
    {
        var content = new ByteArrayContent([]);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        return content;
    }

    static MultipartSection Section(string contentType, byte[] body) =>
        new()
        {
            Headers = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Content-Type"] = contentType
            },
            Body = new MemoryStream(body)
        };
}
