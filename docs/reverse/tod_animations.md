# Tools of Destruction old-engine animations

Branch target: `old-engine-animation-system`.

This document separates byte layout re-derived from ToD data/EBOOT from behaviour whose final runtime semantics are still under reverse. Unknown data stays observable; unsupported modes are not silently replaced with a guessed pose.

For the complete implementation/UI/commit handoff and writer roadmap, also see `OLD_ENGINE_ANIMATION_SYSTEM_HANDOFF.md` in this directory.

## F000 clip record

`main.dat` section `0xF000`, big-endian, 0x40 bytes per record.

| Off | Type | Meaning |
|---|---|---|
| 00 | u16 | local animation slot; runtime rewrites it while building a Moby animset |
| 02 | u16 | flags: bit0 loop, bit1 additive/partial, bit2 packed |
| 04 | u16 | declared bone count; on additive clips it matches the active blend-mask count |
| 06 | u16 | numFrames |
| 08 | u32 | absolute main.dat name pointer |
| 0C | u32 | unknown; observed zero in audited fixtures |
| 10 | f32 | unknown; observed 0.0 in audited fixtures |
| 14 | f32 | linearSpeed |
| 18 | f32 | frameRate |
| 1C | u32 | rootTransformPtr |
| 20 | u32 | controlPtr |
| 24 | u32 | framesPtr |
| 28 | u64 | unknown; observed zero in audited fixtures |
| 30 | u16 | controlByteSize |
| 32 | u16 | frameStride |
| 34 | u16 | numReferenceValues |
| 36 | u16 | num16BitTracks |
| 38 | u16 | num8BitTracks |
| 3A | 6B | unknown; observed zero in audited fixtures |

Across seven audited ToD `main.dat` fixtures, 4,956 clips total, +0x0C, +0x10, +0x28..2F and +0x3A..3F were zero. `framesPtr - controlPtr == align128(controlByteSize)` held 4,956/4,956.

## Moby binding

Old D100:

```text
+0x16 u16 animationCount
+0x24 u32 animationListPointer
```

The +0x24 list is an ordered array of absolute pointers to F000 records. Its list position is the reliable local playback slot. Do not use the disk value at F000 +0x00 as stable identity because the runtime rewrites it. D500 is the global 1:1 pointer pool; per-Moby lists can share clips.

## Physical frames

EBOOT-confirmed stride:

```text
packed:     0x10 + align16(frameStride)
non-packed: 0x10 + align128(frameStride)
```

Packed payload identity:

```text
frameStride = align16(align16(num16BitTracks * 2) + num8BitTracks)
```

Stored-frame end:

```text
framesPtr + (numFrames + (looping ? 1 : 0)) * physicalStride == rootTransformPtr
```

The skeletal decoder starts at physical-frame +0x10. The prefix is usually zero; a few audited frames have first u32=1. Its consumer is still unknown, so the reader exposes four raw u32 values instead of assigning a semantic name.

```text
track16 = frame + 0x10
track8  = frame + 0x10 + align16(num16BitTracks * 2)
value8  = baseValue + signExtend(delta8) * 4
```

The `*4` path is EBOOT-confirmed.

## Control block

The block is laid out with the owning skeleton's total bone count, not F000.numBones:

```text
bones            = skeleton.numBones
refRotations     = 0
refValues        = align16(bones * 8)
refValueMasks    = align16(refValues + nRef*2)
track16Masks     = align16(refValueMasks + nRef*2)
track8Masks      = align16(track16Masks + n16*2)
track8BaseValues = align16(track8Masks + n8*2)
baseEnd          = align16(track8BaseValues + n8*2)
```

Normal clip: `controlByteSize = baseEnd`.

Additive/partial clip:

```text
blendMasks      = baseEnd
controlByteSize = baseEnd + align16(skeleton.numBones)
```

The size formula matched F000 +0x30 for 4,956/4,956 clips. Blend masks are one byte per skeleton bone. Across 322 additive clips every byte was 00/FF and `count(FF) == F000.numBones` for every clip.

## Track masks

The u16 is literally an offset into a 0x40-byte quantized pose record:

```text
mask = bone*0x40 + channel*0x10 + component*4 + 2
bone      = mask >> 6
channel   = (mask >> 4) & 3
component = (mask >> 2) & 3

channel 0 rotation
channel 1 scale
channel 2 translation
```

Across roughly 1.35 million audited masks, `mask & 3 == 2`; channel 3 was never observed. The EBOOT stores the int16 channel value directly at `poseBuffer + mask`.

## Skeleton animation reference pose

The shift values at D300 +0x10..+0x12 are individual bytes, not ushorts. Later EBOOT tracing corrected the earlier interpretation that +0x11 was padding:

```text
+10 byte scaleShift
+11 byte rotationShift
+12 byte translationShift
+13 byte unknown
+14 referenceTranslationBufferPointer
+18 unknown
```

`rotationShift` is preserved through the current skeleton model even though its complete writer-side quantization role is still under reverse.

Old-engine +0x14 points to three signed int16 translations per bone. The runtime initializes the quantized animation pose from:

```text
rotation    <- control.RefPoseRotations
scale       <- identity
translation <- D300 +0x14 reference translations
```

Therefore animation baseline TRS must not be invented by decomposing tms0/tms1. Those matrices stay responsible for bind/inverse-bind skinning.

```text
PositionScale = 1 / (0x8000 >> translationShift)
ScaleScale    = 1 / (0x8000 >> scaleShift)
```

## Root transform block

F000 +0x1C addresses `numFrames` records of 0x40 bytes:

```text
+00 float4 quaternion
+10 float3 scale
+1C float 0
+20 float3 translation
+2C float 0
+30 u32 flags
+34 u32 0
+38 u32 0
+3C float mode
```

Across 266,236 audited entries, flags were only 1 or 3; flags==3 iff scale was non-identity. Mode was 1.0 for normal clips and 0.0 for additive clips. Exact composition/application order is still under reverse. ReLunacy parses and exposes these records but does not apply them yet.

## Branch playback policy

`AnimationPlayer` currently:

- refuses to sample additive clips as absolute poses;
- initializes channels from the EBOOT-confirmed reference sources;
- applies static reference values and frame tracks through validated masks;
- interpolates translations/scales linearly and rotations with slerp;
- walks hierarchy recursively with cycle detection;
- preserves raw D300 bone flags and uses bit 0 for the current scale-isolation preview behaviour;
- outputs row-vector `InverseBindPose * AnimatedWorld` skin matrices;
- rejects invalid data instead of silently substituting a bind pose;
- parses root transforms but does not guess their composition order.

The exact native semantics of the bone-flag scale-isolation behaviour still need a dedicated proof before a writer treats that interpretation as final.

## Remaining reverse targets

Do not call the format 100% complete until these are resolved:

1. exact additive/partial composition semantics;
2. exact root-transform composition order;
3. consumer/meaning of non-zero 0x10 frame prefixes;
4. exact gameplay/authoring use of `linearSpeed`;
5. exact semantics of every F500 auxiliary field;
6. complete writer-side role of `rotationShift`;
7. D300 +0x13 and +0x18;
8. exact native semantics of D300 bone flags used by scale-isolation handling;
9. complete serialization/packing rules required for a lossless writer and new-animation encoder.

Asset Viewer > Animations > Animation Debug exposes the unresolved pieces that are useful during further reverse work.
