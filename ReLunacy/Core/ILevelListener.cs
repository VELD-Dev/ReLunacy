namespace ReLunacy.Core;

/// <summary>
/// Implemented by frames that cache anything derived from the currently loaded level (entities,
/// built models, textures, selection results, ...). LunaWindow calls these on every open frame -
/// via <c>openFrames.OfType&lt;ILevelListener&gt;()</c>, not a hard-coded per-frame-type list - so a
/// new frame that starts caching level data just has to implement this interface instead of
/// requiring a matching edit inside LunaWindow itself.
/// </summary>
public interface ILevelListener
{
    /// <summary>
    /// Called right before the current level's EntityManager/AssetManager/FileManager get
    /// disposed. Drop every reference to level-derived data here (entities, Models, ITextures,
    /// cached search/usage results) - anything still held past this point is a dangling reference
    /// to an object that's about to be destroyed. Don't dispose AssetManager-owned resources
    /// yourself; only clear references and dispose whatever the frame itself uniquely owns.
    /// </summary>
    void OnLevelUnloading();

    /// <summary>Called after a new level's AssetManager/EntityManager have finished building, so the frame can (re)populate whatever it shows from the new data.</summary>
    void OnLevelLoaded();
}
