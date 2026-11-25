using System.Numerics;
using NeoVeldrid;
using ReLunacy.Core.Placement;
using ReLunacy.Core.Project;
using ReLunacy.Engine.Assets.Shrubs;
using ReLunacy.Engine.Packing;
using ReLunacy.Engine.Assets.Browser;
using ReLunacy.Engine.Assets.Interfaces;
using ReLunacy.Engine.Rendering.Resources;
using ReLunacy.Utility;
using ReLunacy.Utility.Localization;

namespace ReLunacy.Core.Frames.DockedFrames;

public partial class AssetViewer
{
    private readonly Dictionary<(int Bangle, ulong Shader), IMaterial> _shaderSwaps = [];
    private readonly Dictionary<GpuTexture, Texture> _swapTextures = [];
    private bool _shaderSwapAwaitingUploads;
    private int _shaderSwapRevision;
    private Shrub? _selectedShrub;
    private (int Bangle, ulong Shader)? _selectedShaderSlot;
    private IMaterial? _originalShader;
    private bool _openShaderPicker, _scrollToPickerShader;
    private string _shaderSearch = "";
    private ulong? _pickedShader;
    private List<AssetEntry> _shaderChoices = [];
    private List<AssetEntry> _filteredShaderChoices = [];

    private void ResetShaderSwaps()
    {
        _shaderSwaps.Clear();
        _selectedShaderSlot = null;
        _originalShader = null;
        _openShaderPicker = false;
        _shaderChoices.Clear();
        _filteredShaderChoices.Clear();
    }

    private ShaderSwapSlot? GetSwapSlot((int Bangle, ulong Shader) slot)
    {
        if (selectedMobyAsset is { } moby) return new(ShaderSwapKind.Moby, moby.Moby.Id, slot.Bangle, slot.Shader);
        if (selectedTieAsset is { } tie) return new(ShaderSwapKind.Tie, tie.Tie.Id, 0, slot.Shader);
        if (selectedUFragAsset is { } ufrag) return new(ShaderSwapKind.UFrag, ufrag.ZoneId, ufrag.Index, slot.Shader);
        if (_selectedShrub is { } shrub) return new(ShaderSwapKind.Shrub, shrub.Id, 0, slot.Shader);
        return null;
    }

    private IMaterial? GetShaderSwap((int Bangle, ulong Shader) slot)
    {
        if (_shaderSwaps.TryGetValue(slot, out var preview)) return preview.Id == slot.Shader ? null : preview;
        return GetSwapSlot(slot) is { } key && ShaderSwapEdits.Get(key) is { } edit
            ? LunaWindow.Instance.Reader?.FindShaderMaterial(edit.Replacement)
            : null;
    }

    private void SelectShaderSlot((int Bangle, ulong Shader) slot, IMaterial material)
    {
        _selectedShaderSlot = slot;
        _originalShader = material;
    }

    private void DrawShaderSwapControls((int Bangle, ulong Shader) slot, IMaterial original, IMaterial current)
    {
        if (GetSwapSlot(slot) is not { } key) return;
        if (ImGui.Button(LM.Get("GUI_Frame_AssetViewer_PreviewShaderSwap")))
        {
            var level = LunaWindow.Instance.Level;
            _shaderChoices = AssetsBrowserFrame.EnsureIndex()?.Entries
                .Where(e => e.Kind == AssetKind.Shader && level?.Shaders.ContainsKey(e.Id) == true)
                .ToList() ?? [];
            _filteredShaderChoices = _shaderChoices;
            _shaderSearch = "";
            _pickedShader = current.Id;
            _scrollToPickerShader = true;
            _openShaderPicker = true;
        }
        bool staged = ShaderSwapEdits.Get(key) != null;
        if (!_shaderSwaps.ContainsKey(slot) && !staged) return;
        ImGui.SameLine();
        if (ImGui.Button(LM.Get("GUI_Frame_AssetViewer_RestoreShader")))
        {
            _shaderSwaps.Remove(slot);
            ShaderSwapEdits.Set(key, null, LM.Get("GUI_Frame_AssetViewer_RestoreShader"));
            previewDirty = true;
        }
        ImGui.TextDisabled(LM.Get("GUI_Frame_AssetViewer_OriginalShader", original.Name ?? $"0x{original.Id:X}"));
        if (staged) ImGui.TextDisabled(LM.Get("GUI_Frame_AssetViewer_StagedShaderSwap"));
    }

    private void DrawShaderPicker()
    {
        string popup = LM.Get("GUI_Frame_AssetViewer_ShaderPicker") + "###asset_shader_picker";
        if (_openShaderPicker)
        {
            ImGui.OpenPopup(popup);
            _openShaderPicker = false;
        }
        ImGui.SetNextWindowSize(new Vector2(660f, 500f), ImGuiCond.FirstUseEver);
        if (!ImGui.BeginPopupModal(popup)) return;
        if (_selectedShaderSlot is not { } slot)
        {
            ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
            return;
        }

        ThumbnailService.Singleton.Pump();
        ImGui.SetNextItemWidth(-1f);
        bool searchChanged = ImGui.InputTextWithHint("##shader_search", LM.Get("GUI_Frame_ShaderBrowser_SearchHint", _shaderChoices.Count), ref _shaderSearch, 128);
        if (searchChanged)
        {
            string query = _shaderSearch.Trim();
            _filteredShaderChoices = _shaderChoices.Where(e =>
                TuidLink.Matches(query, e.Path, e.Id) || TuidLink.Matches(query, e.FullName, e.Id)).ToList();
        }

        if (ImGui.BeginChild("##shader_choices", new Vector2(0f, -ImGui.GetFrameHeightWithSpacing() * 2), ImGuiChildFlags.Borders))
        {
            if (searchChanged) ImGui.SetScrollY(0f);
            if (_filteredShaderChoices.Count == 0)
                ImGui.TextDisabled(LM.Get("GUI_Frame_AssetViewer_NoMatchingShaders"));
            if (ShaderThumbnailGrid.Draw(_filteredShaderChoices, _pickedShader, ref _scrollToPickerShader) is { } choice)
                _pickedShader = choice.Id;
        }
        ImGui.EndChild();

        var picked = _filteredShaderChoices.FirstOrDefault(e => e.Id == _pickedShader);
        ImGui.TextUnformatted(picked?.Name ?? LM.Get("GUI_Frame_ShaderBrowser_NoSelection"));
        ImGui.BeginDisabled(picked == null);
        bool preview = ImGui.Button(LM.Get("GUI_Frame_AssetViewer_ApplyShaderPreview"));
        bool stage = false;
        var key = GetSwapSlot(slot);
        if (key.HasValue && LunaWindow.Instance.Level?.IsOldEngine == false)
        {
            ImGui.SameLine();
            stage = ImGui.Button(LM.Get("GUI_Frame_AssetViewer_StageShaderSwap"));
        }
        if ((preview || stage) && picked != null &&
            LunaWindow.Instance.Reader?.FindShaderMaterial(picked.Id) is { } replacement)
        {
            if (stage && key is { } persistent)
            {
                _shaderSwaps.Remove(slot);
                var meshes = selectedMobyAsset?.Moby.Bangles[slot.Bangle].Meshes ?? selectedTieAsset?.Tie.Meshes;
                int[] indices = meshes == null ? [0] : Enumerable.Range(0, meshes.Count)
                    .Where(i => meshes[i].Material.Id == slot.Shader).ToArray();
                ShaderSwapEdits.Set(persistent, new(replacement.Id, indices), LM.Get("GUI_Frame_AssetViewer_StageShaderSwap"));
            }
            else if (replacement.Id == slot.Shader && (!key.HasValue || ShaderSwapEdits.Get(key.Value) == null))
                _shaderSwaps.Remove(slot);
            else
                _shaderSwaps[slot] = replacement;
            previewDirty = true;
            ImGui.CloseCurrentPopup();
        }
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button(LM.Get("GUI_Common_CancelWord"))) ImGui.CloseCurrentPopup();
        ImGui.EndPopup();
    }

    private bool ShaderSwapTexturesChanged() =>
        _swapTextures.Any(pair => !ReferenceEquals(pair.Key.DeviceTexture, pair.Value));

    private (float[] Vertices, uint[] Indices, RenderMaterial Material)? TryBuildShaderSwap(uint pickId)
    {
        if (assetManager == null || pickId == uint.MaxValue) return null;
        if (selectedUFragAsset is { } terrain)
            return GetShaderSwap((0, terrain.UFrag.Material.Id)) is { } material
                ? assetManager.BuildDetachedUFrag(terrain.UFrag, material)
                : null;

        int bangle = (int)(pickId >> 16);
        int mesh = (int)(pickId & 0xFFFF);
        IMesh? source = selectedMobyAsset is { } moby ? moby.Moby.Bangles[bangle].Meshes[mesh]
            : selectedTieAsset is { } tie ? tie.Tie.Meshes[(int)pickId]
            : _selectedShrub?.Meshes[(int)pickId];
        if (source == null) return null;
        ulong original = _selectedShrub?.MaterialTUID ?? source.Material.Id;
        return GetShaderSwap((bangle, original)) is { } replacement
            ? assetManager.BuildDetachedMesh(source, replacement)
            : null;
    }

    public bool SelectShrub(ulong id)
    {
        if (LunaWindow.Instance.Level?.Shrubs.GetValueOrDefault(id) is not { } shrub) return false;
        SelectedMobyAsset = null;
        SelectedTieAsset = null;
        SelectedUFragAsset = null;
        ClearEffect();
        _selectedFoliage = null;
        _selectedShrub = shrub;
        var (centre, radius) = shrub.GetBoundingSphere();
        Camera.Position = centre - Vector3.UnitZ * Math.Max(radius * 3f, 0.25f);
        Camera.Target = centre;
        IsDirty = true;
        return true;
    }

    private void RenderShrubDetails(Shrub shrub)
    {
        ImGui.TextUnformatted(shrub.Name ?? $"{shrub.Id:X16}");
        if (LunaWindow.Instance.Reader?.FindShaderMaterial(shrub.MaterialTUID) is not { } material) return;
        RenderShaderGrid([material], "shrub_shader_grid");
        RenderInlineMaterialDetails();
    }
}
