using ReLunacy.Engine.Loading.IO;

namespace ReLunacy.Engine.Loading.Vertices;

/// <summary>A tie's LIGHTMAP UV channel: one pair of big-endian half floats per vertex, in its own
/// tightly packed 4-byte-stride array OUTSIDE the 20-byte <see cref="VertexFormat0"/> record.
///
/// CONFIRMED BY THE GAME'S OWN VERTEX PROGRAM (dev/ties/ties_vertex_shader_LOD0.glsl). That program
/// reads five attributes and routes them like this:
///     location 0 -> in_pos          SINT16 x4  @+0   position.xyz, and see VertexFormat0.boneIndex
///     location 1 -> in_weight       SFLOAT16x2 @+8   -> tc0.XY   (albedo/base UV)
///     location 2 -> in_normal       CMP        @+12  -> tc3      (normal)
///     location 3 -> in_diff_color   CMP        @+16  -> tc4      (TANGENT, not a colour)
///     location 4 -> in_spec_color   THIS       stride 4, own stream
/// and then, verbatim:
///     r0.z = in_spec_color.xy.x;  r0.w = in_spec_color.xy.y;   dst_reg7 = r0;   tc0 = dst_reg7;
/// so tc0.ZW IS LOCATION 4 - the exact place the tie fragment programs sample the baked light
/// colour (tex4) and light direction (tex14). No scale, no bias, no flip is applied on the way:
/// whatever bytes are in the file are the texture coordinates. A V flip was tried in the loader and
/// looked worse in the app, so ties use these UVs raw - if the orientation is ever wrong again, fix
/// it in the sampling path UFrags share, not per asset type.
/// The names in that listing are RSX's fixed attribute-slot names (0=position, 1=weight, 2=normal,
/// 3=diffuse colour, 4=specular colour, ...), not semantics - Insomniac repurposed slots 3 and 4.
///
/// This is not a second UV set bolted into VertexFormat0 - there is no room in it, and searching it
/// is what kept failing. RPCS3's captured DrawParametersBuffer for a tie draw
/// (dev/ties/VS_ties_set0-2-buffer.bin) describes every attribute of every draw in the frame, and
/// the tie shape is unambiguous. Of 83,014 draws whose attribute 0 is a stride-20 SINT16 x4
/// position (VertexFormat0's exact size and type), 81,197 bind a FIFTH attribute at location 4
/// living in its own stream at stride 4:
///     attr0  stride=20 offset=+0   SINT16 x4     -> VertexFormat0.position + boneIndex
///     attr1  stride=20 offset=+8   SFLOAT16 x2   -> VertexFormat0.UVs
///     attr2  stride=20 offset=+12  CMP 11:11:10  -> VertexFormat0 packed normal
///     attr3  stride=20 offset=+16  CMP 11:11:10  -> VertexFormat0 packed tangent
///     attr4  stride=4               (own stream) -> THIS
/// attr4 is SFLOAT16 x2 on 30,015 of those draws and UBYTE x4 on the other 51,182 - mutually
/// exclusive, same slot. THE SLOT IS POLYMORPHIC, and the reduced tie fragment programs show what
/// the other reading is. In ties_fragment_shader_far.glsl:
///     h1.w   = clamp16(tc0.zzzz).w;
///     h1.xyz = clamp16(h3 * h1.wwww).xyz;            // light term SCALED by tc0.z
/// and in ties_fragment_shader_medium_no_normal.glsl:
///     h0.w   = clamp16(tc0.zzzz).w;
///     h0.xyz = mix(h0.xyz, tex0.xyz, tc0.z != 0);
///     h0.xyz = clamp16(h0 * h0.wwww).xyz;            // albedo SCALED by tc0.z
/// tc0.z is location 4's FIRST component, so in those variants this attribute is not a texture
/// coordinate at all - it is a per-vertex scalar multiplying the surface colour. That is the
/// PER-VERTEX BAKED LIGHTING the WWS post-mortem budgets for ("50 MB Baked lighting data (mix of
/// light maps and per-vertex data)"). So: half x2 here = lightmap UV pair; UNORM8 x4 here = baked
/// per-vertex light, of which these variants consume one channel.
/// Which one a draw gets is a per-USE decision, the same mechanism as every other knob in "Shader
/// Usage Controls" - the full-featured program samples the baked atlases, the shader-LOD/reduced
/// programs take the cheap per-vertex term instead.
/// This corrects an earlier claim in this file that UNORM8 here meant "an 8-bit UV pair, not vertex
/// colour". That is true only of the LOD0 program, which consumes .xy as a pair; it is not true of
/// the family. Note also 0x18 is not where the UNORM8 arrays live: decoded there as a u8 UV pair the
/// area test below gives median r = -0.099 across 163 ties against +0.899 for the half decode - but
/// that test assumes UVs, so it says nothing about the vertex-colour reading either way.
/// Every one of the 81,197 has frequency=1 and modulo=0, i.e. genuinely indexed per vertex, not a
/// per-instance divisor; and swap_bytes=1, i.e. big-endian source, same as every other attribute
/// here. Hence: 4 bytes per vertex, two big-endian halves, exactly one entry per vertex.
///
/// WHERE THE ARRAY LIVES - SOLVED, and the answer is per MESH, not per tie.
/// It sits immediately after the tie's own vertex block, at TieMetadataOld's 0x18 (see that field:
/// 0x18 is the END offset of the vertex block, not a size - 0x18-0x14 is exactly vertexCount*20 for
/// 186/193 ties), and a mesh's own window begins at 0x18 + TieMesh.verticesIndex * 4. Nothing else
/// is needed: fitting a constant byte delta per tie by brute force returns delta = 0.
///
/// The long hunt through this file's history for "the other 132 ties" was chasing a bug in the
/// question. Baked lighting is a per-MESH property, so requiring every vertex of a tie to decode
/// in [0,1] fails a tie as soon as ONE of its meshes is unshaded or holds something else:
///     meshes with a valid array here          2082 / 3771
///     ties where ALL meshes are valid           61 / 193   <- all the old code could see
///     ties where SOME meshes are valid         112 / 193   <- discarded whole, wrongly
///     ties where none are                       20 / 193
/// Those 112 include every tie carrying one of metropolis's 256x256 lightmaps, the largest in the
/// level. Over the scorable valid meshes the area test below gives median r = 0.874 - the same
/// distribution as the ties that already worked, so this is the same data, not a weaker second tier.
/// Validate per mesh (TieReader.SliceLightmapUVs) and the problem is gone.
///
/// THE RANGE CHECK ALONE IS NOT ENOUGH AT MESH GRANULARITY. Of the 2082 meshes it admits, 259 are
/// all-zero (an unshaded mesh's slot - harmless, they sample texel 0,0 exactly as the game does) and
/// 494 carry enough triangles to test; of those, 16% score below 0.5 on the area test. Those are
/// windows that pass the range check by luck, and they render as the bake REPEATED across the
/// surface, which is what "some ties show the lightmap twice" looks like. TieReader.LooksLikeUnwrap
/// screens them. The failures cluster by tie (18 and 105 are the worst on metropolis), which is what
/// you would expect if those ties' real arrays are somewhere else entirely rather than absent.
/// Blind spot: 1329 admitted meshes have too few triangles to test (34,764 vertices total) and are
/// accepted untested. If a doubled bake survives, look there first.
///
/// It is NOT the lightmap index. Those were checked directly: 1728 of metropolis's 4848 tie
/// instances carry one, all 1728 distinct, no reuse, and the high 16 bits of the u32 at 0x58 are
/// zero in all 4848. Two instances never share a bake, so a doubled-looking tie is always UVs.
///
/// THE TEST THAT SETTLES IT is area preservation, not range and not overlap. A lightmap unwrap
/// allocates texels roughly in proportion to world-space surface area, so per triangle, log(UV
/// area) tracks log(3D area). Over the tie's own indexed triangles, at 0x18:
///     real       n=47 ties   median r = +0.899   40 of 47 above 0.70
///     control    n=60        median r = -0.042    0 of 60 above 0.50
/// where the control is a random 4-aligned window elsewhere in section 0x9000 of the same length
/// that ALSO passes the range check - i.e. matched on every criterion except being this tie's data.
/// Separation is total. (14 of the 61 are all-zero and drop out of the correlation as degenerate;
/// they are what an unshaded tie's slot looks like. It happens per MESH too, not just per tie -
/// tie 85 has real charts for 18% of its vertices and exact (0,0) for the rest.)
///
/// Two earlier numbers in this comment were retracted. "1.05 vs 292 triangles per covered texel"
/// compared against a control that did not have to pass the range check, which made it look ~280x
/// more decisive than it is; with a matched control the same metric gives 4.19 vs 10.72, which is
/// suggestive at best. Do NOT use a range test on its own either: decoded as halves, section 0x9000
/// has 60,000-200,000 four-aligned windows per length entirely inside [0,1], so "all in range" is
/// close to vacuous at blob scale (same trap as UFragVertex.UVs2's u16 reading - see that comment).
/// A distinct-value/quantisation test does not separate them at all.
///
/// THE OTHER 132 ARE NOT IN SECTION 0x9000 - searched and not found, which is worth knowing before
/// anyone searches it again. Using the area correlation as the search score, every 4-aligned window
/// in the blob was ranked for each tie, restricted to windows that decode fully in-range AND do not
/// overlap any tie's declared vertex block. The search is sound: on ties whose array is known, the
/// true offset ranks #1 out of 80,000-180,000 candidates, 10 times out of 10. Run over the 132 it
/// returns exactly one hit above threshold, at r=0.751 against known-good ties scoring 0.74-0.97 -
/// i.e. indistinguishable from the best of ~150,000 draws from the null distribution. Treat it as
/// nothing found. main.dat was searched the same way for the 12 worst offenders (ties with the most
/// lightmapped instances and no array) and is also clean: best r = 0.436 against known-good ties
/// scoring 0.74-0.97. And there is nowhere else obvious to look - vertices.dat is exactly its two
/// sections plus a 128-byte header, with no slack, and the level has no separate lightmap file.
/// STILL UNTESTED, and the best remaining lead: the same search for an 8-BIT array. The shader
/// admits UNORM8 at location 4 and the draw census splits 51,182 UBYTE against 30,015 half - the
/// same lopsided majority as 132 ties without an array against 61 with one. Only the fixed offset
/// 0x18 was checked as u8 (it fails); a blob-wide u8 search was attempted and abandoned, because the
/// range prefilter that makes the half search tractable is vacuous for u8 (every byte pair is in
/// [0,1] by construction) and the locality prefilter tried instead just selects runs of zeros.
/// Whoever picks this up needs a prefilter that demands real spread AND vertex-to-vertex coherence.
/// So those ties either have no baked lighting at all (consistent with the WWS
/// post-mortem's per-use "what should have shadows" control, and with the per-mesh version of the
/// same thing: tie 85 has real charts for 18% of its vertices and exact (0,0) for the rest), or
/// their UVs are stored per-INSTANCE, or outside this section entirely. An earlier draft of this
/// comment blamed an insufficient gap to the next tie; that reasoning was junk, because UFrag and
/// moby vertices share this blob, so "the gap" was never tie-exclusive in the first place.
///
/// THE UV ARRAY IS PER-ASSET; THE BAKED TEXTURE IS PER-INSTANCE. These are separate questions and
/// the answers differ, which is easy to conflate. The texture side is settled and per-instance: 1728
/// of metropolis's 4848 tie instances carry a lightmap index, every one distinct, no reuse (see
/// TieInstance.LightmapIndex). The UV side is per-asset, and the array at 0x18 proves it directly -
/// the gap between a tie's vertex block and the next tie's is exactly vertexCount*4 REGARDLESS of
/// how many instances the tie has. Tie 3 has 310 instances and still gets 4*n bytes; tie 60 has 1
/// and gets the same. If the array were per-instance the gap would scale with the instance count,
/// and it never does. The size accounting agrees: per-asset for all 193 ties is 3.07 MB, per
/// lightmapped instance is 10.99 MB, and per instance outright is 25.69 MB, in a 27.85 MB section
/// that already spends 15.36 MB on tie vertices. Only the per-asset figure fits alongside 1,987
/// UFrags and every moby.
/// So one unwrap is shared by every placement of a tie, and each placement gets its own baked
/// texture painted into that shared UV space - which is exactly why ties need no atlas offset the
/// way UFrags do, and why these UVs span the full [0,1] square.
///
/// WHICH TIES ARE LIT, on metropolis: 105 ties have at least one lightmapped instance but NO UV
/// array at 0x18 - those are the ones that render unlit today and shouldn't. 48 ties have no
/// lightmapped instance at all, and those are genuinely meant to be unlit: the WWS post-mortem's
/// per-use "what should have shadows" control, served by shader-template variants with the lightmap
/// inputs switched off (the captured far/medium tie programs are exactly those - they read tc0.z as
/// a lone scalar and never sample tex4/tex14). Do not treat that second group as a bug.
/// </summary>
public readonly record struct TieLightmapUV
{
    /// <summary>Bytes per vertex - the RSX attribute's stride.</summary>
    public const uint Size = 0x04;

    public readonly Half U;
    public readonly Half V;

    public TieLightmapUV(Half u, Half v)
    {
        U = u;
        V = v;
    }

    /// <summary>Reads <paramref name="vertexCount"/> consecutive pairs starting at
    /// <paramref name="offset"/>, returning them flat as [u0, v0, u1, v1, ...] to match
    /// IUFrag.GetLightmapUVs()'s shape.
    ///
    /// The offset is REQUIRED and deliberately has no default. TieMetadataOld's 0x18 is the right
    /// answer for only 61 of 193 ties on metropolis (see the type comment), and defaulting to it
    /// would silently decode unrelated blob bytes into plausible-looking in-range UVs for the rest -
    /// which is worse than having no lightmap at all, because it renders instead of failing.
    /// <paramref name="sh"/> must be positioned over section 0x9000's stream, big-endian.</summary>
    public static float[] ReadArray(StreamHelper sh, uint offset, int vertexCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(vertexCount);

        var uvs = new float[vertexCount * 2];

        sh.Seek(offset);
        for (int i = 0; i < vertexCount; i++)
        {
            uvs[i * 2 + 0] = (float)sh.ReadHalf();
            uvs[i * 2 + 1] = (float)sh.ReadHalf();
        }

        return uvs;
    }
}
