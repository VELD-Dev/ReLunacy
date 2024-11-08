namespace ReLunacy.Engine.Tools;

// Credits to github.com/RatchetModding/Replanetizer

public class Toolbox
{
    public Tool? Tool => _tool;
    public ToolType ToolType => _type;
    public TransformSpace TransformSpace { get; set; } = TransformSpace.Global;
    public PivotPositioning PivotPositioning { get; set; } = PivotPositioning.Mean;

    public event EventHandler<ToolChangedEventArgs>? ToolChanged;

    private Tool? _tool;
    private ToolType _type = ToolType.None;
    // TODO: Define tools

    public Toolbox()
    {
        // Define tools
    }

    public void ChangeTool(ToolType tt)
    {
        if (tt == ToolType) return;
        _type = tt;

        if (tt == ToolType.None)                _tool = null;
        else if (tt == ToolType.Translation)    _tool = TRANSLATION_TOOL;
        else if (tt == ToolType.Rotation)       _tool = ROTATION_TOOL;
        else if (tt == ToolType.Scaling)        _tool = SCALING_TOOL;

        //_tool?.Reset();   
        OnToolChanged(new(ToolType));
    }

    private void OnToolChanged(ToolChangedEventArgs e)
    {
        ToolChanged?.Invoke(this, e);
    }
}
