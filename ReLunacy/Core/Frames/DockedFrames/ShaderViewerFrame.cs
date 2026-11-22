using System.Numerics;
using ReLunacy.Engine.Assets.Browser;
using ReLunacy.Engine.Assets.Interfaces;
using ReLunacy.Engine.Loading.Shaders;
using ReLunacy.Engine.Rendering;
using ReLunacy.Engine.Rendering.Resources;
using ReLunacy.Engine.Rendering.Vulkan;
using ReLunacy.Utility;
using ReLunacy.Utility.Localization;
using LegacyTexture = ReLunacy.Engine.Loading.Textures.Texture;
using LunaLog = ReLunacy.Utility.LunaLog;

namespace ReLunacy.Core.Frames.DockedFrames;

/// <summary>
/// One shader (material) of the loaded level, in a window of its own, as an engine's material editor: the material on a sphere,
/// cube or plane drawn by the level view's renderer through the same material path (as a probe-lit Moby, so the game's
/// program for that route is used when there is one), and tabs for its textures, details, a read-only graph of how its inputs
/// reach the output, the game program it runs, its animation and the raw record. Opened from <see cref="AssetOpener"/>; a
/// shader already open is focused instead of opened twice.
/// </summary>
public sealed partial class ShaderViewerFrame : DockedFrame, ILevelListener
{
    protected override ImGuiCond DockingConditions { get; set; } = ImGuiCond.Appearing;
    protected override Vector2 DefaultPosition { get; set; } = ImGui.GetWorkCenter(ImGui.GetMainViewport());
    protected override ImGuiWindowFlags WindowFlags { get; set; } = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;

    private static readonly List<ShaderViewerFrame> Viewers = [];

    private readonly ulong _tuid;
    private Shader? _shader;
    private bool _crackInTime;
    private IMaterial? _material;
    private RenderMaterial? _renderMaterial;

    private float _previewWidth = 380f;
    private const float SplitterThickness = 6f;

    /// <summary>Opens the viewer of a shader, or focuses it when it is already open. False when the level has no such shader.</summary>
    public static bool Show(ulong tuid)
    {
        Viewers.RemoveAll(v => !v.isOpen);
        if (Viewers.FirstOrDefault(v => v._tuid == tuid) is { } open)
        {
            open.Focus();
            return true;
        }
        if (LunaWindow.Instance.Level?.Shaders.ContainsKey(tuid) != true) return false;
        var viewer = new ShaderViewerFrame(tuid);
        Viewers.Add(viewer);
        LunaWindow.Instance.AddFrame(viewer);
        viewer.Focus();
        return true;
    }

    private ShaderViewerFrame(ulong tuid)
    {
        _tuid = tuid;
        Camera = NewCamera();
        Bind();
    }

    /// <summary>Takes the shader from the loaded level (again, after a level change).</summary>
    private void Bind()
    {
        var level = LunaWindow.Instance.Level;
        _shader = level != null && level.Shaders.TryGetValue(_tuid, out var shader) ? shader : null;
        _crackInTime = level?.GameDefinition?.Id == Engine.Games.GameId.ACrackInTime;
        _material = _shader != null ? LunaWindow.Instance.Reader?.FindShaderMaterial(_tuid) : null;
        _renderMaterial = null;
        _programs = null;
        _graphDirty = true;
        _previewDirty = true;
        string name = _shader == null ? $"{_tuid:X}" : DisplayName(_shader);
        FrameName = $"{Icons.Palette} {name}";
    }

    /// <summary>The Assets Browser entry of this shader: its Folder / Name carry the full name (the files cut the first
    /// characters of a shader's name; the index recovers them).</summary>
    private AssetEntry? Entry() => AssetsBrowserFrame.EnsureIndex()?.Get(new AssetKey(AssetKind.Shader, _tuid));

    private string DisplayName(Shader shader) => Entry()?.Name ?? ShortName(shader);

    private string FullPath(Shader shader) => Entry()?.Path ?? (string.IsNullOrEmpty(shader.name) ? "-" : shader.name);

    private static string ShortName(Shader shader)
    {
        if (string.IsNullOrEmpty(shader.name)) return $"{shader.TUID:X}";
        int slash = shader.name.LastIndexOfAny(['/', '\\']);
        return slash >= 0 && slash < shader.name.Length - 1 ? shader.name[(slash + 1)..] : shader.name;
    }

    public void OnLevelUnloading()
    {
        _shader = null;
        _material = null;
        _renderMaterial = null;
        _programs = null;
        _uvAnimations = null;
        // The image may already be queued in this frame's draw list: freed on the next Render.
        _disposePending = true;
    }

    public void OnLevelLoaded() => Bind();

    private RenderMaterial? EnsureRenderMaterial()
    {
        if (_renderMaterial != null || _material == null) return _renderMaterial;
        var assets = LunaWindow.Instance.AssetManager;
        if (assets == null) return null;
        try { _renderMaterial = assets.GetOrBuildMaterial(_material); }
        catch (Exception e) { LunaLog.LogError($"[ShaderViewer] material 0x{_tuid:X} failed: {e.Message}"); }
        return _renderMaterial;
    }

    public override void RenderAsWindow(double deltaTime)
    {
        ImGui.SetNextWindowSize(new Vector2(1100f, 680f), ImGuiCond.FirstUseEver);
        base.RenderAsWindow(deltaTime);
        // Closed this frame: the image was not drawn (Render checks isOpen first), so the renderer can go now.
        if (!isOpen)
        {
            DisposePreview();
            Viewers.Remove(this);
        }
    }

    protected override void Render(double deltaTime)
    {
        if (_disposePending)
        {
            DisposePreview();
            _disposePending = false;
        }
        if (!isOpen) return;

        if (_shader == null)
        {
            ImGui.TextDisabled(LM.Get("GUI_Frame_ShaderViewer_NotLoaded"));
            return;
        }

        var avail = ImGui.GetContentRegionAvail();
        _previewWidth = Math.Clamp(_previewWidth, 160f, MathF.Max(160f, avail.X - 260f));

        if (ImGui.BeginChild("##shader_preview", new Vector2(_previewWidth, avail.Y), ImGuiChildFlags.None, ImGuiWindowFlags.NoScrollbar))
            DrawPreview(deltaTime);
        ImGui.EndChild();

        ImGui.SameLine(0, 0);
        ImGui.Button("##shader_split", new Vector2(SplitterThickness, avail.Y));
        if (ImGui.IsItemHovered() || ImGui.IsItemActive())
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEw);
        if (ImGui.IsItemActive())
            _previewWidth += ImGui.GetIO().MouseDelta.X;
        ImGui.SameLine(0, 0);

        if (ImGui.BeginChild("##shader_tabs", ImGui.GetContentRegionAvail(), ImGuiChildFlags.None))
            DrawTabs(_shader);
        ImGui.EndChild();
    }

    private void DrawTabs(Shader shader)
    {
        if (!ImGui.BeginTabBar("##shader_viewer_tabs")) return;

        if (ImGui.BeginTabItem(LM.Get("GUI_Frame_ShaderViewer_Tab_Details")))
        {
            if (ImGui.BeginChild("##details")) DrawDetails(shader);
            ImGui.EndChild();
            ImGui.EndTabItem();
        }
        if (ImGui.BeginTabItem(LM.Get("GUI_Frame_ShaderViewer_Tab_Textures")))
        {
            if (ImGui.BeginChild("##textures")) DrawTextures(shader);
            ImGui.EndChild();
            ImGui.EndTabItem();
        }
        if (ImGui.BeginTabItem(LM.Get("GUI_Frame_ShaderViewer_Tab_Graph")))
        {
            DrawGraphTab(shader);
            ImGui.EndTabItem();
        }
        if (!shader.isOld && ImGui.BeginTabItem(LM.Get("GUI_Frame_ShaderViewer_Tab_Program")))
        {
            DrawProgramTab(shader);
            ImGui.EndTabItem();
        }
        if (HasAnimation(shader) && ImGui.BeginTabItem(LM.Get("GUI_Frame_ShaderViewer_Tab_Animation")))
        {
            if (ImGui.BeginChild("##animation")) DrawAnimation(shader);
            ImGui.EndChild();
            ImGui.EndTabItem();
        }
        if (ImGui.BeginTabItem(LM.Get("GUI_Frame_ShaderViewer_Tab_Raw")))
        {
            if (ImGui.BeginChild("##raw")) DrawRaw(shader);
            ImGui.EndChild();
            ImGui.EndTabItem();
        }
        ImGui.EndTabBar();
    }

    // ---------------------------------------------------------------- textures

    private static readonly string[] SlotKeys =
    [
        "GUI_Frame_ShaderViewer_Slot_Albedo", "GUI_Frame_ShaderViewer_Slot_Normal", "GUI_Frame_ShaderViewer_Slot_Expensive",
        "GUI_Frame_ShaderViewer_Slot_Detail", "GUI_Frame_ShaderViewer_Slot_4", "GUI_Frame_ShaderViewer_Slot_5",
    ];

    private static string SlotName(int slot) => LM.Get(SlotKeys[slot]);

    private static int SlotCount(Shader shader) => shader.isOld ? 4 : Shader.NewTextureSlotCount;

    private static LegacyTexture? SlotTexture(Shader shader, int slot) => slot switch
    {
        0 => shader.Albedo,
        1 => shader.Normal,
        2 => shader.Expensive,
        3 => shader.DetailMap,
        4 => shader.Auxiliary4,
        5 => shader.Auxiliary5,
        _ => null,
    };

    private static string TextureName(LegacyTexture texture) =>
        string.IsNullOrEmpty(texture.name) ? $"{texture.id:X}" : texture.name;

    /// <summary>The texture's image in the GPU cache under its own id or one of its binding wrappers; null while not built.</summary>
    private static ImTextureRef? TextureImage(LegacyTexture texture)
    {
        var window = LunaWindow.Instance;
        if (window.AssetManager is not { } assets) return null;
        foreach (ulong id in AssetOpener.TextureIds(texture.id))
            if (assets.BuiltTextures.TryGetValue(id, out var gpu))
                return window.imGuiController.GetOrCreateImGuiBinding(window.GraphicsDevice.ResourceFactory, gpu.DeviceTexture);
        return null;
    }

    private static void OpenTexture(ulong id)
    {
        var index = AssetsBrowserFrame.EnsureIndex();
        if (index == null) return;
        var entry = index.Get(new AssetKey(AssetKind.Texture, index.TextureSource(id))) ?? index.Get(new AssetKey(AssetKind.Texture, id));
        if (entry != null) AssetOpener.Open(entry);
    }

    private static string ChannelsOf(int slot) => slot switch
    {
        2 => LM.Get("GUI_Frame_ShaderViewer_Channels_Expensive"),
        3 => LM.Get("GUI_Frame_ShaderViewer_Channels_Detail"),
        _ => "",
    };

    private void DrawTextures(Shader shader)
    {
        const float thumb = 96f;
        if (!ImGui.BeginTable("##slots", 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp)) return;
        ImGui.TableSetupColumn("##thumb", ImGuiTableColumnFlags.WidthFixed, thumb + 8f);
        ImGui.TableSetupColumn("##info", ImGuiTableColumnFlags.WidthStretch);
        for (int slot = 0; slot < SlotCount(shader); slot++)
        {
            var texture = SlotTexture(shader, slot);
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.PushID(slot);
            if (texture != null && TextureImage(texture) is { } image)
            {
                ImGui.Image(image, new Vector2(thumb, thumb), Vector2.UnitY, Vector2.UnitX);
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_Frame_ShaderViewer_OpenTexture"));
                if (ImGui.IsItemClicked()) OpenTexture(texture.id);
            }
            else
            {
                ImGui.Dummy(new Vector2(thumb, thumb));
                var min = ImGui.GetItemRectMin();
                ImGui.GetWindowDrawList().AddRect(min, min + new Vector2(thumb, thumb), ImGui.GetColorU32(ImGuiCol.Border), 4f);
            }

            ImGui.TableSetColumnIndex(1);
            ImGui.TextUnformatted(SlotName(slot));
            if (texture == null)
            {
                ImGui.TextDisabled(LM.Get("GUI_Frame_ShaderViewer_Empty"));
            }
            else
            {
                if (ImGui.Selectable($"{TextureName(texture)}##tex{slot}"))
                    OpenTexture(texture.id);
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_Frame_ShaderViewer_OpenTexture"));
                ImGui.TextDisabled($"{texture.Width} x {texture.Height}  {texture.TexFormat}  0x{texture.id:X}");
                if (!shader.isOld && shader.NewTextureDescriptors[slot] is { } d)
                    ImGui.TextDisabled(LM.Get("GUI_Frame_ShaderViewer_Sampler", $"0x{d.Address:X}", $"0x{d.Control1:X}", $"0x{d.Filter:X}"));
                if (ChannelsOf(slot) is { Length: > 0 } channels)
                    ImGui.TextDisabled(channels);
            }
            ImGui.PopID();
        }
        ImGui.EndTable();
    }

    // ---------------------------------------------------------------- details

    private static void Row(string label, string value)
    {
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.TextDisabled(label);
        ImGui.TableSetColumnIndex(1);
        ImGui.TextUnformatted(value);
    }

    private static bool BeginFields(string id)
    {
        if (!ImGui.BeginTable(id, 2, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg)) return false;
        ImGui.TableSetupColumn("##k", ImGuiTableColumnFlags.WidthStretch, 0.45f);
        ImGui.TableSetupColumn("##v", ImGuiTableColumnFlags.WidthStretch, 1f);
        return true;
    }

    private static string F(float v) => v.ToString("0.####");
    private static string V3(float x, float y, float z) => $"{F(x)}, {F(y)}, {F(z)}";

    private static string RenderModeName(Shader shader)
    {
        byte mode = shader.isOld ? shader.metadataOld!.Value.renderingMode : shader.metadataNew!.Value.renderingMode;
        if (!shader.isOld && Engine.Importing.ShaderClasses.RenderModes.FirstOrDefault(m => m.Id == mode) is { } info)
            return $"{mode}  {info.Name}";
        return Enum.IsDefined((RenderingMode)mode) ? $"{mode}  {(RenderingMode)mode}" : $"{mode}";
    }

    private static string ClassName(Shader shader)
    {
        if (shader.isOld) return $"0x{shader.metadataOld!.Value.Class:X2}";
        byte id = shader.metadataNew!.Value.shaderClass;
        return Engine.Importing.ShaderClasses.Find(id) is { } info ? $"0x{id:X2}  {info.Name}" : $"0x{id:X2}";
    }

    private static IEnumerable<string> FeatureNames(Shader shader)
    {
        if (shader.isOld)
        {
            var m = shader.metadataOld!.Value;
            if (m.UsesParallax) yield return LM.Get("GUI_Frame_ShaderViewer_Feature_Parallax");
            if (m.UsesGloss) yield return LM.Get("GUI_Frame_ShaderViewer_Feature_Gloss");
            if (m.UsesReflection) yield return LM.Get("GUI_Frame_ShaderViewer_Feature_Reflection");
            if (m.UsesNormalMap) yield return LM.Get("GUI_Frame_ShaderViewer_Feature_Normal");
            if (m.UsesDetailMap) yield return LM.Get("GUI_Frame_ShaderViewer_Feature_Detail");
            yield break;
        }
        var n = shader.metadataNew!.Value;
        if (n.ParallaxApplies) yield return LM.Get("GUI_Frame_ShaderViewer_Feature_Parallax");
        if (n.UsesSpecularPath) yield return LM.Get("GUI_Frame_ShaderViewer_Feature_Specular");
        if (n.UsesNormalMap) yield return LM.Get("GUI_Frame_ShaderViewer_Feature_Normal");
        if (n.UsesDetailMap) yield return LM.Get("GUI_Frame_ShaderViewer_Feature_Detail");
    }

    private static IEnumerable<string> FlagNames(byte flags)
    {
        if ((flags & 0x02) != 0) yield return LM.Get("GUI_Frame_ShaderViewer_Flag_DoubleSided");
        if ((flags & 0x08) != 0) yield return LM.Get("GUI_Frame_ShaderViewer_Flag_DefaultExpensive");
        if ((flags & 0x10) != 0) yield return LM.Get("GUI_Frame_ShaderViewer_Flag_TintAlone");
        if ((flags & 0x20) != 0) yield return LM.Get("GUI_Frame_ShaderViewer_Flag_NonStandard");
    }

    private static string Joined(IEnumerable<string> names, byte raw)
    {
        string list = string.Join(", ", names);
        return list.Length == 0 ? $"0x{raw:X2}" : $"0x{raw:X2}  {list}";
    }

    /// <summary>Parallax scale / bias and detail tiling as the file stores them (A Crack in Time keeps them elsewhere, see
    /// MaterialReader.CrackInTimeLayout).</summary>
    private (float Scale, float Bias, float Tiling) FileParallaxDetail(Shader shader)
    {
        if (shader.isOld)
        {
            var o = shader.metadataOld!.Value;
            return (o.parallaxScale, o.parallaxBias, o.detailTiling);
        }
        var n = shader.metadataNew!.Value;
        return _crackInTime
            ? (n.reflectionParamX, n.reflectionParamY, n.parallaxScale)
            : (n.parallaxScale, n.parallaxBias, n.detailTiling);
    }

    private void DrawDetails(Shader shader)
    {
        var index = AssetsBrowserFrame.EnsureIndex();
        var key = new AssetKey(AssetKind.Shader, _tuid);
        var entry = index?.Get(key);

        ImGui.SeparatorText(LM.Get("GUI_Frame_ShaderViewer_Identity"));
        if (BeginFields("##identity"))
        {
            Row(LM.Get("GUI_Frame_ShaderViewer_Name"), DisplayName(shader));
            Row(LM.Get("GUI_Frame_ShaderViewer_Tuid"), $"0x{_tuid:X16}");
            Row(LM.Get("GUI_Frame_ShaderViewer_Path"), FullPath(shader));
            Row(LM.Get("GUI_Frame_ShaderViewer_Engine"), LM.Get(shader.isOld ? "GUI_Frame_ShaderViewer_EngineOld" : "GUI_Frame_ShaderViewer_EngineNew"));
            if (entry != null) Row(LM.Get("GUI_Frame_ShaderViewer_Origin"), entry.Source.ToString());
            ImGui.EndTable();
        }
        if (ImGui.SmallButton(LM.Get("GUI_Frame_ShaderViewer_CopyTuid"))) ImGui.SetClipboardText($"0x{_tuid:X16}");
        ImGui.SameLine();
        if (entry != null && ImGui.SmallButton(LM.Get("GUI_Frame_ShaderViewer_Reveal"))) AssetsBrowserFrame.Reveal(key);

        ImGui.SeparatorText(LM.Get("GUI_Frame_ShaderViewer_Shading"));
        if (BeginFields("##shading"))
        {
            Row(LM.Get("GUI_Frame_ShaderViewer_Class"), ClassName(shader));
            if (!shader.isOld && Engine.Importing.ShaderClasses.Find(shader.metadataNew!.Value.shaderClass) is { } info)
                Row("", info.Notes);
            Row(LM.Get("GUI_Frame_ShaderViewer_RenderMode"), RenderModeName(shader));
            if (shader.isOld)
            {
                var o = shader.metadataOld!.Value;
                Row(LM.Get("GUI_Frame_ShaderViewer_FeatureMask"), Joined(FeatureNames(shader), o.flags));
                Row(LM.Get("GUI_Frame_ShaderViewer_Flags"), $"0x{(o.Unk1 is { Length: > 0 } u ? u[0] : 0):X2}");
            }
            else
            {
                var n = shader.metadataNew!.Value;
                Row(LM.Get("GUI_Frame_ShaderViewer_FeatureMask"), Joined(FeatureNames(shader), n.featureFlags));
                Row(LM.Get("GUI_Frame_ShaderViewer_Flags"), Joined(FlagNames(n.shaderFlags), n.shaderFlags));
            }
            ImGui.EndTable();
        }

        ImGui.SeparatorText(LM.Get("GUI_Frame_ShaderViewer_Constants"));
        if (BeginFields("##constants"))
        {
            var (scale, bias, tiling) = FileParallaxDetail(shader);
            if (shader.isOld)
            {
                var o = shader.metadataOld!.Value;
                Row(LM.Get("GUI_Frame_ShaderViewer_Colour"), V3(o.value0X, o.value0Y, o.value0Z));
                var v30 = o.Vector30;
                Row(LM.Get("GUI_Frame_ShaderViewer_GlossVector"), $"{F(v30.X)}, {F(v30.Y)}, {F(v30.Z)}, {F(v30.W)}");
                var tint = o.ReflectionTint;
                Row(LM.Get("GUI_Frame_ShaderViewer_ReflectionTint"), V3(tint.X, tint.Y, tint.Z));
                Row(LM.Get("GUI_Frame_ShaderViewer_AlbedoMix"), F(tint.W));
            }
            else
            {
                var n = shader.metadataNew!.Value;
                Row(LM.Get("GUI_Frame_ShaderViewer_Tint"), V3(n.tintR, n.tintG, n.tintB));
                Row(LM.Get("GUI_Frame_ShaderViewer_NormalStrength"), F(n.parameter30));
                Row(LM.Get("GUI_Frame_ShaderViewer_ReflectionStrength"), F(n.parameter34));
                if (n.IsFurFamily) Row(LM.Get("GUI_Frame_ShaderViewer_ShellCount"), $"{n.packedState38 & 0xFF}");
                else Row(LM.Get("GUI_Frame_ShaderViewer_BlendAlpha"), $"{0x7F - (n.packedState38 & 0x7F)}");
                Row(LM.Get("GUI_Frame_ShaderViewer_DetailGain"), F(n.parameter3C));
                Row(LM.Get("GUI_Frame_ShaderViewer_CubeIndex"), n.shaderClass is 0x0E or 0x0F
                    ? F(BitConverter.UInt32BitsToSingle(n.environmentFilterIndex))
                    : $"{n.environmentFilterIndex}");
                Row(LM.Get("GUI_Frame_ShaderViewer_ReflectionTint"), V3(n.reflectionTintR, n.reflectionTintG, n.reflectionTintB));
                Row(LM.Get("GUI_Frame_ShaderViewer_AlbedoMix"), F(n.reflectionTintAlbedoMix));
                if (!_crackInTime)
                {
                    Row(LM.Get("GUI_Frame_ShaderViewer_RimGain"), F(n.reflectionParamX));
                    Row(LM.Get("GUI_Frame_ShaderViewer_FresnelExponent"), F(n.reflectionParamY));
                }
                Row(LM.Get("GUI_Frame_ShaderViewer_CubeBias"), F(n.reflectionParamZ));
                Row(LM.Get("GUI_Frame_ShaderViewer_FresnelGain"), F(n.reflectionParamW));
                if (n.UsesEmbeddedShaderBundle) Row(LM.Get("GUI_Frame_ShaderViewer_ProgramBase"), $"0x{n.fragmentShaderBaseTuid:X16}");
                if (n.shaderClass is 0x01 or 0x0F) Row(LM.Get("GUI_Frame_ShaderViewer_ClassWord"), $"0x{n.parameter44:X8}");
            }
            Row(LM.Get("GUI_Frame_ShaderViewer_ParallaxScale"), F(scale));
            Row(LM.Get("GUI_Frame_ShaderViewer_ParallaxBias"), F(bias));
            Row(LM.Get("GUI_Frame_ShaderViewer_DetailTiling"), F(tiling));
            ImGui.EndTable();
        }

        DrawLiveTuning(shader);
        DrawUsedBy(index, key);
    }

    /// <summary>The Shader Browser's runtime-only tuning (scene materials and this preview; nothing is saved).</summary>
    private void DrawLiveTuning(Shader shader)
    {
        var assets = LunaWindow.Instance.AssetManager;
        if (assets == null || !assets.TryGetParallax(_tuid, out float scale, out float bias)) return;

        ImGui.SeparatorText(LM.Get("GUI_Frame_ShaderViewer_LiveTuning"));
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_Frame_ShaderViewer_LiveTuningTip"));
        var (fileScale, fileBias, fileTiling) = FileParallaxDetail(shader);

        bool changed = ImGui.DragFloat(LM.Get("GUI_Frame_ShaderViewer_ParallaxScale"), ref scale, 0.001f, 0f, 0f, "%.6f");
        bool done = ImGui.IsItemDeactivatedAfterEdit();
        changed |= ImGui.DragFloat(LM.Get("GUI_Frame_ShaderViewer_ParallaxBias"), ref bias, 0.001f, 0f, 0f, "%.6f");
        done |= ImGui.IsItemDeactivatedAfterEdit();
        if (changed) assets.SetParallax(_tuid, scale, bias);
        if (ImGui.SmallButton($"{LM.Get("GUI_Common_Reset")}##parallax"))
        {
            assets.SetParallax(_tuid, fileScale, fileBias);
            done = true;
        }

        if (assets.TryGetDetailTiling(_tuid, out float tiling))
        {
            if (ImGui.DragFloat(LM.Get("GUI_Frame_ShaderViewer_DetailTiling"), ref tiling, 0.1f, 0f, 0f, "%.3f"))
                assets.SetDetailTiling(_tuid, tiling);
            done |= ImGui.IsItemDeactivatedAfterEdit();
            if (ImGui.SmallButton($"{LM.Get("GUI_Common_Reset")}##detail"))
            {
                assets.SetDetailTiling(_tuid, fileTiling != 0f ? fileTiling : AssetManager.DefaultDetailTiling);
                done = true;
            }
        }
        // The preview copied the values when it was built.
        if (done) _previewDirty = true;
    }

    private static void DrawUsedBy(AssetIndex? index, AssetKey key)
    {
        ImGui.SeparatorText(LM.Get("GUI_Frame_ShaderViewer_UsedBy"));
        if (index == null) return;
        var users = index.References.UsedBy(key);
        if (users.Count == 0)
        {
            ImGui.TextDisabled(LM.Get("GUI_Frame_ShaderViewer_UsedByNone"));
        }
        else
        {
            foreach (var user in users)
            {
                var entry = index.Get(user);
                string name = entry?.Name ?? $"{user.Id:X}";
                ImGui.PushStyleColor(ImGuiCol.Text, AssetsBrowserFrame.KindColour(user.Kind));
                ImGui.TextUnformatted(AssetsBrowserFrame.KindIcon(user.Kind));
                ImGui.PopStyleColor();
                ImGui.SameLine();
                if (ImGui.Selectable($"{name}##user{user}", false, ImGuiSelectableFlags.AllowDoubleClick) &&
                    ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left) && entry != null)
                    AssetOpener.Open(entry);
                if (ImGui.IsItemHovered()) ImGui.SetTooltip($"{AssetsBrowserFrame.KindName(user.Kind)}  {entry?.Path ?? user.ToString()}");
            }
        }
        if (ImGui.SmallButton(LM.Get("GUI_Frame_ShaderViewer_ReferenceViewer"))) ReferenceViewerFrame.Show(key);
    }

    // ---------------------------------------------------------------- raw

    private bool _rawFloats;

    private void DrawRaw(Shader shader)
    {
        ImGui.Checkbox(LM.Get("GUI_Frame_ShaderViewer_ShowFloats"), ref _rawFloats);
        ImGui.SameLine();
        if (ImGui.SmallButton(LM.Get("GUI_Frame_ShaderViewer_Copy"))) ImGui.SetClipboardText(RawText(shader));
        ImGui.Separator();
        ImGui.TextUnformatted(RawText(shader));
    }

    private string? _rawCache;
    private bool _rawCacheFloats;

    /// <summary>The 0x80-byte 0x5000 record, re-serialised big-endian from the parsed fields, as a hex dump.</summary>
    private string RawText(Shader shader)
    {
        if (_rawCache != null && _rawCacheFloats == _rawFloats) return _rawCache;
        byte[] record = RawRecord(shader);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"0x5000  ({record.Length} bytes)");
        for (int i = 0; i < record.Length; i += 16)
        {
            sb.Append($"{i:X4}:  ");
            for (int j = i; j < Math.Min(i + 16, record.Length); j++)
                sb.Append($"{record[j]:X2}{(j % 4 == 3 ? "  " : " ")}");
            if (_rawFloats)
            {
                sb.Append("  ");
                for (int j = i; j + 3 < Math.Min(i + 16, record.Length); j += 4)
                    sb.Append($"{System.Buffers.Binary.BinaryPrimitives.ReadSingleBigEndian(record.AsSpan(j, 4)),12:0.#####}");
            }
            sb.AppendLine();
        }
        if (!shader.isOld && shader.ShaderBundle is { } bundle)
        {
            sb.AppendLine();
            sb.AppendLine($"0x33003  {Convert.ToHexString(bundle.SelectorData)}");
            foreach (var v in bundle.Variants)
                sb.AppendLine($"0x33002  {v.Tuid:X16}  +0x{v.ResourceOffset:X} / 0x{v.ResourceSize:X}  ucode 0x{v.MicrocodeSize:X}  config {v.Config04:X8} {v.Config08:X8} {v.Config0C:X8}  {v.SkeletonKey}");
        }
        if (!shader.isOld)
        {
            sb.AppendLine();
            for (int slot = 0; slot < Shader.NewTextureSlotCount; slot++)
            {
                if (shader.NewTextureDescriptors[slot] is not { } d) continue;
                sb.AppendLine($"0x5200 slot {slot}  source {shader.NewTextureSourceIds[slot]:X8}  offset {d.Offset:X8}  mips {d.MipmapCount}  format {d.FormatBitfield:X4}  " +
                              $"address {d.Address:X8}  control0 {d.Control0:X8}  control1 {d.Control1:X8}  filter {d.Filter:X8}  {d.Width}x{d.Height}  control3 {d.Control3:X8}");
            }
        }
        _rawCacheFloats = _rawFloats;
        return _rawCache = sb.ToString();
    }

    private static byte[] RawRecord(Shader shader)
    {
        var bytes = new byte[0x80];
        var span = bytes.AsSpan();
        void U32(int at, uint v) => System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan()[at..], v);
        void I32(int at, int v) => System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan()[at..], v);
        void F32(int at, float v) => System.Buffers.Binary.BinaryPrimitives.WriteSingleBigEndian(bytes.AsSpan()[at..], v);
        void Raw(int at, byte[]? data) { if (data != null) data.AsSpan(0, Math.Min(data.Length, 0x80 - at)).CopyTo(bytes.AsSpan()[at..]); }
        if (shader.isOld)
        {
            var o = shader.metadataOld!.Value;
            U32(0x00, o.albedo); U32(0x04, o.normal); U32(0x08, o.expensive); U32(0x0C, o.detailMap);
            bytes[0x10] = o.flags; bytes[0x11] = o.renderingMode; bytes[0x12] = o.Class;
            Raw(0x13, o.Unk1);
            F32(0x20, o.value0X); F32(0x24, o.value0Y); F32(0x28, o.value0Z); F32(0x2C, o.value0W);
            F32(0x30, o.value1X); F32(0x34, o.value1Y);
            Raw(0x38, o.Unk2b);
            F32(0x50, o.parallaxScale); F32(0x54, o.parallaxBias); F32(0x58, o.detailTiling);
            Raw(0x5C, o.Unk3);
        }
        else
        {
            var n = shader.metadataNew!.Value;
            I32(0x00, n.albedo); I32(0x04, n.normal); I32(0x08, n.expensive); I32(0x0C, n.detailMap);
            I32(0x10, n.auxiliaryA); I32(0x14, n.auxiliaryB);
            Raw(0x18, n.Reserved18);
            bytes[0x20] = n.featureFlags; bytes[0x21] = n.renderingMode; bytes[0x22] = n.shaderClass; bytes[0x23] = n.shaderFlags;
            F32(0x24, n.tintR); F32(0x28, n.tintG); F32(0x2C, n.tintB);
            F32(0x30, n.parameter30); F32(0x34, n.parameter34); U32(0x38, n.packedState38); F32(0x3C, n.parameter3C);
            U32(0x40, n.environmentFilterIndex); U32(0x44, n.parameter44);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt64BigEndian(span[0x48..], n.fragmentShaderBaseTuid);
            F32(0x50, n.reflectionTintR); F32(0x54, n.reflectionTintG); F32(0x58, n.reflectionTintB); F32(0x5C, n.reflectionTintAlbedoMix);
            F32(0x60, n.reflectionParamX); F32(0x64, n.reflectionParamY); F32(0x68, n.reflectionParamZ); F32(0x6C, n.reflectionParamW);
            F32(0x70, n.parallaxScale); F32(0x74, n.parallaxBias); F32(0x78, n.detailTiling); F32(0x7C, n.reserved7C);
        }
        return bytes;
    }
}
