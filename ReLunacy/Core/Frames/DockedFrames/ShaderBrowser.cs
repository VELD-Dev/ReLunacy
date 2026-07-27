using System.Numerics;
using System.Text;
using ReLunacy.Core.Selection;
using ReLunacy.Engine.Assets.Interfaces;
using ReLunacy.Engine.Assets.Mobys;
using ReLunacy.Engine.Assets.Ties;
using ReLunacy.Engine.Loading.Readers;
using ReLunacy.Engine.Loading.Shaders;
using ReLunacy.Engine.Loading.Textures;
using ReLunacy.Engine.Scene;
using ReLunacy.Utility.Localization;

namespace ReLunacy.Core.Frames.DockedFrames;

/// <summary>
/// Reverse-engineering tool: lists every shader (material) the loaded level parsed, and for the
/// selected one shows its raw renderingMode byte, alphaClip, decoded texture references, and a
/// hex dump of every still-unidentified byte range (ShaderMetadataOld/New's Unk1/Unk2/Unk3) —
/// nothing here is hidden behind the IMaterial abstraction the renderer uses, since the whole
/// point is to see what the file actually contains, not what we've already decided it means.
/// </summary>
public class ShaderBrowser : DockedFrame, ILevelListener
{
    protected override ImGuiCond DockingConditions { get; set; } = ImGuiCond.Appearing;
    protected override Vector2 DefaultPosition { get; set; } = ImGui.GetWorkCenter(ImGui.GetMainViewport());
    protected override ImGuiWindowFlags WindowFlags { get; set; } = ImGuiWindowFlags.NoScrollbar;

    private string inputText = "";
    private List<Shader> shaders = [];
    private int selectedShader = -1;
    private ShaderUsageResult? usageResults;

    // null = no filter (show every render mode). Filtering by the raw byte rather than the
    // RenderingMode enum so 0x01/0x02/0x03/etc. — anything not named yet — can still be isolated
    // and inspected, which is the whole point of this for reverse engineering.
    private byte? renderModeFilter;
    // Distinct renderingMode byte values actually present among the loaded shaders, with counts —
    // rebuilt whenever the shader list changes, not per frame.
    private readonly List<(byte value, int count)> renderModeCounts = [];

    // Off by default (the hex dump alone is the more compact, general-purpose view) — toggled on
    // when hunting for a specific numeric value, e.g. a per-material decal-offset bias, across the
    // still-unidentified Unk byte ranges.
    private bool showFloatInterpretation;

    private sealed record ShaderUsageResult(List<Moby> Mobys, List<Tie> Ties, List<IUFrag> UFrags)
    {
        public bool IsEmpty => Mobys.Count == 0 && Ties.Count == 0 && UFrags.Count == 0;
    }

    public ShaderBrowser()
    {
        FrameName = LM.Get("GUI_Frame_ShaderBrowser");
    }

    public void TransmitShaders(LevelData level)
    {
        shaders = [.. level.Shaders.Values];
        renderModeCounts.Clear();
        renderModeCounts.AddRange(shaders
            .GroupBy(s => (byte)s.RenderingMode)
            .OrderBy(g => g.Key)
            .Select(g => (g.Key, g.Count())));
    }

    public void OnLevelLoaded()
    {
        if (LunaWindow.Instance.Level != null)
            TransmitShaders(LunaWindow.Instance.Level);
    }

    /// <summary>Selects the shader with the given TUID, e.g. when jumping here from the Asset
    /// Viewer or Textures Explorer. Returns false if it isn't in the currently transmitted set.</summary>
    public bool SelectShader(ulong tuid)
    {
        int index = shaders.FindIndex(s => s.TUID == tuid);
        if (index == -1) return false;

        selectedShader = index;
        usageResults = null;
        return true;
    }

    public void OnLevelUnloading()
    {
        shaders.Clear();
        renderModeCounts.Clear();
        renderModeFilter = null;
        selectedShader = -1;
        usageResults = null;
    }

    private static string RenderModeLabel(byte value) =>
        Enum.IsDefined((RenderingMode)value)
            ? $"0x{value:X2} ({(RenderingMode)value})"
            : $"0x{value:X2} ({LM.Get("GUI_Frame_ShaderBrowser_Unknown")})";

    private IEnumerable<Shader> FilteredShaders()
    {
        IEnumerable<Shader> result = shaders;

        if (renderModeFilter.HasValue)
            result = result.Where(s => (byte)s.RenderingMode == renderModeFilter.Value);

        if (!string.IsNullOrWhiteSpace(inputText))
            result = result.Where(s =>
                (!string.IsNullOrEmpty(s.name) && s.name.Contains(inputText, StringComparison.OrdinalIgnoreCase)) ||
                s.TUID.ToString("X").Contains(inputText, StringComparison.OrdinalIgnoreCase));

        return result;
    }

    protected override void Render(double deltaTime)
    {
        ImGui.InputTextWithHint(LM.Get("GUI_Frame_ShaderBrowser_SearchLabel"), LM.Get("GUI_Frame_ShaderBrowser_SearchHint", shaders.Count), ref inputText, 128);

        string comboPreview = renderModeFilter.HasValue ? RenderModeLabel(renderModeFilter.Value) : LM.Get("GUI_Common_FilterAll");
        if (ImGui.BeginCombo(LM.Get("GUI_Frame_ShaderBrowser_FilterRenderMode"), comboPreview))
        {
            if (ImGui.Selectable(LM.Get("GUI_Common_FilterAll"), renderModeFilter == null))
                renderModeFilter = null;

            foreach (var (value, count) in renderModeCounts)
            {
                if (ImGui.Selectable($"{RenderModeLabel(value)} — {count}##rendermode_{value:X2}", renderModeFilter == value))
                    renderModeFilter = value;
            }
            ImGui.EndCombo();
        }

        var filtered = FilteredShaders().ToList();

        if (ImGui.BeginChild("shader_list", new(ImGui.GetContentRegionAvail().X / 3, ImGui.GetContentRegionAvail().Y), ImGuiChildFlags.Borders))
        {
            foreach (var shader in filtered)
            {
                bool isSelected = selectedShader >= 0 && selectedShader < shaders.Count && ReferenceEquals(shaders[selectedShader], shader);
                string label = string.IsNullOrEmpty(shader.name) ? shader.TUID.ToString("X") : shader.name;
                if (ImGui.Selectable($"{label}##shader_{shader.TUID:X}", isSelected))
                {
                    selectedShader = shaders.IndexOf(shader);
                    usageResults = null;
                }
            }
        }
        ImGui.EndChild();

        ImGui.SameLine();

        if (ImGui.BeginChild("shader_detail", ImGui.GetContentRegionAvail(), ImGuiChildFlags.Borders, ImGuiWindowFlags.AlwaysVerticalScrollbar))
        {
            if (selectedShader < 0 || selectedShader >= shaders.Count)
                ImGui.TextDisabled(LM.Get("GUI_Frame_ShaderBrowser_NoSelection"));
            else
                DrawShaderDetail(shaders[selectedShader]);
        }
        ImGui.EndChild();
    }

    private void DrawShaderDetail(Shader shader)
    {
        ImGui.Text(LM.Get("GUI_Frame_ShaderBrowser_Name", string.IsNullOrEmpty(shader.name) ? "-" : shader.name));
        ImGui.Text(LM.Get("GUI_Frame_ShaderBrowser_Tuid", shader.TUID.ToString("X")));
        ImGui.Text(LM.Get("GUI_Frame_ShaderBrowser_Engine", shader.isOld ? "Old" : "New"));

        ImGui.SeparatorText(LM.Get("GUI_Frame_ShaderBrowser_AlphaSection"));

        ImGui.Text(LM.Get("GUI_Frame_ShaderBrowser_RenderingMode", RenderModeLabel((byte)shader.RenderingMode)));

        float alphaClip = shader.isOld ? shader.metadataOld!.Value.alphaClip : shader.metadataNew!.Value.alphaClip;
        ImGui.Text(LM.Get("GUI_Frame_ShaderBrowser_AlphaClip", alphaClip));

        ImGui.SeparatorText(LM.Get("GUI_Frame_ShaderBrowser_TexturesSection"));
        DrawTextureRef(LM.Get("GUI_Frame_ShaderBrowser_Albedo"), shader.Albedo);
        DrawTextureRef(LM.Get("GUI_Frame_ShaderBrowser_Normal"), shader.Normal);
        DrawTextureRef(LM.Get("GUI_Frame_ShaderBrowser_Expensive"), shader.Expensive);
        DrawTextureRef(LM.Get("GUI_Frame_ShaderBrowser_DetailMap"), shader.DetailMap);

        // Live per-material parallax multiplier (default 1) — a reverse-engineering aid: tweak it
        // on a shader while eyeing candidate values from the raw metadata hex dump below, to find
        // which field (if any) the real game sources its parallax strength from. Runtime-only by
        // design, nothing is persisted. Only shown when the material is actually built (i.e. the
        // loaded region uses it) and the lit shader that consumes it is active.
        var assetManager = LunaWindow.Instance.AssetManager;
        if (assetManager != null && assetManager.TryGetParallaxMultiplier(shader.TUID, out float parallaxMultiplier))
        {
            ImGui.SeparatorText(LM.Get("GUI_Frame_ShaderBrowser_LiveTuningSection"));
            if (!Program.Settings.EnableLighting)
                ImGui.TextDisabled(LM.Get("GUI_Frame_ShaderBrowser_ParallaxNeedsLighting"));
            if (ImGui.DragFloat(LM.Get("GUI_Frame_ShaderBrowser_ParallaxMultiplier"), ref parallaxMultiplier, 0.05f, 0f, 64f, "%.2f"))
                assetManager.SetParallaxMultiplier(shader.TUID, parallaxMultiplier);
        }

        ImGui.SeparatorText(LM.Get("GUI_Frame_ShaderBrowser_RawMetadataSection"));
        ImGui.Checkbox(LM.Get("GUI_Frame_ShaderBrowser_ShowAsFloats"), ref showFloatInterpretation);
        if (shader.isOld && shader.metadataOld.HasValue)
        {
            var meta = shader.metadataOld.Value;
            ImGui.Text($"0x12 Class: {meta.Class}");
            DrawHexDump("Unk1", 0x13, meta.Unk1);
            DrawHexDump("Unk2", 0x24, meta.Unk2);
            DrawHexDump("Unk4", 0x48, meta.Unk4);
            DrawHexDump("Unk3a", 0x50, meta.Unk3a);
        }
        else if (!shader.isOld && shader.metadataNew.HasValue)
        {
            var meta = shader.metadataNew.Value;
            DrawHexDump("Unk1", 0x0C, meta.Unk1);
            DrawHexDump("Unk2", 0x22, meta.Unk2);
            DrawHexDump("Unk3a", 0x34, meta.Unk3a);
            DrawHexDump("Unk4", 0x48, meta.Unk4);
            DrawHexDump("Unk3b", 0x50, meta.Unk3b);
        }

        ImGui.Separator();
        if (ImGui.Button(LM.Get("GUI_Frame_ShaderBrowser_FindUsages")))
            usageResults = FindShaderUsages(shader.TUID);

        if (usageResults == null)
            return;

        if (usageResults.IsEmpty)
        {
            ImGui.TextDisabled(LM.Get("GUI_Frame_TextureExplorer_Preview_NoUsagesFound"));
            return;
        }

        if (usageResults.Mobys.Count > 0)
        {
            ImGui.Text(LM.Get("GUI_Frame_TextureExplorer_Preview_UsagesMobys", usageResults.Mobys.Count));
            foreach (var moby in usageResults.Mobys)
            {
                if (ImGui.Selectable($"{moby.Name ?? moby.Id.ToString("X")}##shaderusage_moby_{moby.Id:X}"))
                    OpenMobyInAssetViewer(moby.Id);
            }
        }
        if (usageResults.Ties.Count > 0)
        {
            ImGui.Text(LM.Get("GUI_Frame_TextureExplorer_Preview_UsagesTies", usageResults.Ties.Count));
            foreach (var tie in usageResults.Ties)
            {
                if (ImGui.Selectable($"{tie.Name ?? tie.Id.ToString("X")}##shaderusage_tie_{tie.Id:X}"))
                    OpenTieInAssetViewer(tie.Id);
            }
        }
        if (usageResults.UFrags.Count > 0)
        {
            ImGui.Text(LM.Get("GUI_Frame_TextureExplorer_Preview_UsagesUFrags", usageResults.UFrags.Count));
            // Indexed, not keyed by ufrag.Id — IUFrag.Id is only unique within its own zone (see
            // TexturesExplorer.SelectUFragInView3D), so two results here can share an Id.
            for (int i = 0; i < usageResults.UFrags.Count; i++)
            {
                var ufrag = usageResults.UFrags[i];
                if (ImGui.Selectable($"{ufrag.Name ?? ufrag.Id.ToString("X")}##shaderusage_ufrag_{i}"))
                    SelectUFragInView3D(ufrag);
            }
        }
    }

    private void DrawTextureRef(string label, Texture? tex)
    {
        if (tex == null)
        {
            ImGui.Text($"{label}: {LM.Get("GUI_Frame_ShaderBrowser_None")}");
            return;
        }

        ImGui.Text($"{label}: {(string.IsNullOrEmpty(tex.name) ? tex.id.ToString("X") : tex.name)} (0x{tex.id:X}, {tex.Width}x{tex.Height}, {tex.TexFormat})");

        // See TextureMetadataOld.AlphaKillCandidate — a candidate per-texture alpha bit distinct
        // from the shader's own renderingMode byte, cross-referenced from InsomniaToolset but not
        // yet confirmed against real data. Surfaced here since it's a texture-level flag, not a
        // shader-level one.
        if (tex.isOld && tex.textureMetadata is TextureMetadataOld oldMeta)
            ImGui.Text(LM.Get("GUI_Frame_ShaderBrowser_AlphaKillCandidate", oldMeta.AlphaKillCandidate));

        var am = LunaWindow.Instance.AssetManager;
        if (am != null && am.BuiltTextures.TryGetValue(tex.id, out var tex2d))
        {
            var ptr = LunaWindow.Instance.imGuiController.GetOrCreateImGuiBinding(LunaWindow.Instance.GraphicsDevice.ResourceFactory, tex2d.DeviceTexture);
            ImGui.Image(ptr, new(64, 64), Vector2.UnitY, Vector2.UnitX);
            if (ImGui.IsItemClicked())
                OpenTextureInExplorer(tex.id);
        }
    }

    private void DrawHexDump(string label, int baseOffset, byte[]? data)
    {
        if (data == null || data.Length == 0)
        {
            ImGui.Text($"{label}: ({LM.Get("GUI_Frame_ShaderBrowser_Empty")})");
            return;
        }

        ImGui.Text($"{label} (0x{baseOffset:X2}, {data.Length} bytes):");
        var sb = new StringBuilder();
        for (int i = 0; i < data.Length; i += 16)
        {
            sb.Append($"  {baseOffset + i:X4}: ");
            int lineEnd = Math.Min(i + 16, data.Length);
            for (int j = i; j < lineEnd; j++)
                sb.Append($"{data[j]:X2} ");
            sb.Append('\n');
        }
        ImGui.TextUnformatted(sb.ToString());

        if (!showFloatInterpretation || data.Length < 4)
            return;

        // Every 4-byte-aligned position reinterpreted as a big-endian float32 — this file format
        // is PS3/PowerPC (big-endian throughout, see StreamHelper.Endianness.Big), so a naive
        // BitConverter read would silently byte-swap every value. Aligned to the start of this
        // array, not to the file's absolute offset — if the real field turns out to start at an
        // odd byte, this won't show it, but 4-byte alignment is the overwhelmingly common case for
        // game data structures and keeps this from being an unreadable wall of every byte offset.
        var floatSb = new StringBuilder();
        for (int i = 0; i + 3 < data.Length; i += 4)
        {
            float f = System.Buffers.Binary.BinaryPrimitives.ReadSingleBigEndian(data.AsSpan(i, 4));
            floatSb.Append($"  {baseOffset + i:X4}: {f,14:0.000000}\n");
        }
        ImGui.TextUnformatted(floatSb.ToString());
    }

    private static bool MaterialUsesShader(IMaterial mat, ulong tuid) => mat.Id == tuid;

    private static bool MobyUsesShader(Moby moby, ulong tuid) =>
        moby.Bangles.Any(bangle => bangle.Meshes.Any(mesh => MaterialUsesShader(mesh.Material, tuid)));

    private static bool TieUsesShader(Tie tie, ulong tuid) =>
        tie.Meshes.Any(mesh => MaterialUsesShader(mesh.Material, tuid));

    private static ShaderUsageResult FindShaderUsages(ulong tuid)
    {
        var level = LunaWindow.Instance.Level;
        if (level == null) return new ShaderUsageResult([], [], []);

        var mobys = level.Mobys.Values.Where(m => MobyUsesShader(m, tuid)).ToList();
        var ties = level.Ties.Values.Where(t => TieUsesShader(t, tuid)).ToList();
        var ufrags = level.Zones.Values
            .SelectMany(z => z.UFrags)
            .Where(u => MaterialUsesShader(u.Material, tuid))
            .ToList();

        return new ShaderUsageResult(mobys, ties, ufrags);
    }

    private static void OpenMobyInAssetViewer(ulong mobyId)
    {
        var viewer = OpenAssetViewer();
        if (viewer != null)
        {
            viewer.SelectMobyById(mobyId);
            viewer.Focus();
        }
    }

    private static void OpenTieInAssetViewer(ulong tieId)
    {
        var viewer = OpenAssetViewer();
        if (viewer != null)
        {
            viewer.SelectTieById(tieId);
            viewer.Focus();
        }
    }

    private static AssetViewer? OpenAssetViewer()
    {
        var viewer = LunaWindow.Instance.GetFirstFrame<AssetViewer>();
        if (viewer == null)
        {
            viewer = new AssetViewer(LunaWindow.Instance.GraphicsDevice);
            LunaWindow.Instance.AddFrame(viewer);
        }
        if (LunaWindow.Instance.AssetManager == null || LunaWindow.Instance.Level == null)
            return null;

        viewer.TransmitAssets(LunaWindow.Instance.AssetManager, LunaWindow.Instance.Level.Mobys, LunaWindow.Instance.Level.Ties);
        return viewer;
    }

    private static void OpenTextureInExplorer(ulong textureId)
    {
        var explorer = LunaWindow.Instance.GetFirstFrame<TexturesExplorer>();
        if (explorer == null)
        {
            explorer = new TexturesExplorer();
            if (LunaWindow.Instance.AssetManager != null)
                explorer.TransmitTextures(LunaWindow.Instance.AssetManager);
            LunaWindow.Instance.AddFrame(explorer);
        }
        explorer.SelectTexture(textureId);
        explorer.Focus();
    }

    private static void SelectUFragInView3D(IUFrag ufrag)
    {
        var entity = EntityManager.Singleton.AllEntities().OfType<EntityUFrag>().FirstOrDefault(e => ReferenceEquals(e.UFrag, ufrag));
        if (entity == null) return;

        SelectionManager.Singleton.Select(entity);
        var v3d = LunaWindow.Instance.GetFirstFrame<View3D>();
        if (v3d != null) v3d.SelectedEntity = entity;
    }

    public override void RenderAsWindow(double deltaTime)
    {
        ImGui.SetNextWindowPos(DefaultPosition, ImGuiCond.Appearing);
        base.RenderAsWindow(deltaTime);
    }
}
