using System.Globalization;
using System.Text;

namespace ReLunacy.Engine.Rendering.Vulkan;

/// <summary>
/// RSX (NV47) fragment microcode -> GLSL, so a class-0x13 material draws with the game's own program
/// instead of the editor's generic one (Docs/reverse/NEW_ENGINE_EMBEDDED_PROGRAMS_2026-10-07.md).
///
/// Encoding: 16-byte instructions, each word (b2 &lt;&lt; 24) | (b3 &lt;&lt; 16) | (b0 &lt;&lt; 8) | b1; a source of
/// type 2 is followed by 16 bytes of inline float constant. Field layout and semantics follow RPCS3's
/// FragmentProgramDecompiler (RSXFragmentProgram.h): src0 bit 29 = abs, bit 30 = the condition register a
/// SETCOND writes, bit 31 = the one a conditional instruction tests; src1 bits 19-21 = src0's input clamp,
/// 28-30 = result scale; IFE else / end offsets are in 4-byte words. Measured on the 204 distinct QForce
/// embedded programs: src0 bit 29 is set on 125 unconditional instructions (so it is abs, not a condition
/// register), and the IFE end offsets land on instruction boundaries.
///
/// The output is a function body: `vec4 rsxProgram()` reading the interpolants (WPOS, TEX0..TEX9),
/// the texture functions rsxTex0..rsxTex15 and the patch-group vectors G0..G22, all supplied by the caller.
/// h and r registers are kept apart, like RPCS3 before register aliasing and like the reverse tools: the
/// Cg-compiled programs never read a register through the other view.
/// </summary>
public static class RsxFragmentTranslator
{
    public sealed class Result
    {
        public required string Body { get; init; }
        public required IReadOnlySet<int> TextureUnits { get; init; }
        public required IReadOnlySet<string> Inputs { get; init; }
        public required IReadOnlySet<int> Groups { get; init; }
        public required int InstructionCount { get; init; }
    }

    private const string Swizzle = "xyzw";

    private static readonly string[] InputNames =
        ["WPOS", "COL0", "COL1", "FOGC", "TEX0", "TEX1", "TEX2", "TEX3", "TEX4", "TEX5", "TEX6", "TEX7", "TEX8", "TEX9", "SSFC", "IN15"];

    private readonly record struct Instruction(int Offset, int Index, uint D0, uint S0, uint S1, uint S2, int Op, float[]? Constant);

    /// <summary>
    /// Translates one program. <paramref name="slotGroups"/> maps a constant slot (byte offset / 16) to its
    /// patch group; those constants read G&lt;group&gt; instead of their stored value. <paramref name="exports32"/>
    /// is the shader-control 32-bit-exports bit: r0 is then the colour, else h0. Null with a reason when the
    /// program uses something the translator does not implement.
    /// </summary>
    public static Result? Translate(ReadOnlySpan<byte> microcode, IReadOnlyDictionary<int, int> slotGroups, bool exports32, out string? failure)
    {
        failure = null;
        var program = Decode(microcode);
        if (program.Count == 0 || (program[^1].D0 & 1) == 0)
        {
            failure = "no end instruction";
            return null;
        }

        var body = new StringBuilder();
        var registers = new SortedSet<string>(StringComparer.Ordinal);
        var units = new SortedSet<int>();
        var inputs = new SortedSet<string>(StringComparer.Ordinal);
        var groups = new SortedSet<int>();
        var elseOffsets = new List<int>();
        var endOffsets = new List<int>();
        int depth = 1;

        void Line(string text) => body.Append(' ', 4 * depth).Append(text).Append('\n');

        foreach (Instruction ins in program)
        {
            // RPCS3 order: close the blocks that end here, then open the else of the ones that branch here.
            for (int at = endOffsets.IndexOf(ins.Offset); at >= 0; at = endOffsets.IndexOf(ins.Offset))
            {
                endOffsets.RemoveAt(at);
                depth--;
                Line("}");
            }
            for (int at = elseOffsets.IndexOf(ins.Offset); at >= 0; at = elseOffsets.IndexOf(ins.Offset))
            {
                elseOffsets.RemoveAt(at);
                depth--;
                Line("} else {");
                depth++;
            }

            string? error = Emit(ins, Line, registers, units, inputs, groups, slotGroups, elseOffsets, endOffsets, ref depth);
            // Debugging: RELUNACY_RSX_DEBUG_AT=<index> returns RELUNACY_RSX_DEBUG_REG right after that instruction.
            if (int.TryParse(Environment.GetEnvironmentVariable("RELUNACY_RSX_DEBUG_AT"), out int stopAt) && stopAt == ins.Index &&
                Environment.GetEnvironmentVariable("RELUNACY_RSX_DEBUG_REG") is { Length: > 0 } stopRegister)
            {
                registers.Add(stopRegister);
                Line($"return {stopRegister};");
            }
            if (error != null)
            {
                failure = $"{error} at +0x{ins.Offset:X}";
                return null;
            }
        }
        while (depth > 1)
        {
            depth--;
            Line("}");
        }

        var text = new StringBuilder();
        text.Append("vec4 rsxProgram() {\n");
        foreach (string reg in registers)
            text.Append("    vec4 ").Append(reg).Append(" = vec4(0.0);\n");
        text.Append("    vec4 cc0 = vec4(0.0);\n    vec4 cc1 = vec4(0.0);\n");
        text.Append(body);
        string output = exports32 ? "r0" : "h0";
        // Debugging: RELUNACY_RSX_DEBUG_REG=<register> returns that register instead of the colour.
        if (Environment.GetEnvironmentVariable("RELUNACY_RSX_DEBUG_REG") is { Length: > 0 } debugRegister)
            output = debugRegister;
        text.Append("    return ").Append(registers.Contains(output) ? output : "vec4(0.0)").Append(";\n}\n");
        return new Result
        {
            Body = text.ToString(),
            TextureUnits = units,
            Inputs = inputs,
            Groups = groups,
            InstructionCount = program.Count,
        };
    }

    private static List<Instruction> Decode(ReadOnlySpan<byte> bytes)
    {
        var list = new List<Instruction>();
        int offset = 0;
        while (offset + 16 <= bytes.Length && list.Count < 4096)
        {
            uint d0 = Word(bytes, offset), s0 = Word(bytes, offset + 4), s1 = Word(bytes, offset + 8), s2 = Word(bytes, offset + 12);
            int op = Bits(d0, 24, 6) | (Bits(s1, 31, 1) << 6);
            bool inline = Bits(s0, 0, 2) == 2 || Bits(s1, 0, 2) == 2 || Bits(s2, 0, 2) == 2;
            float[]? constant = null;
            if (inline)
            {
                if (offset + 32 > bytes.Length)
                    break;
                constant = new float[4];
                for (int k = 0; k < 4; k++)
                    constant[k] = BitConverter.UInt32BitsToSingle(Word(bytes, offset + 16 + 4 * k));
            }
            list.Add(new Instruction(offset, list.Count, d0, s0, s1, s2, op, constant));
            offset += inline ? 32 : 16;
            if ((d0 & 1) != 0)
                break;
        }
        return list;
    }

    private static string? Emit(
        in Instruction ins,
        Action<string> line,
        SortedSet<string> registers,
        SortedSet<int> units,
        SortedSet<string> inputs,
        SortedSet<int> groups,
        IReadOnlyDictionary<int, int> slotGroups,
        List<int> elseOffsets,
        List<int> endOffsets,
        ref int depth)
    {
        int op = ins.Op;
        Instruction self = ins;
        string Src(int k) => Source(self, k, registers, inputs, groups, slotGroups);

        switch (op)
        {
            case 0x00: // NOP
            case 0x3D: // FENCT
            case 0x3E: // FENCB
                return null;
            case 0x42: // IFE
            {
                line($"if ({Condition(ins)}) {{");
                depth++;
                int elseAt = (int)(ins.S1 & 0x7FFFFFFF) << 2;
                int endAt = (int)(ins.S2 & 0x7FFFFFFF) << 2;
                if (elseAt != endAt)
                    elseOffsets.Add(elseAt);
                endOffsets.Add(endAt);
                return null;
            }
            case 0x12: // KIL
            {
                string cond = Condition(ins);
                line(cond == "true" ? "discard;" : $"if ({cond}) discard;");
                return null;
            }
            case 0x40: case 0x41: case 0x43: case 0x44: case 0x45:
                return $"flow opcode 0x{op:X2}";
        }

        string? e = op switch
        {
            0x01 => Src(0),
            0x02 => $"{Src(0)} * {Src(1)}",
            0x03 => $"{Src(0)} + {Src(1)}",
            0x04 => $"{Src(0)} * {Src(1)} + {Src(2)}",
            0x05 => $"vec4(dot(({Src(0)}).xyz, ({Src(1)}).xyz))",
            0x06 => $"vec4(dot({Src(0)}, {Src(1)}))",
            0x07 => $"rsxDst({Src(0)}, {Src(1)})",
            0x08 => $"min({Src(0)}, {Src(1)})",
            0x09 => $"max({Src(0)}, {Src(1)})",
            0x0A => $"vec4(lessThan({Src(0)}, {Src(1)}))",
            0x0B => $"vec4(greaterThanEqual({Src(0)}, {Src(1)}))",
            0x0C => $"vec4(lessThanEqual({Src(0)}, {Src(1)}))",
            0x0D => $"vec4(greaterThan({Src(0)}, {Src(1)}))",
            0x0E => $"vec4(notEqual({Src(0)}, {Src(1)}))",
            0x0F => $"vec4(equal({Src(0)}, {Src(1)}))",
            0x10 => $"fract({Src(0)})",
            0x11 => $"floor({Src(0)})",
            0x15 => $"dFdx({Src(0)})",
            0x16 => $"dFdy({Src(0)})",
            0x17 => Texture(ins, Src(0), units),
            0x18 => Texture(ins, $"rsxProject({Src(0)})", units),
            // TXL / TXB (Tools of Destruction's cube reflection: lod = props.r x fade): the editor's
            // texture functions sample with the hardware's own level, the lod / bias is not applied.
            0x2F or 0x31 => Texture(ins, Src(0), units),
            0x1A => $"vec4(1.0 / ({Src(0)}).x)",
            0x1B => $"vec4(inversesqrt(abs(({Src(0)}).x)))",
            0x1C => $"vec4(exp2(({Src(0)}).x))",
            0x1D => $"vec4(log2(max(abs(({Src(0)}).x), 1.0e-37)))",
            0x1E => $"rsxLit({Src(0)})",
            0x1F => $"mix({Src(2)}, {Src(1)}, {Src(0)})",
            0x20 => "vec4(1.0)",
            0x21 => "vec4(0.0)",
            0x22 => $"vec4(cos(({Src(0)}).x))",
            0x23 => $"vec4(sin(({Src(0)}).x))",
            0x26 => $"vec4(pow(({Src(0)}).x, ({Src(1)}).x))",
            0x2E => $"vec4(dot(({Src(0)}).xy, ({Src(1)}).xy) + ({Src(2)}).z)",
            0x38 => $"vec4(dot(({Src(0)}).xy, ({Src(1)}).xy))",
            0x39 => $"rsxNormalize({Src(0)})",
            0x3A => $"{Src(0)} / ({Src(1)}).x",
            0x3B => $"{Src(0)} / sqrt(({Src(1)}).x)",
            _ => null,
        };
        if (e == null)
            return $"opcode 0x{op:X2}";

        // Texture samples expanded to [-1, 1] by the instruction itself (RPCS3 exp_tex / _bx2).
        if (Bits(ins.D0, 21, 1) == 1 && op is 0x17 or 0x18 or 0x2F or 0x31)
            e = $"({e}) * 2.0 - 1.0";
        string scaled = Bits(ins.S1, 28, 3) switch
        {
            1 => $"({e}) * 2.0",
            2 => $"({e}) * 4.0",
            3 => $"({e}) * 8.0",
            5 => $"({e}) * 0.5",
            6 => $"({e}) * 0.25",
            7 => $"({e}) * 0.125",
            _ => e,
        };
        string value = Bits(ins.D0, 31, 1) == 1 ? $"clamp({scaled}, 0.0, 1.0)" : Clamp(scaled, Bits(ins.D0, 22, 2));
        string temp = $"t{ins.Index}";
        line($"vec4 {temp} = {value};");

        string mask = "";
        for (int c = 0; c < 4; c++)
            if (Bits(ins.D0, 9 + c, 1) == 1)
                mask += Swizzle[c];
        if (mask.Length == 0)
            return null;

        bool noDest = Bits(ins.D0, 30, 1) == 1;
        bool setCond = Bits(ins.D0, 8, 1) == 1;
        string ccSet = $"cc{Bits(ins.S0, 30, 1)}";
        string target = noDest ? ccSet : ((Bits(ins.D0, 7, 1) == 1 ? "h" : "r") + Bits(ins.D0, 1, 6));
        if (!noDest)
            registers.Add(target);

        WriteMasked(ins, line, target, temp, mask);
        if (setCond && !noDest)
            line($"{ccSet}.{mask} = {target}.{mask};");
        return null;
    }

    private static void WriteMasked(in Instruction ins, Action<string> line, string target, string value, string mask)
    {
        int lt = Bits(ins.S0, 18, 1), eq = Bits(ins.S0, 19, 1), gr = Bits(ins.S0, 20, 1);
        if (lt == 1 && eq == 1 && gr == 1)
        {
            line($"{target}.{mask} = {value}.{mask};");
            return;
        }
        if (lt == 0 && eq == 0 && gr == 0)
            return;
        string cc = $"cc{Bits(ins.S0, 31, 1)}";
        foreach (char c in mask)
        {
            int i = Swizzle.IndexOf(c);
            char tested = Swizzle[Bits(ins.S0, 21 + 2 * i, 2)];
            line($"if ({Test($"{cc}.{tested}", lt, eq, gr)}) {target}.{c} = {value}.{c};");
        }
    }

    private static string Condition(in Instruction ins)
    {
        int lt = Bits(ins.S0, 18, 1), eq = Bits(ins.S0, 19, 1), gr = Bits(ins.S0, 20, 1);
        if (lt == 1 && eq == 1 && gr == 1)
            return "true";
        if (lt == 0 && eq == 0 && gr == 0)
            return "false";
        string cc = $"cc{Bits(ins.S0, 31, 1)}";
        var parts = new string[4];
        for (int i = 0; i < 4; i++)
            parts[i] = Test($"{cc}.{Swizzle[Bits(ins.S0, 21 + 2 * i, 2)]}", lt, eq, gr);
        return "(" + string.Join(" || ", parts.Distinct()) + ")";
    }

    private static string Test(string x, int lt, int eq, int gr) => (lt, eq, gr) switch
    {
        (1, 0, 0) => $"{x} < 0.0",
        (0, 1, 0) => $"{x} == 0.0",
        (0, 0, 1) => $"{x} > 0.0",
        (1, 1, 0) => $"{x} <= 0.0",
        (0, 1, 1) => $"{x} >= 0.0",
        _ => $"{x} != 0.0",
    };

    private static string Texture(in Instruction ins, string coordinate, SortedSet<int> units)
    {
        int unit = Bits(ins.D0, 17, 4);
        units.Add(unit);
        return $"rsxTex{unit}({coordinate})";
    }

    private static string Source(
        in Instruction ins,
        int k,
        SortedSet<string> registers,
        SortedSet<string> inputs,
        SortedSet<int> groups,
        IReadOnlyDictionary<int, int> slotGroups)
    {
        uint s = k == 0 ? ins.S0 : k == 1 ? ins.S1 : ins.S2;
        string text;
        switch (Bits(s, 0, 2))
        {
            case 0:
                text = (Bits(s, 8, 1) == 1 ? "h" : "r") + Bits(s, 2, 6);
                registers.Add(text);
                break;
            case 1:
                text = InputNames[Bits(ins.D0, 13, 4)];
                inputs.Add(text);
                break;
            case 2:
                int slot = ins.Offset / 16 + 1;
                if (slotGroups.TryGetValue(slot, out int group))
                {
                    groups.Add(group);
                    text = $"G{group}";
                }
                else
                {
                    float[] c = ins.Constant!;
                    text = $"vec4({Number(c[0])}, {Number(c[1])}, {Number(c[2])}, {Number(c[3])})";
                }
                break;
            default:
                text = "vec4(0.0)";
                break;
        }

        string swizzle = "";
        for (int i = 0; i < 4; i++)
            swizzle += Swizzle[Bits(s, 9 + 2 * i, 2)];
        if (swizzle != "xyzw")
            text += "." + swizzle;
        if (k == 0 ? Bits(s, 29, 1) == 1 : Bits(s, 18, 1) == 1)
            text = $"abs({text})";
        if (k == 0)
            text = Clamp(text, Bits(ins.S1, 19, 3));
        if (Bits(s, 17, 1) == 1)
            text = $"(-{text})";
        return text;
    }

    /// <summary>RPCS3 ClampValue: 1 fp16, 2 fx12, 3 fx9, 4 saturate; 0 and 5 leave the value alone.</summary>
    private static string Clamp(string code, int precision) => precision switch
    {
        1 => $"clamp({code}, -65504.0, 65504.0)",
        2 => $"clamp({code}, -2.0, 2.0)",
        3 => $"clamp({code}, -1.0, 1.0)",
        4 => $"clamp({code}, 0.0, 1.0)",
        _ => code,
    };

    private static string Number(float v)
    {
        if (float.IsNaN(v)) return "0.0";
        if (float.IsPositiveInfinity(v)) return "3.0e38";
        if (float.IsNegativeInfinity(v)) return "-3.0e38";
        string s = v.ToString("R", CultureInfo.InvariantCulture);
        if (!s.Contains('.') && !s.Contains('E') && !s.Contains('e'))
            s += ".0";
        return s;
    }

    private static uint Word(ReadOnlySpan<byte> b, int o) =>
        ((uint)b[o + 2] << 24) | ((uint)b[o + 3] << 16) | ((uint)b[o] << 8) | b[o + 1];

    private static int Bits(uint value, int low, int count) => (int)((value >> low) & ((1u << count) - 1u));
}
