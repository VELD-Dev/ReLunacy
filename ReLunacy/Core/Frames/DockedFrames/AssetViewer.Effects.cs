// Ported from ReLunacy Resurgence by Fianshoo (GPL-3.0), adapted to this editor's Asset Viewer and renderer.
using System.Numerics;
using ReLunacy.Engine.Assets.Effects;
using ReLunacy.Utility;
using ReLunacy.Utility.Localization;

namespace ReLunacy.Core.Frames.DockedFrames;

/// <summary>
/// The Asset Viewer's effect family: every package of the level's effect.dat (as <see cref="LevelEffectPlayer"/>
/// reads it), named by the Tags of the FxController keys that play it where the game has them. The selected one
/// plays at the origin of the preview, through the same renderer's effect pass as the level view
/// (VulkanRenderer.Effects.cs), with the orbit camera and overlay of the models.
/// </summary>
public partial class AssetViewer
{
    /// <summary>One package, with the Tags names of the keys that play it (empty when none, or unnamed).</summary>
    private sealed record EffectEntry(EffectPackage Package, string Name, IReadOnlyList<string> Tags, string Ids);

    private LevelEffects? _effectsListed;
    private IReadOnlyDictionary<uint, string>? _effectNamesListed;
    private List<EffectEntry> _effectEntries = [];

    private EffectPackage? _selectedEffect;
    private EffectSimulator? _effectSim;
    private readonly EffectSimulationSettings _effectSettings = new();
    private readonly EffectSpriteBatch _effectBatch = new();
    private readonly List<EffectBillboard> _effectSprites = [];
    private readonly List<EffectLightMarker> _effectLights = [];
    // The pieces the selected effect plays, sub-effects flattened, for the detail panel.
    private readonly List<EffectComponent> _effectPieces = [];
    private readonly List<(Vector3 a, Vector3 b, Vector4 color)> _effectGrid = [];
    private object? _effectGridSentTo;
    private bool _effectPlaying = true, _effectLoop = true;
    private float _effectRestartWait;
    private bool _revealEffectsTab;

    /// <summary>Selects the package a game-wide effect id maps to and shows the Effects tab. False when the
    /// level's effects are not read yet or have no such id.</summary>
    public bool SelectEffectById(int effectId)
    {
        RefreshEffectEntries();
        var entry = _effectEntries.FirstOrDefault(e => e.Package.GlobalIds.Contains(effectId));
        if (entry == null) return false;
        _revealEffectsTab = true;
        SelectEffect(entry.Package);
        return true;
    }

    /// <summary>Selects an effect package and shows the Effects tab. False when the level's effects are not read yet.</summary>
    public bool SelectEffectPackage(EffectPackage package)
    {
        RefreshEffectEntries();
        if (_effectsListed == null || !_effectsListed.Library.Packages.Contains(package)) return false;
        _revealEffectsTab = true;
        SelectEffect(package);
        return true;
    }

    private ImGuiTabItemFlags RevealEffectsTab()
    {
        if (!_revealEffectsTab) return ImGuiTabItemFlags.None;
        _revealEffectsTab = false;
        return ImGuiTabItemFlags.SetSelected;
    }

    private void SelectEffect(EffectPackage package)
    {
        if (LevelEffectPlayer.Current is not { } effects) return;
        SelectedMobyAsset = null;
        SelectedTieAsset = null;
        SelectedUFragAsset = null;
        _selectedEffect = package;
        _effectSim = new EffectSimulator(effects.Library, package, _effectSettings);
        _effectPlaying = true;
        _effectRestartWait = 0f;
        _effectPieces.Clear();
        CollectPieces(package, 0);
        FrameEffect();
        IsDirty = true;
    }

    private void ClearEffect()
    {
        _selectedEffect = null;
        _effectSim = null;
        _effectPieces.Clear();
    }

    /// <summary>Drops the list with the level it was read from.</summary>
    private void ForgetEffects()
    {
        _effectsListed = null;
        _effectNamesListed = null;
        _effectEntries = [];
    }

    private void CollectPieces(EffectPackage package, int depth)
    {
        if (depth > 6) return;
        foreach (EffectComponent component in package.Components)
        {
            if (component.Piece is EffectPackage child) CollectPieces(child, depth + 1);
            else _effectPieces.Add(component);
        }
    }

    /// <summary>Fits the camera to the effect, and sizes the ground grid to it.</summary>
    private void FrameEffect()
    {
        float extent = _effectSim?.MeasureExtent() ?? 1f;
        float distance = Math.Clamp(extent * 2.4f, 0.3f, 80f);
        Camera.Target = new Vector3(0f, extent * 0.35f, 0f);
        Camera.Position = Camera.Target + new Vector3(0f, distance * 0.35f, -distance);

        _effectGrid.Clear();
        _effectGridSentTo = null;
        float step = extent > 20f ? 2f : extent > 5f ? 1f : 0.25f;
        const int lines = 10;
        float half = step * lines;
        var colour = new Vector4(0.45f, 0.45f, 0.45f, 1f);
        for (int i = -lines; i <= lines; i++)
        {
            float o = i * step;
            _effectGrid.Add((new Vector3(o, 0f, -half), new Vector3(o, 0f, half), i == 0 ? new Vector4(0.3f, 0.45f, 1f, 1f) : colour));
            _effectGrid.Add((new Vector3(-half, 0f, o), new Vector3(half, 0f, o), i == 0 ? new Vector4(1f, 0.3f, 0.3f, 1f) : colour));
        }
    }

    private static IReadOnlyDictionary<uint, string>? TagNames()
    {
        string? source = LunaWindow.Instance.fileManager?.SourcePath;
        return EffectTags.For(string.IsNullOrEmpty(source) ? null : Engine.Config.GameConfigs.GameFolderFor(source));
    }

    /// <summary>Rebuilds the list when the level's effects are read, or the Tags names arrive.</summary>
    private void RefreshEffectEntries()
    {
        var effects = LevelEffectPlayer.Current;
        var names = effects != null ? TagNames() : null;
        if (ReferenceEquals(effects, _effectsListed) && ReferenceEquals(names, _effectNamesListed)) return;
        if (!ReferenceEquals(effects, _effectsListed) && _selectedEffect != null) { ClearEffect(); IsDirty = true; }
        _effectsListed = effects;
        _effectNamesListed = names;
        _effectEntries = [];
        if (effects == null) return;

        var tags = new Dictionary<EffectPackage, List<string>>(ReferenceEqualityComparer.Instance);
        if (names != null)
            foreach (uint key in effects.ControllerKeys())
                if (names.TryGetValue(key, out string? name) && LevelEffectPlayer.ResolveKey(key)?.Package is { } package)
                {
                    if (!tags.TryGetValue(package, out var list)) tags[package] = list = [];
                    list.Add(name);
                }

        foreach (EffectPackage package in effects.Library.Packages)
        {
            IReadOnlyList<string> named = tags.TryGetValue(package, out var list) ? list.Order(StringComparer.OrdinalIgnoreCase).ToList() : [];
            _effectEntries.Add(new EffectEntry(package, named.Count > 0 ? named[0] : package.DisplayName, named,
                string.Join(", ", package.GlobalIds)));
        }
        // Named ones first, then the rest in file order.
        _effectEntries = [.. _effectEntries.OrderBy(e => e.Tags.Count == 0).ThenBy(e => e.Tags.Count > 0 ? e.Name : "", StringComparer.OrdinalIgnoreCase)];
    }

    private static bool EffectMatches(EffectEntry entry, string search) =>
        entry.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
        || entry.Tags.Any(t => t.Contains(search, StringComparison.OrdinalIgnoreCase))
        || entry.Package.GlobalIds.Any(id => id.ToString().StartsWith(search, StringComparison.Ordinal));

    private void RenderEffectList()
    {
        RefreshEffectEntries();
        if (_effectsListed == null)
        {
            ImGui.TextDisabled(LM.Get(LevelEffectPlayer.IsLoading ? "GUI_Frame_AssetViewer_EffectsLoading" : "GUI_Frame_AssetViewer_NoEffects"));
            return;
        }

        string search = assetSearch.Trim();
        foreach (EffectEntry entry in _effectEntries)
        {
            if (search.Length > 0 && !EffectMatches(entry, search)) continue;
            if (ImGui.Selectable($"{entry.Name}##fx_{entry.Package.Index}", ReferenceEquals(_selectedEffect, entry.Package)))
                SelectEffect(entry.Package);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(LM.Get("GUI_Frame_AssetViewer_EffectTip", entry.Ids.Length > 0 ? entry.Ids : "-", entry.Package.Summary));
        }
    }

    /// <summary>Steps the selected effect and hands its sprites to the preview renderer.</summary>
    private void UpdateEffectPreview(double deltaTime)
    {
        if (_vkPreview == null || _effectSim is not { } sim) return;

        LevelEffectPlayer.Prepare(graphicsDevice);
        if (!ReferenceEquals(_vkPreview.EffectTextureOwner, LevelEffectPlayer.TextureToken))
            _vkPreview.SetEffectTextures(graphicsDevice, LevelEffectPlayer.Textures.Select(g => g?.DeviceTexture).ToList(), LevelEffectPlayer.TextureToken);
        if (!ReferenceEquals(_effectGridSentTo, _vkPreview))
        {
            _vkPreview.SetDepthDebugLines(_effectGrid);
            _effectGridSentTo = _vkPreview;
        }

        float dt = Math.Clamp((float)deltaTime, 0f, 0.1f);
        if (_effectPlaying)
        {
            sim.Step(dt);
            // Looping plays it again shortly after it ends, as a retriggered FxController would.
            if (_effectLoop && sim.Finished && (_effectRestartWait += dt) > 0.35f)
            {
                sim.Reset();
                _effectRestartWait = 0f;
            }
        }

        _effectSprites.Clear();
        _effectLights.Clear();
        sim.Collect(_effectSprites, _effectLights);
        Matrix4x4 view = Camera.GetView();
        _effectBatch.Clear();
        _effectBatch.Add(_effectSprites, view);
        // No lighting pass for effect lights: a faint glow shows where they are, as in the level view.
        foreach (EffectLightMarker light in _effectLights)
            _effectBatch.AddGlow(light.Position, Math.Clamp(light.Radius * 0.3f, 0.05f, 3f),
                new Vector4(light.Color.X, light.Color.Y, light.Color.Z, 0.3f), view);
        _vkPreview.SetEffectSprites(_effectBatch);
    }

    private void RestartEffect()
    {
        _effectSim?.Reset();
        _effectRestartWait = 0f;
        _effectPlaying = true;
    }

    private void RenderEffectDetails(EffectPackage package)
    {
        if (_effectSim is not { } sim) return;
        var entry = _effectEntries.FirstOrDefault(e => ReferenceEquals(e.Package, package));

        ImGui.TextUnformatted(entry?.Name ?? package.DisplayName);
        if (ImGui.Button(LM.Get(_effectPlaying ? "GUI_Frame_AssetViewer_EffectPause" : "GUI_Frame_AssetViewer_EffectPlay") + "##fx_play"))
        {
            if (!_effectPlaying && sim.Finished) RestartEffect();
            else _effectPlaying = !_effectPlaying;
        }
        ImGui.SameLine();
        if (ImGui.Button(LM.Get("GUI_Frame_AssetViewer_EffectRestart") + "##fx_restart"))
            RestartEffect();
        ImGui.SameLine();
        ImGui.Checkbox(LM.Get("GUI_Frame_AssetViewer_EffectLoop") + "##fx_loop", ref _effectLoop);
        ImGui.SetItemTooltip(LM.Get("GUI_Frame_AssetViewer_EffectLoopTip"));
        ImGui.SameLine();
        if (ImGui.Button(LM.Get("GUI_Frame_AssetViewer_EffectFrame") + "##fx_frame"))
            FrameEffect();
        ImGui.SetItemTooltip(LM.Get("GUI_Frame_AssetViewer_EffectFrameTip"));
        ImGui.SameLine();
        ImGui.TextDisabled(LM.Get("GUI_Frame_AssetViewer_EffectTime", sim.Time.ToString("0.00"), sim.ParticleCount));

        if (!ImGui.BeginTabBar("asset_details_tabs_effect")) return;

        if (ImGui.BeginTabItem(LM.Get("GUI_Frame_AssetViewer_EffectOverview")))
        {
            int emitters = _effectPieces.Count(c => c.Type == EffectPieceType.Emitter);
            int quads = _effectPieces.Count(c => c.Type == EffectPieceType.Quad);
            int lights = _effectPieces.Count(c => c.Type == EffectPieceType.Light);
            ImGui.Text(LM.Get("GUI_Frame_AssetViewer_EffectIds", entry?.Ids is { Length: > 0 } ids ? ids : "-", package.Index));
            if (entry is { Tags.Count: > 0 })
                ImGui.TextWrapped(LM.Get("GUI_Frame_AssetViewer_EffectTags", string.Join(", ", entry.Tags)));
            ImGui.Text(LM.Get("GUI_Frame_AssetViewer_EffectCounts", emitters, quads, lights));
            ImGui.Text(LM.Get("GUI_Frame_AssetViewer_EffectLength",
                sim.Continuous ? LM.Get("GUI_Frame_AssetViewer_EffectContinuous") : $"{sim.PlayLength:0.##} s"));
            ImGui.TextDisabled(package.Summary);
            RenderEffectControllers(package);
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem(LM.Get("GUI_Frame_AssetViewer_EffectPieces", _effectPieces.Count)))
        {
            RenderEffectPieces();
            ImGui.EndTabItem();
        }

        var textures = EffectTextureIndices();
        if (ImGui.BeginTabItem(LM.Get("GUI_Frame_AssetViewer_EffectTextures", textures.Count)))
        {
            RenderEffectTextures(textures);
            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
    }

    /// <summary>The level's FxControllers that play the effect, each a link that selects it in the level.</summary>
    private void RenderEffectControllers(EffectPackage package)
    {
        var controllers = LevelEffectPlayer.ControllersOf(package);
        ImGui.Separator();
        if (controllers.Count == 0)
        {
            ImGui.TextDisabled(LM.Get("GUI_Frame_AssetViewer_EffectNoControllers"));
            return;
        }
        ImGui.TextUnformatted(LM.Get("GUI_Frame_InstanceInspector_UsedBy") + $" {controllers.Count}");
        bool several = controllers.Select(c => c.Region).Distinct().Count() > 1;
        for (int i = 0; i < controllers.Count; i++)
        {
            var (region, entity, index, key) = controllers[i];
            ImGui.PushID(i);
            ImGui.Bullet();
            if (entity != null) PropertyInspectorFrame.ReferenceLink(region, "Controller", entity.Name, index);
            else ImGui.TextUnformatted($"FxController #{index}");
            ImGui.SameLine();
            string tag = _effectNamesListed?.GetValueOrDefault(key) ?? key.ToString();
            ImGui.TextDisabled((several && region != null ? region.RegionName + "  " : "") + tag);
            ImGui.PopID();
        }
    }

    private void RenderEffectPieces()
    {
        if (!ImGui.BeginTable("fx_pieces", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.ScrollY))
            return;
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn(LM.Get("GUI_Frame_AssetViewer_EffectPiece"));
        ImGui.TableSetupColumn(LM.Get("GUI_Frame_AssetViewer_EffectStart"));
        ImGui.TableSetupColumn(LM.Get("GUI_Frame_AssetViewer_EffectDuration"));
        ImGui.TableHeadersRow();
        string forever = LM.Get("GUI_Frame_AssetViewer_EffectContinuous");
        foreach (EffectComponent component in _effectPieces)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(component.Piece?.Label ?? $"{component.Type} {component.PieceIndex}");
            ImGui.TableNextColumn();
            ImGui.TextUnformatted($"{component.StartDelay:0.##} s");
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(component.Piece switch
            {
                EffectEmitter e => e.Duration > 1e-4f ? $"{e.Duration:0.##} s" : forever,
                EffectQuad q => q.Lifetime > 0f ? $"{q.Lifetime:0.##} s" : forever,
                EffectLight l => l.Lifetime > 0f ? $"{l.Lifetime:0.##} s" : forever,
                _ => "-",
            });
        }
        ImGui.EndTable();
    }

    /// <summary>The VFX slots the selected effect's emitters and quads draw with, in slot order.</summary>
    private List<int> EffectTextureIndices()
    {
        var set = new SortedSet<int>();
        foreach (EffectComponent component in _effectPieces)
        {
            var (textures, count) = component.Piece switch
            {
                EffectEmitter e => (e.Textures, e.TextureCount),
                EffectQuad q => (q.Textures, q.TextureCount),
                _ => ((IReadOnlyList<EffectTexture>)[], 0),
            };
            foreach (EffectTexture texture in textures.Take(Math.Max(1, count)))
                if (texture.DrawTextureIndex >= 0) set.Add(texture.DrawTextureIndex);
        }
        return [.. set];
    }

    private void RenderEffectTextures(List<int> indices)
    {
        if (indices.Count == 0)
        {
            ImGui.TextDisabled(LM.Get("GUI_Frame_AssetViewer_EffectNoTextures"));
            return;
        }
        var gpu = LevelEffectPlayer.Textures;
        var set = LevelEffectPlayer.Current?.Textures;
        var controller = LunaWindow.Instance.imGuiController;
        foreach (int index in indices)
        {
            if (index < gpu.Count && gpu[index] is { } texture)
            {
                ImGui.Image(controller.GetOrCreateImGuiBinding(graphicsDevice.ResourceFactory, texture.DeviceTexture), new Vector2(64, 64));
                ImGui.SameLine();
                ImGui.TextUnformatted($"{index}: {texture.Width}x{texture.Height} {set?[index]?.FormatName}");
            }
            else ImGui.TextDisabled($"{index}: -");
        }
    }
}
