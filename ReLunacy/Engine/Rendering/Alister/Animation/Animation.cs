using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReLunacy.Engine.Rendering.Alister.Animation;

public class Animation
{
    public string Name { get; set; }
    public float Duration { get; set; }
    public Dictionary<string, List<Keyframe>> Keyframes { get; set; } = [];

    public Animation(string name, float duration)
    {
        Name = name;
        Duration = duration;
    }

    public Keyframe GetKeyframe(string boneName, float time)
    {
        if (!Keyframes.ContainsKey(boneName)) return null;

        var keyframes = Keyframes[boneName];
        return keyframes.FirstOrDefault(k => k.Time == time);
    }
}
