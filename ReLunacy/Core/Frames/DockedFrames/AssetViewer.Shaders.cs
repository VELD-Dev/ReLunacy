using System.Numerics;
using NeoVeldrid;
using ReLunacy.Core.Placement;
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

    private void SelectShaderSlot((int Bangle, ulong Shader) slot, IMaterial material)
    {
        _selectedShaderSlot = slot;
        _originalShader = material;
    }

    private void DrawShaderSwapControls((int Bangle, ulong Shader) slot, IMaterial original, IMaterial current)
    {
        if (selectedMobyAsset == null && selectedTieAsset == null) return;
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
        if (!_shaderSwaps.ContainsKey(slot)) return;
        ImGui.SameLine();
        if (ImGui.Button(LM.Get("GUI_Frame_AssetViewer_RestoreShader")))
        {
            _shaderSwaps.Remove(slot);
            previewDirty = true;
        }
        ImGui.TextDisabled(LM.Get("GUI_Frame_AssetViewer_OriginalShader", original.Name ?? $"0x{original.Id:X}"));
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
        if (ImGui.Button(LM.Get("GUI_Frame_AssetViewer_ApplyShaderPreview")) && picked != null &&
            LunaWindow.Instance.Reader?.FindShaderMaterial(picked.Id) is { } replacement)
        {
            if (replacement.Id == slot.Shader)
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
        if (_shaderSwaps.Count == 0 || assetManager == null) return null;
        int bangle = (int)(pickId >> 16);
        int mesh = (int)(pickId & 0xFFFF);
        IMesh? source = selectedMobyAsset is { } moby ? moby.Moby.Bangles[bangle].Meshes[mesh]
            : selectedTieAsset is { } tie ? tie.Tie.Meshes[(int)pickId] : null;
        if (source == null || !_shaderSwaps.TryGetValue((selectedMobyAsset != null ? bangle : 0, source.Material.Id), out var replacement))
            return null;
        return assetManager.BuildDetachedMesh(source, replacement);
    }
}
