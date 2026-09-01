// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/aspnetcore, src/Http/WebUtilities/test/BufferedReadStreamTests.cs.

[TestFixture]
public class BufferedReadStreamTests
{
    [Test]
    public async Task ReadLineAsync_LineWithinSingleBuffer_Succeeds()
    {
        var stream = MakeStream("hello world\r\n", bufferSize: 4096);

        var line = await stream.ReadLineAsync(lengthLimit: 100, Cancel.None);

        Assert.That(line, Is.EqualTo("hello world"));
    }

    [Test]
    public async Task ReadLineAsync_LineSpanningMultipleBuffersWithinLimit_Succeeds()
    {
        var content = new string('a', 100);
        var stream = MakeStream(content + "\r\n", bufferSize: 16);

        var line = await stream.ReadLineAsync(lengthLimit: 1000, Cancel.None);

        Assert.That(line, Is.EqualTo(content));
    }

    // The line is larger than both the buffer size and the length limit, so it spans several internal
    // buffers before the limit is reached.
    [Test]
    public void ReadLineAsync_LineSpanningMultipleBuffersExceedingLimit_Throws()
    {
        var stream = MakeStream(new string('a', 100) + "\r\n", bufferSize: 16);

        var exception = Assert.ThrowsAsync<InvalidDataException>(
            () => stream.ReadLineAsync(lengthLimit: 40, Cancel.None))!;
        Assert.That(exception.Message, Is.EqualTo("Line length limit 40 exceeded."));
    }

    // No CRLF terminator, using the real default buffer (4 KiB) and header limit (16 KiB). The limit
    // must be enforced while reading rather than by accumulating the whole payload first.
    [Test]
    public void ReadLineAsync_UnterminatedLineExceedingLimit_ThrowsInsteadOfAccumulating()
    {
        var stream = MakeStream(new('a', 100_000), bufferSize: 1024 * 4);

        var exception = Assert.ThrowsAsync<InvalidDataException>(
            () => stream.ReadLineAsync(lengthLimit: 1024 * 16, Cancel.None))!;
        Assert.That(exception.Message, Is.EqualTo("Line length limit 16384 exceeded."));
    }

    [Test]
    public void ReadLine_LineSpanningMultipleBuffersExceedingLimit_Throws()
    {
        var stream = MakeStream(new string('a', 100) + "\r\n", bufferSize: 16);

        var exception = Assert.Throws<InvalidDataException>(() => stream.ReadLine(lengthLimit: 40))!;
        Assert.That(exception.Message, Is.EqualTo("Line length limit 40 exceeded."));
    }

    [Test]
    public void ReadLine_LineSpanningMultipleBuffersWithinLimit_Succeeds()
    {
        var content = new string('a', 100);
        var stream = MakeStream(content + "\r\n", bufferSize: 16);

        var line = stream.ReadLine(lengthLimit: 1000);

        Assert.That(line, Is.EqualTo(content));
    }

    static BufferedReadStream MakeStream(string text, int bufferSize) =>
        new(new MemoryStream(Encoding.UTF8.GetBytes(text)), bufferSize);
}
