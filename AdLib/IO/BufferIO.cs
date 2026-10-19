using System;
using System.Text;

namespace AdLib.IO;

public static class BufferIO
{
    /// <summary>
    ///     Converts a 64-bit unsigned integer to a little-endian array of bytes
    ///     with variable length. The encoding is possible and reversible for all
    ///     numbers.
    /// </summary>
    /// <param name="value">The value to encode.</param>
    /// <param name="buffer">The Span to place the encoded number into.</param>
    /// <returns>The resulting number's length in bytes.</returns>
    public static int WriteVarInt(ulong value, Span<byte> buffer)
    {
        if (buffer.Length < 10) throw new ArgumentException("Buffer too small", nameof(buffer));
        int count = 0;

        // never falls through - should always break (otherwise the entire
        // number has continuation bits set, which would break it)
        for (int i = 0; i < buffer.Length; i++)
        {
            // add value
            buffer[i] = (byte)(value & 0x7F);
            count++;

            // set MSB if there's more to come
            value >>= 7;

            // operates on updated value
            if (value != 0)
            {
                buffer[i] |= 0x80; // go back again
            }
            else
            {
                break;
            }
        }

        return count;
    }

    public static ulong ReadVarInt(ReadOnlySpan<byte> bits, out int length)
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

        length = i;
        return value;
    }

    public static uint ReadVarInt32(ReadOnlySpan<byte> span, out int length)
    {
        ulong value = ReadVarInt(span, out length);
        return checked((uint)value);
    }

    public static void WriteUInt32(Span<byte> destination, uint value, out int length)
    {
        destination[0] = (byte)((value >> 24) & 0xFF);
        destination[1] = (byte)((value >> 16) & 0xFF);
        destination[2] = (byte)((value >> 8) & 0xFF);
        destination[3] = (byte)(value & 0xFF);
        length = sizeof(uint);
    }

    public static uint ReadUInt32(ReadOnlySpan<byte> source, out int length)
    {
        length = sizeof(uint);
        return (uint)((source[0] << 24) | (source[1] << 16) | (source[2] << 8) | source[3]);
    }

    public static void WriteUInt64(Span<byte> destination, ulong value, out int length)
    {
        destination[0] = (byte)((value >> 56) & 0xFF);
        destination[1] = (byte)((value >> 48) & 0xFF);
        destination[2] = (byte)((value >> 40) & 0xFF);
        destination[3] = (byte)((value >> 32) & 0xFF);
        destination[4] = (byte)((value >> 24) & 0xFF);
        destination[5] = (byte)((value >> 16) & 0xFF);
        destination[6] = (byte)((value >> 8) & 0xFF);
        destination[7] = (byte)(value & 0xFF);
        length = sizeof(uint);
    }

    public static ulong ReadUInt64(ReadOnlySpan<byte> source, out int length)
    {
        length = sizeof(ulong);

        return ((ulong)source[0] << 56) | ((ulong)source[1] << 48) | ((ulong)source[2] << 40) |
               ((ulong)source[3] << 32) |
               ((ulong)source[4] << 24) | ((ulong)source[5] << 16) | ((ulong)source[6] << 8) | source[7];
    }

    public static void WriteString(Span<byte> destination, string value, out int length)
    {
        int stringLength = Encoding.UTF8.GetByteCount(value);
        int varIntLength = WriteVarInt((ulong)stringLength, destination);
        length = varIntLength + Encoding.UTF8.GetBytes(value, destination[varIntLength..]);
    }

    public static int GetStringLength(string value)
    {
        // writing to stackalloc is essentially as fast as an estimate based on length
        int stringLength = Encoding.UTF8.GetByteCount(value);
        return stringLength + WriteVarInt((ulong)stringLength, stackalloc byte[10]);
    }

    public static string ReadString(ReadOnlySpan<byte> source, out int length)
    {
        uint stringLength = ReadVarInt32(source, out int varIntLength);
        length = varIntLength + (int)stringLength;
        return Encoding.UTF8.GetString(source[varIntLength..length]);
    }

    public static void WriteBlock(Span<byte> destination, ReadOnlySpan<byte> block, out int length)
    {
        WriteUInt32(destination, (uint)block.Length, out length);
        block.CopyTo(destination[length..(length + block.Length)]);
        length += block.Length;
    }

    public static int GetBlockLength(ReadOnlySpan<byte> block) => sizeof(uint) + block.Length;

    public static Span<byte> ReadBlock(ReadOnlySpan<byte> source, out int length)
    {
        int blockLength = (int)ReadUInt32(source, out length);

        byte[] data = new byte[blockLength];
        source[length..(length + blockLength)].CopyTo(data);
        length += blockLength;
        return data;
    }

    public static void WriteSmallBlock(Span<byte> destination, ReadOnlySpan<byte> block, out int length)
    {
        if (block.Length > byte.MaxValue)
        {
            throw new InvalidOperationException("Message is too long");
        }

        destination[0] = (byte)block.Length;
        length = sizeof(byte) + block.Length;
        block.CopyTo(destination[sizeof(byte)..length]);
    }

    public static Span<byte> ReadSmallBlock(ReadOnlySpan<byte> source, out int length)
    {
        byte blockLength = source[0];
        byte[] data = new byte[blockLength];

        length = blockLength + sizeof(byte);
        source[sizeof(byte)..length].CopyTo(data);
        return data;
    }
}