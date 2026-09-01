using System.Text;

namespace Benchmarks;

/// <summary>
/// Builds the multipart bodies the benchmarks read. They are written with <see cref="MultipartWriter"/>
/// rather than hand-assembled, so what is measured is a body the writer in this package actually
/// produces, and a framing bug could not quietly make the reader's job easier.
/// </summary>
static class Bodies
{
    /// <summary>A body of one part of <paramref name="size"/> bytes.</summary>
    public static (byte[] Body, string Boundary) OnePart(int size, bool declareLength = false)
    {
        var content = new byte[size];
        // Content that is not all one byte, and never contains a CR, so the reader's partial-boundary
        // scan finds no false positives to skew the measurement either way.
        for (var i = 0; i < content.Length; i++)
        {
            content[i] = (byte) ('a' + i % 26);
        }

        var stream = new MemoryStream();
        var writer = MultipartWriter.Create(stream);
        if (declareLength)
        {
            writer.WritePart("application/octet-stream", content).GetAwaiter().GetResult();
        }
        else
        {
            writer.OpenPart("application/octet-stream").GetAwaiter().GetResult();
            stream.Write(content);
        }

        writer.Terminate().GetAwaiter().GetResult();
        return (stream.ToArray(), writer.Boundary);
    }

    /// <summary>A body of <paramref name="count"/> parts, each carrying a short line of text.</summary>
    public static (byte[] Body, string Boundary) ManyParts(int count)
    {
        var stream = new MemoryStream();
        var writer = MultipartWriter.Create(stream);
        for (var i = 0; i < count; i++)
        {
            writer.OpenPart("text/plain").GetAwaiter().GetResult();
            stream.Write(Encoding.UTF8.GetBytes($"part {i} of {count}"));
        }

        writer.Terminate().GetAwaiter().GetResult();
        return (stream.ToArray(), writer.Boundary);
    }
}
