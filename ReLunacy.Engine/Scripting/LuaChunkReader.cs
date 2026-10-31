using System.Buffers.Binary;

namespace ReLunacy.Engine.Scripting;

/// <summary>Thrown when a chunk is not a precompiled Lua chunk this reader understands.</summary>
public sealed class LuaChunkFormatException(string message) : Exception(message);

/// <summary>
/// Reads a precompiled Lua chunk. The field order is Lua 5.0's <c>lundump.c</c>: source, lineDefined,
/// nups, numparams, is_vararg, maxstacksize, then lineinfo, locals, upvalues, constants (values then
/// nested prototypes), then code.
///
/// Sizes and endianness come from the chunk's own header rather than being assumed, so a chunk built
/// with different settings is either read correctly or rejected with a clear message - not misparsed.
/// </summary>
public static class LuaChunkReader
{
    private static readonly byte[] Signature = [0x1B, (byte)'L', (byte)'u', (byte)'a'];

    public static LuaChunk Read(byte[] data)
    {
        var cursor = new Cursor(data);
        LuaChunkHeader header = ReadHeader(ref cursor);
        LuaPrototype main = ReadPrototype(ref cursor, header);
        if (cursor.Offset != data.Length)
            throw new LuaChunkFormatException(
                $"chunk has {data.Length - cursor.Offset} trailing byte(s) after the top-level function.");
        return new LuaChunk { Header = header, Main = main };
    }

    public static bool IsLuaChunk(ReadOnlySpan<byte> data) =>
        data.Length >= 4 && data[..4].SequenceEqual(Signature);

    private static LuaChunkHeader ReadHeader(ref Cursor c)
    {
        if (!IsLuaChunk(c.Take(4)))
            throw new LuaChunkFormatException("missing the \\x1bLua signature; not a precompiled chunk.");

        byte version = c.Byte();
        if (version != LuaChunkHeader.Lua50)
            throw new LuaChunkFormatException(
                $"Lua version 0x{version:X2}; this reader implements 0x{LuaChunkHeader.Lua50:X2} (5.0, the b4404 variant).");

        // Lua 5.0 writes the endianness flag before the sizes, and every size as a single byte.
        bool little = c.Byte() != 0;
        byte sizeInt = c.Byte(), sizeSizeT = c.Byte(), sizeInstr = c.Byte();
        byte op = c.Byte(), a = c.Byte(), b = c.Byte(), cc = c.Byte();
        byte sizeNumber = c.Byte();

        if (sizeInt is not (4 or 8)) throw new LuaChunkFormatException($"unsupported sizeof(int) {sizeInt}.");
        if (sizeSizeT is not (4 or 8)) throw new LuaChunkFormatException($"unsupported sizeof(size_t) {sizeSizeT}.");
        if (sizeInstr != 4) throw new LuaChunkFormatException($"unsupported sizeof(Instruction) {sizeInstr}.");
        if (sizeNumber is not (4 or 8)) throw new LuaChunkFormatException($"unsupported sizeof(lua_Number) {sizeNumber}.");

        byte[] testBytes = c.Take(sizeNumber).ToArray();
        return new LuaChunkHeader
        {
            Version = version,
            LittleEndian = little,
            SizeOfInt = sizeInt,
            SizeOfSizeT = sizeSizeT,
            SizeOfInstruction = sizeInstr,
            OpBits = op,
            ABits = a,
            BBits = b,
            CBits = cc,
            SizeOfNumber = sizeNumber,
            NumberTestBytes = testBytes,
        };
    }

    private static LuaPrototype ReadPrototype(ref Cursor c, LuaChunkHeader h)
    {
        var p = new LuaPrototype
        {
            Source = ReadString(ref c, h),
            LineDefined = ReadInt(ref c, h),
            UpvalueCount = c.Byte(),
            ParameterCount = c.Byte(),
            IsVararg = c.Byte(),
            MaxStackSize = c.Byte(),
        };

        int lines = ReadInt(ref c, h);
        var lineInfo = new int[lines];
        for (int i = 0; i < lines; i++) lineInfo[i] = ReadInt(ref c, h);
        p.LineInfo = lineInfo;

        int localCount = ReadInt(ref c, h);
        var locals = new LuaLocal[localCount];
        for (int i = 0; i < localCount; i++)
            locals[i] = new LuaLocal(ReadString(ref c, h), ReadInt(ref c, h), ReadInt(ref c, h));
        p.Locals = locals;

        int upvalues = ReadInt(ref c, h);
        var ups = new byte[]?[upvalues];
        for (int i = 0; i < upvalues; i++) ups[i] = ReadString(ref c, h);
        p.Upvalues = ups;

        int constants = ReadInt(ref c, h);
        var k = new LuaConstant[constants];
        for (int i = 0; i < constants; i++)
        {
            byte tag = c.Byte();
            k[i] = tag switch
            {
                LuaConstant.TagNil => LuaConstant.Nil.Instance,
                LuaConstant.TagNumber => new LuaConstant.Number(ReadNumber(ref c, h)),
                LuaConstant.TagString => new LuaConstant.Str(ReadString(ref c, h) ?? []),
                // 5.0 never dumps booleans; seeing one means the field order has drifted, which is
                // worth saying plainly rather than guessing at a value.
                _ => throw new LuaChunkFormatException(
                        $"constant {i} has tag {tag}, which Lua 5.0 does not dump (nil/number/string only)."),
            };
        }
        p.Constants = k;

        int protos = ReadInt(ref c, h);
        var nested = new LuaPrototype[protos];
        for (int i = 0; i < protos; i++) nested[i] = ReadPrototype(ref c, h);
        p.Prototypes = nested;

        int codeLength = ReadInt(ref c, h);
        var code = new uint[codeLength];
        for (int i = 0; i < codeLength; i++) code[i] = ReadUInt32(ref c, h);
        p.Code = code;

        return p;
    }

    private static int ReadInt(ref Cursor c, LuaChunkHeader h) => h.SizeOfInt == 4
        ? unchecked((int)ReadUInt32(ref c, h))
        : checked((int)ReadUInt64(ref c, h));

    private static uint ReadUInt32(ref Cursor c, LuaChunkHeader h)
    {
        var s = c.Take(4);
        return h.LittleEndian ? BinaryPrimitives.ReadUInt32LittleEndian(s) : BinaryPrimitives.ReadUInt32BigEndian(s);
    }

    private static ulong ReadUInt64(ref Cursor c, LuaChunkHeader h)
    {
        var s = c.Take(8);
        return h.LittleEndian ? BinaryPrimitives.ReadUInt64LittleEndian(s) : BinaryPrimitives.ReadUInt64BigEndian(s);
    }

    private static long ReadSizeT(ref Cursor c, LuaChunkHeader h) => h.SizeOfSizeT == 4
        ? ReadUInt32(ref c, h)
        : checked((long)ReadUInt64(ref c, h));

    private static double ReadNumber(ref Cursor c, LuaChunkHeader h)
    {
        var s = c.Take(h.SizeOfNumber);
        if (h.SizeOfNumber == 4)
        {
            uint bits = h.LittleEndian ? BinaryPrimitives.ReadUInt32LittleEndian(s) : BinaryPrimitives.ReadUInt32BigEndian(s);
            return BitConverter.UInt32BitsToSingle(bits);
        }
        ulong wide = h.LittleEndian ? BinaryPrimitives.ReadUInt64LittleEndian(s) : BinaryPrimitives.ReadUInt64BigEndian(s);
        return BitConverter.UInt64BitsToDouble(wide);
    }

    /// <summary>A dumped string is a size_t length then that many bytes, the last of which is the
    /// terminating NUL. Length 0 means "no string" and reads back as null, which is how nested
    /// prototypes record that they share the parent's source.</summary>
    private static byte[]? ReadString(ref Cursor c, LuaChunkHeader h)
    {
        long length = ReadSizeT(ref c, h);
        if (length == 0) return null;
        if (length < 0 || length > int.MaxValue)
            throw new LuaChunkFormatException($"string length {length} is out of range.");
        var bytes = c.Take((int)length);
        if (bytes[^1] != 0)
            throw new LuaChunkFormatException("dumped string is not NUL-terminated; field order has drifted.");
        return bytes[..^1].ToArray();
    }

    /// <summary>Bounds-checked forward cursor: a truncated chunk reports where it ran out.</summary>
    private struct Cursor(byte[] data)
    {
        private readonly byte[] _data = data;
        public int Offset { get; private set; }

        public ReadOnlySpan<byte> Take(int count)
        {
            if (Offset + count > _data.Length)
                throw new LuaChunkFormatException(
                    $"chunk ends early: wanted {count} byte(s) at 0x{Offset:X} of 0x{_data.Length:X}.");
            var span = _data.AsSpan(Offset, count);
            Offset += count;
            return span;
        }

        public byte Byte() => Take(1)[0];
    }
}
