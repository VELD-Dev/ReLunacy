using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;

namespace ReLunacy.Engine.Loading.IO;

// Endianness-aware BinaryReader. Per-file endianness is auto-detected from the IGHW magic
// (see IGFile), not fixed — the game ships both big- and little-endian containers.
public class StreamHelper : BinaryReader
{
    public enum Endianness
    {
        Little,
        Big,
    }

    public Endianness _endianness = Endianness.Little;
    public byte bitPosition = 0;

    public long Offset => BaseStream.Position;

    public StreamHelper(Stream input) : base(input) { }
    public StreamHelper(Stream input, Encoding encoding) : base(input, encoding) { }
    public StreamHelper(Stream input, Encoding encoding, bool leaveOpen) : base(input, encoding, leaveOpen) { }
    public StreamHelper(Stream input, Endianness endianness) : base(input) => _endianness = endianness;
    public StreamHelper(Stream input, Encoding encoding, Endianness endianness) : base(input, encoding) => _endianness = endianness;
    public StreamHelper(Stream input, Encoding encoding, bool leaveOpen, Endianness endianness) : base(input, encoding, leaveOpen) => _endianness = endianness;

    public void Seek(long offset, SeekOrigin origin = SeekOrigin.Begin) => BaseStream.Seek(offset, origin);

    public override string ReadString()
    {
        var sb = new StringBuilder();
        while (true)
        {
            var newByte = ReadByte();
            if (newByte == 0) break;
            sb.Append((char)newByte);
        }
        return sb.ToString();
    }

    public string ReadUnicodeString()
    {
        var sb = new StringBuilder();
        while (true)
        {
            byte newByte, newByte2;
            try
            {
                newByte = ReadByte();
                newByte2 = ReadByte();
            }
            catch (EndOfStreamException)
            {
                break;
            }
            if (newByte == 0 && newByte2 == 0) break;
            sb.Append(_endianness == Endianness.Big
                ? Encoding.Unicode.GetString([newByte2, newByte])
                : Encoding.Unicode.GetString([newByte, newByte2]));
        }
        return sb.ToString();
    }

    public string ReadUnicodeString(uint size)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < size; i++)
        {
            byte newByte, newByte2;
            try
            {
                newByte = ReadByte();
                newByte2 = ReadByte();
            }
            catch (EndOfStreamException)
            {
                break;
            }
            if (newByte == 0 && newByte2 == 0) break;
            sb.Append(_endianness == Endianness.Big
                ? Encoding.Unicode.GetString([newByte2, newByte])
                : Encoding.Unicode.GetString([newByte, newByte2]));
        }
        return sb.ToString();
    }

    public new byte ReadByte() => ReadByte((uint)BaseStream.Position);
    public byte ReadByte(uint offset)
    {
        BaseStream.Seek(offset, SeekOrigin.Begin);
        byte[] buffer = new byte[1];
        BaseStream.Read(buffer, 0x00, 0x01);
        return buffer[0];
    }

    public string ReadString(uint offset)
    {
        BaseStream.Seek(offset, SeekOrigin.Begin);
        return ReadString();
    }

    public byte[] ReadBytes(uint count) => ReadBytes((int)count);
    public byte[] ReadFromOffset(int count, uint offset)
    {
        BaseStream.Seek(offset, SeekOrigin.Begin);
        return ReadBytes(count);
    }

    public object ReadStruct(Type t) =>
        GetType().GetMethod(nameof(ReadStruct), [])!.MakeGenericMethod(t).Invoke(this, null)!;

    public unsafe T ReadStruct<T>() where T : struct
    {
        byte[] data = ReadBytes(Marshal.SizeOf(typeof(T)));

        if (_endianness == Endianness.Big && BitConverter.IsLittleEndian || _endianness == Endianness.Little && !BitConverter.IsLittleEndian)
        {
            foreach (var field in typeof(T).GetFields())
            {
                var fieldType = field.FieldType;
                if (field.IsStatic) continue;
                if (fieldType == typeof(string)) continue;

                var offset = Marshal.OffsetOf(typeof(T), field.Name).ToInt32();

                if (fieldType.IsEnum) fieldType = Enum.GetUnderlyingType(fieldType);

                var subFields = fieldType.GetFields().Where(subField => !subField.IsStatic).ToArray();

                if (subFields.Length == 0)
                {
                    Array.Reverse(data, offset, Marshal.SizeOf(fieldType));
                }
            }
            if (typeof(T).IsPrimitive)
            {
                Array.Reverse(data, 0, data.Length);
            }
        }

        GCHandle handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        T read = (T)Marshal.PtrToStructure(handle.AddrOfPinnedObject(), typeof(T))!;
        handle.Free();

        return read;
    }

    public object ReadStructArray(Type t, uint count) =>
        GetType().GetMethod(nameof(ReadStructArray), [typeof(uint)])!.MakeGenericMethod(t).Invoke(this, [count])!;

    public T[] ReadStructArray<T>(uint count) where T : struct
    {
        T[] items = new T[count];
        for (uint i = 0; i < count; i++)
        {
            items[i] = ReadStruct<T>();
        }
        return items;
    }

    public override float ReadSingle() => ReadSingle(_endianness);
    public float ReadSingle(Endianness endianness) => BitConverter.ToSingle(ReadForEndianness(sizeof(float), endianness), 0);
    public override Half ReadHalf() => ReadHalf(_endianness);
    public Half ReadHalf(Endianness endianness) => BitConverter.ToHalf(ReadForEndianness(2, endianness), 0);

    public override short ReadInt16() => ReadInt16(_endianness);
    public short ReadInt16(uint offset) => ReadInt16(offset, _endianness);
    public short ReadInt16(uint offset, Endianness endianness)
    {
        BaseStream.Seek(offset, SeekOrigin.Begin);
        return ReadInt16(endianness);
    }

    public override int ReadInt32() => ReadInt32(_endianness);
    public int ReadInt32(uint offset) => ReadInt32(offset, _endianness);
    public int ReadInt32(uint offset, Endianness endianness)
    {
        BaseStream.Seek(offset, SeekOrigin.Begin);
        return ReadInt32(endianness);
    }

    public override ushort ReadUInt16() => ReadUInt16(_endianness);
    public ushort ReadUInt16(uint offset) => ReadUInt16(offset, _endianness);
    public ushort ReadUInt16(uint offset, Endianness endianness)
    {
        BaseStream.Seek(offset, SeekOrigin.Begin);
        return ReadUInt16(endianness);
    }

    public override uint ReadUInt32() => ReadUInt32(_endianness);
    public uint ReadUInt32(uint offset) => ReadUInt32(offset, _endianness);
    public uint ReadUInt32(uint offset, Endianness endianness)
    {
        BaseStream.Seek(offset, SeekOrigin.Begin);
        return ReadUInt32(endianness);
    }

    public ulong ReadUIntN(byte n) => ReadUIntN((uint)BaseStream.Position, bitPosition, n);
    public ulong ReadUIntN(uint offset, byte n) => ReadUIntN(offset, bitPosition, n);
    public ulong ReadUIntN(uint offset, byte bitOffset, byte n)
    {
        if (n > 64) throw new ArgumentException("The number of bits to read cannot be greater than 64.");
        if (bitOffset > 7) throw new ArgumentException("The bit offset cannot be equal to or greater than 8.");

        ulong ret = 0;
        for (int i = 0; i < n; i++)
        {
            bool bit = ReadBit();
            ret |= bit ? 1u : 0u;
            ret <<= 1;
        }
        ret >>= 1;
        return ret;
    }
    public long ReadIntN(byte n) => ReadIntN((uint)BaseStream.Position, bitPosition, n);
    public long ReadIntN(uint offset, byte n) => ReadIntN(offset, bitPosition, n);
    public long ReadIntN(uint offset, byte bitOffset, byte n)
    {
        ulong unsignedn = ReadUIntN(offset, bitOffset, n);
        long signedn = (long)(unsignedn & ~(1u << n));
        signedn *= (unsignedn & 1u << n) != 0 ? -1 : 1;
        return signedn;
    }
    public bool ReadBit() => ReadBit((uint)BaseStream.Position, bitPosition);
    public bool ReadBit(uint offset, byte bitOffset)
    {
        Seek(offset);
        bitPosition = bitOffset;
        if (bitPosition > 0) BaseStream.Position--;
        byte currentByte = ReadByte();
        int bit = currentByte >> 7 - bitPosition & 1;
        bitPosition++;
        bitPosition %= 8;
        return bit == 1;
    }
    public override ulong ReadUInt64() => ReadUInt64(_endianness);

    public short ReadInt16(Endianness endianness) => BitConverter.ToInt16(ReadForEndianness(sizeof(short), endianness), 0);
    public int ReadInt32(Endianness endianness) => BitConverter.ToInt32(ReadForEndianness(sizeof(int), endianness), 0);
    public override long ReadInt64() => ReadInt64(_endianness);
    public long ReadInt64(Endianness endianness) => BitConverter.ToInt64(ReadForEndianness(sizeof(long), endianness), 0);
    public ushort ReadUInt16(Endianness endianness) => BitConverter.ToUInt16(ReadForEndianness(sizeof(ushort), endianness), 0);
    public uint ReadUInt32(Endianness endianness) => BitConverter.ToUInt32(ReadForEndianness(sizeof(uint), endianness), 0);
    public ulong ReadUInt64(Endianness endianness) => BitConverter.ToUInt64(ReadForEndianness(sizeof(ulong), endianness), 0);

    public byte[] ReadForEndianness(int bytesToRead, Endianness endianness)
    {
        byte[] bytesRead = new byte[bytesToRead];
        int res = BaseStream.Read(bytesRead, 0, bytesToRead);
        if (res != bytesToRead)
        {
            throw new Exception($"Read {res} instead of {bytesToRead}");
        }
        switch (endianness)
        {
            case Endianness.Little:
                if (!BitConverter.IsLittleEndian) Array.Reverse(bytesRead);
                break;
            case Endianness.Big:
                if (BitConverter.IsLittleEndian) Array.Reverse(bytesRead);
                break;
        }
        return bytesRead;
    }

    // Write methods (endianness-aware) — used by the round-trip/rebuild path (AssetBuilder).

    public void WriteUInt16(ushort value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(ushort)];
        if (_endianness == Endianness.Big) BinaryPrimitives.WriteUInt16BigEndian(buffer, value);
        else BinaryPrimitives.WriteUInt16LittleEndian(buffer, value);
        BaseStream.Write(buffer);
    }

    public void WriteInt16(short value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(short)];
        if (_endianness == Endianness.Big) BinaryPrimitives.WriteInt16BigEndian(buffer, value);
        else BinaryPrimitives.WriteInt16LittleEndian(buffer, value);
        BaseStream.Write(buffer);
    }

    public void WriteUInt32(uint value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(uint)];
        if (_endianness == Endianness.Big) BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
        else BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
        BaseStream.Write(buffer);
    }

    public void WriteInt32(int value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(int)];
        if (_endianness == Endianness.Big) BinaryPrimitives.WriteInt32BigEndian(buffer, value);
        else BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
        BaseStream.Write(buffer);
    }

    public void WriteUInt64(ulong value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(ulong)];
        if (_endianness == Endianness.Big) BinaryPrimitives.WriteUInt64BigEndian(buffer, value);
        else BinaryPrimitives.WriteUInt64LittleEndian(buffer, value);
        BaseStream.Write(buffer);
    }

    public void WriteInt64(long value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(long)];
        if (_endianness == Endianness.Big) BinaryPrimitives.WriteInt64BigEndian(buffer, value);
        else BinaryPrimitives.WriteInt64LittleEndian(buffer, value);
        BaseStream.Write(buffer);
    }

    public void WriteHalf(Half value)
    {
        Span<byte> buffer = stackalloc byte[2];
        if (_endianness == Endianness.Big) BinaryPrimitives.WriteHalfBigEndian(buffer, value);
        else BinaryPrimitives.WriteHalfLittleEndian(buffer, value);
        BaseStream.Write(buffer);
    }

    public void WriteSingle(float value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(float)];
        if (_endianness == Endianness.Big) BinaryPrimitives.WriteSingleBigEndian(buffer, value);
        else BinaryPrimitives.WriteSingleLittleEndian(buffer, value);
        BaseStream.Write(buffer);
    }

    public void WriteDouble(double value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(double)];
        if (_endianness == Endianness.Big) BinaryPrimitives.WriteDoubleBigEndian(buffer, value);
        else BinaryPrimitives.WriteDoubleLittleEndian(buffer, value);
        BaseStream.Write(buffer);
    }

    public void WriteVector3(Vector3 value)
    {
        WriteSingle(value.X);
        WriteSingle(value.Y);
        WriteSingle(value.Z);
    }

    public void WriteVector4(Vector4 value)
    {
        WriteSingle(value.X);
        WriteSingle(value.Y);
        WriteSingle(value.Z);
        WriteSingle(value.W);
    }

    public void WriteMatrix4x4(Matrix4x4 value)
    {
        WriteVector4(new Vector4(value.M11, value.M12, value.M13, value.M14));
        WriteVector4(new Vector4(value.M21, value.M22, value.M23, value.M24));
        WriteVector4(new Vector4(value.M31, value.M32, value.M33, value.M34));
        WriteVector4(new Vector4(value.M41, value.M42, value.M43, value.M44));
    }

    public void WriteString(string value, bool nullTerminated = true)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        BaseStream.Write(bytes, 0, bytes.Length);
        if (nullTerminated) BaseStream.WriteByte(0);
    }

    public void WriteBytes(byte[] value) => BaseStream.Write(value, 0, value.Length);
    public void WriteBytes(ReadOnlySpan<byte> value) => BaseStream.Write(value);
}
