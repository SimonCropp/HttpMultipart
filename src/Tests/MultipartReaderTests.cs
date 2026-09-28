// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/aspnetcore, src/Http/WebUtilities/test/MultipartReaderTests.cs. The upstream
// method names are kept so this file can be diffed against upstream when it grows a test.

public class MultipartReaderTests
{
    const string boundary = "9051914041544843365972754266";

    const string boundaryWithQuotes =
        """
        "9051914041544843365972754266"
        """;

    // Transport padding, which is allowed after a delimiter and has to be trimmed. Named rather than
    // written as trailing whitespace inside the raw strings below, where trim_trailing_whitespace
    // would silently eat it.
    const string openPadding = "             ";
    const string closePadding = "   ";

    // Written with plain newlines and converted once by Crlf, since the delimiter is defined in terms
    // of CRLF. A body that ends at a delimiter has a blank line before the closing quotes; one that is
    // deliberately truncated, or left without its final CRLF, does not.
    static string onePartBody =
        """
            --9051914041544843365972754266
            Content-Disposition: form-data; name="text"

            text default
            --9051914041544843365972754266--

            """.Crlf();

    static string onePartBodyTwoHeaders =
        """
            --9051914041544843365972754266
            Content-Disposition: form-data; name="text"
            Custom-header: custom-value

            text default
            --9051914041544843365972754266--

            """.Crlf();

    static string onePartBodyWithTrailingWhitespace =
        $"""
             --9051914041544843365972754266{openPadding}
             Content-Disposition: form-data; name="text"

             text default
             --9051914041544843365972754266--

             """.Crlf();

    // Non-compliant, but common: the last CRLF is left off.
    static string onePartBodyWithoutFinalCrlf =
        """
            --9051914041544843365972754266
            Content-Disposition: form-data; name="text"

            text default
            --9051914041544843365972754266--
            """.Crlf();

    static string twoPartBody =
        """
            --9051914041544843365972754266
            Content-Disposition: form-data; name="text"

            text default
            --9051914041544843365972754266
            Content-Disposition: form-data; name="file1"; filename="a.txt"
            Content-Type: text/plain

            Content of a.txt.

            --9051914041544843365972754266--

            """.Crlf();

    static string twoPartBodyWithUnicodeFileName =
        """
            --9051914041544843365972754266
            Content-Disposition: form-data; name="text"

            text default
            --9051914041544843365972754266
            Content-Disposition: form-data; name="file1"; filename="a色.txt"
            Content-Type: text/plain

            Content of a.txt.

            --9051914041544843365972754266--

            """.Crlf();

    static string threePartBody =
        """
            --9051914041544843365972754266
            Content-Disposition: form-data; name="text"

            text default
            --9051914041544843365972754266
            Content-Disposition: form-data; name="file1"; filename="a.txt"
            Content-Type: text/plain

            Content of a.txt.

            --9051914041544843365972754266
            Content-Disposition: form-data; name="file2"; filename="a.html"
            Content-Type: text/html

            <!DOCTYPE html><title>Content of a.html.</title>

            --9051914041544843365972754266--

            """.Crlf();

    // Truncated mid-delimiter, so no trailing newline.
    static string twoPartBodyIncompleteBuffer =
        """
            --9051914041544843365972754266
            Content-Disposition: form-data; name="text"

            text default
            --9051914041544843365972754266
            Content-Disposition: form-data; name="file1"; filename="a.txt"
            Content-Type: text/plain

            Content of a.txt.

            --9051914041544843365
            """.Crlf();

    static string boundaryWithGarbage =
        """
            --9051914041544843365972754266
            Content-Disposition: form-data; name="text"

            text default
            --9051914041544843365972754266 garbage

            """.Crlf();

    [Test]
    public async Task MultipartReader_ReadSinglePartBody_Success()
    {
        var reader = new MultipartReader(boundary, MakeStream(onePartBody));

        var section = await ReadSection(reader);
        await Assert.That(section.Headers).Count().IsEqualTo(1);
        await Assert.That(section.ContentDisposition).IsEqualTo("form-data; name=\"text\"");
        await Assert.That(await ReadBody(section)).IsEqualTo("text default");

        await Assert.That(await reader.ReadNextSectionAsync()).IsNull();
    }

    [Test]
    public async Task MultipartReader_HeaderCountExceeded_Throws()
    {
        var reader = new MultipartReader(boundary, MakeStream(onePartBodyTwoHeaders))
        {
            HeadersCountLimit = 1
        };

        var exception = await Assert.ThrowsExactlyAsync<InvalidDataException>(() => reader.ReadNextSectionAsync());
        await Assert.That(exception!.Message).IsEqualTo("Multipart headers count limit 1 exceeded.");
    }

    [Test]
    public async Task MultipartReader_HeadersLengthExceeded_Throws()
    {
        var reader = new MultipartReader(boundary, MakeStream(onePartBodyTwoHeaders))
        {
            HeadersLengthLimit = 60
        };

        var exception = await Assert.ThrowsExactlyAsync<InvalidDataException>(() => reader.ReadNextSectionAsync());
        await Assert.That(exception!.Message).IsEqualTo("Line length limit 17 exceeded.");
    }

    // A single header line much larger than the internal read buffer (4 KiB) and the headers length
    // limit (16 KiB), never terminated with a CRLF. The limit has to be enforced while reading the
    // line, rather than by a length check after the whole payload is buffered in memory.
    [Test]
    public async Task MultipartReader_HeaderLineSpanningMultipleBuffers_EnforcesHeadersLengthLimit()
    {
        var body =
            $"""
                 --9051914041544843365972754266
                 {new string('a', 100_000)}
                 """.Crlf();
        var reader = new MultipartReader(boundary, MakeStream(body));

        var exception = await Assert.ThrowsExactlyAsync<InvalidDataException>(() => reader.ReadNextSectionAsync());
        await Assert.That(exception!.Message).IsEqualTo("Line length limit 16384 exceeded.");
    }

    [Test]
    public async Task MultipartReader_HeadersLengthExceeded_LargePreamble()
    {
        var body =
            $"""
                 preamble {new string('a', 17000)}
                 --9051914041544843365972754266

                 text default
                 --9051914041544843365972754266--

                 """.Crlf();
        var reader = new MultipartReader(boundary, MakeStream(body));

        var exception = await Assert.ThrowsExactlyAsync<InvalidDataException>(() => reader.ReadNextSectionAsync());
        await Assert.That(exception!.Message).IsEqualTo("Multipart header length limit 16384 exceeded. Too much data before the first boundary.");
    }

    [Test]
    public async Task MultipartReader_HeadersLengthLimitSettable_LargePreamblePasses()
    {
        var body =
            $"""
                 preamble {new string('a', 100_000)}
                 --9051914041544843365972754266

                 text default
                 --9051914041544843365972754266--

                 """.Crlf();
        var reader = new MultipartReader(boundary, MakeStream(body))
        {
            HeadersLengthLimit = 200_000
        };

        var section = await ReadSection(reader);
        await Assert.That(await ReadBody(section)).IsEqualTo("text default");
    }

    [Test]
    public async Task MultipartReader_ReadSinglePartBodyWithTrailingWhitespace_Success()
    {
        var reader = new MultipartReader(boundary, MakeStream(onePartBodyWithTrailingWhitespace));

        var section = await ReadSection(reader);
        await Assert.That(section.Headers).Count().IsEqualTo(1);
        await Assert.That(section.ContentDisposition).IsEqualTo("form-data; name=\"text\"");
        await Assert.That(await ReadBody(section)).IsEqualTo("text default");

        await Assert.That(await reader.ReadNextSectionAsync()).IsNull();
    }

    [Test]
    public async Task MultipartReader_ReadSinglePartBodyWithoutLastCRLF_Success()
    {
        var reader = new MultipartReader(boundary, MakeStream(onePartBodyWithoutFinalCrlf));

        var section = await ReadSection(reader);
        await Assert.That(section.Headers).Count().IsEqualTo(1);
        await Assert.That(section.ContentDisposition).IsEqualTo("form-data; name=\"text\"");
        await Assert.That(await ReadBody(section)).IsEqualTo("text default");

        await Assert.That(await reader.ReadNextSectionAsync()).IsNull();
    }

    [Test]
    public async Task MultipartReader_ReadTwoPartBody_Success()
    {
        var reader = new MultipartReader(boundary, MakeStream(twoPartBody));

        var section = await ReadSection(reader);
        await Assert.That(section.Headers).Count().IsEqualTo(1);
        await Assert.That(section.ContentDisposition).IsEqualTo("form-data; name=\"text\"");
        await Assert.That(await ReadBody(section)).IsEqualTo("text default");

        section = await ReadSection(reader);
        await Assert.That(section.Headers).Count().IsEqualTo(2);
        await Assert.That(section.ContentDisposition).IsEqualTo("form-data; name=\"file1\"; filename=\"a.txt\"");
        await Assert.That(section.ContentType).IsEqualTo("text/plain");
        await Assert.That(await ReadBody(section)).IsEqualTo("Content of a.txt.\r\n");

        await Assert.That(await reader.ReadNextSectionAsync()).IsNull();
    }

    [Test]
    public async Task MultipartReader_ReadTwoPartBodyWithUnicodeFileName_Success()
    {
        var reader = new MultipartReader(boundary, MakeStream(twoPartBodyWithUnicodeFileName));

        var section = await ReadSection(reader);
        await Assert.That(section.Headers).Count().IsEqualTo(1);
        await Assert.That(section.ContentDisposition).IsEqualTo("form-data; name=\"text\"");
        await Assert.That(await ReadBody(section)).IsEqualTo("text default");

        section = await ReadSection(reader);
        await Assert.That(section.Headers).Count().IsEqualTo(2);
        await Assert.That(section.ContentDisposition).IsEqualTo("form-data; name=\"file1\"; filename=\"a色.txt\"");
        await Assert.That(section.ContentType).IsEqualTo("text/plain");
        await Assert.That(await ReadBody(section)).IsEqualTo("Content of a.txt.\r\n");

        await Assert.That(await reader.ReadNextSectionAsync()).IsNull();
    }

    [Test]
    public async Task MultipartReader_ThreePartBody_Success()
    {
        var reader = new MultipartReader(boundary, MakeStream(threePartBody));

        var section = await ReadSection(reader);
        await Assert.That(section.Headers).Count().IsEqualTo(1);
        await Assert.That(section.ContentDisposition).IsEqualTo("form-data; name=\"text\"");
        await Assert.That(await ReadBody(section)).IsEqualTo("text default");

        section = await ReadSection(reader);
        await Assert.That(section.Headers).Count().IsEqualTo(2);
        await Assert.That(section.ContentDisposition).IsEqualTo("form-data; name=\"file1\"; filename=\"a.txt\"");
        await Assert.That(section.ContentType).IsEqualTo("text/plain");
        await Assert.That(await ReadBody(section)).IsEqualTo("Content of a.txt.\r\n");

        section = await ReadSection(reader);
        await Assert.That(section.Headers).Count().IsEqualTo(2);
        await Assert.That(section.ContentDisposition).IsEqualTo("form-data; name=\"file2\"; filename=\"a.html\"");
        await Assert.That(section.ContentType).IsEqualTo("text/html");
        await Assert.That(await ReadBody(section)).IsEqualTo("<!DOCTYPE html><title>Content of a.html.</title>\r\n");

        await Assert.That(await reader.ReadNextSectionAsync()).IsNull();
    }

    [Test]
    public async Task MultipartReader_BufferSizeMustBeLargerThanBoundary_Throws()
    {
        var stream = MakeStream(threePartBody);

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = new MultipartReader(boundary, stream, 5));
    }

    [Test]
    public async Task MultipartReader_ReadMultipartBodyWithFilesForDeferredCopy_Success()
    {
        var reader = new MultipartReader(boundary, MakeStream(threePartBody));

        // Skip the text field section.
        await reader.ReadNextSectionAsync();

        var section = await ReadSection(reader);
        await Assert.That(section.Headers).Count().IsEqualTo(2);
        await Assert.That(section.ContentDisposition).IsEqualTo("form-data; name=\"file1\"; filename=\"a.txt\"");
        await Assert.That(section.ContentType).IsEqualTo("text/plain");
        var stream1 = section.Body;

        section = await ReadSection(reader);
        await Assert.That(section.Headers).Count().IsEqualTo(2);
        await Assert.That(section.ContentDisposition).IsEqualTo("form-data; name=\"file2\"; filename=\"a.html\"");
        await Assert.That(section.ContentType).IsEqualTo("text/html");
        var stream2 = section.Body;

        await Assert.That(await reader.ReadNextSectionAsync()).IsNull();

        await Assert.That(stream1.CanSeek).IsTrue();
        await Assert.That(stream1.Seek(0, SeekOrigin.Begin)).IsEqualTo(0);
        await Assert.That(await ReadStream(stream1)).IsEqualTo("Content of a.txt.\r\n");

        await Assert.That(stream2.CanSeek).IsTrue();
        await Assert.That(stream2.Seek(0, SeekOrigin.Begin)).IsEqualTo(0);
        await Assert.That(await ReadStream(stream2)).IsEqualTo("<!DOCTYPE html><title>Content of a.html.</title>\r\n");
    }

    [Test]
    public async Task MultipartReader_TwoPartBodyIncompleteBuffer_TwoSectionsReadSuccessfullyThirdSectionThrows()
    {
        var reader = new MultipartReader(boundary, MakeStream(twoPartBodyIncompleteBuffer));
        var buffer = new byte[128];

        var section = await ReadSection(reader);
        await Assert.That(section.Headers).Count().IsEqualTo(1);
        await Assert.That(section.ContentDisposition).IsEqualTo("form-data; name=\"text\"");
        // ReSharper disable once MethodHasAsyncOverload
        var read = section.Body.Read(buffer, 0, buffer.Length);
        await Assert.That(GetString(buffer, read)).IsEqualTo("text default");

        // The second section reads even though its closing boundary is truncated.
        section = await ReadSection(reader);
        await Assert.That(section.Headers).Count().IsEqualTo(2);
        await Assert.That(section.ContentDisposition).IsEqualTo("form-data; name=\"file1\"; filename=\"a.txt\"");
        await Assert.That(section.ContentType).IsEqualTo("text/plain");
        // ReSharper disable once MethodHasAsyncOverload
        read = section.Body.Read(buffer, 0, buffer.Length);
        await Assert.That(GetString(buffer, read)).IsEqualTo("Content of a.txt.\r\n");

        // There are not enough bytes left to even contain a final boundary.
        await Assert.ThrowsExactlyAsync<IOException>(() => reader.ReadNextSectionAsync());
    }

    [Test]
    public async Task MultipartReader_ReadInvalidUtf8Header_ReplacementCharacters()
    {
        var reader = new MultipartReader(boundary, MakeSplitHeaderStream([0xC1, 0x21]));

        var section = await ReadSection(reader);
        await Assert.That(section.Headers).Count().IsEqualTo(1);
        await Assert.That(section.ContentDisposition).IsEqualTo("form-data; name=\"text\" filename=\"a�!.txt\"");
        await Assert.That(await ReadBody(section)).IsEqualTo("text default");

        await Assert.That(await reader.ReadNextSectionAsync()).IsNull();
    }

    [Test]
    public async Task MultipartReader_ReadInvalidUtf8SurrogateHeader_ReplacementCharacters()
    {
        var reader = new MultipartReader(boundary, MakeSplitHeaderStream([0xED, 0xA0, 85]));

        var section = await ReadSection(reader);
        await Assert.That(section.Headers).Count().IsEqualTo(1);
        await Assert.That(section.ContentDisposition).IsEqualTo("form-data; name=\"text\" filename=\"a��U.txt\"");
        await Assert.That(await ReadBody(section)).IsEqualTo("text default");

        await Assert.That(await reader.ReadNextSectionAsync()).IsNull();
    }

    // The reader strips quotes from the boundary rather than throwing.
    [Test]
    public async Task MultipartReader_StripQuotesFromBoundary()
    {
        var reader = new MultipartReader(boundaryWithQuotes, MakeStream(onePartBody));

        await Assert.That(await reader.ReadNextSectionAsync()).IsNotNull();
    }

    [Test]
    public async Task SyncReadWithOffsetWorks()
    {
        var reader = new MultipartReader(boundary, MakeStream(onePartBody));
        var buffer = new byte[5];

        var section = await ReadSection(reader);
        await Assert.That(section.Headers).Count().IsEqualTo(1);
        await Assert.That(section.ContentDisposition).IsEqualTo("form-data; name=\"text\"");

        // ReSharper disable once MethodHasAsyncOverload
        var read = section.Body.Read(buffer, 2, buffer.Length - 2);
        await Assert.That(GetString(buffer, read + 2)).IsEqualTo("\0\0tex");

        // ReSharper disable once MethodHasAsyncOverload
        read = section.Body.Read(buffer, 1, buffer.Length - 1);
        await Assert.That(GetString(buffer, read + 1)).IsEqualTo("\0t de");

        // ReSharper disable once MethodHasAsyncOverload
        read = section.Body.Read(buffer, 0, buffer.Length);
        await Assert.That(GetString(buffer, read)).IsEqualTo("fault");

        await Assert.That(await reader.ReadNextSectionAsync()).IsNull();
    }

    // A boundary line with trailing data that is not the "--" final marker.
    [Test]
    public async Task MultipartReader_BoundaryWithUnexpectedTrailingData_ThrowsIOException()
    {
        var reader = new MultipartReader(boundary, MakeStream(boundaryWithGarbage));

        var section = await ReadSection(reader);

        await Assert.ThrowsExactlyAsync<IOException>(() => section.Body.CopyToAsync(new MemoryStream()));
    }

    // The same, through the synchronous read path.
    [Test]
    public async Task MultipartReader_BoundaryWithUnexpectedTrailingData_SyncRead_ThrowsIOException()
    {
        var reader = new MultipartReader(boundary, MakeStream(boundaryWithGarbage));

        var section = await ReadSection(reader);

        var buffer = new byte[1024];
        Assert.ThrowsExactly<IOException>(() =>
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
            $"""
                 --9051914041544843365972754266
                 Content-Disposition: form-data; name="text"

                 text default
                 --9051914041544843365972754266--{closePadding}

                 """.Crlf();
        var reader = new MultipartReader(boundary, MakeStream(body));

        var section = await ReadSection(reader);
        await Assert.That(await ReadBody(section)).IsEqualTo("text default");

        await Assert.That(await reader.ReadNextSectionAsync()).IsNull();
    }

    // A non-final boundary with non-whitespace trailing data.
    [Test]
    public async Task MultipartReader_IntermediateBoundaryWithTrailingData_ThrowsIOException()
    {
        var body =
            """
                --9051914041544843365972754266
                Content-Disposition: form-data; name="text"

                text default
                --9051914041544843365972754266 notwhitespace
                Content-Disposition: form-data; name="text2"

                text2
                --9051914041544843365972754266--

                """.Crlf();
        var reader = new MultipartReader(boundary, MakeStream(body));

        var section = await ReadSection(reader);

        await Assert.ThrowsExactlyAsync<IOException>(() => section.Body.CopyToAsync(new MemoryStream()));
    }

    static MemoryStream MakeStream(string text) =>
        new(Encoding.UTF8.GetBytes(text));

    // A one-part body with the given raw bytes spliced into the middle of a header value.
    static MemoryStream MakeSplitHeaderStream(byte[] invalid)
    {
        // The two halves meet mid-header-value, so neither carries the newline at the splice.
        var before =
            """
                --9051914041544843365972754266
                Content-Disposition: form-data; name="text" filename="a
                """.Crlf();
        var after =
            """
                .txt"

                text default
                --9051914041544843365972754266--

                """.Crlf();

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
        await Assert.That(section).IsNotNull();
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
