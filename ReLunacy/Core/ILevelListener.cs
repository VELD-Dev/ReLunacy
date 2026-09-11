namespace ReLunacy.Core;

/// <summary>
/// Implemented by frames that cache anything derived from the currently loaded level (entities,
/// built models, textures, selection results, ...). LunaWindow calls these on every open frame
/// via <c>openFrames.OfType&lt;ILevelListener&gt;()</c>.
/// </summary>
public interface ILevelListener
{
    /// <summary>
    /// Called right before the current level's EntityManager/AssetManager/FileManager get
    /// disposed. Drop every reference to level-derived data here; don't dispose
    /// AssetManager-owned resources, only clear references and dispose what the frame itself owns.
    /// </summary>
    void OnLevelUnloading();

    /// <summary>Called after a new level's AssetManager/EntityManager have finished building, so the frame can (re)populate whatever it shows from the new data.</summary>
    void OnLevelLoaded();
}
