using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace LibLunacy.Experimental.Core.IO;

/// <summary>
/// Endianness for binary reading
/// </summary>
public enum Endianness
{
    Little,
    Big
}

/// <summary>
/// Modern binary reader with endianness support
/// </summary>
public sealed class BinaryDataReader : IDisposable
{
    private readonly Stream _stream;
    private readonly Endianness _endianness;
    private readonly bool _leaveOpen;

    public Stream BaseStream => _stream;
    public Endianness Endianness => _endianness;
    public long Position
    {
        get => _stream.Position;
        set => _stream.Position = value;
    }
    public long Length => _stream.Length;

    public BinaryDataReader(Stream stream, Endianness endianness = Endianness.Big, bool leaveOpen = false)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _endianness = endianness;
        _leaveOpen = leaveOpen;
    }

    public void Seek(long offset, SeekOrigin origin = SeekOrigin.Begin)
    {
        _stream.Seek(offset, origin);
    }

    // Primitive reads
    public byte ReadByte() => (byte)_stream.ReadByte();

    public byte[] ReadBytes(int count)
    {
        var buffer = new byte[count];
        _stream.Read(buffer, 0, count);
        return buffer;
    }

    public short ReadInt16()
    {
        Span<byte> buffer = stackalloc byte[2];
        _stream.Read(buffer);
        return _endianness == Endianness.Big
            ? BinaryPrimitives.ReadInt16BigEndian(buffer)
            : BinaryPrimitives.ReadInt16LittleEndian(buffer);
    }

    public ushort ReadUInt16()
    {
        Span<byte> buffer = stackalloc byte[2];
        _stream.Read(buffer);
        return _endianness == Endianness.Big
            ? BinaryPrimitives.ReadUInt16BigEndian(buffer)
            : BinaryPrimitives.ReadUInt16LittleEndian(buffer);
    }

    public int ReadInt32()
    {
        Span<byte> buffer = stackalloc byte[4];
        _stream.Read(buffer);
        return _endianness == Endianness.Big
            ? BinaryPrimitives.ReadInt32BigEndian(buffer)
            : BinaryPrimitives.ReadInt32LittleEndian(buffer);
    }

    public uint ReadUInt32()
    {
        Span<byte> buffer = stackalloc byte[4];
        _stream.Read(buffer);
        return _endianness == Endianness.Big
            ? BinaryPrimitives.ReadUInt32BigEndian(buffer)
            : BinaryPrimitives.ReadUInt32LittleEndian(buffer);
    }

    public long ReadInt64()
    {
        Span<byte> buffer = stackalloc byte[8];
        _stream.Read(buffer);
        return _endianness == Endianness.Big
            ? BinaryPrimitives.ReadInt64BigEndian(buffer)
            : BinaryPrimitives.ReadInt64LittleEndian(buffer);
    }

    public ulong ReadUInt64()
    {
        Span<byte> buffer = stackalloc byte[8];
        _stream.Read(buffer);
        return _endianness == Endianness.Big
            ? BinaryPrimitives.ReadUInt64BigEndian(buffer)
            : BinaryPrimitives.ReadUInt64LittleEndian(buffer);
    }

    public float ReadSingle()
    {
        Span<byte> buffer = stackalloc byte[4];
        _stream.Read(buffer);
        return _endianness == Endianness.Big
            ? BinaryPrimitives.ReadSingleBigEndian(buffer)
            : BinaryPrimitives.ReadSingleLittleEndian(buffer);
    }

    public Half ReadHalf()
    {
        Span<byte> buffer = stackalloc byte[2];
        _stream.Read(buffer);
        return _endianness == Endianness.Big
            ? BinaryPrimitives.ReadHalfBigEndian(buffer)
            : BinaryPrimitives.ReadHalfLittleEndian(buffer);
    }

    public string ReadString()
    {
        var bytes = new List<byte>();
        int b;
        while ((b = _stream.ReadByte()) != 0 && b != -1)
        {
            bytes.Add((byte)b);
        }
        return Encoding.UTF8.GetString(bytes.ToArray());
    }

    public string ReadStringAt(long offset)
    {
        var oldPos = Position;
        Seek(offset);
        var result = ReadString();
        Position = oldPos;
        return result;
    }

    // Array reads
    public T[] ReadArray<T>(int count, Func<BinaryDataReader, T> readFunc)
    {
        var array = new T[count];
        for (int i = 0; i < count; i++)
        {
            array[i] = readFunc(this);
        }
        return array;
    }

    // Struct reads using marshaling
    public T ReadStruct<T>() where T : struct
    {
        int size = Marshal.SizeOf<T>();
        var buffer = ReadBytes(size);

        if (_endianness == Endianness.Big && BitConverter.IsLittleEndian)
        {
            // Need to reverse for big endian on little endian system
            ConvertEndianness(buffer, typeof(T));
        }

        GCHandle handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            return Marshal.PtrToStructure<T>(handle.AddrOfPinnedObject());
        }
        finally
        {
            handle.Free();
        }
    }

    private static void ConvertEndianness(byte[] buffer, Type type)
    {
        // Simple field-by-field endianness conversion
        // This is a simplified version - production code would need more sophistication
        var fields = type.GetFields();
        int offset = 0;
        foreach (var field in fields)
        {
            var fieldType = field.FieldType;
            int size = GetFieldSize(fieldType);

            if (size > 1)
            {
                Array.Reverse(buffer, offset, size);
            }
            offset += size;
        }
    }

    private static int GetFieldSize(Type type)
    {
        if (type == typeof(byte) || type == typeof(sbyte)) return 1;
        if (type == typeof(short) || type == typeof(ushort) || type == typeof(Half)) return 2;
        if (type == typeof(int) || type == typeof(uint) || type == typeof(float)) return 4;
        if (type == typeof(long) || type == typeof(ulong) || type == typeof(double)) return 8;
        return Marshal.SizeOf(type);
    }

    public void Dispose()
    {
        if (!_leaveOpen)
        {
            _stream?.Dispose();
        }
    }
}
