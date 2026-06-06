using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AdLib.IO;

public static class StreamIO
{
    /// <summary>
    ///     Converts a 64-bit unsigned integer to a little-endian array of bytes
    ///     with variable length. The encoding is possible and reversible for all
    ///     numbers.
    /// </summary>
    /// <param name="value">The value to encode.</param>
    /// <returns>
    ///     A little-endian array of bytes of variable length that encodes
    ///     the specified number.
    /// </returns>
    public static byte[] EncodeVarInt(ulong value)
    {
        // NOTE: least-significant first = little endian
        byte[] bits = new byte[10]; // maximum (total 70 bits)
        int count = 0;

        // never falls through - should always break (otherwise the entire
        // number has continuation bits set, which would break it)
        for (int i = 0; i < bits.Length; i++)
        {
            // add value
            bits[i] = (byte)(value & 0x7F);
            count++;

            // set MSB if there's more to come
            value >>= 7;

            // operates on updated value
            if (value != 0)
            {
                bits[i] |= 0x80; // go back again
            }
            else
            {
                break;
            }
        }

        // trim to length
        byte[] result = new byte[count];
        Array.Copy(bits, result, count);
        return result;
    }

    public static ulong DecodeVarLong(byte[] bits)
    {
        ulong value = 0;
        int i = 0;

        // short circuit zero-check so if it's the first position it doesn't
        // throw (the 0th byte is always parsed)
        // if not 0th, check that the previous byte's continuation bit is set
        while (i == 0 || (bits[i - 1] & 0x80) != 0)
        {
            checked
            {
                // shift value into appropriate location, ignoring continuation
                // bit
                value |= (ulong)(bits[i] & 0x7F) << (7 * i);
                i++;
            }
        }

        return value;
    }

    public static void WriteVarInt(Stream stream, ulong value)
    {
        byte[] encoded = EncodeVarInt(value);
        stream.Write(encoded, 0, encoded.Length);
    }

    public static ulong ReadVarLong(Stream stream)
    {
        ulong value = 0;
        int index = 0;
        byte currentByte;

        do
        {
            int read = stream.ReadByte();
            if (read == -1) throw new EndOfStreamException("Insufficient data for VarInt.");
            currentByte = (byte)read;

            checked
            {
                value |= (ulong)(currentByte & 0x7F) << (7 * index);
            }

            index++;
        } while ((currentByte & 0x80) != 0);

        return value;
    }

    public static uint ReadVarInt(Stream stream) => checked((uint)ReadVarLong(stream));

    public static void WriteFixed(Stream stream, byte[] data) => stream.Write(data, 0, data.Length);

    public static byte[] ReadFixed(Stream stream, int length)
    {
        byte[] buffer = new byte[length];
        int totalRead = 0;

        while (totalRead < length)
        {
            int read = stream.Read(buffer, totalRead, length - totalRead);

            if (read <= 0)
            {
                throw new EndOfStreamException(
                    $"Insufficient data - expected {length} bytes, got {totalRead}");
            }

            totalRead += read;
        }

        return buffer;
    }

    public static void WriteUInt32(Stream stream, uint value) =>
        stream.Write(BitConverter.GetBytes(value), 0, 4);

    public static uint ReadUInt32(Stream stream) => BitConverter.ToUInt32(ReadFixed(stream, 4), 0);

    public static void WriteString(Stream stream, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        WriteVarInt(stream, (ulong)bytes.Length);
        stream.Write(bytes, 0, bytes.Length);
    }

    public static string ReadString(Stream stream)
    {
        int length = (int)ReadVarInt(stream);
        byte[] bytes = ReadBlock(stream, length);
        return Encoding.UTF8.GetString(bytes);
    }

    public static void WriteBlock(Stream stream, ReadOnlySpan<byte> block)
    {
        stream.Write(BitConverter.GetBytes(block.Length), 0, 4);
        stream.Write(block);
    }

    public static byte[] ReadBlock(Stream stream, int? length = null)
    {
        int actualLength = length ?? (int)ReadUInt32(stream);
        return ReadFixed(stream, actualLength);
    }

    public static async Task WriteFixedAsync(Stream stream, byte[] data, CancellationToken token = default)
    {
        await stream.WriteAsync(data.AsMemory(0, data.Length), token);
    }

    public static async Task<byte[]> ReadFixedAsync(Stream stream, int length, CancellationToken token = default)
    {
        Memory<byte> buffer = new(new byte[length]);
        int totalRead = 0;

        while (totalRead < length)
        {
            int read = await stream.ReadAsync(buffer.Slice(totalRead, length - totalRead), token);

            if (read <= 0)
            {
                throw new EndOfStreamException(
                    $"Insufficient data - expected {length} bytes, got {totalRead}");
            }

            totalRead += read;
        }

        return buffer.ToArray();
    }

    public static async Task WriteUInt32Async(Stream stream, uint value, CancellationToken token = default)
    {
        await stream.WriteAsync(BitConverter.GetBytes(value).AsMemory(0, 4), token);
    }

    public static async Task<uint> ReadUInt32Async(Stream stream, CancellationToken token = default) =>
        BitConverter.ToUInt32(await ReadFixedAsync(stream, 4, token), 0);

    public static async Task WriteBlockAsync(
        Stream stream, ReadOnlyMemory<byte> block, CancellationToken token = default
    )
    {
        await stream.WriteAsync(BitConverter.GetBytes(block.Length).AsMemory(0, 4), token);
        await stream.WriteAsync(block, token);
    }

    public static async Task<byte[]> ReadBlockAsync(
        Stream stream, int? length = null, CancellationToken token = default
    )
    {
        int actualLength = length ?? (int)await ReadUInt32Async(stream, token);
        return await ReadFixedAsync(stream, actualLength, token);
    }
}