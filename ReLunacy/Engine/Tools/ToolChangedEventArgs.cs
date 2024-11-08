namespace ReLunacy.Engine.Tools;

// Credits to github.com/RatchetModding/Replanetizer

public class ToolChangedEventArgs : EventArgs
{
    public ToolType ToolType { get; }

    public ToolChangedEventArgs(ToolType tt)
    {
        ToolType = tt;
    }
}
