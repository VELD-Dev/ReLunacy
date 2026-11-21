using ReLunacy.Engine.Assets.Effects;
using ReLunacy.Engine.Assets.Interfaces;
using ReLunacy.Engine.Scene;

namespace ReLunacy.Engine.Assets.Browser;

/// <summary>
/// What each asset of an <see cref="AssetIndex"/> uses and what uses it, and how many times the level places a class.
/// Classes use shaders (Moby bangle meshes and their variants, Tie meshes, a Shrub's 0x5600 shader, a UFrag's layers);
/// shaders use textures (Shader.Albedo/Normal/Expensive/DetailMap/Auxiliary4/5); effects use their child packages and
/// effect textures. A material that is no shader of the level (old-engine foliage) points at its textures directly.
/// A class not read yet has no known uses. Nothing is loaded or uploaded to build it.
/// </summary>
public sealed class AssetReferences
{
    private readonly Dictionary<AssetKey, List<AssetKey>> _uses = [];
    private readonly Dictionary<AssetKey, List<AssetKey>> _usedBy = [];
    private readonly Dictionary<AssetKey, int> _placements = [];

    /// <summary>The assets <paramref name="asset"/> uses, in the order found.</summary>
    public IReadOnlyList<AssetKey> Uses(AssetKey asset) => _uses.TryGetValue(asset, out var list) ? list : [];

    /// <summary>The assets that use <paramref name="asset"/>.</summary>
    public IReadOnlyList<AssetKey> UsedBy(AssetKey asset) => _usedBy.TryGetValue(asset, out var list) ? list : [];

    /// <summary>How many instances of a class (Moby, Tie, Shrub, Foliage) or placed controllers of an effect the level has.</summary>
    public int Placements(AssetKey asset) => _placements.GetValueOrDefault(asset);

    public IReadOnlyList<AssetKey> Uses(AssetEntry entry) => Uses(entry.Key);
    public IReadOnlyList<AssetKey> UsedBy(AssetEntry entry) => UsedBy(entry.Key);
    public int Placements(AssetEntry entry) => Placements(entry.Key);

    public int LinkCount => _uses.Values.Sum(l => l.Count);

    private void Link(AssetKey user, AssetKey used)
    {
        if (user == used) return;
        if (!_uses.TryGetValue(user, out var uses)) _uses[user] = uses = [];
        if (uses.Contains(used)) return;
        uses.Add(used);
        if (!_usedBy.TryGetValue(used, out var users)) _usedBy[used] = users = [];
        users.Add(user);
    }

    internal static AssetReferences Build(AssetIndex index, AssetIndexSources sources)
    {
        var refs = new AssetReferences();
        var level = sources.Level;

        void Material(AssetKey user, IMaterial? material)
        {
            if (material == null) return;
            if (level.Shaders.ContainsKey(material.Id) || index.Get(new(AssetKind.Shader, material.Id)) != null)
            {
                refs.Link(user, new(AssetKind.Shader, material.Id));
                return;
            }
            foreach (var texture in (ITexture?[])[material.AlbedoTexture, material.NormalTexture, material.PropertiesTexture, material.DetailTexture])
                if (texture != null) refs.Link(user, new(AssetKind.Texture, index.TextureSource(texture.Id)));
        }

        foreach (var (id, moby) in level.Mobys)
        {
            var key = new AssetKey(AssetKind.Moby, id);
            foreach (var bangle in moby.Bangles)
                foreach (var mesh in bangle.Meshes)
                {
                    Material(key, mesh.Material);
                    foreach (var variant in mesh.MaterialVariants) Material(key, variant);
                }
        }

        foreach (var (id, tie) in level.Ties)
        {
            var key = new AssetKey(AssetKind.Tie, id);
            foreach (var mesh in tie.Meshes) Material(key, mesh.Material);
        }

        foreach (var (id, shrub) in level.Shrubs)
            if (shrub.MaterialTUID != 0) refs.Link(new(AssetKind.Shrub, id), new(AssetKind.Shader, shrub.MaterialTUID));

        foreach (var foliage in level.Foliages)
        {
            Material(new(AssetKind.Foliage, foliage.Id), foliage.Material);
            refs._placements[new(AssetKind.Foliage, foliage.Id)] = foliage.Placements.Count;
        }

        int zoneIndex = 0;
        foreach (var zone in level.Zones.Values)
        {
            for (int i = 0; i < zone.UFrags.Count; i++)
            {
                var ufrag = zone.UFrags[i];
                var key = new AssetKey(AssetKind.UFrag, AssetIndex.UFragId(zoneIndex, i));
                Material(key, ufrag.Material);
                Material(key, ufrag.SecondMaterial);
                // New-engine blend masks are zone data, not textures of the level: linked only when they are one.
                if (ufrag.BlendMask != null && index.Get(new(AssetKind.Texture, index.TextureSource(ufrag.BlendMask.Id))) is { } mask)
                    refs.Link(key, mask.Key);
            }
            zoneIndex++;
        }

        foreach (var (id, shader) in level.Shaders)
        {
            var key = new AssetKey(AssetKind.Shader, id);
            foreach (var texture in (Loading.Textures.Texture?[])[shader.Albedo, shader.Normal, shader.Expensive, shader.DetailMap, shader.Auxiliary4, shader.Auxiliary5])
                if (texture != null && texture.id != 0) refs.Link(key, new(AssetKind.Texture, texture.id));
        }
        foreach (var pending in sources.Regions.SelectMany(r => r.PendingShaders))
            foreach (var texture in pending.Textures)
                refs.Link(new(AssetKind.Shader, pending.Tuid), new(AssetKind.Texture, texture.Tuid));

        if (sources.Effects is { } effects) AddEffects(refs, effects);

        CountPlacements(refs, sources);
        return refs;
    }

    private static void AddEffects(AssetReferences refs, LevelEffects effects)
    {
        foreach (var package in effects.Library.Packages)
        {
            var key = new AssetKey(AssetKind.Effect, (ulong)package.Index);
            foreach (var component in package.Components)
            {
                switch (component.Piece)
                {
                    case EffectPackage child:
                        refs.Link(key, new(AssetKind.Effect, (ulong)child.Index));
                        break;
                    case EffectEmitter emitter:
                        Textures(key, emitter.Textures);
                        break;
                    case EffectQuad quad:
                        Textures(key, quad.Textures);
                        break;
                }
            }
        }

        foreach (var controller in effects.Controllers)
            if (effects.Resolve(controller).Package is { } placed)
            {
                var key = new AssetKey(AssetKind.Effect, (ulong)placed.Index);
                refs._placements[key] = refs._placements.GetValueOrDefault(key) + 1;
            }

        void Textures(AssetKey key, IReadOnlyList<EffectTexture> textures)
        {
            foreach (var texture in textures)
            {
                for (int h = 0; h < texture.Handles.Length; h++)
                    if (texture.TextureIndex(h) is >= 0 and var index) refs.Link(key, new(AssetKind.EffectTexture, (ulong)index));
                if (texture.CompositeIndex >= 0) refs.Link(key, new(AssetKind.EffectTexture, (ulong)texture.CompositeIndex));
            }
        }
    }

    /// <summary>From the scene when there is one, which counts what was added or deleted since; else from the level's files.</summary>
    private static void CountPlacements(AssetReferences refs, AssetIndexSources sources)
    {
        void Add(AssetKind kind, ulong id)
        {
            var key = new AssetKey(kind, id);
            refs._placements[key] = refs._placements.GetValueOrDefault(key) + 1;
        }

        bool fromScene = false;
        if (sources.SceneEntities != null)
            foreach (var entity in sources.SceneEntities)
            {
                switch (entity)
                {
                    case EntityMoby moby: Add(AssetKind.Moby, moby.BaseMoby.Id); fromScene = true; break;
                    case EntityTie tie: Add(AssetKind.Tie, tie.BaseTie.Id); fromScene = true; break;
                }
            }

        foreach (var region in sources.Level.Regions)
        {
            if (!fromScene)
                foreach (var moby in region.MobyInstances) Add(AssetKind.Moby, moby.Asset.Id);
            foreach (var zone in region.Zones)
            {
                if (!fromScene)
                    foreach (var tie in zone.TieInstances) Add(AssetKind.Tie, tie.Asset.Id);
                if (zone is Levels.Zone full)
                    foreach (var shrub in full.ShrubInstances) Add(AssetKind.Shrub, shrub.Asset.Id);
            }
        }
    }
}
