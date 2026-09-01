// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/aspnetcore, src/Http/WebUtilities/test/MultipartReaderTests.cs. The upstream
// method names are kept so this file can be diffed against upstream when it grows a test.

using System.Text;

[TestFixture]
public class MultipartReaderTests
{
    const string Boundary = "9051914041544843365972754266";
    const string BoundaryWithQuotes = @"""9051914041544843365972754266""";

    // CRLF is required, so these cannot be multi-line C# strings: the line breaks on Linux are just LF.
    const string OnePartBody =
        "--9051914041544843365972754266\r\n" +
        "Content-Disposition: form-data; name=\"text\"\r\n" +
        "\r\n" +
        "text default\r\n" +
        "--9051914041544843365972754266--\r\n";

    const string OnePartBodyTwoHeaders =
        "--9051914041544843365972754266\r\n" +
        "Content-Disposition: form-data; name=\"text\"\r\n" +
        "Custom-header: custom-value\r\n" +
        "\r\n" +
        "text default\r\n" +
        "--9051914041544843365972754266--\r\n";

    const string OnePartBodyWithTrailingWhitespace =
        "--9051914041544843365972754266             \r\n" +
        "Content-Disposition: form-data; name=\"text\"\r\n" +
        "\r\n" +
        "text default\r\n" +
        "--9051914041544843365972754266--\r\n";

    // Non-compliant, but common: the last CRLF is left off.
    const string OnePartBodyWithoutFinalCRLF =
        "--9051914041544843365972754266\r\n" +
        "Content-Disposition: form-data; name=\"text\"\r\n" +
        "\r\n" +
        "text default\r\n" +
        "--9051914041544843365972754266--";

    const string TwoPartBody =
        "--9051914041544843365972754266\r\n" +
        "Content-Disposition: form-data; name=\"text\"\r\n" +
        "\r\n" +
        "text default\r\n" +
        "--9051914041544843365972754266\r\n" +
        "Content-Disposition: form-data; name=\"file1\"; filename=\"a.txt\"\r\n" +
        "Content-Type: text/plain\r\n" +
        "\r\n" +
        "Content of a.txt.\r\n" +
        "\r\n" +
        "--9051914041544843365972754266--\r\n";

    const string TwoPartBodyWithUnicodeFileName =
        "--9051914041544843365972754266\r\n" +
        "Content-Disposition: form-data; name=\"text\"\r\n" +
        "\r\n" +
        "text default\r\n" +
        "--9051914041544843365972754266\r\n" +
        "Content-Disposition: form-data; name=\"file1\"; filename=\"a色.txt\"\r\n" +
        "Content-Type: text/plain\r\n" +
        "\r\n" +
        "Content of a.txt.\r\n" +
        "\r\n" +
        "--9051914041544843365972754266--\r\n";

    const string ThreePartBody =
        "--9051914041544843365972754266\r\n" +
        "Content-Disposition: form-data; name=\"text\"\r\n" +
        "\r\n" +
        "text default\r\n" +
        "--9051914041544843365972754266\r\n" +
        "Content-Disposition: form-data; name=\"file1\"; filename=\"a.txt\"\r\n" +
        "Content-Type: text/plain\r\n" +
        "\r\n" +
        "Content of a.txt.\r\n" +
        "\r\n" +
        "--9051914041544843365972754266\r\n" +
        "Content-Disposition: form-data; name=\"file2\"; filename=\"a.html\"\r\n" +
        "Content-Type: text/html\r\n" +
        "\r\n" +
        "<!DOCTYPE html><title>Content of a.html.</title>\r\n" +
        "\r\n" +
        "--9051914041544843365972754266--\r\n";

    const string TwoPartBodyIncompleteBuffer =
        "--9051914041544843365972754266\r\n" +
        "Content-Disposition: form-data; name=\"text\"\r\n" +
        "\r\n" +
        "text default\r\n" +
        "--9051914041544843365972754266\r\n" +
        "Content-Disposition: form-data; name=\"file1\"; filename=\"a.txt\"\r\n" +
        "Content-Type: text/plain\r\n" +
        "\r\n" +
        "Content of a.txt.\r\n" +
        "\r\n" +
        "--9051914041544843365";

    const string BoundaryWithGarbage =
        "--9051914041544843365972754266\r\n" +
        "Content-Disposition: form-data; name=\"text\"\r\n" +
        "\r\n" +
        "text default\r\n" +
        "--9051914041544843365972754266 garbage\r\n";

    [Test]
    public async Task MultipartReader_ReadSinglePartBody_Success()
    {
        var reader = new MultipartReader(Boundary, MakeStream(OnePartBody));

        var section = await ReadSection(reader);
        Assert.That(section.Headers, Has.Count.EqualTo(1));
        Assert.That(section.ContentDisposition, Is.EqualTo("form-data; name=\"text\""));
        Assert.That(await ReadBody(section), Is.EqualTo("text default"));

        Assert.That(await reader.ReadNextSectionAsync(), Is.Null);
    }

    [Test]
    public void MultipartReader_HeaderCountExceeded_Throws()
    {
        var reader = new MultipartReader(Boundary, MakeStream(OnePartBodyTwoHeaders))
        {
            HeadersCountLimit = 1
        };

        var exception = Assert.ThrowsAsync<InvalidDataException>(() => reader.ReadNextSectionAsync())!;
        Assert.That(exception.Message, Is.EqualTo("Multipart headers count limit 1 exceeded."));
    }

    [Test]
    public void MultipartReader_HeadersLengthExceeded_Throws()
    {
        var reader = new MultipartReader(Boundary, MakeStream(OnePartBodyTwoHeaders))
        {
            HeadersLengthLimit = 60
        };

        var exception = Assert.ThrowsAsync<InvalidDataException>(() => reader.ReadNextSectionAsync())!;
        Assert.That(exception.Message, Is.EqualTo("Line length limit 17 exceeded."));
    }

    // A single header line much larger than the internal read buffer (4 KiB) and the headers length
    // limit (16 KiB), never terminated with a CRLF. The limit has to be enforced while reading the
    // line, rather than by a length check after the whole payload is buffered in memory.
    [Test]
    public void MultipartReader_HeaderLineSpanningMultipleBuffers_EnforcesHeadersLengthLimit()
    {
        var body = "--9051914041544843365972754266\r\n" + new string('a', 100_000);
        var reader = new MultipartReader(Boundary, MakeStream(body));

        var exception = Assert.ThrowsAsync<InvalidDataException>(() => reader.ReadNextSectionAsync())!;
        Assert.That(exception.Message, Is.EqualTo("Line length limit 16384 exceeded."));
    }

    [Test]
    public void MultipartReader_HeadersLengthExceeded_LargePreamble()
    {
        var body = $"preamble {new string('a', 17000)}\r\n" +
                   "--9051914041544843365972754266\r\n" +
                   "\r\n" +
                   "text default\r\n" +
                   "--9051914041544843365972754266--\r\n";
        var reader = new MultipartReader(Boundary, MakeStream(body));

        var exception = Assert.ThrowsAsync<InvalidDataException>(() => reader.ReadNextSectionAsync())!;
        Assert.That(
            exception.Message,
            Is.EqualTo("Multipart header length limit 16384 exceeded. Too much data before the first boundary."));
    }

    [Test]
    public async Task MultipartReader_HeadersLengthLimitSettable_LargePreamblePasses()
    {
        var body = $"preamble {new string('a', 100_000)}\r\n" +
                   "--9051914041544843365972754266\r\n" +
                   "\r\n" +
                   "text default\r\n" +
                   "--9051914041544843365972754266--\r\n";
        var reader = new MultipartReader(Boundary, MakeStream(body))
        {
            HeadersLengthLimit = 200_000
        };

        var section = await ReadSection(reader);
        Assert.That(await ReadBody(section), Is.EqualTo("text default"));
    }

    [Test]
    public async Task MultipartReader_ReadSinglePartBodyWithTrailingWhitespace_Success()
    {
        var reader = new MultipartReader(Boundary, MakeStream(OnePartBodyWithTrailingWhitespace));

        var section = await ReadSection(reader);
        Assert.That(section.Headers, Has.Count.EqualTo(1));
        Assert.That(section.ContentDisposition, Is.EqualTo("form-data; name=\"text\""));
        Assert.That(await ReadBody(section), Is.EqualTo("text default"));

        Assert.That(await reader.ReadNextSectionAsync(), Is.Null);
    }

    [Test]
    public async Task MultipartReader_ReadSinglePartBodyWithoutLastCRLF_Success()
    {
        var reader = new MultipartReader(Boundary, MakeStream(OnePartBodyWithoutFinalCRLF));

        var section = await ReadSection(reader);
        Assert.That(section.Headers, Has.Count.EqualTo(1));
        Assert.That(section.ContentDisposition, Is.EqualTo("form-data; name=\"text\""));
        Assert.That(await ReadBody(section), Is.EqualTo("text default"));

        Assert.That(await reader.ReadNextSectionAsync(), Is.Null);
    }

    [Test]
    public async Task MultipartReader_ReadTwoPartBody_Success()
    {
        var reader = new MultipartReader(Boundary, MakeStream(TwoPartBody));

        var section = await ReadSection(reader);
        Assert.That(section.Headers, Has.Count.EqualTo(1));
        Assert.That(section.ContentDisposition, Is.EqualTo("form-data; name=\"text\""));
        Assert.That(await ReadBody(section), Is.EqualTo("text default"));

        section = await ReadSection(reader);
        Assert.That(section.Headers, Has.Count.EqualTo(2));
        Assert.That(section.ContentDisposition, Is.EqualTo("form-data; name=\"file1\"; filename=\"a.txt\""));
        Assert.That(section.ContentType, Is.EqualTo("text/plain"));
        Assert.That(await ReadBody(section), Is.EqualTo("Content of a.txt.\r\n"));

        Assert.That(await reader.ReadNextSectionAsync(), Is.Null);
    }

    [Test]
    public async Task MultipartReader_ReadTwoPartBodyWithUnicodeFileName_Success()
    {
        var reader = new MultipartReader(Boundary, MakeStream(TwoPartBodyWithUnicodeFileName));

        var section = await ReadSection(reader);
        Assert.That(section.Headers, Has.Count.EqualTo(1));
        Assert.That(section.ContentDisposition, Is.EqualTo("form-data; name=\"text\""));
        Assert.That(await ReadBody(section), Is.EqualTo("text default"));

        section = await ReadSection(reader);
        Assert.That(section.Headers, Has.Count.EqualTo(2));
        Assert.That(
            section.ContentDisposition,
            Is.EqualTo("form-data; name=\"file1\"; filename=\"a色.txt\""));
        Assert.That(section.ContentType, Is.EqualTo("text/plain"));
        Assert.That(await ReadBody(section), Is.EqualTo("Content of a.txt.\r\n"));

        Assert.That(await reader.ReadNextSectionAsync(), Is.Null);
    }

    [Test]
    public async Task MultipartReader_ThreePartBody_Success()
    {
        var reader = new MultipartReader(Boundary, MakeStream(ThreePartBody));

        var section = await ReadSection(reader);
        Assert.That(section.Headers, Has.Count.EqualTo(1));
        Assert.That(section.ContentDisposition, Is.EqualTo("form-data; name=\"text\""));
        Assert.That(await ReadBody(section), Is.EqualTo("text default"));

        section = await ReadSection(reader);
        Assert.That(section.Headers, Has.Count.EqualTo(2));
        Assert.That(section.ContentDisposition, Is.EqualTo("form-data; name=\"file1\"; filename=\"a.txt\""));
        Assert.That(section.ContentType, Is.EqualTo("text/plain"));
        Assert.That(await ReadBody(section), Is.EqualTo("Content of a.txt.\r\n"));

        section = await ReadSection(reader);
        Assert.That(section.Headers, Has.Count.EqualTo(2));
        Assert.That(section.ContentDisposition, Is.EqualTo("form-data; name=\"file2\"; filename=\"a.html\""));
        Assert.That(section.ContentType, Is.EqualTo("text/html"));
        Assert.That(
            await ReadBody(section),
            Is.EqualTo("<!DOCTYPE html><title>Content of a.html.</title>\r\n"));

        Assert.That(await reader.ReadNextSectionAsync(), Is.Null);
    }

    [Test]
    public void MultipartReader_BufferSizeMustBeLargerThanBoundary_Throws()
    {
        var stream = MakeStream(ThreePartBody);

        Assert.Throws<ArgumentOutOfRangeException>(() => _ = new MultipartReader(Boundary, stream, 5));
    }

    [Test]
    public async Task MultipartReader_ReadMultipartBodyWithFilesForDeferredCopy_Success()
    {
        var reader = new MultipartReader(Boundary, MakeStream(ThreePartBody));

        // Skip the text field section.
        await reader.ReadNextSectionAsync();

        var section = await ReadSection(reader);
        Assert.That(section.Headers, Has.Count.EqualTo(2));
        Assert.That(section.ContentDisposition, Is.EqualTo("form-data; name=\"file1\"; filename=\"a.txt\""));
        Assert.That(section.ContentType, Is.EqualTo("text/plain"));
        var stream1 = section.Body;

        section = await ReadSection(reader);
        Assert.That(section.Headers, Has.Count.EqualTo(2));
        Assert.That(section.ContentDisposition, Is.EqualTo("form-data; name=\"file2\"; filename=\"a.html\""));
        Assert.That(section.ContentType, Is.EqualTo("text/html"));
        var stream2 = section.Body;

        Assert.That(await reader.ReadNextSectionAsync(), Is.Null);

        Assert.That(stream1.CanSeek, Is.True);
        Assert.That(stream1.Seek(0, SeekOrigin.Begin), Is.EqualTo(0));
        Assert.That(await ReadStream(stream1), Is.EqualTo("Content of a.txt.\r\n"));

        Assert.That(stream2.CanSeek, Is.True);
        Assert.That(stream2.Seek(0, SeekOrigin.Begin), Is.EqualTo(0));
        Assert.That(
            await ReadStream(stream2),
            Is.EqualTo("<!DOCTYPE html><title>Content of a.html.</title>\r\n"));
    }

    [Test]
    public async Task MultipartReader_TwoPartBodyIncompleteBuffer_TwoSectionsReadSuccessfullyThirdSectionThrows()
    {
        var reader = new MultipartReader(Boundary, MakeStream(TwoPartBodyIncompleteBuffer));
        var buffer = new byte[128];

        var section = await ReadSection(reader);
        Assert.That(section.Headers, Has.Count.EqualTo(1));
        Assert.That(section.ContentDisposition, Is.EqualTo("form-data; name=\"text\""));
        var read = section.Body.Read(buffer, 0, buffer.Length);
        Assert.That(GetString(buffer, read), Is.EqualTo("text default"));

        // The second section reads even though its closing boundary is truncated.
        section = await ReadSection(reader);
        Assert.That(section.Headers, Has.Count.EqualTo(2));
        Assert.That(section.ContentDisposition, Is.EqualTo("form-data; name=\"file1\"; filename=\"a.txt\""));
        Assert.That(section.ContentType, Is.EqualTo("text/plain"));
        read = section.Body.Read(buffer, 0, buffer.Length);
        Assert.That(GetString(buffer, read), Is.EqualTo("Content of a.txt.\r\n"));

        // There are not enough bytes left to even contain a final boundary.
        Assert.ThrowsAsync<IOException>(() => reader.ReadNextSectionAsync());
    }

    [Test]
    public async Task MultipartReader_ReadInvalidUtf8Header_ReplacementCharacters()
    {
        var reader = new MultipartReader(Boundary, MakeSplitHeaderStream([0xC1, 0x21]));

        var section = await ReadSection(reader);
        Assert.That(section.Headers, Has.Count.EqualTo(1));
        Assert.That(
            section.ContentDisposition,
            Is.EqualTo("form-data; name=\"text\" filename=\"a�!.txt\""));
        Assert.That(await ReadBody(section), Is.EqualTo("text default"));

        Assert.That(await reader.ReadNextSectionAsync(), Is.Null);
    }

    [Test]
    public async Task MultipartReader_ReadInvalidUtf8SurrogateHeader_ReplacementCharacters()
    {
        var reader = new MultipartReader(Boundary, MakeSplitHeaderStream([0xED, 0xA0, 85]));

        var section = await ReadSection(reader);
        Assert.That(section.Headers, Has.Count.EqualTo(1));
        Assert.That(
            section.ContentDisposition,
            Is.EqualTo("form-data; name=\"text\" filename=\"a��U.txt\""));
        Assert.That(await ReadBody(section), Is.EqualTo("text default"));

        Assert.That(await reader.ReadNextSectionAsync(), Is.Null);
    }

    // The reader strips quotes from the boundary rather than throwing.
    [Test]
    public async Task MultipartReader_StripQuotesFromBoundary()
    {
        var reader = new MultipartReader(BoundaryWithQuotes, MakeStream(OnePartBody));

        Assert.That(await reader.ReadNextSectionAsync(), Is.Not.Null);
    }

    [Test]
    public async Task SyncReadWithOffsetWorks()
    {
        var reader = new MultipartReader(Boundary, MakeStream(OnePartBody));
        var buffer = new byte[5];

        var section = await ReadSection(reader);
        Assert.That(section.Headers, Has.Count.EqualTo(1));
        Assert.That(section.ContentDisposition, Is.EqualTo("form-data; name=\"text\""));

        var read = section.Body.Read(buffer, 2, buffer.Length - 2);
        Assert.That(GetString(buffer, read + 2), Is.EqualTo("\0\0tex"));

        read = section.Body.Read(buffer, 1, buffer.Length - 1);
        Assert.That(GetString(buffer, read + 1), Is.EqualTo("\0t de"));

        read = section.Body.Read(buffer, 0, buffer.Length);
        Assert.That(GetString(buffer, read), Is.EqualTo("fault"));

        Assert.That(await reader.ReadNextSectionAsync(), Is.Null);
    }

    // A boundary line with trailing data that is not the "--" final marker.
    [Test]
    public async Task MultipartReader_BoundaryWithUnexpectedTrailingData_ThrowsIOException()
    {
        var reader = new MultipartReader(Boundary, MakeStream(BoundaryWithGarbage));

        var section = await ReadSection(reader);

        Assert.ThrowsAsync<IOException>(() => section.Body.CopyToAsync(new MemoryStream()));
    }

    // The same, through the synchronous read path.
    [Test]
    public async Task MultipartReader_BoundaryWithUnexpectedTrailingData_SyncRead_ThrowsIOException()
    {
        var reader = new MultipartReader(Boundary, MakeStream(BoundaryWithGarbage));

        var section = await ReadSection(reader);

        var buffer = new byte[1024];
        Assert.Throws<IOException>(() =>
        {
            while (section.Body.Read(buffer, 0, buffer.Length) > 0)
            {
            }
        });
    }

    // Trailing whitespace on the final boundary line is allowed, and trimmed.
    [Test]
    public async Task MultipartReader_FinalBoundaryWithTrailingWhitespace_Success()
    {
        var body =
            "--9051914041544843365972754266\r\n" +
            "Content-Disposition: form-data; name=\"text\"\r\n" +
            "\r\n" +
            "text default\r\n" +
            "--9051914041544843365972754266--   \r\n";
        var reader = new MultipartReader(Boundary, MakeStream(body));

        var section = await ReadSection(reader);
        Assert.That(await ReadBody(section), Is.EqualTo("text default"));

        Assert.That(await reader.ReadNextSectionAsync(), Is.Null);
    }

    // A non-final boundary with non-whitespace trailing data.
    [Test]
    public async Task MultipartReader_IntermediateBoundaryWithTrailingData_ThrowsIOException()
    {
        var body =
            "--9051914041544843365972754266\r\n" +
            "Content-Disposition: form-data; name=\"text\"\r\n" +
            "\r\n" +
            "text default\r\n" +
            "--9051914041544843365972754266 notwhitespace\r\n" +
            "Content-Disposition: form-data; name=\"text2\"\r\n" +
            "\r\n" +
            "text2\r\n" +
            "--9051914041544843365972754266--\r\n";
        var reader = new MultipartReader(Boundary, MakeStream(body));

        var section = await ReadSection(reader);

        Assert.ThrowsAsync<IOException>(() => section.Body.CopyToAsync(new MemoryStream()));
    }

    static MemoryStream MakeStream(string text) =>
        new(Encoding.UTF8.GetBytes(text));

    // A one-part body with the given raw bytes spliced into the middle of a header value.
    static MemoryStream MakeSplitHeaderStream(byte[] invalid)
    {
        const string before =
            "--9051914041544843365972754266\r\n" +
            "Content-Disposition: form-data; name=\"text\" filename=\"a";
        const string after =
            ".txt\"\r\n" +
            "\r\n" +
            "text default\r\n" +
            "--9051914041544843365972754266--\r\n";

        var stream = new MemoryStream();
        stream.Write(Encoding.UTF8.GetBytes(before));
        stream.Write(invalid);
        stream.Write(Encoding.UTF8.GetBytes(after));
        stream.Seek(0, SeekOrigin.Begin);
        return stream;
    }

    static string GetString(byte[] buffer, int count) =>
        Encoding.ASCII.GetString(buffer, 0, count);

    static async Task<MultipartSection> ReadSection(MultipartReader reader)
    {
        var section = await reader.ReadNextSectionAsync();
        Assert.That(section, Is.Not.Null);
        return section!;
    }

    static Task<string> ReadBody(MultipartSection section) =>
        ReadStream(section.Body);

    static async Task<string> ReadStream(Stream stream)
    {
        var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        return Encoding.ASCII.GetString(buffer.ToArray());
    }
}
