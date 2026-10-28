namespace ReLunacy.Engine.Scripting;

/// <summary>
/// A loaded Lua precompiled chunk (a <c>.lc</c> file), as shipped in global_cached.psarc.
///
/// The games here (Resistance: Fall of Man, R&amp;C Tools of Destruction, R&amp;C Quest for Booty) use
/// the LuaC variant catalogued as b4404: Lua <b>5.0</b> bytecode - not 5.1 - little-endian, 32-bit
/// int/size_t/Instruction, instruction fields OP6/A8/B9/C9, and <c>lua_Number</c> as <b>float32</b>
/// rather than stock Lua's double. All 524 ToD chunks carry a byte-identical 18-byte header.
///
/// The model keeps everything needed to write the chunk back unchanged, which is what proves the
/// reader correct and what the compiler will emit through.
/// </summary>
public sealed class LuaChunk
{
    public required LuaChunkHeader Header { get; init; }

    /// <summary>The chunk's top-level function. Shipped chunks have their source name stripped to
    /// <c>=(none)</c>, so there is no original filename to recover from the bytecode.</summary>
    public required LuaPrototype Main { get; init; }
}

/// <summary>
/// The 12 fixed bytes plus the <c>lua_Number</c> test value that open a precompiled chunk. Retained
/// verbatim rather than assumed, so a chunk from a different build round-trips and a mismatch is
/// reported instead of silently misparsed.
/// </summary>
public sealed class LuaChunkHeader
{
    public const byte Lua50 = 0x50;

    /// <summary>
    /// The header check value, written in the chunk's own number format. b4404 uses PI * 1e7, which
    /// reads back as float32 31415926 - NOT the PI * 1e8 of stock Lua 5.0's lundump.h. Taken from the
    /// shipped chunks: every one of the 524 carries 3B AF EF 4B here.
    ///
    /// The round-trip check could never have found this. The reader keeps these bytes and the writer
    /// replays them, so a chunk read from disk is reproduced either way; only synthesising a header,
    /// as the compiler does, depends on the value being right.
    /// </summary>
    public const double NumberTestValue = 3.14159265358979323846E7;

    public byte Version { get; init; } = Lua50;
    public bool LittleEndian { get; init; } = true;
    public byte SizeOfInt { get; init; } = 4;
    public byte SizeOfSizeT { get; init; } = 4;
    public byte SizeOfInstruction { get; init; } = 4;
    public byte OpBits { get; init; } = 6;
    public byte ABits { get; init; } = 8;
    public byte BBits { get; init; } = 9;
    public byte CBits { get; init; } = 9;

    /// <summary>4 on b4404 (float32). Stock Lua 5.0 uses 8 (double).</summary>
    public byte SizeOfNumber { get; init; } = 4;

    /// <summary>The test value exactly as it appeared, so writing cannot perturb it.</summary>
    public required byte[] NumberTestBytes { get; init; }

    /// <summary>True for the b4404 shape this editor targets.</summary>
    public bool IsB4404 =>
        Version == Lua50 && LittleEndian && SizeOfInt == 4 && SizeOfSizeT == 4 &&
        SizeOfInstruction == 4 && OpBits == 6 && ABits == 8 && BBits == 9 && CBits == 9 &&
        SizeOfNumber == 4;

    public override string ToString() =>
        $"Lua {Version >> 4}.{Version & 0xF} {(LittleEndian ? "LE" : "BE")} " +
        $"int{SizeOfInt} size_t{SizeOfSizeT} instr{SizeOfInstruction} " +
        $"{OpBits}/{ABits}/{BBits}/{CBits} number{SizeOfNumber}";
}

/// <summary>One function prototype. Lua 5.0's field order, which is what the dump format uses.</summary>
public sealed class LuaPrototype
{
    /// <summary>Raw bytes of the source name without its terminating NUL; null when the dump stored
    /// no string (nested prototypes inherit the parent's source, so theirs is usually null).</summary>
    public byte[]? Source { get; set; }

    public int LineDefined { get; set; }
    public byte UpvalueCount { get; set; }
    public byte ParameterCount { get; set; }

    /// <summary>Lua 5.0 stores this as a byte, not 5.1's flag set.</summary>
    public byte IsVararg { get; set; }

    public byte MaxStackSize { get; set; }

    /// <summary>One source line per instruction; empty when debug info was stripped.</summary>
    public int[] LineInfo { get; set; } = [];

    public LuaLocal[] Locals { get; set; } = [];

    /// <summary>Upvalue names; empty when stripped.</summary>
    public byte[]?[] Upvalues { get; set; } = [];

    public LuaConstant[] Constants { get; set; } = [];

    /// <summary>Nested function prototypes, in dump order.</summary>
    public LuaPrototype[] Prototypes { get; set; } = [];

    /// <summary>Raw 32-bit instructions. Decoding into opcodes is the disassembler's job, not the
    /// reader's - keeping them raw is what lets a chunk round-trip byte-exactly.</summary>
    public uint[] Code { get; set; } = [];

    /// <summary>Every prototype in this tree, depth-first, this one first.</summary>
    public IEnumerable<LuaPrototype> Walk()
    {
        yield return this;
        foreach (var child in Prototypes)
            foreach (var p in child.Walk())
                yield return p;
    }
}

/// <summary>A debug entry naming a register over an instruction range.</summary>
public readonly record struct LuaLocal(byte[]? Name, int StartPc, int EndPc);

/// <summary>
/// A constant-table entry. Lua 5.0 dumps only nil, number and string - booleans are produced by
/// OP_LOADBOOL rather than stored, which is one of the differences from 5.1.
/// </summary>
public abstract record LuaConstant
{
    /// <summary>Lua's type tags, as written in the dump.</summary>
    public const byte TagNil = 0;
    public const byte TagBoolean = 1;
    public const byte TagNumber = 3;
    public const byte TagString = 4;

    public sealed record Nil : LuaConstant
    {
        public static readonly Nil Instance = new();
        public override string ToString() => "nil";
    }

    /// <summary>Stored as read; b4404 numbers are float32, so values are not double-precision.</summary>
    public sealed record Number(double Value) : LuaConstant
    {
        /// <summary>b4404 numbers are float32, so a constant read back as a double carries the
        /// widening artefacts of that - 0.2f prints as 0.20000000298023224 unless it is rendered at
        /// the precision it was actually stored with. Invariant culture throughout: a listing that
        /// says "1,5" on a French machine and "1.5" elsewhere is not a format tool.</summary>
        public override string ToString()
        {
            var f = (float)Value;
            return (double)f == Value
                ? f.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
                : Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    /// <summary>Raw bytes without the terminating NUL. Lua strings are byte strings, so they are kept
    /// as bytes rather than decoded, both to round-trip exactly and because nothing guarantees UTF-8.</summary>
    public sealed record Str(byte[] Bytes) : LuaConstant
    {
        public string Text => System.Text.Encoding.UTF8.GetString(Bytes);
        public override string ToString() => $"\"{Text}\"";
    }
}
