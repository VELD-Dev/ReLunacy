using Bliss.CSharp.Images;
using Bliss.CSharp.Textures;
using ImGuiNET;
using LibLunacy.Textures;
using ReLunacy.Utility;
using ReLunacy.Utility.Localization;
using System.Numerics;

namespace ReLunacy.Core.Frames.DockedFrames;

public record struct TextureObject
{
    public TextureObject(Texture lunaTexture, Texture2D tex2d)
    {
        Texture = lunaTexture;
        TexturePtr = LunaWindow.Instance.imGuiController.GetOrCreateImGuiBinding(LunaWindow.Instance.GraphicsDevice.ResourceFactory, tex2d.DeviceTexture);
        BlissTexture = tex2d;
    }

    public readonly string? TextureName => Texture.name;
    public readonly Texture Texture;
    public readonly Texture2D BlissTexture;
    public readonly nint TexturePtr;
}

public class TexturesExplorer : DockedFrame
{
    protected override ImGuiCond DockingConditions { get; set; } = ImGuiCond.Appearing;
    protected override Vector2 DefaultPosition { get; set; } = ImGui.GetMainViewport().GetWorkCenter();
    protected override ImGuiWindowFlags WindowFlags { get; set; } = ImGuiWindowFlags.NoScrollbar;

    private string inputText = "";

    // for now I do it this way so it's faster
    private List<TextureObject> textureObjects = [];

    private int selectedTexture = -1;
    private nint selectedTexturePtr = nint.Zero;

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
            textureObjects.Add(new(lunaTex, tex));
        }
    }

    protected override void Render(double deltaTime)
    {
        if(ImGui.InputTextWithHint(LM.Get("GUI_Frame_TextureExplorer_SearchLabel"), LM.Get("GUI_Frame_TextureExplorer_SearchHint", LunaWindow.Instance.AssetManager?.Textures.Count ?? 0), ref inputText, 128))
        {

        }
        if (ImGui.BeginChild("texture_gridview", new (ImGui.GetContentRegionAvail().X / 2, ImGui.GetContentRegionAvail().Y), ImGuiChildFlags.Borders))
        {
            var columns = (int)ImGui.GetContentRegionAvail().X / 128;
            if(columns >= 1)
            {
                ImGui.Columns(columns, "texture_grid", false);
                for (int i = 0; i < textureObjects.Count; i++)
                {
                    var texobj = textureObjects[i];
                    if (i > 0 && i % columns == 0) ImGui.Spacing();

                    ImGui.Image(texobj.TexturePtr, new(128, 128), Vector2.UnitY, Vector2.UnitX);
                    if (ImGui.IsItemClicked())
                    {
                        selectedTexture = i;
                        selectedTexturePtr = texobj.TexturePtr;
                    }
                    ImGui.Text(texobj.TextureName ?? $"Tex_{i}");


                    ImGui.NextColumn();
                }
            }
        }
        ImGui.EndChild();
        if(selectedTexture != -1)
        {
            ImGui.SameLine();
            if(ImGui.BeginChild("texture_preview", ImGui.GetContentRegionAvail(), ImGuiChildFlags.Borders))
            {
                var selection = textureObjects[selectedTexture];

                ImGui.Image(selectedTexturePtr, new(ImGui.GetContentRegionAvail().X, ImGui.GetContentRegionAvail().X), Vector2.UnitY, Vector2.UnitX);
                ImGui.Text(LM.Get("GUI_Frame_TextureExplorer_Preview_SelectColorChannel"));
                ImGui.SameLine();
                // Optimizations will be done by making copies of these channels only when the texture is selected.
                if(ImGui.Button("All"))
                {
                    selectedTexturePtr = selection.TexturePtr;
                }
                ImGui.SameLine();
                if (ImGui.Button("R"))
                {
                }
                ImGui.SameLine();
                if(ImGui.Button("G"))
                {
                }
                ImGui.SameLine();
                if(ImGui.Button("B"))
                {
                }
                if(selection.Texture.TexFormat != TextureFormat.DXT1 && selection.Texture.TexFormat != TextureFormat.R5G6B5)
                {
                    ImGui.SameLine();
                    if (ImGui.Button("A"))
                    {
                    }
                }
                ImGui.Separator();
                ImGui.BeginGroup();
                ImGui.Text(LM.Get("GUI_Frame_TextureExplorer_Preview_TextureName"));
                ImGui.Text(LM.Get("GUI_Frame_TextureExplorer_Preview_TextureCompressionType"));
                ImGui.Text(LM.Get("GUI_Frame_TextureExplorer_Preview_TextureDimensions"));
                ImGui.Text(LM.Get("GUI_Frame_TextureExplorer_Preview_TextureBufferSize"));
                ImGui.Text(LM.Get("GUI_Frame_TextureExplorer_Preview_TextureSizeOnDisk"));
                ImGui.EndGroup();
                ImGui.SameLine();
                ImGui.BeginGroup();
                ImGui.Text(selection.TextureName ?? $"Tex_{selectedTexture}");
                ImGui.Text(selection.Texture.TexFormat.ToString());
                ImGui.Text($"{selection.Texture.Width}x{selection.Texture.Height}");
                ImGui.Text($"{selection.BlissTexture.Images[0].Data.Length / 1000f}KB");
                ImGui.Text($"{selection.Texture.data.Length / 1000f}KB");
                ImGui.EndGroup();
                if(ImGui.Button(LM.Get("GUI_Frame_TextureExplorer_Preview_ExportRaw")))
                {
                    var path = Path.Combine(Program.EditorPath, "Extracted");
                    if (!Directory.Exists(path))
                        Directory.CreateDirectory(path);

                    File.WriteAllBytes(Path.Combine(path, selection.TextureName != null ? selection.TextureName + ".raw" : $"Tex_{selectedTexture}.raw"), selection.Texture.data);
                }
                ImGui.SameLine();
                if(ImGui.Button(LM.Get("GUI_Frame_TextureExplorer_Preview_ExportPNG")))
                {
                    var path = Path.Combine(Program.EditorPath, "Extracted");
                    if (!Directory.Exists(path))
                        Directory.CreateDirectory(path);

                    var clone = (Image)selection.BlissTexture.Images[0].Clone();
                    if (selection.Texture.TexFormat > TextureFormat.A8R8G8B8)
                        clone.FlipVertical();
                    clone.SaveAsPng(Path.Combine(path, selection.TextureName != null ? selection.TextureName + ".png" : $"Tex_{selectedTexture}.png"));
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
