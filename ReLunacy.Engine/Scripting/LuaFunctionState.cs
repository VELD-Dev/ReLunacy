namespace ReLunacy.Engine.Scripting;

/// <summary>
/// Per-prototype bookkeeping while compiling: the instruction list, the constant pool, register
/// allocation, the active locals and pending jumps.
///
/// It mirrors lcode.c's FuncState because the target is not merely working bytecode but the SAME
/// bytecode the reference compiler produced - there are 524 shipped chunks to compare against, and
/// matching them is only possible by allocating registers and interning constants in the same order.
///
/// Two details were read off those chunks rather than taken from the reference: the top-level
/// prototype is not vararg (stock 5.0 marks it so), and maxstacksize starts at 2 because registers
/// 0 and 1 are always considered valid.
/// </summary>
public sealed class LuaFunctionState
{
    /// <summary>Registers 0 and 1 are always valid, so every prototype starts here.</summary>
    public const int MinimumStackSize = 2;

    /// <summary>Highest constant index that fits in an RK operand. Past it a constant has to be
    /// loaded into a register first, because the operand's upper range is all that is left once the
    /// low 250 values are reserved for registers.</summary>
    public const int MaxRkConstant = LuaInstructionLayout.MaxRkConstantIndex;

    private readonly List<uint> _code = [];
    private readonly List<LuaConstant> _constants = [];
    private readonly Dictionary<string, int> _constantIndex = new(StringComparer.Ordinal);
    private readonly List<LuaPrototype> _prototypes = [];
    private readonly List<Local> _actives = [];

    public LuaFunctionState? Parent { get; }
    public byte ParameterCount { get; set; }
    public byte IsVararg { get; set; }

    /// <summary>First register not in use. Locals occupy the low registers permanently; temporaries
    /// are taken from here and released after use.</summary>
    public int FreeRegister { get; private set; }

    public int MaxStackSize { get; private set; } = MinimumStackSize;

    public int InstructionCount => _code.Count;

    public LuaFunctionState(LuaFunctionState? parent = null) => Parent = parent;

    private readonly record struct Local(string Name, int Register);

    // --- emission ------------------------------------------------------------------------------

    public int Emit(uint instruction)
    {
        _code.Add(instruction);
        return _code.Count - 1;
    }

    public int EmitABC(LuaOpcode op, int a, int b, int c) => Emit(
        (uint)op
        | ((uint)a << LuaInstructionLayout.PosA)
        | ((uint)b << LuaInstructionLayout.PosB)
        | ((uint)c << LuaInstructionLayout.PosC));

    public int EmitABx(LuaOpcode op, int a, int bx) => Emit(
        (uint)op
        | ((uint)a << LuaInstructionLayout.PosA)
        | ((uint)bx << LuaInstructionLayout.PosBx));

    public int EmitAsBx(LuaOpcode op, int a, int sbx) =>
        EmitABx(op, a, sbx + LuaInstructionLayout.MaxArgSBx);

    /// <summary>
    /// Removes and returns the instructions from <paramref name="start"/> onwards.
    ///
    /// This is how Lua 5.0 compiles a while loop: it generates the condition, copies the
    /// instructions out, rewinds pc, compiles the body, then re-emits the saved code at the bottom.
    /// The result is body-first code with the condition's CONSTANTS interned in source order -
    /// compiling the body first gets the layout right and the constant order wrong.
    /// </summary>
    public uint[] ExtractFrom(int start)
    {
        var extracted = _code.Skip(start).ToArray();
        _code.RemoveRange(start, _code.Count - start);
        return extracted;
    }

    public uint InstructionAt(int pc) => _code[pc];
    public void SetInstruction(int pc, uint instruction) => _code[pc] = instruction;

    /// <summary>Rewrites an emitted instruction's A operand - how a relocatable expression is told
    /// which register to land in once that is known.</summary>
    public void SetA(int pc, int a)
    {
        uint mask = (uint)LuaInstructionLayout.MaxArgA << LuaInstructionLayout.PosA;
        _code[pc] = (_code[pc] & ~mask) | ((uint)a << LuaInstructionLayout.PosA);
    }

    // --- registers -----------------------------------------------------------------------------

    public void CheckStack(int needed)
    {
        int top = FreeRegister + needed;
        if (top > MaxStackSize) MaxStackSize = top;
    }

    public void ReserveRegisters(int count)
    {
        CheckStack(count);
        FreeRegister += count;
    }

    /// <summary>Releases a register if it is a temporary. A register below the active locals belongs
    /// to one of them and must not be reclaimed, which is the distinction lcode.c draws with nactive.</summary>
    public void FreeRegisterIfTemporary(int register)
    {
        if (register < _actives.Count) return;
        FreeRegister--;
    }

    public void SetFreeRegister(int value) => FreeRegister = value;

    // --- locals --------------------------------------------------------------------------------

    public int ActiveLocalCount => _actives.Count;

    /// <summary>Brings a local into scope at the current register. Order matters: a local declared
    /// later shadows an earlier one of the same name, so lookups scan backwards.</summary>
    public int DeclareLocal(string name)
    {
        int register = _actives.Count;
        _actives.Add(new Local(name, register));
        if (register + 1 > MaxStackSize) MaxStackSize = register + 1;
        FreeRegister = Math.Max(FreeRegister, register + 1);
        return register;
    }

    /// <summary>The register holding a local, or -1 when the name is not one. Backwards, so the
    /// innermost and newest binding wins.</summary>
    public int FindLocal(string name)
    {
        for (int i = _actives.Count - 1; i >= 0; i--)
            if (_actives[i].Name == name) return _actives[i].Register;
        return -1;
    }

    /// <summary>Drops locals declared past a mark, which is what leaving a block does.</summary>
    public void RemoveLocalsAfter(int mark)
    {
        while (_actives.Count > mark) _actives.RemoveAt(_actives.Count - 1);
        FreeRegister = Math.Max(_actives.Count, Math.Min(FreeRegister, _actives.Count));
    }

    // --- constants -----------------------------------------------------------------------------

    /// <summary>
    /// Interns a constant, returning its index. Deduplicated and assigned in order of first use,
    /// which is what the reference compiler's constant table does - any other order would produce
    /// different operands for identical source.
    /// </summary>
    public int AddConstant(LuaConstant constant)
    {
        string key = constant switch
        {
            LuaConstant.Nil => "nil",
            LuaConstant.Number n => "n:" + n.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            LuaConstant.Str s => "s:" + s.Text,
            _ => throw new ArgumentException($"cannot intern {constant.GetType().Name}", nameof(constant)),
        };
        if (_constantIndex.TryGetValue(key, out int existing)) return existing;

        _constants.Add(constant);
        int index = _constants.Count - 1;
        _constantIndex[key] = index;
        return index;
    }

    public int AddStringConstant(string text) =>
        AddConstant(new LuaConstant.Str(System.Text.Encoding.UTF8.GetBytes(text)));

    public int AddNumberConstant(double value) => AddConstant(new LuaConstant.Number(value));

    // --- nested prototypes ---------------------------------------------------------------------

    public int AddPrototype(LuaPrototype prototype)
    {
        _prototypes.Add(prototype);
        return _prototypes.Count - 1;
    }

    // --- jumps ---------------------------------------------------------------------------------

    /// <summary>Emits a jump with no destination yet, to be patched once the target is known.</summary>
    public int EmitJump() => EmitAsBx(LuaOpcode.Jmp, 0, 0);

    /// <summary>Points a jump at the current end of the code. Lua jumps are relative to the
    /// instruction AFTER the jump, which is why the offset subtracts one more.</summary>
    public void PatchToHere(int jumpPc)
    {
        var instruction = new LuaInstruction(_code[jumpPc]);
        int offset = _code.Count - (jumpPc + 1);
        _code[jumpPc] = (uint)instruction.Opcode
            | ((uint)instruction.A << LuaInstructionLayout.PosA)
            | ((uint)(offset + LuaInstructionLayout.MaxArgSBx) << LuaInstructionLayout.PosBx);
    }

    public void PatchTo(int jumpPc, int target)
    {
        var instruction = new LuaInstruction(_code[jumpPc]);
        int offset = target - (jumpPc + 1);
        _code[jumpPc] = (uint)instruction.Opcode
            | ((uint)instruction.A << LuaInstructionLayout.PosA)
            | ((uint)(offset + LuaInstructionLayout.MaxArgSBx) << LuaInstructionLayout.PosBx);
    }

    // --- finalisation --------------------------------------------------------------------------

    /// <summary>
    /// Produces the prototype. Every shipped chunk ends in <c>RETURN 0 1</c> - return no values - so
    /// that is appended unconditionally, matching what the reference emits at the end of a body.
    /// Debug information is left empty because the shipped chunks carry none: no line info, no local
    /// names, no upvalue names, and a source of "=(none)" on the top-level prototype only.
    /// </summary>
    public LuaPrototype Finish(bool isMainChunk)
    {
        EmitABC(LuaOpcode.Return, 0, 1, 0);
        return new LuaPrototype
        {
            Source = isMainChunk ? System.Text.Encoding.UTF8.GetBytes("=(none)") : null,
            LineDefined = 0,
            UpvalueCount = 0,
            ParameterCount = ParameterCount,
            IsVararg = IsVararg,
            MaxStackSize = (byte)Math.Min(MaxStackSize, byte.MaxValue),
            LineInfo = [],
            Locals = [],
            Upvalues = [],
            Constants = [.. _constants],
            Prototypes = [.. _prototypes],
            Code = [.. _code],
        };
    }
}
