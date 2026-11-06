using ReLunacy.Engine.Loading.IO;
using ReLunacy.Engine.Loading.Textures;

namespace ReLunacy.Engine.Loading.Shaders;

public class Shader
{
    public const uint NewInternalTUIDSecID = 0x5600;
    public const uint PointerID = 0x1D100;
    public const int NewTextureSlotCount = 6;

    public ulong TUID { get; private set; }
    public string name = string.Empty;
    public ShaderMetadataOld? metadataOld;
    public ShaderMetadataNew? metadataNew;
    public ShaderReference? reference;
    public StreamHelper shaderStream;
    public bool isOld;

    public Texture? Albedo;
    public Texture? Normal;
    // Insomniac's "expensive" map - four masks packed into one texture:
    //   R = gloss/specular mask   multiplies the specular term
    //   G = parallax height       fed to fma(h, parallaxScale, parallaxBias)
    //   B = additive emissive     added to the baked light colour
    //   A = detail-map mask       modulates detail map intensity
    // NOTE: GltfExporter.ApplyExpensiveChannels still uses an older R=spec/G=metallic/B=emissive
    // split (R and B match; G does not).
    public Texture? Expensive;
    // Old engine only (ShaderMetadataOld.detailMap, offset 0x0C) - new engine's equivalent isn't
    // identified yet. DXT5 or ARGB8888, four channels:
    //   R = normal delta dx   added to base normal's .x/.y, scaled by detailNormalStrength
    //   G = normal delta dy
    //   B = colour offset     added to albedo, scaled by detailAlbedoStrength
    //   A = gloss offset      added to tex2 gloss term, scaled by detailSpecStrength
    // Both offsets are signed, -1..+1. Sampled at a UV tiled by ShaderMetadataOld.detailTiling
    // (0x58), falling back to AssetManager.DefaultDetailTiling when that field is zero.
    public Texture? DetailMap;

    // New-engine 0x5000 carries six AUTHORITATIVE local texture indices. Slots 4/5 are genuine
    // auxiliaries on 21/666 and 1/666 acid_refinery_pvp materials respectively; the embedded
    // fragment shaders correlate them with RSX texture units 8 and 9. 0x5D00 supplies source
    // names/hash lookups, but does not decide whether a slot exists.
    public Texture? Auxiliary4;
    public Texture? Auxiliary5;
    public uint[] NewTextureSourceIds { get; } = new uint[NewTextureSlotCount];
    public string?[] NewTextureSourceNames { get; } = new string?[NewTextureSlotCount];
    // Descriptor provenance is PER BINDING. The same source texture occurs with multiple 0x5200
    // states in retail data, including CONTROL1 differences, so a texture-global "first wins"
    // descriptor loses authored RSX state.
    public ReLunacy.Engine.Assets.Textures.OldTextureDescriptorState?[] NewTextureDescriptors { get; } =
        new ReLunacy.Engine.Assets.Textures.OldTextureDescriptorState?[NewTextureSlotCount];

    /// <summary>Retail 0x33001/0x33002/0x33003 fragment-shader bundle, when this material owns one.</summary>
    public NewEngineShaderBundle? ShaderBundle { get; internal set; }

    public RenderingMode RenderingMode => (RenderingMode)(isOld ? metadataOld!.Value.renderingMode : metadataNew!.Value.renderingMode);

    // Old-engine authoring overlay. `0` is a real authored value meaning an empty slot, so a separate
    // dirty bit is required instead of using null as both "no edit" and "clear this slot". The values
    // are descriptor addresses from main.dat:0x5200, exactly like the four retail 0x5000 fields.
    private readonly uint[] _oldAuthoredTextureBindings = new uint[4];
    private readonly bool[] _oldTextureBindingDirty = new bool[4];

    public bool HasOldTextureBindingEdits => isOld && _oldTextureBindingDirty.Any(value => value);

    public uint GetOldTextureBinding(int slot)
    {
        if ((uint)slot >= 4u) throw new ArgumentOutOfRangeException(nameof(slot));
        if (_oldTextureBindingDirty[slot]) return _oldAuthoredTextureBindings[slot];
        return slot switch
        {
            0 => checked((uint)(Albedo?.id ?? 0)),
            1 => checked((uint)(Normal?.id ?? 0)),
            2 => checked((uint)(Expensive?.id ?? 0)),
            3 => checked((uint)(DetailMap?.id ?? 0)),
            _ => 0,
        };
    }

    public bool IsOldTextureBindingEdited(int slot)
    {
        if ((uint)slot >= 4u) throw new ArgumentOutOfRangeException(nameof(slot));
        return _oldTextureBindingDirty[slot];
    }

    public void SetOldTextureBinding(int slot, uint descriptorAddress)
    {
        if (!isOld || metadataOld == null)
            throw new InvalidOperationException("Native old texture bindings can only be authored on an old-engine shader.");
        if ((uint)slot >= 4u) throw new ArgumentOutOfRangeException(nameof(slot));
        _oldAuthoredTextureBindings[slot] = descriptorAddress;
        _oldTextureBindingDirty[slot] = true;
    }

    public void RevertOldTextureBinding(int slot)
    {
        if ((uint)slot >= 4u) throw new ArgumentOutOfRangeException(nameof(slot));
        _oldTextureBindingDirty[slot] = false;
        _oldAuthoredTextureBindings[slot] = 0;
    }

    public void AcceptPersistedOldTextureBindings()
    {
        if (!isOld || metadataOld is not { } metadata)
            return;

        for (int slot = 0; slot < 4; slot++)
        {
            if (!_oldTextureBindingDirty[slot]) continue;
            uint value = _oldAuthoredTextureBindings[slot];
            switch (slot)
            {
                case 0: metadata.albedo = value; break;
                case 1: metadata.normal = value; break;
                case 2: metadata.expensive = value; break;
                case 3: metadata.detailMap = value; break;
            }
            _oldTextureBindingDirty[slot] = false;
        }
        metadataOld = metadata;
    }

    public Shader(StreamHelper sh, bool isOld = false, uint index = 0, ulong? tuidOverride = null)
    {
        this.isOld = isOld;
        shaderStream = sh;

        if (isOld)
        {
            metadataOld = ShaderMetadataOld.Read(sh);
            TUID = index;
        }
        else
        {
            metadataNew = ShaderMetadataNew.Read(sh);
            var ig = new IGFile(sh.BaseStream);
            var refSec = ig.QuerySection(ShaderReference.ID);
            sh.Seek(refSec.offset);
            reference = ShaderReference.Read(sh);
            // reference.Value.TUID reads as a genuine 0 for every shader in this format (see
            // TextureShaderLoader.LoadShadersNew, which already works around this for its own
            // Shaders dictionary key) - tuidOverride carries the real identity instead: the
            // assetlookup pointer-table TUID, the same value Tie.ShaderTUIDs/MaterialReader resolve
            // materials against. Without this, every new-engine Shader object's own TUID property
            // read back as 0, so anything comparing against it directly (ShaderBrowser.SelectShader,
            // its used-shader filter, its Find Usages button) silently failed for every new-engine
            // shader even though the dictionary lookup and material resolution were already correct.
            TUID = tuidOverride ?? reference.Value.TUID;
        }
    }
}