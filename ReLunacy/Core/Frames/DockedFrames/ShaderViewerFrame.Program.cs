using System.Numerics;
using System.Text.RegularExpressions;
using ReLunacy.Engine.Assets.Interfaces;
using ReLunacy.Engine.Loading.Shaders;
using ReLunacy.Engine.Rendering.Vulkan;
using ReLunacy.Utility.Localization;

namespace ReLunacy.Core.Frames.DockedFrames;

/// <summary>The game programs a shader runs: a class-0x13 shader's own bundle (0x33001 / 0x33002, one program per route), or
/// for a standard shader the EBOOT program its feature bits and render mode select (NewEngineStandardShaders, one per table
/// block). Each is translated by the renderer's RsxFragmentTranslator, which is also what the graph reads.</summary>
public sealed partial class ShaderViewerFrame
{
    /// <summary>One program of the shader. <paramref name="Route"/> / <paramref name="K"/> are the renderer's route and vertex
    /// permutation (VulkanRenderer.RsxShaders), which decide what its interpolants hold.</summary>
    private sealed record ProgramInfo(string Label, NewEngineShaderVariant Variant, int Route, int K,
        RsxFragmentTranslator.Result? Result, string? Failure, bool Embedded);

    private List<ProgramInfo>? _programs;
    private int _programIndex;
    private string? _programWhy;

    // VulkanRenderer.RsxShaders routes.
    private const int RouteProbe = 0, RouteBaked = 1, RoutePrelit = 2, RouteStdLightmapped = 3, RouteStdPrelit = 4, RouteStdProbe = 5;

    private List<ProgramInfo> Programs(Shader shader)
    {
        if (_programs != null) return _programs;
        _programs = [];
        _programWhy = null;
        if (shader.isOld || shader.metadataNew is not { } meta) return _programs;

        if (shader.ShaderBundle is { Variants.Count: > 0 } bundle)
        {
            int count = bundle.Variants.Count;
            for (int i = 0; i < count; i++)
            {
                int route = count == 4 ? Math.Max(0, i - 1) : Math.Min(i, 2);
                string label = count == 4 && i == 0
                    ? LM.Get("GUI_Frame_ShaderViewer_Route_Extra")
                    : RouteLabel(route);
                _programs.Add(Translate(label, bundle.Variants[i], route, 0, embedded: true));
            }
            return _programs;
        }

        if (meta.shaderClass != 0)
        {
            _programWhy = LM.Get("GUI_Frame_ShaderViewer_NoProgramClass");
            return _programs;
        }
        if (LunaWindow.Instance.AssetManager?.StandardShaders is not { } table)
        {
            _programWhy = LM.Get("GUI_Frame_ShaderViewer_NoStandardTable");
            return _programs;
        }

        int bits = StandardBits(shader);
        bool translucent = meta.renderingMode is not (0 or 4 or 5);
        foreach (var (block, route) in (ReadOnlySpan<(NewEngineStandardShaders.Block, int)>)
                 [(NewEngineStandardShaders.Block.Probe, RouteStdProbe),
                  (NewEngineStandardShaders.Block.Lightmapped, RouteStdLightmapped),
                  (NewEngineStandardShaders.Block.Prelit, RouteStdPrelit)])
        {
            if (table.Get(block, false, translucent, bits) is not { } variant) continue;
            _programs.Add(Translate(RouteLabel(route), variant, route, (bits >> 2) & 7, embedded: false));
        }
        if (_programs.Count == 0) _programWhy = LM.Get("GUI_Frame_ShaderViewer_NoProgram");
        return _programs;
    }

    /// <summary>The permutation bits of a standard draw: material +0x20 as the loader keeps it (parallax only with the normal
    /// map), detail only with a detail map, masked to the table's 0x1D.</summary>
    private int StandardBits(Shader shader)
    {
        if (EnsureRenderMaterial() is { } material && LunaWindow.Instance.AssetManager is { } assets)
        {
            Vector4 permutation = assets.GetVkMaterialPermutation(material);
            int fromMaterial = (int)(permutation.W + 0.5f);
            if (permutation.Z < 0.5f) fromMaterial &= ~0x10;
            return fromMaterial & 0x1D;
        }
        var meta = shader.metadataNew!.Value;
        int bits = meta.featureFlags;
        if ((bits & 0x08) == 0) bits &= ~0x01;
        if (shader.DetailMap == null) bits &= ~0x10;
        return bits & 0x1D;
    }

    private static string RouteLabel(int route) => route switch
    {
        RouteProbe or RouteStdProbe => LM.Get("GUI_Frame_ShaderViewer_Route_Probe"),
        RouteBaked or RouteStdLightmapped => LM.Get("GUI_Frame_ShaderViewer_Route_Baked"),
        _ => LM.Get("GUI_Frame_ShaderViewer_Route_Prelit"),
    };

    private static ProgramInfo Translate(string label, NewEngineShaderVariant variant, int route, int k, bool embedded)
    {
        try
        {
            var result = RsxFragmentTranslator.Translate(variant.Microcode, variant.SlotGroups(), variant.Exports32, out string? failure);
            return new ProgramInfo(label, variant, route, k, result, failure, embedded);
        }
        catch (Exception e)
        {
            return new ProgramInfo(label, variant, route, k, null, e.Message, embedded);
        }
    }

    private ProgramInfo? SelectedProgram(Shader shader)
    {
        var programs = Programs(shader);
        if (programs.Count == 0) return null;
        _programIndex = Math.Clamp(_programIndex, 0, programs.Count - 1);
        return programs[_programIndex];
    }

    private void ProgramCombo(Shader shader)
    {
        var programs = Programs(shader);
        if (programs.Count == 0) return;
        var current = SelectedProgram(shader)!;
        ImGui.SetNextItemWidth(MathF.Min(260f, ImGui.GetContentRegionAvail().X));
        if (ImGui.BeginCombo(LM.Get("GUI_Frame_ShaderViewer_Variant"), $"{current.Label}  {current.Variant.SkeletonKey}"))
        {
            for (int i = 0; i < programs.Count; i++)
                if (ImGui.Selectable($"{programs[i].Label}  {programs[i].Variant.SkeletonKey}##prog{i}", i == _programIndex))
                {
                    _programIndex = i;
                    _graphDirty = true;
                }
            ImGui.EndCombo();
        }
    }

    private void DrawProgramTab(Shader shader)
    {
        var program = SelectedProgram(shader);
        if (program == null)
        {
            ImGui.TextDisabled(_programWhy ?? LM.Get("GUI_Frame_ShaderViewer_NoProgram"));
            return;
        }
        ProgramCombo(shader);

        if (BeginFields("##program"))
        {
            var v = program.Variant;
            Row(LM.Get("GUI_Frame_ShaderViewer_ProgramKind"), LM.Get(program.Embedded ? "GUI_Frame_ShaderViewer_ProgramEmbedded" : "GUI_Frame_ShaderViewer_ProgramStandard"));
            Row(LM.Get("GUI_Frame_ShaderViewer_Family"), v.SkeletonKey);
            Row(LM.Get("GUI_Frame_ShaderViewer_Tuid"), $"0x{v.Tuid:X16}");
            Row(LM.Get("GUI_Frame_ShaderViewer_Instructions"), $"{v.InstructionCount}");
            Row(LM.Get("GUI_Frame_ShaderViewer_Units"), string.Join(", ", v.TextureUnits.Select(u => $"{u} {UnitLabel(u)}")));
            if (program.Result is { } r)
            {
                Row(LM.Get("GUI_Frame_ShaderViewer_Inputs"), string.Join(", ", r.Inputs));
                Row(LM.Get("GUI_Frame_ShaderViewer_Groups"), string.Join(", ", r.Groups.Select(g => $"G{g} {GroupLabel(g, program.Route)}")));
                if (ReadsClock(program)) Row(LM.Get("GUI_Frame_ShaderViewer_Animated"), LM.Get("GUI_Frame_ShaderViewer_ReadsClock"));
            }
            if (program.Embedded && shader.ShaderBundle is { } bundle && GameplayInput(bundle) is { } source)
                Row(LM.Get("GUI_Frame_ShaderViewer_GameplayInput"), $"0x{source:X2}");
            if (program.Embedded && Family(shader) is var family && family != EmbeddedShader.Generic)
                Row(LM.Get("GUI_Frame_ShaderViewer_Decoded"), FamilyDescription(family));
            ImGui.EndTable();
        }

        if (program.Result == null)
        {
            ImGui.TextColored(new Vector4(1f, 0.6f, 0.3f, 1f), LM.Get("GUI_Frame_ShaderViewer_NotTranslated", program.Failure ?? "?"));
            return;
        }

        ImGui.SeparatorText(LM.Get("GUI_Frame_ShaderViewer_Source"));
        if (ImGui.SmallButton(LM.Get("GUI_Frame_ShaderViewer_Copy"))) ImGui.SetClipboardText(program.Result.Body);
        if (ImGui.BeginChild("##glsl", ImGui.GetContentRegionAvail(), ImGuiChildFlags.Borders, ImGuiWindowFlags.HorizontalScrollbar))
            ImGui.TextUnformatted(program.Result.Body);
        ImGui.EndChild();
    }

    /// <summary>The 0x33003 source feeding patch group 19 (NewEngineShaderBundle.SelectorData), or null.</summary>
    private static int? GameplayInput(NewEngineShaderBundle bundle)
    {
        var data = bundle.SelectorData;
        if (data.Length < 1) return null;
        for (int k = 0; k < data[0] && 2 + 2 * k < data.Length; k++)
            if (data[2 + 2 * k] == 0x13) return data[1 + 2 * k];
        return null;
    }

    private static readonly Regex ClockSwizzle = new(@"\bG7\.[xyzw]*w", RegexOptions.Compiled);

    /// <summary>The program reads the clock: group 15 (.x, the scene time), or group 7's .w on the embedded routes.</summary>
    private static bool ReadsClock(ProgramInfo program) =>
        program.Result is { } r && (r.Groups.Contains(15) && !(program.Route >= RouteStdLightmapped && program.Variant.TextureUnits.Contains(12)) ||
                                    program.Route < RouteStdLightmapped && r.Groups.Contains(7) && ClockSwizzle.IsMatch(r.Body));

    /// <summary>The decoded family among the shader's embedded programs (NewEngineEmbeddedShaders).</summary>
    private static EmbeddedShader Family(Shader shader) =>
        shader.ShaderBundle is { } bundle
            ? NewEngineEmbeddedShaders.Classify(bundle.Variants.Select(v => v.SkeletonKey).ToList())
            : EmbeddedShader.Generic;

    private static string FamilyDescription(EmbeddedShader family) => family switch
    {
        EmbeddedShader.ScrollingVeil => LM.Get("GUI_Frame_ShaderViewer_Family_ScrollingVeil"),
        _ => family.ToString(),
    };

    internal static string UnitLabel(int unit) => unit switch
    {
        0 => LM.Get("GUI_Frame_ShaderViewer_Slot_Albedo"),
        1 => LM.Get("GUI_Frame_ShaderViewer_Slot_Normal"),
        2 => LM.Get("GUI_Frame_ShaderViewer_Slot_Expensive"),
        3 => LM.Get("GUI_Frame_ShaderViewer_Slot_Detail"),
        4 => LM.Get("GUI_Frame_ShaderViewer_Unit_Lighting"),
        5 => LM.Get("GUI_Frame_ShaderViewer_Unit_Shadow"),
        7 => LM.Get("GUI_Frame_ShaderViewer_Unit_Cube"),
        8 => LM.Get("GUI_Frame_ShaderViewer_Slot_4"),
        9 => LM.Get("GUI_Frame_ShaderViewer_Slot_5"),
        10 or 11 => LM.Get("GUI_Frame_ShaderViewer_Unit_Scene"),
        12 => LM.Get("GUI_Frame_ShaderViewer_Unit_BlendMask"),
        14 => LM.Get("GUI_Frame_ShaderViewer_Unit_LightDirection"),
        _ => LM.Get("GUI_Frame_ShaderViewer_Unit_Other", unit),
    };

    /// <summary>The texture slot an RSX unit reads, for the units that read the material's own textures.</summary>
    private static int UnitSlot(int unit) => unit switch
    {
        0 or 1 or 2 or 3 => unit,
        8 => 4,
        9 => 5,
        _ => -1,
    };

    /// <summary>What a patch group carries (VulkanRenderer.RsxShaders' rsxSetup).</summary>
    internal static string GroupLabel(int group, int route) => group switch
    {
        0 => LM.Get("GUI_Frame_ShaderViewer_Group_Fog"),
        1 => LM.Get("GUI_Frame_ShaderViewer_Group_Occlusion"),
        3 => LM.Get("GUI_Frame_ShaderViewer_Group_KeyColour"),
        4 => LM.Get("GUI_Frame_ShaderViewer_Group_KeyDirection"),
        5 => LM.Get("GUI_Frame_ShaderViewer_Group_ReflectionTint"),
        6 => LM.Get("GUI_Frame_ShaderViewer_Group_Reflection"),
        7 => LM.Get(route < RouteStdLightmapped ? "GUI_Frame_ShaderViewer_Group_ParallaxClock" : "GUI_Frame_ShaderViewer_Group_Parallax"),
        15 => LM.Get("GUI_Frame_ShaderViewer_Group_Clock"),
        16 => LM.Get("GUI_Frame_ShaderViewer_Group_Switches"),
        17 => LM.Get("GUI_Frame_ShaderViewer_Group_Screen"),
        19 => LM.Get("GUI_Frame_ShaderViewer_Group_Gameplay"),
        _ => LM.Get("GUI_Frame_ShaderViewer_Group_Constant"),
    };

    /// <summary>What an interpolant holds on the probe routes (Moby / unbaked Tie, the preview's); the register name elsewhere.</summary>
    internal static string InputLabel(string input, int route, int k)
    {
        bool probe = route is RouteProbe or RouteStdProbe;
        bool normalMap = route == RouteProbe || (k & 2) != 0;
        return input switch
        {
            "WPOS" => LM.Get("GUI_Frame_ShaderViewer_Input_Screen"),
            "COL0" => LM.Get("GUI_Frame_ShaderViewer_Input_Colour"),
            "TEX0" => LM.Get("GUI_Frame_ShaderViewer_Input_UV"),
            "TEX1" when probe => LM.Get("GUI_Frame_ShaderViewer_Input_Tint"),
            "TEX2" when probe => LM.Get("GUI_Frame_ShaderViewer_Input_View"),
            "TEX3" when probe => LM.Get("GUI_Frame_ShaderViewer_Input_Normal"),
            "TEX4" when probe => LM.Get(normalMap ? "GUI_Frame_ShaderViewer_Input_Tangent" : "GUI_Frame_ShaderViewer_Input_DetailUV"),
            "TEX5" when probe && normalMap => LM.Get("GUI_Frame_ShaderViewer_Input_Bitangent"),
            "TEX6" when probe => LM.Get("GUI_Frame_ShaderViewer_Input_DetailUV"),
            _ => LM.Get("GUI_Frame_ShaderViewer_Input_Other"),
        };
    }

    // ---------------------------------------------------------------- animation

    private sealed record UvUser(string Owner, Engine.Assets.Geometry.UvAnimation Animation);

    private List<UvUser>? _uvAnimations;

    /// <summary>The animated UVs (section 0x5C00) of the loaded primitives drawn with this shader. UV animation belongs to the
    /// primitive, not the shader, so each user is listed.</summary>
    private List<UvUser> UvAnimations()
    {
        if (_uvAnimations != null) return _uvAnimations;
        _uvAnimations = [];
        var level = LunaWindow.Instance.Level;
        if (level == null) return _uvAnimations;
        static Engine.Assets.Geometry.UvAnimation? Of(IMesh mesh) => (mesh as Engine.Assets.Geometry.Mesh)?.UvAnimation;
        foreach (var moby in level.Mobys.Values)
            foreach (var bangle in moby.Bangles)
                foreach (var mesh in bangle.Meshes)
                    if (mesh.Material.Id == _tuid && Of(mesh) is { } a)
                        _uvAnimations.Add(new UvUser(moby.Name ?? $"{moby.Id:X}", a));
        foreach (var tie in level.Ties.Values)
            foreach (var mesh in tie.Meshes)
                if (mesh.Material.Id == _tuid && Of(mesh) is { } a)
                    _uvAnimations.Add(new UvUser(tie.Name ?? $"{tie.Id:X}", a));
        foreach (var zone in level.Zones.Values)
            foreach (var ufrag in zone.UFrags)
                if (ufrag.Material.Id == _tuid && ufrag.UvAnimation is { } a)
                    _uvAnimations.Add(new UvUser(ufrag.Name ?? $"{ufrag.Id:X}", a));
        return _uvAnimations;
    }

    private bool HasAnimation(Shader shader) =>
        UvAnimations().Count > 0 || Programs(shader).Any(ReadsClock);

    private void DrawAnimation(Shader shader)
    {
        var users = UvAnimations();
        if (users.Count > 0)
        {
            ImGui.SeparatorText(LM.Get("GUI_Frame_ShaderViewer_UvAnimation"));
            ImGui.TextDisabled("u' = cos a (u - px) - sin a (v - py) + px + U");
            ImGui.TextDisabled("v' = sin a (u - px) + cos a (v - py) + py + V");
            ImGui.TextDisabled(LM.Get("GUI_Frame_ShaderViewer_UvAnimationTerms"));
            if (ImGui.BeginTable("##uv", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.BordersInnerH))
            {
                ImGui.TableSetupColumn(LM.Get("GUI_Frame_ShaderViewer_Owner"));
                ImGui.TableSetupColumn(LM.Get("GUI_Frame_ShaderViewer_Scroll"));
                ImGui.TableSetupColumn(LM.Get("GUI_Frame_ShaderViewer_Rotation"));
                ImGui.TableSetupColumn(LM.Get("GUI_Frame_ShaderViewer_Pivot"));
                ImGui.TableHeadersRow();
                foreach (var (owner, a) in users)
                {
                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0); ImGui.TextUnformatted(owner);
                    ImGui.TableSetColumnIndex(1); ImGui.TextUnformatted($"{F(a.ScrollPerSecond.X)}, {F(a.ScrollPerSecond.Y)} /s");
                    ImGui.TableSetColumnIndex(2); ImGui.TextUnformatted($"{F(float.RadiansToDegrees(a.RotationPerSecond))} deg/s");
                    ImGui.TableSetColumnIndex(3); ImGui.TextUnformatted($"{F(a.Pivot.X)}, {F(a.Pivot.Y)}");
                }
                ImGui.EndTable();
            }
        }

        var clocked = Programs(shader).Where(ReadsClock).ToList();
        if (clocked.Count > 0)
        {
            ImGui.SeparatorText(LM.Get("GUI_Frame_ShaderViewer_AnimatedProgram"));
            ImGui.TextWrapped(LM.Get("GUI_Frame_ShaderViewer_AnimatedProgramTip"));
            if (Family(shader) is var family && family != EmbeddedShader.Generic)
                ImGui.TextWrapped(FamilyDescription(family));
            foreach (var program in clocked)
            {
                ImGui.PushID(program.Variant.Tuid.GetHashCode());
                if (ImGui.TreeNode($"{program.Label}  {program.Variant.SkeletonKey}"))
                {
                    foreach (string line in ClockLines(program.Result!.Body, program.Route))
                        ImGui.TextUnformatted(line);
                    ImGui.TreePop();
                }
                ImGui.PopID();
            }
        }
    }

    /// <summary>The translated lines that read the clock: the program's time-driven logic.</summary>
    private static IEnumerable<string> ClockLines(string body, int route)
    {
        foreach (string raw in body.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Contains("G15") || route < RouteStdLightmapped && ClockSwizzle.IsMatch(line))
                yield return line;
        }
    }
}
