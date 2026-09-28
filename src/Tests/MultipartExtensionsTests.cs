public class MultipartExtensionsTests
{
    [Test]
    public async Task BoundaryIsFoundOnTheContentType()
    {
        var content = Content("multipart/mixed; boundary=abc123");

        await Assert.That(content.TryGetMultipartBoundary(out var boundary)).IsTrue();
        await Assert.That(boundary).IsEqualTo("abc123");
    }

    // The reader de-quotes the boundary itself, so a quoted one is passed on as it arrived.
    [Test]
    public async Task AQuotedBoundaryIsPassedOnQuoted()
    {
        var content = Content("multipart/mixed; boundary=\"abc123\"");

        await Assert.That(content.TryGetMultipartBoundary(out var boundary)).IsTrue();
        await Assert.That(boundary).IsEqualTo("\"abc123\"");
    }

    [Test]
    public async Task TheBoundaryParameterNameIsCaseInsensitive()
    {
        var content = Content("multipart/mixed; BOUNDARY=abc123");

        await Assert.That(content.TryGetMultipartBoundary(out var boundary)).IsTrue();
        await Assert.That(boundary).IsEqualTo("abc123");
    }

    [Test]
    public async Task NoContentTypeIsNoBoundary()
    {
        var content = new ByteArrayContent([]);
        content.Headers.ContentType = null;

        await Assert.That(content.TryGetMultipartBoundary(out var boundary)).IsFalse();
        await Assert.That(boundary).IsNull();
    }

    [Test]
    public async Task AContentTypeWithoutABoundaryIsNoBoundary()
    {
        var content = Content("multipart/mixed");

        await Assert.That(content.TryGetMultipartBoundary(out var boundary)).IsFalse();
        await Assert.That(boundary).IsNull();
    }

    [Test]
    public async Task AMatchingMediaTypeYieldsTheBoundary()
    {
        var content = Content("multipart/mixed; boundary=abc123");

        await Assert.That(content.TryGetMultipartBoundary("MULTIPART/MIXED", out var boundary)).IsTrue();
        await Assert.That(boundary).IsEqualTo("abc123");
    }

    [Test]
    public async Task AMismatchedMediaTypeYieldsNothing()
    {
        var content = Content("multipart/mixed; boundary=abc123");

        await Assert.That(content.TryGetMultipartBoundary("multipart/related", out var boundary)).IsFalse();
        await Assert.That(boundary).IsNull();
    }

    [Test]
    public async Task ReadAsBytesAsyncReadsTheWholeBody()
    {
        var section = Section("application/octet-stream", [1, 2, 3, 0, 255]);

        await Assert.That(await section.ReadAsBytesAsync()).IsEquivalentTo(new byte[] {1, 2, 3, 0, 255}, CollectionOrdering.Matching);
    }

    // Content-Length only sizes the buffer. A body longer than it claims is still read whole.
    [Test]
    public async Task ReadAsBytesAsyncDoesNotTrustContentLength()
    {
        var section = Section("application/octet-stream", [1, 2, 3, 4, 5]);
        section.Headers!["Content-Length"] = "2";

        await Assert.That(await section.ReadAsBytesAsync()).IsEquivalentTo(new byte[] {1, 2, 3, 4, 5}, CollectionOrdering.Matching);
    }

    // Content-Length sizes a buffer and nothing else. A part declaring two gigabytes over a three-byte
    // body must not have that allocated, and must still read back what is actually there.
    [Test]
    public async Task ReadAsBytesAsyncIgnoresAnAbsurdContentLength()
    {
        var section = Section("application/octet-stream", [1, 2, 3]);
        section.Headers!["Content-Length"] = "2147483647";

        await Assert.That(await section.ReadAsBytesAsync()).IsEquivalentTo(new byte[] {1, 2, 3}, CollectionOrdering.Matching);
    }

    // Parsed as an int this would read as null, which is indistinguishable from a part declaring
    // nothing at all.
    [Test]
    public async Task ContentLengthIsA64BitQuantity()
    {
        var section = Section("application/octet-stream", []);
        section.Headers!["Content-Length"] = "3000000000";

        await Assert.That(section.ContentLength).IsEqualTo(3_000_000_000L);
    }

    [Test]
    public async Task ReadAsStringAsyncDefaultsToUtf8()
    {
        var section = Section("text/plain", "héllo"u8.ToArray());

        await Assert.That(await section.ReadAsStringAsync()).IsEqualTo("héllo");
    }

    [Test]
    public async Task ReadAsStringAsyncHonoursTheDeclaredCharset()
    {
        var section = Section("text/plain; charset=iso-8859-1", [0x68, 0xE9, 0x6C, 0x6C, 0x6F]);

        await Assert.That(await section.ReadAsStringAsync()).IsEqualTo("héllo");
    }

    [Test]
    public async Task ReadAsStringAsyncFallsBackToUtf8ForAnUnknownCharset()
    {
        var section = Section("text/plain; charset=not-a-charset", "héllo"u8.ToArray());

        await Assert.That(await section.ReadAsStringAsync()).IsEqualTo("héllo");
    }

    // UTF-7 is obsolete and unsafe to decode, so it is treated as absent rather than honoured.
    [Test]
    public async Task ReadAsStringAsyncRefusesUtf7()
    {
        var section = Section("text/plain; charset=utf-7", "héllo"u8.ToArray());

        await Assert.That(await section.ReadAsStringAsync()).IsEqualTo("héllo");
    }

    [Test]
    public async Task ReadAsStringAsyncWithNoContentTypeIsUtf8()
    {
        var section = new MultipartSection
        {
            Headers = new(StringComparer.OrdinalIgnoreCase),
            Body = new MemoryStream("héllo"u8.ToArray())
        };

        await Assert.That(await section.ReadAsStringAsync()).IsEqualTo("héllo");
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
