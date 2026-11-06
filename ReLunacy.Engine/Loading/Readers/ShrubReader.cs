using ReLunacy.Engine.Assets.Geometry;
using ReLunacy.Engine.Assets.Interfaces;
using ReLunacy.Engine.Loading.IO;
using ReLunacy.Engine.Loading.Objects;

namespace ReLunacy.Engine.Loading.Readers;

/// <summary>
/// Reads shrubs.dat into shrub assets.
///
/// New engine only - the old engine has no shrub family at all, so this returns nothing for Tools
/// of Destruction and Quest for Booty rather than guessing at one.
/// </summary>
public sealed class ShrubReader(FileManager fileManager, MaterialReader materialReader)
{
    private readonly FileManager _fileManager = fileManager ?? throw new ArgumentNullException(nameof(fileManager));
    private readonly MaterialReader _materialReader = materialReader ?? throw new ArgumentNullException(nameof(materialReader));

    /// <param name="progress">The level load's bar; the Shrub phase counts one item per class.</param>
    public Dictionary<ulong, Assets.Shrubs.Shrub> ReadAllShrubs(LevelLoadProgress? progress = null)
    {
        var shrubs = new Dictionary<ulong, Assets.Shrubs.Shrub>();
        if (_fileManager.isOld) return shrubs;

        AssetPointer[] pointers = NewEngineAssetLookup.ReadPointers(
            _fileManager, NewEngineAssetLookup.ShrubPointers, "Shrubs");
        if (pointers.Length == 0) return shrubs;

        if (!NewEngineAssetLookup.TryGetPayloadStream(_fileManager, "shrubs.dat", "Shrubs", out Stream? stream) ||
            stream is null)
            return shrubs;

        progress?.SetTotal(pointers.Length);
        foreach (AssetPointer pointer in pointers)
        {
            progress?.Advance();
            byte[] slice = NewEngineAssetLookup.ReadPayload(stream, pointer, "Shrub");
            var parsed = ShrubClass.Read(slice);
            if (parsed.VertexCount == 0 || parsed.Indices.Length == 0) continue;

            // The shader TUID is carried, NOT resolved. GetMaterialByTuid mints a runtime texture
            // id per (texture, binding descriptor) pair into the shared registry the renderer packs
            // its atlas from, so resolving materials for geometry nothing draws shifted every other
            // material's atlas coordinates and the whole level sampled the wrong textures - green
            // surfaces and moire, measured as 3,388 registry entries on zolar_forest against 3,386.
            // Whoever draws shrubs resolves IShrub.MaterialTUID at that point, when the cost is
            // paid for geometry that is actually on screen.
            IMaterial placeholder = _materialReader.GetDefaultMaterial();

            // Keyed by the pointer table's TUID, as Ties are, because that is what a zone's class
            // table names - not the TUID the class also stores in its own header (they agree on
            // all 49 measured, but the table is the thing doing the addressing).
            var geometry = new GeometryData(
                pointer.TUID, parsed.Positions, parsed.UVs, parsed.Indices);

            var mesh = new Mesh(geometry, placeholder, parsed.SourcePath);
            shrubs[pointer.TUID] = new Assets.Shrubs.Shrub(
                pointer.TUID, [mesh], parsed.SourcePath, parsed.DrawDistances, parsed.BoundingSphere,
                materialTuid: parsed.MaterialTUID);
        }

        return shrubs;
    }
}
