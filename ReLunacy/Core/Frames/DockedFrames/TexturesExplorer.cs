using Bliss.CSharp.Images;
using Bliss.CSharp.Textures;
using ReLunacy.Core.Selection;
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
    public TextureObject(ITexture texture, Texture2D tex2d)
    {
        Texture = texture;
        TexturePtr = LunaWindow.Instance.imGuiController.GetOrCreateImGuiBinding(LunaWindow.Instance.GraphicsDevice.ResourceFactory, tex2d.DeviceTexture);
        BlissTexture = tex2d;
    }

    public readonly string? TextureName => Texture.Name;
    public readonly ITexture Texture;
    public readonly Texture2D BlissTexture;
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
    // Owned by us (unlike TextureObject.BlissTexture, which AssetManager owns) — built on demand
    // when a channel-preview button is clicked, must be disposed before being replaced/dropped.
    private Texture2D? channelPreviewTexture;

    private enum UsageFilter { All, Used, Unused }
    private UsageFilter textureUsageFilter = UsageFilter.All;

    // "Used" = referenced by at least one loaded Moby/Tie/UFrag material — same definition
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
        foreach (var (id, tex) in assetManager.SourceTextures)
        {
            if (assetManager.BuiltTextures.TryGetValue(id, out var tex2d))
                textureObjects.Add(new(tex, tex2d));
        }

        usedTextureIds = ComputeUsedTextureIds();
    }

    /// <summary>textureObjects wraps AssetManager-owned Texture2Ds that are about to be disposed —
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
    }

    /// <summary>Rebuilds selectedTexturePtr as a grayscale view of a single channel of the
    /// currently selected texture's decoded RGBA — lets the user visually confirm whether a
    /// texture actually carries real alpha data instead of guessing from the format alone.</summary>
    private void ShowChannel(TextureObject selection, ReLunacy.Engine.Rendering.TextureUtils.Colours channel)
    {
        byte[]? rgba = ReLunacy.Engine.Rendering.TextureUtils.DecodeToRgba8888(selection.Texture, out int width, out int height);
        if (rgba == null) return;

        byte[] filtered = ReLunacy.Engine.Rendering.TextureUtils.ColourAsMain(rgba, channel);
        var image = new Image(width, height, filtered);

        channelPreviewTexture?.Dispose();
        channelPreviewTexture = new Texture2D(LunaWindow.Instance.GraphicsDevice, image, true);
        selectedTexturePtr = LunaWindow.Instance.imGuiController.GetOrCreateImGuiBinding(LunaWindow.Instance.GraphicsDevice.ResourceFactory, channelPreviewTexture.DeviceTexture);
    }

    /// <summary>Same "referenced by a loaded Moby/Tie/UFrag material" definition as
    /// MaterialUsesTexture/FindTextureUsages below, just collected in one pass over every asset
    /// instead of one scan per texture — building this once for potentially thousands of textures
    /// the way FindTextureUsages does per-click would be O(textures × assets).</summary>
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
        // UFrags carry a single Material directly (no per-mesh loop — a UFrag is one mesh).
        var ufrags = level.Zones.Values
            .SelectMany(z => z.UFrags)
            .Where(u => MaterialUsesTexture(u.Material, textureId))
            .ToList();

        return new TextureUsageResult(mobys, ties, ufrags);
    }

    // Raw shaders, not materials — a texture can be referenced by a shader that isn't actually
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

    // UFrags are baked per-zone terrain, not a browsable asset catalog like Mobys/Ties — the
    // coherent selection target for one is the scene entity already loaded in the 3D view.
    // Matched by reference, not Id: IUFrag.Id is only unique within its own zone (ZoneReader
    // assigns it as a local loop index), so two UFrags from different zones can share an Id —
    // EntityUFrag.UFrag holds the exact same IUFrag instance from LevelData.Zones though, so
    // reference equality is the one comparison that's actually unambiguous here.
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

        return string.IsNullOrWhiteSpace(inputText)
            ? objects
            : objects.Where(t => (t.TextureName ?? "").Contains(inputText, StringComparison.OrdinalIgnoreCase));
    }

    protected override void Render(double deltaTime)
    {
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
                        // Index into the FULL textureObjects list, not filteredObjects — the
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
                    ImGui.Text(texobj.TextureName ?? $"Tex_{i}");
                    if (!isUsed) ImGui.PopStyleColor();

                    ImGui.NextColumn();
                }
            }
        }
        ImGui.EndChild();
        if(selectedTexture != -1)
        {
            ImGui.SameLine();
            // AlwaysVerticalScrollbar: without it, the scrollbar's appearance depends on whether
            // the Find Usages results (a variable-length list) push content past the visible
            // height — but the image above is sized from ContentRegionAvail().X, so the
            // scrollbar showing up shrinks the available width, which shrinks the square image,
            // which shrinks total content height, which removes the need for a scrollbar next
            // frame, which grows the image back... an every-frame oscillation. Reserving the
            // scrollbar's space unconditionally keeps the available width constant regardless of
            // whether it's actually needed, breaking the feedback loop.
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
                ImGui.Text(LM.Get("GUI_Frame_TextureExplorer_Preview_TextureName"));
                ImGui.Text(LM.Get("GUI_Frame_TextureExplorer_Preview_TextureCompressionType"));
                ImGui.Text(LM.Get("GUI_Frame_TextureExplorer_Preview_TextureDimensions"));
                ImGui.Text(LM.Get("GUI_Frame_TextureExplorer_Preview_TextureBufferSize"));
                ImGui.EndGroup();
                ImGui.SameLine();
                ImGui.BeginGroup();
                ImGui.Text(selection.TextureName ?? $"Tex_{selectedTexture}");
                ImGui.Text(selection.Texture.Format.ToString());
                ImGui.Text($"{selection.Texture.Width}x{selection.Texture.Height}");
                ImGui.Text($"{selection.BlissTexture.Images[0].Data.Length / 1000f}KB");
                ImGui.EndGroup();
                if(ImGui.Button(LM.Get("GUI_Frame_TextureExplorer_Preview_ExportRaw")))
                {
                    var path = Path.Combine(Program.EditorPath, "Extracted");
                    if (!Directory.Exists(path))
                        Directory.CreateDirectory(path);

                    File.WriteAllBytes(Path.Combine(path, selection.TextureName != null ? selection.TextureName + ".raw" : $"Tex_{selectedTexture}.raw"), selection.Texture.GetPixelData());
                }
                ImGui.SameLine();
                if(ImGui.Button(LM.Get("GUI_Frame_TextureExplorer_Preview_ExportPNG")))
                {
                    var path = Path.Combine(Program.EditorPath, "Extracted");
                    if (!Directory.Exists(path))
                        Directory.CreateDirectory(path);

                    var clone = (Image)selection.BlissTexture.Images[0].Clone();
                    clone.SaveAsPng(Path.Combine(path, selection.TextureName != null ? selection.TextureName + ".png" : $"Tex_{selectedTexture}.png"));
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
                            // an Id — using the list index keeps these ImGui ids unique instead.
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

    public override void RenderAsWindow(double deltaTime)
    {
        ImGui.SetNextWindowPos(DefaultPosition, ImGuiCond.Appearing);
        base.RenderAsWindow(deltaTime);
    }
}
