# Ratchet & Clank Q-Force animation runtime reverse

Source fixture: retail Q-Force PS3 EBOOT supplied for the animation-reader audit. The executable is a stripped big-endian PPC64 Cell/LV2 ELF. Addresses below are EBOOT virtual code addresses.

This document records only findings that have a direct static instruction-level basis. Inferences are labelled separately. The purpose is to use the later engine as an independent oracle for the shared ReLunacy animation model without silently projecting Q-Force-only behaviour back onto Tools of Destruction.

## Runtime architecture confirmation

The executable contains the class/type strings:

```text
IGPS3_MotionRotationFrame_CLASS
IGPS3_MotionTranslationFrame_CLASS
IGPS3_MotionChannel_CLASS
IGPS3_PhaseChannel_CLASS
IGPS3_ClipData_CLASS
IGPS3_AnimMetaData_CLASS
IGPS3_AnimGamePlayData_CLASS
IGPS3_SkeletonDbg_CLASS
IGPS3_JointDbg_CLASS
```

It also contains `animsets.dat`, `igAnim`, `user pose clip`, DynamicJoint diagnostics and AttachmentJointMap/DynamicJoint-related runtime types.

### PROVED: richer disk clip -> compact runtime clip

The `igAnim` subsystem around `0x68316C` allocates/initializes a compact 0x40-byte animation header. `0x6831A4..0x68320C` explicitly writes fields through +0x3E, including the eight halfwords at +0x30,+0x32,+0x34,+0x36,+0x38,+0x3A,+0x3C,+0x3E.

`0x683A04..0x683A84` copies an existing 0x40-byte clip core field-for-field. `0x683B74..0x683BF0` constructs the built-in `user pose clip` in that same representation.

This is important for ReLunacy's architecture: Q-Force's disk F000 is richer than ToD's F000, yet Q-Force itself normalizes animation data into a compact common runtime form. Keeping raw F000 revisions behind `IAnimationClipStorage` and exposing a shared `AnimationClip` is therefore consistent with the native engine rather than merely a code-cleanliness abstraction.

## Native skeletal frame sampler

The main clip sampler begins at `0x6850C8`. Its inputs include the compact clip header at r4 and sample time in f1.

### PROVED: clip-local clock is `time * frameRate`

Relevant sequence:

```text
0x685150  lfs   f31,0x18(r4)   ; clip frameRate
...
0x6851B8  fmuls f31,f1,f31     ; sampleTime * frameRate
...
0x685214  fctiwz ...           ; integer frame
0x685220  fcfid  ...
0x68522C  fsubs f31,f31,f1     ; fractional part
```

Therefore the skeletal clip decoder itself is not driven by a hidden variable-rate per-frame clock. `AnimationPlayer` using `Time * FrameRate` is correct at this layer. Phase/Motion channels may still transform the time passed into this function, but they are a higher-level playback concern.

### PROVED: physical stride is aligned from F000 +0x32

At `0x685188..0x6851A0`, the sampler reads clip +0x32 and computes:

```text
packed:     align16(frameStride)
non-packed: align128(frameStride)
```

Q-Force's new-engine skeletal frame has no ToD-style +0x10 prefix in this path, so its physical frame distance is the aligned payload stride itself.

This corrects the earlier ReLunacy new-engine adapter, which advanced frames by raw `frameStride`. The branch now uses the aligned physical stride.

### PROVED: authored loop interpolation uses a stored endpoint

For a looping clip, the native sampler reduces the integer frame modulo `numFrames`, but it then samples `current` and `current + 1`. It does not synthesize the second endpoint by wrapping to logical frame 0.

Without a frame remap, `0x685250..0x6852A0` forms the current frame address and the immediately following physical frame address.

With a remap, `0x685424..0x685454` reads two independent u16 entries:

```text
remap[current]
remap[current + 1]
```

The second lookup therefore intentionally permits the logical endpoint index `numFrames` on an authored looping clip.

This matches an independently proven ToD invariant: looping old-engine clips physically store one extra skeletal frame before the root-transform block. ReLunacy now exposes that native loop endpoint in both storage backends instead of interpolating the last frame against a synthesized frame-0 sample.

This is a concrete candidate for visible loop hitches, orientation pops and apparently uneven animation pacing near loop seams.

### PROVED: frame remap is applied to each interpolation endpoint

The remap path does not remap only the integer frame and then assume storedFrame+1. Both logical endpoints are looked up separately and multiplied by the aligned physical stride.

ReLunacy's adapter therefore keeps remap as a logical-to-stored mapping, including the authored loop endpoint.

## DynamicJoint path

Q-Force has a distinct post/base-skeleton DynamicJoint path around `0x6A2450..0x6A28E8`.

### PROVED layout facts

The runtime obtains a dynamic-joint table through a Moby-owned object and validates an index against a byte count. Records use a 592-byte stride:

```text
mulli ..., jointIndex, 592
```

Within each record:

```text
+0x240 / +576 : tested state/control value
+0x244 / +580 : joint/matrix index
```

The matrix index is multiplied by 64 bytes:

```text
slwi ..., index, 6
```

and used against a matrix array at Moby +0x98 in several paths. Matrices are then passed through the transform helper at `0x6988B8`.

The EBOOT contains explicit diagnostics:

```text
Invalid Dynamic Joint Hierarchy for Moby named:
Invalid Dynamic Joint:
```

### PROVED scale interaction

`0x6A24F0` reads float Moby +0xD0 and applies it during a DynamicJoint transform path. Another path at `0x6A2820..0x6A2830` computes a reciprocal against the same +0xD0 value before composing additional matrices.

This proves that dynamic/attachment transforms are not simply another ordinary skeletal child matrix with unconditional inherited SRT. Moby scale is handled explicitly around this subsystem.

### INFERENCE relevant to ReLunacy symptoms

A visible piece that is driven through DynamicJoint/AttachmentJointMap semantics can have an apparently correct base skeleton animation while still facing the wrong way or drifting if the viewer treats that piece as ordinary skinning only. This is a strong candidate for the remaining 'some meshes go in the wrong direction / do not adapt correctly' cases, but the exact attachment composition order still needs to be traced before patching renderer transforms.

## Partial/additive system

The EBOOT contains explicit higher-level concepts including partial clips and animation blenders, while binary animset auditing already proved that Q-Force blend bytes are not boolean. Observed values include intermediate values such as 0x33, 0x66, 0x7F, 0x99, 0xB2 and 0xCC in addition to 0x00/0xFF.

Therefore a Q-Force/new-engine blend byte must remain a raw 0..255 value until the native blend operation is traced. ReLunacy must not collapse it to active/inactive.

The exact additive rotation/translation/scale composition law remains UNPROVED and `AnimationPlayer` continues to refuse partial/additive clips as absolute poses.

## Motion / phase channels

`IGPS3_PhaseChannel` and `IGPS3_MotionChannel` are native runtime concepts. However, the direct clip sampler proves that once a sample time reaches the clip, skeletal frame selection is `time * frameRate` plus normal integer/fraction interpolation.

Current interpretation:

- PROVED: clip-local skeletal sampling is constant-FPS.
- INFERENCE: PhaseChannel may alter/coordinate the time supplied to clips at a higher layer.
- INFERENCE: MotionChannel likely owns movement/root-motion-style data distinct from ordinary local skeletal translation.

Do not change base viewer timing to a guessed variable-duration sampler merely because PhaseChannel exists.

## ReLunacy changes derived from this EBOOT

The `animation-shared-engine` branch now:

1. computes new-engine physical skeletal frame stride as `align16(frameStride)` for packed clips and `align128(frameStride)` for non-packed clips;
2. permits the authored loop interpolation endpoint at logical index `numFrames` for skeletal track sampling;
3. reads `remap[current + 1]` for that endpoint rather than wrapping to `remap[0]`;
4. uses the same stored-endpoint concept for ToD, whose extra loop frame was independently proved from old-engine data;
5. retains `Time * FrameRate` as the common clip-local clock because Q-Force directly confirms it.

## Remaining Q-Force reverse targets

Before the animation subsystem can be called complete:

1. trace the exact partial/additive blend operation, including conversion/use of the per-bone blend byte;
2. trace DynamicJoint/AttachmentJointMap matrix composition far enough to reproduce attached/derived mesh transforms;
3. identify MotionChannel data layout and its relation to root/motion transforms;
4. identify PhaseChannel's higher-level time transformation and transition semantics;
5. determine which F000 +0x40..+0x5C fields feed those systems;
6. trace FC00/FD00/F991 consumers before assigning semantic names;
7. validate the fixes against real new-engine assets in the viewer, not only against static binary invariants and compilation.
