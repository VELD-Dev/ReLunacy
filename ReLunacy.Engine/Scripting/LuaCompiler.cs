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
        foreach (var statement in block.Statements) CompileStatement(statement);
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

        var target = assign.Targets[0];

        // The target is resolved first, which matters for more than ordering of work: Lua 5.0's
        // exprstat parses the left-hand side before it sees the '=', and singlevar interns a
        // global's name as it resolves it. So in `pterodactyl = moby_handle:new(0)` the reference
        // compiler's constant table starts with "pterodactyl", not "moby_handle". Compiling the
        // value first produced correct code with every constant index shifted.
        int targetGlobal = target is LuaNameExpr targetName && _state.FindLocal(targetName.Name) < 0
            ? _state.AddStringConstant(targetName.Name)
            : -1;

        var value = CompileExpression(assign.Values[0]);

        switch (target)
        {
            case LuaNameExpr name when _state.FindLocal(name.Name) is var register && register >= 0:
                // Assigning to a local materialises the value straight into its register.
                DischargeToRegister(ref value, register);
                break;

            case LuaNameExpr:
            {
                int source = ExpressionToAnyRegister(ref value);
                _state.EmitABx(LuaOpcode.SetGlobal, source, targetGlobal);
                _state.FreeRegisterIfTemporary(source);
                break;
            }

            case LuaIndexExpr index:
            {
                var table = CompileExpression(index.Target);
                int tableRegister = ExpressionToAnyRegister(ref table);
                var key = CompileExpression(index.Key);
                int keyRk = ExpressionToRk(ref key);
                int valueRk = ExpressionToRk(ref value);
                _state.EmitABC(LuaOpcode.SetTable, tableRegister, keyRk, valueRk);
                break;
            }

            default:
                Unsupported($"assignment to {target.GetType().Name}", assign.Span);
                break;
        }
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
        Nil, True, False, Number, String, Local, Global, Indexed, Relocatable, NonReloc,
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
        public double Number;
        public string Text;
    }

    private Exp CompileExpression(LuaExpr expression)
    {
        switch (expression)
        {
            case LuaNilExpr: return new Exp { Kind = ExpKind.Nil };
            case LuaBoolExpr b: return new Exp { Kind = b.Value ? ExpKind.True : ExpKind.False };
            case LuaNumberExpr n: return new Exp { Kind = ExpKind.Number, Number = n.Value };
            case LuaStringExpr s: return new Exp { Kind = ExpKind.String, Text = s.Value };

            case LuaNameExpr name:
            {
                int register = _state.FindLocal(name.Name);
                return register >= 0
                    ? new Exp { Kind = ExpKind.Local, Info = register }
                    : new Exp { Kind = ExpKind.Global, Info = _state.AddStringConstant(name.Name), Text = name.Name };
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

            case LuaParenExpr paren:
            {
                // Parentheses truncate to one value, so the inner expression is forced into a
                // single register rather than being left able to yield several.
                var inner = CompileExpression(paren.Inner);
                int register = ExpressionToAnyRegister(ref inner);
                return new Exp { Kind = ExpKind.NonReloc, Info = register };
            }

            default:
                Unsupported(expression.GetType().Name, expression.Span);
                return new Exp { Kind = ExpKind.Nil };
        }
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
                _state.EmitABC(LuaOpcode.Self, baseRegister, receiverRegister,
                               LuaInstructionLayout.EncodeConstant(_state.AddStringConstant(method.Method)));
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
            case ExpKind.Nil:
                _state.EmitABC(LuaOpcode.LoadNil, register, register, 0);
                break;
            case ExpKind.True:
            case ExpKind.False:
                _state.EmitABC(LuaOpcode.LoadBool, register, e.Kind == ExpKind.True ? 1 : 0, 0);
                break;
            case ExpKind.Number:
                _state.EmitABx(LuaOpcode.LoadK, register, _state.AddNumberConstant(e.Number));
                break;
            case ExpKind.String:
                _state.EmitABx(LuaOpcode.LoadK, register, _state.AddStringConstant(e.Text));
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
        DischargeToRegister(ref e, register);
    }

    private int ExpressionToAnyRegister(ref Exp e)
    {
        DischargeVars(ref e);
        if (e.Kind == ExpKind.NonReloc) return e.Info;
        ExpressionToNextRegister(ref e);
        return e.Info;
    }

    /// <summary>An operand that may name a constant directly. Only the first 256 constants fit in an
    /// RK operand, so past that the value has to be loaded into a register first.</summary>
    private int ExpressionToRk(ref Exp e)
    {
        DischargeVars(ref e);
        int? constant = e.Kind switch
        {
            ExpKind.Nil => _state.AddConstant(LuaConstant.Nil.Instance),
            ExpKind.Number => _state.AddNumberConstant(e.Number),
            ExpKind.String => _state.AddStringConstant(e.Text),
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
        int conditionStart = _state.InstructionCount;
        var backJump = CompileCondition(whileStat.Condition, jumpWhenTrue: true);
        uint[] conditionCode = _state.ExtractFrom(conditionStart);

        int bodyStart = _state.InstructionCount;
        CompileBlock(whileStat.Body);

        // The lifted condition lands here, so the entry jump targets it and the back jump moves with
        // it. Only the trailing jump needs relocating - a condition containing its own jumps, which
        // `and` and `or` would introduce, is not supported yet.
        _state.PatchToHere(entryJump);
        int conditionBase = _state.InstructionCount;
        var relocated = backJump.Select(pc => conditionBase + (pc - conditionStart)).ToList();
        foreach (uint instruction in conditionCode) _state.Emit(instruction);
        _state.PatchTo(relocated, bodyStart);
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
        // A literal condition generates no test. Lua 5.0 folds these in goiftrue/goiffalse, so
        // `while true do` ends in a bare backward JMP with no LOADBOOL and no TEST - emitting the
        // test anyway is correct code and two instructions longer than the reference.
        if (ConstantTruth(condition) is { } always)
            return always == jumpWhenTrue ? _state.EmitJump() : [];

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
        int register = ExpressionToAnyRegister(ref value);
        _state.FreeRegisterIfTemporary(register);
        _state.EmitABC(LuaOpcode.Test, register, register, jumpWhenTrue ? 1 : 0);
        return _state.EmitJump();
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
