/// <summary>
/// Disposing a reader returns its pooled read buffer. That is the whole benefit, and it is also what
/// makes disposal observable: the buffer belongs to the pool afterwards, so anything still reading
/// through it has to fail rather than quietly read an array someone else now owns.
/// </summary>
public class DisposalTests
{
    const string boundary = "test-boundary";

    [Test]
    public async Task DisposingTheReaderLeavesTheCallersStreamOpen()
    {
        var inner = new MemoryStream(Body());

        using (var reader = new MultipartReader(boundary, inner))
        {
            var section = await reader.ReadNextSectionAsync();
            await Assert.That(section).IsNotNull();
            await section!.Body.CopyToAsync(Stream.Null);
        }

        await Assert.That(inner.CanRead).IsTrue();
    }

    [Test]
    public async Task ASectionCannotBeReadAfterTheReaderIsDisposed()
    {
        var reader = new MultipartReader(boundary, new MemoryStream(Body()));
        var section = await reader.ReadNextSectionAsync();
        await Assert.That(section).IsNotNull();

        reader.Dispose();

        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => section!.Body.CopyToAsync(Stream.Null));
    }

    // Returning the same array to the pool twice would hand it out twice. The guard is a field rather
    // than anything the caller has to get right, so a stray second dispose has to be harmless.
    [Test]
    public async Task DisposingTwiceReturnsTheBufferOnce()
    {
        var reader = new MultipartReader(boundary, new MemoryStream(Body()));

        reader.Dispose();

        reader.Dispose();
    }

    [Test]
    public async Task DisposingABufferedReadStreamDisposesTheStreamItWraps()
    {
        var inner = new MemoryStream(Body());

        using (new BufferedReadStream(inner, 4096))
        {
        }

        await Assert.That(inner.CanRead).IsFalse();
    }

    [Test]
    public async Task LeaveOpenKeepsTheWrappedStreamOpen()
    {
        var inner = new MemoryStream(Body());

        using (new BufferedReadStream(inner, 4096, leaveOpen: true))
        {
        }

        await Assert.That(inner.CanRead).IsTrue();
    }

    static byte[] Body() =>
        Encoding.UTF8.GetBytes(
            """
            --test-boundary
            Content-Type: text/plain

            data
            --test-boundary--

            """.Crlf());
}
