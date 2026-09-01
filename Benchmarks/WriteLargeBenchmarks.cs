using BenchmarkDotNet.Attributes;

namespace Benchmarks;

/// <summary>
/// Writing one large part with its <c>Content-Length</c>, held in memory against copied from a stream.
/// </summary>
/// <remarks>
/// Both arms write to <see cref="System.IO.Stream.Null"/>, so what the allocation column shows is the
/// part itself and nothing else. The buffered arm materialises it because that overload has no other
/// way to take it; the streamed arm never does. The time is the same either way — the whole point of
/// the length-carrying overload is the memory, not the speed.
/// </remarks>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class WriteLargeBenchmarks
{
    [Params(4 * 1024 * 1024)]
    public int PartSize { get; set; }

    [Benchmark(Baseline = true, Description = "one large part, held in memory")]
    public async Task Buffered()
    {
        var writer = MultipartWriter.Create(System.IO.Stream.Null);
        await writer.WritePart("application/octet-stream", new byte[PartSize]);
        await writer.Terminate();
    }

    [Benchmark(Description = "one large part, copied from a stream")]
    public async Task Streamed()
    {
        var writer = MultipartWriter.Create(System.IO.Stream.Null);
        await writer.WritePart("application/octet-stream", new Generated(PartSize), PartSize);
        await writer.Terminate();
    }

    /// <summary>
    /// A read-only stream of <paramref name="length"/> bytes that exists nowhere — it fills whatever
    /// buffer it is handed. Standing in for the file or socket a caller would be copying from, without
    /// which the buffered arm's allocation could not be told apart from the source's.
    /// </summary>
    sealed class Generated(int length) :
        Stream
    {
        long position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => length;

        public override long Position
        {
            get => position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            var toFill = (int) Math.Min(buffer.Length, length - position);
            buffer[..toFill].Fill(0x61);
            position += toFill;
            return toFill;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}
