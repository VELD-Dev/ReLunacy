using System.Text;

namespace ReLunacy.Engine.Scripting;

/// <summary>
/// Renders a chunk as a readable listing: one line per instruction with operands resolved against
/// the prototype's own constant table, so <c>GETGLOBAL 0 3</c> reads as <c>GETGLOBAL R0 "wait"</c>.
///
/// This is the surface the compiler is debugged against. Once step 5 compiles the 524 shipped
/// sources and byte-compares them, a mismatch is a byte offset - useless on its own. Disassembling
/// both sides turns it into a named instruction difference, which is the whole point of building
/// this before the compiler.
/// </summary>
public static class LuaDisassembler
{
    /// <summary>One rendered instruction.</summary>
    public readonly record struct Line(int Pc, uint Raw, LuaOpcode Opcode, string Text, string? Comment)
    {
        public override string ToString() =>
            $"{Pc,5}  {Raw:X8}  {Text}" + (Comment is null ? "" : $"  ; {Comment}");
    }

    /// <summary>Decodes one prototype's code. Jump targets are resolved to absolute pc, which is what
    /// you actually want when reading control flow.</summary>
    public static List<Line> Disassemble(LuaPrototype proto)
    {
        var lines = new List<Line>(proto.Code.Length);
        for (int pc = 0; pc < proto.Code.Length; pc++)
        {
            var ins = new LuaInstruction(proto.Code[pc]);
            var info = LuaOpcodeInfo.Of(ins.Opcode);
            string operands = FormatOperands(ins, info, proto, pc, out string? comment);
            lines.Add(new Line(pc, ins.Raw, ins.Opcode, $"{info.Name,-9} {operands}".TrimEnd(), comment));
        }
        return lines;
    }

    private static string FormatOperands(
        LuaInstruction ins, LuaOpcodeInfo info, LuaPrototype proto, int pc, out string? comment)
    {
        comment = null;
        switch (info.Mode)
        {
            case LuaOpMode.AsBx:
                // Absolute target: Lua jumps are relative to the NEXT instruction.
                comment = $"to {pc + 1 + ins.SBx}";
                return $"R{ins.A} {ins.SBx:+#;-#;0}";

            case LuaOpMode.ABx:
                switch (ins.Opcode)
                {
                    case LuaOpcode.LoadK or LuaOpcode.GetGlobal or LuaOpcode.SetGlobal:
                        comment = Constant(proto, ins.Bx);
                        return $"R{ins.A} K{ins.Bx}";
                    case LuaOpcode.Closure:
                        comment = ins.Bx < proto.Prototypes.Length
                            ? $"proto #{ins.Bx}, {proto.Prototypes[ins.Bx].UpvalueCount} upvalue(s)"
                            : $"proto #{ins.Bx} OUT OF RANGE";
                        return $"R{ins.A} P{ins.Bx}";
                    case LuaOpcode.SetList or LuaOpcode.SetListO:
                        int batch = ins.Bx - ins.Bx % LuaInstructionLayout.FieldsPerFlush;
                        comment = $"fields {batch + 1}..{ins.Bx + 1}";
                        return $"R{ins.A} {ins.Bx}";
                    default:
                        return $"R{ins.A} {ins.Bx}";
                }

            default:
                string b = info.Rk.HasFlag(LuaRkOperands.B) ? Rk(proto, ins.B, ref comment) : $"R{ins.B}";
                string c = info.Rk.HasFlag(LuaRkOperands.C) ? Rk(proto, ins.C, ref comment) : $"R{ins.C}";
                return ins.Opcode switch
                {
                    // Opcodes that genuinely use only A, or A and B: printing three operands for them
                    // is noise that makes a listing harder to read than it needs to be.
                    LuaOpcode.Close => $"R{ins.A}",
                    LuaOpcode.Return => $"R{ins.A} {ins.B}",
                    LuaOpcode.Move or LuaOpcode.LoadNil or LuaOpcode.Unm or LuaOpcode.Not => $"R{ins.A} {b}",
                    LuaOpcode.GetUpval or LuaOpcode.SetUpval => $"R{ins.A} U{ins.B}"
                        + (ins.B < proto.Upvalues.Length && proto.Upvalues[ins.B] is { } n
                            ? $"  ; {LuaText.FromBytes(n)}" : ""),

                    // The rest carry values, counts or flags in B/C rather than register numbers, so
                    // rendering those as R<n> actively misleads - LOADBOOL's B is the boolean itself.
                    LuaOpcode.LoadBool =>
                        $"R{ins.A} {(ins.B != 0 ? "true" : "false")}{(ins.C != 0 ? " skip" : "")}",
                    // B and C are the array and hash size hints for the new table.
                    LuaOpcode.NewTable => $"R{ins.A} {ins.B} {ins.C}",
                    // B is argument count + 1, C is result count + 1; 0 means "through to top".
                    LuaOpcode.Call or LuaOpcode.TailCall => $"R{ins.A} {ins.B} {ins.C}",
                    // A is the comparison result being tested for, not a register.
                    LuaOpcode.Eq or LuaOpcode.Lt or LuaOpcode.Le => $"{ins.A} {b} {c}",
                    // C is the truthiness the test is looking for.
                    LuaOpcode.Test => $"R{ins.A} R{ins.B} {ins.C}",
                    // C is how many values the iterator call yields.
                    LuaOpcode.TForLoop => $"R{ins.A} {ins.C}",

                    _ => $"R{ins.A} {b} {c}",
                };
        }
    }

    /// <summary>An RK operand: a register, or a constant when the RK bit is set.</summary>
    private static string Rk(LuaPrototype proto, int rk, ref string? comment)
    {
        if (!LuaInstructionLayout.IsConstant(rk)) return $"R{rk}";
        int index = LuaInstructionLayout.ConstantIndex(rk);
        string described = Constant(proto, index);
        comment = comment is null ? described : $"{comment}, {described}";
        return $"K{index}";
    }

    private static string Constant(LuaPrototype proto, int index) =>
        index >= 0 && index < proto.Constants.Length
            ? proto.Constants[index].ToString() ?? "?"
            : $"K{index} OUT OF RANGE";

    /// <summary>A full listing of a chunk: every prototype, depth-first, with its header and code.</summary>
    public static string Dump(LuaChunk chunk, int maxInstructionsPerProto = int.MaxValue)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"-- {chunk.Header}");
        DumpProto(sb, chunk.Main, "main", maxInstructionsPerProto);
        return sb.ToString();
    }

    private static void DumpProto(StringBuilder sb, LuaPrototype p, string path, int max)
    {
        sb.AppendLine();
        sb.AppendLine($"function {path}  params={p.ParameterCount} vararg={p.IsVararg} " +
                      $"stack={p.MaxStackSize} upvals={p.UpvalueCount} " +
                      $"consts={p.Constants.Length} protos={p.Prototypes.Length} code={p.Code.Length}");
        var lines = Disassemble(p);
        foreach (var line in lines.Take(max)) sb.AppendLine("  " + line);
        if (lines.Count > max) sb.AppendLine($"  ... {lines.Count - max} more instruction(s)");

        for (int i = 0; i < p.Prototypes.Length; i++)
            DumpProto(sb, p.Prototypes[i], $"{path}.{i}", max);
    }
}
