using System.Text;

/// <summary>
/// The writer's output fed straight back through the reader. Neither implementation this package draws
/// on has this test, and it is the one that pins the two halves against each other: the writer's rule
/// that a delimiter's leading CRLF belongs to the delimiter, not to the part before it, is only
/// observable as a reader handing back content that is byte-exact.
/// </summary>
[TestFixture]
public class MultipartRoundTripTests
{
    [Test]
    public async Task ContentSurvivesByteExact()
    {
        // Content chosen to sit on the framing's edges: a trailing CRLF that a naive writer would eat,
        // a bare CR, a bare LF, and a line that looks like a delimiter for a different boundary.
        string[] parts =
        [
            "plain",
            "ends with a newline\r\n",
            "has\ra bare CR",
            "has\na bare LF",
            "\r\n--not-the-boundary\r\n",
            ""
        ];

        var body = new MemoryStream();
        var writer = MultipartWriter.Create(body);
        foreach (var part in parts)
        {
            await writer.OpenPart("text/plain");
            await body.WriteAsync(Encoding.UTF8.GetBytes(part));
        }

        await writer.Terminate();

        Assert.That(await ReadBack(body, writer.Boundary), Is.EqualTo(parts));
    }

    [Test]
    public async Task BinaryPartsSurviveWithTheirDeclaredLength()
    {
        var first = new byte[] {0, 1, 2, 13, 10, 45, 45, 255};
        var second = new byte[512];
        for (var i = 0; i < second.Length; i++)
        {
            second[i] = (byte) (i % 256);
        }

        var body = new MemoryStream();
        var writer = MultipartWriter.Create(body, "multipart/related");
        await writer.WritePart("application/octet-stream", first);
        await writer.WritePart("application/octet-stream", second);
        await writer.Terminate();

        body.Seek(0, SeekOrigin.Begin);
        var reader = new MultipartReader(writer.Boundary, body);

        var section = await ReadSection(reader);
        Assert.That(section.ContentLength, Is.EqualTo(first.Length));
        Assert.That(await section.ReadAsBytesAsync(), Is.EqualTo(first));

        section = await ReadSection(reader);
        Assert.That(section.ContentLength, Is.EqualTo(second.Length));
        Assert.That(await section.ReadAsBytesAsync(), Is.EqualTo(second));

        Assert.That(await reader.ReadNextSectionAsync(), Is.Null);
    }

    // The shape a mixed response travels in: raw binary parts in wire order, then the JSON that
    // references them.
    [Test]
    public async Task MixedContentTypesAreReportedPerPart()
    {
        var body = new MemoryStream();
        var writer = MultipartWriter.Create(body);
        await writer.WritePart("application/octet-stream", new byte[] {7, 7, 7});
        await writer.OpenPart("application/json");
        await body.WriteAsync("""{"ok":true}"""u8.ToArray());
        await writer.Terminate();

        body.Seek(0, SeekOrigin.Begin);
        var reader = new MultipartReader(writer.Boundary, body);

        var section = await ReadSection(reader);
        Assert.That(section.ContentType, Is.EqualTo("application/octet-stream"));
        Assert.That(await section.ReadAsBytesAsync(), Is.EqualTo(new byte[] {7, 7, 7}));

        section = await ReadSection(reader);
        Assert.That(section.ContentType, Is.EqualTo("application/json"));
        Assert.That(await section.ReadAsStringAsync(), Is.EqualTo("""{"ok":true}"""));

        Assert.That(await reader.ReadNextSectionAsync(), Is.Null);
    }

    [Test]
    public async Task AnEmptyBodyReadsBackAsNoSections()
    {
        var body = new MemoryStream();
        var writer = MultipartWriter.Create(body);
        await writer.Terminate();

        Assert.That(await ReadBack(body, writer.Boundary), Is.Empty);
    }

    // A buffer barely larger than the boundary forces the reader to carry partial delimiter matches
    // across refills, which is where a writer/reader disagreement would show up.
    [Test]
    public async Task ContentSurvivesAMinimalReadBuffer()
    {
        var content = string.Join("", Enumerable.Repeat("line of content\r\n", 60));

        var body = new MemoryStream();
        var writer = new MultipartWriter(body, "0123456789abcdef");
        await writer.OpenPart("text/plain");
        await body.WriteAsync(Encoding.UTF8.GetBytes(content));
        await writer.Terminate();

        body.Seek(0, SeekOrigin.Begin);
        var reader = new MultipartReader(writer.Boundary, body, bufferSize: writer.Boundary.Length + 8);

        var section = await ReadSection(reader);
        Assert.That(await section.ReadAsStringAsync(), Is.EqualTo(content));

        Assert.That(await reader.ReadNextSectionAsync(), Is.Null);
    }

    static async Task<List<string>> ReadBack(MemoryStream body, string boundary)
    {
        body.Seek(0, SeekOrigin.Begin);
        var reader = new MultipartReader(boundary, body);
        var read = new List<string>();
        while (await reader.ReadNextSectionAsync() is {} section)
        {
            read.Add(await section.ReadAsStringAsync());
        }

        return read;
    }

    static async Task<MultipartSection> ReadSection(MultipartReader reader)
    {
        var section = await reader.ReadNextSectionAsync();
        Assert.That(section, Is.Not.Null);
        return section!;
    }
}
