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
/// hex dump of every still-unidentified byte range (ShaderMetadataOld/New's Unk fields) — nothing
/// here is hidden behind the IMaterial abstraction the renderer uses, since the whole point is to
/// see what the file actually contains, not what we've already decided it means. Those ranges are
/// deliberately kept as few and as LONG as the known fields allow: an unknown split at a boundary
/// that isn't real hides any multi-word value straddling it.
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

    // ShaderMetadataOld 0x50/0x54. The new engine's metadata has no identified equivalent — see
    // MaterialReader.GetParallaxScale, which returns 0 there for the same reason.
    private static float MetadataParallaxScale(Shader shader) =>
        shader.isOld && shader.metadataOld.HasValue ? shader.metadataOld.Value.parallaxScale : 0f;

    private static float MetadataParallaxBias(Shader shader) =>
        shader.isOld && shader.metadataOld.HasValue ? shader.metadataOld.Value.parallaxBias : 0f;

    // Old-engine only; new-engine metadata has no identified detail fields (see MaterialReader).
    private static float MetadataDetailFloat(Shader shader, Func<ShaderMetadataOld, float> select) =>
        shader.isOld && shader.metadataOld.HasValue ? select(shader.metadataOld.Value) : 0f;

    // Matches AssetManager's own 0-means-absent fallback, so Reset lands on exactly what a fresh
    // material build would produce rather than on a literal 0 that collapses the map to one texel.
    private static float MetadataDetailTiling(Shader shader)
    {
        float tiling = shader.isOld && shader.metadataOld.HasValue ? shader.metadataOld.Value.detailTiling : 0f;
        return tiling != 0f ? tiling : Engine.Rendering.AssetManager.DefaultDetailTiling;
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

        // Live per-material parallax scale/bias — a reverse-engineering aid: the game's own shader
        // computes height * scale + bias from two per-material constants, so these are the two
        // numbers to hunt for in the raw metadata hex dump below. Type a candidate pair in here
        // (ctrl+click a drag to enter an exact value) and watch the surface. Deliberately
        // unclamped and shown at float precision so a value read straight out of the dump can be
        // used verbatim — a wide range is the whole point, and the sign is part of what's being
        // searched for. Runtime-only, nothing is persisted. Only shown when the material is
        // actually built (i.e. the loaded region uses it).
        var assetManager = LunaWindow.Instance.AssetManager;
        if (assetManager != null && assetManager.TryGetParallax(shader.TUID, out float parallaxScale, out float parallaxBias))
        {
            ImGui.SeparatorText(LM.Get("GUI_Frame_ShaderBrowser_LiveTuningSection"));
            if (!Program.Settings.EnableLighting)
                ImGui.TextDisabled(LM.Get("GUI_Frame_ShaderBrowser_ParallaxNeedsLighting"));

            // What the loader actually parsed out of the metadata, shown verbatim next to the live
            // controls. The 0x50/0x54 identification is a hypothesis: if these read as 0 across
            // every material, parallax legitimately does nothing, and without this line that is
            // indistinguishable from a rendering regression. New-engine shaders have no identified
            // parallax fields at all and always report 0/0 (see MaterialReader.GetParallaxScale).
            ImGui.TextDisabled(LM.Get("GUI_Frame_ShaderBrowser_ParallaxFromFile", MetadataParallaxScale(shader), MetadataParallaxBias(shader)));

            // min == max == 0 is ImGui's own spelling for "unbounded" — passing float.MinValue /
            // float.MaxValue instead overflows the internal (max - min) range calculation to
            // infinity and leaves the drag inert. Unbounded is deliberate: the sign is part of
            // what's being searched for, and a candidate straight out of the dump can be any
            // magnitude.
            bool changed = ImGui.DragFloat(LM.Get("GUI_Frame_ShaderBrowser_ParallaxScale"), ref parallaxScale, 0.001f, 0f, 0f, "%.6f");
            changed |= ImGui.DragFloat(LM.Get("GUI_Frame_ShaderBrowser_ParallaxBias"), ref parallaxBias, 0.001f, 0f, 0f, "%.6f");
            if (changed)
                assetManager.SetParallax(shader.TUID, parallaxScale, parallaxBias);

            // Back to what the FILE says, not to a hardcoded constant — the point of the sliders is
            // to deviate from the parsed value and come back to it.
            if (ImGui.SmallButton($"{LM.Get("GUI_Common_Reset")}##parallax_reset"))
                assetManager.SetParallax(shader.TUID, MetadataParallaxScale(shader), MetadataParallaxBias(shader));

            // The game weights each detail channel by its own fragment constant; none of the three
            // is located in ShaderMetadata yet, so these start at a neutral 1 and are here to be
            // hunted the same way parallax was. Detail only shows up at all where the expensive
            // map's alpha (the detail mask) is non-zero.
            if (assetManager.TryGetDetailStrengths(shader.TUID, out float detailNormal, out float detailSpec, out float detailTiling))
            {
                bool detailChanged = ImGui.DragFloat(LM.Get("GUI_Frame_ShaderBrowser_DetailNormalStrength"), ref detailNormal, 0.01f, 0f, 0f, "%.4f");
                // Clamped 0..1, unlike the others: this one rides a byte-quantised colour channel
                // now that slots 6/7 carry the baked lighting textures.
                detailChanged |= ImGui.DragFloat(LM.Get("GUI_Frame_ShaderBrowser_DetailSpecStrength"), ref detailSpec, 0.01f, 0f, 1f, "%.4f");
                // Detail maps are authored to tile above the base map's frequency; the real
                // multiplier isn't in the captured fragment shader (it arrives pre-tiled in a
                // vertex interpolant), so this is the knob for finding what it should be.
                detailChanged |= ImGui.DragFloat(LM.Get("GUI_Frame_ShaderBrowser_DetailTiling"), ref detailTiling, 0.1f, 0f, 0f, "%.3f");
                if (detailChanged)
                    assetManager.SetDetailStrengths(shader.TUID, detailNormal, detailSpec, detailTiling);

                // Resets to what a fresh material build produces: normal/spec/tiling from the file,
                // albedo pinned off (see AssetManager.ForcedDetailAlbedoStrength).
                if (ImGui.SmallButton($"{LM.Get("GUI_Common_Reset")}##detail_reset"))
                    assetManager.SetDetailStrengths(shader.TUID,
                        MetadataDetailFloat(shader, static m => m.detailNormalStrength),
                        MetadataDetailFloat(shader, static m => m.detailSpecStrength),
                        MetadataDetailTiling(shader));
            }
        }

        ImGui.SeparatorText(LM.Get("GUI_Frame_ShaderBrowser_RawMetadataSection"));
        ImGui.Checkbox(LM.Get("GUI_Frame_ShaderBrowser_ShowAsFloats"), ref showFloatInterpretation);
        if (shader.isOld && shader.metadataOld.HasValue)
        {
            var meta = shader.metadataOld.Value;
            // Raw byte AND decoded flags, deliberately both: the bit walk direction is a
            // convention inherited from InsomniaToolset's x86 build (see ShaderMetadataOld.flags).
            // Comparing "Detail" here against whether DetailMap above is actually present, across
            // a few materials, is what confirms or reverses it.
            ImGui.Text($"0x10 flags: 0x{meta.flags:X2} (binary {Convert.ToString(meta.flags, 2).PadLeft(8, '0')})");
            // Parallax, not specular — see ShaderMetadataOld.UsesParallax. Printed next to
            // parallaxScale below so the two can be compared across materials, which is what
            // confirms the bit.
            ImGui.Text($"     Parallax:{meta.UsesParallax} Gloss:{meta.UsesGlossiness} Normal:{meta.UsesNormalMap} Detail:{meta.UsesDetailMap}");
            ImGui.Text($"0x12 Class: {meta.Class}");
            DrawHexDump("Unk1", 0x13, meta.Unk1);
            // Printed as a float as well as hex: this is the candidate slot for the detail-strength
            // triple starting one float earlier (0x24/0x28/0x2C instead of 0x28/0x2C/0x30), so it
            // needs to be directly comparable against the three below.
            DrawHexDump("Unk2a", 0x24, meta.Unk2a);
            ImGui.Text($"0x28 detailNormalStrength: {meta.detailNormalStrength:0.######}");
            ImGui.Text($"0x2C detailSpecStrength:   {meta.detailSpecStrength:0.######}");
            ImGui.Text($"0x30 detailAlbedoStrength: {meta.detailAlbedoStrength:0.######}");
            DrawHexDump("Unk2b", 0x34, meta.Unk2b);
            ImGui.Text($"0x50 parallaxScale: {meta.parallaxScale:0.######}");
            ImGui.Text($"0x54 parallaxBias:  {meta.parallaxBias:0.######}");
            ImGui.Text($"0x58 detailTiling:  {meta.detailTiling:0.######}");
            DrawHexDump("Unk3", 0x5C, meta.Unk3);
        }
        else if (!shader.isOld && shader.metadataNew.HasValue)
        {
            var meta = shader.metadataNew.Value;
            DrawHexDump("Unk1", 0x0C, meta.Unk1);
            DrawHexDump("Unk2", 0x22, meta.Unk2);
            DrawHexDump("Unk3", 0x34, meta.Unk3);
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
        // BitConverter read would silently byte-swap every value. Alignment is to the FILE's
        // absolute offset, not to the start of this array: several of these ranges begin at an
        // unaligned offset (e.g. 0x13, 0x22), and a float field in the real structure sits on a
        // real 4-byte boundary, so aligning to the array start would show every such value split
        // across two entries. If a field turns out to start at an unaligned byte this still won't
        // show it, but 4-byte alignment is the overwhelmingly common case for game data structures
        // and keeps this from being an unreadable wall of every byte offset.
        var floatSb = new StringBuilder();
        int firstAligned = (4 - (baseOffset & 3)) & 3;
        for (int i = firstAligned; i + 3 < data.Length; i += 4)
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
