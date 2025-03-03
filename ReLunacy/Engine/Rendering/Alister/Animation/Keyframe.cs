using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReLunacy.Engine.Rendering.Alister.Animation;

public record struct Keyframe
{
    public float Time { get; set; }
    public Mat4 Transform { get; set; }

    public Keyframe(Mat4 transform)
    {
        Transform = transform;
    }
}
