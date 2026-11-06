using System.Buffers.Binary;

namespace ReLunacy.Engine.Scripting;

/// <summary>
/// Writes a <see cref="LuaChunk"/> back to precompiled form, mirroring <see cref="LuaChunkReader"/>
/// field for field.
///
/// Two jobs: it is how the reader is proven (read a shipped chunk, write it, require the bytes to be
/// identical), and it is the output stage the compiler emits through, so the bytecode the editor
/// produces is written by exactly the code already validated against all 524 shipped chunks.
/// </summary>
public static class LuaChunkWriter
{
    public static byte[] Write(LuaChunk chunk)
    {
        var buffer = new MemoryStream();
        WriteHeader(buffer, chunk.Header);
        WritePrototype(buffer, chunk.Main, chunk.Header);
        return buffer.ToArray();
    }

    private static void WriteHeader(Stream s, LuaChunkHeader h)
    {
        s.Write([0x1B, (byte)'L', (byte)'u', (byte)'a']);
        s.WriteByte(h.Version);
        s.WriteByte((byte)(h.LittleEndian ? 1 : 0));
        s.WriteByte(h.SizeOfInt);
        s.WriteByte(h.SizeOfSizeT);
        s.WriteByte(h.SizeOfInstruction);
        s.WriteByte(h.OpBits);
        s.WriteByte(h.ABits);
        s.WriteByte(h.BBits);
        s.WriteByte(h.CBits);
        s.WriteByte(h.SizeOfNumber);
        // Written back verbatim: re-deriving PI*1e8 could round differently from the build that
        // produced the chunk, and the loader compares these bytes exactly.
        s.Write(h.NumberTestBytes);
    }

    private static void WritePrototype(Stream s, LuaPrototype p, LuaChunkHeader h)
    {
        WriteString(s, p.Source, h);
        WriteInt(s, p.LineDefined, h);
        s.WriteByte(p.UpvalueCount);
        s.WriteByte(p.ParameterCount);
        s.WriteByte(p.IsVararg);
        s.WriteByte(p.MaxStackSize);

        WriteInt(s, p.LineInfo.Length, h);
        foreach (int line in p.LineInfo) WriteInt(s, line, h);

        WriteInt(s, p.Locals.Length, h);
        foreach (var local in p.Locals)
        {
            WriteString(s, local.Name, h);
            WriteInt(s, local.StartPc, h);
            WriteInt(s, local.EndPc, h);
        }

        WriteInt(s, p.Upvalues.Length, h);
        foreach (var name in p.Upvalues) WriteString(s, name, h);

        WriteInt(s, p.Constants.Length, h);
        foreach (var k in p.Constants)
        {
            switch (k)
            {
                case LuaConstant.Nil:
                    s.WriteByte(LuaConstant.TagNil);
                    break;
                case LuaConstant.Number number:
                    s.WriteByte(LuaConstant.TagNumber);
                    WriteNumber(s, number.Value, h);
                    break;
                case LuaConstant.Str str:
                    s.WriteByte(LuaConstant.TagString);
                    WriteString(s, str.Bytes, h);
                    break;
                default:
                    throw new ArgumentException($"cannot write constant of type {k.GetType().Name}.", nameof(p));
            }
        }

        WriteInt(s, p.Prototypes.Length, h);
        foreach (var nested in p.Prototypes) WritePrototype(s, nested, h);

        WriteInt(s, p.Code.Length, h);
        foreach (uint instruction in p.Code) WriteUInt32(s, instruction, h);
    }

    private static void WriteInt(Stream s, int value, LuaChunkHeader h)
    {
        if (h.SizeOfInt == 4) WriteUInt32(s, unchecked((uint)value), h);
        else WriteUInt64(s, unchecked((ulong)(long)value), h);
    }

    private static void WriteSizeT(Stream s, long value, LuaChunkHeader h)
    {
        if (h.SizeOfSizeT == 4) WriteUInt32(s, checked((uint)value), h);
        else WriteUInt64(s, unchecked((ulong)value), h);
    }

    private static void WriteUInt32(Stream s, uint value, LuaChunkHeader h)
    {
        Span<byte> b = stackalloc byte[4];
        if (h.LittleEndian) BinaryPrimitives.WriteUInt32LittleEndian(b, value);
        else BinaryPrimitives.WriteUInt32BigEndian(b, value);
        s.Write(b);
    }

    private static void WriteUInt64(Stream s, ulong value, LuaChunkHeader h)
    {
        Span<byte> b = stackalloc byte[8];
        if (h.LittleEndian) BinaryPrimitives.WriteUInt64LittleEndian(b, value);
        else BinaryPrimitives.WriteUInt64BigEndian(b, value);
        s.Write(b);
    }

    private static void WriteNumber(Stream s, double value, LuaChunkHeader h)
    {
        if (h.SizeOfNumber == 4) WriteUInt32(s, BitConverter.SingleToUInt32Bits((float)value), h);
        else WriteUInt64(s, BitConverter.DoubleToUInt64Bits(value), h);
    }

    /// <summary>Length includes the terminating NUL the reader strips; null writes a zero length.</summary>
    private static void WriteString(Stream s, byte[]? bytes, LuaChunkHeader h)
    {
        if (bytes == null)
        {
            WriteSizeT(s, 0, h);
            return;
        }
        WriteSizeT(s, bytes.Length + 1, h);
        s.Write(bytes);
        s.WriteByte(0);
    }
}
