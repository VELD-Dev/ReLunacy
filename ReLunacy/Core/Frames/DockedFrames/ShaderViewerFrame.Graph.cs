using System.Numerics;
using ReLunacy.Engine.Loading.Shaders;
using ReLunacy.Utility.Localization;
using LegacyTexture = ReLunacy.Engine.Loading.Textures.Texture;

namespace ReLunacy.Core.Frames.DockedFrames;

/// <summary>
/// The read-only graph: how the shader's inputs reach its output. Two views of the same shader:
/// <list type="bullet">
/// <item>Material: the fixed pipeline its feature bits switch on (texture slots and their channels, the terms they feed,
/// lighting, blend), as the standard programs of both engines are laid out. Every edge exists only when the bit and the
/// texture behind it do.</item>
/// <item>Program: the game program actually selected for the chosen route, translated by RsxFragmentTranslator: every texture
/// unit, interpolant and constant patch group it reads, into the program, into the blend.</item>
/// </list>
/// Drawn with the window draw list: drag to pan, wheel to zoom, click a texture node to open it.
/// </summary>
public sealed partial class ShaderViewerFrame
{
    private enum GraphKind { Material, Program }

    private sealed class GraphNode
    {
        public required string Title;
        public List<string> Lines = [];
        public int Column;
        public Vector4 Colour;
        public Vector2 Position, Size;
        public ulong? TextureId;
    }

    private sealed record GraphEdge(GraphNode From, GraphNode To, string? Label);

    private GraphKind _graphKind = GraphKind.Material;
    private bool _graphDirty = true, _graphFit = true, _graphPanning;
    private readonly List<GraphNode> _nodes = [];
    private readonly List<GraphEdge> _edges = [];
    private Vector2 _graphPan;
    private float _graphZoom = 1f;

    private const float NodeWidth = 190f, ColumnGap = 70f, RowGap = 14f, HeaderHeight = 22f, LineHeight = 17f, NodePad = 6f;

    private static readonly Vector4 TextureColour = new(0.35f, 0.62f, 0.95f, 1f);
    private static readonly Vector4 ChannelColour = new(0.55f, 0.75f, 0.45f, 1f);
    private static readonly Vector4 ConstantColour = new(0.85f, 0.65f, 0.30f, 1f);
    private static readonly Vector4 LightColour = new(0.95f, 0.85f, 0.35f, 1f);
    private static readonly Vector4 ProgramColour = new(0.75f, 0.45f, 0.90f, 1f);
    private static readonly Vector4 OutputColour = new(0.90f, 0.40f, 0.40f, 1f);

    private void DrawGraphTab(Shader shader)
    {
        bool hasProgram = !shader.isOld && Programs(shader).Count > 0;
        if (!hasProgram) _graphKind = GraphKind.Material;
        bool materialAvailable = shader.isOld || shader.metadataNew!.Value.shaderClass != 0x13;
        if (!materialAvailable && hasProgram) _graphKind = GraphKind.Program;

        if (materialAvailable && ImGui.RadioButton(LM.Get("GUI_Frame_ShaderViewer_GraphMaterial"), _graphKind == GraphKind.Material))
        {
            _graphKind = GraphKind.Material;
            _graphDirty = true;
        }
        if (hasProgram)
        {
            if (materialAvailable) ImGui.SameLine();
            if (ImGui.RadioButton(LM.Get("GUI_Frame_ShaderViewer_GraphProgram"), _graphKind == GraphKind.Program))
            {
                _graphKind = GraphKind.Program;
                _graphDirty = true;
            }
            if (_graphKind == GraphKind.Program)
            {
                ImGui.SameLine();
                ProgramCombo(shader);
            }
        }
        ImGui.SameLine();
        if (ImGui.SmallButton(LM.Get("GUI_Frame_ShaderViewer_GraphFit"))) _graphFit = true;

        if (_graphDirty)
        {
            _graphDirty = false;
            _graphFit = true;
            BuildGraph(shader);
        }
        DrawGraphCanvas();
    }

    private GraphNode Node(int column, string title, Vector4 colour, params string[] lines)
    {
        var node = new GraphNode { Title = title, Column = column, Colour = colour, Lines = [.. lines.Where(l => l.Length > 0)] };
        _nodes.Add(node);
        return node;
    }

    private void Edge(GraphNode from, GraphNode to, string? label = null) => _edges.Add(new GraphEdge(from, to, label));

    private void BuildGraph(Shader shader)
    {
        _nodes.Clear();
        _edges.Clear();
        if (_graphKind == GraphKind.Program && SelectedProgram(shader) is { } program)
            BuildProgramGraph(shader, program);
        else
            BuildMaterialGraph(shader);
        Layout();
    }

    private GraphNode TextureNode(Shader shader, int slot)
    {
        var texture = SlotTexture(shader, slot);
        var node = Node(0, SlotName(slot), TextureColour,
            texture == null ? LM.Get("GUI_Frame_ShaderViewer_Empty") : TextureName(texture),
            texture == null ? "" : $"{texture.Width} x {texture.Height}  {texture.TexFormat}");
        node.TextureId = texture?.id;
        return node;
    }

    private static bool Translucent(byte mode) => mode is not (0 or 4 or 5);

    private void BuildMaterialGraph(Shader shader)
    {
        bool old = shader.isOld;
        byte mode = old ? shader.metadataOld!.Value.renderingMode : shader.metadataNew!.Value.renderingMode;
        var (pScale, pBias, tiling) = FileParallaxDetail(shader);
        LegacyTexture? normalTex = shader.Normal, expensiveTex = shader.Expensive, detailTex = shader.DetailMap;

        bool parallax, specular, normal, detail, reflection;
        if (old)
        {
            var o = shader.metadataOld!.Value;
            parallax = o.UsesParallax; specular = o.UsesGloss; reflection = o.UsesReflection;
            normal = o.UsesNormalMap; detail = o.UsesDetailMap;
        }
        else
        {
            var n = shader.metadataNew!.Value;
            int bits = StandardBits(shader);
            parallax = (bits & 0x01) != 0; specular = (bits & 0x04) != 0; reflection = specular;
            normal = (bits & 0x08) != 0; detail = (bits & 0x10) != 0;
            if (n.shaderClass != 0)
            {
                BuildClassGraph(shader, mode);
                return;
            }
        }
        normal &= normalTex != null;
        detail &= detailTex != null;
        bool hasExpensive = expensiveTex != null;
        // The expensive texture's blue channel reaches the shader unless the sampler zeroes it (CONTROL1 bit 0x8000 clear).
        bool emissive = hasExpensive && (old || shader.NewTextureDescriptors[2] is not { } d2 || (d2.Control1 & 0x8000) != 0);

        // Column 0: textures and constants. Column 1: per-texel terms. Column 2: lighting. 3: blend. 4: output.
        var albedo = TextureNode(shader, 0);
        GraphNode? normalTexNode = normal ? TextureNode(shader, 1) : null;
        GraphNode? expensive = hasExpensive ? TextureNode(shader, 2) : null;
        GraphNode? detailTexNode = detail ? TextureNode(shader, 3) : null;
        GraphNode? cube = reflection ? Node(0, LM.Get("GUI_Frame_ShaderViewer_Unit_Cube"), LightColour,
            old ? "" : LM.Get("GUI_Frame_ShaderViewer_CubeIndexValue", shader.metadataNew!.Value.environmentFilterIndex)) : null;

        GraphNode? parallaxNode = parallax && hasExpensive && normal
            ? Node(1, LM.Get("GUI_Frame_ShaderViewer_Node_Parallax"), ChannelColour, $"h x {F(pScale)} + {F(pBias)}")
            : null;
        var baseColour = Node(1, LM.Get("GUI_Frame_ShaderViewer_Node_BaseColour"), ChannelColour);
        GraphNode? alpha = Translucent(mode) || mode == 4
            ? Node(1, LM.Get("GUI_Frame_ShaderViewer_Node_Alpha"), ChannelColour, mode == 4 ? "a < 0.5: discard" : "")
            : null;
        GraphNode? normalNode = normal
            ? Node(1, LM.Get("GUI_Frame_ShaderViewer_Node_Normal"), ChannelColour,
                old ? "" : $"{LM.Get("GUI_Frame_ShaderViewer_NormalStrength")} {F(shader.metadataNew!.Value.parameter30)}")
            : null;
        GraphNode? specularNode = specular && hasExpensive ? Node(1, LM.Get("GUI_Frame_ShaderViewer_Node_Specular"), ChannelColour) : null;
        GraphNode? emissiveNode = emissive ? Node(1, LM.Get("GUI_Frame_ShaderViewer_Node_Emissive"), ChannelColour, "4 x B") : null;
        GraphNode? detailNode = detail
            ? Node(1, LM.Get("GUI_Frame_ShaderViewer_Node_Detail"), ChannelColour,
                $"{LM.Get("GUI_Frame_ShaderViewer_DetailTiling")} {F(tiling)}",
                old ? "" : $"{LM.Get("GUI_Frame_ShaderViewer_DetailGain")} {F(shader.metadataNew!.Value.parameter3C)}")
            : null;
        GraphNode? reflectionNode = reflection
            ? Node(1, LM.Get("GUI_Frame_ShaderViewer_Node_Reflection"), ChannelColour, ReflectionLines(shader))
            : null;

        if (!old)
        {
            var n = shader.metadataNew!.Value;
            if (n.tintR != 1f || n.tintG != 1f || n.tintB != 1f)
                Edge(Node(0, LM.Get("GUI_Frame_ShaderViewer_Tint"), ConstantColour, V3(n.tintR, n.tintG, n.tintB)), baseColour);
        }

        var lighting = Node(2, LM.Get("GUI_Frame_ShaderViewer_Node_Lighting"), LightColour, LM.Get("GUI_Frame_ShaderViewer_LightingProbe"));
        var blend = Node(3, LM.Get("GUI_Frame_ShaderViewer_Node_Blend"), OutputColour, RenderModeName(shader));
        var output = Node(4, LM.Get("GUI_Frame_ShaderViewer_Node_Output"), OutputColour);

        Edge(albedo, baseColour, "rgb");
        if (alpha != null) Edge(albedo, alpha, "a");
        if (normalTexNode != null && normalNode != null) Edge(normalTexNode, normalNode, old ? "xy" : "ag");
        if (expensive != null)
        {
            if (specularNode != null) Edge(expensive, specularNode, "r");
            if (parallaxNode != null) Edge(expensive, parallaxNode, "g");
            if (emissiveNode != null) Edge(expensive, emissiveNode, "b");
            if (detailNode != null) Edge(expensive, detailNode, "a");
        }
        if (parallaxNode != null)
        {
            Edge(parallaxNode, baseColour, "uv");
            if (normalNode != null) Edge(parallaxNode, normalNode, "uv");
            if (detailNode != null) Edge(parallaxNode, detailNode, "uv");
        }
        if (detailTexNode != null && detailNode != null) Edge(detailTexNode, detailNode, "rgba");
        if (detailNode != null)
        {
            Edge(detailNode, baseColour, "b");
            if (normalNode != null) Edge(detailNode, normalNode, "rg");
            if (specularNode != null) Edge(detailNode, specularNode, "a");
        }
        if (cube != null && reflectionNode != null) Edge(cube, reflectionNode);
        if (reflectionNode != null)
        {
            if (specularNode != null) Edge(specularNode, reflectionNode, LM.Get("GUI_Frame_ShaderViewer_Mask"));
            if (normalNode != null) Edge(normalNode, reflectionNode);
            Edge(baseColour, reflectionNode, LM.Get("GUI_Frame_ShaderViewer_AlbedoMix"));
        }

        Edge(baseColour, lighting);
        if (normalNode != null) Edge(normalNode, lighting);
        if (specularNode != null) Edge(specularNode, lighting);
        if (emissiveNode != null) Edge(emissiveNode, lighting);
        if (reflectionNode != null) Edge(reflectionNode, lighting);
        Edge(lighting, blend, "rgb");
        if (alpha != null) Edge(alpha, blend, "a");
        Edge(blend, output);
    }

    private string[] ReflectionLines(Shader shader)
    {
        if (shader.isOld)
        {
            var t = shader.metadataOld!.Value.ReflectionTint;
            return [$"{LM.Get("GUI_Frame_ShaderViewer_ReflectionTint")} {V3(t.X, t.Y, t.Z)}"];
        }
        var n = shader.metadataNew!.Value;
        return
        [
            $"{LM.Get("GUI_Frame_ShaderViewer_ReflectionTint")} {V3(n.reflectionTintR, n.reflectionTintG, n.reflectionTintB)}",
            $"{LM.Get("GUI_Frame_ShaderViewer_ReflectionStrength")} {F(n.parameter34)}",
            _crackInTime ? "" : $"{LM.Get("GUI_Frame_ShaderViewer_FresnelExponent")} {F(n.reflectionParamY)}",
            $"{LM.Get("GUI_Frame_ShaderViewer_FresnelGain")} {F(n.reflectionParamW)}",
        ];
    }

    /// <summary>A non-standard class without a program of its own (fur, foliage, render target, anisotropic): its own handler
    /// draws it, so the graph shows only what it reads.</summary>
    private void BuildClassGraph(Shader shader, byte mode)
    {
        var handler = Node(2, ClassName(shader), ProgramColour, LM.Get("GUI_Frame_ShaderViewer_ClassHandler"));
        for (int slot = 0; slot < SlotCount(shader); slot++)
            if (SlotTexture(shader, slot) != null)
                Edge(TextureNode(shader, slot), handler);
        var n = shader.metadataNew!.Value;
        Edge(Node(0, LM.Get("GUI_Frame_ShaderViewer_Tint"), ConstantColour, V3(n.tintR, n.tintG, n.tintB)), handler);
        var blend = Node(3, LM.Get("GUI_Frame_ShaderViewer_Node_Blend"), OutputColour, RenderModeName(shader));
        Edge(handler, blend);
        Edge(blend, Node(4, LM.Get("GUI_Frame_ShaderViewer_Node_Output"), OutputColour));
        _ = mode;
    }

    private void BuildProgramGraph(Shader shader, ProgramInfo program)
    {
        var meta = shader.metadataNew!.Value;
        var v = program.Variant;
        var programNode = Node(2, LM.Get("GUI_Frame_ShaderViewer_Node_Program"), ProgramColour,
            program.Label,
            v.SkeletonKey,
            LM.Get("GUI_Frame_ShaderViewer_InstructionCount", v.InstructionCount),
            program.Result == null ? LM.Get("GUI_Frame_ShaderViewer_NotTranslated", program.Failure ?? "?") : "");

        // Inputs: the material's textures by unit, then the scene's.
        foreach (int unit in v.TextureUnits)
        {
            int slot = UnitSlot(unit);
            GraphNode node;
            if (slot >= 0 && (slot < 4 || !(program.Route >= RouteStdLightmapped && v.TextureUnits.Contains(12))))
            {
                node = TextureNode(shader, slot);
                node.Lines.Insert(0, LM.Get("GUI_Frame_ShaderViewer_UnitNumber", unit));
            }
            else
            {
                node = Node(0, UnitLabel(unit), LightColour, LM.Get("GUI_Frame_ShaderViewer_UnitNumber", unit));
            }
            Edge(node, programNode);
        }

        if (program.Result is { } result)
        {
            var inputs = Node(1, LM.Get("GUI_Frame_ShaderViewer_Node_Interpolants"), ChannelColour,
                [.. result.Inputs.Select(i => $"{i}  {InputLabel(i, program.Route, program.K)}")]);
            Edge(inputs, programNode);
            foreach (int group in result.Groups)
            {
                var node = Node(1, $"G{group}  {GroupLabel(group, program.Route)}", ConstantColour, GroupValue(meta, group, program.Route));
                Edge(node, programNode);
            }
        }

        var blend = Node(3, LM.Get("GUI_Frame_ShaderViewer_Node_Blend"), OutputColour, RenderModeName(shader));
        Edge(programNode, blend, v.Exports32 ? "r0" : "h0");
        Edge(blend, Node(4, LM.Get("GUI_Frame_ShaderViewer_Node_Output"), OutputColour));
    }

    /// <summary>The material values a group carries, where they come from the material (VulkanRenderer.RsxShaders' rsxSetup:
    /// G5 = +0x50, G6 = +0x60, G7 = +0x70 parallax and detail tiling).</summary>
    private string GroupValue(ShaderMetadataNew meta, int group, int route)
    {
        var (scale, bias, tiling) = FileParallaxDetail(_shader!);
        return group switch
        {
            5 => $"{V3(meta.reflectionTintR, meta.reflectionTintG, meta.reflectionTintB)}, {F(meta.reflectionTintAlbedoMix)}",
            6 => $"{F(meta.reflectionParamX)}, {F(meta.reflectionParamY)}, {F(meta.reflectionParamZ)}, {F(meta.reflectionParamW)}",
            7 => route >= RouteStdLightmapped ? $"{F(scale)}, {F(bias)}, {F(tiling)}" : $"{F(scale)}, {F(bias)}, 1, t",
            15 => "t",
            _ => "",
        };
    }

    /// <summary>Columns left to right, each centred on the graph's middle line.</summary>
    private void Layout()
    {
        float fontScale = ImGui.GetFontSize() / 13f;
        foreach (var node in _nodes)
            node.Size = new Vector2(NodeWidth * fontScale, HeaderHeight * fontScale + node.Lines.Count * LineHeight * fontScale + NodePad * fontScale);
        // Columns with nothing in them collapse.
        var columns = _nodes.Select(n => n.Column).Distinct().OrderBy(c => c).ToList();
        for (int c = 0; c < columns.Count; c++)
        {
            var nodes = _nodes.Where(n => n.Column == columns[c]).ToList();
            float total = nodes.Sum(n => n.Size.Y) + RowGap * (nodes.Count - 1);
            float y = -total * 0.5f;
            foreach (var node in nodes)
            {
                node.Position = new Vector2(c * (NodeWidth * fontScale + ColumnGap), y);
                y += node.Size.Y + RowGap;
            }
        }
    }

    private Vector2 ToScreen(Vector2 origin, Vector2 graph) => origin + _graphPan + graph * _graphZoom;

    private GraphNode? GraphNodeAt(Vector2 origin, Vector2 mouse)
    {
        var p = (mouse - origin - _graphPan) / _graphZoom;
        return _nodes.LastOrDefault(n => p.X >= n.Position.X && p.Y >= n.Position.Y && p.X <= n.Position.X + n.Size.X && p.Y <= n.Position.Y + n.Size.Y);
    }

    private void DrawGraphCanvas()
    {
        var size = ImGui.GetContentRegionAvail();
        if (size.X < 8f || size.Y < 8f) return;
        var origin = ImGui.GetCursorScreenPos();
        var max = origin + size;
        ImGui.InvisibleButton("##graph_canvas", size, ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.MouseButtonMiddle);
        bool hovered = ImGui.IsItemHovered();
        var mouse = ImGui.GetMousePos();

        if (_graphFit && _nodes.Count > 0)
        {
            _graphFit = false;
            var min = new Vector2(_nodes.Min(n => n.Position.X), _nodes.Min(n => n.Position.Y));
            var maxG = new Vector2(_nodes.Max(n => n.Position.X + n.Size.X), _nodes.Max(n => n.Position.Y + n.Size.Y));
            var span = maxG - min;
            _graphZoom = Math.Clamp(MathF.Min((size.X - 40f) / span.X, (size.Y - 40f) / span.Y), 0.2f, 1.25f);
            _graphPan = size * 0.5f - (min + span * 0.5f) * _graphZoom;
        }

        if (hovered && ImGui.GetIO().MouseWheel != 0f)
        {
            float oldZoom = _graphZoom;
            _graphZoom = Math.Clamp(_graphZoom * MathF.Pow(1.15f, ImGui.GetIO().MouseWheel), 0.2f, 2.5f);
            var local = mouse - origin;
            _graphPan = local - (local - _graphPan) * (_graphZoom / oldZoom);
        }

        var hover = hovered ? GraphNodeAt(origin, mouse) : null;
        if (hovered && (ImGui.IsMouseClicked(ImGuiMouseButton.Left) || ImGui.IsMouseClicked(ImGuiMouseButton.Middle)))
            _graphPanning = true;
        if (_graphPanning)
        {
            if (ImGui.IsMouseDown(ImGuiMouseButton.Left) || ImGui.IsMouseDown(ImGuiMouseButton.Middle)) _graphPan += ImGui.GetIO().MouseDelta;
            else _graphPanning = false;
        }
        if (hovered && hover?.TextureId is { } textureId && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            OpenTexture(textureId);
        if (hover?.TextureId != null && !_graphPanning) ImGui.SetTooltip(LM.Get("GUI_Frame_ShaderViewer_OpenTextureGraph"));

        var dl = ImGui.GetWindowDrawList();
        dl.PushClipRect(origin, max, true);
        dl.AddRectFilled(origin, max, ImGui.GetColorU32(ImGuiCol.ChildBg));
        var font = ImGui.GetFont();
        float fontSize = ImGui.GetFontSize() * _graphZoom;
        uint dim = ImGui.GetColorU32(ImGuiCol.TextDisabled), text = ImGui.GetColorU32(ImGuiCol.Text);

        foreach (var (from, to, label) in _edges)
        {
            var a = ToScreen(origin, from.Position + new Vector2(from.Size.X, from.Size.Y * 0.5f));
            var b = ToScreen(origin, to.Position + new Vector2(0f, to.Size.Y * 0.5f));
            bool lit = hover != null && (hover == from || hover == to);
            uint colour = ImGui.ColorConvertFloat4ToU32(from.Colour * new Vector4(1f, 1f, 1f, lit ? 1f : 0.6f));
            float bend = MathF.Max(30f * _graphZoom, MathF.Abs(b.X - a.X) * 0.5f);
            dl.AddBezierCubic(a, a + new Vector2(bend, 0f), b - new Vector2(bend, 0f), b, colour, (lit ? 2.5f : 1.5f) * MathF.Max(0.6f, _graphZoom));
            dl.AddCircleFilled(b, 3f * MathF.Max(0.6f, _graphZoom), colour);
            if (label != null && _graphZoom >= 0.5f)
                unsafe { dl.AddText(font, fontSize * 0.85f, a + new Vector2(5f, -fontSize - 1f), dim, label); }
        }

        float round = 4f * _graphZoom;
        foreach (var node in _nodes)
        {
            var min = ToScreen(origin, node.Position);
            var maxP = min + node.Size * _graphZoom;
            if (maxP.X < origin.X || min.X > max.X || maxP.Y < origin.Y || min.Y > max.Y) continue;
            float header = HeaderHeight * (ImGui.GetFontSize() / 13f) * _graphZoom;
            dl.AddRectFilled(min, maxP, ImGui.GetColorU32(ImGuiCol.FrameBg), round);
            dl.AddRectFilled(min, new Vector2(maxP.X, min.Y + header), ImGui.ColorConvertFloat4ToU32(node.Colour * new Vector4(0.55f, 0.55f, 0.55f, 1f)), round, ImDrawFlags.RoundCornersTop);
            dl.AddRect(min, maxP, node == hover ? ImGui.ColorConvertFloat4ToU32(node.Colour) : ImGui.GetColorU32(ImGuiCol.Border), round, ImDrawFlags.None, node == hover ? 2f : 1f);
            if (_graphZoom < 0.3f) continue;
            dl.PushClipRect(min, maxP, true);
            float pad = NodePad * _graphZoom;
            unsafe { dl.AddText(font, fontSize, min + new Vector2(pad, (header - fontSize) * 0.5f), text, node.Title); }
            float y = min.Y + header + pad * 0.5f;
            foreach (string line in node.Lines)
            {
                unsafe { dl.AddText(font, fontSize * 0.92f, new Vector2(min.X + pad, y), dim, line); }
                y += LineHeight * (ImGui.GetFontSize() / 13f) * _graphZoom;
            }
            dl.PopClipRect();
        }
        dl.PopClipRect();
    }
}
