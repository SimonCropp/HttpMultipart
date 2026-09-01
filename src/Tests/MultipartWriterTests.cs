using System.Text;

/// <summary>
/// The framing the writer emits. It encodes the boundary once and caches the opening bytes of a
/// repeated part type — a streaming caller opens one part per row — so what this pins is that the
/// cached opening is the same framing an uncached one produces, and that the leading CRLF still
/// belongs to the delimiter rather than to the part before it.
/// </summary>
[TestFixture]
public class MultipartWriterTests
{
    [Test]
    public async Task FramesRepeatedPartsIdentically()
    {
        using var body = new MemoryStream();
        var writer = new MultipartWriter(body, "b");

        await writer.OpenPart("application/x-ndjson");
        await body.WriteAsync("row1"u8.ToArray());
        await writer.OpenPart("application/x-ndjson");
        await body.WriteAsync("row2"u8.ToArray());
        // The third goes through the cached opening; the second is what filled it.
        await writer.OpenPart("application/x-ndjson");
        await body.WriteAsync("row3"u8.ToArray());
        await writer.Terminate();

        Assert.That(
            Text(body),
            Is.EqualTo(
                "--b\r\nContent-Type: application/x-ndjson\r\n\r\nrow1" +
                "\r\n--b\r\nContent-Type: application/x-ndjson\r\n\r\nrow2" +
                "\r\n--b\r\nContent-Type: application/x-ndjson\r\n\r\nrow3" +
                "\r\n--b--\r\n"));
    }

    [Test]
    public async Task WritePartDeclaresItsLength()
    {
        using var body = new MemoryStream();
        var writer = new MultipartWriter(body, "b");

        await writer.WritePart("application/octet-stream", new byte[] {1, 2, 3});
        await writer.OpenPart("application/json");
        await body.WriteAsync("""{"ok":true}"""u8.ToArray());
        await writer.Terminate();

        Assert.That(
            Text(body),
            Is.EqualTo(
                "--b\r\nContent-Type: application/octet-stream\r\nContent-Length: 3\r\n\r\n\u0001\u0002\u0003" +
                "\r\n--b\r\nContent-Type: application/json\r\n\r\n{\"ok\":true}" +
                "\r\n--b--\r\n"));
    }

    // Alternating types is the batch shape, and it must not serve one type's cached opening for the
    // other.
    [Test]
    public async Task DoesNotReuseAnOpeningAcrossContentTypes()
    {
        using var body = new MemoryStream();
        var writer = new MultipartWriter(body, "b");

        await writer.OpenPart("application/json");
        await body.WriteAsync("one"u8.ToArray());
        await writer.OpenPart("text/plain");
        await body.WriteAsync("two"u8.ToArray());
        await writer.OpenPart("application/json");
        await body.WriteAsync("three"u8.ToArray());
        await writer.Terminate();

        Assert.That(
            Text(body),
            Is.EqualTo(
                "--b\r\nContent-Type: application/json\r\n\r\none" +
                "\r\n--b\r\nContent-Type: text/plain\r\n\r\ntwo" +
                "\r\n--b\r\nContent-Type: application/json\r\n\r\nthree" +
                "\r\n--b--\r\n"));
    }

    [Test]
    public void ContentTypeCarriesTheBoundary()
    {
        var writer = new MultipartWriter(new MemoryStream(), "b", "multipart/related");

        Assert.That(writer.Boundary, Is.EqualTo("b"));
        Assert.That(writer.ContentType, Is.EqualTo("multipart/related; boundary=b"));
    }

    [Test]
    public void CreateDefaultsToMultipartMixed()
    {
        var writer = MultipartWriter.Create(new MemoryStream());

        Assert.That(writer.ContentType, Is.EqualTo($"multipart/mixed; boundary={writer.Boundary}"));
    }

    [Test]
    public void CreateMakesAFreshBoundaryBehindTheGivenPrefix()
    {
        var first = MultipartWriter.Create(new MemoryStream(), boundaryPrefix: "part-");
        var second = MultipartWriter.Create(new MemoryStream(), boundaryPrefix: "part-");

        Assert.That(first.Boundary, Does.StartWith("part-"));
        Assert.That(second.Boundary, Does.StartWith("part-"));
        Assert.That(first.Boundary, Is.Not.EqualTo(second.Boundary));
    }

    [Test]
    public async Task AnEmptyBodyIsJustTheTerminator()
    {
        using var body = new MemoryStream();
        var writer = new MultipartWriter(body, "b");

        await writer.Terminate();

        Assert.That(Text(body), Is.EqualTo("--b--\r\n"));
    }

    static string Text(MemoryStream body) =>
        Encoding.UTF8.GetString(body.ToArray());
}
