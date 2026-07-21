namespace ReLunacy.Engine.Assets.Interfaces;

public interface IAsset
{
    ulong Id { get; }
    string? Name { get; }
    bool IsLoaded { get; }
}
