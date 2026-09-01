using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;

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

    [Test]
    public async Task ReadAsStringAsyncDefaultsToUtf8()
    {
        var section = Section("text/plain", Encoding.UTF8.GetBytes("héllo"));

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
        var section = Section("text/plain; charset=not-a-charset", Encoding.UTF8.GetBytes("héllo"));

        Assert.That(await section.ReadAsStringAsync(), Is.EqualTo("héllo"));
    }

    // UTF-7 is obsolete and unsafe to decode, so it is treated as absent rather than honoured.
    [Test]
    public async Task ReadAsStringAsyncRefusesUtf7()
    {
        var section = Section("text/plain; charset=utf-7", Encoding.UTF8.GetBytes("héllo"));

        Assert.That(await section.ReadAsStringAsync(), Is.EqualTo("héllo"));
    }

    [Test]
    public async Task ReadAsStringAsyncWithNoContentTypeIsUtf8()
    {
        var section = new MultipartSection
        {
            Headers = new(StringComparer.OrdinalIgnoreCase),
            Body = new MemoryStream(Encoding.UTF8.GetBytes("héllo"))
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
