using BenchmarkDotNet.Attributes;

namespace Benchmarks;

/// <summary>
/// The fixed cost of a section, isolated from the cost of its content: every part allocates a reader
/// stream, a header dictionary, and a string per header line.
/// </summary>
/// <remarks>
/// Compare the growth from 10 parts to 1000 rather than the absolute totals — the fixed cost of
/// constructing the reader is in every arm, and what is interesting is what each additional part adds.
/// </remarks>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class SectionBenchmarks
{
    byte[] body = null!;
    string boundary = null!;

    [Params(10, 100, 1000)]
    public int Parts { get; set; }

    [GlobalSetup]
    public void Setup() =>
        (body, boundary) = Bodies.ManyParts(Parts);

    [Benchmark(Description = "read every section, draining each body")]
    public async Task<int> ReadAll()
    {
        var reader = new MultipartReader(boundary, new MemoryStream(body));
        var count = 0;
        while (await reader.ReadNextSectionAsync() is {} section)
        {
            await section.Body.CopyToAsync(System.IO.Stream.Null);
            count++;
        }

        return count;
    }
}
