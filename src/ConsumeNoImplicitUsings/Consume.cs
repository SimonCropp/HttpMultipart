// Implicit usings are off here, so the package's shipped global using does not apply and this has to
// name the namespace itself. What it proves is that the shipped sources compile without any of the
// usings an implicit-usings project would have handed them.

using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HttpMultipart;

static class Consume
{
    static async Task Read(HttpContent content, CancellationToken cancel)
    {
        if (!content.TryGetMultipartBoundary(out var boundary))
        {
            return;
        }

        var reader = new MultipartReader(boundary, await content.ReadAsStreamAsync(cancel));
        while (await reader.ReadNextSectionAsync(cancel) is {} section)
        {
            _ = await section.ReadAsStringAsync(cancel);
        }
    }

    static async Task Write(Stream stream, CancellationToken cancel)
    {
        var writer = MultipartWriter.Create(stream);
        await writer.WritePart("text/plain", new byte[] {1}, cancel);
        await writer.Terminate(cancel);
    }

    static Task Main() =>
        Task.WhenAll(
            Read(new ByteArrayContent([]), default),
            Write(Stream.Null, default));
}
