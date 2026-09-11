using ReLunacy.Engine.Rendering.Resources;
using ReLunacy.Core.Selection;
using ReLunacy.Engine.Assets.Cubemaps;
using ReLunacy.Engine.Assets.Interfaces;
using ReLunacy.Engine.Assets.Mobys;
using ReLunacy.Engine.Assets.Ties;
using ReLunacy.Engine.Loading.Shaders;
using ReLunacy.Engine.Rendering;
using ReLunacy.Engine.Scene;
using ReLunacy.Utility;
using ReLunacy.Utility.Localization;
using System.Numerics;

namespace ReLunacy.Core.Frames.DockedFrames;

public record struct TextureObject
{
    public TextureObject(ITexture texture, GpuTexture tex2d, int index)
    {
        Texture = texture;
        Index = index;
        TexturePtr = LunaWindow.Instance.imGuiController.GetOrCreateImGuiBinding(LunaWindow.Instance.GraphicsDevice.ResourceFactory, tex2d.DeviceTexture);
        GpuTexture = tex2d;
    }

    public readonly string? TextureName => Texture.Name;

    /// <summary>Position in the level's texture table, counted in load order - the number the file
    /// formats reference textures by (e.g. foliage's 0xA200 record). Shown even when a debug name
    /// was recovered.</summary>
    public readonly int Index;

    public readonly ITexture Texture;
    public readonly GpuTexture GpuTexture;
    public readonly ImTextureRef TexturePtr;
}

public class TexturesExplorer : DockedFrame, ILevelListener
{
    protected override ImGuiCond DockingConditions { get; set; } = ImGuiCond.Appearing;
    protected override Vector2 DefaultPosition { get; set; } = ImGui.GetWorkCenter(ImGui.GetMainViewport());
    protected override ImGuiWindowFlags WindowFlags { get; set; } = ImGuiWindowFlags.NoScrollbar;

    private string inputText = "";

    private List<TextureObject> textureObjects = [];

    private int selectedTexture = -1;
    private ImTextureRef selectedTexturePtr;
    private TextureUsageResult? textureUsageResults;
    private List<Shader>? relatedShaders;
    // Owned by us (unlike TextureObject.GpuTexture, which AssetManager owns): built on demand
    // when a channel-preview button is clicked, must be disposed before being replaced/dropped.
    private GpuTexture? channelPreviewTexture;

    // Environment cubemaps (section 0x5920). Their faces aren't in AssetManager's texture table, so
    // the preview textures here are built and owned by this frame (see BuildCubemapFace/RebuildCubemap).
    private readonly List<CubemapObject> cubemapObjects = [];

    // The cubemap's real signal is a shared HDR exponent in the alpha channel, so a plain RGB view
    // reads as near-white - HDR exposes rgb * 2^((a-128)/16 * exposure) tonemapped, which is what
    // actually shows the environment. The single channels are the raw decoded bytes, grayscale.
    private enum CubemapChannel { Hdr, Rgb, R, G, B, A }

    private sealed class CubemapObject(Cubemap cubemap)
    {
        public readonly Cubemap Cubemap = cubemap;
        public float Exposure = 1f;
        public CubemapChannel Channel = CubemapChannel.Hdr;
        public bool Dirty = true;
        public readonly GpuTexture?[] FaceTextures = new GpuTexture?[cubemap.Faces.Count];
        public readonly ImTextureRef[] FacePtrs = new ImTextureRef[cubemap.Faces.Count];

        public void DisposeFaces()
        {
            for (int i = 0; i < FaceTextures.Length; i++)
            {
                FaceTextures[i]?.Dispose();
                FaceTextures[i] = null;
            }
        }
    }

    private enum UsageFilter { All, Used, Unused }
    private UsageFilter textureUsageFilter = UsageFilter.All;

    // "Used" = referenced by at least one loaded Moby/Tie/UFrag material - same definition
    // FindTextureUsages below already answers per-texture on click; computed once per
    // TransmitTextures call instead of re-scanning every asset for every texture every frame.
    private HashSet<ulong> usedTextureIds = [];

    private sealed record TextureUsageResult(List<Moby> Mobys, List<Tie> Ties, List<IUFrag> UFrags)
    {
        public bool IsEmpty => Mobys.Count == 0 && Ties.Count == 0 && UFrags.Count == 0;
    }

    public TexturesExplorer() : base()
    {
        FrameName = LM.Get("GUI_Frame_TextureExplorer");
    }

    public void TransmitTextures(AssetManager assetManager)
    {
        textureObjects.Clear();
        // The counter advances for EVERY source texture, including ones with no built Texture2D to
        // show - skipping those would silently renumber everything after them, and the whole point
        // of the index is that it matches the position the file formats reference.
        int index = 0;
        foreach (var (id, tex) in assetManager.SourceTextures)
        {
            if (assetManager.BuiltTextures.TryGetValue(id, out var tex2d))
                textureObjects.Add(new(tex, tex2d, index));
            index++;
        }

        usedTextureIds = ComputeUsedTextureIds();
    }

    private void TransmitCubemaps()
    {
        foreach (var obj in cubemapObjects) obj.DisposeFaces();
        cubemapObjects.Clear();

        var level = LunaWindow.Instance.Level;
        if (level == null) return;

        foreach (var cubemap in level.Cubemaps)
            cubemapObjects.Add(new CubemapObject(cubemap)); // face textures built lazily on first render
    }

    /// <summary>textureObjects wraps AssetManager-owned Texture2Ds that are about to be disposed -
    /// drop the reference before that happens rather than leaving a stale/dangling entry showing.</summary>
    public void OnLevelUnloading()
    {
        textureObjects.Clear();
        usedTextureIds.Clear();
        selectedTexture = -1;
        textureUsageResults = null;
        relatedShaders = null;
        channelPreviewTexture?.Dispose();
        channelPreviewTexture = null;

        foreach (var obj in cubemapObjects) obj.DisposeFaces();
        cubemapObjects.Clear();
    }

    /// <summary>Rebuilds selectedTexturePtr as a grayscale view of a single channel of the
    /// currently selected texture's decoded RGBA - lets the user visually confirm whether a
    /// texture actually carries real alpha data instead of guessing from the format alone.</summary>
    private void ShowChannel(TextureObject selection, ReLunacy.Engine.Rendering.TextureUtils.Colours channel)
    {
        byte[]? rgba = ReLunacy.Engine.Rendering.TextureUtils.DecodeToRgba8888(selection.Texture, out int width, out int height);
        if (rgba == null) return;

        byte[] filtered = ReLunacy.Engine.Rendering.TextureUtils.ColourAsMain(rgba, channel);

        channelPreviewTexture?.Dispose();
        channelPreviewTexture = new GpuTexture(LunaWindow.Instance.GraphicsDevice, (uint)width, (uint)height, filtered);
        selectedTexturePtr = LunaWindow.Instance.imGuiController.GetOrCreateImGuiBinding(LunaWindow.Instance.GraphicsDevice.ResourceFactory, channelPreviewTexture.DeviceTexture);
    }

    // Cross cell (row, col) for each face, parallel to Cubemap.FaceNames (+X,-X,+Y,-Y,+Z,-Z) - a
    // standard horizontal cross, the same arrangement the RenderDoc reference used.
    private static readonly (int Row, int Col)[] CrossCells =
        [(1, 2), (1, 0), (0, 1), (2, 1), (1, 1), (1, 3)];

    private void RenderCubemaps()
    {
        if (cubemapObjects.Count == 0) return;
        if (!ImGui.CollapsingHeader(LM.Get("GUI_Frame_TextureExplorer_Cubemaps", cubemapObjects.Count), ImGuiTreeNodeFlags.DefaultOpen))
            return;

        string[] channelLabels = ["HDR", "RGB", "R", "G", "B", "A"];

        for (int ci = 0; ci < cubemapObjects.Count; ci++)
        {
            var obj = cubemapObjects[ci];
            ImGui.PushID(ci);

            for (int i = 0; i < channelLabels.Length; i++)
            {
                if (i > 0) ImGui.SameLine();
                if (ImGui.RadioButton(channelLabels[i], (int)obj.Channel == i))
                {
                    obj.Channel = (CubemapChannel)i;
                    obj.Dirty = true;
                }
            }

            if (obj.Channel == CubemapChannel.Hdr)
            {
                float exposure = obj.Exposure;
                ImGui.SetNextItemWidth(220);
                if (ImGui.SliderFloat(LM.Get("GUI_Frame_TextureExplorer_Cubemap_Exposure"), ref exposure, 0.1f, 4f))
                {
                    obj.Exposure = exposure;
                    obj.Dirty = true;
                }
            }

            if (obj.Dirty) RebuildCubemap(obj);

            for (int f = 0; f < obj.Cubemap.Faces.Count; f++)
            {
                if (f > 0) ImGui.SameLine();
                ImGui.BeginGroup();
                if (obj.FaceTextures[f] != null)
                    ImGui.Image(obj.FacePtrs[f], new Vector2(96, 96), Vector2.UnitY, Vector2.UnitX);
                else
                    ImGui.Dummy(new Vector2(96, 96));
                ImGui.Text(Cubemap.FaceNames[f]);
                ImGui.EndGroup();
            }

            ImGui.Text(LM.Get("GUI_Frame_TextureExplorer_Cubemap_Info", obj.Cubemap.FaceSize, obj.Cubemap.Faces.Count));
            if (ImGui.Button(LM.Get("GUI_Frame_TextureExplorer_Cubemap_ExportCross")))
                ExportCubemapCross(obj);

            ImGui.PopID();
            ImGui.Separator();
        }
    }

    private static void RebuildCubemap(CubemapObject obj)
    {
        obj.Dirty = false;
        var gd = LunaWindow.Instance.GraphicsDevice;

        for (int f = 0; f < obj.Cubemap.Faces.Count; f++)
        {
            obj.FaceTextures[f]?.Dispose();
            obj.FaceTextures[f] = null;

            byte[]? rgba = BuildCubemapFace(obj.Cubemap.Faces[f], obj.Channel, obj.Exposure, out int w, out int h);
            if (rgba == null) continue;

            obj.FaceTextures[f] = new GpuTexture(gd, (uint)w, (uint)h, rgba, mipmap: false);
            obj.FacePtrs[f] = LunaWindow.Instance.imGuiController.GetOrCreateImGuiBinding(gd.ResourceFactory, obj.FaceTextures[f]!.DeviceTexture);
        }
    }

    /// <summary>Decodes one cubemap face and applies the current view mode. HDR reconstructs the
    /// probe's brightness from the alpha exponent (rgb * 2^((a-128)/16 * exposure)) and tonemaps it;
    /// the single-channel modes are the raw decoded bytes shown grayscale, same as the texture
    /// channel preview.</summary>
    private static byte[]? BuildCubemapFace(ITexture face, CubemapChannel channel, float exposure, out int w, out int h)
    {
        byte[]? rgba = ReLunacy.Engine.Rendering.TextureUtils.DecodeToRgba8888(face, out w, out h);
        if (rgba == null) return null;

        var result = new byte[rgba.Length];
        for (int i = 0; i < rgba.Length; i += 4)
        {
            byte r = rgba[i], g = rgba[i + 1], b = rgba[i + 2], a = rgba[i + 3];
            switch (channel)
            {
                case CubemapChannel.Hdr:
                    float e = MathF.Pow(2f, (a - 128) / 16f * exposure);
                    result[i + 0] = Tonemap(r / 255f * e);
                    result[i + 1] = Tonemap(g / 255f * e);
                    result[i + 2] = Tonemap(b / 255f * e);
                    result[i + 3] = 255;
                    break;
                case CubemapChannel.Rgb:
                    result[i + 0] = r; result[i + 1] = g; result[i + 2] = b; result[i + 3] = 255;
                    break;
                default:
                    byte v = channel switch
                    {
                        CubemapChannel.R => r,
                        CubemapChannel.G => g,
                        CubemapChannel.B => b,
                        _ => a,
                    };
                    result[i + 0] = v; result[i + 1] = v; result[i + 2] = v; result[i + 3] = 255;
                    break;
            }
        }

        return result;
    }

    // Reinhard tonemap + gamma, so HDR values above 1 roll off instead of clipping flat white.
    private static byte Tonemap(float c)
    {
        c = c / (1f + c);
        return (byte)(Math.Clamp(MathF.Pow(c, 1f / 2.2f), 0f, 1f) * 255f);
    }

    private static void ExportCubemapCross(CubemapObject obj)
    {
        int fs = obj.Cubemap.FaceSize;
        var cross = new byte[4 * fs * 3 * fs * 4]; // 4x3 grid of faces, RGBA, transparent by default

        for (int f = 0; f < obj.Cubemap.Faces.Count; f++)
        {
            byte[]? face = BuildCubemapFace(obj.Cubemap.Faces[f], obj.Channel, obj.Exposure, out int w, out int h);
            if (face == null || w != fs || h != fs) continue;

            var (row, col) = CrossCells[f];
            for (int y = 0; y < fs; y++)
            {
                int srcRow = y * fs * 4;
                int dstRow = ((row * fs + y) * (4 * fs) + col * fs) * 4;
                Array.Copy(face, srcRow, cross, dstRow, fs * 4);
            }
        }

        var path = Path.Combine(Program.EditorPath, "Extracted");
        if (!Directory.Exists(path)) Directory.CreateDirectory(path);
        new Image(4 * fs, 3 * fs, cross).SaveAsPng(Path.Combine(path, $"Cubemap_{obj.Cubemap.Id:X}_cross.png"));
    }

    /// <summary>Same "referenced by a loaded Moby/Tie/UFrag material" definition as
    /// MaterialUsesTexture/FindTextureUsages below, just collected in one pass over every asset
    /// instead of one scan per texture - building this once for potentially thousands of textures
    /// the way FindTextureUsages does per-click would be O(textures x assets).</summary>
    private static HashSet<ulong> ComputeUsedTextureIds()
    {
        var used = new HashSet<ulong>();
        var level = LunaWindow.Instance.Level;
        if (level == null) return used;

        static void AddMaterial(HashSet<ulong> used, IMaterial mat)
        {
            if (mat.AlbedoTexture != null) used.Add(mat.AlbedoTexture.Id);
            if (mat.NormalTexture != null) used.Add(mat.NormalTexture.Id);
            if (mat.PropertiesTexture != null) used.Add(mat.PropertiesTexture.Id);
        }

        foreach (var moby in level.Mobys.Values)
            foreach (var bangle in moby.Bangles)
                foreach (var mesh in bangle.Meshes)
                    AddMaterial(used, mesh.Material);

        foreach (var tie in level.Ties.Values)
            foreach (var mesh in tie.Meshes)
                AddMaterial(used, mesh.Material);

        foreach (var zone in level.Zones.Values)
            foreach (var ufrag in zone.UFrags)
                AddMaterial(used, ufrag.Material);

        return used;
    }

    public void OnLevelLoaded()
    {
        if (LunaWindow.Instance.AssetManager != null)
            TransmitTextures(LunaWindow.Instance.AssetManager);
        TransmitCubemaps();
    }

    /// <summary>Selects the texture with the given asset id, e.g. when jumping here from another frame. Returns false if it isn't in the currently transmitted set.</summary>
    public bool SelectTexture(ulong textureId)
    {
        int index = textureObjects.FindIndex(t => t.Texture.Id == textureId);
        if (index == -1) return false;

        selectedTexture = index;
        selectedTexturePtr = textureObjects[index].TexturePtr;
        textureUsageResults = null;
        relatedShaders = null;
        channelPreviewTexture?.Dispose();
        channelPreviewTexture = null;
        return true;
    }

    // Texture names come straight from the game's own string tables, which for some formats
    // (e.g. new-engine shader-referenced texture names) are full slash-delimited asset paths,
    // not bare filenames - writing that as-is into Path.Combine either creates unwanted nested
    // directories under Extracted/ or fails outright. Keep only the last path segment, and fall
    // back to the texture's index (not e.g. "unnamed") when it has no name at all.
    private static string GetExportFileName(string? textureName, int index) =>
        string.IsNullOrEmpty(textureName) ? $"Tex_{index}" : textureName.Split('/')[^1];

    private static bool MaterialUsesTexture(IMaterial mat, ulong textureId) =>
        mat.AlbedoTexture?.Id == textureId ||
        mat.NormalTexture?.Id == textureId ||
        mat.PropertiesTexture?.Id == textureId;

    private static bool MobyUsesTexture(Moby moby, ulong textureId) =>
        moby.Bangles.Any(bangle => bangle.Meshes.Any(mesh => MaterialUsesTexture(mesh.Material, textureId)));

    private static bool TieUsesTexture(Tie tie, ulong textureId) =>
        tie.Meshes.Any(mesh => MaterialUsesTexture(mesh.Material, textureId));

    private static TextureUsageResult FindTextureUsages(ulong textureId)
    {
        var level = LunaWindow.Instance.Level;
        if (level == null) return new TextureUsageResult([], [], []);

        var mobys = level.Mobys.Values.Where(m => MobyUsesTexture(m, textureId)).ToList();
        var ties = level.Ties.Values.Where(t => TieUsesTexture(t, textureId)).ToList();
        // UFrags carry a single Material directly (no per-mesh loop - a UFrag is one mesh).
        var ufrags = level.Zones.Values
            .SelectMany(z => z.UFrags)
            .Where(u => MaterialUsesTexture(u.Material, textureId))
            .ToList();

        return new TextureUsageResult(mobys, ties, ufrags);
    }

    // Raw shaders, not materials - a texture can be referenced by a shader that isn't actually
    // used by any loaded mesh (cut content), which FindTextureUsages above wouldn't find at all
    // since it only walks placed Mobys/Ties/UFrags. Level.Shaders carries every shader the loader
    // parsed regardless of whether it's reachable from loaded geometry (see LevelData.Shaders).
    private static List<Shader> FindRelatedShaders(ulong textureId)
    {
        var level = LunaWindow.Instance.Level;
        if (level == null) return [];

        return level.Shaders.Values
            .Where(s => s.Albedo?.id == textureId || s.Normal?.id == textureId || s.Expensive?.id == textureId)
            .ToList();
    }

    private static void OpenShaderInBrowser(ulong tuid)
    {
        var browser = LunaWindow.Instance.GetFirstFrame<ShaderBrowser>();
        if (browser == null)
        {
            browser = new ShaderBrowser();
            LunaWindow.Instance.AddFrame(browser);
        }
        if (LunaWindow.Instance.Level != null)
            browser.TransmitShaders(LunaWindow.Instance.Level);
        browser.SelectShader(tuid);
        browser.Focus();
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

    // UFrags are baked per-zone terrain, not a browsable asset catalog like Mobys/Ties - the
    // coherent selection target is the scene entity already loaded in the 3D view. Matched by
    // reference, not Id: IUFrag.Id is only unique within its own zone.
    private static void SelectUFragInView3D(IUFrag ufrag)
    {
        var entity = EntityManager.Singleton.AllEntities().OfType<EntityUFrag>().FirstOrDefault(e => ReferenceEquals(e.UFrag, ufrag));
        if (entity == null) return;

        SelectionManager.Singleton.Select(entity);
        var v3d = LunaWindow.Instance.GetFirstFrame<View3D>();
        if (v3d != null) v3d.SelectedEntity = entity;
    }

    private IEnumerable<TextureObject> FilteredTextureObjects()
    {
        IEnumerable<TextureObject> objects = textureUsageFilter switch
        {
            UsageFilter.Used => textureObjects.Where(t => usedTextureIds.Contains(t.Texture.Id)),
            UsageFilter.Unused => textureObjects.Where(t => !usedTextureIds.Contains(t.Texture.Id)),
            _ => textureObjects,
        };

        if (string.IsNullOrWhiteSpace(inputText)) return objects;

        // A bare number matches the index exactly, so typing "1" finds texture #1 rather than
        // every name containing a 1 - that's the only way to look a texture up when all you have
        // is the number some other structure referenced it by. Anything else searches names.
        if (int.TryParse(inputText.Trim(), out int wantedIndex))
            return objects.Where(t => t.Index == wantedIndex);

        return objects.Where(t => (t.TextureName ?? "").Contains(inputText, StringComparison.OrdinalIgnoreCase));
    }

    protected override void Render(double deltaTime)
    {
        RenderCubemaps();

        ImGui.InputTextWithHint(LM.Get("GUI_Frame_TextureExplorer_SearchLabel"), LM.Get("GUI_Frame_TextureExplorer_SearchHint", textureObjects.Count), ref inputText, 128);

        int filter = (int)textureUsageFilter;
        ImGui.RadioButton(LM.Get("GUI_Common_FilterAll"), ref filter, (int)UsageFilter.All);
        ImGui.SameLine();
        ImGui.RadioButton(LM.Get("GUI_Common_FilterUsed"), ref filter, (int)UsageFilter.Used);
        ImGui.SameLine();
        ImGui.RadioButton(LM.Get("GUI_Common_FilterUnused"), ref filter, (int)UsageFilter.Unused);
        textureUsageFilter = (UsageFilter)filter;

        var filteredObjects = FilteredTextureObjects().ToList();

        if (ImGui.BeginChild("texture_gridview", new (ImGui.GetContentRegionAvail().X / 2, ImGui.GetContentRegionAvail().Y), ImGuiChildFlags.Borders))
        {
            var columns = (int)ImGui.GetContentRegionAvail().X / 128;
            if(columns >= 1)
            {
                ImGui.Columns(columns, "texture_grid", false);
                for (int i = 0; i < filteredObjects.Count; i++)
                {
                    var texobj = filteredObjects[i];
                    if (i > 0 && i % columns == 0) ImGui.Spacing();

                    ImGui.Image(texobj.TexturePtr, new(128, 128), Vector2.UnitY, Vector2.UnitX);
                    if (ImGui.IsItemClicked())
                    {
                        // Index into the FULL textureObjects list, not filteredObjects - the
                        // preview panel below indexes textureObjects[selectedTexture] directly, and
                        // filtering/searching can reorder or drop entries relative to it.
                        selectedTexture = textureObjects.FindIndex(t => t.Texture.Id == texobj.Texture.Id);
                        selectedTexturePtr = texobj.TexturePtr;
                        textureUsageResults = null;
                        relatedShaders = null;
                        channelPreviewTexture?.Dispose();
                        channelPreviewTexture = null;
                    }

                    bool isUsed = usedTextureIds.Contains(texobj.Texture.Id);
                    if (!isUsed) ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
                    // texobj.Index, never the loop counter: `i` walks the FILTERED list, so with a
                    // search or usage filter active it labelled textures with whatever position
                    // they happened to land on that frame.
                    ImGui.Text($"#{texobj.Index}");
                    if (texobj.TextureName is { Length: > 0 } name)
                    {
                        ImGui.SameLine();
                        ImGui.TextWrapped(name.Split('/')[^1]);
                    }
                    if (!isUsed) ImGui.PopStyleColor();

                    ImGui.NextColumn();
                }
            }
        }
        ImGui.EndChild();
        if(selectedTexture != -1)
        {
            ImGui.SameLine();
            // AlwaysVerticalScrollbar: reserves the scrollbar's space unconditionally, since the
            // preview image is sized from available width and a conditional scrollbar would
            // otherwise create an every-frame width/height feedback loop.
            if(ImGui.BeginChild("texture_preview", ImGui.GetContentRegionAvail(), ImGuiChildFlags.Borders, ImGuiWindowFlags.AlwaysVerticalScrollbar))
            {
                var selection = textureObjects[selectedTexture];

                ImGui.Image(selectedTexturePtr, new(ImGui.GetContentRegionAvail().X, ImGui.GetContentRegionAvail().X), Vector2.UnitY, Vector2.UnitX);
                ImGui.Text(LM.Get("GUI_Frame_TextureExplorer_Preview_SelectColorChannel"));
                ImGui.SameLine();
                // Optimizations will be done by making copies of these channels only when the texture is selected.
                if(ImGui.Button("All"))
                {
                    channelPreviewTexture?.Dispose();
                    channelPreviewTexture = null;
                    selectedTexturePtr = selection.TexturePtr;
                }
                ImGui.SameLine();
                if (ImGui.Button("R"))
                {
                    ShowChannel(selection, ReLunacy.Engine.Rendering.TextureUtils.Colours.Red);
                }
                ImGui.SameLine();
                if(ImGui.Button("G"))
                {
                    ShowChannel(selection, ReLunacy.Engine.Rendering.TextureUtils.Colours.Green);
                }
                ImGui.SameLine();
                if(ImGui.Button("B"))
                {
                    ShowChannel(selection, ReLunacy.Engine.Rendering.TextureUtils.Colours.Blue);
                }
                if(selection.Texture.Format != TextureFormat.DXT1 && selection.Texture.Format != TextureFormat.R5G6B5)
                {
                    ImGui.SameLine();
                    if (ImGui.Button("A"))
                    {
                        ShowChannel(selection, ReLunacy.Engine.Rendering.TextureUtils.Colours.Alpha);
                    }
                }
                ImGui.Separator();
                ImGui.BeginGroup();
                ImGui.Text(LM.Get("GUI_Frame_TextureExplorer_Preview_TextureIndex"));
                ImGui.Text(LM.Get("GUI_Frame_TextureExplorer_Preview_TextureName"));
                ImGui.Text(LM.Get("GUI_Frame_TextureExplorer_Preview_TextureCompressionType"));
                ImGui.Text(LM.Get("GUI_Frame_TextureExplorer_Preview_TextureDimensions"));
                ImGui.Text(LM.Get("GUI_Frame_TextureExplorer_Preview_TextureBufferSize"));
                ImGui.EndGroup();
                ImGui.SameLine();
                ImGui.BeginGroup();
                // Both numbers, because they answer different questions: Index is the position the
                // file formats reference a texture by, Id is where its metadata record physically
                // sits (the old engine uses the record's own offset in main.dat as its id).
                ImGui.Text($"{selection.Index}  (id 0x{selection.Texture.Id:X})");
                ImGui.Text(selection.TextureName is { Length: > 0 } n ? n : $"Tex_{selection.Index}");
                ImGui.Text(selection.Texture.Format.ToString());
                ImGui.Text($"{selection.Texture.Width}x{selection.Texture.Height}");
                ImGui.Text($"{selection.Texture.Width * selection.Texture.Height * 4 / 1000f}KB");
                ImGui.EndGroup();
                if(ImGui.Button(LM.Get("GUI_Frame_TextureExplorer_Preview_ExportRaw")))
                {
                    var path = Path.Combine(Program.EditorPath, "Extracted");
                    if (!Directory.Exists(path))
                        Directory.CreateDirectory(path);

                    File.WriteAllBytes(Path.Combine(path, GetExportFileName(selection.TextureName, selection.Index) + ".raw"), selection.Texture.GetPixelData());
                }
                ImGui.SameLine();
                if(ImGui.Button(LM.Get("GUI_Frame_TextureExplorer_Preview_ExportPNG")))
                {
                    var path = Path.Combine(Program.EditorPath, "Extracted");
                    if (!Directory.Exists(path))
                        Directory.CreateDirectory(path);

                    // Re-decoded from the source texture rather than read back off the GPU one: the
                    // GPU copy has a mip chain and no CPU-side pixels, and this is the same decode
                    // every other view in this frame does.
                    byte[]? pixels = ReLunacy.Engine.Rendering.TextureUtils.DecodeToRgba8888(selection.Texture, out int pw, out int ph);
                    if (pixels != null)
                        new Image(pw, ph, pixels).SaveAsPng(Path.Combine(path, GetExportFileName(selection.TextureName, selection.Index) + ".png"));
                }
                ImGui.Separator();
                if (ImGui.Button(LM.Get("GUI_Frame_TextureExplorer_Preview_FindUsages")))
                    textureUsageResults = FindTextureUsages(selection.Texture.Id);

                if (textureUsageResults != null)
                {
                    if (textureUsageResults.IsEmpty)
                    {
                        ImGui.TextDisabled(LM.Get("GUI_Frame_TextureExplorer_Preview_NoUsagesFound"));
                    }
                    else
                    {
                        if (textureUsageResults.Mobys.Count > 0)
                        {
                            ImGui.Text(LM.Get("GUI_Frame_TextureExplorer_Preview_UsagesMobys", textureUsageResults.Mobys.Count));
                            foreach (var moby in textureUsageResults.Mobys)
                            {
                                if (ImGui.Selectable($"{moby.Name ?? moby.Id.ToString("X")}##usage_moby_{moby.Id:X}"))
                                    OpenMobyInAssetViewer(moby.Id);
                            }
                        }
                        if (textureUsageResults.Ties.Count > 0)
                        {
                            ImGui.Text(LM.Get("GUI_Frame_TextureExplorer_Preview_UsagesTies", textureUsageResults.Ties.Count));
                            foreach (var tie in textureUsageResults.Ties)
                            {
                                if (ImGui.Selectable($"{tie.Name ?? tie.Id.ToString("X")}##usage_tie_{tie.Id:X}"))
                                    OpenTieInAssetViewer(tie.Id);
                            }
                        }
                        if (textureUsageResults.UFrags.Count > 0)
                        {
                            ImGui.Text(LM.Get("GUI_Frame_TextureExplorer_Preview_UsagesUFrags", textureUsageResults.UFrags.Count));
                            // Indexed, not keyed by ufrag.Id: IUFrag.Id is only unique within its
                            // own zone (see SelectUFragInView3D), so two results here can share
                            // an Id - using the list index keeps these ImGui ids unique instead.
                            for (int i = 0; i < textureUsageResults.UFrags.Count; i++)
                            {
                                var ufrag = textureUsageResults.UFrags[i];
                                if (ImGui.Selectable($"{ufrag.Name ?? ufrag.Id.ToString("X")}##usage_ufrag_{i}"))
                                    SelectUFragInView3D(ufrag);
                            }
                        }
                    }
                }

                ImGui.Separator();
                if (ImGui.Button(LM.Get("GUI_Frame_TextureExplorer_Preview_FindRelatedShaders")))
                    relatedShaders = FindRelatedShaders(selection.Texture.Id);

                if (relatedShaders != null)
                {
                    if (relatedShaders.Count == 0)
                    {
                        ImGui.TextDisabled(LM.Get("GUI_Frame_TextureExplorer_Preview_NoRelatedShaders"));
                    }
                    else
                    {
                        ImGui.Text(LM.Get("GUI_Frame_TextureExplorer_Preview_RelatedShaders", relatedShaders.Count));
                        foreach (var shader in relatedShaders)
                        {
                            string label = string.IsNullOrEmpty(shader.name) ? shader.TUID.ToString("X") : shader.name;
                            if (ImGui.Selectable($"{label}##related_shader_{shader.TUID:X}"))
                                OpenShaderInBrowser(shader.TUID);
                        }
                    }
                }
            }
            ImGui.EndChild();
        }
    }

    // No RenderAsWindow override - see ShaderBrowser's comment: SetNextWindowPos on first appearance
    // cancels the dockspace preset's placement, which this frame is a target of ("Texture").
}
