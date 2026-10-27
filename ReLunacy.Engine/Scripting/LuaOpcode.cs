namespace ReLunacy.Engine.Scripting;

/// <summary>
/// Lua 5.0's 35 opcodes, in dump order. 5.1 is NOT compatible: it adds MOD, LEN, FORPREP, TESTSET
/// and VARARG, drops TFORPREP and SETLISTO, and changes SETLIST - so a 5.1 table decodes b4404
/// chunks into nonsense.
/// </summary>
public enum LuaOpcode : byte
{
    Move, LoadK, LoadBool, LoadNil, GetUpval, GetGlobal, GetTable, SetGlobal,
    SetUpval, SetTable, NewTable, Self, Add, Sub, Mul, Div, Pow, Unm, Not, Concat,
    Jmp, Eq, Lt, Le, Test, Call, TailCall, Return, ForLoop, TForLoop, TForPrep,
    SetList, SetListO, Close, Closure,
}

/// <summary>How an instruction's operands are packed.</summary>
public enum LuaOpMode { ABC, ABx, AsBx }

/// <summary>Which operands of an instruction may name a constant instead of a register.</summary>
[Flags]
public enum LuaRkOperands { None = 0, B = 1, C = 2 }

/// <summary>
/// Operand packing for b4404, established empirically rather than assumed: all 310,047 instructions
/// in ToD's 524 chunks satisfy every range invariant under this layout (CLOSURE's Bx within the
/// prototype table, LOADK/GETGLOBAL/SETGLOBAL within the constant table and of the right type,
/// A within the declared stack), while the Lua 5.1 layout violates them tens of thousands of times.
///
/// Word is OP|C|B|A from the low bit up - the reverse of 5.1's OP|A|C|B.
/// </summary>
public static class LuaInstructionLayout
{
    public const int OpBits = 6, ABits = 8, BBits = 9, CBits = 9;

    public const int PosOp = 0;
    public const int PosC = PosOp + OpBits;   // 6
    public const int PosB = PosC + CBits;     // 15
    public const int PosA = PosB + BBits;     // 24
    public const int PosBx = PosC;            // 6, spanning C and B

    public const int MaxArgA = (1 << ABits) - 1;
    public const int MaxArgB = (1 << BBits) - 1;
    public const int MaxArgC = (1 << CBits) - 1;
    public const int MaxArgBx = (1 << (CBits + BBits)) - 1;

    /// <summary>Signed Bx is stored biased by half its range.</summary>
    public const int MaxArgSBx = MaxArgBx >> 1;

    /// <summary>An RK operand with this bit set names a constant; the rest is the index.
    /// Lua 5.0 puts it at 1 &lt;&lt; (SIZE_B - 1).</summary>
    public const int BitRk = 1 << (BBits - 1);

    /// <summary>Lua 5.0 flushes a table constructor every 32 fields, which is how SETLIST's Bx
    /// encodes both the batch and the count.</summary>
    public const int FieldsPerFlush = 32;

    public static bool IsConstant(int rk) => (rk & BitRk) != 0;
    public static int ConstantIndex(int rk) => rk & ~BitRk;
}

/// <summary>One decoded instruction. Operands are raw; interpreting B/C as register-or-constant is
/// the caller's job via <see cref="LuaOpcodeInfo"/>.</summary>
public readonly record struct LuaInstruction(uint Raw)
{
    public LuaOpcode Opcode => (LuaOpcode)(Raw & ((1u << LuaInstructionLayout.OpBits) - 1));
    public int A => (int)((Raw >> LuaInstructionLayout.PosA) & LuaInstructionLayout.MaxArgA);
    public int B => (int)((Raw >> LuaInstructionLayout.PosB) & LuaInstructionLayout.MaxArgB);
    public int C => (int)((Raw >> LuaInstructionLayout.PosC) & LuaInstructionLayout.MaxArgC);
    public int Bx => (int)((Raw >> LuaInstructionLayout.PosBx) & LuaInstructionLayout.MaxArgBx);
    public int SBx => Bx - LuaInstructionLayout.MaxArgSBx;

    /// <summary>True when the opcode is one Lua 5.0 actually defines.</summary>
    public bool IsKnown => (byte)Opcode <= (byte)LuaOpcode.Closure;
}

/// <summary>Name, operand mode and RK operands for each opcode, plus the semantics comment from
/// Lua 5.0's lopcodes.h so a disassembly listing can explain itself.</summary>
public sealed record LuaOpcodeInfo(string Name, LuaOpMode Mode, LuaRkOperands Rk, string Semantics)
{
    private static readonly LuaOpcodeInfo[] Table =
    [
        new("MOVE",      LuaOpMode.ABC,  LuaRkOperands.None,      "R(A) := R(B)"),
        new("LOADK",     LuaOpMode.ABx,  LuaRkOperands.None,      "R(A) := Kst(Bx)"),
        new("LOADBOOL",  LuaOpMode.ABC,  LuaRkOperands.None,      "R(A) := (Bool)B; if (C) PC++"),
        new("LOADNIL",   LuaOpMode.ABC,  LuaRkOperands.None,      "R(A) := ... := R(B) := nil"),
        new("GETUPVAL",  LuaOpMode.ABC,  LuaRkOperands.None,      "R(A) := UpValue[B]"),
        new("GETGLOBAL", LuaOpMode.ABx,  LuaRkOperands.None,      "R(A) := Gbl[Kst(Bx)]"),
        new("GETTABLE",  LuaOpMode.ABC,  LuaRkOperands.C,         "R(A) := R(B)[RK(C)]"),
        new("SETGLOBAL", LuaOpMode.ABx,  LuaRkOperands.None,      "Gbl[Kst(Bx)] := R(A)"),
        new("SETUPVAL",  LuaOpMode.ABC,  LuaRkOperands.None,      "UpValue[B] := R(A)"),
        new("SETTABLE",  LuaOpMode.ABC,  LuaRkOperands.B | LuaRkOperands.C, "R(A)[RK(B)] := RK(C)"),
        new("NEWTABLE",  LuaOpMode.ABC,  LuaRkOperands.None,      "R(A) := {} (size = B,C)"),
        new("SELF",      LuaOpMode.ABC,  LuaRkOperands.C,         "R(A+1) := R(B); R(A) := R(B)[RK(C)]"),
        new("ADD",       LuaOpMode.ABC,  LuaRkOperands.B | LuaRkOperands.C, "R(A) := RK(B) + RK(C)"),
        new("SUB",       LuaOpMode.ABC,  LuaRkOperands.B | LuaRkOperands.C, "R(A) := RK(B) - RK(C)"),
        new("MUL",       LuaOpMode.ABC,  LuaRkOperands.B | LuaRkOperands.C, "R(A) := RK(B) * RK(C)"),
        new("DIV",       LuaOpMode.ABC,  LuaRkOperands.B | LuaRkOperands.C, "R(A) := RK(B) / RK(C)"),
        new("POW",       LuaOpMode.ABC,  LuaRkOperands.B | LuaRkOperands.C, "R(A) := RK(B) ^ RK(C)"),
        new("UNM",       LuaOpMode.ABC,  LuaRkOperands.None,      "R(A) := -R(B)"),
        new("NOT",       LuaOpMode.ABC,  LuaRkOperands.None,      "R(A) := not R(B)"),
        new("CONCAT",    LuaOpMode.ABC,  LuaRkOperands.None,      "R(A) := R(B).. ... ..R(C)"),
        new("JMP",       LuaOpMode.AsBx, LuaRkOperands.None,      "PC += sBx"),
        new("EQ",        LuaOpMode.ABC,  LuaRkOperands.B | LuaRkOperands.C, "if ((RK(B) == RK(C)) ~= A) then PC++"),
        new("LT",        LuaOpMode.ABC,  LuaRkOperands.B | LuaRkOperands.C, "if ((RK(B) <  RK(C)) ~= A) then PC++"),
        new("LE",        LuaOpMode.ABC,  LuaRkOperands.B | LuaRkOperands.C, "if ((RK(B) <= RK(C)) ~= A) then PC++"),
        new("TEST",      LuaOpMode.ABC,  LuaRkOperands.None,      "if (boolean(R(B)) == C) then R(A) := R(B) else PC++"),
        new("CALL",      LuaOpMode.ABC,  LuaRkOperands.None,      "R(A), ... ,R(A+C-2) := R(A)(R(A+1), ... ,R(A+B-1))"),
        new("TAILCALL",  LuaOpMode.ABC,  LuaRkOperands.None,      "return R(A)(R(A+1), ... ,R(A+B-1))"),
        new("RETURN",    LuaOpMode.ABC,  LuaRkOperands.None,      "return R(A), ... ,R(A+B-2)"),
        new("FORLOOP",   LuaOpMode.AsBx, LuaRkOperands.None,      "R(A) += R(A+2); if R(A) <?= R(A+1) then PC += sBx"),
        new("TFORLOOP",  LuaOpMode.ABC,  LuaRkOperands.None,      "R(A+2), ... ,R(A+2+C) := R(A)(R(A+1), R(A+2)); if R(A+2) ~= nil then pc++"),
        new("TFORPREP",  LuaOpMode.AsBx, LuaRkOperands.None,      "if type(R(A)) == table then R(A+1) := R(A), R(A) := next; PC += sBx"),
        new("SETLIST",   LuaOpMode.ABx,  LuaRkOperands.None,      "R(A)[Bx-Bx%FPF+i] := R(A+i), 1 <= i <= Bx%FPF+1"),
        new("SETLISTO",  LuaOpMode.ABx,  LuaRkOperands.None,      "R(A)[Bx-Bx%FPF+i] := R(A+i), 1 <= i <= top-A"),
        new("CLOSE",     LuaOpMode.ABC,  LuaRkOperands.None,      "close all variables in the stack up to (>=) R(A)"),
        new("CLOSURE",   LuaOpMode.ABx,  LuaRkOperands.None,      "R(A) := closure(KPROTO[Bx], R(A), ... ,R(A+n))"),
    ];

    public static LuaOpcodeInfo Of(LuaOpcode op) => (byte)op < Table.Length
        ? Table[(byte)op]
        : new($"UNKNOWN_{(byte)op}", LuaOpMode.ABC, LuaRkOperands.None, "not a Lua 5.0 opcode");

    public static int Count => Table.Length;
}
