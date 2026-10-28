namespace ReLunacy.Engine.Scripting;

/// <summary>Something the compiler does not handle yet, with where it is.</summary>
public readonly record struct LuaCompileError(string Message, LuaSpan Span)
{
    public override string ToString() => $"{Span.Start}: {Message}";
}

/// <summary>
/// Compiles Lua 5.0 source to a b4404 chunk.
///
/// Expressions go through a descriptor rather than being emitted as they are parsed, the way
/// lcode.c does: a constant, a local, a global or a pending instruction is carried unmaterialised
/// until the consumer knows where it has to land. That is not an optimisation, it is the only way to
/// reproduce the reference compiler's register use and constant ordering - and reproducing it
/// exactly is checkable, because all 524 shipped sources ship next to the bytecode the real compiler
/// produced from them.
///
/// Incomplete by design at this stage. Constructs that are not implemented are reported rather than
/// approximated, so the match rate against those 524 pairs measures real progress instead of
/// counting chunks that happen to be wrong in a way that still assembles.
/// </summary>
public sealed class LuaCompiler
{
    private readonly List<LuaCompileError> _errors = [];
    private LuaFunctionState _state = null!;

    public IReadOnlyList<LuaCompileError> Errors => _errors;

    /// <summary>Compiles source to a chunk, or null when something is unsupported or malformed.</summary>
    public static LuaChunk? Compile(string source, out IReadOnlyList<LuaCompileError> errors)
    {
        var block = LuaParser.Parse(source, out var syntax);
        var compiler = new LuaCompiler();
        foreach (var diagnostic in syntax)
            compiler._errors.Add(new LuaCompileError(diagnostic.Message, diagnostic.Span));
        if (compiler._errors.Count > 0)
        {
            errors = compiler._errors;
            return null;
        }

        var chunk = compiler.CompileChunk(block);
        errors = compiler._errors;
        return compiler._errors.Count > 0 ? null : chunk;
    }

    private LuaChunk CompileChunk(LuaBlock block)
    {
        // The top-level prototype of every shipped chunk is numparams=0, nups=0 and NOT vararg -
        // stock Lua 5.0 marks its main chunk vararg, so this was taken from the chunks themselves.
        _state = new LuaFunctionState { ParameterCount = 0, IsVararg = 0 };
        CompileBlock(block);
        var main = _state.Finish(isMainChunk: true);

        return new LuaChunk
        {
            Header = new LuaChunkHeader
            {
                Version = LuaChunkHeader.Lua50,
                LittleEndian = true,
                SizeOfInt = 4,
                SizeOfSizeT = 4,
                SizeOfInstruction = 4,
                OpBits = 6, ABits = 8, BBits = 9, CBits = 9,
                SizeOfNumber = 4,
                // The check value in the chunk's own number format: float32, as b4404 uses.
                NumberTestBytes = BitConverter.GetBytes((float)LuaChunkHeader.NumberTestValue),
            },
            Main = main,
        };
    }

    private void CompileBlock(LuaBlock block)
    {
        int localMark = _state.ActiveLocalCount;
        foreach (var statement in block.Statements)
        {
            CompileStatement(statement);
            // Every temporary a statement used is released at its end - chunk() does this after
            // each one, and it is what keeps a register from leaking statement after statement.
            // `DialogueSpecs.X = "y"` repeated 3700 times climbed to 255 stack slots without it,
            // where the reference reuses one.
            _state.SetFreeRegister(_state.ActiveLocalCount);
        }
        _state.RemoveLocalsAfter(localMark);
    }

    private void CompileStatement(LuaStat statement)
    {
        switch (statement)
        {
            case LuaCallStat call:
            {
                int baseRegister = _state.FreeRegister;
                var expression = CompileCall(call.Call, resultCount: 0);
                // A call used as a statement discards its results, so the registers it took go back.
                _state.SetFreeRegister(baseRegister);
                _ = expression;
                break;
            }

            case LuaLocalStat local:
                CompileLocal(local);
                break;

            case LuaAssignStat assign:
                CompileAssign(assign);
                break;

            case LuaReturnStat ret:
                CompileReturn(ret);
                break;

            case LuaIfStat ifStat:
                CompileIf(ifStat);
                break;

            case LuaWhileStat whileStat:
                CompileWhile(whileStat);
                break;

            case LuaDoStat doStat:
                CompileBlock(doStat.Body);
                break;

            default:
                Unsupported(statement.GetType().Name, statement.Span);
                break;
        }
    }

    private void CompileLocal(LuaLocalStat local)
    {
        // Values are evaluated before the names come into scope, which is what makes
        // `local x = x` read the outer x.
        foreach (var value in local.Values)
        {
            var expression = CompileExpression(value);
            ExpressionToNextRegister(ref expression);
        }

        // Too few values: the remaining names are nil.
        for (int i = local.Values.Count; i < local.Names.Count; i++)
        {
            int register = _state.FreeRegister;
            _state.ReserveRegisters(1);
            _state.EmitABC(LuaOpcode.LoadNil, register, register, 0);
        }
        // Too many values: the extras were still evaluated, and their registers are dropped.
        if (local.Values.Count > local.Names.Count)
            _state.SetFreeRegister(_state.FreeRegister - (local.Values.Count - local.Names.Count));

        foreach (var name in local.Names) _state.DeclareLocal(name.Name);
    }

    private void CompileAssign(LuaAssignStat assign)
    {
        if (assign.Targets.Count != 1 || assign.Values.Count != 1)
        {
            Unsupported("multiple assignment", assign.Span);
            return;
        }

        // The target is compiled FIRST, and all of it - not just a global's name.
        //
        // exprstat parses the whole left-hand side before it sees the '=', so everything the target
        // mentions is interned and placed before the value is touched. In
        // `pterodactyl = moby_handle:new(0)` that puts "pterodactyl" at the head of the constant
        // table rather than "moby_handle"; in `arena_challenge_7.active = f(x.id) > 0` it puts
        // "active" before "f". Compiling the value first produces correct code with every constant
        // index shifted, and gives the value the registers the target should have had.
        var target = CompileExpression(assign.Targets[0]);
        var value = CompileExpression(assign.Values[0]);

        switch (target.Kind)
        {
            case ExpKind.Local:
                // The temporary holding the value is released BEFORE it is moved into the local -
                // the one case where luaK_storevar frees first rather than last. Without it
                // `starting_health = hero_get_health(Ratchet1)` left the call's result register
                // reserved, so the rest of that block allocated one register too high.
                DischargeVars(ref value);
                FreeIfTemporary(ref value);
                ExpressionToRegister(ref value, target.Info);
                return;

            case ExpKind.Global:
            {
                int source = ExpressionToAnyRegister(ref value);
                _state.EmitABx(LuaOpcode.SetGlobal, source, target.Info);
                break;
            }

            case ExpKind.Indexed:
            {
                int valueRk = ExpressionToRk(ref value);
                _state.EmitABC(LuaOpcode.SetTable, target.Info, target.Aux, valueRk);
                break;
            }

            default:
                Unsupported($"assignment to {assign.Targets[0].GetType().Name}", assign.Span);
                return;
        }

        FreeIfTemporary(ref value);
    }

    private void CompileReturn(LuaReturnStat ret)
    {
        if (ret.Values.Count == 0)
        {
            _state.EmitABC(LuaOpcode.Return, 0, 1, 0);
            return;
        }

        int baseRegister = _state.FreeRegister;
        var last = default(Exp);
        for (int i = 0; i < ret.Values.Count; i++)
        {
            var expression = CompileExpression(ret.Values[i]);
            if (i == ret.Values.Count - 1)
            {
                last = expression;
                // Returning a call returns everything it produced, and when it is the only value
                // being returned it is a tail call - retstat rewrites the opcode in place.
                if (SetMultipleReturns(ref last))
                {
                    if (ret.Values.Count == 1) _state.SetInstructionOpcode(last.Info, LuaOpcode.TailCall);
                    _state.EmitABC(LuaOpcode.Return, baseRegister, 0, 0);
                    _state.SetFreeRegister(baseRegister);
                    return;
                }
            }
            ExpressionToNextRegister(ref expression);
        }
        _state.EmitABC(LuaOpcode.Return, baseRegister, ret.Values.Count + 1, 0);
        _state.SetFreeRegister(baseRegister);
    }

    // --- expressions ---------------------------------------------------------------------------

    private enum ExpKind
    {
        Nil, True, False,
        /// <summary>An interned constant: <c>Info</c> is its index in the pool.</summary>
        Constant,
        Local, Global, Indexed, Relocatable, NonReloc,
        /// <summary>
        /// A test and the jump that depends on it - Lua's VJMP. The expression IS the jump: whether
        /// it ever becomes a value in a register depends on what asks for it.
        /// </summary>
        Jump,
        /// <summary>
        /// A call whose result count is not settled yet - Lua's VCALL. It matters because a call in
        /// the last position of an argument list, or of a return, passes every value it produces
        /// rather than one, and that is decided after the call has already been emitted.
        /// <c>Info</c> is the pc of its CALL instruction, not a register.
        /// </summary>
        Call,
    }

    /// <summary>An expression that has not been committed to a register yet. Mirrors expdesc.</summary>
    private struct Exp
    {
        public ExpKind Kind;
        /// <summary>Register, constant index, or the pc of a relocatable instruction.</summary>
        public int Info;
        /// <summary>For an index: the key's RK operand.</summary>
        public int Aux;

        /// <summary>
        /// Jumps taken when this expression is true, and when it is false - Lua's <c>t</c> and
        /// <c>f</c>. A short-circuiting operator leaves them here for whoever consumes the
        /// expression to resolve, which is what lets <c>a or b</c> be either a condition or a value
        /// without being compiled differently.
        /// </summary>
        public List<int>? TrueList;
        public List<int>? FalseList;

        /// <summary>For <see cref="ExpKind.Jump"/>: the jump itself, plus anything it absorbed.
        /// <c>Info</c> is its head, which is the one whose test sits directly before it.</summary>
        public List<int>? JumpList;
    }

    private static void Concat(ref List<int>? list, IReadOnlyList<int>? extra)
    {
        if (extra is null || extra.Count == 0) return;
        (list ??= []).AddRange(extra);
    }

    private static bool HasJumps(in Exp e) =>
        (e.TrueList?.Count ?? 0) + (e.FalseList?.Count ?? 0) > 0;

    private Exp CompileExpression(LuaExpr expression)
    {
        switch (expression)
        {
            case LuaNilExpr: return new Exp { Kind = ExpKind.Nil };
            case LuaBoolExpr b: return new Exp { Kind = b.Value ? ExpKind.True : ExpKind.False };
            // A literal is interned HERE, not when it is finally used. Lua 5.0's simpleexp builds a
            // VK with the constant already in the pool - there is no deferred numeric kind, that
            // came with 5.1 - and the timing is observable: it is why a negated literal leaves the
            // positive value behind in the table as a constant nothing references.
            case LuaNumberExpr n:
                return new Exp { Kind = ExpKind.Constant, Info = _state.AddNumberConstant(n.Value) };
            case LuaStringExpr s:
                return new Exp { Kind = ExpKind.Constant, Info = _state.AddStringConstant(s.Value) };

            case LuaNameExpr name:
            {
                int register = _state.FindLocal(name.Name);
                return register >= 0
                    ? new Exp { Kind = ExpKind.Local, Info = register }
                    : new Exp { Kind = ExpKind.Global, Info = _state.AddStringConstant(name.Name) };
            }

            case LuaIndexExpr index:
            {
                var table = CompileExpression(index.Target);
                int tableRegister = ExpressionToAnyRegister(ref table);
                var key = CompileExpression(index.Key);
                int keyRk = ExpressionToRk(ref key);
                return new Exp { Kind = ExpKind.Indexed, Info = tableRegister, Aux = keyRk };
            }

            case LuaCallExpr or LuaMethodCallExpr:
                return CompileCall(expression, resultCount: 1);

            case LuaBinaryExpr arithmetic when ArithmeticFor(arithmetic.Operator) is { } opcode:
                return CompileArithmetic(opcode, arithmetic);

            // `a and b` / `a or b` producing a VALUE rather than driving a branch. The left operand
            // contributes only a jump; the right one is the expression, carrying the left's
            // short-circuit jumps along with it - luaK_infix then luaK_posfix.
            case LuaBinaryExpr { Operator: "and" } conjunction:
            {
                var left = CompileExpression(conjunction.Left);
                GoIfTrue(ref left);
                var right = CompileExpression(conjunction.Right);
                DischargeVars(ref right);
                Concat(ref right.FalseList, left.FalseList);
                return right;
            }

            case LuaBinaryExpr { Operator: "or" } disjunction:
            {
                var left = CompileExpression(disjunction.Left);
                GoIfFalse(ref left);
                var right = CompileExpression(disjunction.Right);
                DischargeVars(ref right);
                Concat(ref right.TrueList, left.TrueList);
                return right;
            }

            case LuaBinaryExpr comparison when ComparisonFor(comparison.Operator) is { } shape:
                return CompileComparisonValue(shape, comparison);

            case LuaTableExpr table:
                return CompileTable(table);

            case LuaUnaryExpr unary when unary.Operator == "-":
                return CompileNegate(unary);

            case LuaUnaryExpr unary when unary.Operator == "not":
                return CompileNot(unary);

            case LuaParenExpr paren:
            {
                // Parentheses truncate to one value, which is what discharging the variable forms
                // does - a call becomes its single result. They do NOT force a register: prefixexp
                // calls luaK_dischargevars and nothing more, so an expression still held as an
                // instruction stays that way. Forcing one here hid the NOT in `if (not x)` from the
                // fold in CompileCondition, and the parentheses alone changed the code emitted.
                var inner = CompileExpression(paren.Inner);
                DischargeVars(ref inner);
                return inner;
            }

            default:
                Unsupported(expression.GetType().Name, expression.Span);
                return new Exp { Kind = ExpKind.Nil };
        }
    }

    /// <summary>
    /// Emits the jump a short-circuiting <c>and</c> needs from its left operand: taken when the
    /// expression is FALSE, because that already settles the result. Lua's luaK_goiftrue.
    ///
    /// The true list is resolved here - if the left operand was itself a logical expression, the
    /// jumps it had pending for "true" belong at the right operand, which is what comes next.
    /// </summary>
    private void GoIfTrue(ref Exp e)
    {
        DischargeVars(ref e);
        List<int> whenFalse = e.Kind switch
        {
            // A constant, or true, is never false: no jump at all.
            ExpKind.Constant or ExpKind.True => [],
            // nil and false always are: jump unconditionally.
            ExpKind.Nil or ExpKind.False => _state.EmitJump(),
            ExpKind.Jump => InvertJump(ref e),
            _ => JumpOnCondition(ref e, whenTrue: false),
        };
        Concat(ref e.FalseList, whenFalse);
        _state.PatchToHere(e.TrueList ?? []);
        e.TrueList = null;
    }

    /// <summary>The mirror of <see cref="GoIfTrue"/> for <c>or</c> - luaK_goiffalse.</summary>
    private void GoIfFalse(ref Exp e)
    {
        DischargeVars(ref e);
        List<int> whenTrue = e.Kind switch
        {
            ExpKind.Nil or ExpKind.False => [],
            ExpKind.Constant or ExpKind.True => _state.EmitJump(),
            ExpKind.Jump => [.. e.JumpList ?? []],
            _ => JumpOnCondition(ref e, whenTrue: true),
        };
        Concat(ref e.TrueList, whenTrue);
        _state.PatchToHere(e.FalseList ?? []);
        e.FalseList = null;
    }

    /// <summary>
    /// Flips the sense of a test whose jump this expression is, so the jump is taken on the opposite
    /// outcome - invertjump. It rewrites the A operand of the COMPARISON, where A is the result the
    /// comparison is looking for; a TEST never reaches here, since its sense is chosen when it is
    /// emitted.
    /// </summary>
    private List<int> InvertJump(ref Exp e)
    {
        int control = e.Info - 1;
        _state.SetA(control, _state.InstructionA(control) == 0 ? 1 : 0);
        return [.. e.JumpList ?? []];
    }

    /// <summary>
    /// Tests an expression's value and jumps on the outcome asked for - jumponcond. A NOT emitted
    /// for it is taken back and folded into the test's sense instead.
    /// </summary>
    private List<int> JumpOnCondition(ref Exp e, bool whenTrue)
    {
        if (e.Kind == ExpKind.Relocatable
            && e.Info == _state.InstructionCount - 1
            && _state.InstructionOpcode(e.Info) == LuaOpcode.Not)
        {
            int negated = _state.InstructionB(e.Info);
            _state.RemoveLastInstruction();
            _state.EmitABC(LuaOpcode.Test, negated, negated, whenTrue ? 0 : 1);
            return _state.EmitJump();
        }

        int register = ExpressionToAnyRegister(ref e);
        FreeIfTemporary(ref e);
        _state.EmitABC(LuaOpcode.Test, register, register, whenTrue ? 1 : 0);
        return _state.EmitJump();
    }

    /// <summary>
    /// A comparison used as a value: the instruction plus its jump, left as a jump for whoever
    /// consumes it - codecomp. Both operands go to RK left-to-right and are released right-to-left,
    /// and <c>&gt;</c> / <c>&gt;=</c> become LT / LE with the operands exchanged because Lua 5.0 has
    /// no greater-than opcode.
    /// </summary>
    private Exp CompileComparisonValue(
        (LuaOpcode Opcode, bool Swap, bool Invert) shape, LuaBinaryExpr comparison)
    {
        var (opcode, swap, invert) = shape;

        var left = CompileExpression(comparison.Left);
        int leftRk = ExpressionToRk(ref left);
        var right = CompileExpression(comparison.Right);
        int rightRk = ExpressionToRk(ref right);
        FreeIfTemporary(ref right);
        FreeIfTemporary(ref left);
        if (swap) (leftRk, rightRk) = (rightRk, leftRk);

        _state.EmitABC(opcode, invert ? 0 : 1, leftRk, rightRk);
        var jump = _state.EmitJump();
        return new Exp { Kind = ExpKind.Jump, Info = jump[0], JumpList = jump };
    }

    /// <summary>
    /// Whether any jump in a list arrives without having left a value behind.
    ///
    /// TEST both tests and assigns, so a jump it controls carries the value with it and can go
    /// straight to the end. A comparison assigns nothing, so its jump has to land on a LOADBOOL -
    /// and the presence of even one such jump is what makes the pair of LOADBOOLs necessary.
    /// </summary>
    private bool NeedValue(List<int>? list)
    {
        foreach (int jump in list ?? [])
            if (ControlOf(jump) is { } control && _state.InstructionOpcode(control) != LuaOpcode.Test)
                return true;
        return false;
    }

    /// <summary>The instruction a jump is conditional on, when it has one - getjumpcontrol.</summary>
    private int? ControlOf(int jump)
    {
        if (jump < 1) return null;
        var opcode = _state.InstructionOpcode(jump - 1);
        return opcode is LuaOpcode.Eq or LuaOpcode.Lt or LuaOpcode.Le or LuaOpcode.Test
            ? jump - 1
            : null;
    }

    /// <summary>
    /// Points a list at the end of the expression, or at a LOADBOOL for the jumps that need one.
    /// A TEST's destination register is set at the same time, since that is how its jump delivers
    /// the value - patchlistaux with patchtestreg.
    /// </summary>
    private void PatchListAux(List<int>? list, int final, int register, int loadBool)
    {
        foreach (int jump in list ?? [])
        {
            int? control = ControlOf(jump);
            if (control is { } at && _state.InstructionOpcode(at) == LuaOpcode.Test)
            {
                int tested = _state.InstructionB(at);
                _state.SetA(at, register >= 0 ? register : tested);
                _state.PatchTo([jump], final);
            }
            else
            {
                _state.PatchTo([jump], loadBool);
            }
        }
    }

    /// <summary>A LOADBOOL that can be jumped to. <paramref name="skipNext"/> makes it step over the
    /// instruction after it, which is how the false arm avoids falling into the true one.</summary>
    private int CodeLabel(int register, bool value, bool skipNext)
    {
        _state.MarkLabel();
        return _state.EmitABC(LuaOpcode.LoadBool, register, value ? 1 : 0, skipNext ? 1 : 0);
    }

    /// <summary>
    /// How many pending array items are flushed with one SETLIST - LFIELDS_PER_FLUSH.
    /// </summary>
    private const int FieldsPerFlush = 32;

    /// <summary>
    /// A table constructor: NEWTABLE, then the record fields with SETTABLE and the array items in
    /// consecutive registers flushed with SETLIST.
    ///
    /// The sizes go into NEWTABLE's operands only at the end, once both are counted, so the
    /// instruction is emitted blank and patched - which is what the reference does, and the reason
    /// it can be done at all is that the operands are not needed until the table is built.
    /// </summary>
    private Exp CompileTable(LuaTableExpr table)
    {
        int newTable = _state.EmitABC(LuaOpcode.NewTable, 0, 0, 0);
        var constructed = new Exp { Kind = ExpKind.Relocatable, Info = newTable };
        // Fixed at the top of the stack straight away: the collector can run during the
        // constructor, and the table has to be reachable from a register while it is being filled.
        ExpressionToNextRegister(ref constructed);
        int register = constructed.Info;

        int arrayCount = 0, recordCount = 0, pending = 0;
        Exp? heldItem = null;

        foreach (var item in table.Items)
        {
            // closelistfield: the item read last goes to its register now, and a full batch is
            // flushed before another one starts.
            if (heldItem is { } held)
            {
                var value = held;
                ExpressionToNextRegister(ref value);
                heldItem = null;
                if (pending == FieldsPerFlush)
                {
                    EmitSetList(register, arrayCount, pending);
                    pending = 0;
                }
            }

            if (item.Key is { } key)
            {
                // recfield. The registers it borrows are given back afterwards, so a constructor of
                // record fields does not grow the stack with every entry.
                int mark = _state.FreeRegister;
                var keyExp = CompileExpression(key);
                int keyRk = ExpressionToRk(ref keyExp);
                recordCount++;
                var valueExp = CompileExpression(item.Value);
                int valueRk = ExpressionToRk(ref valueExp);
                _state.EmitABC(LuaOpcode.SetTable, register, keyRk, valueRk);
                _state.SetFreeRegister(mark);
            }
            else
            {
                // listfield: held rather than stored, because only the LAST one can turn out to be
                // a call whose results all belong to the table.
                heldItem = CompileExpression(item.Value);
                arrayCount++;
                pending++;
            }
        }

        // lastlistfield
        if (pending > 0)
        {
            if (heldItem is { } last && last.Kind == ExpKind.Call)
            {
                var open = last;
                SetMultipleReturns(ref open);
                EmitSetList(register, arrayCount, multipleReturns: true);
                // The call's own results are not a known count, so it stops being one element.
                arrayCount--;
            }
            else
            {
                if (heldItem is { } value)
                {
                    var stored = value;
                    ExpressionToNextRegister(ref stored);
                }
                EmitSetList(register, arrayCount, pending);
            }
        }

        _state.SetInstructionB(newTable, FloatingByte(arrayCount));
        _state.SetInstructionC(newTable, BitLength(recordCount));
        return new Exp { Kind = ExpKind.NonReloc, Info = register };
    }

    /// <summary>
    /// Stores the pending array items. Bx counts every element placed so far, less one, which is
    /// how one operand carries both which batch this is and how many it holds; the registers the
    /// items occupied are released.
    /// </summary>
    private void EmitSetList(int register, int elements, int pending = 0, bool multipleReturns = false)
    {
        _state.EmitABx(multipleReturns ? LuaOpcode.SetListO : LuaOpcode.SetList,
                       register, elements - 1);
        _state.SetFreeRegister(register + 1);
    }

    /// <summary>
    /// NEWTABLE's array-size operand: a three-bit mantissa with an exponent, rounding up to the
    /// next representable size - Lua's luaO_int2fb.
    ///
    /// Read off the shipped chunks rather than from memory, because 5.0 and 5.1 differ: 8 encodes
    /// as 12, 11 and 12 both as 14, 13 as 15, 18 and 19 both as 21, 26 as 23, and anything under 8
    /// as itself. All 14 distinct counts in the corpus fit this and only this. Lua 5.1 raised the
    /// loop's threshold to 16, which would give 8 for 8 and 17 for 18.
    /// </summary>
    private static int FloatingByte(int size)
    {
        int exponent = 0;
        while (size >= 8)
        {
            size = (size + 1) >> 1;
            exponent++;
        }
        return (exponent << 3) | size;
    }

    /// <summary>
    /// NEWTABLE's record-size operand, which is NOT the same encoding: it is the number of bits the
    /// count needs - luaO_log2 plus one. 5 and 6 both give 3, 2 gives 2, 1 gives 1.
    /// </summary>
    private static int BitLength(int size)
    {
        int bits = 0;
        while (size > 0)
        {
            size >>= 1;
            bits++;
        }
        return bits;
    }

    /// <summary>Lua 5.0's arithmetic and concatenation opcodes. There is no modulo or integer
    /// division - both arrived in 5.1 - so an operator not here is not arithmetic.</summary>
    private static LuaOpcode? ArithmeticFor(string op) => op switch
    {
        "+" => LuaOpcode.Add,
        "-" => LuaOpcode.Sub,
        "*" => LuaOpcode.Mul,
        "/" => LuaOpcode.Div,
        "^" => LuaOpcode.Pow,
        ".." => LuaOpcode.Concat,
        _ => null,
    };

    private Exp CompileArithmetic(LuaOpcode opcode, LuaBinaryExpr expression)
    {
        if (opcode == LuaOpcode.Concat) return CompileConcat(expression);

        // The left operand is committed to an RK slot BEFORE the right is compiled, which is what
        // luaK_infix does and what decides the order operands are evaluated in.
        var left = CompileExpression(expression.Left);
        ExpressionToRk(ref left);

        var right = CompileExpression(expression.Right);
        CodeArithmetic(opcode, ref left, ref right);
        return left;
    }

    /// <summary>
    /// Emits an arithmetic instruction, or folds it away when both operands are literals.
    ///
    /// The right operand is turned into an RK slot before the left, as codearith does - the reverse
    /// order would intern the constants the other way round and every operand in the chunk would
    /// shift. Registers are released highest-first for the same reason.
    /// </summary>
    private void CodeArithmetic(LuaOpcode opcode, ref Exp e1, ref Exp e2)
    {
        int o2 = ExpressionToRk(ref e2);
        int o1 = ExpressionToRk(ref e1);
        if (o1 > o2)
        {
            FreeIfTemporary(ref e1);
            FreeIfTemporary(ref e2);
        }
        else
        {
            FreeIfTemporary(ref e2);
            FreeIfTemporary(ref e1);
        }
        e1.Info = _state.EmitABC(opcode, 0, o1, o2);
        e1.Kind = ExpKind.Relocatable;
    }

    private void FreeIfTemporary(ref Exp e)
    {
        if (e.Kind == ExpKind.NonReloc) _state.FreeRegisterIfTemporary(e.Info);
    }

    /// <summary>
    /// <c>a .. b</c>. CONCAT takes a register RANGE rather than two operands, so both sides have to
    /// be on the stack, and a chain is folded into one instruction by widening the range - which is
    /// why <c>a .. b .. c</c> is one CONCAT over three registers and not two instructions.
    /// </summary>
    private Exp CompileConcat(LuaBinaryExpr expression)
    {
        var left = CompileExpression(expression.Left);
        ExpressionToNextRegister(ref left);

        var right = CompileExpression(expression.Right);
        // `..` is right-associative, so the right side of a chain is itself a concat sitting
        // directly above this operand; extending its range absorbs this one.
        if (right.Kind == ExpKind.Relocatable
            && _state.InstructionOpcode(right.Info) == LuaOpcode.Concat
            && _state.InstructionB(right.Info) == left.Info + 1)
        {
            FreeIfTemporary(ref left);
            _state.SetInstructionB(right.Info, left.Info);
            return right;
        }

        ExpressionToNextRegister(ref right);
        CodeArithmetic(LuaOpcode.Concat, ref left, ref right);
        return left;
    }

    /// <summary>
    /// Unary minus.
    ///
    /// A numeric literal is negated by interning the negated value as a SECOND constant, leaving the
    /// positive one in the pool unreferenced - exactly what luaK_prefix does, and why the shipped
    /// chunks contain no UNM at all despite the sources being full of negative numbers. The dead
    /// constant is not waste to be optimised away: it is part of matching the reference, and a pool
    /// without it shifts every later index.
    /// </summary>
    private Exp CompileNegate(LuaUnaryExpr expression)
    {
        var operand = CompileExpression(expression.Operand);
        if (operand.Kind == ExpKind.Constant && _state.ConstantNumber(operand.Info) is { } value)
        {
            operand.Info = _state.AddNumberConstant(-value);
            return operand;
        }

        ExpressionToAnyRegister(ref operand);
        FreeIfTemporary(ref operand);
        return new Exp
        {
            Kind = ExpKind.Relocatable,
            Info = _state.EmitABC(LuaOpcode.Unm, 0, operand.Info, 0),
        };
    }

    /// <summary>
    /// <c>not x</c> as a VALUE, which is the only place NOT is emitted. In a condition the operator
    /// is folded into the test's sense instead - see CompileCondition - which is why NOT is rare in
    /// the chunks even though `not` appears in 253 of the sources.
    /// </summary>
    private Exp CompileNot(LuaUnaryExpr expression)
    {
        var operand = CompileExpression(expression.Operand);
        DischargeVars(ref operand);
        switch (operand.Kind)
        {
            case ExpKind.Nil or ExpKind.False: return new Exp { Kind = ExpKind.True };
            // Any interned constant here is a number or a string, so it is truthy.
            case ExpKind.True or ExpKind.Constant: return new Exp { Kind = ExpKind.False };
        }

        ExpressionToAnyRegister(ref operand);
        FreeIfTemporary(ref operand);
        return new Exp
        {
            Kind = ExpKind.Relocatable,
            Info = _state.EmitABC(LuaOpcode.Not, 0, operand.Info, 0),
        };
    }

    /// <summary>
    /// Puts an argument list in consecutive registers, reporting whether it ends in a call that was
    /// left open. Only the LAST argument can be - anywhere else a call is cut down to one value,
    /// which is what makes <c>f(g(), h())</c> pass one value from g and all of h's.
    /// </summary>
    private bool CompileArguments(List<LuaExpr> arguments)
    {
        for (int i = 0; i < arguments.Count; i++)
        {
            var value = CompileExpression(arguments[i]);
            if (i == arguments.Count - 1 && SetMultipleReturns(ref value)) return true;
            ExpressionToNextRegister(ref value);
        }
        return false;
    }

    private Exp CompileCall(LuaExpr call, int resultCount)
    {
        int baseRegister = _state.FreeRegister;
        int callPc;

        switch (call)
        {
            case LuaCallExpr direct:
            {
                var target = CompileExpression(direct.Target);
                ExpressionToNextRegister(ref target);
                bool openArguments = CompileArguments(direct.Arguments);
                // An open call takes however many values the last argument produced, which is only
                // known at run time, so B says "everything above A" rather than a count.
                callPc = _state.EmitABC(LuaOpcode.Call, baseRegister,
                                        openArguments ? 0 : direct.Arguments.Count + 1,
                                        resultCount + 1);
                break;
            }

            case LuaMethodCallExpr method:
            {
                // SELF puts the receiver in base+1 and the looked-up function in base, so the
                // method's own argument list starts one register further along.
                var receiver = CompileExpression(method.Receiver);
                int receiverRegister = ExpressionToAnyRegister(ref receiver);
                _state.SetFreeRegister(baseRegister);
                _state.ReserveRegisters(2);
                // The method name goes through the same RK choice as any other operand instead of
                // being assumed to fit as a constant. Past index 261 it does not, and the reference
                // loads it into a register first - which is what `obj:get_first()` needs in a chunk
                // with 391 constants. Encoding it as a constant regardless overflowed C and
                // produced two unrelated register numbers.
                var methodName = new Exp
                {
                    Kind = ExpKind.Constant,
                    Info = _state.AddStringConstant(method.Method),
                };
                int nameRk = ExpressionToRk(ref methodName);
                _state.EmitABC(LuaOpcode.Self, baseRegister, receiverRegister, nameRk);
                FreeIfTemporary(ref methodName);
                bool openMethodArguments = CompileArguments(method.Arguments);
                callPc = _state.EmitABC(LuaOpcode.Call, baseRegister,
                                        openMethodArguments ? 0 : method.Arguments.Count + 2,
                                        resultCount + 1);
                break;
            }

            default:
                Unsupported(call.GetType().Name, call.Span);
                return new Exp { Kind = ExpKind.Nil };
        }

        _state.SetFreeRegister(baseRegister);
        if (resultCount > 0) _state.ReserveRegisters(1);
        // Returned as a call rather than as its register: whoever receives it decides whether it
        // keeps one result or hands over all of them.
        return new Exp { Kind = ExpKind.Call, Info = callPc };
    }

    // --- discharge -----------------------------------------------------------------------------

    /// <summary>Turns a variable reference into something that occupies or will occupy a register.</summary>
    private void DischargeVars(ref Exp e)
    {
        switch (e.Kind)
        {
            case ExpKind.Local:
                e.Kind = ExpKind.NonReloc;
                break;
            case ExpKind.Global:
                e.Info = _state.EmitABx(LuaOpcode.GetGlobal, 0, e.Info);
                e.Kind = ExpKind.Relocatable;
                break;
            case ExpKind.Indexed:
                // BOTH operands are released, the key before the table, as dischargevars does. The
                // key only occupies a register when its constant was too far along the pool to fit
                // the instruction's operand field - past index 261 - which is why this only shows
                // up in a chunk with hundreds of constants: `DialogueSpecs.TALW_R07_x` there loads
                // its key with LOADK, and leaving that register reserved put the result one slot
                // too high.
                if (!LuaInstructionLayout.IsConstant(e.Aux)) _state.FreeRegisterIfTemporary(e.Aux);
                _state.FreeRegisterIfTemporary(e.Info);
                e.Info = _state.EmitABC(LuaOpcode.GetTable, 0, e.Info, e.Aux);
                e.Kind = ExpKind.Relocatable;
                break;
            case ExpKind.Call:
                // Nobody asked for every value, so the call keeps the single result it was emitted
                // with and becomes that register. luaK_setoneret.
                e.Kind = ExpKind.NonReloc;
                e.Info = _state.InstructionA(e.Info);
                break;
        }
    }

    /// <summary>
    /// Makes a call produce every value it has, by rewriting the result count of the instruction
    /// already emitted for it. Lua's luaK_setmultret.
    /// </summary>
    private bool SetMultipleReturns(ref Exp e)
    {
        if (e.Kind != ExpKind.Call) return false;
        _state.SetInstructionC(e.Info, 0);
        return true;
    }

    private void DischargeToRegister(ref Exp e, int register)
    {
        DischargeVars(ref e);
        switch (e.Kind)
        {
            // A jump has no value to move anywhere yet - that is exp2reg's job, not this one.
            case ExpKind.Jump:
                return;
            case ExpKind.Nil:
                _state.LoadNil(register, 1);
                break;
            case ExpKind.True:
            case ExpKind.False:
                _state.EmitABC(LuaOpcode.LoadBool, register, e.Kind == ExpKind.True ? 1 : 0, 0);
                break;
            case ExpKind.Constant:
                _state.EmitABx(LuaOpcode.LoadK, register, e.Info);
                break;
            case ExpKind.Relocatable:
                _state.SetA(e.Info, register);
                break;
            case ExpKind.NonReloc:
                if (e.Info != register) _state.EmitABC(LuaOpcode.Move, register, e.Info, 0);
                break;
        }
        e.Kind = ExpKind.NonReloc;
        e.Info = register;
    }

    private void ExpressionToNextRegister(ref Exp e)
    {
        DischargeVars(ref e);
        if (e.Kind == ExpKind.NonReloc) _state.FreeRegisterIfTemporary(e.Info);
        int register = _state.FreeRegister;
        _state.ReserveRegisters(1);
        ExpressionToRegister(ref e, register);
    }

    /// <summary>
    /// Puts an expression in a given register, materialising a short-circuiting one into an actual
    /// true or false along the way - Lua's exp2reg.
    ///
    /// Everything up to here can leave an expression as a set of jumps; this is where <c>a or b</c>
    /// stops being control flow and becomes a value. Jumps controlled by a TEST already carry the
    /// value, because TEST assigns as it jumps; a comparison's jump carries nothing, so when one of
    /// those is present a pair of LOADBOOLs is emitted for the two outcomes to land on. The first
    /// skips the second, which is why the false arm does not fall into the true one.
    /// </summary>
    private void ExpressionToRegister(ref Exp e, int register)
    {
        DischargeToRegister(ref e, register);
        if (e.Kind == ExpKind.Jump) Concat(ref e.TrueList, e.JumpList);

        if (HasJumps(e))
        {
            int loadFalse = -1, loadTrue = -1;
            if (NeedValue(e.TrueList) || NeedValue(e.FalseList))
            {
                // A jump over the two loads, for the path that fell through with its value already
                // in place. An expression that IS a jump never falls through, so it needs none.
                var skip = e.Kind == ExpKind.Jump ? [] : _state.EmitJump();
                loadFalse = CodeLabel(register, value: false, skipNext: true);
                loadTrue = CodeLabel(register, value: true, skipNext: false);
                _state.PatchToHere(skip);
            }

            _state.MarkLabel();
            int final = _state.InstructionCount;
            PatchListAux(e.FalseList, final, register, loadFalse);
            PatchListAux(e.TrueList, final, register, loadTrue);
        }

        e.TrueList = e.FalseList = e.JumpList = null;
        e.Kind = ExpKind.NonReloc;
        e.Info = register;
    }

    private int ExpressionToAnyRegister(ref Exp e)
    {
        DischargeVars(ref e);
        // A value already in a register still needs work when jumps are outstanding: those jumps
        // have to be given somewhere to land before the register can be read.
        if (e.Kind == ExpKind.NonReloc && !HasJumps(e)) return e.Info;
        ExpressionToNextRegister(ref e);
        return e.Info;
    }

    /// <summary>An operand that may name a constant directly. Only the first 256 constants fit in an
    /// RK operand, so past that the value has to be loaded into a register first.</summary>
    private int ExpressionToRk(ref Exp e)
    {
        DischargeVars(ref e);
        if (e.Kind == ExpKind.Jump || HasJumps(e)) return ExpressionToAnyRegister(ref e);
        int? constant = e.Kind switch
        {
            // nil is the one literal still interned on demand, which is what exp2RK does with VNIL.
            ExpKind.Nil => _state.AddConstant(LuaConstant.Nil.Instance),
            ExpKind.Constant => e.Info,
            _ => null,
        };
        if (constant is { } index && index <= LuaFunctionState.MaxRkConstant)
            return LuaInstructionLayout.EncodeConstant(index);
        return ExpressionToAnyRegister(ref e);
    }

    /// <summary>
    /// <c>if c then B [elseif...] [else E] end</c>. Each clause tests its condition with a jump
    /// taken when the condition is FALSE, so falling through enters the body; a body that is not the
    /// last then jumps past everything after it.
    /// </summary>
    private void CompileIf(LuaIfStat ifStat)
    {
        var exits = new List<int>();

        for (int i = 0; i < ifStat.Clauses.Count; i++)
        {
            var clause = ifStat.Clauses[i];
            var falseJump = CompileCondition(clause.Condition, jumpWhenTrue: false);
            CompileBlock(clause.Body);

            // Only needed when something follows this body; the last clause with no else falls
            // straight out, and emitting a jump to the next instruction is not what the reference
            // does.
            //
            // The order here is ifstat's and it matters: the jump out is emitted BEFORE the false
            // jump is held, so it absorbs whatever the body left waiting - a nested if's own false
            // jump - and those land past the else with it.
            bool somethingFollows = i + 1 < ifStat.Clauses.Count || ifStat.ElseBody is not null;
            if (somethingFollows) exits.AddRange(_state.EmitJump());

            _state.PatchToHere(falseJump);
        }

        if (ifStat.ElseBody is { } elseBody) CompileBlock(elseBody);
        _state.PatchToHere(exits);
    }

    /// <summary>
    /// <c>while c do B end</c>, with the condition at the bottom: an initial forward jump reaches
    /// the test, which jumps back into the body while it holds. That costs one jump on entry and
    /// none per iteration, and it is the shape the reference compiler emits - a top-tested loop
    /// would be correct and byte-wrong.
    /// </summary>
    private void CompileWhile(LuaWhileStat whileStat)
    {
        var entryJump = _state.EmitJump();

        // The condition is generated FIRST and then lifted out, as whilestat does. Its code belongs
        // at the bottom, but its constants have to be interned before the body's: for
        // `while is_alive(x) do wait() end` the reference pool is is_alive, x, wait, and compiling
        // the body first puts wait at K0 - identical instructions, every constant index shifted.
        _state.MarkLabel();
        int conditionStart = _state.InstructionCount;
        var backJump = CompileCondition(whileStat.Condition, jumpWhenTrue: true);
        // A short-circuiting condition - `while a and b do` - leaves the jumps that skip the rest of
        // the test waiting for the instruction after it. That instruction is the loop exit, but the
        // code they sit in is about to be lifted out, so they have to be collected and relocated
        // with it rather than left pointing past the end of the function.
        var exitJumps = _state.TakePendingJumps();
        uint[] conditionCode = _state.ExtractFrom(conditionStart);

        _state.MarkLabel();
        int bodyStart = _state.InstructionCount;
        CompileBlock(whileStat.Body);

        // The lifted condition lands here, so the entry jump targets it and the back jump moves with
        // it. Only the trailing jump needs relocating - a condition containing its own jumps, which
        // `and` and `or` would introduce, is not supported yet.
        _state.PatchToHere(entryJump);
        int conditionBase = _state.InstructionCount;
        int Relocate(int pc) => conditionBase + (pc - conditionStart);
        var relocatedBack = backJump.Select(Relocate).ToList();
        var relocatedExit = exitJumps.Select(Relocate).ToList();
        foreach (uint instruction in conditionCode) _state.Emit(instruction);
        _state.PatchTo(relocatedBack, bodyStart);
        // The short circuits mean the condition failed, so they leave the loop.
        _state.PatchToHere(relocatedExit);
    }

    /// <summary>
    /// Emits a condition and the jump that depends on it, returning the jump's pc to patch.
    ///
    /// A comparison compiles to the comparison opcode itself rather than to a value plus a test:
    /// Lua 5.0 has no GT or GE, so <c>a &gt; b</c> becomes LT with the operands exchanged, and
    /// <c>~=</c> is EQ with the expected result flipped. Anything else is materialised into a
    /// register and tested with TEST, whose A and B are both that register in 5.0 - the opcode still
    /// assigns, which is what 5.1 split out into TESTSET.
    /// </summary>
    /// <summary>
    /// Emits a condition and the jump that depends on it, returning the jumps to patch. Empty when
    /// the condition is constant and needs no jump at all.
    /// </summary>
    private List<int> CompileCondition(LuaExpr condition, bool jumpWhenTrue)
    {
        // Parentheses are transparent to a test. In Lua they are transparent for free, because the
        // true/false lists live on the expression descriptor and survive prefixexp; here the
        // structural cases below match on the tree, so the wrapper has to come off first or
        // `if ( a and b )` would be compiled as a value and `and` has no value form yet.
        while (condition is LuaParenExpr wrapped) condition = wrapped.Inner;

        // A literal condition generates no test. Lua 5.0 folds these in goiftrue/goiffalse, so
        // `while true do` ends in a bare backward JMP with no LOADBOOL and no TEST - emitting the
        // test anyway is correct code and two instructions longer than the reference.
        if (ConstantTruth(condition) is { } always)
            return always == jumpWhenTrue ? _state.EmitJump() : [];

        if (condition is LuaBinaryExpr { Operator: "and" or "or" } logical)
            return CompileLogicalCondition(logical, jumpWhenTrue);

        if (condition is LuaBinaryExpr binary && ComparisonFor(binary.Operator) is { } comparison)
        {
            var (opcode, swap, invert) = comparison;

            // Operands are evaluated left then right and released right then left, as codecomp
            // does; the exchange for > and >= swaps the already-computed operands rather than
            // re-evaluating them the other way round.
            var left = CompileExpression(binary.Left);
            int leftRk = ExpressionToRk(ref left);
            var right = CompileExpression(binary.Right);
            int rightRk = ExpressionToRk(ref right);
            if (right.Kind == ExpKind.NonReloc) _state.FreeRegisterIfTemporary(right.Info);
            if (left.Kind == ExpKind.NonReloc) _state.FreeRegisterIfTemporary(left.Info);
            if (swap) (leftRk, rightRk) = (rightRk, leftRk);

            int expected = (jumpWhenTrue ^ invert) ? 1 : 0;
            _state.EmitABC(opcode, expected, leftRk, rightRk);
            return _state.EmitJump();
        }

        var value = CompileExpression(condition);
        DischargeVars(ref value);

        // A NOT emitted for this condition is taken back and folded into the test's sense, which is
        // what jumponcond does. It works on the INSTRUCTION rather than on the syntax, and that
        // distinction is the whole point: `if (not move2D)` is a parenthesised expression, so a
        // check for a negation in the tree never sees it, while the NOT sitting at the top of the
        // code is as plain either way.
        if (value.Kind == ExpKind.Relocatable
            && value.Info == _state.InstructionCount - 1
            && _state.InstructionOpcode(value.Info) == LuaOpcode.Not)
        {
            int negated = _state.InstructionB(value.Info);
            _state.RemoveLastInstruction();
            _state.EmitABC(LuaOpcode.Test, negated, negated, jumpWhenTrue ? 0 : 1);
            return _state.EmitJump();
        }

        int register = ExpressionToAnyRegister(ref value);
        _state.FreeRegisterIfTemporary(register);
        _state.EmitABC(LuaOpcode.Test, register, register, jumpWhenTrue ? 1 : 0);
        return _state.EmitJump();
    }

    /// <summary>
    /// <c>a and b</c> / <c>a or b</c> as a condition, short-circuiting.
    ///
    /// Only one of the two operands' jumps survives as the result; the other operand's jumps are the
    /// short circuit and land here, on the instruction after this condition. That handing-over is
    /// what luaK_infix and luaK_posfix do between them - goiftrue for <c>and</c>, goiffalse for
    /// <c>or</c>, then the surviving list concatenated - and holding the short-circuit jumps rather
    /// than resolving them is what lets the jump that follows absorb them.
    ///
    /// The four cases are the two operators times the two senses a condition can be asked for. An
    /// <c>and</c> wanted TRUE still needs its left operand's FALSE jump, because a false left means
    /// the whole thing is false and the true jump must not be taken - which is why this cannot just
    /// recurse with the same sense on both sides.
    /// </summary>
    private List<int> CompileLogicalCondition(LuaBinaryExpr logical, bool jumpWhenTrue)
    {
        bool isAnd = logical.Operator == "and";

        // The left operand is tested for the sense that lets the right one decide: `and` carries on
        // only while the left is true, `or` only while it is false.
        var shortCircuit = CompileCondition(logical.Left, jumpWhenTrue: !isAnd);

        // Taking the short circuit means the answer is already known. When that answer is the one
        // being jumped on, those jumps ARE part of the result; otherwise they skip the right
        // operand entirely and land after it.
        bool shortCircuitDecides = isAnd != jumpWhenTrue;

        var fromRight = CompileCondition(logical.Right, jumpWhenTrue);
        if (shortCircuitDecides)
        {
            fromRight.AddRange(shortCircuit);
            return fromRight;
        }

        _state.PatchToHere(shortCircuit);
        return fromRight;
    }

    /// <summary>
    /// Whether an expression is a literal whose truth is known at compile time. Everything except
    /// nil and false is true in Lua, so a number or a string literal counts as a true condition.
    /// Anything that has to be evaluated returns null.
    /// </summary>
    private static bool? ConstantTruth(LuaExpr expression) => expression switch
    {
        LuaNilExpr => false,
        LuaBoolExpr b => b.Value,
        LuaNumberExpr or LuaStringExpr => true,
        // Parentheses change how many values an expression yields, not whether it is true.
        LuaParenExpr paren => ConstantTruth(paren.Inner),
        LuaUnaryExpr { Operator: "not" } negation => ConstantTruth(negation.Operand) is { } inner
            ? !inner
            : null,
        _ => null,
    };

    /// <summary>The opcode a comparison compiles to, whether its operands exchange, and whether the
    /// expected result flips. Lua 5.0 provides only EQ, LT and LE.</summary>
    private static (LuaOpcode Opcode, bool Swap, bool Invert)? ComparisonFor(string op) => op switch
    {
        "==" => (LuaOpcode.Eq, false, false),
        "~=" => (LuaOpcode.Eq, false, true),
        "<" => (LuaOpcode.Lt, false, false),
        "<=" => (LuaOpcode.Le, false, false),
        ">" => (LuaOpcode.Lt, true, false),
        ">=" => (LuaOpcode.Le, true, false),
        _ => null,
    };

    private void Unsupported(string what, LuaSpan span) =>
        _errors.Add(new LuaCompileError($"not implemented yet: {what}", span));
}
