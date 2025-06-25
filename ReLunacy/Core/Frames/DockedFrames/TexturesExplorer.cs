using Bliss.CSharp.Textures;
using ImGuiNET;
using ReLunacy.Utility;
using ReLunacy.Utility.Localization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace ReLunacy.Core.Frames.DockedFrames;

public class TexturesExplorer : DockedFrame
{
    protected override ImGuiCond DockingConditions { get; set; } = ImGuiCond.Appearing;
    protected override Vector2 DefaultPosition { get; set; } = ImGui.GetMainViewport().GetWorkCenter();
    protected override ImGuiWindowFlags WindowFlags { get; set; } = ImGuiWindowFlags.NoScrollbar;

    private string inputText = "";

    // for now I do it this way so it's faster
    private List<nint> texturesHandles = [];
    private List<string> texturesNames = [];

    public TexturesExplorer() : base()
    {
        FrameName = LM.Get("GUI_Frame_TextureExplorer");
    }

    public void TransmitTextures(AssetManager assetManager, LunaLoader loader)
    {
        List<Texture2D> textures = [.. assetManager.Textures.Values];
        for(int i = 0; i < textures.Count; i++)
        {
            var tex = textures[i];
            var lunaTex = loader.Textures.Values.ToArray()[i];
            var handle = LunaWindow.Instance.imGuiController.GetOrCreateImGuiBinding(LunaWindow.Instance.GraphicsDevice.ResourceFactory, tex.DeviceTexture);
            texturesHandles.Add(handle);
            texturesNames.Add(lunaTex.name);
        }
    }

    protected override void Render(double deltaTime)
    {
        if(ImGui.InputTextWithHint(LM.Get("GUI_Frame_TextureExplorer_SearchLabel"), LM.Get("GUI_Frame_TextureExplorer_SearchHint", LunaWindow.Instance.AssetManager?.Textures.Count ?? 0), ref inputText, 128))
        {

        }
        if (ImGui.BeginChild("texture_gridview", ImGui.GetContentRegionAvail(), ImGuiChildFlags.Borders))
        {
            var columns = (int)ImGui.GetContentRegionAvail().X / 128;
            ImGui.Columns(columns, "texture_grid", false);
            for(int i = 0; i < texturesHandles.Count; i++)
            {
                var texPtr = texturesHandles.ToArray()[i];
                var texName = texturesNames.ToArray()[i];
                if (i > 0 && i % columns == 0) ImGui.Spacing();

                ImGui.Image(texPtr, new(128, 128), Vector2.UnitY, Vector2.UnitX);
                ImGui.Text(texName ?? $"Tex_{i}");

                ImGui.NextColumn();
            }
        }
        ImGui.EndChild();
    }

    public override void RenderAsWindow(double deltaTime)
    {
        ImGui.SetNextWindowPos(DefaultPosition, ImGuiCond.Appearing);
        base.RenderAsWindow(deltaTime);
    }
}
