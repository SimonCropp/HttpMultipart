/// <summary>
/// RFC 2046 edge cases the ported aspnetcore tests do not reach, and the two places this reader
/// deliberately differs from upstream. The payloads are written from the RFC rather than lifted from
/// another implementation's suite.
/// </summary>
/// <remarks>
/// The bodies are raw string literals converted by <see cref="WireExtensions.Crlf"/>, since the
/// delimiter is defined in terms of CRLF. The one body that does not call it is the one testing what
/// happens without CRLF.
/// </remarks>
public class MultipartConformanceTests
{
    const string boundary = "test-boundary";

    [Test]
    public async Task EmptyPartBodyReadsAsEmpty()
    {
        var reader = Read(
            """
            --test-boundary
            Content-Type: text/plain


            --test-boundary--

            """);

        var section = await ReadSection(reader);
        await Assert.That(section.ContentType).IsEqualTo("text/plain");
        await Assert.That(await Body(section)).IsEmpty();

        await Assert.That(await reader.ReadNextSectionAsync()).IsNull();
    }

    [Test]
    public async Task PartWithNoHeadersIsRead()
    {
        var reader = Read(
            """
            --test-boundary

            data
            --test-boundary--

            """);

        var section = await ReadSection(reader);
        await Assert.That(section.Headers).IsEmpty();
        await Assert.That(await Body(section)).IsEqualTo("data");
    }

    // "There appears to be room for additional information prior to the first boundary delimiter line
    // [...] this 'preamble' area should generally be left blank" — and, either way, discarded.
    [Test]
    public async Task PreambleIsDiscarded()
    {
        var reader = Read(
            """
            this is a preamble a reader must ignore
            --test-boundary

            data
            --test-boundary--

            """);

        var section = await ReadSection(reader);
        await Assert.That(await Body(section)).IsEqualTo("data");
    }

    [Test]
    public async Task EpilogueAfterTheCloseDelimiterIsDiscarded()
    {
        var reader = Read(
            """
            --test-boundary

            data
            --test-boundary--
            this is an epilogue a reader must ignore

            """);

        var section = await ReadSection(reader);
        await Assert.That(await Body(section)).IsEqualTo("data");

        await Assert.That(await reader.ReadNextSectionAsync()).IsNull();
    }

    // The epilogue is drained rather than returned, but only up to HeadersLengthLimit: an endless one
    // must not be pulled into memory on the way to reporting the end of the body. This is the only
    // place the reader passes a limit when draining, and the short epilogue above is the other half of
    // the pair.
    [Test]
    public async Task AnEpilogueBeyondTheHeadersLengthLimitIsRefused()
    {
        var reader = Read(
            $"""
            --test-boundary

            data
            --test-boundary--
            {new string('e', 20_000)}

            """);

        var section = await ReadSection(reader);
        await Assert.That(await Body(section)).IsEqualTo("data");

        var exception = await Assert.ThrowsExactlyAsync<InvalidDataException>(() => reader.ReadNextSectionAsync());
        await Assert.That(exception!.Message).IsEqualTo("The stream exceeded the data limit 16384.");
    }

    [Test]
    public async Task BodyOfOnlyACloseDelimiterHasNoSections()
    {
        var reader = Read(
            """
            --test-boundary--

            """);

        await Assert.That(await reader.ReadNextSectionAsync()).IsNull();
    }

    // A delimiter is only a delimiter at the start of a line. The same characters mid-line are content,
    // which is what the reader's partial-match scan exists to get right.
    [Test]
    public async Task BoundaryTextMidLineIsContent()
    {
        var reader = Read(
            """
            --test-boundary

            text--test-boundary more
            --test-boundary--

            """);

        var section = await ReadSection(reader);
        await Assert.That(await Body(section)).IsEqualTo("text--test-boundary more");
    }

    // With a buffer this small the body spans many refills and the delimiter itself straddles one, so
    // the read has to carry a partial match across the boundary of its own buffer.
    [Test]
    public async Task ContentSpanningManyBufferRefillsIsReadWhole()
    {
        var content = new string('x', 500);
        var stream = MakeStream(
            $"""
            --test-boundary

            {content}
            --test-boundary--

            """.Crlf());
        var reader = new MultipartReader(boundary, stream, bufferSize: boundary.Length + 8);

        var section = await ReadSection(reader);
        await Assert.That(await Body(section)).IsEqualTo(content);

        await Assert.That(await reader.ReadNextSectionAsync()).IsNull();
    }

    // A read looks for a boundary only as far as it could return, so with an internal buffer far larger
    // than the caller's these reads each scan a sliver of what is buffered and the boundary is found by
    // whichever call reaches it. The content is laced with near-misses — a prefix of the delimiter that
    // is not one — so a window that stopped a byte short would show up as content going missing.
    [Test]
    [Arguments(97)]
    [Arguments(1)]
    public async Task ABoundaryBeyondTheCallersBufferIsFoundByTheCallThatReachesIt(int readSize)
    {
        var content = NearMissContent();
        var reader = new MultipartReader(
            boundary,
            MakeStream(Laced(content)),
            bufferSize: 64 * 1024);

        var section = await ReadSection(reader);
        await Assert.That(await Drain(section, readSize)).IsEqualTo(content);

        await Assert.That(await reader.ReadNextSectionAsync()).IsNull();
    }

    // The same through the synchronous read path, which has its own copy of the scan.
    [Test]
    public async Task ABoundaryBeyondTheCallersBufferIsFoundBySyncReadsToo()
    {
        var content = NearMissContent();
        var reader = new MultipartReader(
            boundary,
            MakeStream(Laced(content)),
            bufferSize: 64 * 1024);

        var section = await ReadSection(reader);

        var read = new MemoryStream();
        var buffer = new byte[97];
        int count;
        // ReSharper disable once MethodHasAsyncOverload
        while ((count = section.Body.Read(buffer, 0, buffer.Length)) > 0)
        {
            read.Write(buffer, 0, count);
        }

        await Assert.That(Encoding.UTF8.GetString(read.ToArray())).IsEqualTo(content);
        await Assert.That(await reader.ReadNextSectionAsync()).IsNull();
    }

    static string NearMissContent() =>
        string.Concat(
            Enumerable.Repeat("\r\n--test-bound is not the delimiter\r\n" + new string('x', 200), 200));

    static string Laced(string content) =>
        $"""
        --test-boundary

        {content}
        --test-boundary--

        """.Crlf();

    static async Task<string> Drain(MultipartSection section, int readSize)
    {
        var read = new MemoryStream();
        var buffer = new byte[readSize];
        int count;
        while ((count = await section.Body.ReadAsync(buffer)) > 0)
        {
            read.Write(buffer, 0, count);
        }

        return Encoding.UTF8.GetString(read.ToArray());
    }

    // The delimiter is defined in terms of CRLF. A body using bare LF has no delimiter line at all, and
    // the trailing content is reported rather than silently treated as a part. This is the one body
    // here that goes through Lf rather than Crlf.
    [Test]
    public async Task LineFeedOnlyDelimitersAreNotDelimiters()
    {
        var reader = new MultipartReader(
            boundary,
            MakeStream(
                """
                --test-boundary

                data
                --test-boundary--

                """.Lf()));

        await Assert.ThrowsExactlyAsync<IOException>(() => reader.ReadNextSectionAsync());
    }

    [Test]
    public async Task HeaderNamesAreCaseInsensitive()
    {
        var reader = Read(
            """
            --test-boundary
            content-TYPE: text/plain

            data
            --test-boundary--

            """);

        var section = await ReadSection(reader);
        await Assert.That(section.ContentType).IsEqualTo("text/plain");
        await Assert.That(section.Headers!["Content-Type"]).IsEqualTo("text/plain");
    }

    // Where aspnetcore accumulates a repeated header into a StringValues, this reader keeps headers
    // single-valued and takes the last. Pinned because it is a deliberate difference, not an accident.
    [Test]
    public async Task ARepeatedHeaderNameIsLastWins()
    {
        var reader = Read(
            """
            --test-boundary
            X-Custom: one
            X-Custom: two

            data
            --test-boundary--

            """);

        var section = await ReadSection(reader);
        await Assert.That(section.Headers).Count().IsEqualTo(1);
        await Assert.That(section.Headers!["X-Custom"]).IsEqualTo("two");
    }

    [Test]
    public async Task ContentLengthIsReadFromTheHeader()
    {
        var reader = Read(
            """
            --test-boundary
            Content-Length: 4

            data
            --test-boundary--

            """);

        var section = await ReadSection(reader);
        await Assert.That(section.ContentLength).IsEqualTo(4);
    }

    [Test]
    [Arguments("not-a-number")]
    [Arguments("-1")]
    public async Task AnUnusableContentLengthIsNull(string value)
    {
        var reader = Read(
            $"""
            --test-boundary
            Content-Length: {value}

            data
            --test-boundary--

            """);

        var section = await ReadSection(reader);
        await Assert.That(section.ContentLength).IsNull();
    }

    // Not covered upstream: the per-section body limit the transport cannot enforce for the caller.
    [Test]
    public async Task BodyLengthLimitIsEnforced()
    {
        var reader = Read(
            """
            --test-boundary

            text default
            --test-boundary--

            """);
        reader.BodyLengthLimit = 5;

        var section = await ReadSection(reader);

        var exception = await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => section.Body.CopyToAsync(new MemoryStream()));
        await Assert.That(exception!.Message).IsEqualTo("Multipart body length limit 5 exceeded.");
    }

    // RFC 2046 allows 70 characters from a set wider than the hex most senders use.
    [Test]
    public async Task ABoundaryOfMaximumLengthAndCharacterSetIsRead()
    {
        var boundary = "0'()+_,-./:=?abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ01234";
        await Assert.That(boundary).Length().IsEqualTo(70);

        var stream = MakeStream(
            $"""
            --{boundary}

            data
            --{boundary}--

            """.Crlf());
        var reader = new MultipartReader(boundary, stream);

        var section = await ReadSection(reader);
        await Assert.That(await Body(section)).IsEqualTo("data");
    }

    static MultipartReader Read(string body) =>
        new(boundary, MakeStream(body.Crlf()));

    static MemoryStream MakeStream(string text) =>
        new(Encoding.UTF8.GetBytes(text));

    static async Task<MultipartSection> ReadSection(MultipartReader reader)
    {
        var section = await reader.ReadNextSectionAsync();
        await Assert.That(section).IsNotNull();
        return section!;
    }

    static async Task<string> Body(MultipartSection section)
    {
        var buffer = new MemoryStream();
        await section.Body.CopyToAsync(buffer);
        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}
