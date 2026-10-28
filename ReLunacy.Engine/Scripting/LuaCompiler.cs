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
        var value = CompileExpression(assign.Values[0]);

        switch (target)
        {
            case LuaNameExpr name when _state.FindLocal(name.Name) is var register && register >= 0:
                // Assigning to a local materialises the value straight into its register.
                DischargeToRegister(ref value, register);
                break;

            case LuaNameExpr name:
            {
                int source = ExpressionToAnyRegister(ref value);
                _state.EmitABx(LuaOpcode.SetGlobal, source, _state.AddStringConstant(name.Name));
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
        foreach (var value in ret.Values)
        {
            var expression = CompileExpression(value);
            ExpressionToNextRegister(ref expression);
        }
        _state.EmitABC(LuaOpcode.Return, baseRegister, ret.Values.Count + 1, 0);
        _state.SetFreeRegister(baseRegister);
    }

    // --- expressions ---------------------------------------------------------------------------

    private enum ExpKind { Nil, True, False, Number, String, Local, Global, Indexed, Relocatable, NonReloc }

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

    private Exp CompileCall(LuaExpr call, int resultCount)
    {
        int baseRegister = _state.FreeRegister;

        switch (call)
        {
            case LuaCallExpr direct:
            {
                var target = CompileExpression(direct.Target);
                ExpressionToNextRegister(ref target);
                foreach (var argument in direct.Arguments)
                {
                    var value = CompileExpression(argument);
                    ExpressionToNextRegister(ref value);
                }
                _state.EmitABC(LuaOpcode.Call, baseRegister, direct.Arguments.Count + 1, resultCount + 1);
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
                               _state.AddStringConstant(method.Method) | LuaInstructionLayout.BitRk);
                foreach (var argument in method.Arguments)
                {
                    var value = CompileExpression(argument);
                    ExpressionToNextRegister(ref value);
                }
                _state.EmitABC(LuaOpcode.Call, baseRegister, method.Arguments.Count + 2, resultCount + 1);
                break;
            }

            default:
                Unsupported(call.GetType().Name, call.Span);
                return new Exp { Kind = ExpKind.Nil };
        }

        _state.SetFreeRegister(baseRegister);
        if (resultCount > 0) _state.ReserveRegisters(1);
        return new Exp { Kind = ExpKind.NonReloc, Info = baseRegister };
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
        }
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
        if (constant is { } index && index < LuaFunctionState.MaxRkConstant)
            return index | LuaInstructionLayout.BitRk;
        return ExpressionToAnyRegister(ref e);
    }

    private void Unsupported(string what, LuaSpan span) =>
        _errors.Add(new LuaCompileError($"not implemented yet: {what}", span));
}
