using Vector3 = OpenTK.Mathematics.Vector3;

namespace ReLunacy.Engine.Tools;

// Credits to github.com/RatchetModding/Replanetizer

public class TransformToolData
{
    public Vector3 cameraPos;
    private bool _vecComputed = false;
    private Vector3 _vec;
    public Vector3 axisDir;
    public Vector3 mousePrevDir;
    public Vector3 mouseCurrDir;
    private bool _mouseDiffDirComputer = false;
    private Vector3 _mouseDiffDir;
    public Vector3 Vec
    {
        get
        {
            if(_vecComputed) return _vec;
            _vec = axisDir * MouseDiffDir * 50.0f;
            _vecComputed = true;
            return _vec;
        }
    }
    public Vector3 MouseDiffDir
    {
        get
        {
            if (_mouseDiffDirComputer) return _mouseDiffDir;
            _mouseDiffDir = mouseCurrDir - mousePrevDir;
            _mouseDiffDirComputer = true;
            return _mouseDiffDir;
        }
    }

    public TransformToolData(Camera camera, Vector3 mprevdir, Vector3 mcurrdir, Vector3 axdir)
    {
        cameraPos = camera.transform.position.ToOpenTK();
        mousePrevDir = mprevdir;
        mouseCurrDir = mcurrdir;
        axisDir = axdir;
    }

}
