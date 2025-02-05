namespace ReLunacy.Engine.Tools;

// Credits to github.com/RatchetModding/Replanetizer

public class ToolChangedEventArgs(ToolType tt) : EventArgs
{
    public ToolType ToolType { get; } = tt;
}
