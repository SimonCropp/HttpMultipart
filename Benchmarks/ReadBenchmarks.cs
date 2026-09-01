using BenchmarkDotNet.Attributes;

namespace Benchmarks;

/// <summary>
/// Streaming one large part out of a body, which is the read path that costs constant memory however
/// large the part is.
/// </summary>
/// <remarks>
/// A single read can never return more than the internal buffer holds, whatever buffer the caller
/// passes, so <see cref="BufferSize"/> is the knob that decides how many read calls a large part takes.
/// That is what this prices, and it is the arm that moves when the reader stops paying an extra pooled
/// array and a full extra copy per read.
/// </remarks>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class ReadBenchmarks
{
    byte[] body = null!;
    string boundary = null!;

    /// <summary>The part is 16 MB, which is large enough that per-read overhead dominates.</summary>
    const int partSize = 16 * 1024 * 1024;

    [Params(4 * 1024, 64 * 1024, 1024 * 1024)]
    public int BufferSize { get; set; }

    /// <summary>
    /// Disposing returns the read buffer to the pool. Undisposed is what every caller did before the
    /// reader was disposable, and is why allocation here used to track <see cref="BufferSize"/>.
    /// </summary>
    [Params(true, false)]
    public bool DisposeReader { get; set; }

    [GlobalSetup]
    public void Setup() =>
        (body, boundary) = Bodies.OnePart(partSize);

    [Benchmark(Description = "stream one 16 MB part to Stream.Null")]
    public async Task<long> CopyTo()
    {
        var reader = new MultipartReader(boundary, new MemoryStream(body), BufferSize);
        try
        {
            var section = await reader.ReadNextSectionAsync();
            await section!.Body.CopyToAsync(System.IO.Stream.Null);
            return section.Body.Length;
        }
        finally
        {
            if (DisposeReader)
            {
                reader.Dispose();
            }
        }
    }
}
