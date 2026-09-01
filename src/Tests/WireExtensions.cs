static class WireExtensions
{
    /// <summary>
    /// The multipart delimiter is defined in terms of CRLF, but a raw string literal carries whatever
    /// line endings the file itself has. Normalising first, rather than replacing LF outright, means a
    /// checkout that produced CRLF gets the same bytes as this one, which produces LF.
    /// </summary>
    public static string Crlf(this string value) =>
        value.Lf()
            .Replace("\n", "\r\n");

    /// <summary>
    /// The same, for a body that is deliberately malformed by using bare LF where the delimiter
    /// requires CRLF. Without this the test would be asserting whatever the checkout happened to write.
    /// </summary>
    public static string Lf(this string value) =>
        value.Replace("\r\n", "\n");
}
