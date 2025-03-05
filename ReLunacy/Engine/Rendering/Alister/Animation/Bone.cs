using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReLunacy.Engine.Rendering.Alister.Animation;

public class Bone
{
    public string Name { get; set; }
    public Mat4 Transform { get; set; }
    public Mat4 Offset { get; set; }
    public Bone? Parent { get; set; }
    public List<Bone> Children { get; set; } = [];

    public Bone(string name, Mat4 transform, Mat4 offset, Bone? parent = null)
    {
        Name = name;
        Transform = transform;
        Offset = offset;
        Parent = parent;
    }

    public void Update(float deltaTime, Animation animation)
    {
        var keyframe = animation.GetKeyframe(Name, deltaTime);
        if (keyframe is null)
            return;
        Transform = keyframe.Value.Transform;
    }
}
