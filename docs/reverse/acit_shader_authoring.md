# A Crack in Time shader authoring

## Making a material for a custom moon

1. Open the ACiT level where the moon will live. Use **File > Import > Texture** for each image and choose its shader slot. The same imported texture can be assigned to multiple shaders.
2. Use **File > Import > Create shader** or **Create shader** in the Shader Browser. Choose **Standard (0x00)** material settings, select imported textures, and choose Opaque, Cut-out or Sorted blend. An unassigned albedo slot can generate a 4×4 solid colour. Automatic template selection prefers a standard Tie shader with the requested render mode, an unlit primitive, and matching texture slots.
3. Preview the shader in the Assets Browser or Shader Viewer. In **File > Import > Model**, select the shader as the existing shader for the matching glTF material. This works as soon as the shader is queued in the target region. For a two-layer Tie, assign one standard shader per layer and import the separate overlay mask with the model.
4. Save to the project. The writer registers standalone textures before shaders, and shaders before the Tie classes and placements that reference them. The project retains texture names and slot roles under `editor.authoredTextures`, so textures stay selectable by name after a reload. It keeps the source template TUID under `editor.authoredShaders`, so a later model import can use the same Tie primitive settings before the new shader has been used by a Tie. Both tables are keyed by level path and TUID. An installed game archive has the texture data and TUID. Its texture table has no standalone name field.

The authoring dialogs target ACiT levels with a standard Tie shader template in the selected region. The renderer can preview more classes than the writer can author. The notes below distinguish observed data from renderer interpretations.

## Shader classes

The class byte is `shaders.dat` → shader resource → `0x5000 +0x22`. This census counts **3,056 distinct TUIDs over 22 ACiT levels**. Family counts overlap when assets share a shader. The class dispatch interpretation is based on the equivalent QForce table at `0x00E7DB18` and the section and slot patterns in ACiT. Counts and the editor catalog are in [ShaderClasses.cs](../../ReLunacy.Engine/Importing/ShaderClasses.cs).

| Class | Count | Observed use | Slots and record differences | Creation |
| --- | ---: | --- | --- | --- |
| `0x00` Standard | 2,945 | Ties, Mobys, UFrags | Albedo, optional normal, expensive and detail. Five-section resource. | Supported |
| `0x04` Render target | 6 | Moby screens and holovision | Albedo or no texture, `+0x23` bit `0x20` | Inspect |
| `0x05` Fur fins | 5 | Moby fur cards | Albedo, render mode 6 | Inspect |
| `0x0C` Fur shells | 7 | Moby shell fur | Albedo and normal, shell count in the low byte of `+0x38` | Inspect |
| `0x0E` Foliage | 6 | Leaf materials outside the scanned Tie/Moby/UFrag references | Albedo, normal, expensive and detail, float at `+0x40` | Inspect |
| `0x0F` Anisotropic BRDF | 2 | Zoni Moby materials | Albedo, normal, expensive and detail, float at `+0x40` | Inspect |
| `0x13` Embedded program | 85 | 19 Tie, 70 Moby and 5 UFrag shaders | Embedded fragment programs and variants in `0x33001`/`0x33002`, selectors in `0x33003`. Slots 4–5 depend on the program. | Inspect |

Retail two-layer Tie shaders were all class `0x00` and mode 0 in the census. Lightmapping and the overlay layer belong to the Tie primitive. Lightmapping is governed by primitive `+0x0E` bit 0. The overlay uses a mask at `+0x0A` and a second shader slot at `+0x22`. A glow in a standard shader comes from the expensive map's blue channel. Sky, fur, render-target materials and embedded programs need their own calling conventions. The standard template writer has no program compiler.

## Standard shader resource

All fields below are big-endian. Section offsets are relative to the resource. In a texture descriptor, an offset is relative to the shader's texture allocation.

| Section | Role |
| --- | --- |
| `0x5000` | One 0x80-byte material record. Six signed 32-bit local texture indices start at `+0x00`; `-1` means unused. |
| `0x5A00` | One 8-byte entry per local texture, its 32-bit texture ID followed by its allocation length aligned to 0x80. |
| `0x5200` | One 0x20-byte RSX texture descriptor per local texture, including format, width, height, mip count, and sampler state. |
| `0x5D00` | One 0x40-byte reference record. `+0x00` is the shader TUID, `+0x08` the name pointer, `+0x10` the six texture IDs, and `+0x28` the six texture-name pointers. |
| `0x11300` | String pool for the shader name and the six texture-name entries. Its seven references are listed in the IGHW fix-up table. |

In `0x5000`, `+0x20` is the feature byte. The standard writer uses `0x08` for the normal slot, `0x04` for the expensive feature, and `0x10` for detail. It retains the template's parallax bit `0x01` when the normal and expensive slots are both present. ACiT stores parallax scale and bias at `+0x60` and `+0x64`, and detail UV tiling at `+0x70`. The standard writer updates the latter when requested and carries the remaining constants and non-edited flags from the template. Flag `0x02` at `+0x23` controls double-sided drawing.

`0x5000 +0x21` is the render mode. The standard census and current standalone authoring choices are:

| Mode | Standard shaders | Description | Creation |
| --- | ---: | --- | --- |
| 0 | 2,353 | Opaque | Yes |
| 1 | 119 | Unsorted blend | Inspect |
| 2 | 76 | Sorted additive | Inspect |
| 3 | 7 | Blend in pass 0x40 | Inspect |
| 4 | 165 | Cut-out | Yes |
| 5 | 14 | Opaque variant | Inspect |
| 6 | 211 | Sorted blend | Yes |

Mode 4 uses the game's render-state cutoff. The material record has no independently authored cutoff. `+0x30` is the normal-strength field used by the renderer.

## Texture slot and archive layout

| Slot | Input | Encoding and interpretation |
| --- | --- | --- |
| 0 Albedo | RGBA, with sRGB colour | DXT1 or DXT5 when alpha is retained and used. Gamma-enabled RGB sampling. |
| 1 Normal | Tangent-space RGB normal map | DXT5 with the biased X/Z slope in alpha and Y/Z in green. The green channel can be flipped on import. |
| 2 Expensive | Packed linear RGBA | R specular, G parallax height, B emissive, A detail mask. DXT1 or DXT5 when alpha is retained and used. |
| 3 Detail | Packed unsigned bytes centred on 128 | Square swizzled A8R8G8B8. R/G normal perturbation, B albedo detail and A specular detail. |

The importer resamples to powers of two up to 512 pixels per side. Albedo mips are filtered in linear colour and normal mips are renormalized. DXT chains end when the longer side reaches 4 pixels. Square detail maps retain mips down to 1×1. A single-mip texture lives in `textures.dat`. Otherwise the top mip goes in `highmips.dat` and the rest in `textures.dat`.

The low 32 bits of the texture TUID identify the texture in its shaders. The high 32 bits are the Insomniac CRC32 of the texture's source name. `assetlookup.dat` stores format and dimensions at `0x1D140`, resident and high-mip pointers at `0x1D180` and `0x1D1C0`, and region masks at `0x1D181`. The shader resource is appended to `shaders.dat`, indexed by `0x1D100` and `0x1D101`, and listed in each target region's `0x1C100`. Registration also updates the affected region budget and archive TOCs. A second shader binding the same imported texture reuses its lookup rows and can extend its region mask.

See [Tie mesh splitting and asset registration](newengine_tie_mesh_splitting.md) for the Tie-side records and template model.
