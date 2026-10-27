using System.Numerics;
using ReLunacy.Engine.Assets.Sky;
using ReLunacy.Engine.Loading.Readers;
using ReLunacy.Engine.Rendering.Vulkan;

namespace ReLunacy.Engine.Rendering;

/// <summary>
/// Live state for the proof-bounded old-engine Sky path. Pass 14 Dome is generated from the recovered
/// retail CPU producer; Color SkyShells use authored vertices/UVs/index windows. Clouds and Bloom stay
/// separate until their terminal shader/post-process paths are closed.
/// </summary>
public static class SkyRenderOptions
{
    public static bool Enabled = true;
    public static VkSkyScene? Scene { get; private set; }

    private static LevelData? _sceneLevel;
    private static AssetManager? _sceneAssets;
    private static VulkanRenderer? _attachedRenderer;
    private static VkSkyScene? _attachedScene;

    // Which of the level's skies are drawn, by LevelSky.Index. A level opens on its first sky alone -
    // what retail shows before any set_sky() - and the Sky menu ticks the others on and off. Several
    // ticked are drawn together: the first ticked one's Dome and Clouds, every ticked one's shells.
    private static readonly HashSet<int> _visible = [0];
    private static SkyData? _visibleSky;

    /// <summary>Whether sky <paramref name="index"/> of <paramref name="level"/> is drawn.</summary>
    public static bool IsVisible(LevelData level, int index)
    {
        SyncLevel(level);
        return _visible.Contains(index);
    }

    public static void SetVisible(LevelData level, int index, bool visible)
    {
        SyncLevel(level);
        if (visible ? _visible.Add(index) : _visible.Remove(index)) Rebuild();
    }

    /// <summary>Draws sky <paramref name="index"/> and no other.</summary>
    public static void ShowOnly(LevelData level, int index)
    {
        SyncLevel(level);
        if (_visible.Count == 1 && _visible.Contains(index)) return;
        _visible.Clear();
        _visible.Add(index);
        Rebuild();
    }

    private static void SyncLevel(LevelData level)
    {
        if (ReferenceEquals(_sceneLevel, level)) return;
        Scene = null;
        _sceneLevel = level;
        _attachedRenderer = null;
        _attachedScene = null;
        _visible.Clear();
        _visible.Add(0);
        _visibleSky = null;
    }

    // The renderer keeps drawing the packet it has until the new one is built, then swaps.
    private static void Rebuild()
    {
        Scene = null;
        _visibleSky = null;
    }

    private static SkyData VisibleSky(LevelData level) => _visibleSky ??=
        SkyData.Combine([.. level.Skies.Where(sky => _visible.Contains(sky.Index)).Select(sky => sky.Data)]);
    private static VulkanRenderer? _clearRenderer;
    private static Vector4 _editorClearColour;

    /// <summary>
    /// Ensures the loaded old-engine level's Sky textures are built, publishes the Vulkan packet once
    /// uploads are complete, and attaches that packet to the level's raw Vulkan renderer. The packet is
    /// then drawn inside the main opaque framebuffer, before the level geometry.
    /// SkyDefs alone is enough for a Dome-only packet; SkyShell geometry is optional.
    /// </summary>

    // PrepareScene returns early on several conditions and used to do it silently, so "no sky" gave
    // no clue which gate stopped it. Logged once per distinct reason: this runs every frame.
    private static string? _lastBail;

    private static void Bail(string reason)
    {
        if (_lastBail == reason) return;
        _lastBail = reason;
        Vulkan.LunaLog.LogInfo($"Sky: not drawn - {reason}");
    }

    private static void BailCleared(string note)
    {
        if (_lastBail == null) return;
        _lastBail = null;
        Vulkan.LunaLog.LogInfo($"Sky: {note}");
    }

    public static void PrepareScene(LevelData? level, AssetManager? assets)
    {
        if (level == null || assets == null)
        {
            RestoreEditorClear(assets);
            Scene = null;
            _sceneLevel = null;
            _attachedRenderer = null;
            return;
        }

        SyncLevel(level);

        // The packet points at textures of the AssetManager it was built with. A level reload can build it
        // while the PREVIOUS level's AssetManager is still live (the new LevelData is published first), and
        // DoLoadEntitiesCheck then disposes that manager: attaching the packet to the new renderer made
        // vkCreateImageView read destroyed images (0xC0000005 in CreateSkyDescriptors, after every
        // new-engine Create / structural edit). Rebuild against the manager in use.
        if (!ReferenceEquals(_sceneAssets, assets))
        {
            Scene = null;
            _visibleSky = null;
            _sceneAssets = assets;
        }

        // A renderer can be recreated for resize/level-view reasons while the decoded Sky packet stays
        // valid. Attach the already-built packet exactly once to each renderer instance. The clear colour
        // is synchronized every frame because the Sky visibility toggle is live state, not packet state.
        if (Scene is { HasGeometry: true })
        {
            AttachToRenderer(assets);
            SyncRetailClear(assets);
            return;
        }

        SkyData sky = VisibleSky(level);
        if (!sky.HasSkyDefs)
        {
            Bail($"no SkyDefs (0x8150) among the {level.Skies.Count} sky(ies) ticked {string.Join(",", _visible.Order())}");
            AttachToRenderer(assets); // nothing ticked: take the sky off a renderer that still draws one
            RestoreEditorClear(assets);
            return;
        }

        // Build through the normal texture cache/uploader. Color SkyShells need their authored sources
        // now; the three Cloud references are also kept ready for pass 17 without inventing a second
        // Sky-specific texture decoder.
        foreach (var source in sky.RenderSources)
        {
            try
            {
                assets.GetOrBuildTexture(source.Texture);
            }
            catch (Exception ex)
            {
                Vulkan.LunaLog.LogWarning($"sky source '{source.Label}' texture build failed: {ex.Message}");
            }
        }

        if (assets.HasPendingUploads)
        {
            Bail("waiting on texture uploads (retried every frame)");
            return;
        }

        Scene = VkSkySceneBuilder.Build(sky, assets.BuiltTextures);
        if (Scene is not { HasGeometry: true })
        {
            Bail(Scene == null
                ? "VkSkySceneBuilder.Build returned null (SkyDefs missing at build time)"
                : "the built packet has neither a dome nor shell geometry");
            AttachToRenderer(assets);
            RestoreEditorClear(assets);
            return;
        }
        BailCleared("packet built; proceeding to attach");

        string dome = Scene.HasDome
            ? $"Dome {Scene.Dome!.VertexData.Length / (int)VkSkyDome.FloatsPerVertex:N0}v/{Scene.Dome.Indices.Length:N0}i"
            : "Dome absent";
        string shell = sky.ShellMesh is { } mesh
            ? $"shell {mesh.Vertices.Count:N0}v/{mesh.Indices.Count:N0}i, {Scene.Draws.Length} draw(s)"
            : "shell absent";
        Vulkan.LunaLog.LogInfo($"Sky: native Vulkan packet {dome}; {shell}; sky(ies) [{string.Join(", ", _visible.Order())}] of {level.Skies.Count}.");

        AttachToRenderer(assets);
        SyncRetailClear(assets);
    }

    /// <summary>
    /// Gives the renderer the current packet: once per renderer instance, and again - swapped with its
    /// frames drained - whenever the ticked skies produce another packet, or none.
    /// </summary>
    private static void AttachToRenderer(AssetManager assets)
    {
        if (assets.SceneRenderer is not { } renderer) return;
        VkSkyScene? scene = Scene is { HasGeometry: true } built ? built : null;
        if (ReferenceEquals(_attachedRenderer, renderer))
        {
            if (ReferenceEquals(_attachedScene, scene)) return;
            renderer.ReplaceSky(scene);
            _attachedScene = scene;
            Vulkan.LunaLog.LogInfo(scene == null ? "Sky: no sky ticked; packet removed." : "Sky: packet swapped for the ticked skies.");
            return;
        }
        if (scene == null) return;

        CaptureEditorClear(renderer);
        // A level reload keeps drawing the previous level on the same renderer while the new LevelData is
        // published (SyncLevel then forgets which renderer it attached): that renderer already draws a sky,
        // and SetSky would destroy it under a frame in flight (0xC0000005 in vkQueueSubmit). Swap it at the
        // next frame instead, the way the Sky menu does.
        if (renderer.HasSky) renderer.ReplaceSky(scene);
        else renderer.SetSky(scene);
        _attachedRenderer = renderer;
        _attachedScene = scene;
        Vulkan.LunaLog.LogInfo("Sky: packet attached to the main Vulkan renderer; direct opaque-pass rendering active.");
    }

    private static void SyncRetailClear(AssetManager assets)
    {
        if (assets.SceneRenderer is not { } renderer)
            return;

        CaptureEditorClear(renderer);
        if (!Enabled || Scene?.Dome is not { } dome)
        {
            renderer.ClearColour = _editorClearColour;
            return;
        }

        // PROVEN (NPUA80965 EBOOT, 0x580C50..0x580CFC): retail clamps each SkyDefs float to [0,1],
        // quantizes it with *255+0.5 and packs
        //
        //     word = q(+0x50) << 24 | q(+0x44) << 16 | q(+0x48) << 8 | q(+0x4C)
        //
        // into Sky runtime +0x60. 0x580FCC..0x580FD4 then writes that word to the render context
        // clear-colour slot and ORs 0xF0 (colour RGBA) into the clear mask. SET_COLOR_CLEAR_VALUE is
        // 0xAARRGGBB, so R/G/B = +0x44/+0x48/+0x4C and A = +0x50.
        //
        // This is the SAME float order the Dome fragment constant C0 uses, which is why the Dome's
        // lower fade and the background below it meet without a seam. An earlier revision read
        // A=+0x44, R=+0x48, G=+0x4C, B=+0x50 - one channel off - which tinted everything under the Dome
        // (yellow on Metropolis, whose +0x50 is 0.0; +0x50 is 2.0 on Rykan V, clearly not a colour).
        Vector4 source44 = dome.FragmentConstant0;
        byte r = QuantizeUnorm8(source44.X); // SkyDefs +0x44 -> clear R
        byte g = QuantizeUnorm8(source44.Y); // SkyDefs +0x48 -> clear G
        byte b = QuantizeUnorm8(source44.Z); // SkyDefs +0x4C -> clear B
        byte a = QuantizeUnorm8(source44.W); // SkyDefs +0x50 -> clear A

        // The retail clear ALPHA is destination-alpha state for the Bloom/post chain (passes 16/19).
        // It is NOT host framebuffer coverage: the editor composites this colour target by alpha, so
        // an authored A of 0 made the whole viewport transparent. Keep the authored byte for the Bloom
        // reverse and clear the host target opaque.
        RetailClearAlpha = a / 255f;
        renderer.ClearColour = new Vector4(r / 255f, g / 255f, b / 255f, 1f);
    }

    /// <summary>
    /// Authored retail SET_COLOR_CLEAR_VALUE alpha (SkyDefs +0x44) for the current level, preserved
    /// for the still-OPEN destination-alpha Bloom chain. It is deliberately not applied to the host
    /// colour target, whose alpha is editor compositing coverage rather than retail state.
    /// </summary>
    public static float RetailClearAlpha { get; private set; } = 1f;

    private static void CaptureEditorClear(VulkanRenderer renderer)
    {
        if (ReferenceEquals(_clearRenderer, renderer))
            return;

        _clearRenderer = renderer;
        _editorClearColour = renderer.ClearColour;
    }

    private static void RestoreEditorClear(AssetManager? assets)
    {
        if (assets?.SceneRenderer is not { } renderer || !ReferenceEquals(_clearRenderer, renderer))
            return;

        renderer.ClearColour = _editorClearColour;
    }

    private static byte QuantizeUnorm8(float value)
    {
        float clamped = Math.Clamp(value, 0f, 1f);
        return (byte)(clamped * 255f + 0.5f);
    }
}
