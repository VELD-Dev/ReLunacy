﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace LibLunacy;

public class LunaStream : Stream
{
    public enum Endianness : byte
    {
        Little,
        Big
    }

    public override bool CanRead => true;

    public override bool CanSeek => true;

    public override bool CanWrite => true;

    public override long Length => ReadStream.Length;
    public long WriteLength => WriteStream.Length;

    public override long Position { get => ReadStream.Position; set => ReadStream.Position = value; } 
    public long WritePosition { get => WriteStream.Position; set => WriteStream.Position = value; }

    public Endianness endianness = Endianness.Little;

    public readonly Stream ReadStream;
    public readonly Stream WriteStream;

    public LunaStream(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Impossible to open a stream of the file.", filePath);

        ReadStream = File.OpenRead(filePath);
        WriteStream = File.OpenWrite(filePath);
    }

    public LunaStream(Stream read, Stream write)
    {
        ReadStream = read;
        WriteStream = write;
    }

    public override void Flush()
    {
        WriteStream.Flush();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        return ReadStream.Read(buffer, offset, count);
    }

    public override long Seek(long offset, SeekOrigin origin = SeekOrigin.Begin)
    {
        return ReadStream.Seek(offset, origin);
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        WriteStream.Write(buffer, offset, count);
    }

    public override void Close()
    {
        ReadStream.Close();
        WriteStream.Flush();
        WriteStream.Close();
        base.Close();
    }

    public byte[] Peek(int offset, int count, bool relative = true)
    {
        var baseOffset = Position;
        if (relative) ReadStream.Seek(offset, SeekOrigin.Current);
        else ReadStream.Seek(offset, SeekOrigin.Begin);

        var buffer = new byte[count];
        ReadStream.Read(buffer, 0, count);
        ReadStream.Seek(offset, SeekOrigin.Begin);
        if (endianness == Endianness.Big)
            buffer = buffer.Reverse().ToArray();
        return buffer;
    }

    public ushort ReadUInt16(int offset, bool relative = true)
    {
        var bytes = Peek(offset, 0x02, relative);
        return BitConverter.ToUInt16(bytes);
    }

    public short ReadInt16(int offset , bool relative = true)
    {
        var bytes = Peek(offset, 0x02, relative);
        return BitConverter.ToInt16(bytes);
    }

    public uint ReadUInt32(int offset, bool relative = true)
    {
        var bytes = Peek(offset, 0x04, relative);
        return BitConverter.ToUInt32(bytes);
    }

    public int ReadInt32(int offset, bool relative = true)
    {
        var bytes = Peek(offset, 0x04, relative);
        return BitConverter.ToInt32(bytes);
    }

    public ulong ReadUInt64(int offset, bool relative = true)
    {
        var bytes = Peek(offset, 0x08, relative);
        return BitConverter.ToUInt64(bytes);
    }

    public long ReadInt64(int offset, bool relative = true)
    {
        var bytes = Peek(offset, 0x08, relative);
        return BitConverter.ToInt64(bytes);
    }

    public Half ReadHalf(int offset, bool relative = true)
    {
        var bytes = Peek(offset, 0x02, relative);
        return BitConverter.ToHalf(bytes);
    }

    public float ReadSingle(int offset, bool relative = true)
    {
        var bytes = Peek(offset, 0x04, relative);
        return BitConverter.ToSingle(bytes);
    }

    public double ReadDouble(int offset, bool relative = true)
    {
        var bytes = Peek(offset, 0x08, relative);
        return BitConverter.ToDouble(bytes);
    }

    public Vector3 ReadVector3(int offset, bool relative = true)
    {
        return new Vector3(ReadSingle(offset, relative), ReadSingle(offset + 0x04, relative), ReadSingle(offset + 0x08, relative));
    }

    public Vector4 ReadVector4(int offset, bool relative = true)
    {
        return new Vector4(ReadSingle(offset, relative), ReadSingle(offset + 0x04, relative), ReadSingle(offset + 0x08, relative), ReadSingle(offset + 0x0C, relative));
    }

    public Matrix4x4 ReadMatrix4x4(int offset, bool relative = true)
    {
        var r1 = ReadVector4(0x00, relative);
        var r2 = ReadVector4(0x10, relative);
        var r3 = ReadVector4(0x20, relative);
        var r4 = ReadVector4(0x30, relative);
        return new Matrix4x4(
            r1.X, r1.Y, r1.Z, r1.W,
            r2.X, r2.Y, r2.Z, r2.W,
            r3.X, r3.Y, r3.Z, r3.W,
            r4.X, r4.Y, r4.Z, r4.W
        );
    }

    public string ReadString(int offset, bool relative = true, uint length = 0)
    {
        int index = 0;
        string s = "";
        while(Peek(offset + index + 1, 1)[0] != 0x00)
        {
            index++;
            var b = Peek(offset + index, 1);
            s += BitConverter.ToString(b);
            if (index == length) break;
        }
        return s;
    }

    public void JumpRead(int offset)
    {
        ReadStream.Seek(offset, SeekOrigin.Current);

    }

    public override void SetLength(long value)
    {
        WriteStream.SetLength(value);
    }
}
