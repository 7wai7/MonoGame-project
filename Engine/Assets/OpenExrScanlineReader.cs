using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace MonoGame_project.Engine.Assets;

/// <summary>
/// Reads the uncompressed, single-part, 32-bit float scanline EXR variant used
/// by the terrain assets. Other EXR variants fail explicitly rather than being
/// interpreted as the wrong height or color data.
/// </summary>
public sealed class OpenExrScanlineReader : IDisposable
{
    private const uint Magic = 20_000_630;
    private readonly FileStream _stream;
    private readonly BinaryReader _reader;
    private readonly long[] _lineOffsets;
    private readonly byte[] _scanlineBytes;
    private readonly string[] _channels;

    public OpenExrScanlineReader(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!BitConverter.IsLittleEndian)
        {
            throw new PlatformNotSupportedException("EXR float data requires a little-endian CPU.");
        }

        _stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024);
        _reader = new BinaryReader(_stream, Encoding.ASCII);
        try
        {
            if (_reader.ReadUInt32() != Magic)
            {
                throw new InvalidDataException($"'{path}' is not an OpenEXR file.");
            }

            var version = _reader.ReadUInt32();
            if (version != 2)
            {
                throw new NotSupportedException(
                    $"'{path}' must be a single-part, non-tiled OpenEXR version 2 file.");
            }

            string[]? channels = null;
            var compression = -1;
            var minX = 0;
            var minY = 0;
            var maxX = -1;
            var maxY = -1;
            while (true)
            {
                var name = ReadCString(_reader);
                if (name.Length == 0)
                {
                    break;
                }

                var type = ReadCString(_reader);
                var size = _reader.ReadInt32();
                if (size < 0 || size > _stream.Length - _stream.Position)
                {
                    throw new InvalidDataException($"'{path}' has an invalid EXR attribute size.");
                }

                var end = _stream.Position + size;
                switch (name)
                {
                    case "channels" when type == "chlist":
                        channels = ReadChannels(_reader, end);
                        break;
                    case "compression" when type == "compression" && size == 1:
                        compression = _reader.ReadByte();
                        break;
                    case "dataWindow" when type == "box2i" && size == 16:
                        minX = _reader.ReadInt32();
                        minY = _reader.ReadInt32();
                        maxX = _reader.ReadInt32();
                        maxY = _reader.ReadInt32();
                        break;
                }

                _stream.Position = end;
            }

            if (compression != 0 || channels is null || channels.Length == 0)
            {
                throw new NotSupportedException(
                    $"'{path}' must use uncompressed 32-bit float EXR channels.");
            }

            var width = (long)maxX - minX + 1;
            var height = (long)maxY - minY + 1;
            if (width is < 1 or > 16_384 || height is < 1 or > 16_384)
            {
                throw new InvalidDataException($"'{path}' has unsupported EXR dimensions.");
            }

            Width = (int)width;
            Height = (int)height;
            _channels = channels;
            _scanlineBytes = new byte[checked(Width * channels.Length * sizeof(float))];
            _lineOffsets = new long[Height];
            for (var row = 0; row < Height; row++)
            {
                var offset = _reader.ReadUInt64();
                if (offset > long.MaxValue)
                {
                    throw new InvalidDataException($"'{path}' has an invalid EXR scanline offset.");
                }

                _lineOffsets[row] = (long)offset;
            }

            var firstDataOffset = _stream.Position;
            foreach (var offset in _lineOffsets)
            {
                if (offset < firstDataOffset || offset > _stream.Length - 8L - _scanlineBytes.Length)
                {
                    throw new InvalidDataException($"'{path}' has an invalid EXR scanline offset.");
                }
            }

            DataWindowMinY = minY;
        }
        catch
        {
            _reader.Dispose();
            throw;
        }
    }

    public int Width { get; }
    public int Height { get; }
    public IReadOnlyList<string> Channels => _channels;
    private int DataWindowMinY { get; }

    public int GetChannelIndex(string name)
    {
        for (var index = 0; index < _channels.Length; index++)
        {
            if (_channels[index] == name)
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>Returned data is valid until the next call to ReadScanline.</summary>
    public ReadOnlySpan<float> ReadScanline(int row)
    {
        if ((uint)row >= (uint)Height)
        {
            throw new ArgumentOutOfRangeException(nameof(row));
        }

        _stream.Position = _lineOffsets[row];
        var fileY = _reader.ReadInt32();
        var byteCount = _reader.ReadInt32();
        if (fileY != DataWindowMinY + row || byteCount != _scanlineBytes.Length)
        {
            throw new InvalidDataException("EXR scanline coordinates or size do not match its header.");
        }

        _stream.ReadExactly(_scanlineBytes);
        return MemoryMarshal.Cast<byte, float>(_scanlineBytes);
    }

    public void Dispose() => _reader.Dispose();

    private static string[] ReadChannels(BinaryReader reader, long end)
    {
        var channels = new List<string>();
        while (reader.BaseStream.Position < end)
        {
            var name = ReadCString(reader);
            if (name.Length == 0)
            {
                break;
            }

            var pixelType = reader.ReadInt32();
            reader.ReadBytes(4); // pLinear and reserved bytes
            var xSampling = reader.ReadInt32();
            var ySampling = reader.ReadInt32();
            if (pixelType != 2 || xSampling != 1 || ySampling != 1)
            {
                throw new NotSupportedException(
                    "Terrain EXR channels must be full-resolution 32-bit floats.");
            }

            channels.Add(name);
        }

        if (reader.BaseStream.Position != end)
        {
            throw new InvalidDataException("EXR channel list has an invalid length.");
        }

        return channels.ToArray();
    }

    private static string ReadCString(BinaryReader reader)
    {
        Span<byte> bytes = stackalloc byte[256];
        for (var length = 0; length < bytes.Length; length++)
        {
            var value = reader.ReadByte();
            if (value == 0)
            {
                return Encoding.ASCII.GetString(bytes[..length]);
            }

            bytes[length] = value;
        }

        throw new InvalidDataException("EXR contains an overlong name.");
    }
}
