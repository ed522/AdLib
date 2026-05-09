using System;
using System.IO;
using System.Text;

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
        byte[] bits = new byte[10]; // maximum
        int count = 0;

        // never falls through - should always break (otherwise the entire
        // number has continuation bits set, which would break it)
        for (int i = 0; i < bits.Length; i++)
        {
            count++;
            // add value
            bits[i] = (byte)(value & 0x7F);
            // set MSB if there's more to come
            value >>= 7;

            if (value != 0)
            {
                bits[i] |= 0x80;
            }
            else
            {
                break;
            }
        }

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
        int i = 0;
        byte b;

        do
        {
            int read = stream.ReadByte();
            if (read == -1) throw new EndOfStreamException("Insufficient data for VarInt.");
            b = (byte)read;

            checked
            {
                value |= (ulong)(b & 0x7F) << (7 * i);
            }

            i++;
        } while ((b & 0x80) != 0);

        return value;
    }

    public static uint ReadVarInt(Stream stream) => checked((uint)ReadVarLong(stream));

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

    public static void WriteBlock(Stream stream, byte[] block)
    {
        stream.Write(BitConverter.GetBytes(block.Length), 0, 4);
        stream.Write(block, 0, block.Length);
    }

    public static void WriteBlock(Stream stream, ReadOnlySpan<byte> block)
    {
        stream.Write(BitConverter.GetBytes(block.Length), 0, 4);
        stream.Write(block);
    }

    public static byte[] ReadBlock(Stream stream, int? length = null)
    {
        int actualLength = length ?? (int)BitConverter.ToUInt32(ReadFixed(stream, 4), 0);
        return ReadFixed(stream, actualLength);
    }

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
                    $"Insufficient data. Expected {length} bytes, got {totalRead}.");
            }

            totalRead += read;
        }

        return buffer;
    }

    public static void WriteUInt32(Stream stream, uint value) =>
        stream.Write(BitConverter.GetBytes(value), 0, 4);

    public static uint ReadUInt32(Stream stream) => BitConverter.ToUInt32(ReadFixed(stream, 4), 0);
}
