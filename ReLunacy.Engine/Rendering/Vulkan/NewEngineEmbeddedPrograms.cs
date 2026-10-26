namespace ReLunacy.Engine.Rendering.Vulkan;

/// <summary>
/// A class-0x13 material's own fragment program, when the lit shader knows its equation. Pushed as
/// uMat1.x; 0 keeps the generic standard-class path.
/// </summary>
public enum EmbeddedProgram : byte
{
    Generic = 0,

    /// <summary>
    /// Family be10f7bae86f (Tools/Reverse/evidence/newengine_fp_dataflow_qforce/be10f7bae86f.txt): the
    /// translucent veil of jungle_ruins (pre/post/boss/pvp) and plasma_harvester_pre. Read off the retail
    /// program, its output is
    ///   rgb = (tex2.z + 0.25) * 4, then the fog mix
    ///   a   = saturate(pow(saturate(N.V), 5) * T0.z * mix(T0.x, T0'.y, cos(t / 2) / 2 + 1/2))
    ///         * saturate((depth - 15) / 15)
    /// with T0 = tex0(uv + (0.015, 0.01) t), T0' = tex0(uv + (0.01, 0) t), t the time constant (K34.x),
    /// N the normal-mapped normal. Albedo is a set of masks, not a colour: drawn as the generic class
    /// it was a solid white-blue shape. PROVEN for the equation (the listing); the time unit (seconds)
    /// is STRONG - K34.x is 3158-3161 in a capture taken about 53 minutes into a session.
    /// </summary>
    ScrollingVeil = 1,
}

public static class NewEngineEmbeddedPrograms
{
    private static readonly Dictionary<string, EmbeddedProgram> Families = new(StringComparer.Ordinal)
    {
        ["be10f7bae86f"] = EmbeddedProgram.ScrollingVeil,
    };

    /// <summary>The decoded family among a material's embedded programs (Material.NewEngineProgramKeys).</summary>
    public static EmbeddedProgram Classify(IReadOnlyList<string> programKeys)
    {
        foreach (string key in programKeys)
            if (Families.TryGetValue(key, out EmbeddedProgram program))
                return program;
        return EmbeddedProgram.Generic;
    }
}
