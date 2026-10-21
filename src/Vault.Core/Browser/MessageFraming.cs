using System.Buffers.Binary;

namespace Vault.Core.Browser;

/// <summary>
/// Chrome/Firefox native-messaging framing: a 4-byte little-endian length, then UTF-8 JSON.
/// The same framing is used on the named pipe, so the native host just relays frames.
/// </summary>
public static class MessageFraming
{
    /// <summary>Browsers cap host→browser messages at 1 MB; requests are far smaller.</summary>
    public const int MaxMessageSize = 1024 * 1024;

    /// <summary>Returns null at end of stream.</summary>
    /// <exception cref="InvalidDataException">Oversized or truncated frame.</exception>
    public static async Task<byte[]?> ReadAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        var header = new byte[4];
        if (!await ReadExactlyOrEndAsync(stream, header, cancellationToken)) return null;

        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is < 0 or > MaxMessageSize) throw new InvalidDataException($"Invalid message length {length}.");

        var body = new byte[length];
        if (!await ReadExactlyOrEndAsync(stream, body, cancellationToken)) throw new InvalidDataException("Truncated message.");
        return body;
    }

    public static async Task WriteAsync(Stream stream, ReadOnlyMemory<byte> message, CancellationToken cancellationToken = default)
    {
        if (message.Length > MaxMessageSize) throw new InvalidDataException("Message too large.");
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, message.Length);
        await stream.WriteAsync(header, cancellationToken);
        await stream.WriteAsync(message, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static async Task<bool> ReadExactlyOrEndAsync(Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer[read..], cancellationToken);
            if (n == 0) return read == 0 ? false : throw new InvalidDataException("Truncated message.");
            read += n;
        }
        return true;
    }
}
