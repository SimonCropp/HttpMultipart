using BenchmarkDotNet.Attributes;

namespace Benchmarks;

/// <summary>
/// Reading with an explicit caller buffer, rather than through <c>CopyToAsync</c>.
/// </summary>
/// <remarks>
/// <c>CopyToAsync</c> cannot reach this: the base implementation asks for a buffer of 1 when a seekable
/// stream reports a length at or below its position, which a section always does before its first read,
/// so the override floors it at 4 KB and every copy reads in 4 KB regardless. A caller reading directly
/// picks its own size, and the gap between that and the internal buffer is what decides how much of the
/// buffered data each read has to scan for a boundary.
/// </remarks>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class ScanBenchmarks
{
    byte[] body = null!;
    string boundary = null!;
    byte[] readBuffer = null!;

    const int partSize = 16 * 1024 * 1024;

    /// <summary>Well above the caller buffer, which is where the scan window matters.</summary>
    [Params(1024 * 1024)]
    public int BufferSize { get; set; }

    [Params(64 * 1024, 256 * 1024)]
    public int ReadSize { get; set; }

    /// <summary>
    /// Content holding a CR per line against content holding none. The scan hunts the delimiter's
    /// leading CR, so the first makes it compare and the second lets it sail — and rescanning what a
    /// read could not return costs accordingly.
    /// </summary>
    [Params(true, false)]
    public bool TextContent { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        (body, boundary) = TextContent
            ? Bodies.OneTextPart(partSize)
            : Bodies.OnePart(partSize);
        readBuffer = new byte[ReadSize];
    }

    [Benchmark(Description = "read one 16 MB part with an explicit buffer")]
    public async Task<long> Read()
    {
        using var reader = new MultipartReader(boundary, new MemoryStream(body), BufferSize);
        var section = await reader.ReadNextSectionAsync();
        long total = 0;
        int read;
        while ((read = await section!.Body.ReadAsync(readBuffer)) > 0)
        {
            total += read;
        }

        return total;
    }
}
