using System.Numerics;
using ReLunacy.Engine.Assets.Interfaces;

namespace ReLunacy.Engine.Assets.Animations;

/// <summary>
/// Engine-independent skeletal animation sampler. Storage-specific frame layout and track decoding
/// are hidden behind AnimationClip. Additive composition and root-transform application remain
/// deliberately unapplied until their native composition rules are confirmed.
/// </summary>
public sealed class AnimationPlayer
{
    private const ushort BoneFlagDontInheritScale = 0x0001;

    public AnimationClip? Clip { get; private set; }
    public bool IsPlaying { get; private set; }
    public bool Loop { get; set; }
    public float Speed { get; set; } = 1f;
    public float Time { get; private set; }
    public Matrix4x4[]? LastAnimatedWorld { get; private set; }
    public AnimationRootTransform? LastRootTransform { get; private set; }

    public bool CanSamplePose => Clip is { Additive: false };
    public string? UnsupportedReason => Clip is { Additive: true }
        ? "Partial/additive composition is still under native reverse; the clip is not treated as an absolute pose."
        : null;

    public int CurrentFrame => Clip is null || Clip.FrameRate <= 0f || Clip.NumFrames <= 0
        ? 0
        : Math.Clamp((int)MathF.Floor(Time * Clip.FrameRate + 1e-4f), 0, Clip.NumFrames - 1);

    public void SetClip(AnimationClip? clip, bool resetTime = true)
    {
        Clip = clip;
        if (resetTime) Time = 0f;
        IsPlaying = false;
        LastAnimatedWorld = null;
        LastRootTransform = null;
    }

    public void Play()
    {
        if (!CanSamplePose || Clip is null || Clip.FrameRate <= 0f || Clip.NumFrames <= 0) return;
        if (!Loop && Clip.DurationSeconds > 0f && Time >= Clip.DurationSeconds) Time = 0f;
        IsPlaying = true;
    }

    public void Pause() => IsPlaying = false;

    public void Stop()
    {
        IsPlaying = false;
        Time = 0f;
        LastAnimatedWorld = null;
        LastRootTransform = null;
    }

    public void SeekToFrame(int frame)
    {
        if (Clip is null || Clip.FrameRate <= 0f || Clip.NumFrames <= 0) { Time = 0f; return; }
        frame = Loop ? ((frame % Clip.NumFrames) + Clip.NumFrames) % Clip.NumFrames : Math.Clamp(frame, 0, Clip.NumFrames - 1);
        Time = frame / Clip.FrameRate;
    }

    public void Update(float deltaSeconds)
    {
        if (!IsPlaying || Clip is null || !CanSamplePose || Clip.FrameRate <= 0f) return;
        float duration = Clip.DurationSeconds;
        if (duration <= 0f) { IsPlaying = false; return; }

        Time += deltaSeconds * Speed;
        if (Loop)
        {
            Time %= duration;
            if (Time < 0f) Time += duration;
        }
        else if (Time >= duration)
        {
            Time = Math.Max(0f, (Clip.NumFrames - 1) / Clip.FrameRate);
            IsPlaying = false;
        }
        else if (Time < 0f)
        {
            Time = 0f;
            IsPlaying = false;
        }
    }

    private sealed class DecodedPose
    {
        public Quaternion[] Rotation { get; }
        public Vector3[] Scale { get; }
        public Vector3[] Translation { get; }

        public DecodedPose(int boneCount)
        {
            Rotation = new Quaternion[boneCount];
            Scale = new Vector3[boneCount];
            Translation = new Vector3[boneCount];
        }
    }

    private static DecodedPose DecodeFramePose(AnimationClip clip, ISkeleton skeleton, int frameIndex, AnimationControl control)
    {
        int boneCount = skeleton.Bones.Count;
        if (skeleton.ReferenceTranslations.Count != boneCount)
            throw new InvalidDataException($"Animation '{clip.Name}' needs {boneCount} reference translations, but the skeleton exposes {skeleton.ReferenceTranslations.Count}.");
        if (skeleton.ReferenceScales.Count != boneCount)
            throw new InvalidDataException($"Animation '{clip.Name}' needs {boneCount} reference scales, but the skeleton exposes {skeleton.ReferenceScales.Count}.");
        if (control.RefPoseRotations.Length != boneCount)
            throw new InvalidDataException($"Animation '{clip.Name}' control has {control.RefPoseRotations.Length} reference rotations for a {boneCount}-bone skeleton.");

        var pose = new DecodedPose(boneCount);
        var quantizedRotation = new short[boneCount * 4];

        for (int bone = 0; bone < boneCount; bone++)
        {
            short[] q = control.RefPoseRotations[bone];
            quantizedRotation[bone * 4 + 0] = q[0];
            quantizedRotation[bone * 4 + 1] = q[1];
            quantizedRotation[bone * 4 + 2] = q[2];
            quantizedRotation[bone * 4 + 3] = q[3];

            // The native quantized scratch pose starts scale at one, but that scratch record is not
            // the final local skeleton transform. Cross-engine bind/clip audits prove that authored
            // scale tracks are absolute local components and that missing components retain the
            // skeleton's reference local scale. Starting from unit scale was the main cause of bones
            // with un-authored scale channels stretching or collapsing in the preview.
            pose.Scale[bone] = skeleton.ReferenceScales[bone];
            pose.Translation[bone] = skeleton.ReferenceTranslations[bone];
        }

        void Apply(AnimationTrackMask mask, short value)
        {
            int bone = mask.BoneIndex;
            if ((uint)bone >= (uint)boneCount)
                throw new InvalidDataException($"Animation '{clip.Name}' routes outside the skeleton.");

            switch (mask.Kind)
            {
                case AnimationTrackKind.Rotation:
                    if (mask.Component >= 4) throw new InvalidDataException($"Animation '{clip.Name}' has an invalid rotation component.");
                    quantizedRotation[bone * 4 + mask.Component] = value;
                    break;
                case AnimationTrackKind.Scale:
                    if (mask.Component >= 3) throw new InvalidDataException($"Animation '{clip.Name}' has an invalid scale component.");
                    SetComponent(ref pose.Scale[bone], mask.Component, value * skeleton.ScaleScale);
                    break;
                case AnimationTrackKind.Position:
                    if (mask.Component >= 3) throw new InvalidDataException($"Animation '{clip.Name}' has an invalid translation component.");
                    SetComponent(ref pose.Translation[bone], mask.Component, value * skeleton.PositionScale);
                    break;
                default:
                    throw new InvalidDataException($"Animation '{clip.Name}' routes data into unsupported channel 3.");
            }
        }

        for (int i = 0; i < control.RefPoseValues.Length; i++)
            Apply(control.RefPoseMasks[i], control.RefPoseValues[i]);

        clip.ReadFrame(frameIndex, out short[] track16, out sbyte[] track8);
        if (track16.Length != control.Track16Masks.Length || track8.Length != control.Track8Masks.Length)
            throw new InvalidDataException($"Animation '{clip.Name}' frame/control track-count mismatch.");

        for (int i = 0; i < track16.Length; i++) Apply(control.Track16Masks[i], track16[i]);
        for (int i = 0; i < track8.Length; i++)
            Apply(control.Track8Masks[i], clip.DecodeTrack8(control.Track8BaseValues[i], track8[i]));

        for (int bone = 0; bone < boneCount; bone++) pose.Rotation[bone] = DequantizeQuaternion(quantizedRotation, bone);
        return pose;
    }

    public Matrix4x4[] SamplePose(ISkeleton skeleton)
    {
        int boneCount = skeleton.Bones.Count;
        var skin = new Matrix4x4[boneCount];

        if (Clip is null || !CanSamplePose || Clip.FrameRate <= 0f || Clip.NumFrames <= 0)
        {
            Array.Fill(skin, Matrix4x4.Identity);
            LastAnimatedWorld = null;
            LastRootTransform = null;
            return skin;
        }

        var control = Clip.GetControl(boneCount);
        float frameTime = Time * Clip.FrameRate;
        int frameA = Math.Clamp((int)MathF.Floor(frameTime), 0, Clip.NumFrames - 1);
        float fraction = Clip.NumFrames > 1 ? frameTime - MathF.Floor(frameTime) : 0f;

        // Both audited engines store/use an authored endpoint for looping interpolation. Do not
        // synthesize the seam by replacing current+1 with logical frame zero.
        int frameB = frameA + 1;
        if (frameB >= Clip.NumFrames && !Clip.Looping)
            frameB = Loop ? 0 : Clip.NumFrames - 1;

        var a = DecodeFramePose(Clip, skeleton, frameA, control);
        var b = frameA == frameB ? a : DecodeFramePose(Clip, skeleton, frameB, control);

        var localTranslation = new Vector3[boneCount];
        var localScale = new Vector3[boneCount];
        var localRotation = new Quaternion[boneCount];
        for (int bone = 0; bone < boneCount; bone++)
        {
            localTranslation[bone] = Vector3.Lerp(a.Translation[bone], b.Translation[bone], fraction);
            localScale[bone] = Vector3.Lerp(a.Scale[bone], b.Scale[bone], fraction);
            localRotation[bone] = Quaternion.Slerp(a.Rotation[bone], b.Rotation[bone], fraction);
            ValidateLocalTransform(Clip.Name, bone, localTranslation[bone], localScale[bone], localRotation[bone]);
        }

        var worldRT = new Matrix4x4[boneCount];
        var world = new Matrix4x4[boneCount];
        var effectiveScale = new Vector3[boneCount];
        var scaleChainOpen = new bool[boneCount];
        var computed = new bool[boneCount];
        var visiting = new bool[boneCount];

        Matrix4x4 ComputeWorld(int bone)
        {
            if (computed[bone]) return world[bone];
            if (visiting[bone]) throw new InvalidDataException($"Skeleton cycle while sampling '{Clip.Name}' at bone {bone}.");
            visiting[bone] = true;

            int parent = skeleton.Bones[bone].ParentIndex;
            bool hasParent = parent >= 0 && parent < boneCount && parent != bone;
            if (hasParent) ComputeWorld(parent);

            Vector3 inheritedScale = Vector3.One;
            if (!hasParent)
            {
                scaleChainOpen[bone] = true;
            }
            else
            {
                bool boneAllowsInheritance = (skeleton.Bones[bone].Flags & BoneFlagDontInheritScale) == 0;
                scaleChainOpen[bone] = scaleChainOpen[parent] && boneAllowsInheritance;
                if (scaleChainOpen[bone]) inheritedScale = effectiveScale[parent];
            }

            Vector3 translation = MultiplyComponents(localTranslation[bone], inheritedScale);
            effectiveScale[bone] = MultiplyComponents(localScale[bone], inheritedScale);

            if (!IsFinite(translation) || !IsFinite(effectiveScale[bone]))
                throw new InvalidDataException($"Animation '{Clip.Name}' produced non-finite propagated scale/translation on bone {bone}.");
            if (MathF.Abs(effectiveScale[bone].X) > 128f || MathF.Abs(effectiveScale[bone].Y) > 128f || MathF.Abs(effectiveScale[bone].Z) > 128f)
                throw new InvalidDataException($"Animation '{Clip.Name}' produced implausible propagated scale {effectiveScale[bone]} on bone {bone}.");

            Matrix4x4 localRT = Matrix4x4.CreateFromQuaternion(localRotation[bone])
                                * Matrix4x4.CreateTranslation(translation);
            worldRT[bone] = hasParent ? localRT * worldRT[parent] : localRT;
            world[bone] = Matrix4x4.CreateScale(effectiveScale[bone]) * worldRT[bone];

            visiting[bone] = false;
            computed[bone] = true;
            return world[bone];
        }

        for (int bone = 0; bone < boneCount; bone++)
        {
            Matrix4x4 animatedWorld = ComputeWorld(bone);
            skin[bone] = skeleton.Bones[bone].InverseBindPose * animatedWorld;
            if (!IsFinite(skin[bone]))
                throw new InvalidDataException($"Animation '{Clip.Name}' produced a non-finite skin matrix on bone {bone}.");
        }

        LastAnimatedWorld = world;
        LastRootTransform = Clip.TryReadRootTransform(frameA, out var root) ? root : null;
        return skin;
    }

    private static Vector3 MultiplyComponents(Vector3 a, Vector3 b) =>
        new(a.X * b.X, a.Y * b.Y, a.Z * b.Z);

    private static void ValidateLocalTransform(string clipName, int bone, Vector3 translation, Vector3 scale, Quaternion rotation)
    {
        if (!IsFinite(translation) || !IsFinite(scale) || !IsFinite(rotation))
            throw new InvalidDataException($"Animation '{clipName}' produced a non-finite transform on bone {bone}.");
        if (MathF.Abs(scale.X) > 128f || MathF.Abs(scale.Y) > 128f || MathF.Abs(scale.Z) > 128f)
            throw new InvalidDataException($"Animation '{clipName}' produced implausible scale {scale} on bone {bone}.");
    }

    private static void SetComponent(ref Vector3 value, int component, float x)
    {
        switch (component)
        {
            case 0: value.X = x; break;
            case 1: value.Y = x; break;
            case 2: value.Z = x; break;
            default: throw new ArgumentOutOfRangeException(nameof(component));
        }
    }

    private static Quaternion DequantizeQuaternion(short[] raw, int bone)
    {
        const float inverse = 1f / 32767f;
        var q = new Quaternion(raw[bone * 4] * inverse, raw[bone * 4 + 1] * inverse, raw[bone * 4 + 2] * inverse, raw[bone * 4 + 3] * inverse);
        float lengthSquared = q.LengthSquared();
        return lengthSquared > 1e-12f ? Quaternion.Multiply(q, 1f / MathF.Sqrt(lengthSquared)) : Quaternion.Identity;
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static bool IsFinite(Vector3 value) => IsFinite(value.X) && IsFinite(value.Y) && IsFinite(value.Z);
    private static bool IsFinite(Quaternion value) => IsFinite(value.X) && IsFinite(value.Y) && IsFinite(value.Z) && IsFinite(value.W);
    private static bool IsFinite(Matrix4x4 value) =>
        IsFinite(value.M11) && IsFinite(value.M12) && IsFinite(value.M13) && IsFinite(value.M14) &&
        IsFinite(value.M21) && IsFinite(value.M22) && IsFinite(value.M23) && IsFinite(value.M24) &&
        IsFinite(value.M31) && IsFinite(value.M32) && IsFinite(value.M33) && IsFinite(value.M34) &&
        IsFinite(value.M41) && IsFinite(value.M42) && IsFinite(value.M43) && IsFinite(value.M44);
}
