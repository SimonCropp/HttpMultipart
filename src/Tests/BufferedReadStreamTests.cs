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

        var line = await stream.ReadLineAsync(lengthLimit: 100, CancellationToken.None);

        Assert.That(line, Is.EqualTo("hello world"));
    }

    [Test]
    public async Task ReadLineAsync_LineSpanningMultipleBuffersWithinLimit_Succeeds()
    {
        var content = new string('a', 100);
        var stream = MakeStream(content + "\r\n", bufferSize: 16);

        var line = await stream.ReadLineAsync(lengthLimit: 1000, CancellationToken.None);

        Assert.That(line, Is.EqualTo(content));
    }

    // The line is larger than both the buffer size and the length limit, so it spans several internal
    // buffers before the limit is reached.
    [Test]
    public void ReadLineAsync_LineSpanningMultipleBuffersExceedingLimit_Throws()
    {
        var stream = MakeStream(new string('a', 100) + "\r\n", bufferSize: 16);

        var exception = Assert.ThrowsAsync<InvalidDataException>(
            () => stream.ReadLineAsync(lengthLimit: 40, CancellationToken.None))!;
        Assert.That(exception.Message, Is.EqualTo("Line length limit 40 exceeded."));
    }

    // No CRLF terminator, using the real default buffer (4 KiB) and header limit (16 KiB). The limit
    // must be enforced while reading rather than by accumulating the whole payload first.
    [Test]
    public void ReadLineAsync_UnterminatedLineExceedingLimit_ThrowsInsteadOfAccumulating()
    {
        var stream = MakeStream(new('a', 100_000), bufferSize: 1024 * 4);

        var exception = Assert.ThrowsAsync<InvalidDataException>(
            () => stream.ReadLineAsync(lengthLimit: 1024 * 16, CancellationToken.None))!;
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

    // Both read paths in MultipartReaderStream call Read(Span<byte>). Without the override it falls to
    // the base Stream shim, which rents an array, calls the byte[] overload and copies again.
    [Test]
    public void ReadSpanDrainsTheBufferBeforeTheInnerStream()
    {
        var stream = MakeStream("hello world", bufferSize: 4096);
        Assert.That(stream.EnsureBuffered(), Is.True);
        Assert.That(stream.BufferedData.Count, Is.EqualTo(11));

        var buffer = new byte[5];
        Assert.That(stream.Read(buffer.AsSpan()), Is.EqualTo(5));

        Assert.That(Encoding.UTF8.GetString(buffer), Is.EqualTo("hello"));
        Assert.That(stream.BufferedData.Count, Is.EqualTo(6));
    }

    [Test]
    public void ReadSpanFallsThroughToTheInnerStreamWhenNothingIsBuffered()
    {
        var stream = MakeStream("hello world", bufferSize: 4096);
        Assert.That(stream.BufferedData.Count, Is.Zero);

        var buffer = new byte[5];
        Assert.That(stream.Read(buffer.AsSpan()), Is.EqualTo(5));

        Assert.That(Encoding.UTF8.GetString(buffer), Is.EqualTo("hello"));
        Assert.That(stream.BufferedData.Count, Is.Zero);
    }

    [Test]
    public void Read_Span_DrainsBufferedDataBeforeReadingInner()
    {
        // The buffer is rented from the pool, so its actual size may exceed the requested size.
        // The content is long enough that some of it always remains in the inner stream.
        const string content = "0123456789abcdefghijklmnopqrstuvwxyz0123456789abcdefghijklmnopqrstuvwxyz";
        var stream = MakeStream(content, bufferSize: 5);
        Assert.That(stream.EnsureBuffered(5), Is.True);
        var buffered = stream.BufferedData.Count;
        Assert.That(buffered, Is.InRange(5, content.Length - 3));

        Span<byte> buffer = stackalloc byte[3];

        // A span smaller than the buffered data drains it partially.
        var read = stream.Read(buffer);
        Assert.That(read, Is.EqualTo(3));
        Assert.That(Encoding.UTF8.GetString(buffer.Slice(0, read)), Is.EqualTo(content.Substring(0, 3)));
        Assert.That(stream.BufferedData.Count, Is.EqualTo(buffered - 3));

        // Each read returns only buffered data, so the last one returns the remainder
        // rather than topping up from the inner stream.
        var consumed = read;
        while (stream.BufferedData.Count > 0)
        {
            var remaining = stream.BufferedData.Count;
            read = stream.Read(buffer);
            Assert.That(read, Is.EqualTo(Math.Min(remaining, buffer.Length)));
            Assert.That(Encoding.UTF8.GetString(buffer.Slice(0, read)), Is.EqualTo(content.Substring(consumed, read)));
            consumed += read;
        }
        Assert.That(consumed, Is.EqualTo(buffered));

        // With the buffer drained, the read falls through to the inner stream.
        read = stream.Read(buffer);
        Assert.That(read, Is.EqualTo(3));
        Assert.That(Encoding.UTF8.GetString(buffer.Slice(0, read)), Is.EqualTo(content.Substring(consumed, 3)));
        Assert.That(stream.BufferedData.Count, Is.Zero);
    }

    [Test]
    public void Read_Array_DrainsBufferedDataBeforeReadingInner()
    {
        const string content = "0123456789abcdefghijklmnopqrstuvwxyz0123456789abcdefghijklmnopqrstuvwxyz";
        var stream = MakeStream(content, bufferSize: 5);
        Assert.That(stream.EnsureBuffered(5), Is.True);
        var buffered = stream.BufferedData.Count;
        Assert.That(buffered, Is.InRange(5, content.Length - 3));

        var buffer = new byte[3];

        var read = stream.Read(buffer, 0, buffer.Length);
        Assert.That(read, Is.EqualTo(3));
        Assert.That(Encoding.UTF8.GetString(buffer, 0, read), Is.EqualTo(content.Substring(0, 3)));
        Assert.That(stream.BufferedData.Count, Is.EqualTo(buffered - 3));

        var consumed = read;
        while (stream.BufferedData.Count > 0)
        {
            var remaining = stream.BufferedData.Count;
            read = stream.Read(buffer, 0, buffer.Length);
            Assert.That(read, Is.EqualTo(Math.Min(remaining, buffer.Length)));
            Assert.That(Encoding.UTF8.GetString(buffer, 0, read), Is.EqualTo(content.Substring(consumed, read)));
            consumed += read;
        }
        Assert.That(consumed, Is.EqualTo(buffered));

        read = stream.Read(buffer, 0, buffer.Length);
        Assert.That(read, Is.EqualTo(3));
        Assert.That(Encoding.UTF8.GetString(buffer, 0, read), Is.EqualTo(content.Substring(consumed, 3)));
        Assert.That(stream.BufferedData.Count, Is.Zero);
    }

    [Test]
    public void Write_Span_WritesToInnerStream()
    {
        var inner = new MemoryStream();
        var stream = new BufferedReadStream(inner, bufferSize: 16);

        stream.Write("hello"u8);

        Assert.That(Encoding.UTF8.GetString(inner.ToArray()), Is.EqualTo("hello"));
    }

    [Test]
    public void Read_Array_WithEmptyBuffer_ForwardsToInnerArrayOverload()
    {
        var inner = new OverloadCountingStream("hello"u8.ToArray());
        var stream = new BufferedReadStream(inner, bufferSize: 16);
        var buffer = new byte[5];

        var read = stream.Read(buffer, 0, buffer.Length);

        Assert.That(read, Is.EqualTo(5));
        Assert.That(Encoding.UTF8.GetString(buffer, 0, read), Is.EqualTo("hello"));
        Assert.That(inner.ArrayReads, Is.EqualTo(1));
        Assert.That(inner.SpanReads, Is.Zero);
    }

    [Test]
    public void Read_Span_WithEmptyBuffer_ForwardsToInnerSpanOverload()
    {
        var inner = new OverloadCountingStream("hello"u8.ToArray());
        var stream = new BufferedReadStream(inner, bufferSize: 16);
        Span<byte> buffer = stackalloc byte[5];

        var read = stream.Read(buffer);

        Assert.That(read, Is.EqualTo(5));
        Assert.That(Encoding.UTF8.GetString(buffer.Slice(0, read)), Is.EqualTo("hello"));
        Assert.That(inner.SpanReads, Is.EqualTo(1));
        Assert.That(inner.ArrayReads, Is.Zero);
    }

    [Test]
    public void Write_Array_ForwardsToInnerArrayOverload()
    {
        var inner = new OverloadCountingStream([]);
        var stream = new BufferedReadStream(inner, bufferSize: 16);
        var data = "hello"u8.ToArray();

        stream.Write(data, 0, data.Length);

        Assert.That(Encoding.UTF8.GetString(inner.ToArray()), Is.EqualTo("hello"));
        Assert.That(inner.ArrayWrites, Is.EqualTo(1));
        Assert.That(inner.SpanWrites, Is.Zero);
    }

    [Test]
    public void Write_Span_ForwardsToInnerSpanOverload()
    {
        var inner = new OverloadCountingStream([]);
        var stream = new BufferedReadStream(inner, bufferSize: 16);

        stream.Write("hello"u8);

        Assert.That(Encoding.UTF8.GetString(inner.ToArray()), Is.EqualTo("hello"));
        Assert.That(inner.SpanWrites, Is.EqualTo(1));
        Assert.That(inner.ArrayWrites, Is.Zero);
    }

    static BufferedReadStream MakeStream(string text, int bufferSize) =>
        new(new MemoryStream(Encoding.UTF8.GetBytes(text)), bufferSize);

    // Overrides both the array and span overloads so a test can tell which one BufferedReadStream
    // forwarded to. It wraps a MemoryStream rather than deriving from one because MemoryStream's
    // span overloads defer to Stream's base implementation for derived types, which rents an array
    // and calls the array overload, hiding the difference.
    sealed class OverloadCountingStream :
        Stream
    {
        readonly MemoryStream inner;

        public OverloadCountingStream(byte[] data)
        {
            inner = new();
            inner.Write(data, 0, data.Length);
            inner.Position = 0;
        }

        public int ArrayReads { get; private set; }
        public int SpanReads { get; private set; }
        public int ArrayWrites { get; private set; }
        public int SpanWrites { get; private set; }

        public byte[] ToArray() => inner.ToArray();

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => inner.Length;
        public override long Position
        {
            get => inner.Position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ArrayReads++;
            return inner.Read(buffer, offset, count);
        }

        public override int Read(Span<byte> buffer)
        {
            SpanReads++;
            return inner.Read(buffer);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            ArrayWrites++;
            inner.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            SpanWrites++;
            inner.Write(buffer);
        }

        public override void Flush() => inner.Flush();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
