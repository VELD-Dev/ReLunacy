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
        // Anything waiting for "here" now knows where here is. Lua does this in luaK_code for the
        // same reason: the target of a jump-to-here is only decided once something is emitted.
        DischargePendingJumps();
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

    /// <summary>
    /// Rewrites an emitted instruction's C operand. What turning a call into one that returns
    /// everything it has amounts to - <c>wait(rand(a, b))</c> needs the inner call's result count
    /// replaced after it has already been emitted.
    /// </summary>
    public void SetInstructionC(int pc, int c)
    {
        const uint mask = LuaInstructionLayout.MaxArgC << LuaInstructionLayout.PosC;
        _code[pc] = (_code[pc] & ~mask) | ((uint)c << LuaInstructionLayout.PosC);
    }

    /// <summary>Rewrites an emitted instruction's B operand.</summary>
    public void SetInstructionB(int pc, int b)
    {
        const uint mask = LuaInstructionLayout.MaxArgB << LuaInstructionLayout.PosB;
        _code[pc] = (_code[pc] & ~mask) | ((uint)b << LuaInstructionLayout.PosB);
    }

    /// <summary>Replaces an emitted instruction's opcode, keeping its operands - CALL becoming
    /// TAILCALL once <c>return f()</c> turns out to be a tail call.</summary>
    public void SetInstructionOpcode(int pc, LuaOpcode op)
    {
        const uint mask = (1u << LuaInstructionLayout.OpBits) - 1;
        _code[pc] = (_code[pc] & ~mask) | (uint)op;
    }

    /// <summary>The A operand of an emitted instruction - which register a call left its result in.</summary>
    public int InstructionA(int pc) => new LuaInstruction(_code[pc]).A;

    // --- jumps ---------------------------------------------------------------------------------

    /// <summary>
    /// Jumps waiting to learn where "here" is - Lua's <c>fs->jpc</c>.
    ///
    /// A jump to the current end of the code cannot be resolved when it is requested, because what
    /// ends up there may itself be a jump. Holding them lets <see cref="EmitJump"/> take them over
    /// instead, which is what turns a jump to a jump into a single jump: at the end of
    /// <c>if a then if b then X end end</c> the inner test's false jump is waiting, and the outer
    /// jump over the else absorbs it, so both land past the else rather than one landing on the
    /// other.
    /// </summary>
    private readonly List<int> _pendingJumps = [];

    /// <summary>
    /// Emits a jump with no destination yet, together with every jump that was waiting for this
    /// point - they are patched to wherever this one is pointed, not to this instruction.
    ///
    /// The waiting list is taken and cleared before emitting, which is what stops the emission from
    /// discharging them onto this jump: luaK_jump does exactly this.
    /// </summary>
    public List<int> EmitJump()
    {
        var absorbed = new List<int>(_pendingJumps);
        _pendingJumps.Clear();
        int pc = EmitAsBx(LuaOpcode.Jmp, 0, 0);
        absorbed.Insert(0, pc);
        return absorbed;
    }

    /// <summary>Holds jumps until the next emitted instruction settles where they go.</summary>
    public void PatchToHere(IReadOnlyList<int> jumps) => _pendingJumps.AddRange(jumps);

    /// <summary>Points jumps at an instruction that is already known.</summary>
    public void PatchTo(IReadOnlyList<int> jumps, int target)
    {
        foreach (int jumpPc in jumps) PatchOne(jumpPc, target);
    }

    private void DischargePendingJumps()
    {
        if (_pendingJumps.Count == 0) return;
        int here = _code.Count;
        // Cleared first so patching cannot re-enter this through Emit.
        int[] waiting = [.. _pendingJumps];
        _pendingJumps.Clear();
        foreach (int jumpPc in waiting) PatchOne(jumpPc, here);
    }

    /// <summary>Lua jumps are relative to the instruction AFTER the jump, which is why the offset
    /// subtracts one more.</summary>
    private void PatchOne(int jumpPc, int target)
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
