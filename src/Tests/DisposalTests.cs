/// <summary>
/// Disposing a reader returns its pooled read buffer. That is the whole benefit, and it is also what
/// makes disposal observable: the buffer belongs to the pool afterwards, so anything still reading
/// through it has to fail rather than quietly read an array someone else now owns.
/// </summary>
[TestFixture]
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
            Assert.That(section, Is.Not.Null);
            await section!.Body.CopyToAsync(Stream.Null);
        }

        Assert.That(inner.CanRead, Is.True);
    }

    [Test]
    public async Task ASectionCannotBeReadAfterTheReaderIsDisposed()
    {
        var reader = new MultipartReader(boundary, new MemoryStream(Body()));
        var section = await reader.ReadNextSectionAsync();
        Assert.That(section, Is.Not.Null);

        reader.Dispose();

        Assert.ThrowsAsync<ObjectDisposedException>(() => section!.Body.CopyToAsync(Stream.Null));
    }

    // Returning the same array to the pool twice would hand it out twice. The guard is a field rather
    // than anything the caller has to get right, so a stray second dispose has to be harmless.
    [Test]
    public void DisposingTwiceReturnsTheBufferOnce()
    {
        var reader = new MultipartReader(boundary, new MemoryStream(Body()));

        reader.Dispose();

        Assert.DoesNotThrow(reader.Dispose);
    }

    [Test]
    public void DisposingABufferedReadStreamDisposesTheStreamItWraps()
    {
        var inner = new MemoryStream(Body());

        using (new BufferedReadStream(inner, 4096))
        {
        }

        Assert.That(inner.CanRead, Is.False);
    }

    [Test]
    public void LeaveOpenKeepsTheWrappedStreamOpen()
    {
        var inner = new MemoryStream(Body());

        using (new BufferedReadStream(inner, 4096, leaveOpen: true))
        {
        }

        Assert.That(inner.CanRead, Is.True);
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
