# Benchmarks

What the reader and writer cost on large payloads — the question the package could not answer before
these existed.

```bash
dotnet run -c Release --project Benchmarks -- --filter '*'
dotnet run -c Release --project Benchmarks -- --filter '*ReadBenchmarks*'
dotnet run -c Release --project Benchmarks -- --list flat
```

Release is mandatory (BenchmarkDotNet refuses a Debug build), and this is a project rather than a
solution: ProjectDefaults packs the `src` projects on any Release build and resolves their icon through
`$(SolutionDir)`, which MSBuild sets as a *global* property no `Directory.Build.props` can override.
Driving the project directly leaves it unset. The sources are **linked** rather than
project-referenced, which is also what a consumer of a source-only package actually compiles.

## Method

Bodies are built with `MultipartWriter` rather than hand-assembled (`Bodies.cs`), so what is measured is
a body this package actually produces, and a framing bug could not quietly make the reader's job
easier. Part content cycles `a`–`z` and never contains a CR, so the reader's partial-boundary scan
finds no false positives to skew either arm.

## Results

Recorded on .NET 10.0.11, X64 RyuJIT AVX2, BenchmarkDotNet 0.15.2. Treat the absolute times as
machine-specific; the ratios are the point.

### Buffering against streaming — one part, `HelperBenchmarks`

| Part | `Body.CopyToAsync` | `ReadAsBytesAsync` | `ReadAsStringAsync` |
| --- | --- | --- | --- |
| 64 KB | 4.5 µs, **7.5 KB** | 9.3 µs, 135.7 KB | 62.2 µs, 280.9 KB |
| 4 MB | 216 µs, **7.5 KB** | 1,603 µs, 11,272 KB | 4,211 µs, 16,450 KB |

Streaming is flat at 7.5 KB whatever the part size. Buffering to a byte array costs about **2.7× the
part** — the grown buffer and the returned array are alive at once — and to a string about **4×**,
since the content is transcoded to UTF-16 and then copied out of the builder. Neither helper is wrong;
they are for parts you know are small, and this is the price of using one when you don't.

### Buffer size — 16 MB part, `ReadBenchmarks`

| `bufferSize` | Mean | Allocated |
| --- | --- | --- |
| 4 KB (default) | **1.20 ms** | **6.9 KB** |
| 64 KB | 4.17 ms | 66.9 KB |
| 1 MB | 56.4 ms | 1,026.9 KB |

Raising `bufferSize` makes it **worse**, which is the opposite of what the per-call ceiling suggests —
a single read can never return more than the internal buffer holds, so a bigger buffer really does mean
fewer read calls.

`Allocated` says why, and it is not the payload: it tracks `bufferSize` almost exactly.
`MultipartReader` is not disposable, so the `BufferedReadStream` it owns is never disposed and its
pooled array is never returned; every reader allocates a fresh one. At 4 KB that is noise. At 1 MB it is
a large-object-heap allocation per reader, and the arm doing the fewest reads is 47× the slowest.
This shape is inherited from `Microsoft.AspNetCore.WebUtilities`.

So the guidance is narrower than "raise `bufferSize` for throughput": leave it alone unless one reader
consumes enough body to amortise an allocation of that size, and stay under 85 KB unless you mean to
allocate on the LOH per reader.

### Per-section cost — `SectionBenchmarks`

| Parts | Mean | Allocated |
| --- | --- | --- |
| 10 | 6.7 µs | 23.1 KB |
| 100 | 41.2 µs | 186.3 KB |
| 1000 | 398.2 µs | 1,817.5 KB |

Compare the growth, not the totals — constructing the reader is a fixed cost in every arm. The margin
is about **0.4 µs and 1.8 KB per part**: a reader stream, a header dictionary, and a string per header
line.

### Writing — `WriteBenchmarks`, `WriteLargeBenchmarks`

| | Mean | Allocated |
| --- | --- | --- |
| 1000 parts, one content type (cached opening) | **60.0 µs** | **257.5 KB** |
| 1000 parts, alternating types (no cache) | 122.8 µs | 514.8 KB |
| 4 MB part, held in memory | 137.5 µs | 4,097.5 KB |
| 4 MB part, copied from a stream | 103.5 µs | **5.4 KB** |

The opening-bytes cache is worth **2×** in both time and allocation, which is what justifies its
existence. The length-carrying overload allocates **759× less** for a large part — both arms write to
`Stream.Null`, so what the column shows is the part itself and nothing else. The times are close: the
point of that overload is the memory, not the speed.
