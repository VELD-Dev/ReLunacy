using Bliss.CSharp.Effects;
using Bliss.CSharp.Graphics.VertexTypes;
using ReLunacy.Utility;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Veldrid;

namespace ReLunacy.Core;

public static class ShaderManager
{
    public static readonly Dictionary<string, Effect> Shaders = [];

    public static void LoadDefaultShaders(GraphicsDevice gd)
    {
        LunaLog.LogInfo("Loading default shaders...");

        LoadShader(gd, "solid", "Shaders/stdv.glsl", "Shaders/solidf.glsl");
        LoadShader(gd, "composite", "Shaders/stdv.glsl", "Shaders/compositef.glsl");
        LoadShader(gd, "transparent", "Shaders/stdv.glsl", "Shaders/transparentf.glsl");
        LoadShader(gd, "volume", "Shaders/stdv.glsl", "Shaders/volumef.glsl");

        LunaLog.LogInfo("Loaded default shaders !");
    }

    public static Effect LoadShader(GraphicsDevice gd, string name, string vertShaderPath, string fragShaderPath)
    {
        if(Shaders.TryGetValue(name, out Effect? value))
            return value;

        byte[] vertSource = File.ReadAllBytes(Path.Combine(Program.EditorPath, vertShaderPath));
        byte[] fragSource = File.ReadAllBytes(Path.Combine(Program.EditorPath, fragShaderPath));

        var effect = new Effect(gd, Vertex3D.VertexLayout, vertSource, fragSource);
        Shaders.Add(name, effect);

        return effect;
    }
}
