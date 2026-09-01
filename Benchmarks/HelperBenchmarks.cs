using BenchmarkDotNet.Attributes;

namespace Benchmarks;

/// <summary>
/// What the buffering helpers cost against streaming the same part. The allocation column is the point:
/// streaming is flat in the part size and both helpers are a multiple of it.
/// </summary>
/// <remarks>
/// <c>ReadAsBytesAsync</c> holds the grown buffer and the returned array at once, and
/// <c>ReadAsStringAsync</c> transcodes to UTF-16 and then copies the builder into one string. Neither
/// is wrong — they are for parts you know are small — but a caller reaching for one on a part of
/// unknown size should be able to see the price.
/// </remarks>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class HelperBenchmarks
{
    byte[] body = null!;
    string boundary = null!;

    [Params(64 * 1024, 4 * 1024 * 1024)]
    public int PartSize { get; set; }

    [GlobalSetup]
    public void Setup() =>
        (body, boundary) = Bodies.OnePart(PartSize, declareLength: true);

    async Task<MultipartSection> First()
    {
        var reader = new MultipartReader(boundary, new MemoryStream(body));
        return (await reader.ReadNextSectionAsync())!;
    }

    [Benchmark(Baseline = true, Description = "stream the part to Stream.Null")]
    public async Task<long> CopyTo()
    {
        var section = await First();
        await section.Body.CopyToAsync(System.IO.Stream.Null);
        return section.Body.Length;
    }

    [Benchmark(Description = "buffer the part to a byte array")]
    public async Task<int> ReadAsBytes()
    {
        var section = await First();
        return (await section.ReadAsBytesAsync()).Length;
    }

    [Benchmark(Description = "buffer the part to a string")]
    public async Task<int> ReadAsString()
    {
        var section = await First();
        return (await section.ReadAsStringAsync()).Length;
    }
}
