namespace ReLunacy.Engine;

public class MaterialManager
{
    public static Dictionary<string, int> ShaderHandles = [];
    public static Material SelectedVolumeMat;

    public static void Initialize()
    {
        LoadShader("stdv;transparentf", "Shaders/stdv.glsl", "Shaders/transparentf.glsl");
        LoadShader("stdv;solidf", "Shaders/stdv.glsl", "Shaders/solidf.glsl");
        LoadShader("stdv;whitef", "Shaders/stdvsingle.glsl", "Shaders/whitef.glsl");
        LoadShader("stdv;volumef", "Shaders/stdv.glsl", "Shaders/volumef.glsl");
        LoadShader("stdv;pickingf", "Shaders/stdv.glsl", "Shaders/pickingf.glsl");
        LoadShader("screenv;compositef", "Shaders/screenv.glsl", "Shaders/compositef.glsl");
        LoadShader("screenv;screenf", "Shaders/screenv.glsl", "Shaders/screenf.glsl");
        SelectedVolumeMat = new(ShaderHandles["stdv;volumef"], null, LibLunacy.Shaders.RenderingMode.Opaque, PrimitiveType.Lines);
    }

    public static int LoadShader(string name, string vertexShaderPath, string fragmentShaderPath)
    {
        if (ShaderHandles.Any(x => x.Key == name))
        {
            int shaderID = ShaderHandles.First(x => x.Key == name).Value;
            return shaderID;
        }
        string vertexSource = File.ReadAllText(Program.AppPath + "/" + vertexShaderPath);
        string fragmentSource = File.ReadAllText(Program.AppPath + "/" + fragmentShaderPath);

        int vertexProgramId = GL.CreateShader(ShaderType.VertexShader);
        int fragmentProgramId = GL.CreateShader(ShaderType.FragmentShader);

        GL.ShaderSource(vertexProgramId, vertexSource);
        GL.CompileShader(vertexProgramId);

        GL.ShaderSource(fragmentProgramId, fragmentSource);
        GL.CompileShader(fragmentProgramId);

        GL.GetShader(vertexProgramId, ShaderParameter.CompileStatus, out int res);
        if (res != (int)All.True)
        {
            string infoLog = GL.GetShaderInfoLog(vertexProgramId);
            throw new Exception($"Error when compiling vertex shader at {vertexShaderPath}.\nError: {infoLog}");
        }

        GL.GetShader(fragmentProgramId, ShaderParameter.CompileStatus, out res);
        if (res != (int)All.True)
        {
            string infoLog = GL.GetShaderInfoLog(fragmentProgramId);
            throw new Exception($"Error when compiling fragment shader at {fragmentShaderPath}.\nError: {infoLog}");
        }

        int programId = GL.CreateProgram();
        GL.AttachShader(programId, vertexProgramId);
        GL.AttachShader(programId, fragmentProgramId);

        GL.LinkProgram(programId);

        GL.GetProgram(programId, GetProgramParameterName.LinkStatus, out res);
        if (res != (int)All.True)
        {
            string infoLog = GL.GetProgramInfoLog(programId);
            throw new Exception($"Error when linking program.\nError Code {GL.GetError()}.\nError Log: {infoLog}");
        }

        GL.DetachShader(programId, vertexProgramId);
        GL.DetachShader(programId, fragmentProgramId);

        GL.DeleteShader(vertexProgramId);
        GL.DeleteShader(fragmentProgramId);

        ShaderHandles.Add(name, programId);

        return programId;
    }

    public static void Dispose()
    {
        foreach(var shader in ShaderHandles)
        {
            GL.DeleteProgram(shader.Value);
        }
    }
}
