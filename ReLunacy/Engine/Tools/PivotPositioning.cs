namespace ReLunacy.Engine.Tools;

// Credits to github.com/RatchetModding/Replanetizer

public class PivotPositioning(int key, string humanName) : EnhancedEnum<PivotPositioning>(key, humanName)
{
    public static readonly PivotPositioning Mean = new(0, "Mean");
    public static readonly PivotPositioning IndividualOrigins = new(1, "Individual origins");
}
