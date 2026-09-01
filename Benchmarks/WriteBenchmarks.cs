using BenchmarkDotNet.Attributes;

namespace Benchmarks;

/// <summary>
/// The writer's one optimisation: <c>OpenPart</c> caches the opening bytes of a repeated content type,
/// so a caller opening a part per row writes one array it built once. Alternating types defeats the
/// cache, and the gap between the arms is what it is worth.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class WriteBenchmarks
{
    static readonly byte[] row = "a row of content"u8.ToArray();

    [Params(1000)]
    public int Parts { get; set; }

    [Benchmark(Baseline = true, Description = "a part per row, one content type (cached opening)")]
    public async Task<long> CachedOpening()
    {
        var stream = new MemoryStream();
        var writer = MultipartWriter.Create(stream);
        for (var i = 0; i < Parts; i++)
        {
            await writer.OpenPart("application/x-ndjson");
            await stream.WriteAsync(row);
        }

        await writer.Terminate();
        return stream.Length;
    }

    [Benchmark(Description = "a part per row, alternating types (no cache)")]
    public async Task<long> AlternatingTypes()
    {
        var stream = new MemoryStream();
        var writer = MultipartWriter.Create(stream);
        for (var i = 0; i < Parts; i++)
        {
            await writer.OpenPart(i % 2 == 0 ? "application/x-ndjson" : "application/json");
            await stream.WriteAsync(row);
        }

        await writer.Terminate();
        return stream.Length;
    }
}
