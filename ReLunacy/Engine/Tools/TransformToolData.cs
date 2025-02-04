using Vector3 = OpenTK.Mathematics.Vector3;

namespace ReLunacy.Engine.Tools;

// Credits to github.com/RatchetModding/Replanetizer

public class TransformToolData(Camera camera, Vec3 mprevdir, Vec3 mcurrdir, Vec3 axdir)
{
    public Vec3 cameraPos = camera.transform.Position;
    private bool _vecComputed = false;
    private Vec3 _vec;
    public Vec3 axisDir = axdir;
    public Vec3 mousePrevDir = mprevdir;
    public Vec3 mouseCurrDir = mcurrdir;
    private bool _mouseDiffDirComputer = false;
    private Vec3 _mouseDiffDir;
    public Vec3 Vec
    {
        get
        {
            if(_vecComputed) return _vec;
            _vec = axisDir * MouseDiffDir * 50.0f;
            _vecComputed = true;
            return _vec;
        }
    }
    public Vec3 MouseDiffDir
    {
        get
        {
            if (_mouseDiffDirComputer) return _mouseDiffDir;
            _mouseDiffDir = mouseCurrDir - mousePrevDir;
            _mouseDiffDirComputer = true;
            return _mouseDiffDir;
        }
    }
}
