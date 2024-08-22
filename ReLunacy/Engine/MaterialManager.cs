namespace ReLunacy.Engine;

public class MaterialManager
{
    public static Dictionary<string, int> Materials = [];

    public static int LoadMaterial(string name, string vertexShaderPath, string fragmentShaderPath)
    {
        if (Materials.Any(x => x.Key == name))
        {
            int shaderID = Materials.First(x => x.Key == name).Value;
            return shaderID;
        }
        string vertexSource = File.ReadAllText(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) + "/" + vertexShaderPath);
        string fragmentSource = File.ReadAllText(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) + "/" + fragmentShaderPath);

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

        Materials.Add(name, programId);

        return programId;
    }
}
